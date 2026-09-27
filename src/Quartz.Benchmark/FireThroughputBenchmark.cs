using BenchmarkDotNet.Attributes;

namespace Quartz.Benchmark;

/// <summary>
/// What a firing costs end to end against <c>RAMJobStore</c>: fires per second, and bytes allocated
/// per fire, over a job that does nothing.
/// </summary>
/// <remarks>
/// <para>
/// This is the ceiling. Nothing here touches a network or a disk, so what the <c>Mean</c> column
/// holds is the acquisition loop, the store's own bookkeeping, the thread pool and
/// <c>JobRunShell</c> — the part of a firing that a persistent store adds round trips on top of.
/// <see cref="FireThroughputPostgresBenchmark" /> is the same measurement with those round trips in
/// it, and the two are meant to be read as a pair.
/// </para>
/// <para>
/// <c>Mean</c> is the time one firing took, so fires per second is <c>1e9 / Mean(ns)</c>;
/// <c>Allocated</c> is process-wide over the measured window, so it is what one firing costs the
/// process rather than what one thread of it cost. <see cref="FireThroughput" /> says why the
/// scheduler is started once and left running, and what the workload is.
/// </para>
/// <para>
/// <see cref="Batching" /> is the two values <c>MaxBatchSize</c> can ship with: one trigger a round,
/// and the pool. The pool is the largest it may be — the scheduler refuses a batch larger than the
/// pool that would have to run it — so across 10 and 50 the batched arm tracks
/// <see cref="MaxConcurrency" />. The fire-ahead window is left at zero on both, which on this
/// workload decides nothing: every trigger is overdue, so a round fills to <c>MaxBatchSize</c> either
/// way, and the rows taken before #3862 at a one-second window are the batched arm's.
/// </para>
/// <para>
/// In the <c>--smoke</c> run, deliberately. It builds a scheduler, schedules two hundred triggers and
/// waits for firings, which is exactly the kind of harness #3439 found silently broken.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class FireThroughputBenchmark
{
    /// <summary>The thread pool's permit count; ten is the shipped default and fifty a large node.</summary>
    [Params(10, 50)]
    public int MaxConcurrency { get; set; }

    /// <summary>
    /// How many jobs the two thousand triggers are spread over.
    /// </summary>
    /// <remarks>
    /// A hundred is a schedule of ordinary shape. One is what the one-off API produces - a durable job
    /// per job type and a trigger per call - which is the arrangement #3823 found the store quadratic
    /// in, and the row this benchmark exists to keep honest: firing a trigger must not cost more
    /// because the job behind it has others.
    /// </remarks>
    [Params(FireThroughput.DefaultJobCount, 1)]
    public int JobCount { get; set; }

    /// <summary>
    /// One trigger an acquisition round, or as many as the pool can run.
    /// </summary>
    /// <remarks>
    /// Both, because the answer is not the same on the two stores or on every workload: #3824 measured
    /// the batched round 47 % faster on PostgreSQL, #3822 found it slower in memory for a burst of
    /// one-offs, and #3862 measured it faster here, for triggers that repeat. The in-memory default
    /// stays at one on the strength of the burst.
    /// </remarks>
    [Params(FireBatching.One, FireBatching.Pool)]
    public FireBatching Batching { get; set; }

    private IScheduler scheduler = null!;

    /// <summary>Starts the scheduler and gets it firing before anything is measured.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        scheduler = await FireThroughput.StartScheduler(
            instanceName: "RamThroughputBenchmark",
            maxConcurrency: MaxConcurrency,
            configureStore: quartz => quartz.UseInMemoryStore(),
            jobCount: JobCount,
            batching: Batching).ConfigureAwait(false);
    }

    /// <summary>Stops the scheduler this case has been running throughout.</summary>
    [GlobalCleanup]
    public async Task Cleanup()
    {
        await FireThroughput.StopScheduler(scheduler).ConfigureAwait(false);
    }

    /// <summary>One operation is one firing; the body waits for the next batch of them to happen.</summary>
    [Benchmark(OperationsPerInvoke = FireThroughput.RamFiresPerInvocation)]
    public void Fire() => FireThroughput.AwaitFires(FireThroughput.RamFiresPerInvocation);
}
