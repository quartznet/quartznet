using System.Globalization;

namespace Quartz.Benchmark.Competitors;

/// <summary>
/// The recurring-accuracy scenario's recorder: one <c>(scheduledFor, actual)</c> pair per firing.
/// </summary>
/// <remarks>
/// <para>
/// Off unless <see cref="Capturing" /> is set, so the throughput scenarios pay nothing for it. The
/// samples are appended under a lock rather than to a concurrent collection because a minute of
/// hundred-a-second firings is six thousand appends, which is not a rate a lock notices, and because
/// the list has to be read as a whole at the end.
/// </para>
/// <para>
/// <b>Where <c>scheduledFor</c> comes from.</b> Two of the three engines hand the executing job the
/// instant it was scheduled for — Quartz as <c>IJobExecutionContext.ScheduledFireTimeUtc</c>, TickerQ
/// as <c>TickerFunctionContext.ScheduledFor</c> — and those are used as they are given. Hangfire does
/// not: a recurring job is turned into an ordinary background job and the occurrence it came from is
/// not passed on, and under the default <c>MisfireHandlingMode.Relaxed</c> the occurrence is rewritten
/// to "now" before the job is created (<c>RecurringJobEntity.ScheduleNext</c>), so there is no
/// scheduled instant left to report. Its row therefore measures deviation from the nearest whole
/// second, which is what a one-second schedule is nominally aligned to and what the other two are
/// started on. That difference is stated in the README beside the numbers.
/// </para>
/// </remarks>
internal static class Recurring
{
    private static readonly Lock gate = new();
    private static readonly List<Sample> samples = [];

    /// <summary>Whether a firing appends a sample.</summary>
    public static bool Capturing { get; set; }

    /// <summary>Starts a fresh capture window.</summary>
    public static void Reset()
    {
        lock (gate)
        {
            samples.Clear();
        }
    }

    /// <summary>Records one firing.</summary>
    /// <param name="scheduledFor">
    /// What the engine says the firing was scheduled for, or <see langword="null" /> when the engine
    /// does not say — in which case the nearest whole second to the firing is used.
    /// </param>
    public static void Record(DateTimeOffset? scheduledFor)
    {
        if (!Capturing)
        {
            return;
        }

        DateTimeOffset actual = DateTimeOffset.UtcNow;
        DateTimeOffset nominal = scheduledFor ?? NearestSecond(actual);

        lock (gate)
        {
            samples.Add(new Sample(nominal, actual));
        }
    }

    /// <summary>What the capture saw, as the published table's columns.</summary>
    public static Accuracy Summarise(int schedules, TimeSpan window)
    {
        Sample[] taken;
        lock (gate)
        {
            taken = [.. samples];
        }

        if (taken.Length == 0)
        {
            return new Accuracy(0, schedules * (int) window.TotalSeconds, 0, 0, TimeSpan.Zero, TimeSpan.Zero);
        }

        int within50 = 0;
        int within250 = 0;
        TimeSpan worst = TimeSpan.Zero;

        foreach (Sample sample in taken)
        {
            TimeSpan deviation = sample.Actual - sample.ScheduledFor;
            TimeSpan magnitude = deviation < TimeSpan.Zero ? -deviation : deviation;

            if (magnitude <= TimeSpan.FromMilliseconds(50))
            {
                within50++;
            }

            if (magnitude <= TimeSpan.FromMilliseconds(250))
            {
                within250++;
            }

            if (magnitude > worst)
            {
                worst = magnitude;
            }
        }

        Sample last = taken[^1];

        return new Accuracy(
            taken.Length,
            schedules * (int) window.TotalSeconds,
            within50,
            within250,
            worst,
            last.Actual - last.ScheduledFor);
    }

    private static DateTimeOffset NearestSecond(DateTimeOffset value)
    {
        long ticks = value.UtcTicks;
        long remainder = ticks % TimeSpan.TicksPerSecond;
        long rounded = remainder >= TimeSpan.TicksPerSecond / 2
            ? ticks - remainder + TimeSpan.TicksPerSecond
            : ticks - remainder;

        return new DateTimeOffset(rounded, TimeSpan.Zero);
    }

    private readonly record struct Sample(DateTimeOffset ScheduledFor, DateTimeOffset Actual);

    /// <summary>One engine's row in the recurring-accuracy table.</summary>
    internal readonly record struct Accuracy(
        int Fired,
        int Expected,
        int Within50Ms,
        int Within250Ms,
        TimeSpan MaxDeviation,
        TimeSpan LastDeviation)
    {
        public string Describe(string engine)
        {
            int fired = Fired;
            string percent(int part) => fired == 0
                ? "-"
                : (100.0 * part / fired).ToString("F1", CultureInfo.InvariantCulture) + "%";

            return string.Create(CultureInfo.InvariantCulture,
                $"| {engine,-31} | {Fired,7:N0} | {Expected,7:N0} | {percent(Within50Ms),7} | {percent(Within250Ms),7} | {MaxDeviation.TotalMilliseconds,10:F1} | {LastDeviation.TotalMilliseconds,10:F1} |");
        }
    }
}
