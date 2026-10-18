using Microsoft.Extensions.Time.Testing;

namespace Quartz.Tests.Unit;

/// <summary>
/// <see cref="IJobExecutionContext.JobRunTime" /> read while a job runs and the value its listeners see
/// afterwards are counted on the scheduler's clock from the same start (#4097). The value read mid-run used
/// to be the wall clock less the fire time, which a <see cref="FakeTimeProvider" /> could not move.
/// </summary>
/// <remarks>
/// Each job advances the scheduler's fake clock itself, so every duration is exact: nothing else moves a
/// <see cref="FakeTimeProvider" />. The waits for the scheduler are deadlines, never measurements.
/// </remarks>
public sealed class JobRunTimeTest
{
    private static readonly DateTimeOffset start = new(2026, 3, 6, 8, 0, 0, TimeSpan.Zero);

    /// <summary>How long a test waits for the scheduler before calling it stuck. Never a measurement.</summary>
    private static readonly TimeSpan deadline = TimeSpan.FromSeconds(30);

    [Test]
    public async Task TheFinalRunTimeAgreesWithTheClockTheJobReadWhileRunning()
    {
        FakeTimeProvider clock = new(start);
        await using StandaloneSchedulerFactory factory = Build(clock);
        IScheduler scheduler = await factory.GetScheduler();
        RunTimeListener listener = new();
        scheduler.ListenerManager.AddJobListener(listener);
        scheduler.ListenerManager.AddTriggerListener(listener);

        await scheduler.Start();
        await scheduler.ScheduleJob(Job<AdvancingJob>("advancing"), Trigger(clock, "advancing"));

        Observed observed = await ShouldArrive(listener.Executed, "a trigger due now fires without the clock moving");

        observed.BeforeTheRun.Should().Be(TimeSpan.Zero, "a trigger listener hears of the firing before the job has been called");
        observed.MidRun.Should().Be(AdvancingJob.BeforeReading,
            "read mid-run, the run time is what the scheduler's clock has advanced since the run began");
        observed.Final.Should().Be(AdvancingJob.BeforeReading + AdvancingJob.AfterReading,
            "the final value counts from the same start on the same clock, so it is the mid-run value plus what came after it");
    }

    /// <summary>
    /// A refire runs the job again in the same firing, and its run begins again: the value read mid-run is
    /// that attempt's, as the final value always was, rather than the attempt before's measurement.
    /// </summary>
    [Test]
    public async Task ARefiredRunCountsItsOwnAttempt()
    {
        FakeTimeProvider clock = new(start);
        await using StandaloneSchedulerFactory factory = Build(clock);
        IScheduler scheduler = await factory.GetScheduler();
        RunTimeListener listener = new();
        scheduler.ListenerManager.AddJobListener(listener);
        scheduler.ListenerManager.AddTriggerListener(listener);

        await scheduler.Start();
        await scheduler.ScheduleJob(Job<RefiringJob>("refiring"), Trigger(clock, "refiring"));

        Observed observed = await ShouldArrive(listener.Executed, "a trigger due now fires without the clock moving");

        observed.RefireCount.Should().Be(1, "the first attempt asked to be refired and the second completed");
        observed.BeforeTheRun.Should().Be(TimeSpan.FromSeconds(10),
            "the trigger listeners hear of each attempt, and until the second begins the value is the first's measurement");
        observed.MidRun.Should().Be(AdvancingJob.BeforeReading,
            "the second attempt's run began when it was called, not when the first attempt was");
        observed.Final.Should().Be(AdvancingJob.BeforeReading + AdvancingJob.AfterReading,
            "the final value is the attempt that completed");
    }

    private static StandaloneSchedulerFactory Build(TimeProvider clock)
    {
        return QuartzSchedulerBuilder
            .Create(q => q
                .UseTimeProvider(clock)
                .UseInMemoryStore()
                .ConfigureScheduler(options => options.InstanceName = $"run-time-{Guid.NewGuid():N}"))
            .Build();
    }

    private static IJobDetail Job<TJob>(string name) where TJob : IJob
    {
        return JobBuilder.Create<TJob>().WithIdentity(name).Build();
    }

    /// <summary>
    /// A trigger on the test's clock, due at its "now": one built with no clock would take the wall clock's.
    /// </summary>
    private static ITrigger Trigger(TimeProvider clock, string name)
    {
        return TriggerBuilder.Create(clock)
            .WithIdentity(name)
            .ForJob(name)
            .StartAt(clock.GetUtcNow())
            .Build();
    }

    private static async Task<T> ShouldArrive<T>(Task<T> arrival, string because)
    {
        Func<Task<T>> act = () => arrival;
        return (await act.Should().CompleteWithinAsync(deadline, because)).Which;
    }

    /// <summary>
    /// Advances the scheduler's clock, reads its run time into <see cref="IJobExecutionContext.Result" />,
    /// and advances the clock again.
    /// </summary>
    public sealed class AdvancingJob : IJob
    {
        public static readonly TimeSpan BeforeReading = TimeSpan.FromSeconds(3);
        public static readonly TimeSpan AfterReading = TimeSpan.FromSeconds(2);

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Run(context);
            return default;
        }

        internal static void Run(IJobExecutionContext context)
        {
            FakeTimeProvider clock = (FakeTimeProvider) context.Scheduler.TimeProvider;
            clock.Advance(BeforeReading);
            context.Result = context.JobRunTime;
            clock.Advance(AfterReading);
        }
    }

    /// <summary>
    /// Spends ten seconds of the scheduler's clock and asks to be refired, then runs as
    /// <see cref="AdvancingJob" /> does.
    /// </summary>
    public sealed class RefiringJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (context.RefireCount == 0)
            {
                ((FakeTimeProvider) context.Scheduler.TimeProvider).Advance(TimeSpan.FromSeconds(10));
                throw new JobExecutionException("refire me") { RefireImmediately = true };
            }

            AdvancingJob.Run(context);
            return default;
        }
    }

    private sealed record Observed(TimeSpan BeforeTheRun, TimeSpan MidRun, TimeSpan Final, int RefireCount);

    private sealed class RunTimeListener : IJobListener, ITriggerListener
    {
        private readonly TaskCompletionSource<Observed> executed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TimeSpan beforeTheRun = TimeSpan.MinValue;

        public string Name => nameof(RunTimeListener);

        public Task<Observed> Executed => executed.Task;

        public ValueTask TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            beforeTheRun = context.JobRunTime;
            return default;
        }

        public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException, CancellationToken cancellationToken = default)
        {
            executed.TrySetResult(new Observed(beforeTheRun, (TimeSpan) context.Result, context.JobRunTime, context.RefireCount));
            return default;
        }
    }
}
