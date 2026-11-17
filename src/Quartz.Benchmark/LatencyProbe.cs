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
/// <b>Three passes, and the last two are not free.</b> The first pass measures the end-to-end interval
/// with nothing collecting Quartz's instruments, which is how an application runs. The other two attach
/// a <see cref="MeterListener" /> to <c>quartz.trigger.acquisition.duration</c> and
/// <c>quartz.jobstore.operation.duration</c>, whose callbacks run at the instant the measurement is
/// recorded — so a timestamp taken inside them splits the interval where the store has stored the
/// trigger, which is when the scheduler thread is signalled, and where that thread crosses into the
/// store and back out of it — and an <see cref="ActivityListener" /> to the acquisition's span, whose
/// <c>quartz.jobstore.trigger.fired_on_acquire</c> says whether the store fired the trigger as it
/// acquired it. Enabling those makes the scheduler take timestamps it otherwise skips, so the later
/// passes are the decomposition and the first pass is the headline.
/// </para>
/// <para>
/// <b>A trigger takes one of two paths through the store</b>, and each repetition is reported under the
/// one it took. A store fires a trigger already due as it acquires it, with no <c>TriggersFired</c> of its
/// own, so acquisition and fire are one phase. A trigger it leaves pending is fired by the scheduler
/// thread through <c>TriggersFired</c> once it is due. A trigger scheduled for now takes the first path
/// on <c>RAMJobStore</c> and on PostgreSQL, with lock-free rounds or without, so the last pass schedules
/// its trigger <see cref="LeadPast" /> ahead to reach the second. Its wait for the fire time is the lead
/// the pass chose rather than a cost, and no row holds it.
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

    /// <summary>The width of the phase column, which fits the longest phase name.</summary>
    private const int PhaseWidth = 38;

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

    /// <summary>The shortest lead the last pass schedules its trigger with.</summary>
    private static readonly TimeSpan minimumLead = TimeSpan.FromMilliseconds(10);

    private static readonly ManualResetEventSlim executed = new(false);

    private static long storedAt;
    private static long executedAt;
    private static long acquisitionEndedAt;
    private static double acquisitionSeconds;
    private static int firedOnAcquire;
    private static long firedEndedAt;
    private static double firedSeconds;

    /// <summary>
    /// Runs the three passes against an idle <c>RAMJobStore</c> scheduler, then against an idle PostgreSQL
    /// one when <c>QUARTZ_BENCHMARK_POSTGRES</c> is set, and prints their percentiles.
    /// </summary>
    /// <remarks>
    /// The PostgreSQL store is <see cref="FireThroughputPostgresBenchmark" />'s, with
    /// <see cref="RoundTripLatency" /> and <see cref="LockFreeRoundsArm" /> applied, and its rows are cleared
    /// before and after. Its schedule-to-execute interval holds the <c>ScheduleJob</c> commit and the round
    /// trips of an acquisition and a fire, which is what an application on that store waits for.
    /// </remarks>
    public static void Run()
    {
        Run("RAMJobStore", quartz => quartz.UseInMemoryStore());

        if (Environment.GetEnvironmentVariable("QUARTZ_BENCHMARK_POSTGRES") is null)
        {
            return;
        }

        BenchmarkDatabase database = BenchmarkDatabase.Open(BenchmarkDialect.Postgres).GetAwaiter().GetResult();
        try
        {
            OneOffCensus.ClearRows(database, nameof(LatencyProbe)).GetAwaiter().GetResult();

            string connectionString = database.Provider.ConnectionString;
            string providerName = database.ProviderName;

            Run("PostgreSQL", quartz => quartz.UsePersistentStore(store =>
            {
                store.UsePostgres(connectionString);
                store.UseSystemTextJsonSerializer();
                RoundTripLatency.Inject(store, providerName, connectionString);
                LockFreeRoundsArm.Apply(store);
            }));
        }
        finally
        {
            OneOffCensus.ClearRows(database, nameof(LatencyProbe)).GetAwaiter().GetResult();
            database.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static void Run(string store, Action<IQuartzBuilder> configureStore)
    {
        IScheduler scheduler = QuartzSchedulerBuilder
            .Create(quartz =>
            {
                quartz.ConfigureScheduler(options =>
                {
                    options.InstanceName = nameof(LatencyProbe);
                    options.InstanceId = nameof(LatencyProbe);
                });

                configureStore(quartz);
            })
            .BuildScheduler()
            .GetAwaiter().GetResult();

        scheduler.Start().GetAwaiter().GetResult();

        try
        {
            double[] endToEnd = MeasureEndToEnd(scheduler);

            Console.WriteLine();
            Console.WriteLine($"Schedule-to-execute latency, idle {store} scheduler, {Repetitions} repetitions (us)");
            Console.WriteLine();
            PrintHeader();
            PrintRow("schedule -> Execute", endToEnd);

            PrintBreakdowns("scheduled for now", MeasurePhases(scheduler, TimeSpan.Zero));

            TimeSpan lead = LeadPast(endToEnd);
            PrintBreakdowns(
                string.Create(CultureInfo.InvariantCulture, $"due {lead.TotalMilliseconds} ms after scheduling, the wait left out"),
                MeasurePhases(scheduler, lead));
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
            FireOnce(scheduler, ++id, TimeSpan.Zero);
        }

        double[] samples = new double[Repetitions];
        for (int i = 0; i < Repetitions; i++)
        {
            long scheduledAt = FireOnce(scheduler, ++id, TimeSpan.Zero);
            samples[i] = Microseconds(executedAt - scheduledAt);
        }

        return samples;
    }

    /// <summary>
    /// How far ahead of its scheduling the last pass's trigger is due: twice the headline's 95th
    /// percentile, in whole milliseconds, and at least <see cref="minimumLead" />. The whole interval to
    /// <c>Execute</c> is shorter than half of that in nineteen repetitions in twenty, so the acquisition,
    /// which comes before it, almost always finds the trigger not yet due, and the store leaves it
    /// pending. The 95th rather than the 99th, so that one stall of the box does not set the pass's length.
    /// </summary>
    private static TimeSpan LeadPast(double[] endToEnd)
    {
        double[] sorted = [.. endToEnd.Order()];
        TimeSpan lead = TimeSpan.FromMilliseconds(Math.Ceiling(2 * Percentile(sorted, 95) / 1_000));
        return lead > minimumLead ? lead : minimumLead;
    }

    /// <summary>
    /// A decomposition pass: the same repetitions with the acquisition and store-operation histograms
    /// and the acquisition's span enabled, so the scheduler thread's own crossings into the store are
    /// timestamped as they happen, and the acquisition says whether it fired the trigger.
    /// </summary>
    /// <param name="scheduler">The scheduler the headline pass ran on.</param>
    /// <param name="lead">How long after its scheduling each repetition's trigger is due.</param>
    /// <returns>The usable repetitions, one breakdown for each path through the store.</returns>
    /// <remarks>
    /// <para>
    /// <c>TriggersFired</c> is the store operation's own duration, so the scheduler thread's time between
    /// the acquisition and the fire, its wait for the fire time included, is in no row.
    /// </para>
    /// <para>
    /// The wake is counted from the store's answer to <c>ScheduleJob</c>, after which the scheduler thread
    /// is signalled, rather than from the call's return. A caller blocked on a store that answers
    /// asynchronously, as PostgreSQL's does, is woken as the scheduler thread is, and is often the later
    /// of the two; its return is on no path to <c>Execute</c>.
    /// </para>
    /// <para>
    /// A repetition is discarded when its path cannot be told or its timestamps are not in order, which
    /// happens when the acquisition round that follows the previous firing's completion lands inside this
    /// repetition's window instead of before it. The count of usable repetitions is printed rather than
    /// hidden.
    /// </para>
    /// </remarks>
    private static Breakdown[] MeasurePhases(IScheduler scheduler, TimeSpan lead)
    {
        using MeterListener meterListener = new();
        meterListener.InstrumentPublished = static (instrument, listener) =>
        {
            if (instrument.Meter.Name == QuartzInstrumentation.MeterName
                && instrument.Name is QuartzInstrumentation.Instruments.TriggerAcquisitionDuration
                    or QuartzInstrumentation.Instruments.JobStoreOperationDuration)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        meterListener.SetMeasurementEventCallback<double>(OnMeasurement);
        meterListener.Start();

        // The acquisition's span and no other, with its attributes, which is where fired_on_acquire is.
        using ActivityListener activityListener = new()
        {
            ShouldListenTo = static source => source.Name == QuartzInstrumentation.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> options) =>
                options.Name == OperationName.JobStore.AcquireNextTriggers
                    ? ActivitySamplingResult.AllData
                    : ActivitySamplingResult.None,
            ActivityStopped = OnAcquisitionStopped,
        };
        ActivitySource.AddActivityListener(activityListener);

        int id = lead == TimeSpan.Zero ? 1_000_000 : 2_000_000;

        for (int i = 0; i < WarmupRepetitions; i++)
        {
            FireOnce(scheduler, ++id, lead);
        }

        List<Repetition> byAcquisition = [];
        List<Repetition> byTriggersFired = [];

        for (int i = 0; i < Repetitions; i++)
        {
            long scheduledAt = FireOnce(scheduler, ++id, lead);

            long stored = storedAt;
            long acquisitionEnded = acquisitionEndedAt;
            long acquisitionStarted = acquisitionEnded - Ticks(acquisitionSeconds);
            long firedEnded = firedEndedAt;
            long executeEntered = executedAt;

            if (stored == 0 || acquisitionStarted < stored)
            {
                continue;
            }

            if (firedOnAcquire > 0 && executeEntered >= acquisitionEnded)
            {
                byAcquisition.Add(new Repetition(
                    Microseconds(stored - scheduledAt),
                    Microseconds(acquisitionStarted - stored),
                    acquisitionSeconds * 1_000_000,
                    0,
                    Microseconds(executeEntered - acquisitionEnded)));
            }
            else if (firedOnAcquire == 0
                && firedEnded != 0
                && firedEnded - Ticks(firedSeconds) >= acquisitionEnded
                && executeEntered >= firedEnded)
            {
                byTriggersFired.Add(new Repetition(
                    Microseconds(stored - scheduledAt),
                    Microseconds(acquisitionStarted - stored),
                    acquisitionSeconds * 1_000_000,
                    firedSeconds * 1_000_000,
                    Microseconds(executeEntered - firedEnded)));
            }
        }

        return
        [
            new Breakdown("Fired by the acquisition",
            [
                new Phase("ScheduleJob until stored", [.. byAcquisition.Select(static r => r.Store)]),
                new Phase("scheduler thread wake", [.. byAcquisition.Select(static r => r.Wake)]),
                new Phase("AcquireNextTriggers (fired on acquire)", [.. byAcquisition.Select(static r => r.Acquisition)]),
                new Phase("dispatch to Execute", [.. byAcquisition.Select(static r => r.Dispatch)]),
            ]),
            new Breakdown("Fired by TriggersFired",
            [
                new Phase("ScheduleJob until stored", [.. byTriggersFired.Select(static r => r.Store)]),
                new Phase("scheduler thread wake", [.. byTriggersFired.Select(static r => r.Wake)]),
                new Phase("AcquireNextTriggers", [.. byTriggersFired.Select(static r => r.Acquisition)]),
                new Phase("TriggersFired", [.. byTriggersFired.Select(static r => r.Fire)]),
                new Phase("dispatch to Execute", [.. byTriggersFired.Select(static r => r.Dispatch)]),
            ]),
        ];
    }

    /// <summary>
    /// Schedules one job, due <paramref name="lead" /> from now, and blocks until it has started running,
    /// answering with the timestamp the schedule was made at. <see cref="executedAt" /> holds the one its
    /// <c>Execute</c> was entered at.
    /// </summary>
    private static long FireOnce(IScheduler scheduler, int id, TimeSpan lead)
    {
        Thread.Sleep(settle);

        IJobDetail job = JobBuilder.Create<ProbeJob>()
            .WithIdentity(string.Create(CultureInfo.InvariantCulture, $"latency-job-{id}"))
            .Build();

        TriggerBuilder<IJob> builder = TriggerBuilder.Create()
            .WithIdentity(string.Create(CultureInfo.InvariantCulture, $"latency-trigger-{id}"));

        ITrigger trigger = (lead == TimeSpan.Zero ? builder.StartNow() : builder.StartAt(TimeProvider.System.GetUtcNow() + lead))
            .Build();

        executed.Reset();
        storedAt = 0;
        acquisitionEndedAt = 0;
        acquisitionSeconds = 0;
        firedOnAcquire = -1;
        firedEndedAt = 0;
        firedSeconds = 0;

        long scheduledAt = Stopwatch.GetTimestamp();
        scheduler.ScheduleJob(job, trigger).GetAwaiter().GetResult();

        if (!executed.Wait(executeTimeout))
        {
            throw new InvalidOperationException(
                $"Waited {executeTimeout} for one job due {lead.TotalMilliseconds} ms after it was scheduled. The scheduler stopped firing, which is a broken probe rather than a slow one.");
        }

        return scheduledAt;
    }

    /// <summary>
    /// Runs at the instant a measurement is recorded: on the scheduler thread for the acquisition and
    /// <c>TriggersFired</c>, which is why a timestamp taken here says when that thread came back out of the
    /// store, and on whichever thread finished the store's <c>ScheduleJob</c> for that one.
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

        foreach (KeyValuePair<string, object?> tag in tags)
        {
            if (tag.Key != ActivityTags.JobStoreOperation)
            {
                continue;
            }

            string? operation = (string?) tag.Value;
            if (operation == OperationName.JobStore.ScheduleJob && storedAt == 0)
            {
                storedAt = Stopwatch.GetTimestamp();
            }
            else if (operation == OperationName.JobStore.TriggersFired && firedEndedAt == 0)
            {
                firedSeconds = measurement;
                firedEndedAt = Stopwatch.GetTimestamp();
            }

            return;
        }
    }

    /// <summary>
    /// Runs on the scheduler thread as the acquisition's span ends, before the acquisition is measured,
    /// and keeps how many triggers the repetition's first acquisition fired as it acquired them.
    /// </summary>
    private static void OnAcquisitionStopped(Activity activity)
    {
        if (firedOnAcquire < 0 && activity.GetTagItem(ActivityTags.TriggersFiredOnAcquire) is int fired)
        {
            firedOnAcquire = fired;
        }
    }

    private static long Ticks(double seconds) => (long) (seconds * Stopwatch.Frequency);

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

    /// <summary>
    /// Prints a decomposition pass: how many repetitions were usable, then a table for each path that
    /// any of them took.
    /// </summary>
    private static void PrintBreakdowns(string pass, Breakdown[] breakdowns)
    {
        Console.WriteLine();
        Console.WriteLine($"Where it goes, {pass}, with the instruments enabled ({breakdowns.Sum(static b => b.Count)} of {Repetitions} repetitions usable)");

        foreach (Breakdown breakdown in breakdowns)
        {
            if (breakdown.Count == 0)
            {
                continue;
            }

            Console.WriteLine();
            Console.WriteLine($"{breakdown.Path}: {breakdown.Count}");
            Console.WriteLine();
            PrintHeader();
            foreach (Phase phase in breakdown.Phases)
            {
                PrintRow(phase.Name, phase.Samples);
            }
        }
    }

    private static void PrintHeader()
    {
        Console.WriteLine($"| {"Phase",-PhaseWidth} |       p50 |       p95 |       p99 |       min |       max |");
        Console.WriteLine($"|{new string('-', PhaseWidth + 1)} |----------:|----------:|----------:|----------:|----------:|");
    }

    private static void PrintRow(string name, double[] samples)
    {
        double[] sorted = [.. samples.Order()];

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"| {name,-PhaseWidth} | {Percentile(sorted, 50),9:F1} | {Percentile(sorted, 95),9:F1} | {Percentile(sorted, 99),9:F1} | {Percentile(sorted, 0),9:F1} | {Percentile(sorted, 100),9:F1} |"));
    }

    /// <summary>One usable repetition of a decomposition pass, phase by phase, in microseconds.</summary>
    private readonly record struct Repetition(double Store, double Wake, double Acquisition, double Fire, double Dispatch);

    /// <summary>One row of a breakdown: what the phase is called and what it measured.</summary>
    private sealed record Phase(string Name, double[] Samples);

    /// <summary>The repetitions of a decomposition pass that took one path through the store.</summary>
    private sealed record Breakdown(string Path, Phase[] Phases)
    {
        public int Count => Phases[0].Samples.Length;
    }

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
