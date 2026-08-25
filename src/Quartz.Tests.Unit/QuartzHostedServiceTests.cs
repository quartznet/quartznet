using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Extensibility;

using Lifetime = Microsoft.Extensions.Hosting.IHostApplicationLifetime;

namespace Quartz.Tests.Unit;

[NonParallelizable]
public class QuartzHostedServiceTests
{
    private sealed class MockApplicationLifetime : Lifetime
    {
        public CancellationTokenSource StartedSource { get; } = new();
        public CancellationTokenSource StoppingSource { get; } = new();
        public CancellationToken ApplicationStarted => StartedSource.Token;
        public CancellationToken ApplicationStopping => StoppingSource.Token;
        public CancellationToken ApplicationStopped => throw new NotImplementedException();

        public void SetStarted()
        {
            StartedSource.Cancel();
        }

        public void StopApplication()
        {
            StoppingSource.Cancel();
        }
    }

    private sealed class MockSchedulerFactory : ISchedulerFactory
    {
        public MockScheduler LastCreatedScheduler { get; private set; }

        public ValueTask<List<IScheduler>> GetAllSchedulers(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async ValueTask<IScheduler> GetScheduler(CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            var scheduler = new MockScheduler();
            this.LastCreatedScheduler = scheduler;
            return scheduler;
        }

        public ValueTask<IScheduler> LookupScheduler(string schedulerName, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }

    private sealed class MockScheduler : IScheduler
    {
        public string SchedulerName { get; }
        public string SchedulerInstanceId { get; }
        public SchedulerContext Context { get; }
        public SchedulerStatus Status { get; private set; } = SchedulerStatus.Created;
        public IJobFactory JobFactory { set => throw new NotImplementedException(); }
        public IListenerManager ListenerManager { get; }

        public ValueTask DisposeAsync() => default;

        public ValueTask AddCalendar(string calendarName, ICalendar calendar, AddCalendarOptions options = default, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask AddJob(IJobDetail jobDetail, AddJobOptions options = default, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> Exists(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> Exists(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask Clear(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> DeleteCalendar(string calendarName, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> DeleteJob(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<JobKey>> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<ICalendar> GetCalendar(string calendarName, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<PagedResult<FireInstance>> QueryFireInstances(FireInstanceQuery query, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<IJobDetail> GetJobDetail(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<PagedResult<JobHeader>> QueryJobs(JobQuery query, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<PagedResult<TriggerHeader>> QueryTriggers(TriggerQuery query, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<PagedResult<JobGroup>> QueryJobGroups(JobGroupQuery query, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<PagedResult<TriggerGroup>> QueryTriggerGroups(TriggerGroupQuery query, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<PagedResult<string>> QueryCalendarNames(CalendarQuery query, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<IJobDetail>> GetJobDetails(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<ITrigger>> GetTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<SchedulerMetadata> GetMetadata(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<ITrigger> GetTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<TriggerState> GetTriggerState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<TriggerKey>> ResetTriggersFromErrorState(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> Interrupt(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> InterruptFireInstance(string fireInstanceId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask PauseAll(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> PauseJob(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<string>> PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<JobKey>> PauseJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<string>> PauseTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<TriggerKey>> PauseTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<DateTimeOffset?> RescheduleJob(TriggerKey triggerKey, ITrigger newTrigger, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> UpdateTriggerDetails(TriggerKey triggerKey, TriggerDetailsUpdate update, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask SetExecutionLimits(ExecutionLimits limits, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<ExecutionLimits> GetExecutionLimits(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask ResumeAll(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<string>> ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<JobKey>> ResumeJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<string>> ResumeTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<TriggerKey>> ResumeTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<DateTimeOffset> ScheduleJob(IJobDetail jobDetail, ITrigger trigger, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<DateTimeOffset> ScheduleJob(ITrigger trigger, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask ScheduleJob(IJobDetail jobDetail, IReadOnlyCollection<ITrigger> triggersForJob, ScheduleJobOptions options = default, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, ScheduleJobOptions options = default, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask Shutdown(bool waitForJobsToComplete = false, CancellationToken cancellationToken = default)
        {
            this.Status = SchedulerStatus.Shutdown;
            return default;
        }

        public ValueTask Standby(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask Start(CancellationToken cancellationToken = default)
        {
            this.Status = SchedulerStatus.Running;
            return default;
        }

        public ValueTask StartDelayed(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(delay, cancellationToken)
                    .ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.OnlyOnCanceled, TaskScheduler.Default);

                if (!cancellationToken.IsCancellationRequested)
                {
                    await this.Start(cancellationToken);
                }
            }, CancellationToken.None);
            return default;
        }

        public ValueTask TriggerJob(JobKey jobKey, JobDataMap data = null, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> UnscheduleJob(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<TriggerKey>> UnscheduleJobs(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }

    [Test]
    [TestCase(false, false, true)]
    [TestCase(true, false, true)]
    [TestCase(false, true, false)]
    [TestCase(true, true, false)]
    [Parallelizable(ParallelScope.All)]
    public async Task StartAsync_WithStartedApplication_ShouldGetScheduler(bool awaitApplicationStarted, bool withStartDelay, bool shouldSchedulerBeStarted)
    {
        var applicationLifetime = new MockApplicationLifetime();
        var schedulerFactory = new MockSchedulerFactory();
        var quartzHostedService = CreateHostedService(
            applicationLifetime,
            schedulerFactory,
            awaitApplicationStarted,
            withStartDelay);

        Assert.That(schedulerFactory.LastCreatedScheduler, Is.Null);

        using var startupCts = new CancellationTokenSource();

        await quartzHostedService.StartAsync(startupCts.Token);

        Assert.That(schedulerFactory.LastCreatedScheduler, Is.Not.Null);

        await startupCts.CancelAsync().ConfigureAwait(false);
    }

    [Test]
    [TestCase(false, false, true)]
    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    [TestCase(true, true, false)]
    [Parallelizable(ParallelScope.All)]
    public async Task StartAsync_WithStartedApplication_ShouldStartSchedulerDependingOnPotentialDelay(bool awaitApplicationStarted, bool withStartDelay, bool shouldSchedulerBeStartedImmediately)
    {
        var appliationLifetime = new MockApplicationLifetime();
        var schedulerFactory = new MockSchedulerFactory();
        var quartzHostedService = CreateHostedService(
            appliationLifetime,
            schedulerFactory,
            awaitApplicationStarted,
            withStartDelay);

        using var startupCts = new CancellationTokenSource();

        await quartzHostedService.StartAsync(startupCts.Token);

        Assert.That(schedulerFactory.LastCreatedScheduler, Is.Not.Null);
        schedulerFactory.LastCreatedScheduler.Status.Should().Be(
            shouldSchedulerBeStartedImmediately ? SchedulerStatus.Running : SchedulerStatus.Created);

        appliationLifetime.SetStarted();

        if (quartzHostedService.startupTask is not null)
        {
            await quartzHostedService.startupTask
                .ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default); // Wait for the hosted service to respond to the ApplicationStarted token
        }

        schedulerFactory.LastCreatedScheduler.Status.Should().Be(
            withStartDelay ? SchedulerStatus.Created : SchedulerStatus.Running);

        await startupCts.CancelAsync().ConfigureAwait(false);

        await quartzHostedService.StopAsync(CancellationToken.None);

        schedulerFactory.LastCreatedScheduler.Status.Should().Be(SchedulerStatus.Shutdown);
    }

    [Test]
    [TestCase(false, false, true)]
    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    [TestCase(true, true, false)]
    [Parallelizable(ParallelScope.All)]
    public async Task StartAsync_WithCancelledApplicationStartup_ShouldNotStartSchedulerUnlessNonwaiting(bool awaitApplicationStarted, bool withStartDelay, bool shouldSchedulerBeStarted)
    {
        var appliationLifetime = new MockApplicationLifetime();
        var schedulerFactory = new MockSchedulerFactory();
        var quartzHostedService = CreateHostedService(
            appliationLifetime,
            schedulerFactory,
            awaitApplicationStarted,
            withStartDelay);

        using var startupCts = new CancellationTokenSource();

        var startupTask = quartzHostedService.StartAsync(startupCts.Token);

        await startupCts.CancelAsync().ConfigureAwait(false);

        await startupTask;

        schedulerFactory.LastCreatedScheduler.Status.Should().Be(
            shouldSchedulerBeStarted ? SchedulerStatus.Running : SchedulerStatus.Created);
    }

    [Test]
    [TestCase(false, false, true)]
    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    [TestCase(true, true, false)]
    [Parallelizable(ParallelScope.All)]
    public async Task StopAsync_WithStoppedApplication_ShouldShutDownSchedulerAndNotStartItDelayedAfterwards(bool awaitApplicationStarted, bool withStartDelay, bool shouldSchedulerBeStarted)
    {
        var appliationLifetime = new MockApplicationLifetime();
        var schedulerFactory = new MockSchedulerFactory();
        var quartzHostedService = CreateHostedService(
            appliationLifetime,
            schedulerFactory,
            awaitApplicationStarted,
            withStartDelay);

        using var startupCts = new CancellationTokenSource();

        await quartzHostedService.StartAsync(startupCts.Token);
        appliationLifetime.SetStarted();

        appliationLifetime.StopApplication();
        await quartzHostedService.StopAsync(CancellationToken.None);

        if (quartzHostedService.startupTask is not null)
        {
            await quartzHostedService.startupTask
                .ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default); // Wait for the hosted service to respond to the ApplicationStarted token
        }

        // Confirm that not only have we stopped, but that we have not started AFTER being stopped
        schedulerFactory.LastCreatedScheduler.Status.Should().Be(
            SchedulerStatus.Shutdown,
            "a delayed start that arrives after the host has stopped must not put the scheduler back to Running");

        await startupCts.CancelAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task StopAsync_ShutsEverySchedulerDownAtOnceRatherThanInTurn()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var registry = new SchedulerNameRegistry();
        registry.Add("second");

        var services = new ServiceCollection();
        services.AddSingleton(registry);
        services.AddSingleton(FactoryFor(SchedulerBlockedInShutdown(firstEntered, release.Task)));
        services.AddKeyedSingleton(typeof(ISchedulerFactory), "second", (_, _) => FactoryFor(SchedulerBlockedInShutdown(secondEntered, release.Task)));

        await using var provider = services.BuildServiceProvider();

        var hostedService = new QuartzHostedService(
            new MockApplicationLifetime(),
            provider,
            new StaticOptionsMonitor(new QuartzHostedServiceOptions
            {
                WaitForJobsToComplete = true,
                AwaitApplicationStarted = false,
            }));

        await hostedService.StartAsync(CancellationToken.None);

        var stop = hostedService.StopAsync(CancellationToken.None);

        Func<Task> act = async () => await Task.WhenAll(firstEntered.Task, secondEntered.Task).WaitAsync(TimeSpan.FromSeconds(10));

        await act.Should().NotThrowAsync(
            "both schedulers have to be shutting down at the same time: waiting for one drain before starting the next makes the "
            + "host's stop time the sum of the waits, which is what overruns HostOptions.ShutdownTimeout");

        release.TrySetResult();

        await stop.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task StopAsync_ShutsTheOtherSchedulersDownWhenOneOfThemFails()
    {
        var failing = A.Fake<IScheduler>();
        A.CallTo(() => failing.Shutdown(A<bool>._, A<CancellationToken>._)).Throws(new InvalidOperationException("no"));

        var healthy = A.Fake<IScheduler>();

        var registry = new SchedulerNameRegistry();
        registry.Add("healthy");

        var services = new ServiceCollection();
        services.AddSingleton(registry);
        services.AddSingleton(FactoryFor(failing));
        services.AddKeyedSingleton(typeof(ISchedulerFactory), "healthy", (_, _) => FactoryFor(healthy));

        await using var provider = services.BuildServiceProvider();

        var hostedService = new QuartzHostedService(
            new MockApplicationLifetime(),
            provider,
            new StaticOptionsMonitor(new QuartzHostedServiceOptions { AwaitApplicationStarted = false }));

        await hostedService.StartAsync(CancellationToken.None);

        Func<Task> act = async () => await hostedService.StopAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<AggregateException>(
                "a shutdown that failed is reported rather than swallowed, and all of them are reported rather than the first"))
            .WithInnerException<InvalidOperationException>();

        A.CallTo(() => healthy.Shutdown(A<bool>._, A<CancellationToken>._)).MustHaveHappened();
    }

    /// <summary>
    /// A scheduler whose shutdown does not return until the test lets it, so that two of them overlapping
    /// is observable.
    /// </summary>
    private static IScheduler SchedulerBlockedInShutdown(TaskCompletionSource entered, Task release)
    {
        var scheduler = A.Fake<IScheduler>();

        A.CallTo(() => scheduler.Shutdown(A<bool>._, A<CancellationToken>._)).ReturnsLazily(() =>
        {
            entered.TrySetResult();
            return new ValueTask(release);
        });

        return scheduler;
    }

    private static ISchedulerFactory FactoryFor(IScheduler scheduler)
    {
        var factory = A.Fake<ISchedulerFactory>();
        A.CallTo(() => factory.GetScheduler(A<CancellationToken>._)).ReturnsLazily(() => new ValueTask<IScheduler>(scheduler));
        return factory;
    }

    /// <summary>
    /// Builds the hosted service over a container holding just the one scheduler factory, which is what
    /// it resolves its schedulers from.
    /// </summary>
    private static QuartzHostedService CreateHostedService(
        Lifetime applicationLifetime,
        ISchedulerFactory schedulerFactory,
        bool awaitApplicationStarted,
        bool withStartDelay)
    {
        var options = new QuartzHostedServiceOptions
        {
            AwaitApplicationStarted = awaitApplicationStarted,
            StartDelay = withStartDelay ? TimeSpan.FromMinutes(1) : null,
        };

        return new QuartzHostedService(
            applicationLifetime,
            new SchedulerFactoryOnlyServiceProvider(schedulerFactory),
            new StaticOptionsMonitor(options));
    }

    private sealed class SchedulerFactoryOnlyServiceProvider : IServiceProvider
    {
        private readonly ISchedulerFactory schedulerFactory;

        public SchedulerFactoryOnlyServiceProvider(ISchedulerFactory schedulerFactory)
        {
            this.schedulerFactory = schedulerFactory;
        }

        public object GetService(Type serviceType) => serviceType == typeof(ISchedulerFactory) ? schedulerFactory : null;
    }

    private sealed class StaticOptionsMonitor : IOptionsMonitor<QuartzHostedServiceOptions>
    {
        private readonly QuartzHostedServiceOptions options;

        public StaticOptionsMonitor(QuartzHostedServiceOptions options)
        {
            this.options = options;
        }

        public QuartzHostedServiceOptions CurrentValue => options;

        public QuartzHostedServiceOptions Get(string name) => options;

        public IDisposable OnChange(Action<QuartzHostedServiceOptions, string> listener) => null;
    }
}
