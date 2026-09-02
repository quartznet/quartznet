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
/// <c>MaxBatchSize</c> tracks <see cref="MaxConcurrency" /> because it has to: the scheduler refuses
/// a batch larger than the pool that would have to run it, so the two cannot be swept independently
/// across 10 and 50. The fire-ahead window is a second rather than the shipped default of zero,
/// without which a batch is one trigger however large <c>MaxBatchSize</c> is. Both are stated in
/// <c>README.md</c> beside the numbers, because a reader comparing these against their own
/// deployment's defaults would otherwise be comparing two different things.
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

    private IScheduler scheduler = null!;

    /// <summary>Starts the scheduler and gets it firing before anything is measured.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        scheduler = await FireThroughput.StartScheduler(
            instanceName: "RamThroughputBenchmark",
            maxConcurrency: MaxConcurrency,
            configureStore: quartz => quartz.UseInMemoryStore()).ConfigureAwait(false);
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
