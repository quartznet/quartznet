using Microsoft.Extensions.Options;

using Quartz.Impl.Matchers;
using Quartz.Spi;

using Lifetime = Microsoft.Extensions.Hosting.IHostApplicationLifetime;

namespace Quartz.Tests.Unit;

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

        public ValueTask<IReadOnlyList<IScheduler>> GetAllSchedulers(CancellationToken cancellationToken = default)
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

        public ValueTask<IScheduler> GetScheduler(string schedName, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }

    private sealed class MockScheduler : IScheduler
    {
        public string SchedulerName { get; }
        public string SchedulerInstanceId { get; }
        public SchedulerContext Context { get; }
        public bool InStandbyMode { get; }
        public bool IsShutdown { get; private set; }
        public IJobFactory JobFactory { set => throw new NotImplementedException(); }
        public IListenerManager ListenerManager { get; }
        public bool IsStarted { get; private set; }

        public ValueTask AddCalendar(string name, ICalendar calendar, bool replace, bool updateTriggers, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask AddJob(IJobDetail jobDetail, bool replace, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask AddJob(IJobDetail jobDetail, bool replace, bool storeNonDurableWhileAwaitingScheduling, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> CheckExists(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> CheckExists(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask Clear(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> DeleteCalendar(string name, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> DeleteJob(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<ICalendar> GetCalendar(string name, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<string>> GetCalendarNames(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<IJobExecutionContext>> GetCurrentlyExecutingJobs(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<IJobDetail> GetJobDetail(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<string>> GetJobGroupNames(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<JobKey>> GetJobKeys(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<SchedulerMetaData> GetMetaData(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<string>> GetPausedTriggerGroups(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<ITrigger> GetTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<string>> GetTriggerGroupNames(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<TriggerKey>> GetTriggerKeys(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<ITrigger>> GetTriggersOfJob(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<TriggerState> GetTriggerState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> Interrupt(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> Interrupt(string fireInstanceId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> IsJobGroupPaused(string groupName, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> IsTriggerGroupPaused(string groupName, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask PauseAll(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask PauseJob(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask PauseTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<DateTimeOffset?> RescheduleJob(TriggerKey triggerKey, ITrigger newTrigger, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask ResumeAll(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask ResumeTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
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

        public ValueTask ScheduleJob(IJobDetail jobDetail, IReadOnlyCollection<ITrigger> triggersForJob, bool replace, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, bool replace, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask Shutdown(CancellationToken cancellationToken = default)
        {
            this.IsShutdown = true;
            this.IsStarted = false;
            return default;
        }

        public ValueTask Shutdown(bool waitForJobsToComplete, CancellationToken cancellationToken = default)
        {
            this.IsShutdown = true;
            this.IsStarted = false;
            return default;
        }

        public ValueTask Standby(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask Start(CancellationToken cancellationToken = default)
        {
            this.IsStarted = true;
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

        public ValueTask TriggerJob(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask TriggerJob(JobKey jobKey, JobDataMap data, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> UnscheduleJob(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> UnscheduleJobs(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
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
        var quartzHostedService = new QuartzHostedService(
            applicationLifetime,
            schedulerFactory,
            Options.Create(new QuartzHostedServiceOptions
            {
                AwaitApplicationStarted = awaitApplicationStarted,
                StartDelay = withStartDelay ? TimeSpan.FromMinutes(1) : null,
            }));

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
        var quartzHostedService = new QuartzHostedService(
            appliationLifetime,
            schedulerFactory,
            Options.Create(new QuartzHostedServiceOptions
            {
                AwaitApplicationStarted = awaitApplicationStarted,
                StartDelay = withStartDelay ? TimeSpan.FromMinutes(1) : null,
            }));

        using var startupCts = new CancellationTokenSource();

        await quartzHostedService.StartAsync(startupCts.Token);

        Assert.That(schedulerFactory.LastCreatedScheduler, Is.Not.Null);
        Assert.That(schedulerFactory.LastCreatedScheduler.IsStarted, Is.EqualTo(shouldSchedulerBeStartedImmediately));

        appliationLifetime.SetStarted();

        if (quartzHostedService.startupTask is not null)
        {
            await quartzHostedService.startupTask
                .ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default); // Wait for the hosted service to respond to the ApplicationStarted token
        }

        Assert.That(schedulerFactory.LastCreatedScheduler.IsStarted, Is.EqualTo(!withStartDelay));

        await startupCts.CancelAsync().ConfigureAwait(false);

        await quartzHostedService.StopAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(schedulerFactory.LastCreatedScheduler.IsStarted, Is.False);
            Assert.That(schedulerFactory.LastCreatedScheduler.IsShutdown, Is.True);
        });
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
        var quartzHostedService = new QuartzHostedService(
            appliationLifetime,
            schedulerFactory,
            Options.Create(new QuartzHostedServiceOptions
            {
                AwaitApplicationStarted = awaitApplicationStarted,
                StartDelay = withStartDelay ? TimeSpan.FromMinutes(1) : null,
            }));

        using var startupCts = new CancellationTokenSource();

        var startupTask = quartzHostedService.StartAsync(startupCts.Token);

        await startupCts.CancelAsync().ConfigureAwait(false);

        await startupTask;

        Assert.That(schedulerFactory.LastCreatedScheduler.IsStarted, Is.EqualTo(shouldSchedulerBeStarted));
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
        var quartzHostedService = new QuartzHostedService(
            appliationLifetime,
            schedulerFactory,
            Options.Create(new QuartzHostedServiceOptions
            {
                AwaitApplicationStarted = awaitApplicationStarted,
                StartDelay = withStartDelay ? TimeSpan.FromMinutes(1) : null,
            }));

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
        if (shouldSchedulerBeStarted)
        {
            Assert.That(schedulerFactory.LastCreatedScheduler.IsShutdown, Is.True);
        }

        Assert.That(schedulerFactory.LastCreatedScheduler.IsStarted, Is.False);

        await startupCts.CancelAsync().ConfigureAwait(false);
    }
}