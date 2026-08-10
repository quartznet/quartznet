using Microsoft.Extensions.Logging.Abstractions;
using BenchmarkDotNet.Attributes;
using Quartz.Core;
using Quartz.Impl;
using Quartz.Extensibility;

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

        _jobRunShell = new JobRunShell(_basicScheduler, _bundleMayFireAgain, NullLogger<JobRunShell>.Instance);
        _jobRunShell.Initialize(_basicQuartzScheduler).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _basicQuartzScheduler.Shutdown(true).GetAwaiter().GetResult();
    }

    [Benchmark]
    public ValueTask Success_NoTriggerListenersAndSingleJobListener_MayFireAgain()
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
                    .WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow))
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

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
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

    private sealed class NoOpJobStore : IJobStore
    {
        public bool SupportsPersistence => false;

        public TimeSpan EstimatedTimeToReleaseAndAcquireTrigger => throw new NotImplementedException();

        public bool Clustered => throw new NotImplementedException();

        public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

        public TimeSpan GetAcquireRetryDelay(int failureCount)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<IOperableTrigger>> AcquireNextTriggers(TriggerAcquisitionRequest request, CancellationToken cancellationToken = default)
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

        public ValueTask<List<IJobDetail>> GetJobDetails(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<List<IOperableTrigger>> GetTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
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

        public ValueTask Initialize(CancellationToken cancellationToken = default)
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

        public ValueTask<PagedResult<string>> QueryCalendarNames(CalendarQuery query, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<PagedResult<JobGroup>> QueryJobGroups(JobGroupQuery query, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<PagedResult<JobHeader>> QueryJobs(JobQuery query, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<PagedResult<TriggerGroup>> QueryTriggerGroups(TriggerGroupQuery query, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<PagedResult<TriggerHeader>> QueryTriggers(TriggerQuery query, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask ReleaseAcquiredTrigger(IOperableTrigger trigger, CancellationToken cancellationToken = default)
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

        public ValueTask<bool> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> DeleteTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> DeleteTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> ReplaceTrigger(TriggerKey triggerKey, IOperableTrigger newTrigger, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> UpdateTriggerDetails(TriggerKey triggerKey, TriggerDetailsUpdate update, CancellationToken cancellationToken = default)
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

        public ValueTask<ICalendar?> GetCalendar(string calendarName, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<IJobDetail?> GetJob(JobKey jobKey, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<IOperableTrigger?> GetTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
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

        public ValueTask AddCalendar(string calendarName, ICalendar calendar, AddCalendarOptions? options = null, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask AddJob(IJobDetail newJob, bool replace, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask ScheduleJob(IJobDetail newJob, IOperableTrigger newTrigger, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, bool replace, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask AddTrigger(IOperableTrigger newTrigger, bool replace, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask TriggeredJobComplete(IOperableTrigger trigger, IJobDetail jobDetail, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask<List<TriggerFiredResult>> TriggersFired(IReadOnlyCollection<IOperableTrigger> triggers, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}