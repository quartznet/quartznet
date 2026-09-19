using System.Globalization;

namespace Quartz.Benchmark;

/// <summary>
/// Which settings the scheduler under measurement is built with.
/// </summary>
/// <remarks>
/// Both are measured, and the pair is the point: a reader comparing a number against their own
/// deployment needs to know whether it came from the settings they have or from settings somebody
/// chose for a benchmark.
/// </remarks>
public enum OneOffProfile
{
    /// <summary>
    /// The shipped defaults — <c>MaxBatchSize</c> 1 and a zero fire-ahead window — which is what
    /// <c>AddQuartz(q =&gt; q.UsePersistentStore(...))</c> gives you.
    /// </summary>
    Defaults,

    /// <summary>
    /// <c>MaxBatchSize</c> = the pool size, with the fire-ahead window left at the shipped zero.
    /// </summary>
    /// <remarks>
    /// The arm that prices #3824's first candidate on its own. A zero window does not mean "one trigger
    /// a batch": the store ends a batch at <c>max(now, the first trigger's fire time) + window</c>, so
    /// at a window of zero a <em>backlog</em> — every trigger already due — still batches, and only a
    /// trigger due later is left for the next round. Nothing fires early, which is the whole of what
    /// widening the window costs.
    /// </remarks>
    BatchOnly,

    /// <summary>
    /// <c>MaxBatchSize</c> = the pool size and a one-second fire-ahead window, which is what
    /// <see cref="FireThroughputPostgresBenchmark" /> uses and what the README's numbers were taken at.
    /// </summary>
    Tuned,
}

/// <summary>
/// The <em>one-off</em> fire workload: a durable job per job type and one single-shot trigger per
/// firing, which is the row shape <c>IScheduler.ScheduleJob&lt;TJob, TInput&gt;</c> and every
/// "enqueue this for later" caller produces.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FireThroughput" /> is the other shape: a repeating trigger that is never deleted, which
/// is what the README's 162 firings a second measures. A one-off costs more than that number suggests,
/// because completing it deletes the trigger rather than writing it forward — and that difference is
/// what #3824 is about, so it gets a workload of its own rather than a parameter on that one.
/// </para>
/// <para>
/// <b>Every firing is seeded again.</b> A one-off is consumed by firing, so the workload cannot run
/// in a steady state the way the repeating one does: the triggers are re-scheduled before each
/// measured drain, which is why this is a <c>RunStrategy.Monitoring</c> benchmark with an
/// <c>[IterationSetup]</c> rather than a throughput one.
/// </para>
/// <para>
/// <b>The seeding is not in the measurement.</b> The triggers are scheduled to be due at a common
/// instant a short lead away, and the setup blocks until that instant, so what the measured body
/// holds is the drain and nothing else.
/// </para>
/// </remarks>
internal static class OneOffThroughput
{
    /// <summary>The group every trigger this workload creates lives in.</summary>
    public const string Group = "oneOff";

    /// <summary>
    /// How long after scheduling the firings are due, which has to cover the scheduling itself.
    /// </summary>
    /// <remarks>
    /// Scheduling several thousand triggers one at a time against a real database takes seconds, and a
    /// trigger that came due while the rest were still being written would be fired inside the setup
    /// and never counted. The lead is checked rather than assumed: <see cref="Seed" /> throws when the
    /// scheduling overran it.
    /// </remarks>
    public static readonly TimeSpan Lead = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How long a drain may go without completing before it is called a broken harness rather than a
    /// slow one.
    /// </summary>
    private static readonly TimeSpan drainTimeout = TimeSpan.FromMinutes(5);

    private static long fireCount;

    private static volatile Waiter? pending;

    /// <summary>The payload one firing carries, which is what puts a job-data blob on the trigger row.</summary>
    internal sealed record OneOffInput(string Reference);

    /// <summary>
    /// The job under measurement: it records that it ran and returns, so what is measured is the
    /// scheduler and the store.
    /// </summary>
    public sealed class CountingJob : IJob<OneOffInput>
    {
        public ValueTask Execute(IJobExecutionContext context, OneOffInput input, CancellationToken cancellationToken = default)
        {
            Fired();
            return default;
        }
    }

    /// <summary>
    /// Builds and starts a scheduler over the store <paramref name="configureStore" /> selects, at the
    /// settings <paramref name="profile" /> names.
    /// </summary>
    public static async Task<IScheduler> StartScheduler(
        string instanceName,
        int maxConcurrency,
        OneOffProfile profile,
        Action<IQuartzBuilder> configureStore)
    {
        OneOffProfile chosen = profile;
        QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create(quartz =>
        {
            quartz.ConfigureScheduler(options =>
            {
                options.InstanceName = instanceName;
                options.InstanceId = "NODE-01";
                options.IdleWaitTime = TimeSpan.FromSeconds(1);

                if (chosen != OneOffProfile.Defaults)
                {
                    options.MaxBatchSize = maxConcurrency;
                }

                if (chosen == OneOffProfile.Tuned)
                {
                    options.BatchTriggerAcquisitionFireAheadTimeWindow = TimeSpan.FromSeconds(1);
                }
            });

            quartz.UseDefaultThreadPool(maxConcurrency);
            configureStore(quartz);
        });

        IScheduler scheduler = await builder.BuildScheduler().ConfigureAwait(false);
        await scheduler.Start().ConfigureAwait(false);
        return scheduler;
    }

    /// <summary>
    /// Schedules <paramref name="count" /> one-off firings due at a common instant, through the same
    /// extension an application calls, and blocks until that instant.
    /// </summary>
    /// <returns>The instant they were all due at.</returns>
    public static async Task<DateTimeOffset> Seed(IScheduler scheduler, int count)
    {
        Interlocked.Exchange(ref fireCount, 0);

        DateTimeOffset dueAt = scheduler.TimeProvider.GetUtcNow() + Lead;

        for (int i = 0; i < count; i++)
        {
            string name = "one-off-" + i.ToString(CultureInfo.InvariantCulture);
            await scheduler.ScheduleJob<CountingJob, OneOffInput>(
                new OneOffInput(name),
                dueAt,
                new OneOffJobOptions { Name = name, Group = Group }).ConfigureAwait(false);
        }

        TimeSpan remaining = dueAt - scheduler.TimeProvider.GetUtcNow();
        if (remaining < TimeSpan.Zero)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                $"Scheduling {count} one-off firings took longer than the {Lead.TotalSeconds:F0} s lead, so some of them fired during the setup. Raise {nameof(Lead)} or lower the count."));
        }

        if (Interlocked.Read(ref fireCount) != 0)
        {
            throw new InvalidOperationException("A firing happened before the drain opened, so the measurement would not hold all of them.");
        }

        await Task.Delay(remaining).ConfigureAwait(false);
        return dueAt;
    }

    /// <summary>Blocks until <paramref name="count" /> firings have happened since the last <see cref="Seed" />.</summary>
    public static void Drain(int count)
    {
        Waiter waiter = new(count);
        pending = waiter;

        if (Interlocked.Read(ref fireCount) >= count)
        {
            waiter.Done.Set();
        }

        bool completed = waiter.Done.Wait(drainTimeout);
        pending = null;

        if (!completed)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                $"Waited {drainTimeout} for {count} one-off firings and saw {Interlocked.Read(ref fireCount)}. The scheduler stopped firing, which is a broken harness rather than a slow one."));
        }
    }

    public static async Task StopScheduler(IScheduler scheduler)
    {
        await scheduler.Shutdown(waitForJobsToComplete: false).ConfigureAwait(false);
    }

    private static void Fired()
    {
        long count = Interlocked.Increment(ref fireCount);
        Waiter? waiter = pending;
        if (waiter is not null && count >= waiter.Target)
        {
            waiter.Done.Set();
        }
    }

    private sealed class Waiter(long target)
    {
        public long Target { get; } = target;

        public ManualResetEventSlim Done { get; } = new(false);
    }
}
