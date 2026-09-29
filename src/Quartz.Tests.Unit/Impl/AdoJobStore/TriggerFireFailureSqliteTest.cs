#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

#nullable enable

using System.Data.Common;
using System.Globalization;

using FakeItEasy;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// One trigger's fire failing on the database undoes that fire alone; the rest of the batch commits as
/// it is reported (#3931).
/// </summary>
/// <remarks>
/// <para>
/// <c>ApplyTriggerFired</c> writes several rows — the fired row, the <c>BLOCKED</c> state of a
/// <see cref="DisallowConcurrentExecutionAttribute" /> job's other triggers, the trigger row — and a
/// statement that failed partway left the writes before it in the transaction, which the batch then
/// committed beside a <c>Failed</c> result: the job's other triggers <c>BLOCKED</c> with nothing
/// executing to let go of them. Now the attempt is rolled back whole and the batch fired again without
/// the failed trigger.
/// </para>
/// <para>
/// A trigger whose every fire fails is stored <c>ERROR</c> after
/// <see cref="AdoJobStoreOptions.MaxConsecutiveFireFailures" /> failures in a row, rather than released and
/// acquired again ahead of its job-mates forever (#3963).
/// </para>
/// <para>
/// One store on a SQLite file, driven by hand, with the shipped SQLite delegate failing the fire it is
/// told to after the fire's own writes have gone out. The same on every clustered engine is
/// <c>TriggerFireFailureTestBase</c> in the integration suite.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class TriggerFireFailureSqliteTest
{
    private const string Group = "fire-failure";
    private static readonly JobKey serialJobKey = new("serial", Group);
    private static readonly JobKey ordinaryJobKey = new("ordinary", Group);
    private static readonly JobKey brokenJobKey = new("broken", Group);

    /// <summary>
    /// <c>AdoJobStoreOptions.MaxConsecutiveFireFailures</c> as it ships: the failures in a row after which a
    /// trigger is stored <c>ERROR</c>.
    /// </summary>
    private const int DefaultMaxConsecutiveFireFailures = 5;

    /// <summary>Log event <c>AdoJobStoreLog.FailingTriggerParkedInError</c>.</summary>
    private const int FailingTriggerParkedInError = 3050;

    private SqliteTestDatabase database = null!;
    private RecordingLoggerProvider logs = null!;
    private ServiceProvider node = null!;
    private IScheduler scheduler = null!;
    private IJobStore store = null!;
    private DateTimeOffset due;

    [SetUp]
    public async Task CreateNode()
    {
        database = new SqliteTestDatabase("trigger-fire-failure");
        FaultingSqliteDelegate.Reset();
        logs = new RecordingLoggerProvider();

        await BuildNode(configureStore: null);

        // Ahead of now by a known margin, so the misfire cutoff stays out of the acquisition read and
        // the fire-time order is the one each test schedules.
        due = TimeProvider.System.GetUtcNow().AddSeconds(30);
    }

    [TearDown]
    public async Task DisposeNode()
    {
        await node.DisposeAsync();
        logs.Dispose();
        database.Dispose();
    }

    /// <summary>
    /// Builds the node over the fixture's database; a test that needs other store options disposes the
    /// one the set-up built and builds its own over the same file.
    /// </summary>
    private async Task BuildNode(Action<AdoJobStoreOptions>? configureStore)
    {
        ServiceCollection services = new();
        services.AddLogging(logging => logging.AddProvider(logs));
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "trigger-fire-failure";
                options.InstanceId = "node";

                // Batched, so a running scheduler fires a failing trigger beside the rest of its batch.
                options.MaxBatchSize = 4;
            });

            q.UsePersistentStore(persistent =>
            {
                // Before UseSqlite, which registers the delegate it names: the registrations are
                // try-add, so the first one in wins and this subclass would otherwise never be built.
                persistent.UseDriverDelegate<FaultingSqliteDelegate>();
                persistent.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                persistent.ProvisionSchema();
                persistent.ConfigureStore(options =>
                {
                    // A transient failure is retried at once rather than a second later.
                    options.TransientRetryInterval = TimeSpan.Zero;
                    configureStore?.Invoke(options);
                });
            });
        });

        node = services.BuildServiceProvider();
        scheduler = await node.GetRequiredService<ISchedulerFactory>().GetScheduler();
        store = node.GetRequiredService<IJobStore>();
    }

    /// <summary>
    /// A batch of three whose middle fire fails: the two beside it commit as fired, the failed fire's
    /// writes are gone, and the failed trigger's job-mate is not left <c>BLOCKED</c>.
    /// </summary>
    [Test]
    public async Task AFailedFireIsRolledBackAndTheRestOfTheBatchCommitsAsReported()
    {
        await AddJobs();

        // Fire-time order: ordinary-1, poison, ordinary-2, sibling. The sibling is the poison trigger's
        // job-mate: what the poison fire moves to BLOCKED, and must not leave there.
        await Schedule("ordinary-1", ordinaryJobKey, due);
        await Schedule("poison", serialJobKey, due.AddMilliseconds(1));
        await Schedule("ordinary-2", ordinaryJobKey, due.AddMilliseconds(2));
        await Schedule("sibling", serialJobKey, due.AddMilliseconds(3));

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(RequestFor(maxCount: 4));
        acquired.Select(x => x.Key.Name).Should().Equal(["ordinary-1", "poison", "ordinary-2"],
            "a batch takes one trigger of a serial job, so the sibling stays behind");

        FaultingSqliteDelegate.FailFireOf = "poison";

        List<TriggerFiredResult> results = await store.TriggersFired(acquired);

        results.Should().HaveCount(3, "one answer per trigger, in the order asked");
        results[0].TriggerFiredBundle.Should().NotBeNull("ordinary-1 fired before the failure");
        results[1].TriggerFiredBundle.Should().BeNull("the poison fire failed");
        results[1].IsDeclined.Should().BeFalse();
        results[1].Exception.Should().BeOfType<JobPersistenceException>()
            .Which.InnerException.Should().BeAssignableTo<DbException>(
                "the cause is the driver's own exception, which is what the scheduler thread releases the trigger on");
        results[2].TriggerFiredBundle.Should().NotBeNull("ordinary-2 fired after the failure, in the attempt that ran without the poison");

        (await TriggerState("sibling")).Should().Be("WAITING",
            "the poison fire's BLOCKED of its job-mates went with the fire; left BLOCKED, nothing executing would ever let go of it");
        (await TriggerState("poison")).Should().Be("ACQUIRED", "the reservation is the scheduler's to release, not the store's");
        (await FiredState("poison")).Should().Be("ACQUIRED", "its fired row is the reservation as acquisition wrote it, the fire's update undone");
        (await FiredState("ordinary-1")).Should().Be("EXECUTING", "a result reported fired is a fire that committed");
        (await FiredState("ordinary-2")).Should().Be("EXECUTING");
        FaultingSqliteDelegate.FireAttempts.Should().Equal(["ordinary-1", "poison", "ordinary-1", "ordinary-2"],
            "the attempt that met the failure is rolled back whole, and the batch is fired again without the failed trigger");

        // What the scheduler thread does with a failed result.
        await store.ReleaseAcquiredTrigger(acquired[1]);

        (await TriggerState("poison")).Should().Be("WAITING", "released, for the next acquisition to pick up");
        (await FiredRowCount("poison")).Should().Be(0);
        (await FiredState("ordinary-1")).Should().Be("EXECUTING", "the release lets go of the reservation it was asked to, and nothing else");
    }

    /// <summary>
    /// A batch of one is the same story with nothing beside it: the fire's writes are gone and the
    /// reservation is all that is left.
    /// </summary>
    [Test]
    public async Task ABatchOfOneThatFailsLeavesItsReservationAndNothingElse()
    {
        await AddJobs();
        await Schedule("poison", serialJobKey, due);
        await Schedule("sibling", serialJobKey, due.AddMilliseconds(1));

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(RequestFor(maxCount: 1));
        FaultingSqliteDelegate.FailFireOf = "poison";

        List<TriggerFiredResult> results = await store.TriggersFired(acquired);

        results.Should().ContainSingle().Which.Exception.Should().NotBeNull();
        (await TriggerState("sibling")).Should().Be("WAITING");
        (await TriggerState("poison")).Should().Be("ACQUIRED");
        (await FiredState("poison")).Should().Be("ACQUIRED");
        FaultingSqliteDelegate.FireAttempts.Should().Equal(["poison"], "with nothing else in the batch there is nothing to fire again");
    }

    /// <summary>
    /// Every fire failing is every trigger answered <c>Failed</c>, in order, in as many attempts as
    /// there are triggers.
    /// </summary>
    [Test]
    public async Task EveryFireFailingAnswersFailedForEachInOrder()
    {
        await AddJobs();
        await Schedule("poison-1", serialJobKey, due);
        await Schedule("poison-2", ordinaryJobKey, due.AddMilliseconds(1));

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(RequestFor(maxCount: 2));
        acquired.Should().HaveCount(2);
        FaultingSqliteDelegate.FailFireOf = "*";

        List<TriggerFiredResult> results = await store.TriggersFired(acquired);

        results.Should().HaveCount(2).And.OnlyContain(x => x.Exception != null && x.TriggerFiredBundle == null);
        FaultingSqliteDelegate.FireAttempts.Should().Equal(["poison-1", "poison-2"],
            "the first attempt fails at its first trigger, the second at what was left");
        (await TriggerState("poison-1")).Should().Be("ACQUIRED");
        (await TriggerState("poison-2")).Should().Be("ACQUIRED");
    }

    /// <summary>
    /// A failure the store settles inside the transaction is not one it rolls back. A job that will not
    /// load has its trigger stored <c>ERROR</c>, so that it is not acquired again, and the batch commits
    /// that beside the fires around it.
    /// </summary>
    [Test]
    public async Task AJobThatWillNotLoadIsStoredErrorAndTheBatchCommitsThat()
    {
        await AddJobs();
        await Schedule("ordinary-1", ordinaryJobKey, due);
        await Schedule("broken-1", brokenJobKey, due.AddMilliseconds(1));

        ISchedulerListener listener = A.Fake<ISchedulerListener>();
        A.CallTo(() => listener.Name).Returns("error-listener");
        scheduler.ListenerManager.AddSchedulerListener(listener);

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(RequestFor(maxCount: 2));
        acquired.Should().HaveCount(2, "the job loads at acquisition; it stops loading afterwards");

        // The job stops loading between the acquisition and the fire: its data map no longer
        // deserializes, as a serializer change underneath stored rows would make it. (Its type name is
        // resolved lazily, so a type that is gone is not what fails a fire.)
        await ExecuteNonQuery("UPDATE QRTZ_JOB_DETAILS SET JOB_DATA = X'00FF' WHERE JOB_NAME = @name", "broken");

        List<TriggerFiredResult> results = await store.TriggersFired(acquired);

        results.Should().HaveCount(2);
        results[0].TriggerFiredBundle.Should().NotBeNull();
        results[1].TriggerFiredBundle.Should().BeNull();
        results[1].Exception.Should().BeOfType<JobPersistenceException>()
            .Which.InnerException.Should().NotBeAssignableTo<DbException>("the database refused nothing; the job's stored data is what failed");

        (await TriggerState("broken-1")).Should().Be("ERROR", "the store settled the trigger, and the batch committed it");
        (await FiredState("ordinary-1")).Should().Be("EXECUTING");
        FaultingSqliteDelegate.FireAttempts.Should().Equal(["ordinary-1"],
            "nothing was rolled back, so nothing was fired twice; the broken trigger never reached the write");
        A.CallTo(() => listener.TriggerInError(A<IScheduler>._, new TriggerKey("broken-1", Group), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        // The scheduler thread releases a failed trigger; an ERROR row is not a reservation and stays.
        await store.ReleaseAcquiredTrigger(acquired[1]);
        (await TriggerState("broken-1")).Should().Be("ERROR");
    }

    /// <summary>
    /// The transient failure keeps its answer: the whole attempt is retried, and everything in it fires.
    /// </summary>
    [Test]
    public async Task ATransientFailureRetriesTheWholeBatch()
    {
        await AddJobs();
        await Schedule("ordinary-1", ordinaryJobKey, due);
        await Schedule("ordinary-2", ordinaryJobKey, due.AddMilliseconds(1));

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(RequestFor(maxCount: 2));
        FaultingSqliteDelegate.FailFireOfTransientlyOnce = "ordinary-2";

        List<TriggerFiredResult> results = await store.TriggersFired(acquired);

        results.Should().HaveCount(2).And.OnlyContain(x => x.TriggerFiredBundle != null);
        FaultingSqliteDelegate.FireAttempts.Should().Equal(["ordinary-1", "ordinary-2", "ordinary-1", "ordinary-2"],
            "a transient failure is the transaction wrapper's to retry, and it retries the attempt whole");
        (await FiredState("ordinary-1")).Should().Be("EXECUTING");
        (await FiredState("ordinary-2")).Should().Be("EXECUTING");
    }

    /// <summary>
    /// A running scheduler with a trigger whose every fire fails, first in the fire-time order every
    /// round: it is stored <c>ERROR</c> after the fires in a row the store allows, and its
    /// <c>[DisallowConcurrentExecution]</c> job-mate fires (#3963).
    /// </summary>
    /// <remarks>
    /// A batch takes one trigger of a serial job, and a failed fire leaves the trigger's fire time where
    /// it was, so it is first again next round. Released and acquired again without end, it kept its
    /// job-mate out of every batch for good.
    /// </remarks>
    [Test]
    public async Task ATriggerWhoseEveryFireFailsIsStoredErrorAndItsSerialJobMateFires()
    {
        await AddJobs();
        FiringSignal sibling = new("sibling");
        scheduler.ListenerManager.AddJobListener(sibling);

        // Due already, in this order: the poison leads every round it is WAITING in.
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        await Schedule("poison", serialJobKey, now);
        await Schedule("ordinary-1", ordinaryJobKey, now.AddMilliseconds(5));
        await Schedule("sibling", serialJobKey, now.AddMilliseconds(10));

        FaultingSqliteDelegate.FailFireOf = "poison";

        await scheduler.Start();
        try
        {
            Task finished = await Task.WhenAny(sibling.Fired.Task, Task.Delay(TimeSpan.FromSeconds(15)));
            finished.Should().BeSameAs(sibling.Fired.Task,
                "the poison trigger's job-mate is one trigger of a serial job behind it in the order; it fires only once the poison stops being acquired");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        (await TriggerState("poison")).Should().Be("ERROR", "its every fire failed, as many times in a row as the store allows");
        FaultingSqliteDelegate.FireAttempts.Count(x => x == "poison").Should().Be(DefaultMaxConsecutiveFireFailures,
            "stored ERROR on the last failure it allows, it is not acquired again");
    }

    /// <summary>
    /// Driven by hand, as the scheduler thread drives it: one failure short of the limit leaves the
    /// trigger <c>WAITING</c> for the next round, and the failure that reaches it stores the trigger
    /// <c>ERROR</c>, tells the scheduler listeners and logs it, once each.
    /// </summary>
    [Test]
    public async Task TheFailureThatReachesTheLimitStoresTheTriggerErrorAndSaysSoOnce()
    {
        await AddJobs();
        await Schedule("poison", serialJobKey, due);
        await Schedule("sibling", serialJobKey, due.AddMilliseconds(10));
        ISchedulerListener listener = ErrorListener();

        FaultingSqliteDelegate.FailFireOf = "poison";

        for (int failure = 1; failure < DefaultMaxConsecutiveFireFailures; failure++)
        {
            (await FireOnce("poison")).Exception.Should().NotBeNull();
            (await TriggerState("poison")).Should().Be("WAITING", $"{failure} failure(s) in a row is short of the limit");
        }

        A.CallTo(() => listener.TriggerInError(A<IScheduler>._, A<TriggerKey>._, A<CancellationToken>._)).MustNotHaveHappened();
        logs.Entries.Should().NotContain(x => x.EventId.Id == FailingTriggerParkedInError);

        TriggerFiredResult last = await FireOnce("poison");

        last.TriggerFiredBundle.Should().BeNull();
        last.Exception.Should().NotBeNull("the result is the failed fire's, whatever the store did about it afterwards");
        (await TriggerState("poison")).Should().Be("ERROR",
            "the limit's failure stores the trigger ERROR, and the scheduler thread's release of the failed result leaves an ERROR row alone");
        (await FiredRowCount("poison")).Should().Be(0, "the release still lets go of the reservation");
        (await TriggerState("sibling")).Should().Be("WAITING");
        A.CallTo(() => listener.TriggerInError(A<IScheduler>._, new TriggerKey("poison", Group), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        LogEntry parked = logs.Entries.Should().ContainSingle(x => x.EventId.Id == FailingTriggerParkedInError).Subject;
        parked.Level.Should().Be(LogLevel.Error);
        parked.Message.Should().Contain("poison").And.Contain(DefaultMaxConsecutiveFireFailures.ToString(CultureInfo.InvariantCulture));

        List<IOperableTrigger> next = await store.AcquireNextTriggers(RequestFor(maxCount: 2));
        next.Select(x => x.Key.Name).Should().Equal(["sibling"], "the job-mate the poison kept out of every batch is first now");
    }

    /// <summary>
    /// The limit counts failures in a row: a fire that succeeds starts the count again.
    /// </summary>
    [Test]
    public async Task AFireThatSucceedsStartsTheCountAgain()
    {
        await AddJobs();
        await ScheduleRepeating("flaky", ordinaryJobKey, due);

        FaultingSqliteDelegate.FailFireOf = "flaky";
        await FailInARow("flaky", DefaultMaxConsecutiveFireFailures - 1);

        FaultingSqliteDelegate.FailFireOf = null;
        TriggerFiredResult success = await FireOnce("flaky");
        success.TriggerFiredBundle.Should().NotBeNull();
        await store.TriggeredJobComplete(success.TriggerFiredBundle!.Trigger, success.TriggerFiredBundle.JobDetail, SchedulerInstruction.NoInstruction);

        FaultingSqliteDelegate.FailFireOf = "flaky";
        await FailInARow("flaky", DefaultMaxConsecutiveFireFailures - 1);
        (await TriggerState("flaky")).Should().Be("WAITING",
            "the failures either side of the success are one short of the limit each, and the success started the count again");

        await FireOnce("flaky");
        (await TriggerState("flaky")).Should().Be("ERROR", "the count went on from where the success left it");
    }

    /// <summary>
    /// A fire another node committed since the last failure starts the count again as well. The node
    /// counting never saw it; the trigger's previous fire time, which only a committed fire moves, says so.
    /// </summary>
    [Test]
    public async Task AFireCommittedElsewhereStartsTheCountAgain()
    {
        await AddJobs();
        await ScheduleRepeating("flaky", ordinaryJobKey, due);

        FaultingSqliteDelegate.FailFireOf = "flaky";
        await FailInARow("flaky", DefaultMaxConsecutiveFireFailures - 1);

        // What another node's successful fire leaves behind on the row this node reads.
        await SetPreviousFireTime("flaky", due);

        await FailInARow("flaky", DefaultMaxConsecutiveFireFailures - 1);
        (await TriggerState("flaky")).Should().Be("WAITING", "the fire in between was not a failure, wherever it ran");

        await FireOnce("flaky");
        (await TriggerState("flaky")).Should().Be("ERROR");
    }

    /// <summary>
    /// Zero is 4.3's behaviour: a trigger whose every fire fails is released and acquired again however
    /// many times it fails.
    /// </summary>
    [Test]
    public async Task ALimitOfZeroNeverStoresAFailingTriggerError()
    {
        await node.DisposeAsync();
        await BuildNode(options => options.MaxConsecutiveFireFailures = 0);
        await AddJobs();
        await Schedule("poison", serialJobKey, due);
        ISchedulerListener listener = ErrorListener();

        FaultingSqliteDelegate.FailFireOf = "poison";
        await FailInARow("poison", 2 * DefaultMaxConsecutiveFireFailures);

        (await TriggerState("poison")).Should().Be("WAITING", "zero turns the limit off");
        A.CallTo(() => listener.TriggerInError(A<IScheduler>._, A<TriggerKey>._, A<CancellationToken>._)).MustNotHaveHappened();
        logs.Entries.Should().NotContain(x => x.EventId.Id == FailingTriggerParkedInError);
    }

    /// <summary>
    /// A lower limit parks sooner, which is the option reaching the store.
    /// </summary>
    [Test]
    public async Task ALimitOfOneParksOnTheFirstFailure()
    {
        await node.DisposeAsync();
        await BuildNode(options => options.MaxConsecutiveFireFailures = 1);
        await AddJobs();
        await Schedule("poison", serialJobKey, due);

        FaultingSqliteDelegate.FailFireOf = "poison";
        await FireOnce("poison");

        (await TriggerState("poison")).Should().Be("ERROR");
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == FailingTriggerParkedInError);
    }

    /// <summary>
    /// A failure that throws the whole batch out of <c>TriggersFired</c> is the database's, not the
    /// trigger's, and is not counted against it: a transient failure the store has run out of retries for.
    /// </summary>
    [Test]
    public async Task AFailureOfTheWholeBatchIsNotCountedAgainstItsTriggers()
    {
        await node.DisposeAsync();
        await BuildNode(options => options.MaxTransientRetries = 0);
        await AddJobs();
        await Schedule("busy", ordinaryJobKey, due);

        for (int round = 0; round < 2 * DefaultMaxConsecutiveFireFailures; round++)
        {
            List<IOperableTrigger> acquired = await store.AcquireNextTriggers(RequestFor(maxCount: 1));
            FaultingSqliteDelegate.FailFireOfTransientlyOnce = "busy";

            Func<Task> fire = async () => await store.TriggersFired(acquired);
            await fire.Should().ThrowAsync<JobPersistenceException>("with no retries left, a transient failure fails the batch");

            // What the scheduler thread does with a batch that failed whole.
            await store.ReleaseAcquiredTrigger(acquired[0]);
        }

        (await TriggerState("busy")).Should().Be("WAITING");
        logs.Entries.Should().NotContain(x => x.EventId.Id == FailingTriggerParkedInError);
    }

    /// <summary>
    /// A park that fails is logged, and the batch's results are returned all the same: the fires beside
    /// the failed one committed, and the scheduler has jobs to run. The count is kept, so the next
    /// failure parks the trigger.
    /// </summary>
    [Test]
    public async Task AParkThatFailsIsLoggedAndTheNextFailureParks()
    {
        await AddJobs();
        await Schedule("poison", serialJobKey, due);
        await Schedule("ordinary-1", ordinaryJobKey, due.AddMilliseconds(1));

        FaultingSqliteDelegate.FailFireOf = "poison";
        await FailInARow("poison", DefaultMaxConsecutiveFireFailures - 1);

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(RequestFor(maxCount: 2));
        acquired.Select(x => x.Key.Name).Should().Equal(["poison", "ordinary-1"]);
        FaultingSqliteDelegate.FailParkOf = "poison";

        List<TriggerFiredResult> results = await store.TriggersFired(acquired);

        results.Should().HaveCount(2);
        results[0].Exception.Should().NotBeNull();
        results[1].TriggerFiredBundle.Should().NotBeNull("the fire beside the failed one committed, and its job is the scheduler's to run");
        logs.Entries.Should().NotContain(x => x.EventId.Id == FailingTriggerParkedInError);
        logs.Entries.Should().Contain(x => x.EventId.Id == 3027 && x.Level == LogLevel.Error, "the store could not store the trigger ERROR, and says so");
        (await TriggerState("poison")).Should().Be("ACQUIRED", "the park rolled back, and the reservation is the scheduler's to release");

        await store.ReleaseAcquiredTrigger(acquired[0]);
        (await TriggerState("poison")).Should().Be("WAITING");

        FaultingSqliteDelegate.FailParkOf = null;
        await FireOnce("poison");
        (await TriggerState("poison")).Should().Be("ERROR", "the count was kept, so the next failure is past the limit");
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == FailingTriggerParkedInError);
    }

    /// <summary>
    /// <c>ResetTriggerFromErrorState</c> brings a parked trigger back, with its count started again, and
    /// it fires once whatever failed it is fixed.
    /// </summary>
    [Test]
    public async Task AParkedTriggerIsResetFromErrorAndFiresOnceTheFaultIsGone()
    {
        await AddJobs();
        await Schedule("poison", serialJobKey, due);
        TriggerKey poison = new("poison", Group);

        FaultingSqliteDelegate.FailFireOf = "poison";
        await FailInARow("poison", DefaultMaxConsecutiveFireFailures);
        (await scheduler.GetTriggerState(poison)).Should().Be(Quartz.TriggerState.Error);

        (await scheduler.ResetTriggerFromErrorState(poison)).Should().BeTrue();
        (await TriggerState("poison")).Should().Be("WAITING");

        await FireOnce("poison");
        (await TriggerState("poison")).Should().Be("WAITING", "parking cleared the count, so one failure after the reset is one, not one more than the limit");

        FaultingSqliteDelegate.FailFireOf = null;
        TriggerFiredResult fired = await FireOnce("poison");

        fired.TriggerFiredBundle.Should().NotBeNull("the cause is fixed");
        (await FiredState("poison")).Should().Be("EXECUTING");
    }

    /// <summary>
    /// Fires the one trigger due next, as the scheduler thread does: acquired, fired, and released if the
    /// fire did not produce a job to run.
    /// </summary>
    private async Task<TriggerFiredResult> FireOnce(string expected)
    {
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(RequestFor(maxCount: 1));
        acquired.Should().ContainSingle().Which.Key.Name.Should().Be(expected);

        List<TriggerFiredResult> results = await store.TriggersFired(acquired);
        TriggerFiredResult result = results.Should().ContainSingle().Subject;
        if (result.TriggerFiredBundle is null)
        {
            await store.ReleaseAcquiredTrigger(acquired[0]);
        }

        return result;
    }

    private async Task FailInARow(string triggerName, int failures)
    {
        for (int failure = 0; failure < failures; failure++)
        {
            (await FireOnce(triggerName)).Exception.Should().NotBeNull("the fault is on");
        }
    }

    private ISchedulerListener ErrorListener()
    {
        ISchedulerListener listener = A.Fake<ISchedulerListener>();
        A.CallTo(() => listener.Name).Returns("error-listener");
        scheduler.ListenerManager.AddSchedulerListener(listener);
        return listener;
    }

    private static TriggerAcquisitionRequest RequestFor(int maxCount)
    {
        return new TriggerAcquisitionRequest
        {
            NoLaterThan = TimeProvider.System.GetUtcNow().AddMinutes(5),
            MaxCount = maxCount,
            // Wide enough that triggers due milliseconds apart make one batch; a batch ends at the first
            // trigger's fire time plus this.
            TimeWindow = TimeSpan.FromSeconds(5),
        };
    }

    private async Task AddJobs()
    {
        await scheduler.AddJob(JobBuilder.Create<SerialJob>().WithIdentity(serialJobKey).StoreDurably().Build());
        await scheduler.AddJob(JobBuilder.Create<OrdinaryJob>().WithIdentity(ordinaryJobKey).StoreDurably().Build());
        await scheduler.AddJob(JobBuilder.Create<OrdinaryJob>().WithIdentity(brokenJobKey).StoreDurably().Build());
    }

    private async Task Schedule(string name, JobKey job, DateTimeOffset at)
    {
        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(at)
            .Build());
    }

    private Task<string?> TriggerState(string triggerName)
    {
        return ReadScalar("SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name", triggerName);
    }

    private Task<string?> FiredState(string triggerName)
    {
        return ReadScalar("SELECT STATE FROM QRTZ_FIRED_TRIGGERS WHERE TRIGGER_NAME = @name", triggerName);
    }

    private async Task<long> FiredRowCount(string triggerName)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE TRIGGER_NAME = @name";
        command.Parameters.AddWithValue("@name", triggerName);
        return (long) (await command.ExecuteScalarAsync())!;
    }

    private async Task<string?> ReadScalar(string sql, string name)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@name", name);
        return (string?) await command.ExecuteScalarAsync();
    }

    private async Task ExecuteNonQuery(string sql, string name)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@name", name);
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
    }

    private async Task SetPreviousFireTime(string triggerName, DateTimeOffset previousFireTimeUtc)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE QRTZ_TRIGGERS SET PREV_FIRE_TIME = @previous WHERE TRIGGER_NAME = @name";
        command.Parameters.AddWithValue("@previous", previousFireTimeUtc.UtcTicks);
        command.Parameters.AddWithValue("@name", triggerName);
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
    }

    private async Task ScheduleRepeating(string name, JobKey job, DateTimeOffset at)
    {
        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(at)
            .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromSeconds(1)).RepeatForever())
            .Build());
    }

    /// <summary>
    /// The shipped SQLite delegate, failing the fire it is told to after the fire's own writes have gone
    /// out — the shape of a constraint violation or a schema drift on the last statement of a fire.
    /// </summary>
    /// <remarks>
    /// Static, because the container builds the delegate; the fixture zeroes it before each test.
    /// </remarks>
    public sealed class FaultingSqliteDelegate : SQLiteDelegate
    {
        private static readonly List<string> fireAttempts = [];

        /// <summary>The trigger whose fire fails, <c>*</c> for every one, or <see langword="null" /> for none.</summary>
        public static string? FailFireOf { get; set; }

        /// <summary>The trigger whose next fire fails as a busy database would make it fail, once.</summary>
        public static string? FailFireOfTransientlyOnce { get; set; }

        /// <summary>The trigger the store cannot store <c>ERROR</c> after its failed fires, or <see langword="null" /> for none.</summary>
        public static string? FailParkOf { get; set; }

        /// <summary>Every fire the delegate was asked to write, by trigger name, in order — a rolled-back attempt included.</summary>
        public static List<string> FireAttempts
        {
            get
            {
                lock (fireAttempts)
                {
                    return [.. fireAttempts];
                }
            }
        }

        public static void Reset()
        {
            lock (fireAttempts)
            {
                fireAttempts.Clear();
            }

            FailFireOf = null;
            FailFireOfTransientlyOnce = null;
            FailParkOf = null;
        }

        public override async ValueTask<int> UpdateTriggerStateFromOtherState(
            ConnectionAndTransactionHolder conn,
            TriggerKey triggerKey,
            StoredTriggerState newState,
            StoredTriggerState oldState,
            CancellationToken cancellationToken = default)
        {
            if (newState == StoredTriggerState.Error && string.Equals(triggerKey.Name, FailParkOf, StringComparison.Ordinal))
            {
                using DbCommand command = conn.Connection.CreateCommand();
                conn.Attach(command);
                command.CommandText = "UPDATE QRTZ_NO_SUCH_TABLE SET TRIGGER_STATE = 'ERROR'";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return await base.UpdateTriggerStateFromOtherState(conn, triggerKey, newState, oldState, cancellationToken);
        }

        public override async ValueTask ApplyTriggerFired(
            ConnectionAndTransactionHolder conn,
            TriggerFiredUpdate update,
            CancellationToken cancellationToken = default)
        {
            string triggerName = update.Trigger.Key.Name;
            lock (fireAttempts)
            {
                fireAttempts.Add(triggerName);
            }

            await base.ApplyTriggerFired(conn, update, cancellationToken);

            if (FailFireOf == "*" || string.Equals(triggerName, FailFireOf, StringComparison.Ordinal))
            {
                // A real exception from the driver, and one nothing classifies as transient.
                using DbCommand command = conn.Connection.CreateCommand();
                conn.Attach(command);
                command.CommandText = "SELECT 1 FROM QRTZ_NO_SUCH_TABLE";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            if (string.Equals(triggerName, FailFireOfTransientlyOnce, StringComparison.Ordinal))
            {
                FailFireOfTransientlyOnce = null;
                throw new SqliteException("database is locked", 5 /* SQLITE_BUSY, which the store retries */);
            }
        }
    }

    /// <summary>
    /// Completes <see cref="Fired" /> when the named trigger's job is about to run.
    /// </summary>
    private sealed class FiringSignal(string triggerName) : IJobListener
    {
        public TaskCompletionSource Fired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => "firing-signal";

        public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (string.Equals(context.Trigger.Key.Name, triggerName, StringComparison.Ordinal))
            {
                Fired.TrySetResult();
            }

            return default;
        }
    }

    [DisallowConcurrentExecution]
    public sealed class SerialJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed class OrdinaryJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
