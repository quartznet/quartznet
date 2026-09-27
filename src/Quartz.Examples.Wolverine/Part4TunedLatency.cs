namespace Quartz.Examples.Wolverine;

/*
 * Part 4 — the three settings people reach for when a scheduler sits in front of a message bus, and
 * what they actually do.
 *
 * The reflex is to see IdleWaitTime's 30-second default beside Wolverine's 5-second
 * ScheduledJobPollingTime and conclude that Quartz is six times slower at delivering a due message.
 * That reading is wrong, and the code says why.
 *
 *   - QuartzSchedulerThread acquires triggers due within the next IdleWaitTime, not triggers due now:
 *     `NoLaterThan = now + IdleWaitTime`. Having acquired one it waits out the exact fire time rather
 *     than sleeping the full interval.
 *
 *   - Every in-process mutation — ScheduleJob, AddTrigger, RescheduleJob, DeleteJob — calls
 *     SignalSchedulingChange, which wakes the loop unless the trigger is due after its next look, which
 *     finds it anyway. A trigger scheduled from a Wolverine handler through this process's own
 *     IScheduler therefore never waits for the next sweep; the scheduling call wakes the loop.
 *
 *   - So IdleWaitTime bounds *discovery of work this node did not learn about in process*: a trigger
 *     another node wrote to the shared database, or one recovered from a node that died. It is a
 *     cross-node pickup bound and the look-ahead horizon of one acquisition. It is not the resolution
 *     of in-process scheduling, and lowering it does not make a locally scheduled job fire sooner.
 *
 * What is worth tuning, and why:
 *
 *   - IdleWaitTime, only if a *clustered* deployment needs another node's triggers picked up faster
 *     than 30 s. The cost is a database round trip per node per interval. The minimum is one second.
 *
 *   - MaxBatchSize defaults to automatic: the thread pool's size on a persistent store that is not
 *     clustered, one on a clustered store and in memory. A batch ends at the later of now and the first
 *     trigger's fire time, plus the fire-ahead window, so at the default window it takes every trigger
 *     already due and nothing that is not. A clustered, bus-facing scheduler that fires many small jobs
 *     at once may want it raised, bounded by ThreadPoolOptions.MaxConcurrency.
 *
 *   - BatchTriggerAcquisitionFireAheadTimeWindow defaults to TimeSpan.Zero. Widening it also batches
 *     triggers due shortly after the first, and fires them early by up to the window. Set it to the
 *     spread you are willing to fire early by.
 *
 * The values below are the ones a Wolverine-facing scheduler plausibly wants, not the ones this
 * example needs. The example would behave identically on the defaults.
 */

/// <summary>
/// The latency settings, as a single call so the page can quote it and the compiler can check it.
/// </summary>
public static class Part4TunedLatency
{
    public static void Register(IQuartzBuilder q)
    {
        q.ConfigureScheduler(options =>
        {
            // Default 30 s. Only affects how quickly this node notices triggers it did not schedule
            // itself, so it is a clustering setting, not a latency setting.
            options.IdleWaitTime = TimeSpan.FromSeconds(10);

            // Default automatic: the pool on a persistent store that is not clustered, 1 otherwise.
            // Must not exceed ThreadPoolOptions.MaxConcurrency, which defaults to 10.
            options.MaxBatchSize = 10;

            // Default TimeSpan.Zero, which batches only triggers already due. This also batches
            // triggers due up to half a second after the first, and fires them that much early.
            options.BatchTriggerAcquisitionFireAheadTimeWindow = TimeSpan.FromMilliseconds(500);
        });
    }
}
