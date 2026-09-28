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

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FakeItEasy;

using Microsoft.Data.Sqlite;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// One trigger's fire failing on the database undoes that fire alone; the rest of the batch commits as
/// it is reported (#3931).
/// </summary>
/// <remarks>
/// <para>
/// A fire writes several rows — the fired row, the <c>BLOCKED</c> state of a
/// <see cref="DisallowConcurrentExecutionAttribute" /> job's other triggers, the trigger row — and a
/// statement that failed partway left the writes before it in the transaction, which the batch then
/// committed beside a failed result: the job's other triggers <c>BLOCKED</c> with nothing executing to
/// let go of them. Now the attempt is rolled back whole and the batch fired again without the failed
/// trigger.
/// </para>
/// <para>
/// One <see cref="JobStoreTX" /> on a SQLite file, initialized and never started, driven by hand, with
/// the shipped SQLite delegate failing the fire it is told to after the fire's own writes have gone
/// out. The same on PostgreSQL and SQL Server is <c>TriggerFireFailureTestBase</c>.
/// </para>
/// </remarks>
[NonParallelizable]
[Category("db-sqlite")]
public sealed class TriggerFireFailureSqliteTest
{
    private const string SchedulerName = "trigger-fire-failure";
    private const string Group = "fire-failure";
    private static readonly JobKey SerialJobKey = new JobKey("serial", Group);
    private static readonly JobKey OrdinaryJobKey = new JobKey("ordinary", Group);
    private static readonly JobKey BrokenJobKey = new JobKey("broken", Group);

    private string databaseFile;
    private string connectionString;
    private JobStoreTX store;
    private DateTimeOffset due;

    [SetUp]
    public async Task CreateStore()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-trigger-fire-failure-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";
        InstallSchemaFromFreshInstallScript();

        FaultingSqliteDelegate.Reset();
        CancellingJobStore.CancelFireOf = null;
        store = await BuildStore();

        // Ahead of now by a known margin, so the misfire cutoff stays out of the acquisition read and
        // the fire-time order is the one each test schedules.
        due = DateTimeOffset.UtcNow.AddSeconds(30);
    }

    [TearDown]
    public async Task DisposeStore()
    {
        await store.Shutdown();

        SqliteConnection.ClearAllPools();
        if (File.Exists(databaseFile))
        {
            File.Delete(databaseFile);
        }
    }

    /// <summary>
    /// A batch of three whose middle fire fails: the two beside it commit as fired, the failed fire's
    /// writes are gone, and the failed trigger's job-mate is not left <c>BLOCKED</c>.
    /// </summary>
    [Test]
    public async Task AFailedFireIsRolledBackAndTheRestOfTheBatchCommitsAsReported()
    {
        await StoreJobs();

        // Fire-time order: ordinary-1, poison, ordinary-2, sibling. The sibling is the poison trigger's
        // job-mate: what the poison fire moves to BLOCKED, and must not leave there.
        await Schedule("ordinary-1", OrdinaryJobKey, due);
        await Schedule("poison", SerialJobKey, due.AddMilliseconds(1));
        await Schedule("ordinary-2", OrdinaryJobKey, due.AddMilliseconds(2));
        await Schedule("sibling", SerialJobKey, due.AddMilliseconds(3));

        List<IOperableTrigger> acquired = await Acquire(maxCount: 4);
        acquired.Select(x => x.Key.Name).Should().Equal(new[] { "ordinary-1", "poison", "ordinary-2" },
            "a batch takes one trigger of a serial job, so the sibling stays behind");

        FaultingSqliteDelegate.FailFireOf = "poison";

        List<TriggerFiredResult> results = (await store.TriggersFired(acquired)).ToList();

        results.Should().HaveCount(3, "one answer per trigger, in the order asked");
        results[0].TriggerFiredBundle.Should().NotBeNull("ordinary-1 fired before the failure");
        results[1].TriggerFiredBundle.Should().BeNull("the poison fire failed");
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
        FaultingSqliteDelegate.FireAttempts.Should().Equal(new[] { "ordinary-1", "poison", "ordinary-1", "ordinary-2" },
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
        await StoreJobs();
        await Schedule("poison", SerialJobKey, due);
        await Schedule("sibling", SerialJobKey, due.AddMilliseconds(1));

        List<IOperableTrigger> acquired = await Acquire(maxCount: 1);
        FaultingSqliteDelegate.FailFireOf = "poison";

        IReadOnlyCollection<TriggerFiredResult> results = await store.TriggersFired(acquired);

        results.Should().ContainSingle().Which.Exception.Should().NotBeNull();
        (await TriggerState("sibling")).Should().Be("WAITING");
        (await TriggerState("poison")).Should().Be("ACQUIRED");
        (await FiredState("poison")).Should().Be("ACQUIRED");
        FaultingSqliteDelegate.FireAttempts.Should().Equal(new[] { "poison" }, "with nothing else in the batch there is nothing to fire again");
    }

    /// <summary>
    /// Every fire failing is every trigger answered failed, in order, in as many attempts as there are
    /// triggers.
    /// </summary>
    [Test]
    public async Task EveryFireFailingAnswersFailedForEachInOrder()
    {
        await StoreJobs();
        await Schedule("poison-1", SerialJobKey, due);
        await Schedule("poison-2", OrdinaryJobKey, due.AddMilliseconds(1));

        List<IOperableTrigger> acquired = await Acquire(maxCount: 2);
        acquired.Should().HaveCount(2);
        FaultingSqliteDelegate.FailFireOf = "*";

        IReadOnlyCollection<TriggerFiredResult> results = await store.TriggersFired(acquired);

        results.Should().HaveCount(2).And.OnlyContain(x => x.Exception != null && x.TriggerFiredBundle == null);
        FaultingSqliteDelegate.FireAttempts.Should().Equal(new[] { "poison-1", "poison-2" },
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
        await StoreJobs();
        await Schedule("ordinary-1", OrdinaryJobKey, due);
        await Schedule("broken-1", BrokenJobKey, due.AddMilliseconds(1));

        List<IOperableTrigger> acquired = await Acquire(maxCount: 2);
        acquired.Should().HaveCount(2, "the job loads at acquisition; it stops loading afterwards");

        // The job stops loading between the acquisition and the fire: its data map no longer
        // deserializes, as a serializer change underneath stored rows would make it.
        await ExecuteNonQuery("UPDATE QRTZ_JOB_DETAILS SET JOB_DATA = X'00FF' WHERE JOB_NAME = @name", "broken");

        List<TriggerFiredResult> results = (await store.TriggersFired(acquired)).ToList();

        results.Should().HaveCount(2);
        results[0].TriggerFiredBundle.Should().NotBeNull();
        results[1].TriggerFiredBundle.Should().BeNull();
        results[1].Exception.Should().BeOfType<JobPersistenceException>()
            .Which.InnerException.Should().NotBeAssignableTo<DbException>("the database refused nothing; the job's stored data is what failed");

        (await TriggerState("broken-1")).Should().Be("ERROR", "the store settled the trigger, and the batch committed it");
        (await FiredState("ordinary-1")).Should().Be("EXECUTING");
        FaultingSqliteDelegate.FireAttempts.Should().Equal(new[] { "ordinary-1" },
            "nothing was rolled back, so nothing was fired twice; the broken trigger never reached the write");

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
        await StoreJobs();
        await Schedule("ordinary-1", OrdinaryJobKey, due);
        await Schedule("ordinary-2", OrdinaryJobKey, due.AddMilliseconds(1));

        List<IOperableTrigger> acquired = await Acquire(maxCount: 2);
        FaultingSqliteDelegate.FailFireOfTransientlyOnce = "ordinary-2";

        IReadOnlyCollection<TriggerFiredResult> results = await store.TriggersFired(acquired);

        results.Should().HaveCount(2).And.OnlyContain(x => x.TriggerFiredBundle != null);
        FaultingSqliteDelegate.FireAttempts.Should().Equal(new[] { "ordinary-1", "ordinary-2", "ordinary-1", "ordinary-2" },
            "a transient failure is the transaction wrapper's to retry, and it retries the attempt whole");
        (await FiredState("ordinary-1")).Should().Be("EXECUTING");
        (await FiredState("ordinary-2")).Should().Be("EXECUTING");
    }

    /// <summary>
    /// A cancellation inside a fire is the caller asking to stop, not a trigger that failed: the attempt
    /// is rolled back whole, once, and the batch is not fired again trigger by trigger.
    /// </summary>
    /// <remarks>
    /// Raised by a store that overrides <c>TriggerFired</c>, as one honouring the caller's token would:
    /// the shipped <c>TriggerFired</c> wraps what nearly every one of its statements throws in a
    /// <see cref="JobPersistenceException" />, a cancellation included, and that is a failed fire.
    /// </remarks>
    [Test]
    public async Task ACancellationInsideAFireRollsTheBatchBackOnceAndIsNotAFailedFire()
    {
        await StoreJobs();
        await Schedule("ordinary-1", OrdinaryJobKey, due);
        await Schedule("cancelled", SerialJobKey, due.AddMilliseconds(1));
        await Schedule("ordinary-2", OrdinaryJobKey, due.AddMilliseconds(2));

        List<IOperableTrigger> acquired = await Acquire(maxCount: 3);
        acquired.Should().HaveCount(3);
        CancellingJobStore.CancelFireOf = "cancelled";

        Func<Task> act = () => store.TriggersFired(acquired);

        // 3.x's transaction wrapper reports every exception that is not a persistence exception as one,
        // a cancellation included.
        await act.Should().ThrowAsync<JobPersistenceException>().WithInnerException(typeof(OperationCanceledException));
        FaultingSqliteDelegate.FireAttempts.Should().Equal(new[] { "ordinary-1", "cancelled" },
            "the attempt stops where it was cancelled and is not fired again without the trigger it stopped at");
        (await FiredState("ordinary-1")).Should().Be("ACQUIRED", "the attempt was rolled back, the fire before the cancellation with it");
        (await TriggerState("cancelled")).Should().Be("ACQUIRED");
    }

    private async Task<List<IOperableTrigger>> Acquire(int maxCount)
    {
        // Wide enough that triggers due milliseconds apart make one batch; a batch ends at the first
        // trigger's fire time plus the window.
        IReadOnlyCollection<IOperableTrigger> acquired = await store.AcquireNextTriggers(DateTimeOffset.UtcNow.AddMinutes(5), maxCount, TimeSpan.FromSeconds(5));
        return acquired.ToList();
    }

    private async Task StoreJobs()
    {
        await store.StoreJob(JobBuilder.Create<SerialJob>().WithIdentity(SerialJobKey).StoreDurably().Build(), replaceExisting: true);
        await store.StoreJob(JobBuilder.Create<OrdinaryJob>().WithIdentity(OrdinaryJobKey).StoreDurably().Build(), replaceExisting: true);
        await store.StoreJob(JobBuilder.Create<OrdinaryJob>().WithIdentity(BrokenJobKey).StoreDurably().Build(), replaceExisting: true);
    }

    /// <summary>
    /// What the scheduler does with a trigger before handing it to the store.
    /// </summary>
    private async Task Schedule(string name, JobKey job, DateTimeOffset at)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(at)
            .Build();
        trigger.ComputeFirstFireTimeUtc(null);
        await store.StoreTrigger(trigger, replaceExisting: false);
    }

    private Task<string> TriggerState(string triggerName)
    {
        return ReadScalar("SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name", triggerName);
    }

    private Task<string> FiredState(string triggerName)
    {
        return ReadScalar("SELECT STATE FROM QRTZ_FIRED_TRIGGERS WHERE TRIGGER_NAME = @name", triggerName);
    }

    private async Task<long> FiredRowCount(string triggerName)
    {
        using SqliteConnection connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE TRIGGER_NAME = @name";
        command.Parameters.AddWithValue("@name", triggerName);
        return (long) await command.ExecuteScalarAsync();
    }

    private async Task<string> ReadScalar(string sql, string name)
    {
        using SqliteConnection connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@name", name);
        return (string) await command.ExecuteScalarAsync();
    }

    private async Task ExecuteNonQuery(string sql, string name)
    {
        using SqliteConnection connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@name", name);
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
    }

    private async Task<JobStoreTX> BuildStore()
    {
        string dataSource = $"trigger-fire-failure-{Guid.NewGuid():N}";
        DBConnectionManager.Instance.AddConnectionProvider(dataSource, new DbProvider("SQLite-Microsoft", connectionString));

        SystemTextJsonObjectSerializer serializer = new SystemTextJsonObjectSerializer();
        serializer.Initialize();

        JobStoreTX jobStore = new CancellingJobStore
        {
            DataSource = dataSource,
            TablePrefix = "QRTZ_",
            InstanceName = SchedulerName,
            InstanceId = "node",
            DriverDelegateType = typeof(FaultingSqliteDelegate).AssemblyQualifiedName,
            ObjectSerializer = serializer,
            // A transient failure is retried at once rather than a second later.
            TransientRetryInterval = TimeSpan.Zero,
        };

        // Initialized and never started: nothing here is left to a scheduler thread, a misfire pass or
        // a startup recovery, each of which would move the rows these tests read.
        await jobStore.Initialize(new SimpleTypeLoadHelper(), A.Fake<ISchedulerSignaler>());
        return jobStore;
    }

    private void InstallSchemaFromFreshInstallScript()
    {
        using SqliteConnection connection = new SqliteConnection(connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = File.ReadAllText(ResolveRepositoryFile("database", "tables", "tables_sqlite.sql"));
        command.ExecuteNonQuery();
    }

    private static string ResolveRepositoryFile(params string[] pathSegments)
    {
        string relativePath = Path.Combine(pathSegments);
        DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find '{relativePath}' above '{AppContext.BaseDirectory}'.");
    }

    /// <summary>
    /// The shipped SQLite delegate, failing the fire it is told to after the fire's own writes have gone
    /// out — the shape of a constraint violation or a schema drift on the last statement of a fire.
    /// </summary>
    /// <remarks>
    /// A fire's first write is its fired row and its last is the trigger row, so a fire attempt is
    /// counted at the one and failed after the other. Static, because the store builds the delegate from
    /// its type name; the fixture zeroes it before each test.
    /// </remarks>
    public sealed class FaultingSqliteDelegate : SQLiteDelegate
    {
        private static readonly List<string> fireAttempts = new List<string>();

        /// <summary>The trigger whose fire fails, <c>*</c> for every one, or <see langword="null" /> for none.</summary>
        public static string FailFireOf { get; set; }

        /// <summary>The trigger whose next fire fails as a busy database would make it fail, once.</summary>
        public static string FailFireOfTransientlyOnce { get; set; }

        /// <summary>Every fire the delegate was asked to write, by trigger name, in order — a rolled-back attempt included.</summary>
        public static List<string> FireAttempts
        {
            get
            {
                lock (fireAttempts)
                {
                    return fireAttempts.ToList();
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

        public override Task<int> UpdateFiredTrigger(
            ConnectionAndTransactionHolder conn,
            IOperableTrigger trigger,
            string state,
            IJobDetail job,
            CancellationToken cancellationToken = default)
        {
            lock (fireAttempts)
            {
                fireAttempts.Add(trigger.Key.Name);
            }

            return base.UpdateFiredTrigger(conn, trigger, state, job, cancellationToken);
        }

        public override async Task<int> UpdateTrigger(
            ConnectionAndTransactionHolder conn,
            IOperableTrigger trigger,
            string state,
            IJobDetail jobDetail,
            CancellationToken cancellationToken = default)
        {
            int updated = await base.UpdateTrigger(conn, trigger, state, jobDetail, cancellationToken);

            string triggerName = trigger.Key.Name;
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

            return updated;
        }
    }

    /// <summary>
    /// <see cref="JobStoreTX" />, cancelling the fire it is told to once the fire's writes have gone out,
    /// as a store that honours its caller's token between two statements would.
    /// </summary>
    public sealed class CancellingJobStore : JobStoreTX
    {
        /// <summary>The trigger whose fire is cancelled, or <see langword="null" /> for none.</summary>
        public static string CancelFireOf { get; set; }

        protected override async Task<TriggerFiredBundle> TriggerFired(
            ConnectionAndTransactionHolder conn,
            IOperableTrigger trigger,
            CancellationToken cancellationToken = default)
        {
            TriggerFiredBundle bundle = await base.TriggerFired(conn, trigger, cancellationToken);
            if (string.Equals(trigger.Key.Name, CancelFireOf, StringComparison.Ordinal))
            {
                throw new OperationCanceledException("cancelled inside the fire");
            }

            return bundle;
        }
    }

    [DisallowConcurrentExecution]
    public sealed class SerialJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }

    public sealed class OrdinaryJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
