using BenchmarkDotNet.Attributes;

using Quartz.Benchmark.Competitors.Engines;

namespace Quartz.Benchmark.Competitors;

/// <summary>Which library, and at what settings, an S1 row is.</summary>
public enum S1Arm
{
    /// <summary>Quartz at its shipped defaults: batch of one, no fire-ahead window.</summary>
    QuartzDefaults,

    /// <summary>Quartz with the batch tracking the pool and a one-second fire-ahead window.</summary>
    QuartzTuned,

    /// <summary>TickerQ with <c>MinPollingInterval</c> at 100 ms.</summary>
    TickerQ,

    /// <summary>Hangfire through the delayed-job scheduler, polling every 50 ms.</summary>
    HangfireScheduled,

    /// <summary>Hangfire with the jobs enqueued outright, which skips that poll.</summary>
    HangfireEnqueued,
}

/// <summary>
/// S1 — what one execution costs in memory, over twenty thousand of them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The workload.</b> Twenty thousand one-off schedules, all due at the same instant two seconds
/// out, over a job that increments a counter and returns. The measured window is the drain: the
/// iteration setup builds the engine, publishes the target, schedules the work through whatever batch
/// API the library has, and then sleeps until the due instant, so the benchmark body is
/// <em>waiting for twenty thousand executions to happen</em> and <c>Mean</c> is what one of them cost.
/// </para>
/// <para>
/// <b>Why two seconds.</b> TickerQ dispatches a ticker due within one second on the calling thread
/// without going near its scheduler loop, and Hangfire's <c>Enqueue</c> does the same in its own way.
/// Two seconds puts every arm on its scheduler's own path, which is the thing being compared; the two
/// short-circuit paths get rows of their own — <see cref="S1Arm.HangfireEnqueued" /> here and the
/// TickerQ row in <see cref="S3ScheduleLatencyBenchmark" />.
/// </para>
/// <para>
/// <b>None of the three fires before the due instant.</b> Quartz acquires ahead but its scheduler
/// thread waits out the batch's earliest fire time before firing
/// (<c>QuartzSchedulerThread.Run</c>); TickerQ queues the second's worth of tickers ahead and then
/// sleeps the remaining time; Hangfire's delayed-job scheduler only moves a job to <c>Enqueued</c> once
/// its score is in the past. So all three do some acquisition work before the window opens, and none
/// of them runs a job in it.
/// </para>
/// <para>
/// <b>The engine is rebuilt every iteration</b>, because every one of these stores fills up: TickerQ
/// keeps completed tickers and scans every entry it holds on each poll, and Hangfire's finished jobs
/// leave state-history entries behind. A second iteration against the first one's leftovers would be
/// measuring the leftovers.
/// </para>
/// <para>
/// <c>Allocated</c> is process-wide over the measured window — BenchmarkDotNet reads
/// <c>GC.GetTotalAllocatedBytes</c> — so it is what one execution costs the whole engine, polling loop
/// and workers included, rather than what one thread of it cost. That is the number to compare across
/// the rows, and it is exact whatever else the machine is doing.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[Config(typeof(ThroughputConfig))]
public class S1InMemoryThroughputBenchmark
{
    /// <summary>How many executions one invocation waits for.</summary>
    public const int Executions = 20_000;

    /// <summary>
    /// How far out the work is due, which is also how long the scheduling has to finish in.
    /// </summary>
    private static readonly TimeSpan lead = TimeSpan.FromSeconds(2);

    private IEngine engine = null!;

    [Params(S1Arm.QuartzDefaults, S1Arm.QuartzTuned, S1Arm.TickerQ, S1Arm.HangfireScheduled, S1Arm.HangfireEnqueued)]
    public S1Arm Arm { get; set; }

    [IterationSetup]
    public void IterationSetup()
    {
        // Not async: BenchmarkDotNet's iteration setup is an Action. Nothing here is measured.
        Prepare().GetAwaiter().GetResult();
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        Completion.Disarm();
        engine.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Benchmark(OperationsPerInvoke = Executions)]
    public void Drain()
    {
        if (Arm == S1Arm.HangfireEnqueued)
        {
            // The jobs are already on the queue; starting the server is what lets them run, and the
            // startup is inside the measured window rather than outside it.
            ((HangfireEngine) engine).StartServer();
        }

        Completion.Await();
    }

    private async Task Prepare()
    {
        engine = Create(Arm);
        await engine.Start(Harness.MaxConcurrency).ConfigureAwait(false);

        Completion.Arm(Executions);

        if (Arm == S1Arm.HangfireEnqueued)
        {
            await engine.ScheduleOneOff(Executions, DateTimeOffset.UtcNow).ConfigureAwait(false);
            return;
        }

        DateTimeOffset dueAt = Harness.DueAt(lead);
        await engine.ScheduleOneOff(Executions, dueAt).ConfigureAwait(false);
        Harness.EnsureScheduledBeforeDue(dueAt, engine.Name);
        Harness.WaitUntil(dueAt);
        Harness.EnsureNothingRanEarly(Executions, engine.Name);
    }

    internal static IEngine Create(S1Arm arm) => arm switch
    {
        S1Arm.QuartzDefaults => new QuartzEngine(QuartzProfile.Defaults, "S1Defaults", quartz => quartz.UseInMemoryStore()),
        S1Arm.QuartzTuned => new QuartzEngine(QuartzProfile.Tuned, "S1Tuned", quartz => quartz.UseInMemoryStore()),
        S1Arm.TickerQ => new TickerQEngine("TickerQ", TimeSpan.FromMilliseconds(100)),
        S1Arm.HangfireScheduled => new HangfireEngine("Hangfire (scheduled)", HangfireEngine.InMemory, TimeSpan.FromMilliseconds(50)),
        _ => new HangfireEngine("Hangfire (enqueued)", HangfireEngine.InMemory, TimeSpan.FromMilliseconds(50),
            startServerOnStart: false, enqueueRatherThanSchedule: true),
    };
}
