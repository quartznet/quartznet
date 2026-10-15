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

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// What a clustered firing round and a one-off's completion cost in round trips, pinned statement by
/// statement against a real database (#4066): one read per concern for the round, whatever its size,
/// and one read and one write for the completion.
/// </summary>
/// <remarks>
/// <para>
/// A round of triggers whose jobs disallow concurrent execution used to ask per trigger whether the job
/// was executing — in the acquisition and again in the fire — and read the paused groups one to three
/// times per fire, and every round read its triggers' headers back although it had just built the
/// triggers from those rows. A round trip here is a command or a batch the store sent, recorded by
/// <see cref="CountingDbProvider" /> with its text, so what is asserted is which statements a round made
/// rather than a number alone; the number is printed for the pull request's table.
/// </para>
/// <para>
/// SQLite, which has no <c>DbBatch</c>, run twice: once as the driver is, and once with the provider
/// standing in a batch for it, so that the counts are the ones PostgreSQL, SQL Server and MySQL make.
/// Driven against the store on a clock the test owns, initialized but never started, with the SQLite
/// serialization switched off on a file nobody else touches, as <see cref="LockFreeCompletionSqliteTest" />
/// does: the paths under test are the ones every other dialect takes.
/// </para>
/// </remarks>
[TestFixture(false)]
[TestFixture(true)]
public sealed class RoundCommandCountSqliteTest
{
    private const string Group = "round";
    private const string SchedulerName = "RoundCommandCountSqliteTest";
    private const string DataSourceName = "round-command-count";
    private const string InstanceId = "node-a";

    /// <summary>On the hour, UTC, far from any machine's own clock.</summary>
    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    private readonly bool batches;

    private SqliteTestDatabase database = null!;
    private CountingDbProvider provider = null!;
    private FakeTimeProvider clock = null!;
    private LocalTransactionJobStore store = null!;

    /// <param name="batches">Whether the provider stands in a <c>DbBatch</c> for the driver.</param>
    public RoundCommandCountSqliteTest(bool batches)
    {
        this.batches = batches;
    }

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

        provider = new CountingDbProvider(new DbProvider("SQLite-Microsoft", database.ConnectionString), batches);
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

        // Initialize forced the SQLite serialization on; one test at a time on a private file has no
        // second writer, and the lock-free completion is one of the paths under test. The round still
        // acquires within the lock, which SQLite forces too and which is what every round of more than
        // one trigger does on every dialect.
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
    /// A round of five triggers of five jobs that disallow concurrent execution asks once whether any of
    /// the jobs is executing and reads the paused groups once, and reads no header back: six reads and
    /// writes beside the fire writes, where the same round made twenty-five.
    /// </summary>
    [Test]
    public async Task ARoundOfNonConcurrentTriggersReadsEachConcernOnce()
    {
        for (int i = 1; i <= 5; i++)
        {
            await Schedule("serial-" + i, nonConcurrent: true, oneOff: false);
        }

        provider.Reset();
        TriggerAcquisitionResult round = await Round(maxCount: 5);
        List<string> kinds = Kinds();
        Report("five non-concurrent repeating triggers", kinds);

        round.Due.Should().HaveCount(5, "every trigger is due and each belongs to a job of its own");
        round.Fired.Should().OnlyContain(x => x.TriggerFiredBundle != null);

        kinds.Count(x => x == "executing-jobs").Should().Be(1, "one read answers for every job the round takes a trigger of");
        kinds.Should().NotContain("executing-job", "neither the acquisition nor the fire asks per trigger any more");
        kinds.Count(x => x == "paused-groups").Should().Be(1, "the first fire that asks reads the paused groups for the round");
        kinds.Should().NotContain(["paused-trigger-group", "paused-job-group"], "no fire reads them per trigger");
        kinds.Should().NotContain(["headers", "header"], "the discriminator came back with the candidate read, and the state is the claim's");

        List<string> expected = ["acquire", "triggers", "executing-jobs", .. Claims(5), "jobs", "paused-groups"];
        kinds.Take(expected.Count).Should().Equal(expected,
            "the round's reads happen once each, in this order, before the fires' writes go out");
        kinds.Skip(expected.Count).Should().OnlyContain(x => IsFireWrite(x),
            "everything after them is the fires' writes: five reads and the claims, whatever the round's size");

        if (batches)
        {
            // Six round trips before the writes, and the writes in as many batches as the 7 KiB split
            // makes of them; the same round made twenty-five before the writes.
            provider.RoundTrips.Count(x => !x.IsBatch).Should().Be(5);
        }

        // The firings end as they always did: the locked completion lets the job's triggers go.
        foreach (TriggerFiredResult fired in round.Fired)
        {
            await Complete(fired.TriggerFiredBundle!, SchedulerInstruction.NoInstruction);
        }

        for (int i = 1; i <= 5; i++)
        {
            (await Column("TRIGGER_STATE", "serial-" + i)).Should().Be(AdoConstants.StateWaiting);
        }

        (await Count("QRTZ_FIRED_TRIGGERS")).Should().Be(0);
    }

    /// <summary>
    /// A round of triggers whose jobs run beside themselves asks after neither executing jobs nor paused
    /// groups, and reads no header back: four reads and claims beside the fire writes, where it made five.
    /// </summary>
    [Test]
    public async Task ARoundOfPlainTriggersReadsNothingPerTrigger()
    {
        for (int i = 1; i <= 5; i++)
        {
            await Schedule("plain-" + i, nonConcurrent: false, oneOff: false);
        }

        provider.Reset();
        TriggerAcquisitionResult round = await Round(maxCount: 5);
        List<string> kinds = Kinds();
        Report("five concurrent-allowed repeating triggers", kinds);

        round.Due.Should().HaveCount(5);
        round.Fired.Should().OnlyContain(x => x.TriggerFiredBundle != null);

        kinds.Should().NotContain(["executing-jobs", "executing-job"], "no job of the round is held to one firing at a time");
        kinds.Should().NotContain(["paused-groups", "paused-trigger-group", "paused-job-group"], "a fire that stores WAITING asks nothing of the paused groups");
        kinds.Should().NotContain(["headers", "header"]);

        List<string> expected = ["acquire", "triggers", .. Claims(5), "jobs"];
        kinds.Take(expected.Count).Should().Equal(expected);
        kinds.Skip(expected.Count).Should().OnlyContain(x => IsFireWrite(x),
            "three reads and the claims before the fires' writes, where the headers used to make it four");

        if (batches)
        {
            provider.RoundTrips.Count(x => !x.IsBatch).Should().Be(3);
        }
    }

    /// <summary>The claims of a round of <paramref name="count" /> triggers: one batch, or one statement each.</summary>
    private IEnumerable<string> Claims(int count) => batches ? ["batch:claim"] : Enumerable.Repeat("claim", count);

    /// <summary>
    /// Whether a recorded round trip is one of a fire's writes: the fired row, the trigger's and its
    /// job-mates' states, the trigger's schedule row. The 7 KiB split cuts the batches wherever it falls,
    /// so a write batch may begin on any of them.
    /// </summary>
    private static bool IsFireWrite(string kind)
    {
        string statement = kind.StartsWith("batch:", StringComparison.Ordinal) ? kind["batch:".Length..] : kind;
        return statement == "fired-insert" || statement.StartsWith("other:UPDATE ", StringComparison.Ordinal);
    }

    /// <summary>
    /// The one write the paused-group reads could make is still made, once: a group resumed while
    /// everything is paused gets its row back on the first fire of a trigger that asks, and the next fire
    /// of the round finds that row as the next per-trigger read would have.
    /// </summary>
    [Test]
    public async Task AFireMaterializesTheResumedGroupsRowOnceForTheRound()
    {
        await Schedule("serial-1", nonConcurrent: true, oneOff: false);
        await Schedule("serial-2", nonConcurrent: true, oneOff: false);

        await store.PauseAll();
        await store.ResumeTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals(Group));
        (await PausedTriggerGroups()).Should().Equal([AdoConstants.AllGroupsPaused], "the resume removed the group's row and left the all-groups marker");

        provider.Reset();
        TriggerAcquisitionResult round = await Round(maxCount: 2);
        List<string> kinds = Kinds();

        round.Fired.Should().HaveCount(2).And.OnlyContain(x => x.TriggerFiredBundle != null);
        kinds.Count(x => x == "paused-groups").Should().Be(1);
        kinds.Count(x => x == "insert-paused-trigger-group").Should().Be(1,
            "the first fire materializes the group's row because everything is paused, and the second finds it in the round's read");
        (await PausedTriggerGroups()).Should().BeEquivalentTo([AdoConstants.AllGroupsPaused, Group]);
    }

    /// <summary>
    /// A spent one-off of a durable job, whose job runs beside itself, completes without the lock in one
    /// read — its header beside whatever awaits it — and one write: the trigger, its schedule row and its
    /// fired rows as one batch. It used to be two reads and three writes.
    /// </summary>
    [Test]
    public async Task ASpentOneOffCompletesInOneReadAndOneWrite()
    {
        IJobDetail job = await Schedule("one-off", nonConcurrent: false, oneOff: true);
        TriggerFiredBundle firing = await FireOne();

        provider.Reset();
        await Complete(firing, SchedulerInstruction.DeleteTrigger);
        List<string> kinds = Kinds();
        Report("a spent one-off's lock-free completion", kinds);

        kinds.Should().Equal(
            batches ? ["header-and-awaiting", "batch:delete-simple"] : ["header-and-awaiting", "delete-simple", "delete-trigger-of-type", "delete-fired-of-deleted"],
            "the probe and the double-check are one read, and the three deletes go together where the driver batches");
        kinds.Should().NotContain(["awaiting", "header", "delete-fired-by-entry", "delete-trigger"]);

        if (batches)
        {
            provider.RoundTrips.Single(x => x.IsBatch).Statements.Should().HaveCount(3);
        }

        (await store.GetTrigger(firing.Trigger.Key)).Should().BeNull("the trigger has nothing left to fire");
        (await Count("QRTZ_SIMPLE_TRIGGERS")).Should().Be(0, "its schedule row goes with it");
        (await Count("QRTZ_FIRED_TRIGGERS")).Should().Be(0, "and its fired row");
        (await store.GetJob(job.Key)).Should().NotBeNull("a durable job outlives its triggers");
    }

    /// <summary>
    /// The folded read answers the question the probe asked: a trigger awaiting the one-off still sends
    /// the completion to the lock, where it settles the continuation by outcome.
    /// </summary>
    [Test]
    public async Task AContinuationAwaitingTheOneOffStillEscalatesTheCompletionToTheLock()
    {
        await Schedule("parent", nonConcurrent: false, oneOff: true);
        IOperableTrigger child = await ScheduleContinuation("child", new TriggerKey("parent", Group));
        TriggerFiredBundle firing = await FireOne();

        provider.Reset();
        await Complete(firing, SchedulerInstruction.DeleteTrigger);
        List<string> kinds = Kinds();

        kinds.Count(x => x == "header-and-awaiting").Should().Be(1, "the lock-free attempt asked once and found the child");
        kinds.Should().Contain("awaiting", "the locked run read the continuations to settle them");
        (await store.GetTrigger(firing.Trigger.Key)).Should().BeNull("the parent is spent");
        (await store.GetTriggerState(child.Key)).Should().Be(TriggerState.Normal, "the parent succeeded, which is what the child waited for");
    }

    /// <summary>
    /// A completion holds no lock, so a reschedule can replace the trigger's row between the completion's
    /// read and its write. A replacement of another type is not the row that was read: the deletes find
    /// nothing, the replacement keeps its schedule and its fired rows — another node's firing of it may be
    /// among them — and the completion deletes its own fired row by entry id, as a completion whose trigger
    /// stays always has.
    /// </summary>
    [Test]
    public async Task AOneOffReplacedByAnotherTypeBetweenTheReadAndTheDeleteIsLeftAlone()
    {
        await Schedule("one-off", nonConcurrent: false, oneOff: true);
        TriggerFiredBundle firing = await FireOne();
        TriggerKey key = firing.Trigger.Key;

        bool replaced = false;
        provider.BeforeCommand = async (sql, connection, transaction) =>
        {
            // Between the completion's read and its first delete.
            if (Kind(sql) != "delete-simple" || replaced)
            {
                return;
            }

            // What a reschedule to a cron trigger under the same key leaves behind, written inside the
            // completion's own transaction so that its next statement sees it, as the next statement of
            // a completion on another connection would after the reschedule committed. Beside it, a
            // fired row of the replacement, as another node's firing of it would have written.
            replaced = true;
            await Execute(connection, transaction, "DELETE FROM QRTZ_SIMPLE_TRIGGERS WHERE SCHED_NAME = @s AND TRIGGER_NAME = @n AND TRIGGER_GROUP = @g", key);
            await Execute(connection, transaction, "UPDATE QRTZ_TRIGGERS SET TRIGGER_TYPE = 'CRON', TRIGGER_STATE = 'WAITING', NEXT_FIRE_TIME = @t WHERE SCHED_NAME = @s AND TRIGGER_NAME = @n AND TRIGGER_GROUP = @g", key, ("@t", epoch.AddHours(1).UtcTicks));
            await Execute(connection, transaction, "INSERT INTO QRTZ_CRON_TRIGGERS (SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP, CRON_EXPRESSION, TIME_ZONE_ID) VALUES (@s, @n, @g, '0 0 11 * * ?', 'UTC')", key);
            await Execute(connection, transaction, "INSERT INTO QRTZ_FIRED_TRIGGERS (SCHED_NAME, ENTRY_ID, TRIGGER_NAME, TRIGGER_GROUP, INSTANCE_NAME, FIRED_TIME, SCHED_TIME, PRIORITY, STATE, JOB_NAME, JOB_GROUP) VALUES (@s, 'node-b-firing', @n, @g, 'node-b', @t, @t, 5, 'EXECUTING', 'one-off', @g)", key, ("@t", epoch.AddHours(1).UtcTicks));
        };

        provider.Reset();
        await Complete(firing, SchedulerInstruction.DeleteTrigger);
        List<string> kinds = Kinds();

        replaced.Should().BeTrue("the premise: the row was replaced between the read and the delete");
        (await Column("TRIGGER_TYPE", key.Name)).Should().Be(AdoConstants.TriggerTypeCron, "the replacement is not the row the completion read, and is left alone");
        (await Column("NEXT_FIRE_TIME", key.Name)).Should().Be(epoch.AddHours(1).UtcTicks, "with its schedule");
        (await Count("QRTZ_CRON_TRIGGERS")).Should().Be(1, "and its schedule row");
        (await FiredRowEntries(key)).Should().Equal(["node-b-firing"],
            "the replacement keeps its fired rows, and the completion's own row went by entry id");
        kinds.Count(x => x == "delete-fired-by-entry").Should().Be(1, "a completion whose trigger stays deletes its fired row by entry id, as always");

        // Written as before: the trigger is a cron trigger now, so the completion's deletes name the row
        // by the type it read and sweep fired rows only once no row is there.
        kinds.Should().Contain(batches ? "batch:delete-simple" : "delete-trigger-of-type");
    }

    //---------------------------------------------------------------------------
    // The store, driven by hand
    //---------------------------------------------------------------------------

    private async Task<IJobDetail> Schedule(string name, bool nonConcurrent, bool oneOff)
    {
        IJobDetail job = nonConcurrent
            ? JobBuilder.Create<NonConcurrentJob>().WithIdentity(name, Group).StoreDurably().Build()
            : JobBuilder.Create<ConcurrentJob>().WithIdentity(name, Group).StoreDurably().Build();

        TriggerBuilder<IJob> builder = TriggerBuilder.Create(clock)
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(clock.GetUtcNow());

        if (!oneOff)
        {
            builder = builder.WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever());
        }

        IOperableTrigger trigger = (IOperableTrigger) builder.Build();
        trigger.ComputeFirstFireTimeUtc(calendar: null);
        await store.ScheduleJob(job, trigger);
        return job;
    }

    private async Task<IOperableTrigger> ScheduleContinuation(string name, TriggerKey parent)
    {
        IJobDetail job = JobBuilder.Create<ConcurrentJob>().WithIdentity(name, Group).Build();

        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(clock.GetUtcNow().AddMinutes(-1))
            .StartAfter(parent, ContinuationCondition.OnSuccess)
            .Build();

        trigger.ComputeFirstFireTimeUtc(calendar: null);
        await store.ScheduleJob(job, trigger);
        (await store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Awaiting, "the continuation waits for its parent");
        return trigger;
    }

    /// <summary>
    /// One round that acquires and fires what is due, which on this store every round of any size is.
    /// </summary>
    private ValueTask<TriggerAcquisitionResult> Round(int maxCount)
    {
        return store.AcquireNextTriggersAndFireDue(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddSeconds(1),
            MaxCount = maxCount,
            TimeWindow = TimeSpan.Zero,
        });
    }

    private async Task<TriggerFiredBundle> FireOne()
    {
        TriggerAcquisitionResult round = await Round(maxCount: 1);
        TriggerFiredBundle? bundle = round.Fired.Should().ContainSingle().Subject.TriggerFiredBundle;
        bundle.Should().NotBeNull("a firing has to start before it can complete");
        return bundle!;
    }

    private Task Complete(TriggerFiredBundle firing, SchedulerInstruction instruction)
    {
        return store.FiringComplete(new TriggeredJobCompleteContext
        {
            Trigger = firing.Trigger,
            JobDetail = firing.JobDetail,
            Instruction = instruction,
            Outcome = ExecutionOutcome.Succeeded,
        }).AsTask();
    }

    //---------------------------------------------------------------------------
    // What the provider recorded
    //---------------------------------------------------------------------------

    private List<string> Kinds()
    {
        List<string> kinds = new(provider.RoundTrips.Count);
        foreach (RecordedRoundTrip roundTrip in provider.RoundTrips)
        {
            kinds.Add(roundTrip.IsBatch ? "batch:" + Kind(roundTrip.Statements[0]) : Kind(roundTrip.Statements[0]));
        }

        return kinds;
    }

    private void Report(string what, List<string> kinds)
    {
        TestContext.Out.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{what}, {(batches ? "batched" : "unbatched")}: {provider.RoundTrips.Count} round trips: {string.Join(", ", kinds)}"));
    }

    /// <summary>Names a statement by the constant it was built from.</summary>
    private static string Kind(string sql)
    {
        if (sql == Sql(StdAdoConstants.SqlSelectCountExecutingFiredTriggersOfJob))
        {
            return "executing-job";
        }

        if (sql.StartsWith(Sql(StdAdoConstants.SqlSelectExecutingJobsPrefix), StringComparison.Ordinal))
        {
            return "executing-jobs";
        }

        if (sql == Sql(StdAdoConstants.SqlSelectPausedGroups))
        {
            return "paused-groups";
        }

        if (sql == Sql(StdAdoConstants.SqlSelectPausedTriggerGroup))
        {
            return "paused-trigger-group";
        }

        if (sql == Sql(StdAdoConstants.SqlSelectPausedJobGroup))
        {
            return "paused-job-group";
        }

        if (sql == Sql(StdAdoConstants.SqlInsertPausedTriggerGroup))
        {
            return "insert-paused-trigger-group";
        }

        if (sql.StartsWith(Sql(StdAdoConstants.SqlSelectTriggerHeadersByKeysPrefix), StringComparison.Ordinal))
        {
            return "headers";
        }

        if (sql == Sql(StdAdoConstants.SqlSelectTriggerHeader))
        {
            return "header";
        }

        if (sql == Sql(StdAdoConstants.SqlSelectTriggerHeaderAndAwaiting))
        {
            return "header-and-awaiting";
        }

        if (sql == Sql(StdAdoConstants.SqlSelectAwaitingContinuations))
        {
            return "awaiting";
        }

        if (sql.StartsWith(Sql(StdAdoConstants.SqlSelectJobDetailsByKeysPrefix), StringComparison.Ordinal))
        {
            return "jobs";
        }

        if (sql == Sql(StdAdoConstants.SqlSelectTrigger) || sql.StartsWith(Sql(StdAdoConstants.SqlSelectTriggersByKeysPrefix), StringComparison.Ordinal))
        {
            return "triggers";
        }

        if (sql == Sql(StdAdoConstants.SqlUpdateTriggerStateFromStateWithNextFireTime))
        {
            return "claim";
        }

        if (sql == Sql(StdAdoConstants.SqlInsertFiredTrigger))
        {
            return "fired-insert";
        }

        if (sql == Sql(StdAdoConstants.SqlDeleteFiredTrigger))
        {
            return "delete-fired-by-entry";
        }

        if (sql == Sql(StdAdoConstants.SqlDeleteTriggerOfType))
        {
            return "delete-trigger-of-type";
        }

        if (sql == Sql(StdAdoConstants.SqlDeleteTrigger))
        {
            return "delete-trigger";
        }

        if (sql == Sql(StdAdoConstants.SqlDeleteFiredTriggersOfDeletedTrigger))
        {
            return "delete-fired-of-deleted";
        }

        if (sql == Sql(StdAdoConstants.SqlDeleteSimpleTrigger))
        {
            return "delete-simple";
        }

        if (sql == Sql(StdAdoConstants.SqlDeleteCronTrigger))
        {
            return "delete-cron";
        }

        if (sql.Contains("jd." + AdoConstants.ColumnIsNonConcurrent, StringComparison.Ordinal))
        {
            return "acquire";
        }

        return "other:" + string.Join(' ', sql.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries)).Truncate(60);
    }

    private static string Sql(string constant) => AdoJobStoreUtil.ReplaceTablePrefixCached(constant, AdoConstants.DefaultTablePrefix);

    //---------------------------------------------------------------------------
    // The rows
    //---------------------------------------------------------------------------

    private async Task<object?> Column(string column, string triggerName)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name AND TRIGGER_GROUP = @group";
        command.Parameters.AddWithValue("@name", triggerName);
        command.Parameters.AddWithValue("@group", Group);
        object? value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private async Task<long> Count(string table)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM " + table;
        return (long) (await command.ExecuteScalarAsync())!;
    }

    private async Task<List<string>> PausedTriggerGroups()
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT TRIGGER_GROUP FROM QRTZ_PAUSED_TRIGGER_GRPS ORDER BY TRIGGER_GROUP";
        List<string> groups = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            groups.Add(reader.GetString(0));
        }

        return groups;
    }

    private async Task<List<string>> FiredRowEntries(TriggerKey key)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ENTRY_ID FROM QRTZ_FIRED_TRIGGERS WHERE TRIGGER_NAME = @name AND TRIGGER_GROUP = @group ORDER BY ENTRY_ID";
        command.Parameters.AddWithValue("@name", key.Name);
        command.Parameters.AddWithValue("@group", key.Group);
        List<string> entries = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(reader.GetString(0));
        }

        return entries;
    }

    private static async Task Execute(DbConnection connection, DbTransaction? transaction, string sql, TriggerKey key, params (string Name, object Value)[] extra)
    {
        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameter(command, "@s", SchedulerName);
        AddParameter(command, "@n", key.Name);
        AddParameter(command, "@g", key.Group);
        foreach ((string name, object value) in extra)
        {
            AddParameter(command, name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string LoadSqliteTableScript()
    {
        string path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        return File.ReadAllText(path);
    }

    public sealed class ConcurrentJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    [DisallowConcurrentExecution]
    public sealed class NonConcurrentJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}

file static class StringTruncation
{
    public static string Truncate(this string text, int length) => text.Length <= length ? text : text[..length] + "...";
}
