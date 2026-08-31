using BenchmarkDotNet.Attributes;

namespace Quartz.Benchmark;

/// <summary>
/// What it costs to put one new job and one new trigger into a running scheduler backed by
/// <c>RAMJobStore</c>.
/// </summary>
/// <remarks>
/// <para>
/// TickerQ's <c>JobCreationComparison</c> puts this at 4.4 µs and 2.3 KB for a simple trigger and
/// 31 µs and 38.7 KB for a cron one — a seven-fold gap between two calls that differ only in which
/// schedule the trigger carries. That run was taken against Quartz 3.14, before the bitmask cron
/// fields (#3126-#3129) and before 4.0's rebuilt <see cref="Quartz.CronExpression" />, which is what
/// the gap was largely made of, so it says nothing about this branch. This reproduces the shape of
/// their measurement: a scheduler that has been started, a fresh identity for every schedule, and the
/// whole <see cref="IScheduler.ScheduleJob(IJobDetail, ITrigger, CancellationToken)" /> call rather
/// than the store write inside it.
/// </para>
/// <para>
/// <b>Why a loop and a cleared store.</b> Scheduling under a fresh identity every time means the store
/// only grows, and left to a default run it reaches millions of entries — which measures a dictionary
/// and a sorted set growing rather than the call. Clearing it in <see cref="IterationSetup" /> caps
/// that, but BenchmarkDotNet answers an iteration setup by dropping to one invocation an iteration,
/// which would time a single cold call. So the operations are counted here instead, the way
/// <see cref="RAMJobStoreBenchmark" /> counts its own: one invocation is
/// <see cref="SchedulesPerInvocation" /> schedules into a store that started empty, which is long
/// enough to measure and small enough to stay a plausible scheduler.
/// </para>
/// <para>
/// The scheduler is started, as the published comparison starts theirs, so the signal each schedule
/// sends the scheduler thread and that thread's answering acquisition round are inside the number.
/// Nothing fires during a run: every trigger's first fire is at least half a minute out, and no
/// iteration lasts that long.
/// </para>
/// <para>
/// The two cases that build a job and a trigger without storing either are in
/// <see cref="JobAndTriggerBuilderBenchmark" />, because they are nanosecond-scale and do not want any
/// of this.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class ScheduleJobBenchmark
{
    /// <summary>Every five minutes, the schedule the published comparison uses.</summary>
    private const string CronSchedule = "0 0/5 * * * ?";

    private const string Group = "bench";

    /// <summary>
    /// How many jobs one measured invocation schedules. Enough that an iteration is a couple of
    /// hundred milliseconds rather than the microsecond BenchmarkDotNet warns about, and few enough
    /// that the store it fills stays a size a real scheduler reaches.
    /// </summary>
    private const int SchedulesPerInvocation = 50_000;

    /// <summary>
    /// How far out the simple trigger's single fire is. Long enough that nothing fires while an
    /// iteration is running, and the value the published comparison uses.
    /// </summary>
    private static readonly TimeSpan simpleTriggerDelay = TimeSpan.FromSeconds(30);

    private IScheduler scheduler = null!;
    private int counter;

    [GlobalSetup]
    public void GlobalSetup()
    {
        scheduler = QuartzSchedulerBuilder
            .Create(q => q
                .ConfigureScheduler(options =>
                {
                    // Named for this benchmark because the smoke run puts every benchmark in the assembly
                    // in one process, and the scheduler repository refuses a second scheduler that shares
                    // a name and an instance id with one already bound.
                    options.InstanceName = nameof(ScheduleJobBenchmark);
                    options.InstanceId = nameof(ScheduleJobBenchmark);
                })
                .UseInMemoryStore())
            .BuildScheduler()
            .GetAwaiter().GetResult();

        scheduler.Start().GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        scheduler.Shutdown(waitForJobsToComplete: false).GetAwaiter().GetResult();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        scheduler.Clear().GetAwaiter().GetResult();
    }

    [Benchmark(OperationsPerInvoke = SchedulesPerInvocation)]
    public void ScheduleJob_SimpleTrigger()
    {
        for (int i = 0; i < SchedulesPerInvocation; i++)
        {
            int id = Interlocked.Increment(ref counter);

            IJobDetail job = JobBuilder.Create<NoOpJob>()
                .WithIdentity($"simple-job-{id}", Group)
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity($"simple-trigger-{id}", Group)
                .StartAt(DateTimeOffset.UtcNow.Add(simpleTriggerDelay))
                .Build();

            scheduler.ScheduleJob(job, trigger).GetAwaiter().GetResult();
        }
    }

    [Benchmark(OperationsPerInvoke = SchedulesPerInvocation)]
    public void ScheduleJob_CronTrigger()
    {
        for (int i = 0; i < SchedulesPerInvocation; i++)
        {
            int id = Interlocked.Increment(ref counter);

            IJobDetail job = JobBuilder.Create<NoOpJob>()
                .WithIdentity($"cron-job-{id}", Group)
                .UsingJobData("message", "hello")
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity($"cron-trigger-{id}", Group)
                .WithCronSchedule(CronSchedule)
                .Build();

            scheduler.ScheduleJob(job, trigger).GetAwaiter().GetResult();
        }
    }

    private sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }
}
