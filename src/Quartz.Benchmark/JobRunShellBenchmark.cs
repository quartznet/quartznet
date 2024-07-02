using BenchmarkDotNet.Attributes;
using Quartz.Core;
using Quartz.Impl;
using Quartz.Impl.Matchers;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Benchmark;

[MemoryDiagnoser]
public class JobRunShellBenchmark
{
    private readonly QuartzScheduler _basicQuartzScheduler;
    private readonly StdScheduler _basicScheduler;
    private readonly TriggerFiredBundle _bundleMayFireAgain;
    private readonly JobRunShell _jobRunShell;

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
        var trigger = (IOperableTrigger) CreateTrigger(TimeSpan.FromMilliseconds(0.01d));
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

        public ValueTask Execute(IJobExecutionContext context)
        {
            if (Interlocked.Increment(ref RunCount) == _operationsPerRun)
            {
                Done.Set();
            }
            return default;
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
        public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

        public TimeSpan GetAcquireRetryDelay(int failureCount)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<IOperableTrigger>> AcquireNextTriggers(DateTimeOffset noLaterThan, int maxCount, TimeSpan timeWindow, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> CalendarExists(string calName, CancellationToken cancellationToken = default)
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

        public ValueTask ClearAllSchedulingData(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<string>> GetCalendarNames(CancellationToken cancellationToken = default)
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

        public ValueTask<int> GetNumberOfCalendars(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<int> GetNumberOfJobs(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<int> GetNumberOfTriggers(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<string>> GetPausedTriggerGroups(CancellationToken cancellationToken = default)
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

        public ValueTask<List<IOperableTrigger>> GetTriggersForJob(JobKey jobKey, CancellationToken cancellationToken = default)
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

        public ValueTask Initialize(ITypeLoadHelper loadHelper, ISchedulerSignaler signaler, CancellationToken cancellationToken = default)
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

        public ValueTask<List<string>> PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<string>> PauseTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask ReleaseAcquiredTrigger(IOperableTrigger trigger, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> RemoveCalendar(string calName, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> RemoveJob(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> RemoveJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> RemoveTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> RemoveTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> ReplaceTrigger(TriggerKey triggerKey, IOperableTrigger newTrigger, CancellationToken cancellationToken = default)
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

        public ValueTask<List<string>> ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<string>> ResumeTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<ICalendar?> RetrieveCalendar(string calName, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<IJobDetail?> RetrieveJob(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<IOperableTrigger?> RetrieveTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask SchedulerPaused(CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask SchedulerResumed(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask SchedulerStarted(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask Shutdown(CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask StoreCalendar(string name, ICalendar calendar, bool replaceExisting, bool updateTriggers, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask StoreJob(IJobDetail newJob, bool replaceExisting, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask StoreJobAndTrigger(IJobDetail newJob, IOperableTrigger newTrigger, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask StoreJobsAndTriggers(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, bool replace, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask StoreTrigger(IOperableTrigger newTrigger, bool replaceExisting, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask TriggeredJobComplete(IOperableTrigger trigger, IJobDetail jobDetail, SchedulerInstruction triggerInstCode, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask<List<TriggerFiredResult>> TriggersFired(IReadOnlyCollection<IOperableTrigger> triggers, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}