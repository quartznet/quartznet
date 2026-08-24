using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Listeners;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// A listener is told which scheduler is calling it — through its execution context where there is one,
/// and as the first argument of every callback where there is not (#3063).
/// </summary>
[NonParallelizable]
public sealed class ListenerSenderTest
{
    [Test]
    public async Task OneListenerServingTwoSchedulers_IsToldWhichOneCalled()
    {
        // An ISchedulerListener registered as a plain unkeyed service reaches every scheduler in the
        // container, which is exactly the case that used to be unanswerable: the callbacks said what
        // happened but never where.
        RecordingSchedulerListener listener = new();

        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<ISchedulerListener>(listener);

        services.AddQuartz("acme", q => Schedule(q, "acme"));
        services.AddQuartz("initech", q => Schedule(q, "initech"));

        await using ServiceProvider provider = services.BuildServiceProvider();

        IScheduler acme = await provider.GetRequiredKeyedService<ISchedulerFactory>("acme").GetScheduler();
        IScheduler initech = await provider.GetRequiredKeyedService<ISchedulerFactory>("initech").GetScheduler();

        await acme.PauseTrigger(new TriggerKey("trigger", "acme"));
        await initech.PauseTrigger(new TriggerKey("trigger", "initech"));

        listener.Paused.Should().Equal(
            [("acme", new TriggerKey("trigger", "acme")), ("initech", new TriggerKey("trigger", "initech"))],
            "one listener shared by two schedulers has to be able to tell which of them paused a trigger");

        listener.PausedBy.Should().Equal([acme, initech],
            "the scheduler handed to a callback is the very instance the application holds, not a stand-in");
    }

    [Test]
    public async Task JobThatThrows_ReportsTheTriggerJobAndFiringItThrewFor()
    {
        ErrorRecordingListener listener = new();

        IScheduler scheduler = await QuartzSchedulerBuilder.Create().BuildScheduler();

        try
        {
            scheduler.ListenerManager.AddSchedulerListener(listener);

            IJobDetail job = JobBuilder.Create<ThrowingJob>()
                .WithIdentity("job", "sender")
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("trigger", "sender")
                .ForJob(job)
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(job, trigger);
            await scheduler.Start();

            (IScheduler reportedBy, SchedulerErrorContext error) = await listener.Reported.WaitAsync(TimeSpan.FromSeconds(30));

            reportedBy.Should().BeSameAs(scheduler);
            error.Message.Should().Contain("threw an exception");
            error.TriggerKey.Should().Be(new TriggerKey("trigger", "sender"));
            error.JobKey.Should().Be(new JobKey("job", "sender"));
            error.FireInstanceId.Should().NotBeNullOrEmpty(
                "the firing is what distinguishes this failure from the next one of the same job");
        }
        finally
        {
            await scheduler.Shutdown(true);
        }
    }

    [Test]
    public async Task Misfire_IsReportedByTheSchedulerThatNoticedIt()
    {
        // A misfire is noticed by the job store rather than executed, so this is the one trigger-listener
        // callback with no IJobExecutionContext to reach the scheduler through.
        MisfireRecordingListener listener = new();

        IScheduler scheduler = await QuartzSchedulerBuilder.Create()
            .ConfigureScheduler(options => options.InstanceName = "misfires")
            .UseInMemoryStore(store => store.MisfireThreshold = TimeSpan.FromMilliseconds(1))
            .BuildScheduler();

        try
        {
            scheduler.ListenerManager.AddTriggerListener(listener, Quartz.Matchers.AllTriggers());

            IJobDetail job = JobBuilder.Create<HarmlessJob>()
                .WithIdentity("job", "misfire")
                .Build();

            // Scheduled while the scheduler is in standby and dated well into the past, so the trigger is
            // already past its misfire threshold by the time acquisition first looks at it.
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("trigger", "misfire")
                .ForJob(job)
                .StartAt(DateTimeOffset.UtcNow.AddMinutes(-5))
                .Build();

            await scheduler.ScheduleJob(job, trigger);
            await scheduler.Start();

            (IScheduler reportedBy, TriggerKey misfired) = await listener.Reported.WaitAsync(TimeSpan.FromSeconds(30));

            reportedBy.Should().BeSameAs(scheduler);
            reportedBy.SchedulerName.Should().Be("misfires");
            misfired.Should().Be(new TriggerKey("trigger", "misfire"));
        }
        finally
        {
            await scheduler.Shutdown(true);
        }
    }

    [Test]
    public async Task BroadcastSchedulerListener_ForwardsTheSchedulerItWasGiven()
    {
        RecordingSchedulerListener first = new();
        RecordingSchedulerListener second = new();

        BroadcastSchedulerListener broadcast = new("broadcast", [first, second]);

        IScheduler scheduler = await QuartzSchedulerBuilder.Create().BuildScheduler();

        try
        {
            await broadcast.TriggerPaused(scheduler, new TriggerKey("trigger", "broadcast"));

            first.PausedBy.Should().Equal([scheduler],
                "a broadcast has nothing of its own to say about who is calling, so it passes on what it was told");
            second.PausedBy.Should().Equal([scheduler]);
        }
        finally
        {
            await scheduler.Shutdown(true);
        }
    }

    private static void Schedule(IQuartzBuilder builder, string group)
    {
        builder.AddJob<HarmlessJob>(j => j.WithIdentity("job", group));
        builder.AddTrigger<IJob>(t => t
            .ForJob("job", group)
            .WithIdentity("trigger", group)
            .StartAt(DateTimeOffset.UtcNow.AddHours(1)));
    }

    private sealed class RecordingSchedulerListener : ISchedulerListener
    {
        public List<(string SchedulerName, TriggerKey TriggerKey)> Paused { get; } = [];

        public List<IScheduler> PausedBy { get; } = [];

        public ValueTask TriggerPaused(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            Paused.Add((scheduler.SchedulerName, triggerKey));
            PausedBy.Add(scheduler);
            return default;
        }
    }

    private sealed class ErrorRecordingListener : ISchedulerListener
    {
        private readonly TaskCompletionSource<(IScheduler, SchedulerErrorContext)> reported = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<(IScheduler Scheduler, SchedulerErrorContext Error)> Reported => reported.Task;

        public ValueTask SchedulerError(IScheduler scheduler, SchedulerErrorContext errorContext, CancellationToken cancellationToken = default)
        {
            reported.TrySetResult((scheduler, errorContext));
            return default;
        }
    }

    private sealed class MisfireRecordingListener : ITriggerListener
    {
        private readonly TaskCompletionSource<(IScheduler, TriggerKey)> reported = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<(IScheduler Scheduler, TriggerKey TriggerKey)> Reported => reported.Task;

        public ValueTask TriggerMisfired(IScheduler scheduler, ITrigger trigger, CancellationToken cancellationToken = default)
        {
            reported.TrySetResult((scheduler, trigger.Key));
            return default;
        }
    }

    public sealed class HarmlessJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed class ThrowingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("the report has to name this firing");
        }
    }
}
