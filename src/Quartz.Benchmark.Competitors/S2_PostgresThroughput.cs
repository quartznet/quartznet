using BenchmarkDotNet.Attributes;

using Quartz.Benchmark.Competitors.Database;
using Quartz.Benchmark.Competitors.Engines;

namespace Quartz.Benchmark.Competitors;

/// <summary>Which library, and at what settings, an S2 row is.</summary>
public enum S2Arm
{
    /// <summary>Quartz's ADO store at shipped defaults: batch of one, no fire-ahead window.</summary>
    QuartzDefaults,

    /// <summary>Quartz's ADO store with the batch tracking the pool and a one-second window.</summary>
    QuartzTuned,

    /// <summary>TickerQ's EF Core operational store, <c>MinPollingInterval</c> 100 ms.</summary>
    TickerQ,

    /// <summary>Hangfire's PostgreSQL storage, polling every 100 ms.</summary>
    Hangfire,
}

/// <summary>
/// S2 — the same execution, against one PostgreSQL database at its shipped durability settings.
/// </summary>
/// <remarks>
/// <para>
/// Two thousand one-off schedules rather than twenty thousand, because every one of them is round
/// trips. Otherwise the arrangement is <see cref="S1InMemoryThroughputBenchmark" />'s: the engine is
/// built and the work scheduled in the iteration setup, the measured window opens at the due instant,
/// and the body waits for two thousand executions.
/// </para>
/// <para>
/// <b>The lead time is ten seconds here rather than two.</b> Hangfire creates a scheduled job with its
/// own round trips and has no batch API, so writing two thousand of them takes longer than two seconds
/// on this database — and a row whose work fell due while it was still being written would have drained
/// part of the batch outside the measured window. <c>Harness.EnsureScheduledBeforeDue</c> fails the run
/// rather than letting that happen quietly.
/// </para>
/// <para>
/// <b>Each library is in its own schema of one database</b> — Quartz in <c>public</c>, TickerQ in
/// <c>ticker</c>, Hangfire in <c>hangfire</c>, each of those being that library's own default. One
/// database means one <c>fsync</c> setting, one disk and one connection pool implementation under all
/// three, so the commit-durability share of the number is the same for every row.
/// </para>
/// <para>
/// <b>Commits per firing is not measured here.</b> <c>pg_stat_database.xact_commit</c> is a
/// database-wide counter and a BenchmarkDotNet case runs an unknown number of warmup iterations either
/// side of the measured ones, so it is counted by <c>--commits</c>, which runs one complete window per
/// engine and divides. That is how #3802's D1 report counted it.
/// </para>
/// <para>
/// Not in any smoke run and not in CI: it needs a database that is named by an environment variable.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[Config(typeof(DatabaseThroughputConfig))]
[BenchmarkCategory(Program.RequiresDatabase)]
public class S2PostgresThroughputBenchmark
{
    /// <summary>How many executions one invocation waits for.</summary>
    public const int Executions = 2_000;

    /// <summary>
    /// How far out the work is due, which is also how long the scheduling has to finish in.
    /// </summary>
    internal static readonly TimeSpan Lead = TimeSpan.FromSeconds(10);

    private IEngine engine = null!;

    [Params(S2Arm.QuartzDefaults, S2Arm.QuartzTuned, S2Arm.TickerQ, S2Arm.Hangfire)]
    public S2Arm Arm { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        await Reset(Arm).ConfigureAwait(false);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        Prepare().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Stops the engine and empties the arm's schema.
    /// </summary>
    /// <remarks>
    /// The in-memory scenario gets an empty store per iteration for free, because a new engine gets a
    /// new store. A database does not: Quartz deletes a one-off trigger and its orphaned job detail as
    /// it completes them, but Hangfire's succeeded jobs live until their expiry sweep and TickerQ keeps
    /// every completed ticker, so without this the fifth iteration would be reading a table four
    /// iterations deeper than the first. Resetting here rather than tolerating the drift is what makes
    /// the iterations comparable with each other and with the in-memory rows.
    /// </remarks>
    [IterationCleanup]
    public void IterationCleanup()
    {
        Completion.Disarm();
        engine.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Reset(Arm).GetAwaiter().GetResult();
    }

    [Benchmark(OperationsPerInvoke = Executions)]
    public void Drain() => Completion.Await();

    private async Task Prepare()
    {
        engine = Create(Arm, PostgresFixture.ConnectionString);
        await engine.Start(Harness.MaxConcurrency).ConfigureAwait(false);

        Completion.Arm(Executions);

        DateTimeOffset dueAt = Harness.DueAt(Lead);
        await engine.ScheduleOneOff(Executions, dueAt).ConfigureAwait(false);
        Harness.EnsureScheduledBeforeDue(dueAt, engine.Name);
        Harness.WaitUntil(dueAt);
        Harness.EnsureNothingRanEarly(Executions, engine.Name);
    }

    /// <summary>
    /// Empties whatever the arm's library left behind, so a case starts on an empty store.
    /// </summary>
    internal static async Task Reset(S2Arm arm, CancellationToken cancellationToken = default)
    {
        await using PostgresFixture fixture = await PostgresFixture.Open(cancellationToken).ConfigureAwait(false);

        switch (arm)
        {
            case S2Arm.QuartzDefaults:
            case S2Arm.QuartzTuned:
                await fixture.ResetQuartz(cancellationToken).ConfigureAwait(false);
                break;
            case S2Arm.TickerQ:
                await fixture.ResetSchema(PostgresFixture.TickerQSchema, cancellationToken).ConfigureAwait(false);
                await PersistentEngines.ProvisionTickerQ(PostgresFixture.ConnectionString, cancellationToken).ConfigureAwait(false);
                break;
            default:
                await fixture.ResetSchema(PostgresFixture.HangfireSchema, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    internal static IEngine Create(S2Arm arm, string connectionString) => arm switch
    {
        S2Arm.QuartzDefaults => PersistentEngines.Quartz(QuartzProfile.Defaults, connectionString),
        S2Arm.QuartzTuned => PersistentEngines.Quartz(QuartzProfile.Tuned, connectionString),
        S2Arm.TickerQ => PersistentEngines.TickerQ(connectionString, TimeSpan.FromMilliseconds(100)),
        _ => PersistentEngines.Hangfire(connectionString),
    };
}
