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
using System.Data;
using System.Data.Common;
using System.IO;
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
/// A batch fire whose commit fails because the connection broke, as Npgsql reports it: the commit
/// throws, and the driver has disposed the transaction, so that asking it for its connection throws as
/// well (#4255).
/// </summary>
/// <remarks>
/// <para>
/// The rollback after a failed commit asks the transaction for its connection to decide whether there
/// is anything left to roll back. Asked of a disposed Npgsql transaction, that threw an
/// <see cref="ObjectDisposedException" />, which escaped <see cref="IJobStore.TriggersFired" /> in place
/// of the commit's failure. The store never asked whether the fire had landed anyway, and the scheduler
/// thread met an exception that is not a <see cref="SchedulerException" />.
/// </para>
/// <para>
/// The store's check of whether the commit landed can race the commit itself, which may still be settling
/// on the server (#4259). SQLite settles a commit before it returns, so these tests stand in for the race:
/// the check's read of the fired rows is answered from before the fire, inside the check's own transaction,
/// which is then rolled back. <see cref="CheckSees" /> says until when.
/// </para>
/// <para>
/// One <see cref="JobStoreTX" /> on a SQLite file, initialized and never started, driven by hand on a
/// clock the test sets, as <see cref="SerialJobCompletionMisfireSqliteTest" /> does. Its transactions
/// are the driver's behind a stand-in that can fail the next commit the way a broken Npgsql connection
/// fails it.
/// </para>
/// </remarks>
[NonParallelizable]
[Category("db-sqlite")]
public sealed class TriggersFiredBrokenCommitSqliteTest
{
    private const string SchedulerName = "TriggersFiredBrokenCommitSqliteTest";
    private const string Group = "broken-commit";

    /// <summary>On the hour, UTC, far from any machine's own clock.</summary>
    private static readonly DateTimeOffset Epoch = new DateTimeOffset(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Bounds a store that waits for something, so that a regression fails the test instead of hanging it.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private Func<DateTimeOffset> originalUtcNow;
    private string databaseFile;
    private string connectionString;
    private BrokenCommitJobStore store;

    [SetUp]
    public async Task BuildStore()
    {
        originalUtcNow = SystemTime.UtcNow;
        SystemTime.UtcNow = () => Epoch;

        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-broken-commit-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";
        InstallSchemaFromFreshInstallScript(connectionString);

        string dataSource = $"broken-commit-{Guid.NewGuid():N}";
        DBConnectionManager.Instance.AddConnectionProvider(dataSource, new DbProvider("SQLite-Microsoft", connectionString));

        SystemTextJsonObjectSerializer serializer = new SystemTextJsonObjectSerializer();
        serializer.Initialize();

        store = new BrokenCommitJobStore
        {
            DataSource = dataSource,
            TablePrefix = "QRTZ_",
            InstanceName = SchedulerName,
            InstanceId = "node-a",
            DriverDelegateType = typeof(BreakableTransactionSqliteDelegate).AssemblyQualifiedName,
            ObjectSerializer = serializer,
        };

        // Initialized but deliberately not started, so no misfire pass or recovery moves the rows read here.
        await store.Initialize(new SimpleTypeLoadHelper(), A.Fake<ISchedulerSignaler>());
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        await store.Shutdown();
        SqliteConnection.ClearAllPools();
        if (File.Exists(databaseFile))
        {
            File.Delete(databaseFile);
        }

        SystemTime.UtcNow = originalUtcNow;
    }

    /// <summary>
    /// The commit timed out on the client after it had reached the database. The store asks whether the
    /// fire landed rather than assuming either answer, finds that it did, and hands the firing over to
    /// run — whose end lets go of the serial job's other trigger, which nothing else would.
    /// </summary>
    [Test]
    public async Task ACommitThatLandedBeforeTheConnectionBrokeIsFoundAndTheFiringGoesAhead()
    {
        (IOperableTrigger acquired, TriggerKey sibling) = await GivenAnAcquiredTriggerOfASerialJob();

        store.NextCommit = transaction =>
        {
            transaction.Inner.Commit();
            transaction.DisposeAsNpgsqlDoes();
            throw new TimeoutException("Timeout during reading attempt");
        };

        IReadOnlyCollection<TriggerFiredResult> fired = await WithPatience(store.TriggersFired(new[] { acquired }));

        TriggerFiredBundle bundle = fired.Should().ContainSingle().Subject.TriggerFiredBundle;
        bundle.Should().NotBeNull(
            "the fire is in the database, so reporting it failed would leave its row EXECUTING with nothing running it");
        (await FiredStates()).Should().Equal(new[] { AdoConstants.StateExecuting });
        (await StoredState(sibling)).Should().Be(AdoConstants.StateBlocked,
            "the premise: the fire that landed holds the serial job's other trigger back");

        await WithPatience(store.TriggeredJobComplete(bundle.Trigger, bundle.JobDetail, SchedulerInstruction.NoInstruction));

        (await FiredStates()).Should().BeEmpty();
        (await StoredState(sibling)).Should().Be(AdoConstants.StateWaiting, "the firing's end lets go of the job's other triggers");
    }

    /// <summary>
    /// The commit reached the database and was still settling there when the store checked whether it had
    /// landed (#4259). The check's first statement writes the trigger row the fire wrote, which waits until
    /// the commit has settled, so the read after it finds the fire. The firing goes ahead, so the occurrence
    /// is kept, and its end lets go of the serial job's other trigger. A check that only read would have
    /// answered from before the fire: the store threw, and the scheduler released the occurrence.
    /// </summary>
    [Test]
    public async Task ACommitStillInFlightWhenCheckedIsWaitedForAndTheFiringGoesAhead()
    {
        (IOperableTrigger acquired, TriggerKey sibling) = await GivenAnAcquiredTriggerOfASerialJob();
        CommitCheck check = LandTheNextCommitButReportItFailed(CheckSees.OnceSettled, PutTheFiredRowBackToAcquired);

        IReadOnlyCollection<TriggerFiredResult> fired = await WithPatience(store.TriggersFired(new[] { acquired }));

        TriggerFiredBundle bundle = fired.Should().ContainSingle().Subject.TriggerFiredBundle;
        bundle.Should().NotBeNull("the fire landed, so the firing runs rather than its occurrence being released");
        check.Settled.Should().BeTrue("the check's first statement waits on the trigger row the fire wrote");
        check.AnsweredFromBefore.Should().BeFalse("the check read once the commit had settled");
        (await FiredStates()).Should().Equal(new[] { AdoConstants.StateExecuting });
        (await StoredState(sibling)).Should().Be(AdoConstants.StateBlocked,
            "the premise: the fire that landed holds the serial job's other trigger back");

        await WithPatience(store.TriggeredJobComplete(bundle.Trigger, bundle.JobDetail, SchedulerInstruction.NoInstruction));

        (await FiredStates()).Should().BeEmpty();
        (await StoredState(sibling)).Should().Be(AdoConstants.StateWaiting, "the firing's end lets go of the job's other triggers");
    }

    /// <summary>
    /// The commit never reached the database. What reaches the scheduler thread is the commit's own
    /// failure, a <see cref="SchedulerException" />, which it reports to the scheduler listeners before it
    /// releases the batch; and the reservation is still there for that release.
    /// </summary>
    [Test]
    public async Task ACommitThatFailedWithTheConnectionIsReportedAsThePersistenceFailureItWas()
    {
        (IOperableTrigger acquired, TriggerKey sibling) = await GivenAnAcquiredTriggerOfASerialJob();

        store.NextCommit = transaction =>
        {
            transaction.Inner.Rollback();
            transaction.DisposeAsNpgsqlDoes();
            throw new InvalidOperationException("The connection broke before the COMMIT reached the server.");
        };

        Func<Task> fire = () => WithPatience(store.TriggersFired(new[] { acquired }));

        (await fire.Should().ThrowAsync<JobPersistenceException>(
                "the scheduler thread tells its listeners of a SchedulerException; anything else it only logs before releasing the batch"))
            .WithInnerExceptionExactly<InvalidOperationException>()
            .WithMessage("The connection broke before the COMMIT reached the server.");

        (await StoredState(acquired.Key)).Should().Be(AdoConstants.StateAcquired, "nothing of the fire was committed");

        await WithPatience(store.ReleaseAcquiredTrigger(acquired));

        (await StoredState(acquired.Key)).Should().Be(AdoConstants.StateWaiting);
        (await StoredState(sibling)).Should().Be(AdoConstants.StateWaiting);
        (await FiredStates()).Should().BeEmpty();
    }

    /// <summary>
    /// The commit reached the database and the connection broke on the way back, and the store's check of
    /// whether the fire landed read the rows as they were before it, even after its first statement had
    /// waited on the trigger row (#4259). That is the fallback: a delegate that does not derive from
    /// <see cref="StdAdoDelegate" /> has no such statement, and a check is only as good as the database's
    /// row locks. The scheduler thread is told the fire failed and releases the batch. The fire had moved
    /// the serial job's other trigger to <c>BLOCKED</c>, or to <c>PAUSED_BLOCKED</c> if it was paused. No
    /// completion comes for a firing nobody runs, so the release has to let go of it.
    /// </summary>
    [TestCase(false, AdoConstants.StateBlocked, AdoConstants.StateWaiting)]
    [TestCase(true, AdoConstants.StatePausedBlocked, AdoConstants.StatePaused)]
    public async Task ACommitThatLandedUnseenIsReleasedAndLetsGoOfTheSerialJobsOtherTrigger(bool siblingPaused, string held, string released)
    {
        (IOperableTrigger acquired, TriggerKey sibling) = await GivenAnAcquiredTriggerOfASerialJob();
        if (siblingPaused)
        {
            await store.PauseTrigger(sibling);
        }

        CommitCheck check = LandTheNextCommitButReportItFailed(CheckSees.Never, PutTheFiredRowBackToAcquired);

        Func<Task> fire = () => WithPatience(store.TriggersFired(new[] { acquired }));

        await fire.Should().ThrowAsync<JobPersistenceException>(
            "the check found no sign of the fire, so the store reports the commit's failure, and the scheduler thread releases the batch");
        check.AnsweredFromBefore.Should().BeTrue("the premise: the check read the rows as they were before the fire");
        (await FiredStates()).Should().Equal(new[] { AdoConstants.StateExecuting }, "the premise: the fire landed all the same");
        (await StoredState(sibling)).Should().Be(held, "the premise: the fire that landed holds the serial job's other trigger back");

        await WithPatience(store.ReleaseAcquiredTrigger(acquired));

        (await FiredStates()).Should().BeEmpty("the release deletes the fired row of a firing nobody runs");
        (await StoredState(sibling)).Should().Be(released,
            "nothing else of the job is executing and no completion is coming, so the release lets go of the job's other trigger");
    }

    /// <summary>
    /// How the store's check after a failed commit sees a commit that did land.
    /// </summary>
    private enum CheckSees
    {
        /// <summary>
        /// The commit is still settling when the check starts: the check's reads of the fired rows are
        /// answered from before it until a statement of the check has waited on a row it wrote.
        /// </summary>
        OnceSettled,

        /// <summary>The check's first read of the fired rows is answered from before the commit, whatever came first.</summary>
        Never,
    }

    /// <summary>What the store's check after the failed commit did.</summary>
    private sealed class CommitCheck
    {
        /// <summary>Whether the check has yet to see the commit: from the failed commit until it has.</summary>
        public bool Pending { get; set; }

        /// <summary>Whether the check issued the statement that waits for the commit to settle.</summary>
        public bool Settled { get; set; }

        /// <summary>Whether a read of the check's was answered from before the commit.</summary>
        public bool AnsweredFromBefore { get; set; }
    }

    /// <summary>Inside the check's transaction: the fired row as it was before the fire of an acquired trigger.</summary>
    private const string PutTheFiredRowBackToAcquired = "UPDATE QRTZ_FIRED_TRIGGERS SET STATE = 'ACQUIRED'";

    /// <summary>
    /// The start of the statement that waits for the commit to settle: a write of a trigger's state over
    /// itself. Recognised by its text, so that these tests build on a store that does not issue it.
    /// </summary>
    private const string SettlingStatement = "UPDATE QRTZ_TRIGGERS SET TRIGGER_STATE = TRIGGER_STATE ";

    /// <summary>
    /// Has the next commit land and then fail, as a connection that broke on the way back fails it, and the
    /// store's check of whether it landed see it as <paramref name="sees" /> says.
    /// </summary>
    /// <param name="sees">Until when the check's reads are answered from before the commit.</param>
    /// <param name="concealment">
    /// The statement that answers a read from before the commit. It runs inside the check's transaction, which
    /// is then rolled back rather than committed, so the check sees what a read racing the commit would see,
    /// and writes nothing.
    /// </param>
    private CommitCheck LandTheNextCommitButReportItFailed(CheckSees sees, string concealment)
    {
        CommitCheck check = new CommitCheck();
        store.NextCommit = transaction =>
        {
            transaction.Inner.Commit();
            transaction.DisposeAsNpgsqlDoes();
            check.Pending = true;
            throw new InvalidOperationException("The connection broke after the COMMIT reached the server.");
        };

        string checkRead = AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlSelectInstancesFiredTriggers, "QRTZ_");
        store.BeforeCommand = (sql, transaction) =>
        {
            if (!check.Pending)
            {
                return;
            }

            if (sql.StartsWith(SettlingStatement, StringComparison.Ordinal))
            {
                check.Settled = true;
                if (sees == CheckSees.OnceSettled)
                {
                    check.Pending = false;
                }

                return;
            }

            if (sql != checkRead)
            {
                return;
            }

            check.Pending = false;
            check.AnsweredFromBefore = true;

            using SqliteCommand command = (SqliteCommand) transaction.Inner.Connection.CreateCommand();
            command.Transaction = (SqliteTransaction) transaction.Inner;
            command.CommandText = concealment;
            command.ExecuteNonQuery();

            store.NextCommit = checkTransaction => checkTransaction.Inner.Rollback();
        };

        return check;
    }

    /// <summary>
    /// Stores a job that disallows concurrent execution with a trigger due now and one due in an hour,
    /// and acquires the first, as the scheduler thread does before it asks for the fire.
    /// </summary>
    private async Task<(IOperableTrigger Acquired, TriggerKey Sibling)> GivenAnAcquiredTriggerOfASerialJob()
    {
        IJobDetail job = JobBuilder.Create<SerialJobUnderTest>().WithIdentity("serial", Group).StoreDurably().Build();
        IOperableTrigger due = OneShot("due", job, Epoch);
        IOperableTrigger later = OneShot("later", job, Epoch.AddHours(1));

        await store.StoreJob(job, replaceExisting: true);
        await store.StoreTrigger(due, replaceExisting: true);
        await store.StoreTrigger(later, replaceExisting: true);

        IReadOnlyCollection<IOperableTrigger> acquired = await store.AcquireNextTriggers(Epoch.AddSeconds(1), 1, TimeSpan.Zero);

        IOperableTrigger trigger = acquired.Should().ContainSingle().Subject;
        trigger.Key.Should().Be(due.Key, "the trigger under test is the one due now");
        return (trigger, later.Key);
    }

    private static IOperableTrigger OneShot(string name, IJobDetail job, DateTimeOffset startAt)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(startAt)
            .WithSimpleSchedule(x => x.WithRepeatCount(0))
            .Build();

        // What the scheduler does with a trigger before handing it to the store.
        trigger.ComputeFirstFireTimeUtc(null);
        return trigger;
    }

    private async Task<string> StoredState(TriggerKey key)
    {
        using SqliteConnection connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name AND TRIGGER_GROUP = @group";
        command.Parameters.AddWithValue("@name", key.Name);
        command.Parameters.AddWithValue("@group", key.Group);
        return (string) await command.ExecuteScalarAsync();
    }

    private async Task<List<string>> FiredStates()
    {
        using SqliteConnection connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT STATE FROM QRTZ_FIRED_TRIGGERS";

        List<string> states = new List<string>();
        using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            states.Add(reader.GetString(0));
        }

        return states;
    }

    private static async Task WithPatience(Task task)
    {
        Task first = await Task.WhenAny(task, Task.Delay(Patience));
        if (first != task)
        {
            throw new TimeoutException($"The store did not answer within {Patience}.");
        }

        await task;
    }

    private static async Task<T> WithPatience<T>(Task<T> task)
    {
        await WithPatience((Task) task);
        return await task;
    }

    private static void InstallSchemaFromFreshInstallScript(string connectionString)
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
    /// The shipped store, whose every transaction is the driver's behind a <see cref="BreakableTransaction" />.
    /// </summary>
    private sealed class BrokenCommitJobStore : JobStoreTX
    {
        /// <summary>
        /// Runs in place of the next commit, once, with the transaction being committed: what a commit
        /// that fails looks like — whether the driver's own commit got through first or not — is the
        /// test's to say.
        /// </summary>
        public Action<BreakableTransaction> NextCommit { get; set; }

        /// <summary>
        /// Runs before each statement the delegate prepares in one of this store's transactions, with the
        /// statement's text: what the store's check after a failed commit sees is the test's to say.
        /// </summary>
        public Action<string, BreakableTransaction> BeforeCommand { get; set; }

        internal Action<BreakableTransaction> TakeNextCommit()
        {
            Action<BreakableTransaction> commit = NextCommit;
            NextCommit = null;
            return commit;
        }

        protected override ConnectionAndTransactionHolder GetConnection()
        {
            ConnectionAndTransactionHolder holder = base.GetConnection();
            return holder.Transaction == null
                ? holder
                : new ConnectionAndTransactionHolder(holder.Connection, new BreakableTransaction(holder.Transaction, this));
        }
    }

    /// <summary>
    /// The driver's transaction, which it answers for until the store fails its commit. Disposed the way
    /// Npgsql disposes a transaction when its connector breaks, nothing is rolled back, and reading
    /// <see cref="DbTransaction.Connection" /> throws from then on.
    /// </summary>
    private sealed class BreakableTransaction : DbTransaction
    {
        private readonly BrokenCommitJobStore store;
        private bool disposedByDriver;

        public BreakableTransaction(DbTransaction inner, BrokenCommitJobStore store)
        {
            Inner = inner;
            this.store = store;
        }

        public DbTransaction Inner { get; }

        internal BrokenCommitJobStore Store => store;

        public void DisposeAsNpgsqlDoes() => disposedByDriver = true;

        protected override DbConnection DbConnection => disposedByDriver ? throw new ObjectDisposedException("NpgsqlTransaction") : Inner.Connection;

        public override IsolationLevel IsolationLevel => Inner.IsolationLevel;

        public override void Commit()
        {
            Action<BreakableTransaction> commit = store.TakeNextCommit();
            if (commit != null)
            {
                commit(this);
                return;
            }

            Inner.Commit();
        }

        public override void Rollback() => Inner.Rollback();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// The shipped SQLite delegate, putting the driver's own transaction on each command it prepares:
    /// a SQLite command accepts no other. The store's <see cref="BrokenCommitJobStore.BeforeCommand" />
    /// sees each statement first.
    /// </summary>
    public sealed class BreakableTransactionSqliteDelegate : SQLiteDelegate
    {
        public override DbCommand PrepareCommand(ConnectionAndTransactionHolder cth, string commandText)
        {
            if (cth.Transaction is not BreakableTransaction breakable)
            {
                return base.PrepareCommand(cth, commandText);
            }

            breakable.Store.BeforeCommand?.Invoke(commandText, breakable);
            return base.PrepareCommand(new ConnectionAndTransactionHolder(cth.Connection, breakable.Inner), commandText);
        }
    }

    /// <summary>Never executed: these tests drive the store, not a scheduler.</summary>
    [DisallowConcurrentExecution]
    public sealed class SerialJobUnderTest : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
