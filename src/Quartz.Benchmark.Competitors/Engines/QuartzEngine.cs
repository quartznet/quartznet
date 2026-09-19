using System.Globalization;

using Quartz.Configuration;

namespace Quartz.Benchmark.Competitors.Engines;

/// <summary>
/// Which of the two Quartz rows an engine is.
/// </summary>
/// <remarks>
/// Both are published, and the pair is the point: a reader comparing a table against their own
/// deployment needs to know whether the number came from the settings they have or from settings
/// somebody chose for a benchmark. <see cref="Defaults" /> is what
/// <c>AddQuartz(q =&gt; q.UseInMemoryStore())</c> gives you.
/// </remarks>
internal enum QuartzProfile
{
    /// <summary>
    /// The shipped defaults: <c>MaxBatchSize</c> 1 and a zero fire-ahead window, so one acquisition
    /// round per firing.
    /// </summary>
    Defaults,

    /// <summary>
    /// <c>MaxBatchSize</c> = the pool size and a one-second fire-ahead window, which is what
    /// <c>FireThroughputBenchmark</c> uses and what the 2026-09-02 README numbers were taken at.
    /// </summary>
    /// <remarks>
    /// Neither setting batches anything on its own. The store ends a batch at the first acquired
    /// trigger's own fire time plus the window, so at the shipped window of zero a batch holds only the
    /// triggers due at the same instant; and the scheduler refuses a batch larger than the pool that
    /// would have to run it, so the batch tracks the pool.
    /// </remarks>
    Tuned,
}

/// <summary>How the recurring scenario expresses "every second" on Quartz.</summary>
internal enum QuartzRecurringKind
{
    /// <summary>A simple trigger repeating forever at a one-second interval.</summary>
    Simple,

    /// <summary>The cron expression <c>* * * * * ?</c>.</summary>
    Cron,
}

/// <summary>
/// Quartz.NET, built from the working tree rather than from a published package.
/// </summary>
/// <remarks>
/// <para>
/// <b>What one firing does.</b> The scheduler thread acquires a batch of triggers from the store,
/// waits until the first one's fire time, calls <c>TriggersFired</c> to mark them and read their job
/// details, and hands each to the thread pool inside a <c>JobRunShell</c>. The shell creates a
/// dependency-injection scope, builds the job instance through it, runs the middleware pipeline,
/// notifies any registered listeners, executes the job, then calls <c>TriggeredJobComplete</c> to write
/// the trigger forward and release it. On <c>RAMJobStore</c> that is one monitor and a sorted set and
/// costs about 2.5 KB a firing (#3802's profile); on the ADO store it is about 9.7 statements and 1.24
/// commits.
/// </para>
/// <para>
/// The scheduler is built with <c>QuartzSchedulerBuilder</c>, which creates its own container, so what
/// is measured is the arrangement an application gets from <c>AddQuartz</c> rather than a
/// hand-assembled internal one.
/// </para>
/// </remarks>
internal sealed class QuartzEngine : IEngine
{
    private const string Group = "competitors";

    private readonly QuartzProfile profile;
    private readonly QuartzRecurringKind recurringKind;
    private readonly string instanceName;
    private readonly Action<IQuartzBuilder> configureStore;

    private IScheduler? scheduler;

    public QuartzEngine(
        QuartzProfile profile,
        string instanceName,
        Action<IQuartzBuilder> configureStore,
        QuartzRecurringKind recurringKind = QuartzRecurringKind.Simple)
    {
        this.profile = profile;
        this.instanceName = instanceName;
        this.configureStore = configureStore;
        this.recurringKind = recurringKind;
    }

    public string Name => profile == QuartzProfile.Defaults ? "Quartz (defaults)" : "Quartz (tuned)";

    /// <summary>The started scheduler, for the scenarios that call its own API directly.</summary>
    public IScheduler Scheduler => scheduler ?? throw new InvalidOperationException("Start has not been called.");

    public async ValueTask Start(int maxConcurrency, CancellationToken cancellationToken = default)
    {
        QuartzProfile chosen = profile;
        QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create(quartz =>
        {
            quartz.ConfigureScheduler(options =>
            {
                options.InstanceName = instanceName;
                options.InstanceId = "NODE-01";

                if (chosen == QuartzProfile.Tuned)
                {
                    options.MaxBatchSize = maxConcurrency;
                    options.BatchTriggerAcquisitionFireAheadTimeWindow = TimeSpan.FromSeconds(1);
                }
            });

            quartz.UseDefaultThreadPool(maxConcurrency);
            configureStore(quartz);
        });

        scheduler = await builder.BuildScheduler(cancellationToken).ConfigureAwait(false);
        await scheduler.Start(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// <paramref name="count" /> independent one-off schedules, each its own job detail and its own
    /// trigger, through the batch API.
    /// </summary>
    /// <remarks>
    /// <b>One job detail each, not one job detail with N triggers.</b> The other two libraries schedule
    /// independent units — a Hangfire background job carries its own method and arguments, a TickerQ
    /// time ticker its own row — so a Quartz job detail per schedule is the same shape. The other
    /// arrangement is also markedly worse for Quartz and would have made this row look like a scheduler
    /// measurement when it was not: a non-durable job's last trigger being removed makes
    /// <c>RAMJobStore.RemoveTriggerNoLock</c> take a linear <c>List.Remove</c> over that job's trigger
    /// list and materialise the remaining trigger keys into an array, so twenty thousand triggers on one
    /// job cost 83 KB a firing and run quadratically. That is recorded in README.md as a finding rather
    /// than left in the table as a number.
    /// </remarks>
    public async ValueTask ScheduleOneOff(int count, DateTimeOffset dueAt, CancellationToken cancellationToken = default)
    {
        Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> schedule = new(count);

        for (int i = 0; i < count; i++)
        {
            string name = "one-off-" + i.ToString(CultureInfo.InvariantCulture);

            IJobDetail job = JobBuilder.Create<CountingJob>()
                .WithIdentity(name, Group)
                .Build();

            ITrigger trigger = TriggerBuilder.Create<CountingJob>()
                .WithIdentity(name, Group)
                .ForJob(job)
                .StartAt(dueAt)
                .Build();

            schedule.Add(job, [trigger]);
        }

        // The batch API, which is what the other two are given as well.
        await Scheduler.ScheduleJobs(schedule, ScheduleJobOptions.Replacing, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ScheduleRecurring(int count, CancellationToken cancellationToken = default)
    {
        IJobDetail job = JobBuilder.Create<CountingJob>()
            .WithIdentity("recurring", Group)
            .Build();

        // Aligned to the next whole second, so that every schedule on every engine is nominally due on
        // a second boundary and the deviations are comparable.
        DateTimeOffset start = NextSecondBoundary();

        ITrigger[] triggers = new ITrigger[count];
        for (int i = 0; i < count; i++)
        {
            TriggerBuilder<CountingJob> trigger = TriggerBuilder.Create<CountingJob>()
                .WithIdentity("recurring-" + i.ToString(CultureInfo.InvariantCulture), Group)
                .ForJob(job)
                .StartAt(start);

            triggers[i] = recurringKind == QuartzRecurringKind.Simple
                ? trigger.WithSimpleSchedule(simple => simple
                    .RepeatForever()
                    .WithInterval(TimeSpan.FromSeconds(1))
                    .WithMisfireInstruction(SimpleTriggerMisfireInstruction.IgnoreMisfires)).Build()
                : trigger.WithCronSchedule("* * * * * ?", cron => cron
                    .WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing)).Build();
        }

        Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> schedule = new() { [job] = triggers };
        await Scheduler.ScheduleJobs(schedule, ScheduleJobOptions.Replacing, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask Clear(CancellationToken cancellationToken = default)
    {
        await Scheduler.Clear(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// One schedule through <c>IScheduler.ScheduleJob(job, trigger)</c>, which is the whole of what the
    /// per-schedule cost scenario measures.
    /// </summary>
    public async ValueTask ScheduleOne(int index, bool cron, DateTimeOffset dueAt, CancellationToken cancellationToken = default)
    {
        string name = index.ToString(CultureInfo.InvariantCulture);

        IJobDetail job = JobBuilder.Create<CountingJob>()
            .WithIdentity("cost-" + name, Group)
            .Build();

        TriggerBuilder<CountingJob> trigger = TriggerBuilder.Create<CountingJob>()
            .WithIdentity("cost-" + name, Group)
            .StartAt(dueAt);

        ITrigger built = cron
            ? trigger.WithCronSchedule("0 0 12 * * ?").Build()
            : trigger.WithSimpleSchedule(TimeSpan.FromHours(1)).Build();

        await Scheduler.ScheduleJob(job, built, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// One job scheduled for right now, which is the latency scenario's whole measurement.
    /// </summary>
    /// <remarks>
    /// <c>StartNow</c> on a scheduler that has nothing else to do, so the interval measured is a
    /// signal to a parked scheduler thread, an acquisition, a <c>TriggersFired</c> and a hand-off to
    /// the pool.
    /// </remarks>
    public async ValueTask ScheduleNow(int index, CancellationToken cancellationToken = default)
    {
        string name = "now-" + index.ToString(CultureInfo.InvariantCulture);

        IJobDetail job = JobBuilder.Create<CountingJob>()
            .WithIdentity(name, Group)
            .Build();

        ITrigger trigger = TriggerBuilder.Create<CountingJob>()
            .WithIdentity(name, Group)
            .StartNow()
            .Build();

        await Scheduler.ScheduleJob(job, trigger, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (scheduler is not null)
        {
            await scheduler.Shutdown(waitForJobsToComplete: false).ConfigureAwait(false);
            scheduler = null;
        }
    }

    /// <summary>The next whole second, which is where every recurring schedule here starts.</summary>
    internal static DateTimeOffset NextSecondBoundary()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerSecond, TimeSpan.Zero).AddSeconds(1);
    }

    /// <summary>
    /// The job under measurement: it records that it ran and returns. Everything the <c>Mean</c> column
    /// holds is therefore the scheduler's.
    /// </summary>
    public sealed class CountingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Completion.Record();
            Recurring.Record(context.ScheduledFireTimeUtc);
            return default;
        }
    }
}
