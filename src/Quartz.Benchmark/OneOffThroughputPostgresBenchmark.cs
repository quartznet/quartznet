using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace Quartz.Benchmark;

/// <summary>
/// What a <em>one-off</em> firing costs against a real PostgreSQL store — the shape
/// <c>IScheduler.ScheduleJob&lt;TJob, TInput&gt;</c> produces, and the one #3824 measures.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FireThroughputPostgresBenchmark" /> measures a repeating trigger, which is written
/// forward on completion and never deleted. A one-off is deleted instead, and the deletion is a
/// second transaction's worth of statements, so the two numbers are genuinely different workloads
/// rather than the same one at different settings.
/// </para>
/// <para>
/// One operation is one firing. The triggers are re-seeded before every iteration — a one-off is
/// consumed by firing — which is what makes this a <see cref="RunStrategy.Monitoring" /> benchmark:
/// the seeding runs in <c>[IterationSetup]</c>, which is outside the measurement, and the body is the
/// drain.
/// </para>
/// <para>
/// <b>Running it.</b> Start the database yourself and point the benchmark at it. The census
/// #3824 reports needs <c>pg_stat_statements</c>, so start it preloaded even if only the throughput
/// numbers are wanted here:
/// </para>
/// <code>
/// docker run -d --name quartz-bench-pg -p 55432:5432 \
///   -e POSTGRES_DB=quartznet -e POSTGRES_USER=quartznet -e POSTGRES_PASSWORD=quartznet \
///   postgres:15.1 -c shared_preload_libraries=pg_stat_statements
///
/// QUARTZ_BENCHMARK_POSTGRES='Host=localhost;Port=55432;Database=quartznet;Username=quartznet;Password=quartznet'
///
/// dotnet run -c Release --project src/Quartz.Benchmark -- --filter '*OneOffThroughput*'
/// </code>
/// <para>
/// <c>--one-off-census</c> is the same workload with the statements counted at the database instead
/// of the wall clock measured here.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, warmupCount: 1, iterationCount: 5, invocationCount: 1)]
[BenchmarkCategory(BenchmarkCategories.RequiresDatabase)]
public class OneOffThroughputPostgresBenchmark
{
    /// <summary>How many one-off firings one iteration drains, which is also its operation count.</summary>
    internal const int Firings = 500;

    private const string SchedulerName = "PostgresOneOffBenchmark";

    /// <summary>The thread pool's permit count.</summary>
    [Params(10)]
    public int MaxConcurrency { get; set; }

    /// <summary>
    /// The shipped settings, the same with <c>MaxBatchSize</c> raised to the pool, and the settings
    /// the repeating benchmark uses. The middle one is the interesting arm: it fires nothing early.
    /// </summary>
    [Params(OneOffProfile.Defaults, OneOffProfile.BatchOnly, OneOffProfile.Tuned)]
    public OneOffProfile Profile { get; set; }

    private BenchmarkDatabase database = null!;
    private IScheduler scheduler = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        database = await BenchmarkDatabase.Open(BenchmarkDialect.Postgres).ConfigureAwait(false);
        await OneOffCensus.ClearRows(database, SchedulerName).ConfigureAwait(false);

        string connectionString = database.Provider.ConnectionString;

        scheduler = await OneOffThroughput.StartScheduler(
            instanceName: SchedulerName,
            maxConcurrency: MaxConcurrency,
            profile: Profile,
            configureStore: quartz => quartz.UsePersistentStore(store =>
            {
                store.UsePostgres(connectionString);
                store.UseSystemTextJsonSerializer();
            })).ConfigureAwait(false);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await OneOffThroughput.StopScheduler(scheduler).ConfigureAwait(false);
        await OneOffCensus.ClearRows(database, SchedulerName).ConfigureAwait(false);
        await database.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Re-seeds the firings and waits until they are due, neither of which is measured.</summary>
    [IterationSetup]
    public void IterationSetup()
    {
        OneOffThroughput.Seed(scheduler, Firings).GetAwaiter().GetResult();
    }

    /// <summary>One operation is one firing; the body waits for the whole batch of them to happen.</summary>
    [Benchmark(OperationsPerInvoke = Firings)]
    public void Fire() => OneOffThroughput.Drain(Firings);
}
