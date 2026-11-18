using BenchmarkDotNet.Attributes;

namespace Quartz.Benchmark;

/// <summary>
/// <see cref="FireThroughputBenchmark" />'s shipped-default rows with a job that suspends: every job awaits
/// <see cref="Task.Yield" /> once before it counts the firing.
/// </summary>
/// <remarks>
/// <para>
/// A no-op job completes every <c>await</c> on the way down synchronously, so there is no state machine to box
/// and no continuation to schedule, and #4214 found runtime async worth nothing there. A job that suspends makes
/// <c>JobRunShell</c> and everything between it and the job suspend and resume once per firing, which is where
/// runtime async and compiler state machines pay differently. Jint found the suspending case 300-400 bytes a
/// call dearer with runtime async (sebastienros/jint#4373). Read against the no-op rows of
/// <see cref="FireThroughputBenchmark" /> at the same settings, under <c>--runtime-comparison</c>, which puts
/// Quartz's runtime-async asset and its state-machine one side by side.
/// </para>
/// <para>
/// <c>MaxConcurrency</c> 10 and a hundred jobs, the settings of <see cref="FireThroughputBenchmark" />'s shipped
/// default row, with both of its batch shapes.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class SuspendingFireThroughputBenchmark
{
    /// <inheritdoc cref="FireThroughputBenchmark.Batching" />
    [Params(FireBatching.One, FireBatching.Pool)]
    public FireBatching Batching { get; set; }

    private IScheduler scheduler = null!;

    /// <summary>Starts the scheduler and gets it firing before anything is measured.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        Console.WriteLine(RuntimeInfo.Describe());

        scheduler = await FireThroughput.StartScheduler(
            instanceName: "SuspendingThroughputBenchmark",
            maxConcurrency: 10,
            configureStore: quartz => quartz.UseInMemoryStore(),
            batching: Batching,
            suspend: true).ConfigureAwait(false);
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
