using BenchmarkDotNet.Attributes;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Tests;

namespace Quartz.Benchmark;

/// <summary>
/// One trip through <see cref="RAMJobStore.AcquireNextTriggers" />, which the scheduler thread makes
/// once per idle-wait cycle whether or not anything is due.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IdleAttempt" /> is the case that dominates a running scheduler: triggers are stored but
/// none is due inside the window, so the loop takes the earliest one, puts it straight back, and
/// returns nothing. It leaves the store exactly as it found it, so it measures the attempt's fixed
/// cost — the collections the method allocates before it knows whether it will acquire anything —
/// with nothing else mixed in.
/// </para>
/// <para>
/// The two acquiring cases include the matching <see cref="IJobStore.ReleaseAcquiredTrigger" /> calls,
/// because acquisition mutates the store and the benchmark has to hand it back. Their allocation
/// figures therefore cover the release path as well; the idle case is the one to read for the
/// attempt's own allocations.
/// </para>
/// <para>
/// Compare against a run on the parent commit: this measures production code rather than a copy of it,
/// so a before/after is two runs rather than two arms.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class TriggerAcquisitionAttemptBenchmark
{
    private const int IdleTriggerCount = 50;

    private RAMJobStore idleStore = null!;
    private RAMJobStore singleStore = null!;
    private RAMJobStore batchStore = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        IJobDetail job = JobBuilder.Create<AcquisitionNoOpJob>().WithIdentity("job", "group").Build();

        // Nothing due for a day, which is what an idle scheduler's store looks like.
        idleStore = TestJobStores.Ram();
        await idleStore.Initialize(TestJobStores.Identity());
        await idleStore.AddJob(job, false);
        for (int i = 0; i < IdleTriggerCount; i++)
        {
            await idleStore.AddTrigger(Trigger("idle" + i, job, TimeProvider.System.GetUtcNow().AddDays(1)), false);
        }

        singleStore = TestJobStores.Ram();
        await singleStore.Initialize(TestJobStores.Identity());
        await singleStore.AddJob(job, false);
        await singleStore.AddTrigger(Trigger("due", job, fireTime: null), false);

        batchStore = TestJobStores.Ram();
        await batchStore.Initialize(TestJobStores.Identity());
        await batchStore.AddJob(job, false);
        for (int i = 0; i < 10; i++)
        {
            await batchStore.AddTrigger(Trigger("due" + i, job, fireTime: null), false);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        idleStore.Shutdown();
        singleStore.Shutdown();
        batchStore.Shutdown();
    }

    /// <summary>
    /// The store holds triggers, but the earliest fires tomorrow: the attempt allocates its
    /// collections, walks one trigger, and returns empty-handed.
    /// </summary>
    [Benchmark]
    public async Task<int> IdleAttempt()
    {
        List<IOperableTrigger> acquired = await idleStore.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = TimeProvider.System.GetUtcNow(),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero,
        });

        if (acquired.Count != 0)
        {
            throw new InvalidOperationException("Expected an idle attempt to acquire nothing, got " + acquired.Count + ".");
        }

        return acquired.Count;
    }

    /// <summary>One trigger due, acquired at the default batch size of one, then released.</summary>
    [Benchmark]
    public async Task<int> AcquireOne()
    {
        return await AcquireAndRelease(singleStore, maxCount: 1, expected: 1);
    }

    /// <summary>Ten triggers due, acquired in one batch, then released.</summary>
    [Benchmark]
    public async Task<int> AcquireBatch()
    {
        return await AcquireAndRelease(batchStore, maxCount: 10, expected: 10);
    }

    private static async Task<int> AcquireAndRelease(RAMJobStore store, int maxCount, int expected)
    {
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.MaxValue,
            MaxCount = maxCount,
            TimeWindow = TimeSpan.FromTicks(100_000),
        });

        if (acquired.Count != expected)
        {
            throw new InvalidOperationException("Expected " + expected + " triggers, got " + acquired.Count + ".");
        }

        foreach (IOperableTrigger trigger in acquired)
        {
            await store.ReleaseAcquiredTrigger(trigger);
        }

        return acquired.Count;
    }

    private static IOperableTrigger Trigger(string name, IJobDetail job, DateTimeOffset? fireTime)
    {
        SimpleTriggerImpl trigger = new(name, "group", job.Key.Name, job.Key.Group, TimeProvider.System.GetUtcNow(), null, SimpleTriggerImpl.RepeatIndefinitely, TimeSpan.FromHours(1))
        {
            MisfireInstructionCode = MisfireInstruction.IgnoreMisfirePolicy,
        };

        if (fireTime is not null)
        {
            trigger.NextFireTimeUtc = fireTime;
        }
        else
        {
            trigger.ComputeFirstFireTimeUtc(null);
        }

        return trigger;
    }

    /// <summary>A job type for the stored details; nothing ever executes it.</summary>
    private sealed class AcquisitionNoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
