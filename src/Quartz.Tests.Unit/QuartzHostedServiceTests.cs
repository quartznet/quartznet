using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Quartz.Impl.Matchers;
using Quartz.Spi;

#if NETCOREAPP3_1_OR_GREATER
using Lifetime = Microsoft.Extensions.Hosting.IHostApplicationLifetime;
#else
using Lifetime = Microsoft.Extensions.Hosting.IApplicationLifetime;
#endif

namespace Quartz.Tests.Unit
{
    [TestFixture]
    public class QuartzHostedServiceTests
    {
        private sealed class MockApplicationLifetime : Lifetime
        {
            public CancellationTokenSource StartedSource { get; } = new CancellationTokenSource();
            public CancellationTokenSource StoppingSource { get; } = new CancellationTokenSource();
            public CancellationToken ApplicationStarted => this.StartedSource.Token;
            public CancellationToken ApplicationStopping => this.StoppingSource.Token;
            public CancellationToken ApplicationStopped => throw new NotImplementedException();

            public void SetStarted()
            {
                this.StartedSource.Cancel();
            }

            public void StopApplication()
            {
                this.StoppingSource.Cancel();
            }
        }

        private sealed class MockSchedulerFactory : ISchedulerFactory
        {
            public MockScheduler LastCreatedScheduler { get; private set; }

            public Task<IReadOnlyList<IScheduler>> GetAllSchedulers(CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public async Task<IScheduler> GetScheduler(CancellationToken cancellationToken = default)
            {
                await Task.Yield();

                var scheduler = new MockScheduler();
                this.LastCreatedScheduler = scheduler;
                return scheduler;
            }

            public Task<IScheduler> GetScheduler(string schedName, CancellationToken cancellationToken = default)
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

            public Task AddCalendar(string calName, ICalendar calendar, bool replace, bool updateTriggers, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task AddJob(IJobDetail jobDetail, bool replace, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task AddJob(IJobDetail jobDetail, bool replace, bool storeNonDurableWhileAwaitingScheduling, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<bool> CheckExists(JobKey jobKey, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<bool> CheckExists(TriggerKey triggerKey, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task Clear(CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<bool> DeleteCalendar(string calName, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<bool> DeleteJob(JobKey jobKey, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<bool> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<ICalendar> GetCalendar(string calName, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<IReadOnlyCollection<string>> GetCalendarNames(CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<IReadOnlyCollection<IJobExecutionContext>> GetCurrentlyExecutingJobs(CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<IJobDetail> GetJobDetail(JobKey jobKey, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<IReadOnlyCollection<string>> GetJobGroupNames(CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<IReadOnlyCollection<JobKey>> GetJobKeys(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<SchedulerMetaData> GetMetaData(CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<IReadOnlyCollection<string>> GetPausedTriggerGroups(CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<ITrigger> GetTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<IReadOnlyCollection<string>> GetTriggerGroupNames(CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<IReadOnlyCollection<TriggerKey>> GetTriggerKeys(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<IReadOnlyCollection<ITrigger>> GetTriggersOfJob(JobKey jobKey, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<TriggerState> GetTriggerState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<bool> Interrupt(JobKey jobKey, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<bool> Interrupt(string fireInstanceId, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<bool> IsJobGroupPaused(string groupName, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<bool> IsTriggerGroupPaused(string groupName, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task PauseAll(CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task PauseJob(JobKey jobKey, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task PauseTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<DateTimeOffset?> RescheduleJob(TriggerKey triggerKey, ITrigger newTrigger, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task ResumeAll(CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task ResumeTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<DateTimeOffset> ScheduleJob(IJobDetail jobDetail, ITrigger trigger, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<DateTimeOffset> ScheduleJob(ITrigger trigger, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task ScheduleJob(IJobDetail jobDetail, IReadOnlyCollection<ITrigger> triggersForJob, bool replace, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, bool replace, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task Shutdown(CancellationToken cancellationToken = default)
            {
                this.IsShutdown = true;
                this.IsStarted = false;
                return Task.CompletedTask;
            }

            public Task Shutdown(bool waitForJobsToComplete, CancellationToken cancellationToken = default)
            {
                this.IsShutdown = true;
                this.IsStarted = false;
                return Task.CompletedTask;
            }

            public Task Standby(CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task Start(CancellationToken cancellationToken = default)
            {
                this.IsStarted = true;
                return Task.CompletedTask;
            }

            public Task StartDelayed(TimeSpan delay, CancellationToken cancellationToken = default)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(delay, cancellationToken).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnCanceled);

                    if (!cancellationToken.IsCancellationRequested)
                        await this.Start(cancellationToken);
                }, CancellationToken.None);
                return Task.CompletedTask;
            }

            public Task TriggerJob(JobKey jobKey, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task TriggerJob(JobKey jobKey, JobDataMap data, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<bool> UnscheduleJob(TriggerKey triggerKey, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<bool> UnscheduleJobs(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
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
            var appliationLifetime = new MockApplicationLifetime();
            var schedulerFactory = new MockSchedulerFactory();
            var quartzHostedService = new QuartzHostedService(
                appliationLifetime,
                schedulerFactory,
                Options.Create(new QuartzHostedServiceOptions()
                {
                    AwaitApplicationStarted = awaitApplicationStarted,
                    StartDelay = withStartDelay ? TimeSpan.FromMinutes(1) : null,
                }));

            Assert.Null(schedulerFactory.LastCreatedScheduler);

            using var startupCts = new CancellationTokenSource();

            await quartzHostedService.StartAsync(startupCts.Token);

            Assert.NotNull(schedulerFactory.LastCreatedScheduler);

            startupCts.Cancel();
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
                Options.Create(new QuartzHostedServiceOptions()
                {
                    AwaitApplicationStarted = awaitApplicationStarted,
                    StartDelay = withStartDelay ? TimeSpan.FromMinutes(1) : null,
                }));

            using var startupCts = new CancellationTokenSource();

            await quartzHostedService.StartAsync(startupCts.Token);

            Assert.NotNull(schedulerFactory.LastCreatedScheduler);
            Assert.AreEqual(shouldSchedulerBeStartedImmediately, schedulerFactory.LastCreatedScheduler.IsStarted);

            appliationLifetime.SetStarted();

            if (quartzHostedService.startupTask is not null)
                await quartzHostedService.startupTask.ContinueWith(_ => { }); // Wait for the hosted service to respond to the ApplicationStarted token

            Assert.AreEqual(!withStartDelay, schedulerFactory.LastCreatedScheduler.IsStarted);

            startupCts.Cancel();

            await quartzHostedService.StopAsync(CancellationToken.None);

            Assert.False(schedulerFactory.LastCreatedScheduler.IsStarted);
            Assert.True(schedulerFactory.LastCreatedScheduler.IsShutdown);
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
                Options.Create(new QuartzHostedServiceOptions()
                {
                    AwaitApplicationStarted = awaitApplicationStarted,
                    StartDelay = withStartDelay ? TimeSpan.FromMinutes(1) : null,
                }));

            using var startupCts = new CancellationTokenSource();

            var startupTask = quartzHostedService.StartAsync(startupCts.Token);

            startupCts.Cancel();

            await startupTask;

            Assert.AreEqual(shouldSchedulerBeStarted, schedulerFactory.LastCreatedScheduler.IsStarted);
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
                Options.Create(new QuartzHostedServiceOptions()
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
                await quartzHostedService.startupTask.ContinueWith(_ => { }); // Wait for the hosted service to respond to the ApplicationStarted token

            // Confirm that not only have we stopped, but that we have not started AFTER being stopped
            if (shouldSchedulerBeStarted) Assert.True(schedulerFactory.LastCreatedScheduler.IsShutdown);
            Assert.False(schedulerFactory.LastCreatedScheduler.IsStarted);

            startupCts.Cancel();
        }
    }
}
