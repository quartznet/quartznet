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

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

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
/// thread met an exception that is not a <see cref="SchedulerException" />, so it told no scheduler
/// listener.
/// </para>
/// <para>
/// Driven against the store on a clock the test owns, initialized but never started, with the SQLite
/// serialization switched off on a file nobody else touches, as
/// <see cref="SerialJobCompletionMisfireSqliteTest" /> does.
/// </para>
/// </remarks>
public sealed class TriggersFiredBrokenCommitSqliteTest
{
    private const string Group = "broken-commit";
    private const string SchedulerName = "TriggersFiredBrokenCommitSqliteTest";
    private const string DataSourceName = "triggers-fired-broken-commit";
    private const string InstanceId = "node-a";

    /// <summary>On the hour, UTC, far from any machine's own clock.</summary>
    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Bounds a store that waits for something: a wait on the fake clock never elapses, so without this a
    /// regression would hang the test instead of failing it.
    /// </summary>
    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    private SqliteTestDatabase database = null!;
    private CountingDbProvider provider = null!;
    private FakeTimeProvider clock = null!;
    private LocalTransactionJobStore store = null!;

    [SetUp]
    public async Task BuildStore()
    {
        database = new SqliteTestDatabase(DataSourceName);

        await using (SqliteConnection connection = new(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = new(LoadSqliteTableScript(), connection);
            await command.ExecuteNonQueryAsync();
        }

        provider = new CountingDbProvider(new DbProvider("SQLite-Microsoft", database.ConnectionString), emulateBatches: false);
        clock = new FakeTimeProvider(epoch);
        store = new LocalTransactionJobStore(TestJobStores.Dependencies(
            timeProvider: clock,
            schedulerOptions: TestJobStores.SchedulerOptions(instanceName: SchedulerName, instanceId: InstanceId),
            storeOptions: TestJobStores.StoreOptions(DataSourceName),
            dbProvider: provider,
            driverDelegate: new SQLiteDelegate()));

        // Initialized but deliberately not started, so no misfire loop races an assertion.
        await store.Initialize(TestJobStores.Identity(instanceName: SchedulerName, instanceId: InstanceId));
        await store.Clear();

        // Initialize forced the SQLite serialization on; one test at a time on a private file has no second
        // writer, and without it the store takes its locks as every other dialect's does.
        store.LockAllOperations = false;
        store.LockHandler = new InProcessLockHandler();
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        await store.Shutdown();
        database.Dispose();
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

        provider.NextCommit = async (transaction, cancellationToken) =>
        {
            await transaction.Inner.CommitAsync(cancellationToken);
            transaction.DisposeAsNpgsqlDoes();
            throw new TimeoutException("Timeout during reading attempt");
        };

        List<TriggerFiredResult> fired = await store.TriggersFired([acquired]).AsTask().WaitAsync(patience);

        TriggerFiredBundle? bundle = fired.Should().ContainSingle().Subject.TriggerFiredBundle;
        bundle.Should().NotBeNull(
            "the fire is in the database, so reporting it failed would leave its row EXECUTING with nothing running it");
        (await FiredStates()).Should().Equal([AdoConstants.StateExecuting]);
        (await StoredState(sibling)).Should().Be(AdoConstants.StateBlocked,
            "the premise: the fire that landed holds the serial job's other trigger back");

        await store.FiringComplete(new TriggeredJobCompleteContext
        {
            Trigger = bundle!.Trigger,
            JobDetail = bundle.JobDetail,
            Instruction = SchedulerInstruction.NoInstruction,
            Outcome = ExecutionOutcome.Succeeded,
        }).AsTask().WaitAsync(patience);

        (await FiredStates()).Should().BeEmpty();
        (await StoredState(sibling)).Should().Be(AdoConstants.StateWaiting, "the firing's end lets go of the job's other triggers");
    }

    /// <summary>
    /// The commit never reached the database. What reaches the scheduler thread is the commit's own
    /// failure, a <see cref="SchedulerException" />, which is what it tells the scheduler listeners about
    /// and answers by releasing the batch; and the reservation is still there for that release.
    /// </summary>
    [Test]
    public async Task ACommitThatFailedWithTheConnectionIsReportedAsThePersistenceFailureItWas()
    {
        (IOperableTrigger acquired, TriggerKey sibling) = await GivenAnAcquiredTriggerOfASerialJob();

        provider.NextCommit = async (transaction, cancellationToken) =>
        {
            await transaction.Inner.RollbackAsync(cancellationToken);
            transaction.DisposeAsNpgsqlDoes();
            throw new InvalidOperationException("The connection broke before the COMMIT reached the server.");
        };

        Func<Task> fire = () => store.TriggersFired([acquired]).AsTask().WaitAsync(patience);

        (await fire.Should().ThrowAsync<JobPersistenceException>(
                "the scheduler thread notifies its listeners of a SchedulerException and releases the batch; anything else it only logs"))
            .WithInnerExceptionExactly<InvalidOperationException>()
            .WithMessage("The connection broke before the COMMIT reached the server.");

        (await StoredState(acquired.Key)).Should().Be(AdoConstants.StateAcquired, "nothing of the fire was committed");

        await store.ReleaseAcquiredTrigger(acquired).AsTask().WaitAsync(patience);

        (await StoredState(acquired.Key)).Should().Be(AdoConstants.StateWaiting);
        (await StoredState(sibling)).Should().Be(AdoConstants.StateWaiting);
        (await FiredStates()).Should().BeEmpty();
    }

    /// <summary>
    /// The commit reached the database and the connection broke on the way back, and the store's check of
    /// whether the fire landed read the rows as they were before it (#4259). The check runs under a lock its
    /// requestor already holds, so the database lock is not taken again, and a commit still in flight on the
    /// server is not yet visible to it. The scheduler thread is told the fire failed and releases the batch.
    /// The fire had moved the serial job's other trigger to <c>BLOCKED</c>, or to <c>PAUSED_BLOCKED</c> if
    /// it was paused. No completion comes for a firing nobody runs, so the release has to let go of it.
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

        bool checkReadsTheRowsAsTheyWere = false;
        bool checkAnsweredFromBefore = false;
        provider.NextCommit = async (transaction, cancellationToken) =>
        {
            await transaction.Inner.CommitAsync(cancellationToken);
            transaction.DisposeAsNpgsqlDoes();
            checkReadsTheRowsAsTheyWere = true;
            throw new InvalidOperationException("The connection broke after the COMMIT reached the server.");
        };

        // The check's read of this node's fired rows, answered from before the fire: inside the check's own
        // transaction the row is put back to ACQUIRED, and that transaction is rolled back rather than committed,
        // so the check sees what a read racing the in-flight commit would see and writes nothing.
        string checkRead = Sql(StdAdoConstants.SqlSelectFiredTriggers + StdAdoConstants.SqlFiredTriggerInstancePredicate);
        provider.BeforeCommand = async (sql, connection, transaction) =>
        {
            if (!checkReadsTheRowsAsTheyWere || sql != checkRead)
            {
                return;
            }

            checkReadsTheRowsAsTheyWere = false;
            checkAnsweredFromBefore = true;

            await using SqliteCommand command = (SqliteCommand) connection.CreateCommand();
            command.Transaction = (SqliteTransaction?) transaction;
            command.CommandText = "UPDATE QRTZ_FIRED_TRIGGERS SET STATE = 'ACQUIRED' WHERE ENTRY_ID = @entry";
            command.Parameters.AddWithValue("@entry", acquired.FireInstanceId);
            await command.ExecuteNonQueryAsync();

            provider.NextCommit = (checkTransaction, cancellationToken) => checkTransaction.Inner.RollbackAsync(cancellationToken);
        };

        Func<Task> fire = () => store.TriggersFired([acquired]).AsTask().WaitAsync(patience);

        await fire.Should().ThrowAsync<JobPersistenceException>(
            "the check found no sign of the fire, so the store reports the commit's failure, and the scheduler thread releases the batch");
        checkAnsweredFromBefore.Should().BeTrue("the premise: the check read the rows as they were before the fire");
        (await FiredStates()).Should().Equal([AdoConstants.StateExecuting], "the premise: the fire landed all the same");
        (await StoredState(sibling)).Should().Be(held, "the premise: the fire that landed holds the serial job's other trigger back");

        await store.ReleaseAcquiredTrigger(acquired).AsTask().WaitAsync(patience);

        (await FiredStates()).Should().BeEmpty("the release deletes the fired row of a firing nobody runs");
        (await StoredState(sibling)).Should().Be(released,
            "nothing else of the job is executing and no completion is coming, so the release lets go of the job's other trigger");
    }

    /// <summary>
    /// Stores a job that disallows concurrent execution with a trigger due now and one due in an hour,
    /// and acquires the first, as the scheduler thread does before it asks for the fire.
    /// </summary>
    private async Task<(IOperableTrigger Acquired, TriggerKey Sibling)> GivenAnAcquiredTriggerOfASerialJob()
    {
        IJobDetail job = JobBuilder.Create<SerialJobUnderTest>().WithIdentity("serial", Group).StoreDurably().Build();
        DateTimeOffset now = clock.GetUtcNow();
        IOperableTrigger due = OneShot("due", job, now);
        IOperableTrigger later = OneShot("later", job, now.AddHours(1));

        await store.ScheduleJobs(
            new Dictionary<IJobDetail, IReadOnlyCollection<IOperableTrigger>> { [job] = [due, later] },
            ScheduleJobOptions.Replacing);

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = now.AddSeconds(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero,
        });

        IOperableTrigger trigger = acquired.Should().ContainSingle().Subject;
        trigger.Key.Should().Be(due.Key, "the trigger under test is the one due now");
        return (trigger, later.Key);
    }

    private IOperableTrigger OneShot(string name, IJobDetail job, DateTimeOffset startAt)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(startAt)
            .WithSimpleSchedule(x => x.WithRepeatCount(0))
            .Build();

        trigger.ComputeFirstFireTimeUtc(calendar: null);
        return trigger;
    }

    private async Task<string?> StoredState(TriggerKey key)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name AND TRIGGER_GROUP = @group";
        command.Parameters.AddWithValue("@name", key.Name);
        command.Parameters.AddWithValue("@group", key.Group);
        return (string?) await command.ExecuteScalarAsync();
    }

    private async Task<List<string>> FiredStates()
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT STATE FROM QRTZ_FIRED_TRIGGERS";

        List<string> states = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            states.Add(reader.GetString(0));
        }

        return states;
    }

    private static string Sql(string constant) => AdoJobStoreUtil.ReplaceTablePrefixCached(constant, AdoConstants.DefaultTablePrefix);

    private static string LoadSqliteTableScript()
    {
        string path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        return File.ReadAllText(path);
    }

    /// <summary>Never executed: these tests drive the store, not a scheduler.</summary>
    [DisallowConcurrentExecution]
    public sealed class SerialJobUnderTest : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
