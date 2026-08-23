using BenchmarkDotNet.Attributes;

using Quartz.Extensibility;
using Quartz.Impl.Triggers;

namespace Quartz.Benchmark;

/// <summary>
/// What <see cref="Quartz.Core.QuartzSchedulerThread" /> pays to take ownership of the list a job store
/// hands back from <see cref="IJobStore.AcquireNextTriggers" />.
/// </summary>
/// <remarks>
/// <para>
/// The scheduler thread copies that list before working with it, because the interface does not say
/// whose the list is: it mutates it while waiting for the fire time, and a store that returned
/// something it kept a reference to would see those mutations. Both stores in the box build a fresh
/// list per call, so for them the copy is pure overhead — but dropping it is a contract change for
/// stores outside the box, not an optimisation, which is why this measures what the contract change
/// would be worth before anyone proposes making it.
/// </para>
/// <para>
/// Once per acquisition attempt, so the figure to weigh it against is a whole attempt: an in-memory one
/// in <see cref="TriggerAcquisitionAttemptBenchmark" />, or a database round trip for the ADO store.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class AcquiredTriggerHandoffBenchmark
{
    /// <summary>
    /// Nothing acquired, the default batch size, and a batch large enough to be worth copying.
    /// </summary>
    [Params(0, 1, 20)]
    public int AcquiredCount { get; set; }

    private List<IOperableTrigger> acquired = null!;

    [GlobalSetup]
    public void Setup()
    {
        acquired = [];
        for (int i = 0; i < AcquiredCount; i++)
        {
            acquired.Add(new SimpleTriggerImpl("t" + i, "group", "job", "group", TimeProvider.System.GetUtcNow(), null, 0, TimeSpan.Zero));
        }
    }

    /// <summary>What the scheduler thread does today.</summary>
    [Benchmark(Baseline = true)]
    public int DefensiveCopy()
    {
        List<IOperableTrigger> triggers = new(acquired);
        return triggers.Count;
    }

    /// <summary>What it would do if the store's list were documented as the caller's to keep.</summary>
    [Benchmark]
    public int CallerOwns()
    {
        List<IOperableTrigger> triggers = acquired;
        return triggers.Count;
    }
}
