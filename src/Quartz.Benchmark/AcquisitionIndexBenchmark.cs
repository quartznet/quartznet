using System.Diagnostics;
using System.Globalization;
using System.Text;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Benchmark;

/// <summary>
/// The shape of the acquisition index the arm under measurement puts on <c>QRTZ_TRIGGERS</c>.
/// </summary>
/// <remarks>
/// Every arm redefines the shipped index under its shipped name rather than adding a second one beside
/// it, because <c>MySQLDelegate</c> pins the acquisition statement to that name with
/// <c>FORCE INDEX (IDX_*_T_NFT_ST)</c>: on MySQL an index under any other name cannot be reached by
/// the statement at all, so redefining the named one is the only change MySQL could ever adopt.
/// </remarks>
public enum AcquisitionIndexShape
{
    /// <summary>What <c>database/tables/</c> ships: <c>(SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME)</c>.</summary>
    Shipped,

    /// <summary><c>PRIORITY</c> appended ascending, which is the change the issue proposes.</summary>
    TrailingPriority,

    /// <summary>
    /// <c>PRIORITY</c> appended descending. The acquisition statement orders by
    /// <c>NEXT_FIRE_TIME ASC, PRIORITY DESC</c>, and only an index whose columns are sorted the same
    /// two ways can deliver that order without a sort — an ascending trailing column cannot, on any of
    /// these engines. If appending <c>PRIORITY</c> is ever going to pay, this is the form that does it.
    /// </summary>
    TrailingPriorityDescending,

    /// <summary>
    /// The shipped acquisition index, plus <c>(SCHED_NAME, PREFERRED_NODE, PREFERRED_NODE_AUTO)</c> for
    /// the node-affinity paths.
    /// </summary>
    PreferredNode,
}

/// <summary>
/// What the trigger indexes cost the acquisition round trip on a populated schema: the candidate select
/// under each candidate index shape, and the failover re-pin that <c>ClusterRecover</c> issues for every
/// node it finds dead.
/// </summary>
/// <remarks>
/// <para>
/// This is the harness for the index audit in issue #3426. Two columns the shipped indexes do not
/// mention are read on hot paths — <c>PRIORITY</c>, the second <c>ORDER BY</c> key of the acquisition
/// statement, and <c>PREFERRED_NODE</c>, which node affinity filters and re-pins on — and the question
/// is whether covering either of them is worth the write cost an index puts on the table every firing
/// updates. An index that does not pay for itself is not free, so the answer has to be measured.
/// </para>
/// <para>
/// <b>Running it.</b> Start the databases yourself and point the benchmark at them — a container per
/// benchmark case is not a thing BenchmarkDotNet's process-per-case model can give you:
/// </para>
/// <code>
/// docker run -d --name quartz-bench-pg -p 55432:5432 \
///   -e POSTGRES_DB=quartznet -e POSTGRES_USER=quartznet -e POSTGRES_PASSWORD=quartznet postgres:15.1
/// docker run -d --name quartz-bench-mssql -p 51433:1433 \
///   -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='Quartz!DockerP4ss' \
///   mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04
/// docker run -d --name quartz-bench-mysql -p 53306:3306 \
///   -e MYSQL_DATABASE=quartznet -e MYSQL_ROOT_PASSWORD=quartznet mysql:8.0
///
/// QUARTZ_BENCHMARK_POSTGRES='Host=localhost;Port=55432;Database=quartznet;Username=quartznet;Password=quartznet'
/// QUARTZ_BENCHMARK_SQLSERVER='Server=localhost,51433;User ID=sa;Password=Quartz!DockerP4ss;TrustServerCertificate=true'
/// QUARTZ_BENCHMARK_MYSQL='Server=localhost;Port=53306;Database=quartznet;User ID=root;Password=quartznet'
///
/// dotnet run -c Release --project src/Quartz.Benchmark -- --filter '*AcquisitionIndex*'
/// </code>
/// <para>
/// Setting <c>QUARTZ_BENCHMARK_EXPLAIN=1</c> makes each parameter set print three things before
/// measuring anything: the statement it is about to measure, the engine's executed plan for it, and a
/// thousand acquisitions timed one at a time as percentiles. The plan is the other half of the answer —
/// a round trip that did not move because the plan did not change is a different finding from one that
/// did not move because the sort was already cheap — and the percentiles are the half BenchmarkDotNet
/// cannot report, because what it summarises is the distribution of iteration averages rather than the
/// spread of a single round trip.
/// </para>
/// <para>
/// <b>The population.</b> A hundred thousand triggers over two hundred groups and a thousand jobs,
/// which is a large cluster's schedule rather than a toy one. Most are <c>WAITING</c> with a fire time
/// spread over the coming day; the rest sit in the states a live scheduler always has some rows in, so
/// that the <c>(SCHED_NAME, TRIGGER_STATE)</c> prefix has real work to do. Five per cent carry a
/// <c>PREFERRED_NODE</c> pin, half of them to nodes that are still checking in and half to nodes that
/// are not, so the correlated liveness subquery in the acquisition filter is not vacuous.
/// </para>
/// <para>
/// <b><see cref="Candidates" />.</b> How many rows satisfy the whole acquisition predicate, which is
/// what the <c>ORDER BY</c> has to order and therefore the only thing that decides whether an index
/// could retire a sort. It is varied by moving the window's upper bound rather than by reseeding, so
/// one population serves every parameter set and a sweep is minutes rather than hours; the statement
/// only ever sees the bound, so narrowing the window and thinning the data are the same question asked
/// twice.
/// </para>
/// <para>
/// The candidates arrive in groups of a hundred sharing one fire time, because that is the shape a
/// schedule actually has: cron triggers on the same expression become due at the same instant, to the
/// tick. Ties are the whole reason <c>PRIORITY</c> is in the <c>ORDER BY</c>, and they are the case
/// where an index that cannot deliver the ordering has the most to sort — so this is the sharp end of
/// the question rather than the average one.
/// </para>
/// <para>
/// <b>The clock.</b> Fire times and the acquisition window are both anchored to a fixed instant rather
/// than to <c>now</c>, so the same seed is valid in the next process and the numbers repeat.
/// </para>
/// <para>
/// <b><see cref="RepinFromDeadNode" />.</b> The statement <c>ClusterRecover</c> runs once per dead node
/// to release its auto-claimed pins. It is measured in the state it is overwhelmingly in — a node whose
/// pins have already been released, so nothing matches — which is deliberately the case most flattering
/// to an index: proving that no row matches is all a seek has to do, while a scan still reads the
/// table. If an index cannot win here it cannot win anywhere on this path.
/// </para>
/// </remarks>
[SimpleJob(RunStrategy.Throughput, warmupCount: 3, iterationCount: 10)]
[BenchmarkCategory(BenchmarkCategories.RequiresDatabase, BenchmarkCategories.LongRunning)]
public class AcquisitionIndexBenchmark
{
    private const string SchedulerName = "BenchmarkScheduler";
    private const string InstanceId = "NODE-01";
    private const string TablePrefix = "QRTZ_";

    private const string AcquisitionIndexName = "IDX_QRTZ_T_NFT_ST";
    private const string PreferredNodeIndexName = "IDX_QRTZ_T_PN";

    private const int TotalTriggers = 100_000;
    private const int TriggerGroups = 200;
    private const int Jobs = 1_000;

    /// <summary>Triggers carrying a <c>PREFERRED_NODE</c> pin, spread over ten node names.</summary>
    private const int PinnedTriggers = 5_000;

    private const int PinNodes = 10;

    /// <summary>How many of <see cref="PinNodes" /> are still checking in.</summary>
    private const int LiveNodes = 5;

    /// <summary>A batch size a cluster tuned for throughput runs with; the shipped default is one.</summary>
    private const int BatchSize = 20;

    /// <summary>Triggers seeded inside the acquisition window, in <see cref="TiedCandidates" />-sized ties.</summary>
    private const int DueTriggers = 5_000;

    /// <summary>How many triggers share one fire time, as a cron expression's worth of them does.</summary>
    private const int TiedCandidates = 100;

    /// <summary>
    /// Everything the seed and the acquisition window are measured from. A constant rather than
    /// <c>now</c>, so that a seed written by one benchmark process is still the right seed for the next
    /// one and the numbers are comparable across a sweep.
    /// </summary>
    private static readonly DateTimeOffset seedEpoch = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The first, and already slightly late, of the due fire times.</summary>
    private static readonly DateTimeOffset dueBandStart = seedEpoch.AddSeconds(-10);

    /// <summary>The default misfire threshold, which is how far back a misfiring trigger is still acquirable.</summary>
    private static readonly DateTimeOffset noEarlierThan = seedEpoch.AddSeconds(-60);

    private static readonly DateTimeOffset liveNodeCutoff = seedEpoch.AddSeconds(-30);

    [Params(BenchmarkDialect.Postgres, BenchmarkDialect.SqlServer, BenchmarkDialect.MySql)]
    public BenchmarkDialect Dialect { get; set; } = BenchmarkDialect.Postgres;

    [Params(
        AcquisitionIndexShape.Shipped,
        AcquisitionIndexShape.TrailingPriority,
        AcquisitionIndexShape.TrailingPriorityDescending,
        AcquisitionIndexShape.PreferredNode)]
    public AcquisitionIndexShape Index { get; set; } = AcquisitionIndexShape.Shipped;

    /// <summary>Rows satisfying the whole acquisition predicate, and so the size of the sort's input.</summary>
    [Params(TiedCandidates, DueTriggers)]
    public int Candidates { get; set; }

    /// <summary>The upper bound that admits exactly <see cref="Candidates" /> of the seeded rows.</summary>
    private DateTimeOffset NoLaterThan => FireTime(Candidates - 1);

    /// <summary>Where the seed puts the <paramref name="index" />th due trigger's fire time.</summary>
    private static DateTimeOffset FireTime(int index) => dueBandStart.AddSeconds(index / TiedCandidates);

    private BenchmarkDatabase database = null!;
    private StdAdoDelegate driverDelegate = null!;
    private TriggerAcquisitionCriteria singleCriteria = null!;
    private TriggerAcquisitionCriteria batchCriteria = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        database = await BenchmarkDatabase.Open(Dialect).ConfigureAwait(false);
        driverDelegate = database.CreateDelegate(SchedulerName, InstanceId, TablePrefix);

        await Seed().ConfigureAwait(false);
        await ApplyIndexes().ConfigureAwait(false);

        singleCriteria = new TriggerAcquisitionCriteria
        {
            NoLaterThan = NoLaterThan,
            NoEarlierThan = noEarlierThan,
            MaxCount = 1,
            LiveNodeCutoff = liveNodeCutoff,
        };
        batchCriteria = singleCriteria with { MaxCount = BatchSize };

        if (Environment.GetEnvironmentVariable("QUARTZ_BENCHMARK_EXPLAIN") is { Length: > 0 })
        {
            await Explain().ConfigureAwait(false);
            await Percentiles().ConfigureAwait(false);
        }
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await database.DisposeAsync().ConfigureAwait(false);
    }

    // No baseline is declared. BenchmarkDotNet's ratio column compares methods within a parameter set,
    // and these three are different statements rather than three ways of doing one thing; the
    // comparison that matters is one method across the Index parameter, which the ratio column cannot
    // express. Reading the Mean column down an Index sweep is the whole of it.

    /// <summary>One acquisition attempt at the shipped batch size of one, where a sort is worst placed.</summary>
    [Benchmark]
    public async Task<int> Acquire()
    {
        List<TriggerAcquireResult> results = await driverDelegate.SelectTriggersToAcquire(database.Holder, singleCriteria).ConfigureAwait(false);
        return results.Count;
    }

    /// <summary>The same at a batch of twenty, which is what a cluster tuned for throughput asks for.</summary>
    [Benchmark]
    public async Task<int> AcquireBatch()
    {
        List<TriggerAcquireResult> results = await driverDelegate.SelectTriggersToAcquire(database.Holder, batchCriteria).ConfigureAwait(false);
        return results.Count;
    }

    /// <summary>The failover re-pin, in the state it spends nearly all of its life in.</summary>
    [Benchmark]
    public async Task<int> RepinFromDeadNode()
    {
        return await driverDelegate.RepinTriggersFromDeadNode(database.Holder, "NODE-NOT-PINNED", PreferredNode.AutoSentinel).ConfigureAwait(false);
    }

    /// <summary>
    /// Prints the statement about to be measured and the engine's plan for it. Runs once per parameter
    /// set, before anything is measured.
    /// </summary>
    private async Task Explain()
    {
        string acquisitionSql = ((IAcquisitionSqlSource) driverDelegate).AcquisitionSql(1);

        Console.WriteLine($"=== {Dialect} / {Index} / candidates={Candidates} ===");
        Console.WriteLine(acquisitionSql);

        Dictionary<string, object?> parameters = new(StringComparer.Ordinal)
        {
            ["schedulerName"] = SchedulerName,
            ["state"] = StoredTriggerState.Waiting.ToStoredValue(),
            ["noLaterThan"] = NoLaterThan.UtcTicks,
            ["noEarlierThan"] = noEarlierThan.UtcTicks,
            ["instanceId"] = InstanceId,
            ["autoPinSentinel"] = PreferredNode.AutoSentinel,
            ["liveNodeCutoff"] = liveNodeCutoff.UtcTicks,
        };

        Console.WriteLine("--- acquisition plan ---");
        foreach (string line in await database.Explain(acquisitionSql, parameters).ConfigureAwait(false))
        {
            Console.WriteLine(line);
        }

        Console.WriteLine("--- re-pin plan ---");
        Dictionary<string, object?> repinParameters = new(StringComparer.Ordinal)
        {
            ["newPreferredNode"] = PreferredNode.AutoSentinel,
            ["newPreferredNodeAuto"] = false,
            ["schedulerName"] = SchedulerName,
            ["oldPreferredNode"] = "NODE-NOT-PINNED",
            ["oldPreferredNodeAuto"] = true,
        };

        string repinSql = StdAdoConstants.SqlRepinTriggersFromDeadNode.Replace("{0}", TablePrefix);
        foreach (string line in await database.Explain(repinSql, repinParameters).ConfigureAwait(false))
        {
            Console.WriteLine(line);
        }

        Console.WriteLine("=== end plans ===");
    }

    /// <summary>
    /// A thousand acquisitions timed one at a time, reported as percentiles of the single round trip.
    /// </summary>
    /// <remarks>
    /// The tail is the interesting half of an index question. A plan that reads one page and a plan that
    /// reads the whole table have means a millisecond of transport apart on a loopback database and
    /// ninety-fifth percentiles that are nothing like each other, and it is the tail a scheduler thread
    /// waits on. BenchmarkDotNet cannot say this: an iteration there is however many operations fit in
    /// its target time, so what it summarises is the spread of iteration averages.
    /// </remarks>
    private async Task Percentiles()
    {
        const int Warmup = 50;
        const int Samples = 1_000;

        for (int i = 0; i < Warmup; i++)
        {
            await driverDelegate.SelectTriggersToAcquire(database.Holder, singleCriteria).ConfigureAwait(false);
        }

        double[] microseconds = new double[Samples];
        for (int i = 0; i < Samples; i++)
        {
            long started = Stopwatch.GetTimestamp();
            await driverDelegate.SelectTriggersToAcquire(database.Holder, singleCriteria).ConfigureAwait(false);
            microseconds[i] = Stopwatch.GetElapsedTime(started).TotalMicroseconds;
        }

        Array.Sort(microseconds);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"--- {Samples} acquisitions, us: p50={Percentile(microseconds, 0.50):F1} p90={Percentile(microseconds, 0.90):F1} p95={Percentile(microseconds, 0.95):F1} p99={Percentile(microseconds, 0.99):F1} max={microseconds[^1]:F1}"));
    }

    private static double Percentile(double[] sorted, double fraction)
    {
        return sorted[Math.Clamp((int) (sorted.Length * fraction), 0, sorted.Length - 1)];
    }

    /// <summary>
    /// Puts the acquisition index into the shape this arm asks for. Always dropped and recreated, so a
    /// process inherits nothing from the one before it, and the statistics are refreshed afterwards
    /// because a plan chosen from a freshly indexed table with no statistics is not the plan a
    /// deployment gets.
    /// </summary>
    private async Task ApplyIndexes()
    {
        await DropIndex(AcquisitionIndexName).ConfigureAwait(false);
        await DropIndex(PreferredNodeIndexName).ConfigureAwait(false);

        string acquisitionColumns = Index switch
        {
            AcquisitionIndexShape.TrailingPriority => "SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME, PRIORITY",
            AcquisitionIndexShape.TrailingPriorityDescending => "SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME ASC, PRIORITY DESC",
            _ => "SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME",
        };

        await database.Execute($"CREATE INDEX {AcquisitionIndexName} ON {TablePrefix}TRIGGERS ({acquisitionColumns})").ConfigureAwait(false);

        if (Index == AcquisitionIndexShape.PreferredNode)
        {
            await database.Execute($"CREATE INDEX {PreferredNodeIndexName} ON {TablePrefix}TRIGGERS (SCHED_NAME, PREFERRED_NODE, PREFERRED_NODE_AUTO)").ConfigureAwait(false);
        }

        await database.UpdateStatistics(TablePrefix + "TRIGGERS").ConfigureAwait(false);
    }

    private async Task DropIndex(string name)
    {
        if (Dialect == BenchmarkDialect.MySql)
        {
            // MySQL has no DROP INDEX ... IF EXISTS outside a stored program.
            long exists = await database.Scalar(
                "SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '"
                + TablePrefix + "TRIGGERS' AND INDEX_NAME = '" + name + "'").ConfigureAwait(false);

            if (exists > 0)
            {
                await database.Execute($"DROP INDEX {name} ON {TablePrefix}TRIGGERS").ConfigureAwait(false);
            }

            return;
        }

        await database.Execute(Dialect == BenchmarkDialect.Postgres
            ? $"DROP INDEX IF EXISTS {name}"
            : $"DROP INDEX IF EXISTS {name} ON {TablePrefix}TRIGGERS").ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the population, unless it is already there. The seed does not depend on any parameter, so
    /// it is written once per database and every later process finds it and moves on; the check is exact
    /// because the seed is anchored to a constant instant rather than to <c>now</c>.
    /// </summary>
    private async Task Seed()
    {
        if (await database.Scalar($"SELECT COUNT(*) FROM {TablePrefix}TRIGGERS").ConfigureAwait(false) == TotalTriggers
            && await database.Scalar(string.Create(CultureInfo.InvariantCulture,
                $"SELECT COUNT(*) FROM {TablePrefix}TRIGGERS WHERE TRIGGER_STATE = 'WAITING' AND NEXT_FIRE_TIME <= {FireTime(DueTriggers - 1).UtcTicks}")).ConfigureAwait(false) == DueTriggers)
        {
            return;
        }

        foreach (string table in new[] { "SIMPLE_TRIGGERS", "SIMPROP_TRIGGERS", "CRON_TRIGGERS", "BLOB_TRIGGERS", "TRIGGERS", "JOB_DETAILS", "SCHEDULER_STATE", "FIRED_TRIGGERS" })
        {
            await database.Execute("DELETE FROM " + TablePrefix + table).ConfigureAwait(false);
        }

        await SeedJobs().ConfigureAwait(false);
        await SeedSchedulerState().ConfigureAwait(false);
        await SeedTriggers().ConfigureAwait(false);
        await database.UpdateStatistics(TablePrefix + "JOB_DETAILS").ConfigureAwait(false);
    }

    private async Task SeedJobs()
    {
        List<string> rows = new(Jobs);
        for (int i = 0; i < Jobs; i++)
        {
            rows.Add($"('{SchedulerName}', 'job{i}', 'jobs', 'Quartz.Job.NoOpJob, Quartz', {database.True}, {database.False}, {database.False}, {database.False})");
        }

        await InsertRows(
            $"INSERT INTO {TablePrefix}JOB_DETAILS (SCHED_NAME, JOB_NAME, JOB_GROUP, JOB_CLASS_NAME, IS_DURABLE, IS_NONCONCURRENT, IS_UPDATE_DATA, REQUESTS_RECOVERY) VALUES ",
            rows).ConfigureAwait(false);
    }

    /// <summary>
    /// Half the pin targets are checking in and half are not, so the liveness subquery in the
    /// acquisition filter both matches and fails to match on real data.
    /// </summary>
    private async Task SeedSchedulerState()
    {
        List<string> rows = new(PinNodes + 1);
        rows.Add(string.Create(CultureInfo.InvariantCulture,
            $"('{SchedulerName}', '{InstanceId}', {seedEpoch.UtcTicks}, 15000)"));

        for (int i = 0; i < PinNodes; i++)
        {
            long checkin = i < LiveNodes ? seedEpoch.UtcTicks : seedEpoch.AddHours(-1).UtcTicks;
            rows.Add(string.Create(CultureInfo.InvariantCulture, $"('{SchedulerName}', 'PIN-{i}', {checkin}, 15000)"));
        }

        await InsertRows(
            $"INSERT INTO {TablePrefix}SCHEDULER_STATE (SCHED_NAME, INSTANCE_NAME, LAST_CHECKIN_TIME, CHECKIN_INTERVAL) VALUES ",
            rows).ConfigureAwait(false);
    }

    private async Task SeedTriggers()
    {
        List<string> rows = new(TotalTriggers);
        for (int i = 0; i < TotalTriggers; i++)
        {
            bool due = i < DueTriggers;

            // The due band starts before the epoch and runs past it, so a window over it holds triggers
            // that are already late beside triggers that are not yet due, as a waking node's does. Rows
            // outside the band are a day's schedule, well past any window measured here.
            long nextFireTime = due
                ? FireTime(i).UtcTicks
                : seedEpoch.AddMinutes(2).UtcTicks + (long) (i % 86_400) * TimeSpan.TicksPerSecond;

            string state = due ? "WAITING" : NotDueState(i);

            // Priorities vary within each tie group, so the DESC tie-break on PRIORITY decides which row
            // a batch of one comes back with, rather than being a column that happens to hold one value.
            int priority = due ? 1 + i % 10 : 5;

            string preferredNode = i % (TotalTriggers / PinnedTriggers) == 0
                ? $"'PIN-{i % PinNodes}'"
                : "NULL";
            string preferredNodeAuto = preferredNode == "NULL" ? database.False : database.True;

            rows.Add(string.Create(CultureInfo.InvariantCulture,
                $"('{SchedulerName}', 'trigger{i}', 'group{i % TriggerGroups}', 'job{i % Jobs}', 'jobs', '{state}', 'SIMPLE', {seedEpoch.UtcTicks}, {nextFireTime}, {priority}, 0, {preferredNode}, {preferredNodeAuto})"));
        }

        await InsertRows(
            $"INSERT INTO {TablePrefix}TRIGGERS (SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP, JOB_NAME, JOB_GROUP, TRIGGER_STATE, TRIGGER_TYPE, START_TIME, NEXT_FIRE_TIME, PRIORITY, MISFIRE_INSTR, PREFERRED_NODE, PREFERRED_NODE_AUTO) VALUES ",
            rows).ConfigureAwait(false);
    }

    /// <summary>
    /// The states a live scheduler always has some rows in, in roughly the proportions it has them, so
    /// that <c>TRIGGER_STATE</c> is a column worth indexing rather than a constant.
    /// </summary>
    private static string NotDueState(int i)
    {
        return (i % 100) switch
        {
            < 85 => "WAITING",
            < 93 => "ACQUIRED",
            < 98 => "PAUSED",
            _ => "BLOCKED",
        };
    }

    /// <summary>
    /// Sends the rows as multi-row <c>INSERT</c> statements. A hundred thousand single-row statements is
    /// most of a sweep's runtime; five hundred rows to a statement is inside SQL Server's thousand-row
    /// limit on a <c>VALUES</c> list and turns that into a few seconds.
    /// </summary>
    private async Task InsertRows(string prefix, List<string> rows)
    {
        const int RowsPerStatement = 500;

        List<string> statements = new(rows.Count / RowsPerStatement + 1);
        StringBuilder statement = new();
        for (int i = 0; i < rows.Count; i++)
        {
            statement.Append(statement.Length == 0 ? prefix : ", ").Append(rows[i]);
            if ((i + 1) % RowsPerStatement == 0 || i == rows.Count - 1)
            {
                statements.Add(statement.ToString());
                statement.Clear();
            }
        }

        await database.ExecuteBatched(statements, chunkSize: 1).ConfigureAwait(false);
    }
}
