using BenchmarkDotNet.Attributes;

using Quartz.Diagnostics;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Tests;

namespace Quartz.Benchmark;

/// <summary>
/// What wrapping a job store for tracing costs when nothing is collecting the trace.
/// </summary>
/// <remarks>
/// <para>
/// Every store a container builds is wrapped, so this is a cost every scheduler pays on every store call
/// — and the overwhelmingly common case is an application with no <c>ActivitySource</c> listener and no
/// meter listener at all. The decorator asks both before it does anything: with neither on, the call is a
/// pair of boolean reads and the inner store's <see cref="System.Threading.Tasks.ValueTask" /> returned
/// straight through, with no closure, no state machine, no timestamp and no activity.
/// </para>
/// <para>
/// The two operations are the ones the scheduling loop makes constantly:
/// <c>AcquireNextTriggers</c> on every round of the loop, and <c>AddTrigger</c> as a stand-in for the
/// user-initiated half of the surface. Each is measured bare and decorated, so the difference between the
/// pair is the whole of what the decorator costs.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class TracingJobStoreBenchmark
{
    private RAMJobStore bare = null!;
    private TracingJobStore decorated = null!;
    private TriggerAcquisitionRequest request = null!;
    private IOperableTrigger trigger = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        SchedulerIdentity identity = TestJobStores.Identity();

        bare = TestJobStores.Ram();
        await bare.Initialize(identity);

        RAMJobStore inner = TestJobStores.Ram();
        await inner.Initialize(identity);
        decorated = new TracingJobStore(inner, new Meters(meterFactory: null), TimeProvider.System);
        await decorated.Initialize(identity);

        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job", "group").Build();
        await bare.AddJob(job, AddJobOptions.Replacing);
        await inner.AddJob(job, AddJobOptions.Replacing);

        trigger = (IOperableTrigger) TriggerBuilder.Create()
            .ForJob(job)
            .WithIdentity("trigger", "group")
            .WithSimpleSchedule()
            .StartAt(DateTimeOffset.UtcNow.AddYears(1))
            .Build();

        request = new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow,
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero,
        };
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await bare.Shutdown();
        await decorated.Shutdown();
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = 10_000)]
    public async Task Acquire_Bare()
    {
        for (int i = 0; i < 10_000; i++)
        {
            await bare.AcquireNextTriggers(request);
        }
    }

    [Benchmark(OperationsPerInvoke = 10_000)]
    public async Task Acquire_Decorated()
    {
        for (int i = 0; i < 10_000; i++)
        {
            await decorated.AcquireNextTriggers(request);
        }
    }

    [Benchmark(OperationsPerInvoke = 10_000)]
    public async Task AddTrigger_Bare()
    {
        for (int i = 0; i < 10_000; i++)
        {
            await bare.AddTrigger(trigger, AddTriggerOptions.Replacing);
        }
    }

    [Benchmark(OperationsPerInvoke = 10_000)]
    public async Task AddTrigger_Decorated()
    {
        for (int i = 0; i < 10_000; i++)
        {
            await decorated.AddTrigger(trigger, AddTriggerOptions.Replacing);
        }
    }

    private sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
