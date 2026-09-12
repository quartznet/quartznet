using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// What <c>AddQuartzSchedulerEvents()</c> installs, and what a second call to it does not.
/// </summary>
/// <remarks>
/// Three things call it — an application, <c>AddQuartzHttpApi()</c> and <c>AddQuartzDashboard()</c> — so a
/// process that maps both surfaces would otherwise put every event on the stream two or three times, and a
/// live view that shows one firing as three is worse than no live view.
/// </remarks>
[NonParallelizable]
public sealed class SchedulerEventsRegistrationTest
{
    [SetUp]
    public void SetUp() => SignallingJob.Reset();

    [Test]
    public async Task TheProcessesBrokerIsItsEventSource()
    {
        await using ServiceProvider provider = Container(services =>
        {
            services.AddQuartz(quartz => quartz.UseInMemoryStore());
            services.AddQuartzSchedulerEvents();
        });

        provider.GetRequiredService<ISchedulerEventSource>().Should().BeSameAs(
            provider.GetRequiredService<SchedulerEventBroker>(),
            "a reader asking for the source of a scheduler in this process is asking for the broker it publishes into");
    }

    [Test]
    public async Task WhatASchedulerDoesReachesAReaderOnce()
    {
        await using ServiceProvider provider = Container(services =>
        {
            services.AddQuartz(quartz => quartz.UseInMemoryStore());
            services.AddQuartzSchedulerEvents();
        });

        List<SchedulerEvent> published = await RunOneJob(provider);

        published.Should().ContainSingle(@event => @event.Kind == SchedulerEventKind.JobExecuting,
            "one publisher publishes one event for one firing");
        published.Should().ContainSingle(@event => @event.Kind == SchedulerEventKind.JobExecuted);
        published.Where(@event => @event.Kind == SchedulerEventKind.JobExecuting)
            .Should().OnlyContain(@event => @event.JobKey == new KeyDto("streamed", "DummyGroup"));
    }

    [Test]
    public async Task ASecondCallInstallsNoSecondPublisher()
    {
        await using ServiceProvider provider = Container(services =>
        {
            services.AddQuartz(quartz => quartz.UseInMemoryStore());
            services.AddQuartzSchedulerEvents();
            services.AddQuartzSchedulerEvents();
        });

        List<SchedulerEvent> published = await RunOneJob(provider);

        published.Should().ContainSingle(@event => @event.Kind == SchedulerEventKind.JobExecuting,
            "the publisher is installed once however many callers ask for it - two of them would double every event");
    }

    /// <summary>
    /// The order against <c>AddQuartz</c> does not matter, which is what
    /// <c>ConfigureAllQuartzSchedulers</c> promises and what a package calling this cannot control.
    /// </summary>
    [Test]
    public async Task TheStreamReachesASchedulerRegisteredAfterwards()
    {
        await using ServiceProvider provider = Container(services =>
        {
            services.AddQuartzSchedulerEvents();
            services.AddQuartz(quartz => quartz.UseInMemoryStore());
        });

        List<SchedulerEvent> published = await RunOneJob(provider);

        published.Should().NotBeEmpty("a package that streams events cannot know which line the application writes first");
    }

    /// <summary>
    /// A named scheduler is covered too, and its events are keyed by its own name.
    /// </summary>
    [Test]
    public async Task ANamedSchedulersEventsCarryItsOwnName()
    {
        await using ServiceProvider provider = Container(services =>
        {
            services.AddQuartz("acme", quartz => quartz.UseInMemoryStore());
            services.AddQuartzSchedulerEvents();
        });

        IScheduler scheduler = await provider.GetRequiredKeyedService<ISchedulerFactory>("acme").GetScheduler();
        List<SchedulerEvent> published = await RunOneJob(provider, scheduler);

        published.Should().NotBeEmpty().And.OnlyContain(@event => @event.SchedulerName == "acme",
            "a plugin is told its own scheduler's name, which is what the stream is keyed by");
    }

    private static ServiceProvider Container(Action<IServiceCollection> configure)
    {
        ServiceCollection services = new();
        services.AddLogging();
        configure(services);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Subscribes, runs one job, and answers with everything that reached the subscriber.
    /// </summary>
    /// <remarks>
    /// Subscribed before the scheduler is started, because a subscription carries what happens after it is
    /// made — and because the publisher builds nothing while nothing is watching.
    /// </remarks>
    private static async Task<List<SchedulerEvent>> RunOneJob(ServiceProvider provider, IScheduler scheduler = null)
    {
        scheduler ??= await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        SchedulerEventBroker broker = provider.GetRequiredService<SchedulerEventBroker>();
        List<SchedulerEvent> published = [];

        using CancellationTokenSource subscription = new();
        Task reader = Task.Run(async () =>
        {
            await foreach (SchedulerEvent @event in broker.Subscribe(scheduler.SchedulerName, subscription.Token))
            {
                lock (published)
                {
                    published.Add(@event);
                }
            }
        });

        using CancellationTokenSource waitingForTheQueue = new(TimeSpan.FromSeconds(30));
        while (!broker.HasSubscribers(scheduler.SchedulerName))
        {
            await Task.Delay(5, waitingForTheQueue.Token);
        }

        await scheduler.Start();
        await scheduler.ScheduleJob(
            JobBuilder.Create<SignallingJob>().WithIdentity("streamed", "DummyGroup").Build(),
            TriggerBuilder.Create().WithIdentity("now", "DummyGroup").StartNow().Build());

        (await SignallingJob.Executed.Task.WaitAsync(TimeSpan.FromSeconds(30))).Should().BeTrue();

        // The completion is published by the listener the plugin registered, which runs after the job
        // returns.
        await scheduler.Shutdown(waitForJobsToComplete: true);

        await subscription.CancelAsync();
        await reader;

        lock (published)
        {
            return [.. published];
        }
    }

    private sealed class SignallingJob : IJob
    {
        public static TaskCompletionSource<bool> Executed { get; private set; } = new();

        public static void Reset() => Executed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Executed.TrySetResult(true);
            return default;
        }
    }
}
