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
/// A batch fire, or a fire-on-acquire round, whose commit fails because the connection broke, as Npgsql
/// reports it: the commit throws, and the driver has disposed the transaction, so that asking it for its
/// connection throws as well (#4255).
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
/// The store's check of whether the commit landed can race the commit itself, which may still be settling
/// on the server (#4259). SQLite settles a commit before it returns, so these tests stand in for the race:
/// the check's read of the fired rows is answered from before the fire, inside the check's own transaction,
/// which is then rolled back. <see cref="CheckSees" /> says until when.
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

        await Complete(bundle!);

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

        List<TriggerFiredResult> fired = await store.TriggersFired([acquired]).AsTask().WaitAsync(patience);

        TriggerFiredBundle? bundle = fired.Should().ContainSingle().Subject.TriggerFiredBundle;
        bundle.Should().NotBeNull("the fire landed, so the firing runs rather than its occurrence being released");
        check.Settled.Should().BeTrue("the check's first statement waits on the trigger row the fire wrote");
        check.AnsweredFromBefore.Should().BeFalse("the check read once the commit had settled");
        (await FiredStates()).Should().Equal([AdoConstants.StateExecuting]);
        (await StoredState(sibling)).Should().Be(AdoConstants.StateBlocked,
            "the premise: the fire that landed holds the serial job's other trigger back");

        await Complete(bundle!);

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

        Func<Task> fire = () => store.TriggersFired([acquired]).AsTask().WaitAsync(patience);

        await fire.Should().ThrowAsync<JobPersistenceException>(
            "the check found no sign of the fire, so the store reports the commit's failure, and the scheduler thread releases the batch");
        check.AnsweredFromBefore.Should().BeTrue("the premise: the check read the rows as they were before the fire");
        (await FiredStates()).Should().Equal([AdoConstants.StateExecuting], "the premise: the fire landed all the same");
        (await StoredState(sibling)).Should().Be(held, "the premise: the fire that landed holds the serial job's other trigger back");

        await store.ReleaseAcquiredTrigger(acquired).AsTask().WaitAsync(patience);

        (await FiredStates()).Should().BeEmpty("the release deletes the fired row of a firing nobody runs");
        (await StoredState(sibling)).Should().Be(released,
            "nothing else of the job is executing and no completion is coming, so the release lets go of the job's other trigger");
    }

    /// <summary>
    /// A fire-on-acquire round whose commit was still settling when the store checked whether it had landed
    /// (#4259). The round inserted its fired row <c>EXECUTING</c>, and nothing waits on an insert that has
    /// not committed, so the check's first statement writes the row of the trigger the round claimed. The
    /// round's firing is handed over and runs, and its end lets go of the serial job's other trigger. A check
    /// that only read found no row: the round threw, nothing released it, and its <c>EXECUTING</c> row and
    /// the <c>BLOCKED</c> trigger stayed until the node restarted.
    /// </summary>
    [Test]
    public async Task AFireOnAcquireRoundStillInFlightHandsItsFiringsOver()
    {
        (TriggerKey due, TriggerKey sibling) = await GivenASerialJobDueNow();
        CommitCheck check = LandTheNextCommitButReportItFailed(CheckSees.OnceSettled, TakeTheFiredRowAway);

        TriggerAcquisitionResult round = await AcquireAndFireDue().AsTask().WaitAsync(patience);

        TriggerFiredBundle? bundle = round.Fired.Should().ContainSingle().Subject.TriggerFiredBundle;
        bundle.Should().NotBeNull("the round landed, so its firing runs rather than being left behind");
        bundle!.Trigger.Key.Should().Be(due);
        check.Settled.Should().BeTrue("the check's first statement waits on the trigger row the round claimed");
        check.AnsweredFromBefore.Should().BeFalse("the check read once the commit had settled");
        (await FiredStates()).Should().Equal([AdoConstants.StateExecuting]);
        (await StoredState(sibling)).Should().Be(AdoConstants.StateBlocked,
            "the premise: the round that landed holds the serial job's other trigger back");

        await Complete(bundle);

        (await FiredStates()).Should().BeEmpty("the firing's end deletes its row");
        (await StoredState(sibling)).Should().Be(AdoConstants.StateWaiting, "the firing's end lets go of the job's other triggers");
    }

    /// <summary>
    /// A fire-on-acquire round whose commit never reached the database. The check waits on nothing and
    /// finds nothing, so the round reports the commit's failure, and nothing of it is left behind.
    /// </summary>
    [Test]
    public async Task AFireOnAcquireRoundThatDidNotLandLeavesNothingExecuting()
    {
        (TriggerKey due, TriggerKey sibling) = await GivenASerialJobDueNow();
        provider.NextCommit = async (transaction, cancellationToken) =>
        {
            await transaction.Inner.RollbackAsync(cancellationToken);
            transaction.DisposeAsNpgsqlDoes();
            throw new InvalidOperationException("The connection broke before the COMMIT reached the server.");
        };

        Func<Task> round = () => AcquireAndFireDue().AsTask().WaitAsync(patience);

        await round.Should().ThrowAsync<JobPersistenceException>("nothing of the round landed, so there is no firing to hand over");
        (await FiredStates()).Should().BeEmpty("nothing of the round committed");
        (await StoredState(due)).Should().Be(AdoConstants.StateWaiting, "the claim was rolled back, for the next round to take");
        (await StoredState(sibling)).Should().Be(AdoConstants.StateWaiting);
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

    /// <summary>Inside the check's transaction: no fired row, as before a fire-on-acquire round inserted it.</summary>
    private const string TakeTheFiredRowAway = "DELETE FROM QRTZ_FIRED_TRIGGERS";

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
        CommitCheck check = new();
        provider.NextCommit = async (transaction, cancellationToken) =>
        {
            await transaction.Inner.CommitAsync(cancellationToken);
            transaction.DisposeAsNpgsqlDoes();
            check.Pending = true;
            throw new InvalidOperationException("The connection broke after the COMMIT reached the server.");
        };

        string settle = Sql(StdAdoDelegate.SqlSettleTriggerRow);
        string checkRead = Sql(StdAdoConstants.SqlSelectFiredTriggers + StdAdoConstants.SqlFiredTriggerInstancePredicate);
        provider.BeforeCommand = async (sql, connection, transaction) =>
        {
            if (!check.Pending)
            {
                return;
            }

            if (sql == settle)
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

            await using SqliteCommand command = (SqliteCommand) connection.CreateCommand();
            command.Transaction = (SqliteTransaction?) transaction;
            command.CommandText = concealment;
            await command.ExecuteNonQueryAsync();

            provider.NextCommit = (checkTransaction, cancellationToken) => checkTransaction.Inner.RollbackAsync(cancellationToken);
        };

        return check;
    }

    /// <summary>
    /// Stores a job that disallows concurrent execution with a trigger due now and one due in an hour,
    /// and acquires the first, as the scheduler thread does before it asks for the fire.
    /// </summary>
    private async Task<(IOperableTrigger Acquired, TriggerKey Sibling)> GivenAnAcquiredTriggerOfASerialJob()
    {
        (TriggerKey due, TriggerKey sibling) = await GivenASerialJobDueNow();

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddSeconds(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero,
        });

        IOperableTrigger trigger = acquired.Should().ContainSingle().Subject;
        trigger.Key.Should().Be(due, "the trigger under test is the one due now");
        return (trigger, sibling);
    }

    /// <summary>
    /// Stores a job that disallows concurrent execution with a trigger due now and one due in an hour.
    /// </summary>
    private async Task<(TriggerKey Due, TriggerKey Sibling)> GivenASerialJobDueNow()
    {
        IJobDetail job = JobBuilder.Create<SerialJobUnderTest>().WithIdentity("serial", Group).StoreDurably().Build();
        DateTimeOffset now = clock.GetUtcNow();
        IOperableTrigger due = OneShot("due", job, now);
        IOperableTrigger later = OneShot("later", job, now.AddHours(1));

        await store.ScheduleJobs(
            new Dictionary<IJobDetail, IReadOnlyCollection<IOperableTrigger>> { [job] = [due, later] },
            ScheduleJobOptions.Replacing);

        return (due.Key, later.Key);
    }

    /// <summary>A round as the scheduler thread asks for one: what is due now, fired as it is acquired.</summary>
    private ValueTask<TriggerAcquisitionResult> AcquireAndFireDue()
    {
        return store.AcquireNextTriggersAndFireDue(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddSeconds(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero,
        });
    }

    /// <summary>Ends a firing as the scheduler does once its job has run.</summary>
    private Task Complete(TriggerFiredBundle bundle)
    {
        return store.FiringComplete(new TriggeredJobCompleteContext
        {
            Trigger = bundle.Trigger,
            JobDetail = bundle.JobDetail,
            Instruction = SchedulerInstruction.NoInstruction,
            Outcome = ExecutionOutcome.Succeeded,
        }).AsTask().WaitAsync(patience);
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
