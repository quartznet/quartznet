using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;

using Quartz.Diagnostics;

namespace Quartz.Benchmark;

/// <summary>
/// How long a job waits between being scheduled for <em>now</em> and its <c>Execute</c> starting, on a
/// scheduler that has nothing else to do, and which part of the scheduler the wait is spent in.
/// </summary>
/// <remarks>
/// <para>
/// This is the number an interactive caller feels and the one a throughput benchmark cannot report:
/// <see cref="FireThroughputBenchmark" /> measures a scheduler that is never allowed to go idle, so its
/// <c>Mean</c> is the marginal cost of a firing in a saturated pipeline rather than the delay one
/// firing sees when the pipeline is empty. Here the scheduler thread is asleep before every repetition,
/// which is what makes each one measure the wake, the acquisition round and the dispatch end to end.
/// </para>
/// <para>
/// It is not a BenchmarkDotNet case. The measured interval begins on the caller's thread and ends on a
/// worker's, so there is no method whose body is the measurement — and the distribution is the answer,
/// where BenchmarkDotNet reports a mean over invocations it chose the count of.
/// </para>
/// <para>
/// <b>Two passes, and the second one is not free.</b> The first pass measures the end-to-end interval
/// with nothing collecting Quartz's instruments, which is how an application runs. The second attaches
/// a <see cref="MeterListener" /> to <c>quartz.trigger.acquisition.duration</c> and
/// <c>quartz.jobstore.operation.duration</c>, whose callbacks run on the scheduler thread at the
/// instant the measurement is recorded — so a timestamp taken inside them splits the interval where the
/// scheduler thread crosses into the store and back out of it. Enabling those instruments makes the
/// scheduler take timestamps it otherwise skips, so the second pass is the decomposition and the first
/// pass is the headline.
/// </para>
/// </remarks>
internal static class LatencyProbe
{
    /// <summary>How many repetitions each pass reports percentiles over.</summary>
    private const int Repetitions = 200;

    /// <summary>
    /// Repetitions run and thrown away before each pass, so the JIT, the thread pool and the store's
    /// dictionaries are warm and the first firing is not a class being loaded.
    /// </summary>
    private const int WarmupRepetitions = 20;

    /// <summary>
    /// How long a repetition may take before it is called a broken probe rather than a slow one. A
    /// firing that never happens would otherwise hang the run with nobody watching.
    /// </summary>
    private static readonly TimeSpan executeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long the scheduler is left alone between repetitions. Completing a firing signals a
    /// scheduling change, which the scheduler thread answers with one more acquisition round; the pause
    /// lets that round happen before the next repetition starts, so every repetition begins with the
    /// thread parked, which is the case being measured.
    /// </summary>
    private static readonly TimeSpan settle = TimeSpan.FromMilliseconds(20);

    private static readonly ManualResetEventSlim executed = new(false);

    private static long scheduledReturnedAt;
    private static long executedAt;
    private static long acquisitionEndedAt;
    private static long firedEndedAt;
    private static double acquisitionSeconds;

    /// <summary>
    /// Runs both passes against one scheduler and prints their percentiles.
    /// </summary>
    public static void Run()
    {
        IScheduler scheduler = QuartzSchedulerBuilder
            .Create(quartz => quartz
                .ConfigureScheduler(options =>
                {
                    options.InstanceName = nameof(LatencyProbe);
                    options.InstanceId = nameof(LatencyProbe);
                })
                .UseInMemoryStore())
            .BuildScheduler()
            .GetAwaiter().GetResult();

        scheduler.Start().GetAwaiter().GetResult();

        try
        {
            double[] endToEnd = MeasureEndToEnd(scheduler);

            Console.WriteLine();
            Console.WriteLine($"Schedule-to-execute latency, idle RAMJobStore scheduler, {Repetitions} repetitions (us)");
            Console.WriteLine();
            PrintHeader();
            PrintRow("schedule -> Execute", endToEnd);

            Phase[] phases = MeasurePhases(scheduler);

            Console.WriteLine();
            Console.WriteLine($"Where it goes, with the instruments enabled ({phases[0].Samples.Length} of {Repetitions} repetitions usable)");
            Console.WriteLine();
            PrintHeader();
            foreach (Phase phase in phases)
            {
                PrintRow(phase.Name, phase.Samples);
            }
        }
        finally
        {
            scheduler.Shutdown(waitForJobsToComplete: false).GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// The headline pass: nothing is collecting Quartz's instruments, so the scheduler runs the way an
    /// application's does.
    /// </summary>
    private static double[] MeasureEndToEnd(IScheduler scheduler)
    {
        int id = 0;

        for (int i = 0; i < WarmupRepetitions; i++)
        {
            FireOnce(scheduler, ++id);
        }

        double[] samples = new double[Repetitions];
        for (int i = 0; i < Repetitions; i++)
        {
            long scheduledAt = FireOnce(scheduler, ++id);
            samples[i] = Microseconds(executedAt - scheduledAt);
        }

        return samples;
    }

    /// <summary>
    /// The decomposition pass: the same repetitions with the acquisition and store-operation histograms
    /// enabled, so the scheduler thread's own crossings into the store are timestamped as they happen.
    /// </summary>
    /// <remarks>
    /// A repetition is discarded when its four timestamps are not in order, which happens when the
    /// acquisition round that follows the previous firing's completion lands inside this repetition's
    /// window instead of before it. The count of usable repetitions is printed rather than hidden.
    /// </remarks>
    private static Phase[] MeasurePhases(IScheduler scheduler)
    {
        using MeterListener listener = new();
        listener.InstrumentPublished = static (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == QuartzInstrumentation.MeterName
                && instrument.Name is QuartzInstrumentation.Instruments.TriggerAcquisitionDuration
                    or QuartzInstrumentation.Instruments.JobStoreOperationDuration)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<double>(OnMeasurement);
        listener.Start();

        int id = 1_000_000;

        for (int i = 0; i < WarmupRepetitions; i++)
        {
            FireOnce(scheduler, ++id);
        }

        List<double> call = [];
        List<double> wake = [];
        List<double> acquisition = [];
        List<double> fire = [];
        List<double> dispatch = [];

        for (int i = 0; i < Repetitions; i++)
        {
            long scheduledAt = FireOnce(scheduler, ++id);

            long returned = scheduledReturnedAt;
            long acquisitionEnded = acquisitionEndedAt;
            long fired = firedEndedAt;
            long executeEntered = executedAt;
            long acquisitionTicks = (long) (acquisitionSeconds * Stopwatch.Frequency);

            if (acquisitionEnded - acquisitionTicks < returned
                || fired < acquisitionEnded
                || executeEntered < fired)
            {
                continue;
            }

            call.Add(Microseconds(returned - scheduledAt));
            wake.Add(Microseconds(acquisitionEnded - acquisitionTicks - returned));
            acquisition.Add(acquisitionSeconds * 1_000_000);
            fire.Add(Microseconds(fired - acquisitionEnded));
            dispatch.Add(Microseconds(executeEntered - fired));
        }

        return
        [
            new Phase("ScheduleJob call", [.. call]),
            new Phase("scheduler thread wake", [.. wake]),
            new Phase("AcquireNextTriggers", [.. acquisition]),
            new Phase("TriggersFired", [.. fire]),
            new Phase("dispatch to Execute", [.. dispatch]),
        ];
    }

    /// <summary>
    /// Schedules one job for now and blocks until it has started running, answering with the timestamp
    /// the schedule was made at. <see cref="executedAt" /> holds the one its <c>Execute</c> was entered
    /// at.
    /// </summary>
    private static long FireOnce(IScheduler scheduler, int id)
    {
        Thread.Sleep(settle);

        IJobDetail job = JobBuilder.Create<ProbeJob>()
            .WithIdentity(string.Create(CultureInfo.InvariantCulture, $"latency-job-{id}"))
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity(string.Create(CultureInfo.InvariantCulture, $"latency-trigger-{id}"))
            .StartNow()
            .Build();

        executed.Reset();
        acquisitionEndedAt = 0;
        firedEndedAt = 0;
        acquisitionSeconds = 0;

        long scheduledAt = Stopwatch.GetTimestamp();
        scheduler.ScheduleJob(job, trigger).GetAwaiter().GetResult();
        scheduledReturnedAt = Stopwatch.GetTimestamp();

        if (!executed.Wait(executeTimeout))
        {
            throw new InvalidOperationException(
                $"Waited {executeTimeout} for one job scheduled to run now. The scheduler stopped firing, which is a broken probe rather than a slow one.");
        }

        return scheduledAt;
    }

    /// <summary>
    /// Runs on the scheduler thread, at the instant the scheduler records the measurement, which is why
    /// a timestamp taken here says when that thread came back out of the store.
    /// </summary>
    private static void OnMeasurement(
        Instrument instrument,
        double measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        if (instrument.Name == QuartzInstrumentation.Instruments.TriggerAcquisitionDuration)
        {
            if (acquisitionEndedAt == 0)
            {
                acquisitionSeconds = measurement;
                acquisitionEndedAt = Stopwatch.GetTimestamp();
            }

            return;
        }

        if (firedEndedAt != 0)
        {
            return;
        }

        foreach (KeyValuePair<string, object?> tag in tags)
        {
            if (tag.Key == ActivityTags.JobStoreOperation
                && (string?) tag.Value == OperationName.JobStore.TriggersFired)
            {
                firedEndedAt = Stopwatch.GetTimestamp();
                return;
            }
        }
    }

    private static double Microseconds(long ticks) => ticks * 1_000_000d / Stopwatch.Frequency;

    /// <summary>
    /// The nearest-rank percentile, which is a value that was actually measured rather than an
    /// interpolation between two that were.
    /// </summary>
    private static double Percentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 0)
        {
            return double.NaN;
        }

        int rank = (int) Math.Ceiling(percentile / 100 * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }

    private static void PrintHeader()
    {
        Console.WriteLine("| Phase                     |       p50 |       p95 |       p99 |       min |       max |");
        Console.WriteLine("|-------------------------- |----------:|----------:|----------:|----------:|----------:|");
    }

    private static void PrintRow(string name, double[] samples)
    {
        double[] sorted = [.. samples.Order()];

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"| {name,-25} | {Percentile(sorted, 50),9:F1} | {Percentile(sorted, 95),9:F1} | {Percentile(sorted, 99),9:F1} | {Percentile(sorted, 0),9:F1} | {Percentile(sorted, 100),9:F1} |"));
    }

    /// <summary>One row of the decomposition: what the phase is called and what it measured.</summary>
    private sealed record Phase(string Name, double[] Samples);

    /// <summary>
    /// Records the instant its execution was entered at. Everything the probe reports is the interval
    /// ending here.
    /// </summary>
    private sealed class ProbeJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            executedAt = Stopwatch.GetTimestamp();
            executed.Set();
            return default;
        }
    }
}
