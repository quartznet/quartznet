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

using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;

using FakeItEasy;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// A calendar that throws while the persistent store handles a misfire fails that trigger alone: the
/// transaction it runs in commits, and the failure counts toward
/// <see cref="AdoJobStoreOptions.MaxConsecutiveFireFailures" /> as a failed fire does (#4006).
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="DisallowConcurrentExecutionAttribute" /> job's completion handles the misfires of the
/// triggers it unblocks, inside its transaction and lock. A throw there rolled the whole completion back,
/// and the completion retries until it succeeds: the job stayed <c>BLOCKED</c>, its fired row stayed, and
/// the worker that ran it never returned. The misfire handler caught the throw, but never stored the
/// trigger <c>ERROR</c>, so it was logged on every scan and could hold the head of every batch.
/// </para>
/// <para>
/// A database failure in the same step is not the trigger's: it still rolls the work back to be retried.
/// </para>
/// <para>
/// One store on a SQLite file, driven by hand on a clock the test owns, initialized but never started,
/// with the shipped SQLite delegate handing out a calendar the test controls.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class ThrowingCalendarMisfireSqliteTest
{
    private const string Group = "throwing-calendar";
    private const string SchedulerName = "ThrowingCalendarMisfireSqliteTest";
    private const string DataSourceName = "throwing-calendar-misfire";

    /// <summary>The name the delegate answers with the faulty calendar.</summary>
    private const string CalendarName = "faulty";

    /// <summary><c>AdoJobStoreOptions.MaxConsecutiveFireFailures</c> as it ships.</summary>
    private const int DefaultMaxConsecutiveFireFailures = 5;

    /// <summary>Log event <c>AdoJobStoreLog.FailingTriggerParkedInError</c>.</summary>
    private const int FailingTriggerParkedInError = 3050;

    /// <summary>Log event <c>MisfireLog.MisfireUpdatePreparationFailed</c>.</summary>
    private const int MisfireUpdatePreparationFailed = 3603;

    /// <summary>Log event <c>AdoJobStoreLog.WorkAfterCommitFailed</c>.</summary>
    private const int WorkAfterCommitFailed = 3053;

    /// <summary>
    /// <c>AdoJobStoreOptions.MisfireThreshold</c> as it ships, which is also how long after one counted
    /// misfire failure the next one counts.
    /// </summary>
    private static readonly TimeSpan misfireThreshold = TimeSpan.FromMinutes(1);

    /// <summary>On the hour, UTC, far from any machine's own clock.</summary>
    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(15);

    private static readonly JobKey serialJobKey = new("serial", Group);
    private static readonly JobKey ordinaryJobKey = new("ordinary", Group);

    private static readonly TriggerKey firstKey = new("first", Group);
    private static readonly TriggerKey calendaredKey = new("calendared", Group);
    private static readonly TriggerKey mateKey = new("mate", Group);
    private static readonly TriggerKey laterKey = new("later", Group);

    private SqliteTestDatabase database = null!;
    private IDbProvider dbProvider = null!;
    private FakeTimeProvider clock = null!;
    private RecordingLoggerProvider logs = null!;
    private ThrowingLoggerProvider throwingLogs = null!;
    private ILoggerFactory loggerFactory = null!;
    private ISchedulerSignaler signaler = null!;
    private CalendarSqliteDelegate driverDelegate = null!;
    private CalendarFault fault = null!;
    private LocalTransactionJobStore store = null!;

    [SetUp]
    public async Task BuildStore()
    {
        database = new SqliteTestDatabase("throwing-calendar-misfire");
        await using (SqliteConnection connection = new(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = new(LoadSqliteTableScript(), connection);
            await command.ExecuteNonQueryAsync();
        }

        dbProvider = new DbProvider("SQLite-Microsoft", database.ConnectionString);
        clock = new FakeTimeProvider(epoch);
        logs = new RecordingLoggerProvider();
        throwingLogs = new ThrowingLoggerProvider();
        loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.AddProvider(logs);
            logging.AddProvider(throwingLogs);
        });
        signaler = A.Fake<ISchedulerSignaler>();
        fault = new CalendarFault();
        driverDelegate = new CalendarSqliteDelegate(new FaultyCalendar(fault));

        await BuildStore(configure: null);
    }

    private async Task BuildStore(Action<AdoJobStoreOptions>? configure)
    {
        store = new LocalTransactionJobStore(TestJobStores.Dependencies(
            signaler: signaler,
            timeProvider: clock,
            schedulerOptions: TestJobStores.SchedulerOptions(instanceName: SchedulerName, instanceId: "node"),
            storeOptions: TestJobStores.StoreOptions(DataSourceName, configure: options =>
            {
                // A failure the completion retries is retried a second later on the test's clock.
                options.DbRetryInterval = TimeSpan.FromSeconds(1);
                options.TransientRetryInterval = TimeSpan.Zero;
                configure?.Invoke(options);
            }),
            dbProvider: dbProvider,
            driverDelegate: driverDelegate,
            loggerFactory: loggerFactory));

        // Initialized but deliberately not started, so no misfire loop races an assertion; each test
        // runs the misfire handler's pass itself.
        await store.Initialize(TestJobStores.Identity(instanceName: SchedulerName, instanceId: "node"));
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        await store.Shutdown();
        loggerFactory.Dispose();
        logs.Dispose();
        throwingLogs.Dispose();
        database.Dispose();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // A serial job's completion
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// A serial job's completion unblocks its other triggers and handles their misfires. The calendar of
    /// one of them throws: the completion commits, carries out its instruction and lets go of the job's
    /// other triggers, and the one that threw is <c>WAITING</c> with its fire time as it was.
    /// </summary>
    [Test]
    public async Task ACalendarThatThrowsAsACompletionLetsGoOfTheJobsTriggersFailsThatTriggerAlone()
    {
        IOperableTrigger firing = await GivenASerialJobRunningPastItsTriggersFireTime();
        fault.ThrowOnce();

        await CompleteWithin(firing, SchedulerInstruction.SetTriggerComplete);

        fault.Thrown.Should().Be(1);
        (await TriggerState(firstKey)).Should().Be(AdoConstants.StateComplete, "the completion carried out its instruction");
        (await FiredRowCount(firstKey)).Should().Be(0, "and let go of its fired row");
        (await TriggerState(mateKey)).Should().Be(AdoConstants.StateWaiting, "the job's other triggers are let go all the same");
        (await store.GetTrigger(mateKey))!.NextFireTimeUtc.Should().Be(epoch.AddHours(1), "and their misfires handled");
        (await TriggerState(calendaredKey)).Should().Be(AdoConstants.StateWaiting, "released, for the misfire handler");
        (await store.GetTrigger(calendaredKey))!.NextFireTimeUtc.Should().Be(epoch, "nothing of its misfire was written");
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == MisfireUpdatePreparationFailed)
            .Which.Exception.Should().BeSameAs(fault.LastThrown);

        await store.RecoverMisfires(Guid.NewGuid());

        (await store.GetTrigger(calendaredKey))!.NextFireTimeUtc.Should().Be(epoch.AddHours(1),
            "the misfire handler handled the misfire once the calendar answered");
    }

    /// <summary>
    /// The failure at the completion counts toward the limit, in the ledger a failed fire counts in: the
    /// misfire handler's failures go on from it, and the one that reaches the limit stores the trigger
    /// <c>ERROR</c> and says so once.
    /// </summary>
    [Test]
    public async Task AFailureAsACompletionLetsGoOfTheJobsTriggersCountsTowardTheLimit()
    {
        IOperableTrigger firing = await GivenASerialJobRunningPastItsTriggersFireTime();
        fault.ThrowAlways();

        await CompleteWithin(firing, SchedulerInstruction.NoInstruction);

        for (int failure = 2; failure < DefaultMaxConsecutiveFireFailures; failure++)
        {
            // A misfire failure counts once per misfire threshold.
            clock.Advance(misfireThreshold);
            await store.RecoverMisfires(Guid.NewGuid());
            (await TriggerState(calendaredKey)).Should().Be(AdoConstants.StateWaiting, $"{failure} failure(s) in a row is short of the limit");
        }

        A.CallTo(() => signaler.NotifySchedulerListenersTriggerInError(A<TriggerKey>._, A<CancellationToken>._)).MustNotHaveHappened();

        clock.Advance(misfireThreshold);
        await store.RecoverMisfires(Guid.NewGuid());

        (await TriggerState(calendaredKey)).Should().Be(AdoConstants.StateError,
            "the completion's failure and the misfire handler's are one run of failures");
        fault.Thrown.Should().Be(DefaultMaxConsecutiveFireFailures);
        A.CallTo(() => signaler.NotifySchedulerListenersTriggerInError(calendaredKey, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == FailingTriggerParkedInError)
            .Which.Message.Should().Contain(calendaredKey.ToString());
    }

    /// <summary>
    /// The failure that reaches the limit at the completion stores the trigger <c>ERROR</c> in the
    /// completion's own transaction, which commits.
    /// </summary>
    [Test]
    public async Task AFailureAsACompletionLetsGoOfTheJobsTriggersThatReachesTheLimitStoresTheTriggerError()
    {
        await store.Shutdown();
        await BuildStore(options => options.MaxConsecutiveFireFailures = 1);
        IOperableTrigger firing = await GivenASerialJobRunningPastItsTriggersFireTime();
        fault.ThrowOnce();

        await CompleteWithin(firing, SchedulerInstruction.NoInstruction);

        (await TriggerState(calendaredKey)).Should().Be(AdoConstants.StateError);
        (await TriggerState(mateKey)).Should().Be(AdoConstants.StateWaiting);
        (await FiredRowCount(firstKey)).Should().Be(0, "the completion committed");
        A.CallTo(() => signaler.NotifySchedulerListenersTriggerInError(calendaredKey, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == FailingTriggerParkedInError);
    }

    /// <summary>
    /// The database failing to read the calendar in the same step is not the trigger's failure. It rolls
    /// the completion back, which is retried, and nothing is counted: with a limit of one, the trigger is
    /// not stored <c>ERROR</c>, and its misfire is handled on the retry.
    /// </summary>
    [Test]
    public async Task ADatabaseFailureAsACompletionLetsGoOfTheJobsTriggersIsRetriedAndNotCounted()
    {
        await store.Shutdown();
        await BuildStore(options => options.MaxConsecutiveFireFailures = 1);
        IOperableTrigger firing = await GivenASerialJobRunningPastItsTriggersFireTime();
        driverDelegate.FailCalendarReadOnce();

        Task completing = Complete(firing, SchedulerInstruction.NoInstruction);

        await WaitFor(() => driverDelegate.CalendarReadFailures > 0, "the calendar read to fail");
        DateTimeOffset deadline = DateTimeOffset.UtcNow + waitLimit;
        while (!completing.IsCompleted && DateTimeOffset.UtcNow < deadline)
        {
            // The completion waits out its retry interval on the test's clock.
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(50);
        }

        completing.IsCompleted.Should().BeTrue("the completion is retried once the database answers");
        await completing;

        driverDelegate.CalendarReads.Should().Be(2, "the rolled-back attempt read the calendar, and so did the retry");
        (await TriggerState(calendaredKey)).Should().Be(AdoConstants.StateWaiting, "a database failure is not the trigger's, so nothing was counted");
        (await store.GetTrigger(calendaredKey))!.NextFireTimeUtc.Should().BeAfter(epoch, "the retry handled its misfire");
        logs.Entries.Should().NotContain(x => x.EventId.Id == FailingTriggerParkedInError);
        logs.Entries.Should().NotContain(x => x.EventId.Id == MisfireUpdatePreparationFailed);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // The misfire handler
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// The misfire handler already went on past a trigger whose calendar throws, but left it
    /// <c>WAITING</c> for good, logged on every scan. Now the scan whose failure reaches the limit stores
    /// it <c>ERROR</c>, and no scan reads it after that.
    /// </summary>
    [Test]
    public async Task TheMisfireHandlerStoresATriggerWhoseCalendarAlwaysThrowsErrorAtTheLimit()
    {
        await AddJob(ordinaryJobKey, serial: false);
        await Schedule(calendaredKey, ordinaryJobKey, onCalendar: true);
        clock.Advance(TimeSpan.FromMinutes(30));
        fault.ThrowAlways();

        for (int failure = 1; failure < DefaultMaxConsecutiveFireFailures; failure++)
        {
            await store.RecoverMisfires(Guid.NewGuid());
            (await TriggerState(calendaredKey)).Should().Be(AdoConstants.StateWaiting, $"{failure} failure(s) in a row is short of the limit");
            (await store.GetTrigger(calendaredKey))!.NextFireTimeUtc.Should().Be(epoch, "nothing of the misfire is written");
            clock.Advance(misfireThreshold);
        }

        await store.RecoverMisfires(Guid.NewGuid());

        (await TriggerState(calendaredKey)).Should().Be(AdoConstants.StateError);
        A.CallTo(() => signaler.NotifySchedulerListenersTriggerInError(calendaredKey, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == FailingTriggerParkedInError);
        logs.Entries.Count(x => x.EventId.Id == MisfireUpdatePreparationFailed).Should().Be(DefaultMaxConsecutiveFireFailures,
            "every failure is logged with its exception");

        await store.RecoverMisfires(Guid.NewGuid());
        fault.Thrown.Should().Be(DefaultMaxConsecutiveFireFailures, "an ERROR trigger is not a misfire to handle");
    }

    /// <summary>
    /// A batch of one: the trigger whose calendar throws is first in the misfire order, and used to be
    /// the whole of every batch, so the trigger behind it was never handled. Once it is stored
    /// <c>ERROR</c>, the next scan reaches the one behind it.
    /// </summary>
    [Test]
    public async Task ATriggerWhoseCalendarAlwaysThrowsNoLongerHoldsTheHeadOfEveryMisfireBatch()
    {
        await store.Shutdown();
        await BuildStore(options => options.MaxMisfiresToHandleAtATime = 1);
        await AddJob(ordinaryJobKey, serial: false);
        await Schedule(calendaredKey, ordinaryJobKey, onCalendar: true);
        await Schedule(laterKey, ordinaryJobKey, startAt: epoch.AddMinutes(1));
        clock.Advance(TimeSpan.FromMinutes(30));
        fault.ThrowAlways();

        for (int scan = 0; scan < DefaultMaxConsecutiveFireFailures; scan++)
        {
            await store.RecoverMisfires(Guid.NewGuid());
            clock.Advance(misfireThreshold);
        }

        (await TriggerState(calendaredKey)).Should().Be(AdoConstants.StateError);
        (await store.GetTrigger(laterKey))!.NextFireTimeUtc.Should().Be(epoch.AddMinutes(1), "behind it, and not reached yet");

        await store.RecoverMisfires(Guid.NewGuid());

        (await store.GetTrigger(laterKey))!.NextFireTimeUtc.Should().Be(epoch.AddMinutes(61),
            "with the failing trigger out of the way, the batch of one is the trigger behind it");
    }

    /// <summary>
    /// A limit of zero never stores the trigger <c>ERROR</c>; the misfire handler goes on past it on every
    /// scan, as before.
    /// </summary>
    [Test]
    public async Task ALimitOfZeroLeavesATriggerWhoseCalendarThrowsToTheMisfireHandler()
    {
        await store.Shutdown();
        await BuildStore(options => options.MaxConsecutiveFireFailures = 0);
        await AddJob(ordinaryJobKey, serial: false);
        await Schedule(calendaredKey, ordinaryJobKey, onCalendar: true);
        clock.Advance(TimeSpan.FromMinutes(30));
        fault.ThrowAlways();

        for (int scan = 0; scan < 2 * DefaultMaxConsecutiveFireFailures; scan++)
        {
            await store.RecoverMisfires(Guid.NewGuid());
        }

        (await TriggerState(calendaredKey)).Should().Be(AdoConstants.StateWaiting);
        fault.Thrown.Should().Be(2 * DefaultMaxConsecutiveFireFailures);
        logs.Entries.Should().NotContain(x => x.EventId.Id == FailingTriggerParkedInError);
    }

    /// <summary>
    /// A backlog of misfires has the misfire handler pass again at once, and a trigger whose calendar
    /// throws is first in every pass. Its failures count once per misfire threshold, so a calendar that is
    /// briefly unreachable does not park its trigger in a fraction of a second; one that stays unreachable
    /// still parks it, on the failure that reaches the limit.
    /// </summary>
    [Test]
    public async Task AMisfireBacklogDoesNotParkATriggerWhoseCalendarThrowsAtOnce()
    {
        await AddJob(ordinaryJobKey, serial: false);
        await Schedule(calendaredKey, ordinaryJobKey, onCalendar: true);
        for (int i = 1; i <= 25; i++)
        {
            await Schedule(new TriggerKey("backlog-" + i.ToString(CultureInfo.InvariantCulture), Group), ordinaryJobKey, startAt: epoch.AddSeconds(i));
        }

        clock.Advance(TimeSpan.FromMinutes(30));
        fault.ThrowAlways();

        int quickPasses = 4 * DefaultMaxConsecutiveFireFailures;
        for (int pass = 0; pass < quickPasses; pass++)
        {
            await store.RecoverMisfires(Guid.NewGuid());
        }

        fault.Thrown.Should().Be(quickPasses, "the throwing trigger is first in every pass");
        (await TriggerState(calendaredKey)).Should().Be(AdoConstants.StateWaiting,
            "the passes came within one misfire threshold, so they count as one failure");
        (await TriggerState(new TriggerKey("backlog-25", Group))).Should().Be(AdoConstants.StateWaiting);
        (await store.GetTrigger(new TriggerKey("backlog-25", Group)))!.NextFireTimeUtc.Should().Be(epoch.AddHours(1).AddSeconds(25),
            "the backlog behind it was handled");

        for (int failure = 2; failure <= DefaultMaxConsecutiveFireFailures; failure++)
        {
            clock.Advance(misfireThreshold);
            await store.RecoverMisfires(Guid.NewGuid());
        }

        (await TriggerState(calendaredKey)).Should().Be(AdoConstants.StateError,
            "a misfire threshold apart, each failure counts, and the one that reaches the limit parks the trigger");
    }

    /// <summary>
    /// A misfire handled ends the trigger's run of failures: one failure before it and one after it are
    /// one each, not two in a row.
    /// </summary>
    [Test]
    public async Task AMisfireHandledBetweenTwoFailuresStartsTheCountAgain()
    {
        await store.Shutdown();
        await BuildStore(options => options.MaxConsecutiveFireFailures = 2);
        await AddJob(ordinaryJobKey, serial: false);
        await Schedule(calendaredKey, ordinaryJobKey, onCalendar: true);
        clock.Advance(TimeSpan.FromMinutes(30));

        fault.ThrowOnce();
        await store.RecoverMisfires(Guid.NewGuid());

        // The calendar answers: the misfire is handled, and the trigger moves on to its next hourly fire.
        await store.RecoverMisfires(Guid.NewGuid());
        (await store.GetTrigger(calendaredKey))!.NextFireTimeUtc.Should().Be(epoch.AddHours(1));

        // Late for that fire too, and the calendar throws again.
        clock.Advance(TimeSpan.FromHours(1));
        fault.ThrowOnce();
        await store.RecoverMisfires(Guid.NewGuid());

        fault.Thrown.Should().Be(2);
        (await TriggerState(calendaredKey)).Should().Be(AdoConstants.StateWaiting,
            "the handled misfire between the two failures started the count again, so this is one failure, short of the limit");
    }

    /// <summary>
    /// What the store does after a commit is bookkeeping, and a failure there does not fail the committed
    /// operation: a logger that throws once as the trigger is parked costs the completion nothing. The
    /// completion used to be retried for it, on and on while the logger kept throwing.
    /// </summary>
    /// <param name="andAsTheFailureIsLogged">
    /// Whether the logger throws again as the store logs that failure, which leaves nowhere to report it.
    /// </param>
    [TestCase(false)]
    [TestCase(true)]
    public async Task ALoggerThatThrowsAfterTheCommitDoesNotFailTheCompletion(bool andAsTheFailureIsLogged)
    {
        await store.Shutdown();
        await BuildStore(options => options.MaxConsecutiveFireFailures = 1);
        IOperableTrigger firing = await GivenASerialJobRunningPastItsTriggersFireTime();
        fault.ThrowOnce();
        throwingLogs.ThrowOnceOn(FailingTriggerParkedInError);
        if (andAsTheFailureIsLogged)
        {
            throwingLogs.ThrowOnceOn(WorkAfterCommitFailed);
        }

        await CompleteWithin(firing, SchedulerInstruction.NoInstruction);

        (await TriggerState(calendaredKey)).Should().Be(AdoConstants.StateError);
        (await FiredRowCount(firstKey)).Should().Be(0, "the completion committed, once");
        A.CallTo(() => signaler.NotifySchedulerListenersTriggerInError(calendaredKey, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == WorkAfterCommitFailed)
            .Which.Exception.Should().NotBeNull();
    }

    /// <summary>
    /// On a connection the application enlisted, what the store records after its part is done comes before
    /// the application commits or rolls back. A misfire failure there is logged and not counted, so one the
    /// application rolled back does not bring the trigger nearer to <c>ERROR</c>.
    /// </summary>
    [Test]
    public async Task AMisfireFailureOnAnEnlistedConnectionIsNotCounted()
    {
        await store.Shutdown();
        await BuildStore(options =>
        {
            options.MaxConsecutiveFireFailures = 2;
            options.AcceptEnlistedTransactions = true;
        });
        await store.SchedulerResumed();
        await AddJob(ordinaryJobKey, serial: false);
        await Schedule(calendaredKey, ordinaryJobKey, onCalendar: true);
        await store.PauseTrigger(calendaredKey);
        clock.Advance(TimeSpan.FromMinutes(30));
        fault.ThrowAlways();

        await using (SqliteConnection connection = new(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using DbTransaction transaction = await connection.BeginTransactionAsync();
            using (AmbientConnection.Enlist(SchedulerName, connection, transaction))
            {
                await store.ResumeTrigger(calendaredKey);
            }

            await transaction.RollbackAsync();
        }

        fault.Thrown.Should().Be(1);
        (await TriggerState(calendaredKey)).Should().Be(AdoConstants.StatePaused, "the application rolled the resume back");

        clock.Advance(misfireThreshold);
        await store.ResumeTrigger(calendaredKey);

        fault.Thrown.Should().Be(2);
        (await TriggerState(calendaredKey)).Should().Be(AdoConstants.StateWaiting,
            "the failure the application rolled back was not counted, so this is the first, short of the limit; the trigger is resumed as it was");
    }

    /// <summary>
    /// A failure is counted only once the transaction it happened in has committed. The calendar throws,
    /// then a later statement of the same completion fails transiently, and the completion is rolled back
    /// and run again, meeting the calendar again: one failure, not two.
    /// </summary>
    [Test]
    public async Task AMisfireFailureInATransactionThatRolledBackIsNotCounted()
    {
        await store.Shutdown();
        await BuildStore(options => options.MaxConsecutiveFireFailures = 2);
        IOperableTrigger firing = await GivenASerialJobRunningPastItsTriggersFireTime();
        fault.ThrowAlways();

        // The retry comes a misfire threshold later, so it is not the interval that keeps it from counting.
        driverDelegate.FailDeleteFiredTriggerOnce(() => clock.Advance(misfireThreshold + misfireThreshold));

        await CompleteWithin(firing, SchedulerInstruction.NoInstruction);

        fault.Thrown.Should().Be(2, "the rolled-back attempt met the calendar, and so did the retry");
        driverDelegate.DeleteFiredTriggerFailures.Should().Be(1);
        (await FiredRowCount(firstKey)).Should().Be(0, "the retry committed");
        (await TriggerState(calendaredKey)).Should().Be(AdoConstants.StateWaiting,
            "the rolled-back attempt's failure was never counted, so the retry's is the first, short of the limit");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Resuming
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// Resuming triggers handles the misfires they accrued while paused. A calendar that throws there
    /// fails that trigger alone: the rest of the group is resumed, the trigger is resumed as it was, and
    /// the caller is not handed the calendar's exception.
    /// </summary>
    [Test]
    public async Task ACalendarThatThrowsAsATriggerIsResumedFailsThatTriggerAlone()
    {
        await store.SchedulerResumed();
        await AddJob(ordinaryJobKey, serial: false);
        await Schedule(calendaredKey, ordinaryJobKey, onCalendar: true);
        await Schedule(laterKey, ordinaryJobKey);
        await store.PauseTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals(Group));
        clock.Advance(TimeSpan.FromMinutes(30));
        fault.ThrowOnce();

        await store.ResumeTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals(Group));

        fault.Thrown.Should().Be(1);
        (await TriggerState(laterKey)).Should().Be(AdoConstants.StateWaiting, "the rest of the group is resumed");
        (await store.GetTrigger(laterKey))!.NextFireTimeUtc.Should().Be(epoch.AddHours(1), "with its misfire handled");
        (await TriggerState(calendaredKey)).Should().Be(AdoConstants.StateWaiting, "resumed as it was");
        (await store.GetTrigger(calendaredKey))!.NextFireTimeUtc.Should().Be(epoch);

        await store.RecoverMisfires(Guid.NewGuid());

        (await store.GetTrigger(calendaredKey))!.NextFireTimeUtc.Should().Be(epoch.AddHours(1),
            "resumed into the schedule, its misfire is the misfire handler's");
    }

    /// <summary>
    /// The failure that reaches the limit as a trigger is resumed stores it <c>ERROR</c>, and the resume
    /// says it was resumed: it is no longer paused.
    /// </summary>
    [Test]
    public async Task AFailureAsATriggerIsResumedThatReachesTheLimitStoresTheTriggerError()
    {
        await store.Shutdown();
        await BuildStore(options => options.MaxConsecutiveFireFailures = 1);
        await store.SchedulerResumed();
        await AddJob(ordinaryJobKey, serial: false);
        await Schedule(calendaredKey, ordinaryJobKey, onCalendar: true);
        await store.PauseTrigger(calendaredKey);
        clock.Advance(TimeSpan.FromMinutes(30));
        fault.ThrowOnce();

        (await store.ResumeTrigger(calendaredKey)).Should().BeTrue("the row was written, if not as a resume");

        (await TriggerState(calendaredKey)).Should().Be(AdoConstants.StateError);
        A.CallTo(() => signaler.NotifySchedulerListenersTriggerInError(calendaredKey, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Helpers
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// <c>first</c>, <c>calendared</c> and <c>mate</c>, in that order, on the serial job, all due at
    /// once: <c>first</c> fires, which blocks the other two, and the job runs half an hour, past their
    /// fire time and the misfire threshold. Answers <c>first</c>'s firing.
    /// </summary>
    private async Task<IOperableTrigger> GivenASerialJobRunningPastItsTriggersFireTime()
    {
        await AddJob(serialJobKey, serial: true);
        await Schedule(firstKey, serialJobKey, priority: 10);
        await Schedule(calendaredKey, serialJobKey, priority: 5, onCalendar: true);
        await Schedule(mateKey, serialJobKey, priority: 1);

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddSeconds(1),
            MaxCount = 10,
            TimeWindow = TimeSpan.Zero
        });

        acquired.Select(x => x.Key).Should().Equal([firstKey], "a batch takes one trigger of a serial job");
        List<TriggerFiredResult> results = await store.TriggersFired(acquired);
        results.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull();
        (await TriggerState(calendaredKey)).Should().Be(AdoConstants.StateBlocked);
        (await TriggerState(mateKey)).Should().Be(AdoConstants.StateBlocked);

        clock.Advance(TimeSpan.FromMinutes(30));
        return results[0].TriggerFiredBundle!.Trigger;
    }

    /// <summary>
    /// Completes the firing, failing the test rather than hanging it if the completion does not return:
    /// a completion that rolls back is retried, on the test's clock, until it commits.
    /// </summary>
    private async Task CompleteWithin(IOperableTrigger firing, SchedulerInstruction instruction)
    {
        Task completing = Complete(firing, instruction);
        Task finished = await Task.WhenAny(completing, Task.Delay(waitLimit));
        finished.Should().BeSameAs(completing,
            "the completion commits; one that rolled back would wait out its retry on a clock nothing moves, for good");
        await completing;
    }

    private Task Complete(IOperableTrigger firing, SchedulerInstruction instruction)
    {
        return store.FiringComplete(new TriggeredJobCompleteContext
        {
            Trigger = firing,
            JobDetail = Job(serialJobKey, serial: true),
            Instruction = instruction,
            Outcome = ExecutionOutcome.Succeeded
        }).AsTask();
    }

    private async Task AddJob(JobKey key, bool serial)
    {
        await store.AddJob(Job(key, serial));
    }

    private static IJobDetail Job(JobKey key, bool serial)
    {
        return serial
            ? JobBuilder.Create<SerialJob>().WithIdentity(key).StoreDurably().Build()
            : JobBuilder.Create<OrdinaryJob>().WithIdentity(key).StoreDurably().Build();
    }

    /// <summary>
    /// Hourly from <paramref name="startAt" />, or the epoch. A trigger that misfires has its misfire
    /// handled by the simple trigger's smart policy, which consults the calendar for the next fire time.
    /// </summary>
    private async Task Schedule(
        TriggerKey key,
        JobKey jobKey,
        int priority = TriggerConstants.DefaultPriority,
        bool onCalendar = false,
        DateTimeOffset? startAt = null)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity(key)
            .ForJob(jobKey)
            .StartAt(startAt ?? epoch)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .WithPriority(priority)
            .WithCalendarName(onCalendar ? CalendarName : null)
            .Build();

        // Without the calendar, so the calendar is consulted only by the misfires the tests count.
        trigger.ComputeFirstFireTimeUtc(calendar: null);
        await store.AddTrigger(trigger);
    }

    private Task<object?> TriggerState(TriggerKey key) => ReadScalar("SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name AND TRIGGER_GROUP = @group", key);

    private async Task<long> FiredRowCount(TriggerKey key)
    {
        return (long) (await ReadScalar("SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE TRIGGER_NAME = @name AND TRIGGER_GROUP = @group", key))!;
    }

    private async Task<object?> ReadScalar(string sql, TriggerKey key)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@name", key.Name);
        command.Parameters.AddWithValue("@group", key.Group);

        object? value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private static async Task WaitFor(Func<bool> condition, string what)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + waitLimit;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail($"Timed out after {waitLimit.TotalSeconds:F0} s waiting for {what}.");
    }

    private static string LoadSqliteTableScript()
    {
        string path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        return File.ReadAllText(path);
    }

    /// <summary>
    /// The shipped SQLite delegate, answering a read of the faulty calendar with the instance the test
    /// controls rather than one deserialized from a row, and failing that read once when told to, as a
    /// database would.
    /// </summary>
    private sealed class CalendarSqliteDelegate(FaultyCalendar calendar) : SQLiteDelegate
    {
        private int failNextRead;
        private int reads;
        private int failures;

        /// <summary>How many times the faulty calendar has been read.</summary>
        public int CalendarReads => Volatile.Read(ref reads);

        /// <summary>How many of those reads failed.</summary>
        public int CalendarReadFailures => Volatile.Read(ref failures);

        public void FailCalendarReadOnce() => Volatile.Write(ref failNextRead, 1);

        public override async ValueTask<ICalendar?> SelectCalendar(
            ConnectionAndTransactionHolder conn,
            string calendarName,
            CancellationToken cancellationToken = default)
        {
            if (!string.Equals(calendarName, CalendarName, StringComparison.Ordinal))
            {
                return await base.SelectCalendar(conn, calendarName, cancellationToken);
            }

            Interlocked.Increment(ref reads);
            if (Interlocked.Exchange(ref failNextRead, 0) == 1)
            {
                Interlocked.Increment(ref failures);

                // A real exception from the driver, and one nothing classifies as transient.
                using DbCommand command = conn.Connection.CreateCommand();
                conn.Attach(command);
                command.CommandText = "SELECT 1 FROM QRTZ_NO_SUCH_TABLE";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return calendar;
        }

        private Action? onDeleteFiredTriggerFailure;
        private int deleteFiredTriggerFailures;

        /// <summary>How many times deleting a fired-trigger row failed.</summary>
        public int DeleteFiredTriggerFailures => Volatile.Read(ref deleteFiredTriggerFailures);

        /// <summary>
        /// Fails the next deletion of a fired-trigger row as a busy database fails it, which the store
        /// retries, after calling <paramref name="onFailure" />.
        /// </summary>
        public void FailDeleteFiredTriggerOnce(Action onFailure) => Volatile.Write(ref onDeleteFiredTriggerFailure, onFailure);

        public override ValueTask<int> DeleteFiredTrigger(
            ConnectionAndTransactionHolder conn,
            string entryId,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref onDeleteFiredTriggerFailure, null) is { } onFailure)
            {
                Interlocked.Increment(ref deleteFiredTriggerFailures);
                onFailure();
                throw new SqliteException("database is locked", 5 /* SQLITE_BUSY, which the store retries */);
            }

            return base.DeleteFiredTrigger(conn, entryId, cancellationToken);
        }
    }

    /// <summary>
    /// A log sink that throws once on each event it is told to.
    /// </summary>
    private sealed class ThrowingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentDictionary<int, bool> throwOn = new();

        public void ThrowOnceOn(int eventId) => throwOn[eventId] = true;

        public ILogger CreateLogger(string categoryName) => new ThrowingLogger(this);

        public void Dispose()
        {
        }

        private sealed class ThrowingLogger(ThrowingLoggerProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (provider.throwOn.TryRemove(eventId.Id, out _))
                {
                    throw new InvalidOperationException("The log sink is down.");
                }
            }
        }
    }

    /// <summary>
    /// Throws on the calls it is told to, and answers every other time as included.
    /// </summary>
    private sealed class CalendarFault
    {
        private readonly Lock gate = new();
        private int remaining;
        private int thrown;
        private Exception? lastThrown;

        public int Thrown
        {
            get
            {
                lock (gate)
                {
                    return thrown;
                }
            }
        }

        public Exception? LastThrown
        {
            get
            {
                lock (gate)
                {
                    return lastThrown;
                }
            }
        }

        public void ThrowOnce()
        {
            lock (gate)
            {
                remaining = 1;
            }
        }

        public void ThrowAlways()
        {
            lock (gate)
            {
                remaining = -1;
            }
        }

        public void Consult()
        {
            Exception failure;
            lock (gate)
            {
                if (remaining == 0)
                {
                    return;
                }

                if (remaining > 0)
                {
                    remaining--;
                }

                thrown++;
                failure = new InvalidOperationException("The holiday feed is unreachable.");
                lastThrown = failure;
            }

            throw failure;
        }
    }

    private sealed class FaultyCalendar(CalendarFault fault) : ICalendar
    {
        public string? Description { get; set; }

        public ICalendar? CalendarBase { get; set; }

        public bool IsTimeIncluded(DateTimeOffset timeUtc)
        {
            fault.Consult();
            return true;
        }

        public DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc)
        {
            fault.Consult();
            return timeUtc;
        }

        public ICalendar Clone() => new FaultyCalendar(fault) { Description = Description, CalendarBase = CalendarBase };
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
