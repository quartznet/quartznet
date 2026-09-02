using System.Globalization;

namespace Quartz.Benchmark;

/// <summary>
/// The workload the fire-throughput benchmarks measure, and the counting that turns it into a
/// per-fire number. Shared by the <c>RAMJobStore</c> arm and the PostgreSQL one, which differ in
/// nothing but the store they are pointed at.
/// </summary>
/// <remarks>
/// <para>
/// The scheduler is built and started once, in <c>[GlobalSetup]</c>, and left running for the whole
/// case; a benchmark invocation is <em>waiting for the next N fires to happen</em>. That is what makes
/// <c>Mean</c> the time one fire costs and, with <c>OperationsPerInvoke</c> set to the same N, what
/// makes fires per second <c>1e9 / Mean(ns)</c>. The alternative — building a scheduler per iteration
/// and timing it from <c>Start</c> to the last fire — measures a scheduler starting up as much as a
/// scheduler firing, and at these rates the startup is the larger half.
/// </para>
/// <para>
/// The same arrangement is what makes <c>[MemoryDiagnoser]</c> mean something here. BenchmarkDotNet
/// reads <c>GC.GetTotalAllocatedBytes</c>, which is process-wide rather than per-thread, so the
/// allocations of the acquisition loop and of every worker running a job land in the measured window
/// along with the caller's. A steady-state scheduler is therefore measured whole: the
/// <c>Allocated</c> column is what one firing costs the process, not what one thread of it cost.
/// </para>
/// <para>
/// <b>Every trigger is permanently overdue.</b> They repeat indefinitely at <see cref="RepeatInterval" />
/// under <see cref="MisfireInstruction.IgnoreMisfirePolicy" />, so each firing advances the next fire
/// time by a millisecond and leaves it in the past — the scheduler never waits, and the misfire handler
/// never looks at them (both the ADO scan and the acquisition filter special-case
/// <c>MISFIRE_INSTR = -1</c>).
/// This is <c>SchedulerBenchmark</c>'s arrangement, for its reason: what is being measured is the fire
/// path, and a benchmark that spent its time asleep would be measuring the clock.
/// </para>
/// </remarks>
internal static class FireThroughput
{
    /// <summary>
    /// How many fires one invocation of the <c>RAMJobStore</c> arm waits for. Large enough that the
    /// wait dominates the handful of instructions around it, small enough that BenchmarkDotNet's pilot
    /// can still fit several invocations into an iteration.
    /// </summary>
    public const int RamFiresPerInvocation = 2_000;

    /// <summary>
    /// The same for the PostgreSQL arm, which is orders of magnitude slower per fire because every one
    /// of them is round trips to a database.
    /// </summary>
    public const int AdoFiresPerInvocation = 250;

    /// <summary>How many triggers are in flight, spread over <see cref="JobCount" /> jobs.</summary>
    /// <remarks>
    /// A schedule is triggers over a handful of jobs rather than one trigger each, and the store reads
    /// the job on every firing either way. The count is what decides the ceiling: a trigger that
    /// repeats every <see cref="RepeatInterval" /> can sustain a thousand firings a second on its own,
    /// so two thousand of them put the arrangement's own limit at two million a second — several times
    /// what the fastest arm here reaches, which is what keeps the number a measurement of the scheduler
    /// rather than of the schedule.
    /// </remarks>
    private const int TriggerCount = 2_000;

    private const int JobCount = 100;

    /// <summary>
    /// How far each firing advances its trigger, and the smallest interval a persistent store can
    /// carry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>StdAdoDelegate.GetDbTimeSpanValue</c> stores a <see cref="TimeSpan" /> as whole
    /// milliseconds, so anything shorter than one is persisted as zero — and a simple trigger read
    /// back with a zero repeat interval throws <see cref="DivideByZeroException" /> out of
    /// <c>GetFireTimeAfter</c> on its next firing, which the store logs and swallows, leaving the row
    /// stuck in <c>ACQUIRED</c>. This benchmark found that; it is filed as #3673 rather than worked around, and
    /// a millisecond is simply the smallest interval both stores agree on.
    /// </para>
    /// <para>
    /// It has to be short for the reason the interval always did: a firing advances the trigger by one
    /// of these, so as long as a trigger fires fewer than a thousand times a second it stays overdue
    /// and the scheduler never waits.
    /// </para>
    /// </remarks>
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// The group everything this benchmark creates lives in, so the ADO arm can delete exactly its own
    /// rows without disturbing whatever else the shared benchmark database holds.
    /// </summary>
    public const string Group = "fireThroughput";

    /// <summary>
    /// Batching only batches with a window: the store stops a batch at the first trigger's fire time
    /// plus this, so at the shipped default of zero a batch is one trigger whatever
    /// <c>MaxBatchSize</c> says. A second is far more than the span these triggers occupy, so the
    /// batch is bounded by the pool rather than by the clock.
    /// </summary>
    public static readonly TimeSpan FireAheadWindow = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long a wait for fires may go without completing before it is called a broken harness. A
    /// benchmark that hangs is indistinguishable from a slow one, and the smoke run has nobody
    /// watching it.
    /// </summary>
    private static readonly TimeSpan FireTimeout = TimeSpan.FromMinutes(2);

    private static long fireCount;

    /// <summary>
    /// The wait in progress, published for the job to see. Null between invocations.
    /// </summary>
    private static volatile Waiter? pending;

    /// <summary>
    /// Blocks until <paramref name="count" /> more fires have happened, and is the whole body of every
    /// benchmark method here.
    /// </summary>
    /// <remarks>
    /// The target is absolute and is published before it is checked, so a fire that lands between
    /// reading the counter and publishing cannot be lost: either it arrives after publication and sets
    /// the event, or it arrived before and the re-read below sees it. Waiting on an event rather than
    /// spinning matters — a spin would take a core away from the pool whose throughput is the
    /// measurement.
    /// </remarks>
    public static void AwaitFires(int count)
    {
        Waiter waiter = new(Interlocked.Read(ref fireCount) + count);
        pending = waiter;

        if (Interlocked.Read(ref fireCount) >= waiter.Target)
        {
            waiter.Done.Set();
        }

        bool completed = waiter.Done.Wait(FireTimeout);
        pending = null;

        if (!completed)
        {
            // Read only on the way out: everything between the two statements above is inside the
            // measured window, and this is a diagnostic for the case where there is no measurement.
            long seen = count - (waiter.Target - Interlocked.Read(ref fireCount));

            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                $"Waited {FireTimeout} for {count} firings and saw {seen}. The scheduler stopped firing, which is a broken harness rather than a slow one."));
        }
    }

    /// <summary>Records a firing, and releases the wait it completes.</summary>
    private static void Fired()
    {
        long count = Interlocked.Increment(ref fireCount);
        Waiter? waiter = pending;
        if (waiter is not null && count >= waiter.Target)
        {
            waiter.Done.Set();
        }
    }

    /// <summary>
    /// Builds a scheduler over the store <paramref name="configureStore" /> selects, schedules the
    /// workload on it, starts it and waits until it is firing.
    /// </summary>
    /// <param name="instanceName">
    /// The scheduler name, which on a persistent store is also the row key everything is written under.
    /// </param>
    /// <param name="maxConcurrency">
    /// The thread pool's permit count. <c>MaxBatchSize</c> is set to the same number: the scheduler
    /// refuses a batch larger than the pool that would have to run it, so across a 10-and-50 sweep the
    /// two cannot be varied independently and the batch tracks the pool.
    /// </param>
    /// <param name="configureStore">Selects the job store; everything else is the same on both arms.</param>
    public static async Task<IScheduler> StartScheduler(
        string instanceName,
        int maxConcurrency,
        Action<IQuartzBuilder> configureStore)
    {
        QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create(quartz =>
        {
            quartz.ConfigureScheduler(options =>
            {
                options.InstanceName = instanceName;
                options.InstanceId = "NODE-01";

                // Never reached: the triggers below are always due. It is short anyway so that a run
                // which somehow did idle would end rather than sit out the default half minute.
                options.IdleWaitTime = TimeSpan.FromSeconds(1);
                options.MaxBatchSize = maxConcurrency;
                options.BatchTriggerAcquisitionFireAheadTimeWindow = FireAheadWindow;
            });

            quartz.UseDefaultThreadPool(maxConcurrency);
            configureStore(quartz);
        });

        IScheduler scheduler = await builder.BuildScheduler().ConfigureAwait(false);

        Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> schedule = [];
        for (int i = 0; i < JobCount; i++)
        {
            IJobDetail job = JobBuilder.Create<NoOpJob>()
                .WithIdentity("job-" + i.ToString(CultureInfo.InvariantCulture), Group)
                .Build();

            int triggersForThisJob = TriggerCount / JobCount;
            ITrigger[] triggers = new ITrigger[triggersForThisJob];
            for (int j = 0; j < triggersForThisJob; j++)
            {
                triggers[j] = TriggerBuilder.Create()
                    .WithIdentity(string.Create(CultureInfo.InvariantCulture, $"trigger-{i}-{j}"), Group)
                    .ForJob(job)
                    .StartNow()
                    .WithSimpleSchedule(simple => simple
                        .RepeatForever()
                        .WithInterval(RepeatInterval)
                        .WithMisfireInstruction(SimpleTriggerMisfireInstruction.IgnoreMisfires))
                    .Build();
            }

            schedule.Add(job, triggers);
        }

        await scheduler.ScheduleJobs(schedule).ConfigureAwait(false);
        await scheduler.Start().ConfigureAwait(false);

        // Steady state before the first measurement: the pool has to be full and the store warm, or
        // the first iteration measures a scheduler waking up.
        AwaitFires(TriggerCount);

        return scheduler;
    }

    /// <summary>
    /// Shuts a scheduler down without waiting for the jobs in flight, which do nothing and would only
    /// add a round trip each to the teardown.
    /// </summary>
    public static async Task StopScheduler(IScheduler scheduler)
    {
        await scheduler.Shutdown(waitForJobsToComplete: false).ConfigureAwait(false);
    }

    /// <summary>
    /// The job under measurement: it records that it ran and returns. Everything the <c>Mean</c>
    /// column holds is therefore the scheduler's and the store's.
    /// </summary>
    public sealed class NoOpJob : IJob
    {
        /// <summary>Counts the firing and releases whatever wait it completes.</summary>
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Fired();
            return default;
        }
    }

    /// <summary>One benchmark invocation's wait: the absolute fire count it is waiting for.</summary>
    private sealed class Waiter(long target)
    {
        public long Target { get; } = target;

        public ManualResetEventSlim Done { get; } = new(false);
    }
}
