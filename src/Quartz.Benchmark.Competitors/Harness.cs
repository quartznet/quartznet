using System.Diagnostics;
using System.Globalization;

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;

namespace Quartz.Benchmark.Competitors;

/// <summary>
/// The settings every arm of this harness shares, and the two bits of timing every throughput
/// scenario needs.
/// </summary>
internal static class Harness
{
    /// <summary>
    /// The worker limit on every engine, and the one number the three libraries disagree about by
    /// default.
    /// </summary>
    /// <remarks>
    /// Ten is Quartz's shipped default. TickerQ's is <see cref="Environment.ProcessorCount" /> (32 on
    /// the machine these numbers were taken on) and Hangfire's is <c>ProcessorCount * 5</c> (160), so
    /// both are set explicitly — a row whose engine had sixteen times the workers would not be a row
    /// about the scheduler.
    /// </remarks>
    public const int MaxConcurrency = 10;

    /// <summary>
    /// The first whole second at or after <c>now + lead</c>, which is when a throughput scenario's work
    /// falls due.
    /// </summary>
    /// <remarks>
    /// <b>The alignment is not cosmetic.</b> Hangfire records a scheduled job's due time as a whole-second
    /// Unix timestamp — <c>ScheduledState.Handler.Apply</c> stores <c>JobHelper.ToTimestamp(EnqueueAt)</c>
    /// as the score and <c>DelayedJobScheduler</c> compares it against <c>ToTimestamp(now)</c> — so a job
    /// due at a fractional instant becomes eligible at the start of the second containing it, which is up
    /// to a second early. Unaligned, Hangfire ran a fifth of a twenty-thousand batch before the measured
    /// window opened. TickerQ buckets by whole second too: <c>GetEarliestTimeTickers</c> takes the
    /// earliest due ticker's second and returns everything inside it. Aligning to a second boundary is
    /// what makes "due at this instant" mean the same thing to all three.
    /// </remarks>
    public static DateTimeOffset DueAt(TimeSpan lead)
    {
        long ticks = (DateTimeOffset.UtcNow + lead).UtcTicks;
        long remainder = ticks % TimeSpan.TicksPerSecond;

        return new DateTimeOffset(remainder == 0 ? ticks : ticks - remainder + TimeSpan.TicksPerSecond, TimeSpan.Zero);
    }

    /// <summary>
    /// Sleeps until <paramref name="instant" />, finishing on a spin so that the measured window starts
    /// at the due time rather than at whatever the scheduler quantum gave back.
    /// </summary>
    public static void WaitUntil(DateTimeOffset instant)
    {
        while (true)
        {
            TimeSpan remaining = instant - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            if (remaining > TimeSpan.FromMilliseconds(16))
            {
                Thread.Sleep(remaining - TimeSpan.FromMilliseconds(8));
            }
            else
            {
                Thread.SpinWait(200);
            }
        }
    }

    /// <summary>
    /// Fails the run if the scheduling took so long that the work became due before the measured window
    /// opened.
    /// </summary>
    /// <remarks>
    /// Every throughput row is "wall clock from the due instant to the last of N executions". The
    /// scheduling happens in the iteration setup, which is not measured, so an engine whose scheduling
    /// overran the lead time would have drained part of its batch for free. Raising the lead time is the
    /// fix; silently publishing the number is not, so this throws rather than warns.
    /// </remarks>
    public static void EnsureScheduledBeforeDue(DateTimeOffset dueAt, string engine)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now >= dueAt)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                $"{engine} took longer to schedule than the lead time: the work became due {(now - dueAt).TotalMilliseconds:F0} ms before the measured window opened. Raise the lead time rather than publishing the number."));
        }
    }

    /// <summary>
    /// Fails the run if the engine executed jobs before the measured window opened.
    /// </summary>
    /// <remarks>
    /// The other half of <see cref="EnsureScheduledBeforeDue" />, and the one that catches an engine
    /// deciding a job due in two seconds is due now. An execution before the window is counted towards
    /// the target but its time is not measured, so an engine that ran the whole batch early would report
    /// a drain of nothing at all — which is exactly what an unguarded Hangfire row did on the first
    /// sitting. A handful is tolerated; a share of the batch is not.
    /// </remarks>
    public static void EnsureNothingRanEarly(int expected, string engine)
    {
        long already = Completion.Completed;
        if (already * 100 > expected)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                $"{engine} ran {already} of {expected} executions before the measured window opened, so the drain this would report is not one. The row's due time is not being honoured."));
        }
    }

    /// <summary>Microseconds between two <see cref="Stopwatch.GetTimestamp" /> readings.</summary>
    public static double Microseconds(long from, long to)
    {
        return (to - from) * 1_000_000.0 / Stopwatch.Frequency;
    }

    /// <summary>The percentile of an already-sorted sample, by nearest rank.</summary>
    public static double Percentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 0)
        {
            return double.NaN;
        }

        int rank = (int) Math.Ceiling(percentile / 100.0 * sorted.Length) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
    }
}

/// <summary>
/// The job the throughput scenarios run under: one invocation is one whole drain, so it is counted
/// rather than sampled.
/// </summary>
/// <remarks>
/// <see cref="RunStrategy.Monitoring" /> because the workload cannot be repeated inside an invocation —
/// the engine is rebuilt and refilled between them — and because BenchmarkDotNet's pilot has nothing to
/// decide when an invocation is a fixed twenty thousand executions. One invocation per iteration, and
/// the unroll factor has to follow it down or the generated loop would run the body sixteen times.
/// </remarks>
internal sealed class ThroughputConfig : ManualConfig
{
    public ThroughputConfig()
    {
        if (ScenarioConfig.DeferToSmokeRun(this))
        {
            return;
        }

        AddJob(Job.Default
            .WithStrategy(RunStrategy.Monitoring)
            .WithWarmupCount(1)
            .WithIterationCount(7)
            .WithInvocationCount(1)
            .WithUnrollFactor(1)
            .DontEnforcePowerPlan());
    }
}

/// <summary>
/// The same, with fewer iterations, for the scenario whose every iteration is thousands of round
/// trips to a database and takes the better part of a minute.
/// </summary>
internal sealed class DatabaseThroughputConfig : ManualConfig
{
    public DatabaseThroughputConfig()
    {
        if (ScenarioConfig.DeferToSmokeRun(this))
        {
            return;
        }

        AddJob(Job.Default
            .WithStrategy(RunStrategy.Monitoring)
            .WithWarmupCount(1)
            .WithIterationCount(5)
            .WithInvocationCount(1)
            .WithUnrollFactor(1)
            .DontEnforcePowerPlan());
    }
}

/// <summary>
/// The job the latency scenario runs under: two hundred single schedules on an idle engine.
/// </summary>
internal sealed class LatencyConfig : ManualConfig
{
    public LatencyConfig()
    {
        if (ScenarioConfig.DeferToSmokeRun(this))
        {
            return;
        }

        AddJob(Job.Default
            .WithStrategy(RunStrategy.Monitoring)
            .WithWarmupCount(20)
            .WithIterationCount(200)
            .WithInvocationCount(1)
            .WithUnrollFactor(1)
            .DontEnforcePowerPlan());

        AddColumn(BenchmarkDotNet.Columns.StatisticColumn.P50);
        AddColumn(BenchmarkDotNet.Columns.StatisticColumn.P95);
    }
}

/// <summary>
/// The job the per-schedule cost scenario runs under: fifty thousand schedules into a store that
/// started empty, which is <c>ScheduleJobBenchmark</c>'s arrangement.
/// </summary>
internal sealed class ScheduleCostConfig : ManualConfig
{
    public ScheduleCostConfig()
    {
        if (ScenarioConfig.DeferToSmokeRun(this))
        {
            return;
        }

        AddJob(Job.Default
            .WithStrategy(RunStrategy.Monitoring)
            .WithWarmupCount(1)
            .WithIterationCount(5)
            .WithInvocationCount(1)
            .WithUnrollFactor(1)
            .DontEnforcePowerPlan());
    }
}

/// <summary>
/// How a scenario's own BenchmarkDotNet job gets out of the smoke run's way.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ConfigUnionRule" /> is read off the <em>local</em> config — the one an attribute
/// supplies — rather than off the one the command line passes, so a smoke configuration cannot declare
/// that it wins. Left alone, BenchmarkDotNet unions the two and runs every case twice: once under the
/// scenario's measuring job and once under the smoke run's dry one, which makes a liveness check as
/// expensive as a measurement.
/// </para>
/// <para>
/// So the scenario configs ask, and when the run is a smoke run they add nothing and say the global
/// config wins outright. The flag is set in <c>Main</c> before the switcher is handed anything, and
/// these constructors run well after that.
/// </para>
/// </remarks>
internal static class ScenarioConfig
{
    /// <summary>Whether this process was started as a smoke run.</summary>
    public static bool SmokeRun { get; set; }

    public static bool DeferToSmokeRun(ManualConfig config)
    {
        if (!SmokeRun)
        {
            return false;
        }

        config.UnionRule = ConfigUnionRule.AlwaysUseGlobal;
        return true;
    }
}
