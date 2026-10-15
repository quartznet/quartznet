using BenchmarkDotNet.Attributes;

namespace Quartz.Benchmark;

/// <summary>
/// <see cref="FireThroughputBenchmark" />'s firing against <c>RAMJobStore</c>, at the depth and with the
/// recording that benchmark never reaches: a hundred thousand live triggers, and execution history on.
/// </summary>
/// <remarks>
/// <para>
/// Every firing benchmark holds two thousand triggers, which is enough to keep the scheduler busy and
/// far too few to show what the store's own structures cost. <c>RAMJobStore</c> keeps its triggers in a
/// <c>SortedSet</c> ordered by fire time, priority and key, and a firing walks it four times — out at
/// acquisition, out and back in at the fire, and the whole job's worth for a job that disallows
/// concurrent execution — so at a hundred thousand triggers each walk is seventeen levels of string
/// compares rather than eleven. <see cref="TriggerCount" /> is what says whether that shows on the
/// scheduler thread. <see cref="ExecutionHistory" /> is the other cost nothing measured: with history on,
/// every firing is appended to <c>InMemoryExecutionHistoryStore</c>, which trims and caps its feed on each
/// append under one lock — the dashboard's default, and the one #4066 asks to be priced.
/// </para>
/// <para>
/// A class of its own rather than two more <c>[Params]</c> on <see cref="FireThroughputBenchmark" />,
/// because that benchmark is in the smoke run and already runs eight cases: two more axes would be
/// thirty-two. This one holds the pool at ten, the jobs at a hundred and the batch at one — the in-memory
/// defaults — and sweeps the two new axes alone, four cases, all in the smoke run.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class FireThroughputScaleBenchmark
{
    /// <summary>The shipped default, and fifty times it.</summary>
    [Params(FireThroughput.DefaultTriggerCount, 100_000)]
    public int TriggerCount { get; set; }

    /// <summary>Whether every firing is recorded into the in-memory execution history.</summary>
    [Params(false, true)]
    public bool ExecutionHistory { get; set; }

    private IScheduler scheduler = null!;

    /// <summary>Starts the scheduler and gets it firing before anything is measured.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        scheduler = await FireThroughput.StartScheduler(
            instanceName: "RamScaleBenchmark",
            maxConcurrency: 10,
            configureStore: quartz => quartz.UseInMemoryStore(),
            batching: FireBatching.One,
            triggerCount: TriggerCount,
            executionHistory: ExecutionHistory).ConfigureAwait(false);
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
