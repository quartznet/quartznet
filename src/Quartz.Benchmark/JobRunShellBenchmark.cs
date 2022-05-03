using BenchmarkDotNet.Attributes;
using Quartz.Core;
using Quartz.Impl;
using Quartz.Impl.Matchers;
using Quartz.Simpl;
using Quartz.Spi;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Quartz.Benchmark
{
    [MemoryDiagnoser]
    public class JobRunShellBenchmark
    {
        private QuartzScheduler _basicQuartzScheduler;
        private StdScheduler _basicScheduler;
        private TriggerFiredBundle _bundleMayFireAgain;
        private JobRunShell _jobRunShell;

        public JobRunShellBenchmark()
        {
            _basicQuartzScheduler = CreateQuartzScheduler("basic", "basic", 5);
            _basicScheduler = new StdScheduler(_basicQuartzScheduler);

            _bundleMayFireAgain = CreateTriggerFiredBundle();
            _bundleMayFireAgain.Trigger.ComputeFirstFireTimeUtc(null);

            _jobRunShell = new JobRunShell(_basicScheduler, _bundleMayFireAgain);
            _jobRunShell.Initialize(_basicQuartzScheduler).GetAwaiter().GetResult();
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            _basicQuartzScheduler.Shutdown(true).GetAwaiter().GetResult();
        }

        [Benchmark]
        public Task Success_NoTriggerListenersAndSingleJobListener_MayFireAgain()
        {
            return _jobRunShell.Run();
        }

        private static QuartzScheduler CreateQuartzScheduler(string name, string instanceId, int threadCount)
        {
            var threadPool = new DefaultThreadPool { MaxConcurrency = threadCount };
            threadPool.Initialize();

            QuartzSchedulerResources res = new QuartzSchedulerResources
            {
                Name = name,
                InstanceId = instanceId,
                ThreadPool = threadPool,
                JobStore = new NoOpJobStore(),
                IdleWaitTime = TimeSpan.FromSeconds(30),
                MaxBatchSize = threadCount,
                BatchTimeWindow = TimeSpan.Zero
            };

            return new QuartzScheduler(res);
        }

        private TriggerFiredBundle CreateTriggerFiredBundle()
        {
            var job = new Job();
            var jobDetail = CreateJobDetail("A", job.GetType());
            var trigger = (IOperableTrigger)CreateTrigger(TimeSpan.FromMilliseconds(0.01d));
            trigger.FireInstanceId = Guid.NewGuid().ToString();

            return new TriggerFiredBundle(jobDetail, trigger, null, false, DateTimeOffset.Now, null, null, null);
        }

        private static ITrigger CreateTrigger(TimeSpan repeatInterval)
        {
            return TriggerBuilder.Create()
                                 .WithSimpleSchedule(
                                     sb => sb.RepeatForever()
                                             .WithInterval(repeatInterval)
                                             .WithMisfireHandlingInstructionFireNow())
                                 .Build();
        }

        private static IJobDetail CreateJobDetail(string group, Type jobType)
        {
            return JobBuilder.Create(jobType).WithIdentity(Guid.NewGuid().ToString(), group).Build();
        }

        [DisallowConcurrentExecution]
        public class Job : IJob
        {
            private static readonly ManualResetEvent Done = new ManualResetEvent(false);
            private static int RunCount = 0;
            private static int _operationsPerRun;

            public Task Execute(IJobExecutionContext context)
            {
                if (Interlocked.Increment(ref RunCount) == _operationsPerRun)
                {
                    Done.Set();
                }
                return Task.CompletedTask;
            }

            public static void Initialize(int operationsPerRun)
            {
                _operationsPerRun = operationsPerRun;
            }

            public static void Wait()
            {
                Done.WaitOne();
            }

            public static void Reset()
            {
                Done.Reset();
                RunCount = 0;
            }
        }

        private class NoOpJobStore : IJobStore
        {
            public bool SupportsPersistence => false;

            public long EstimatedTimeToReleaseAndAcquireTrigger => throw new NotImplementedException();

            public bool Clustered => throw new NotImplementedException();

            public string InstanceId { set => throw new NotImplementedException(); }
            public string InstanceName { set => throw new NotImplementedException(); }
            public int ThreadPoolSize { set => throw new NotImplementedException(); }

            public Task<IReadOnlyCollection<IOperableTrigger>> AcquireNextTriggers(DateTimeOffset noLaterThan, int maxCount, TimeSpan timeWindow, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<bool> CalendarExists(string calName, CancellationToken cancellationToken = default)
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

            public Task ClearAllSchedulingData(CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<IReadOnlyCollection<string>> GetCalendarNames(CancellationToken cancellationToken = default)
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

            public Task<int> GetNumberOfCalendars(CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<int> GetNumberOfJobs(CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<int> GetNumberOfTriggers(CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<IReadOnlyCollection<string>> GetPausedTriggerGroups(CancellationToken cancellationToken = default)
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

            public Task<IReadOnlyCollection<IOperableTrigger>> GetTriggersForJob(JobKey jobKey, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<TriggerState> GetTriggerState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task Initialize(ITypeLoadHelper loadHelper, ISchedulerSignaler signaler, CancellationToken cancellationToken = default)
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

            public Task<IReadOnlyCollection<string>> PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<IReadOnlyCollection<string>> PauseTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task ReleaseAcquiredTrigger(IOperableTrigger trigger, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<bool> RemoveCalendar(string calName, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<bool> RemoveJob(JobKey jobKey, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<bool> RemoveJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<bool> RemoveTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<bool> RemoveTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<bool> ReplaceTrigger(TriggerKey triggerKey, IOperableTrigger newTrigger, CancellationToken cancellationToken = default)
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

            public Task<IReadOnlyCollection<string>> ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<IReadOnlyCollection<string>> ResumeTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<ICalendar?> RetrieveCalendar(string calName, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<IJobDetail?> RetrieveJob(JobKey jobKey, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task<IOperableTrigger?> RetrieveTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task SchedulerPaused(CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task SchedulerResumed(CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task SchedulerStarted(CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task Shutdown(CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task StoreCalendar(string name, ICalendar calendar, bool replaceExisting, bool updateTriggers, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task StoreJob(IJobDetail newJob, bool replaceExisting, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task StoreJobAndTrigger(IJobDetail newJob, IOperableTrigger newTrigger, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task StoreJobsAndTriggers(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, bool replace, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task StoreTrigger(IOperableTrigger newTrigger, bool replaceExisting, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public Task TriggeredJobComplete(IOperableTrigger trigger, IJobDetail jobDetail, SchedulerInstruction triggerInstCode, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task<IReadOnlyCollection<TriggerFiredResult>> TriggersFired(IReadOnlyCollection<IOperableTrigger> triggers, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }
        }
    }
}
