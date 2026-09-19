using System.Diagnostics;
using System.Globalization;

using BenchmarkDotNet.Attributes;

using Quartz.Benchmark.Competitors.Engines;

namespace Quartz.Benchmark.Competitors;

/// <summary>Which library, and by which call, an S3 row is.</summary>
public enum S3Arm
{
    /// <summary>Quartz, <c>StartNow</c>, at shipped defaults.</summary>
    QuartzStartNow,

    /// <summary>TickerQ with a null <c>ExecutionTime</c>, which is its immediate-dispatch path.</summary>
    TickerQImmediate,

    /// <summary>Hangfire <c>Enqueue</c>, which puts the job straight on the queue.</summary>
    HangfireEnqueue,

    /// <summary>Hangfire <c>Schedule(TimeSpan.Zero)</c>, which goes through the delayed-job poll.</summary>
    HangfireSchedule,
}

/// <summary>
/// S3 — how long it takes from "schedule this now" to the job's first instruction, on an idle engine.
/// </summary>
/// <remarks>
/// <para>
/// One job at a time, two hundred times, with twenty milliseconds of quiet between them so that every
/// repetition begins with the engine's loop parked — which is the shape an interactive caller sees and
/// the shape #3802's own latency probe uses.
/// </para>
/// <para>
/// <b>Two numbers, and they measure different things.</b> BenchmarkDotNet's columns time the whole
/// body: the schedule call, the engine's wake, the dispatch, the job, and the wake of the thread
/// waiting on the event. The percentiles printed at the end instead come from
/// <see cref="Stopwatch.GetTimestamp" /> read at the call and again as the job's first instruction, so
/// they stop where the job starts and are the schedule-to-execute figure proper. The published table
/// uses those; BenchmarkDotNet's P50 and P95 are beside them as a control. There is no built-in P99
/// column, which is the other reason the second measurement exists.
/// </para>
/// <para>
/// <b>Two of these rows are not a wake.</b> TickerQ's immediate path dispatches on the calling thread
/// and Hangfire's <c>Enqueue</c> puts the job where a worker is already looking, so neither has a
/// scheduler loop to wake; Quartz's <c>StartNow</c> and Hangfire's <c>Schedule</c> do. That is a real
/// difference between the libraries rather than a defect in the measurement, and it is what the row
/// pairs are here to show.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[Config(typeof(LatencyConfig))]
public class S3ScheduleLatencyBenchmark
{
    private readonly List<double> microseconds = [];

    private IEngine engine = null!;
    private long scheduledAt;
    private int index;

    [Params(S3Arm.QuartzStartNow, S3Arm.TickerQImmediate, S3Arm.HangfireEnqueue, S3Arm.HangfireSchedule)]
    public S3Arm Arm { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        Completion.CaptureEntryTimestamp = true;
        engine = Create(Arm);
        await engine.Start(Harness.MaxConcurrency).ConfigureAwait(false);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // Let the engine's loop park again, so every repetition measures a wake rather than a loop that
        // happens to be mid-cycle.
        Thread.Sleep(20);
        Completion.Arm(1);
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        long entered = Completion.LastEntryTimestamp;
        if (entered > scheduledAt)
        {
            microseconds.Add(Harness.Microseconds(scheduledAt, entered));
        }
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        Report();
        Completion.CaptureEntryTimestamp = false;
        await engine.DisposeAsync().ConfigureAwait(false);
    }

    [Benchmark]
    public void ScheduleToExecute()
    {
        scheduledAt = Stopwatch.GetTimestamp();
        Schedule();
        Completion.Await();
    }

    private void Schedule()
    {
        switch (Arm)
        {
            case S3Arm.QuartzStartNow:
                ((QuartzEngine) engine).ScheduleNow(index++).AsTask().GetAwaiter().GetResult();
                break;
            case S3Arm.TickerQImmediate:
                ((TickerQEngine) engine).ScheduleNow(index++).AsTask().GetAwaiter().GetResult();
                break;
            case S3Arm.HangfireEnqueue:
                ((HangfireEngine) engine).ScheduleNow(throughTheScheduler: false);
                break;
            default:
                ((HangfireEngine) engine).ScheduleNow(throughTheScheduler: true);
                break;
        }
    }

    /// <summary>
    /// Prints the schedule-call-to-job-entry percentiles, which is the number the published table
    /// carries.
    /// </summary>
    private void Report()
    {
        if (microseconds.Count == 0)
        {
            return;
        }

        double[] sorted = [.. microseconds.Order()];

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"schedule-to-execute {Arm}: n={sorted.Length}, p50={Harness.Percentile(sorted, 50):F1} us, p95={Harness.Percentile(sorted, 95):F1} us, p99={Harness.Percentile(sorted, 99):F1} us, min={sorted[0]:F1} us, max={sorted[^1]:F1} us"));

        microseconds.Clear();
    }

    internal static IEngine Create(S3Arm arm) => arm switch
    {
        S3Arm.QuartzStartNow => new QuartzEngine(QuartzProfile.Defaults, "S3Quartz", quartz => quartz.UseInMemoryStore()),
        S3Arm.TickerQImmediate => new TickerQEngine("TickerQ", TimeSpan.FromMilliseconds(100)),
        S3Arm.HangfireEnqueue => new HangfireEngine("Hangfire (enqueue)", HangfireEngine.InMemory, TimeSpan.FromMilliseconds(50)),
        _ => new HangfireEngine("Hangfire (schedule)", HangfireEngine.InMemory, TimeSpan.FromMilliseconds(50)),
    };
}
