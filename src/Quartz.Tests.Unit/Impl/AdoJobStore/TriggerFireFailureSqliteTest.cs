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

using FakeItEasy;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;

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

    private SqliteTestDatabase database = null!;
    private ServiceProvider node = null!;
    private IScheduler scheduler = null!;
    private IJobStore store = null!;
    private DateTimeOffset due;

    [SetUp]
    public async Task CreateNode()
    {
        database = new SqliteTestDatabase("trigger-fire-failure");
        FaultingSqliteDelegate.Reset();

        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "trigger-fire-failure";
                options.InstanceId = "node";
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
                });
            });
        });

        node = services.BuildServiceProvider();
        scheduler = await node.GetRequiredService<ISchedulerFactory>().GetScheduler();
        store = node.GetRequiredService<IJobStore>();

        // Ahead of now by a known margin, so the misfire cutoff stays out of the acquisition read and
        // the fire-time order is the one each test schedules.
        due = TimeProvider.System.GetUtcNow().AddSeconds(30);
    }

    [TearDown]
    public async Task DisposeNode()
    {
        await node.DisposeAsync();
        database.Dispose();
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
