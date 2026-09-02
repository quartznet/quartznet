using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace Quartz.Benchmark;

/// <summary>
/// The same firing, measured against a real PostgreSQL store: fires per second and bytes allocated
/// per fire, over a job that does nothing.
/// </summary>
/// <remarks>
/// <para>
/// This is the number a production reader asks for, and the one the migration guide's claim about the
/// batched fire path replacing "six to nine round trips" with one has never had.
/// <see cref="FireThroughputBenchmark" /> is the same workload with the round trips taken out, so the
/// difference between the two is what persistence costs.
/// </para>
/// <para>
/// <b>Running it.</b> Start the database yourself and point the benchmark at it — a container per
/// benchmark case is not a thing BenchmarkDotNet's process-per-case model can give you:
/// </para>
/// <code>
/// docker run -d --name quartz-bench-pg -p 55432:5432 \
///   -e POSTGRES_DB=quartznet -e POSTGRES_USER=quartznet -e POSTGRES_PASSWORD=quartznet postgres:15.1
///
/// QUARTZ_BENCHMARK_POSTGRES='Host=localhost;Port=55432;Database=quartznet;Username=quartznet;Password=quartznet'
///
/// dotnet run -c Release --project src/Quartz.Benchmark -- --filter '*FireThroughput*'
/// </code>
/// <para>
/// The schema comes from <c>database/tables/</c> and is applied on first use by
/// <see cref="BenchmarkDatabase" />, so the tables and their indexes are the ones a real deployment
/// has.
/// </para>
/// <para>
/// <b>One node, not clustered.</b> What is measured is the fire path — acquire, fire, complete — and
/// clustering adds a check-in loop, a cluster-wide lock on every acquisition cycle and a second node
/// competing for the same rows. Those are worth measuring and are not measured here; the number below
/// is the per-firing cost of the store, and a clustered deployment pays it plus that. The soak in
/// <c>Quartz.Tests.Integration</c> is where two nodes are exercised.
/// </para>
/// <para>
/// Carries <see cref="BenchmarkCategories.RequiresDatabase" />, so it is outside the smoke run; the
/// <c>RAMJobStore</c> arm is inside it and covers the shared harness.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, warmupCount: 3, iterationCount: 10)]
[BenchmarkCategory(BenchmarkCategories.RequiresDatabase)]
public class FireThroughputPostgresBenchmark
{
    /// <summary>
    /// The scheduler name every row this benchmark writes is keyed by, so its own rows can be removed
    /// without disturbing what the other database benchmarks seeded into the same schema.
    /// </summary>
    private const string SchedulerName = "PostgresThroughputBenchmark";

    /// <summary>The thread pool's permit count; ten is the shipped default and fifty a large node.</summary>
    [Params(10, 50)]
    public int MaxConcurrency { get; set; }

    private BenchmarkDatabase database = null!;
    private IScheduler scheduler = null!;

    /// <summary>
    /// Applies the schema if it is not there, clears anything a previous parameter set left, then
    /// starts the scheduler and gets it firing.
    /// </summary>
    [GlobalSetup]
    public async Task Setup()
    {
        database = await BenchmarkDatabase.Open(BenchmarkDialect.Postgres).ConfigureAwait(false);
        await ClearOwnRows().ConfigureAwait(false);

        string connectionString = database.Provider.ConnectionString;

        scheduler = await FireThroughput.StartScheduler(
            instanceName: SchedulerName,
            maxConcurrency: MaxConcurrency,
            configureStore: quartz => quartz.UsePersistentStore(store =>
            {
                store.UsePostgres(connectionString);
                store.UseSystemTextJsonSerializer();
            })).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops the scheduler and takes its rows out again, so that the next parameter set starts from
    /// the same empty schema this one did.
    /// </summary>
    [GlobalCleanup]
    public async Task Cleanup()
    {
        await FireThroughput.StopScheduler(scheduler).ConfigureAwait(false);
        await ClearOwnRows().ConfigureAwait(false);
        await database.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>One operation is one firing; the body waits for the next batch of them to happen.</summary>
    [Benchmark(OperationsPerInvoke = FireThroughput.AdoFiresPerInvocation)]
    public void Fire() => FireThroughput.AwaitFires(FireThroughput.AdoFiresPerInvocation);

    /// <summary>
    /// Deletes every row this benchmark's scheduler owns, children before parents.
    /// </summary>
    /// <remarks>
    /// A leftover trigger from an earlier parameter set would be acquired along with this one's and
    /// silently change how many triggers are in flight, which is the one thing the workload holds
    /// constant across the sweep.
    /// </remarks>
    private async Task ClearOwnRows()
    {
        foreach (string table in (string[])
                 [
                     "QRTZ_FIRED_TRIGGERS",
                     "QRTZ_SIMPLE_TRIGGERS",
                     "QRTZ_CRON_TRIGGERS",
                     "QRTZ_SIMPROP_TRIGGERS",
                     "QRTZ_BLOB_TRIGGERS",
                     "QRTZ_TRIGGERS",
                     "QRTZ_JOB_DETAILS",
                     "QRTZ_PAUSED_TRIGGER_GRPS",
                     "QRTZ_SCHEDULER_STATE",
                     "QRTZ_LOCKS",
                 ])
        {
            await database.Execute($"DELETE FROM {table} WHERE SCHED_NAME = '{SchedulerName}'").ConfigureAwait(false);
        }
    }
}
