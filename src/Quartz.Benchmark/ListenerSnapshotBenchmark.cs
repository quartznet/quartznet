using BenchmarkDotNet.Attributes;

using Quartz.Core;

namespace Quartz.Benchmark;

/// <summary>
/// The listener snapshots one job execution takes: the trigger listeners twice (fired, complete) and
/// the job listeners twice (about to execute, executed).
/// </summary>
/// <remarks>
/// <para>
/// Each of the four notification entry points on <see cref="Core.QuartzScheduler" /> starts by asking
/// the listener manager for the listeners registered right now, so the snapshot is taken four times per
/// fire whether or not anything is listening. Registrations change perhaps a handful of times in a
/// process's life, which is what makes a copy-on-write snapshot worth measuring against the copy.
/// </para>
/// <para>
/// Measures production code, so a before/after is two runs rather than two arms. The
/// <see cref="ListenerCount" /> of zero is the case every application that registers no listeners is
/// in, and the one where an allocation per notification is hardest to justify.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class ListenerSnapshotBenchmark
{
    [Params(0, 1, 3)]
    public int ListenerCount { get; set; }

    private ListenerManagerImpl listenerManager = null!;

    [GlobalSetup]
    public void Setup()
    {
        listenerManager = new ListenerManagerImpl();
        for (int i = 0; i < ListenerCount; i++)
        {
            listenerManager.AddJobListener(new CountingJobListener("job" + i));
            listenerManager.AddTriggerListener(new CountingTriggerListener("trigger" + i));
        }
    }

    /// <summary>The four snapshots one fire takes.</summary>
    [Benchmark]
    public int OneFiresWorthOfSnapshots()
    {
        return listenerManager.GetTriggerListeners().Count
            + listenerManager.GetJobListeners().Count
            + listenerManager.GetJobListeners().Count
            + listenerManager.GetTriggerListeners().Count;
    }

    /// <summary>A listener that does nothing but be registered.</summary>
    private sealed class CountingJobListener : IJobListener
    {
        public CountingJobListener(string name) => Name = name;

        public string Name { get; }
    }

    /// <inheritdoc cref="CountingJobListener" />
    private sealed class CountingTriggerListener : ITriggerListener
    {
        public CountingTriggerListener(string name) => Name = name;

        public string Name { get; }
    }
}
