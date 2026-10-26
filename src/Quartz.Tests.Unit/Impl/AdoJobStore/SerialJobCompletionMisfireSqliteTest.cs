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

using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// What the completion of a firing of a job that disallows concurrent execution reads to settle the
/// triggers it lets go of, and what it settles (#4167).
/// </summary>
/// <remarks>
/// <para>
/// The completion unblocks the job's other triggers and applies the misfire policy of the ones that went
/// late while held (#1646). It used to read every waiting trigger of the job in full and pick out the late
/// ones in memory, so draining a backlog of N read N rows on each of N completions, under the trigger lock.
/// A delegate Quartz ships now asks the database for the late ones only, with the misfire handler's own
/// predicate. Any other delegate keeps the read it always made, so that an override of it still runs.
/// </para>
/// <para>
/// Run with both, since both have to settle the same triggers. Driven against the store on a clock the test
/// owns, initialized but never started, with the SQLite serialization switched off on a file nobody else
/// touches, as <see cref="RoundCommandCountSqliteTest" /> does.
/// </para>
/// </remarks>
[TestFixture(true)]
[TestFixture(false)]
public sealed class SerialJobCompletionMisfireSqliteTest
{
    private const string Group = "serial";
    private const string SchedulerName = "SerialJobCompletionMisfireSqliteTest";
    private const string DataSourceName = "serial-job-completion";
    private const string InstanceId = "node-a";

    /// <summary>On the hour, UTC, far from any machine's own clock.</summary>
    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan storeThreshold = TimeSpan.FromMinutes(1);

    private readonly bool shipped;

    private SqliteTestDatabase database = null!;
    private CountingDbProvider provider = null!;
    private FakeTimeProvider clock = null!;
    private RecordingSignaler signals = null!;
    private LocalTransactionJobStore store = null!;

    /// <param name="shipped">
    /// Whether the store runs on the SQLite delegate Quartz ships, or on a subclass of it, which the store
    /// treats as a delegate of somebody's own.
    /// </param>
    public SerialJobCompletionMisfireSqliteTest(bool shipped)
    {
        this.shipped = shipped;
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

        provider = new CountingDbProvider(new DbProvider("SQLite-Microsoft", database.ConnectionString), emulateBatches: false);
        clock = new FakeTimeProvider(epoch);
        signals = new RecordingSignaler();
        store = new LocalTransactionJobStore(TestJobStores.Dependencies(
            signaler: signals,
            timeProvider: clock,
            schedulerOptions: TestJobStores.SchedulerOptions(instanceName: SchedulerName, instanceId: InstanceId),
            storeOptions: TestJobStores.StoreOptions(DataSourceName, configure: x => x.MisfireThreshold = storeThreshold),
            dbProvider: provider,
            driverDelegate: shipped ? new SQLiteDelegate() : new OwnSqliteDelegate()));

        // Initialized but deliberately not started, so no misfire loop races an assertion.
        await store.Initialize(TestJobStores.Identity(instanceName: SchedulerName, instanceId: InstanceId));
        await store.Clear();

        // Initialize forced the SQLite serialization on; one test at a time on a private file has no second
        // writer, and without it the store makes the statements every other dialect makes.
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
    /// With a shipped delegate the completion makes the same statements behind a backlog of 450 as behind
    /// one of ten, and reads no trigger in full when none of them is late.
    /// </summary>
    [Test]
    public async Task TheCompletionMakesTheSameStatementsWhateverTheBacklog()
    {
        IgnoreUnless(shipped, "the read of the job's late triggers is the shipped delegates' own");

        List<string> few = await CompletionStatements(backlog: 10);
        List<string> many = await CompletionStatements(backlog: 450);

        many.Should().Equal(few,
            "the database is asked for the job's late triggers, so a longer backlog is more rows for it to look at, not more statements");
        Kinds(many).Should().Contain("misfired-of-job");
        Kinds(many).Should().NotContain(["waiting-of-job", "triggers-by-keys"],
            "no trigger of the job is read in full unless the misfire handler's own predicate says it is late");
    }

    /// <summary>
    /// A delegate of somebody's own keeps the read it always made: the job's waiting keys, then those
    /// triggers in full, so its overrides of either still run.
    /// </summary>
    [Test]
    public async Task ADelegateOfSomebodysOwnKeepsTheReadItAlwaysMade()
    {
        IgnoreUnless(!shipped, "a shipped delegate is asked for the late triggers only");

        List<string> kinds = Kinds(await CompletionStatements(backlog: 10));

        kinds.Should().ContainInOrder("waiting-of-job", "triggers-by-keys");
        kinds.Should().NotContain("misfired-of-job",
            "the store does not know what a subclass overrides, and the read of the late triggers would bypass its overrides of the read it replaces");
    }

    /// <summary>
    /// The completion settles exactly the triggers the misfire handler would: late by their own threshold
    /// or the store's, not ignoring misfires, and waiting rather than paused.
    /// </summary>
    [Test]
    public async Task TheCompletionAppliesThePolicyOfExactlyTheLateTriggersItLetsGo()
    {
        IJobDetail job = SerialJob();
        DateTimeOffset start = clock.GetUtcNow();
        IOperableTrigger running = Repeating("running", job, start);
        await store.ScheduleJob(job, running);
        TriggerFiredBundle firing = await FireOne(running.Key);

        // The firing runs for ten minutes. Everything below is stored while it runs, and so stored held.
        DateTimeOffset end = start + TimeSpan.FromMinutes(10);

        IOperableTrigger fireNow = await Add(OneShot("fire-now", job, start.AddMinutes(1), SimpleTriggerMisfireInstruction.FireNow));
        IOperableTrigger spent = await Add(OneShot("spent", job, start.AddMinutes(1), SimpleTriggerMisfireInstruction.NextWithRemainingCount));
        IOperableTrigger due = await Add(OneShot("due", job, end - TimeSpan.FromSeconds(30), SimpleTriggerMisfireInstruction.FireNow));
        IOperableTrigger paused = await Add(OneShot("paused", job, start.AddMinutes(1), SimpleTriggerMisfireInstruction.FireNow));
        IOperableTrigger ignoring = await Add(OneShot("ignoring", job, start.AddMinutes(1), SimpleTriggerMisfireInstruction.IgnoreMisfires));
        IOperableTrigger tight = await Add(OneShot("tight", job, end - TimeSpan.FromSeconds(30), SimpleTriggerMisfireInstruction.FireNow, TimeSpan.FromSeconds(5)));
        IOperableTrigger tolerant = await Add(OneShot("tolerant", job, end - TimeSpan.FromMinutes(2), SimpleTriggerMisfireInstruction.FireNow, TimeSpan.FromMinutes(10)));

        await store.PauseTrigger(paused.Key);

        (await StoredState(fireNow.Key)).Should().Be(AdoConstants.StateBlocked, "the premise: a trigger stored while its serial job runs is held");
        (await StoredState(paused.Key)).Should().Be(AdoConstants.StatePausedBlocked, "the premise: pausing a held trigger keeps it held");

        clock.Advance(end - start);
        await Complete(firing);

        signals.Misfired.Should().BeEquivalentTo([fireNow.Key, spent.Key, tight.Key],
            "those three are late by the threshold that applies to each; nothing else is, or asks for a misfire");
        signals.Finalized.Should().Equal([spent.Key], "the misfire left that trigger nothing to fire");

        (await NextFireTime(fireNow.Key)).Should().Be(end, "nine minutes late, its policy fires it now");
        (await StoredState(fireNow.Key)).Should().Be(AdoConstants.StateWaiting);

        (await store.GetTrigger(spent.Key)).Should().BeNull(
            "a trigger whose misfire leaves it nothing to fire is deleted rather than left COMPLETE (#1646)");

        (await NextFireTime(due.Key)).Should().Be(end - TimeSpan.FromSeconds(30), "thirty seconds is inside the store's minute");
        (await StoredState(due.Key)).Should().Be(AdoConstants.StateWaiting);

        (await NextFireTime(paused.Key)).Should().Be(start.AddMinutes(1),
            "a paused trigger accrues no misfire; resuming it is what settles the debt");
        (await StoredState(paused.Key)).Should().Be(AdoConstants.StatePaused, "let go of, and still paused");

        (await NextFireTime(ignoring.Key)).Should().Be(start.AddMinutes(1),
            "a trigger that ignores misfires keeps its missed fire time, for acquisition to fire it late as it asks");
        (await StoredState(ignoring.Key)).Should().Be(AdoConstants.StateWaiting);

        (await NextFireTime(tight.Key)).Should().Be(end, "thirty seconds is past the five it allows itself, so its policy fires it now");

        (await NextFireTime(tolerant.Key)).Should().Be(end - TimeSpan.FromMinutes(2),
            "two minutes is past the store's minute but inside the ten it allows itself");
        (await StoredState(tolerant.Key)).Should().Be(AdoConstants.StateWaiting);

        (await NextFireTime(running.Key)).Should().Be(start.AddHours(1), "the firing that ran moved its own trigger on");
        (await StoredState(running.Key)).Should().Be(AdoConstants.StateWaiting);
    }

    /// <summary>
    /// A trigger that ignores misfires, let go late, is the misfire handler's business no more than it was
    /// while held: acquisition takes it late, as it asks.
    /// </summary>
    [Test]
    public async Task ALateTriggerThatIgnoresMisfiresIsFiredLateRatherThanReportedMisfired()
    {
        IJobDetail job = SerialJob();
        DateTimeOffset start = clock.GetUtcNow();
        IOperableTrigger running = Repeating("running", job, start);
        await store.ScheduleJob(job, running);
        TriggerFiredBundle firing = await FireOne(running.Key);

        IOperableTrigger ignoring = await Add(OneShot("ignoring", job, start.AddMinutes(1), SimpleTriggerMisfireInstruction.IgnoreMisfires));

        clock.Advance(TimeSpan.FromMinutes(10));
        await Complete(firing);

        signals.Misfired.Should().BeEmpty("the trigger asked for its misfires to be ignored");
        (await store.RecoverMisfires(Guid.NewGuid())).ProcessedMisfiredTriggerCount.Should().Be(0,
            "the misfire handler leaves it alone too");

        TriggerFiredBundle late = await FireOne(ignoring.Key);
        late.ScheduledFireTimeUtc.Should().Be(start.AddMinutes(1), "it fires the occurrence it missed, nine minutes late");
    }

    //---------------------------------------------------------------------------
    // One completion, counted
    //---------------------------------------------------------------------------

    /// <summary>
    /// Stores a serial job with a repeating trigger and <paramref name="backlog" /> one-shots queued behind
    /// it, fires the repeating one, lets the backlog come due inside the store's threshold, and returns the
    /// statements of the firing's completion.
    /// </summary>
    private async Task<List<string>> CompletionStatements(int backlog)
    {
        await store.Clear();
        signals.Clear();

        IJobDetail job = SerialJob();
        DateTimeOffset now = clock.GetUtcNow();
        IOperableTrigger running = Repeating("running", job, now);

        List<IOperableTrigger> triggers = [running];
        for (int i = 0; i < backlog; i++)
        {
            triggers.Add(OneShot("queued-" + i.ToString("D3", CultureInfo.InvariantCulture), job, now.AddSeconds(1), SimpleTriggerMisfireInstruction.FireNow));
        }

        await store.ScheduleJobs(new Dictionary<IJobDetail, IReadOnlyCollection<IOperableTrigger>> { [job] = triggers }, ScheduleJobOptions.Replacing);

        TriggerFiredBundle firing = await FireOne(running.Key);
        (await CountInState(AdoConstants.StateBlocked)).Should().Be(backlog + 1, "the firing holds back its own trigger and the whole backlog");

        // The backlog came due while the firing ran, and is still inside the store's threshold when it ends.
        clock.Advance(TimeSpan.FromSeconds(2));

        provider.Reset();
        await Complete(firing);
        List<string> statements = provider.RoundTrips.Select(x => x.Statements[0]).ToList();

        TestContext.Out.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{(shipped ? "shipped" : "own")} delegate, backlog {backlog}: {statements.Count} statements: {string.Join(", ", Kinds(statements))}"));

        (await CountInState(AdoConstants.StateWaiting)).Should().Be(backlog + 1, "the completion lets go of every trigger of the job");
        signals.Misfired.Should().BeEmpty("the premise: nothing in the backlog is late");
        return statements;
    }

    private static void IgnoreUnless(bool applies, string reason)
    {
        if (!applies)
        {
            Assert.Ignore(reason);
        }
    }

    private static List<string> Kinds(List<string> statements) => statements.Select(Kind).ToList();

    /// <summary>Names a statement by the constant it was built from.</summary>
    private static string Kind(string sql)
    {
        if (sql == Sql(StdAdoConstants.SqlSelectMisfiredTriggersOfJobInState))
        {
            return "misfired-of-job";
        }

        if (sql == Sql(StdAdoConstants.SqlSelectTriggersForJobInState))
        {
            return "waiting-of-job";
        }

        if (sql.StartsWith(Sql(StdAdoConstants.SqlSelectTriggersByKeysPrefix), StringComparison.Ordinal))
        {
            return "triggers-by-keys";
        }

        if (sql == Sql(StdAdoConstants.SqlSelectAwaitingContinuations))
        {
            return "awaiting";
        }

        if (sql == Sql(StdAdoConstants.SqlUpdateJobTriggerStatesFromOtherState))
        {
            return "unblock";
        }

        if (sql == Sql(StdAdoConstants.SqlDeleteFiredTrigger))
        {
            return "delete-fired-by-entry";
        }

        return "other:" + string.Join(' ', sql.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Sql(string constant) => AdoJobStoreUtil.ReplaceTablePrefixCached(constant, AdoConstants.DefaultTablePrefix);

    //---------------------------------------------------------------------------
    // The store, driven by hand
    //---------------------------------------------------------------------------

    private static IJobDetail SerialJob()
    {
        return JobBuilder.Create<SerialJobUnderTest>().WithIdentity("serial", Group).StoreDurably().Build();
    }

    private IOperableTrigger Repeating(string name, IJobDetail job, DateTimeOffset startAt)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(startAt)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

        trigger.ComputeFirstFireTimeUtc(calendar: null);
        return trigger;
    }

    private IOperableTrigger OneShot(
        string name,
        IJobDetail job,
        DateTimeOffset startAt,
        SimpleTriggerMisfireInstruction instruction,
        TimeSpan? threshold = null)
    {
        TriggerBuilder<IJob> builder = TriggerBuilder.Create(clock)
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(startAt)
            .WithSimpleSchedule(x => x.WithRepeatCount(0).WithMisfireInstruction(instruction));

        if (threshold is { } value)
        {
            builder = builder.WithMisfireThreshold(value);
        }

        IOperableTrigger trigger = (IOperableTrigger) builder.Build();
        trigger.ComputeFirstFireTimeUtc(calendar: null);
        return trigger;
    }

    private async Task<IOperableTrigger> Add(IOperableTrigger trigger)
    {
        await store.AddTrigger(trigger);
        return trigger;
    }

    /// <summary>
    /// One round that acquires and fires what is due, which must be the one trigger named.
    /// </summary>
    private async Task<TriggerFiredBundle> FireOne(TriggerKey expected)
    {
        TriggerAcquisitionResult round = await store.AcquireNextTriggersAndFireDue(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddSeconds(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero,
        });

        TriggerFiredBundle? bundle = round.Fired.Should().ContainSingle().Subject.TriggerFiredBundle;
        bundle.Should().NotBeNull("a firing has to start before it can complete");
        bundle!.Trigger.Key.Should().Be(expected, "the trigger under test is the one due first");
        return bundle;
    }

    private Task Complete(TriggerFiredBundle firing)
    {
        return store.FiringComplete(new TriggeredJobCompleteContext
        {
            Trigger = firing.Trigger,
            JobDetail = firing.JobDetail,
            Instruction = SchedulerInstruction.NoInstruction,
            Outcome = ExecutionOutcome.Succeeded,
        }).AsTask();
    }

    //---------------------------------------------------------------------------
    // The rows
    //---------------------------------------------------------------------------

    private async Task<string?> StoredState(TriggerKey key)
    {
        return (string?) await Column("TRIGGER_STATE", key);
    }

    private async Task<DateTimeOffset?> NextFireTime(TriggerKey key)
    {
        return await Column("NEXT_FIRE_TIME", key) is long ticks ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
    }

    private async Task<object?> Column(string column, TriggerKey key)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name AND TRIGGER_GROUP = @group";
        command.Parameters.AddWithValue("@name", key.Name);
        command.Parameters.AddWithValue("@group", key.Group);
        object? value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    private async Task<long> CountInState(string state)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM QRTZ_TRIGGERS WHERE TRIGGER_STATE = @state";
        command.Parameters.AddWithValue("@state", state);
        return (long) (await command.ExecuteScalarAsync())!;
    }

    private static string LoadSqliteTableScript()
    {
        string path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        return File.ReadAllText(path);
    }

    /// <summary>
    /// The shipped SQLite delegate as a type of somebody's own, which the store does not trust to answer
    /// the statements only Quartz's own delegates carry.
    /// </summary>
    public sealed class OwnSqliteDelegate : SQLiteDelegate;

    /// <summary>
    /// Records what the store told the scheduler about.
    /// </summary>
    private sealed class RecordingSignaler : ISchedulerSignaler
    {
        public List<TriggerKey> Misfired { get; } = [];

        public List<TriggerKey> Finalized { get; } = [];

        public void Clear()
        {
            Misfired.Clear();
            Finalized.Clear();
        }

        public ValueTask NotifyTriggerListenersMisfired(ITrigger trigger, CancellationToken cancellationToken = default)
        {
            Misfired.Add(trigger.Key);
            return default;
        }

        public ValueTask NotifySchedulerListenersFinalized(ITrigger trigger, CancellationToken cancellationToken = default)
        {
            Finalized.Add(trigger.Key);
            return default;
        }

        public ValueTask NotifySchedulerListenersJobDeleted(JobKey jobKey, CancellationToken cancellationToken = default) => default;

        public ValueTask SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersError(SchedulerErrorContext errorContext, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>Never executed: these tests drive the store, not a scheduler.</summary>
    [DisallowConcurrentExecution]
    public sealed class SerialJobUnderTest : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
