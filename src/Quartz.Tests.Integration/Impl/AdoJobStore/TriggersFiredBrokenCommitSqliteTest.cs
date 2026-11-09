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
    /// a SQLite command accepts no other.
    /// </summary>
    public sealed class BreakableTransactionSqliteDelegate : SQLiteDelegate
    {
        public override DbCommand PrepareCommand(ConnectionAndTransactionHolder cth, string commandText)
        {
            return cth.Transaction is BreakableTransaction breakable
                ? base.PrepareCommand(new ConnectionAndTransactionHolder(cth.Connection, breakable.Inner), commandText)
                : base.PrepareCommand(cth, commandText);
        }
    }

    /// <summary>Never executed: these tests drive the store, not a scheduler.</summary>
    [DisallowConcurrentExecution]
    public sealed class SerialJobUnderTest : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
