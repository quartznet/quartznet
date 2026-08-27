using System.Globalization;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Benchmark;

/// <summary>
/// What a cluster-wide execution ceiling costs per acquisition attempt: the aggregate over
/// <c>QRTZ_FIRED_TRIGGERS</c> that counts in-flight work, measured beside the candidate select it is
/// an extra round trip on top of.
/// </summary>
/// <remarks>
/// <para>
/// The ceiling is opt-in. <c>AdoJobStoreBase</c> reads the aggregate only when the configured limits
/// contain a cluster-scoped one, so a deployment that declares none issues no extra statement at all.
/// What is measured here is therefore how well the feature scales for those who ask for it, and
/// whether an index on <c>EXECUTION_GROUP</c> would earn the write cost it puts on a table every
/// firing inserts into and deletes from.
/// </para>
/// <para>
/// <b>Running it.</b> Start the two databases yourself and point the benchmark at them — a container
/// per benchmark case is not a thing BenchmarkDotNet's process-per-case model can give you:
/// </para>
/// <code>
/// docker run -d --name quartz-bench-pg -p 55432:5432 \
///   -e POSTGRES_DB=quartznet -e POSTGRES_USER=quartznet -e POSTGRES_PASSWORD=quartznet postgres:15.1
/// docker run -d --name quartz-bench-mssql -p 51433:1433 \
///   -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='Quartz!DockerP4ss' \
///   mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04
///
/// QUARTZ_BENCHMARK_POSTGRES='Host=localhost;Port=55432;Database=quartznet;Username=quartznet;Password=quartznet'
/// QUARTZ_BENCHMARK_SQLSERVER='Server=localhost,51433;User ID=sa;Password=Quartz!DockerP4ss;TrustServerCertificate=true'
/// </code>
/// <para>
/// The schema comes from <c>database/tables/</c> and is applied on first use by
/// <see cref="BenchmarkDatabase" />, so the tables and their indexes are the ones a real deployment
/// has. Seeding is skipped when the tables already hold what this parameter set wants, which is what
/// keeps a sweep of this size to minutes rather than hours.
/// </para>
/// <para>
/// <b>Row counts.</b> <c>FIRED_TRIGGERS</c> holds one row per reservation or running execution, so its
/// size is bounded by cluster-wide concurrency and job duration rather than by how many triggers are
/// scheduled. Ten rows is a small cluster with a few jobs running; a hundred is around ten nodes with
/// their default thread pools saturated; a thousand is a large cluster — fifty nodes at twenty
/// concurrent jobs each — with every slot busy; ten thousand is past what the thread pools of a
/// realistic cluster can hold in flight at once, and stands in for a cluster that has been losing
/// nodes faster than <c>ClusterRecover</c> has been cleaning up after them.
/// </para>
/// <para>
/// <b>Group counts.</b> The result set is bounded by the number of distinct
/// <c>(EXECUTION_GROUP, TRIGGER_GROUP)</c> pairs in flight, which is what the <c>GROUP BY</c> has to
/// hash or sort, so that is swept separately from the row count.
/// </para>
/// <para>
/// <b>Reading the numbers.</b> <see cref="AcquireCandidates" /> is the denominator — one acquisition
/// attempt's candidate select at the default <c>MaxBatchSize</c> of one, where an extra round trip is
/// proportionally worst — and <see cref="AcquireCandidatesBatched" /> is the same at a batch of five.
/// Neither depends on the row count, the group count or the index, so they double as a control: if
/// they drift between parameter sets, the machine drifted rather than the query.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, warmupCount: 3, iterationCount: 10)]
[BenchmarkCategory(BenchmarkCategories.RequiresDatabase)]
public class ExecutionCeilingBenchmark
{
    private const string SchedulerName = "BenchmarkScheduler";
    private const string InstanceId = "NODE-01";
    private const string TablePrefix = "QRTZ_";

    /// <summary>Waiting triggers the candidate select has to choose from.</summary>
    private const int WaitingTriggers = 5_000;

    private const string CoveringIndexName = "IDX_QRTZ_FT_EG_TG";

    [Params(BenchmarkDialect.Postgres, BenchmarkDialect.SqlServer)]
    public BenchmarkDialect Dialect { get; set; } = BenchmarkDialect.Postgres;

    [Params(10, 100, 1_000, 10_000)]
    public int FiredTriggerRows { get; set; }

    [Params(8, 64)]
    public int ExecutionGroups { get; set; }

    /// <summary>Whether a covering <c>(SCHED_NAME, EXECUTION_GROUP, TRIGGER_GROUP)</c> index exists.</summary>
    [Params(false, true)]
    public bool Indexed { get; set; }

    private BenchmarkDatabase database = null!;
    private StdAdoDelegate driverDelegate = null!;
    private TriggerAcquisitionCriteria singleCriteria = null!;
    private TriggerAcquisitionCriteria batchCriteria = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        database = await BenchmarkDatabase.Open(Dialect).ConfigureAwait(false);
        driverDelegate = database.CreateDelegate(SchedulerName, InstanceId, TablePrefix);

        await EnsureWaitingTriggers().ConfigureAwait(false);
        await EnsureFiredTriggers().ConfigureAwait(false);
        await ApplyIndex().ConfigureAwait(false);

        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        singleCriteria = new TriggerAcquisitionCriteria
        {
            NoLaterThan = now.AddSeconds(30),
            NoEarlierThan = now.AddMinutes(-1),
            MaxCount = 1,
            LiveNodeCutoff = now.AddSeconds(-30),
        };
        batchCriteria = singleCriteria with { MaxCount = 5 };
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await database.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>The aggregate the cluster-wide ceiling reads, once per acquisition attempt.</summary>
    [Benchmark]
    public async Task<int> InFlightAggregate()
    {
        List<ExecutionGroupInFlight> counts = await driverDelegate.SelectExecutionGroupsInFlight(database.Holder).ConfigureAwait(false);
        return counts.Count;
    }

    /// <summary>One acquisition attempt's candidate select at the default batch size of one.</summary>
    [Benchmark]
    public async Task<int> AcquireCandidates()
    {
        List<TriggerAcquireResult> results = await driverDelegate.SelectTriggersToAcquire(database.Holder, singleCriteria).ConfigureAwait(false);
        return results.Count;
    }

    /// <summary>The same at a batch of five, where the aggregate's share of an attempt is smaller.</summary>
    [Benchmark]
    public async Task<int> AcquireCandidatesBatched()
    {
        List<TriggerAcquireResult> results = await driverDelegate.SelectTriggersToAcquire(database.Holder, batchCriteria).ConfigureAwait(false);
        return results.Count;
    }

    private async Task ApplyIndex()
    {
        // Dropped and recreated per parameter set, so the two arms differ in the index and nothing else.
        await database.Execute(Dialect == BenchmarkDialect.Postgres
            ? "DROP INDEX IF EXISTS " + CoveringIndexName
            : "DROP INDEX IF EXISTS " + CoveringIndexName + " ON " + TablePrefix + "FIRED_TRIGGERS").ConfigureAwait(false);

        if (Indexed)
        {
            await database.Execute("CREATE INDEX " + CoveringIndexName + " ON " + TablePrefix
                + "FIRED_TRIGGERS (SCHED_NAME, EXECUTION_GROUP, TRIGGER_GROUP)").ConfigureAwait(false);
        }
    }

    private async Task EnsureWaitingTriggers()
    {
        // Re-seeding 5,000 rows in every one of the ninety-odd processes a sweep launches is most of
        // its runtime, so what is already there is left alone.
        if (await database.Scalar("SELECT COUNT(*) FROM " + TablePrefix + "TRIGGERS").ConfigureAwait(false) == WaitingTriggers
            && await database.Scalar("SELECT COUNT(DISTINCT TRIGGER_GROUP) FROM " + TablePrefix + "TRIGGERS").ConfigureAwait(false) == ExecutionGroups)
        {
            return;
        }

        await database.Execute("DELETE FROM " + TablePrefix + "TRIGGERS").ConfigureAwait(false);
        await database.Execute("DELETE FROM " + TablePrefix + "JOB_DETAILS").ConfigureAwait(false);
        await database.Execute("DELETE FROM " + TablePrefix + "SCHEDULER_STATE").ConfigureAwait(false);

        await database.Execute("INSERT INTO " + TablePrefix + "JOB_DETAILS (SCHED_NAME, JOB_NAME, JOB_GROUP, JOB_CLASS_NAME, IS_DURABLE, IS_NONCONCURRENT, IS_UPDATE_DATA, REQUESTS_RECOVERY) VALUES ('"
            + SchedulerName + "', 'job', 'jobs', 'Quartz.Job.NoOpJob, Quartz', " + database.True + ", " + database.False + ", " + database.False + ", " + database.False + ")").ConfigureAwait(false);

        await database.Execute(string.Create(CultureInfo.InvariantCulture,
            $"INSERT INTO {TablePrefix}SCHEDULER_STATE (SCHED_NAME, INSTANCE_NAME, LAST_CHECKIN_TIME, CHECKIN_INTERVAL) VALUES ('{SchedulerName}', '{InstanceId}', {TimeProvider.System.GetUtcNow().UtcTicks}, 15000)")).ConfigureAwait(false);

        // Fire times spread one second apart, so the candidate select finds a handful inside its window
        // and has to order the rest — which is what it does on a live scheduler.
        long start = TimeProvider.System.GetUtcNow().UtcTicks;
        List<string> inserts = new(WaitingTriggers);
        for (int i = 0; i < WaitingTriggers; i++)
        {
            inserts.Add(string.Create(CultureInfo.InvariantCulture,
                $"INSERT INTO {TablePrefix}TRIGGERS (SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP, JOB_NAME, JOB_GROUP, TRIGGER_STATE, TRIGGER_TYPE, START_TIME, NEXT_FIRE_TIME, PRIORITY, MISFIRE_INSTR, EXECUTION_GROUP, PREFERRED_NODE_AUTO) VALUES ('{SchedulerName}', 'trigger{i}', 'group{i % ExecutionGroups}', 'job', 'jobs', 'WAITING', 'SIMPLE', {start}, {start + i * TimeSpan.TicksPerSecond}, 5, -1, 'exec{i % ExecutionGroups}', {database.False})"));
        }

        await database.ExecuteBatched(inserts).ConfigureAwait(false);
    }

    private async Task EnsureFiredTriggers()
    {
        int wantedGroups = Math.Min(FiredTriggerRows, ExecutionGroups);
        if (await database.Scalar("SELECT COUNT(*) FROM " + TablePrefix + "FIRED_TRIGGERS").ConfigureAwait(false) == FiredTriggerRows
            && await database.Scalar("SELECT COUNT(DISTINCT TRIGGER_GROUP) FROM " + TablePrefix + "FIRED_TRIGGERS").ConfigureAwait(false) == wantedGroups)
        {
            return;
        }

        await database.Execute("DELETE FROM " + TablePrefix + "FIRED_TRIGGERS").ConfigureAwait(false);

        long now = TimeProvider.System.GetUtcNow().UtcTicks;
        string falseLiterals = database.False + ", " + database.False;
        List<string> inserts = new(FiredTriggerRows);
        for (int i = 0; i < FiredTriggerRows; i++)
        {
            inserts.Add(string.Create(CultureInfo.InvariantCulture,
                $"INSERT INTO {TablePrefix}FIRED_TRIGGERS (SCHED_NAME, ENTRY_ID, TRIGGER_NAME, TRIGGER_GROUP, INSTANCE_NAME, FIRED_TIME, SCHED_TIME, PRIORITY, STATE, JOB_NAME, JOB_GROUP, IS_NONCONCURRENT, REQUESTS_RECOVERY, EXECUTION_GROUP) VALUES ('{SchedulerName}', 'entry{i}', 'trigger{i}', 'group{i % ExecutionGroups}', 'NODE-{i % 10}', {now}, {now}, 5, 'EXECUTING', 'job', 'jobs', {falseLiterals}, 'exec{i % ExecutionGroups}')"));
        }

        await database.ExecuteBatched(inserts).ConfigureAwait(false);
    }

}
