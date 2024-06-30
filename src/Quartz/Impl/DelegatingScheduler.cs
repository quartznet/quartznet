using Quartz.Impl.Matchers;
using Quartz.Spi;

namespace Quartz.Impl;

public class DelegatingScheduler : IScheduler
{
    private readonly IScheduler scheduler;

    public DelegatingScheduler(IScheduler scheduler)
    {
        this.scheduler = scheduler;
    }

    public ValueTask<bool> IsJobGroupPaused(string groupName, CancellationToken cancellationToken = default)
    {
        return scheduler.IsJobGroupPaused(groupName, cancellationToken);
    }

    public ValueTask<bool> IsTriggerGroupPaused(string groupName, CancellationToken cancellationToken = default)
    {
        return scheduler.IsTriggerGroupPaused(groupName, cancellationToken);
    }

    public string SchedulerName => scheduler.SchedulerName;
    public string SchedulerInstanceId => scheduler.SchedulerInstanceId;
    public SchedulerContext Context => scheduler.Context;
    public bool InStandbyMode => scheduler.InStandbyMode;
    public bool IsShutdown => scheduler.IsShutdown;

    public ValueTask<SchedulerMetaData> GetMetaData(CancellationToken cancellationToken = default)
    {
        return scheduler.GetMetaData(cancellationToken);
    }

    public ValueTask<List<IJobExecutionContext>> GetCurrentlyExecutingJobs(CancellationToken cancellationToken = default)
    {
        return scheduler.GetCurrentlyExecutingJobs(cancellationToken);
    }

    public IJobFactory JobFactory
    {
        set => scheduler.JobFactory = value;
    }

    public IListenerManager ListenerManager => scheduler.ListenerManager;

    public ValueTask<List<string>> GetJobGroupNames(CancellationToken cancellationToken = default)
    {
        return scheduler.GetJobGroupNames(cancellationToken);
    }

    public ValueTask<List<string>> GetTriggerGroupNames(CancellationToken cancellationToken = default)
    {
        return scheduler.GetTriggerGroupNames(cancellationToken);
    }

    public ValueTask<List<string>> GetPausedTriggerGroups(CancellationToken cancellationToken = default)
    {
        return scheduler.GetPausedTriggerGroups(cancellationToken);
    }

    public ValueTask Start(CancellationToken cancellationToken = default)
    {
        return scheduler.Start(cancellationToken);
    }

    public ValueTask StartDelayed(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        return scheduler.StartDelayed(delay, cancellationToken);
    }

    public bool IsStarted => scheduler.IsStarted;

    public ValueTask Standby(CancellationToken cancellationToken = default)
    {
        return scheduler.Standby(cancellationToken);
    }

    public ValueTask Shutdown(CancellationToken cancellationToken = default)
    {
        return scheduler.Shutdown(cancellationToken);
    }

    public ValueTask Shutdown(bool waitForJobsToComplete, CancellationToken cancellationToken = default)
    {
        return scheduler.Shutdown(waitForJobsToComplete, cancellationToken);
    }

    public ValueTask<DateTimeOffset> ScheduleJob(IJobDetail jobDetail, ITrigger trigger, CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJob(jobDetail, trigger, cancellationToken);
    }

    public ValueTask<DateTimeOffset> ScheduleJob(ITrigger trigger, CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJob(trigger, cancellationToken);
    }

    public ValueTask ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, bool replace, CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJobs(triggersAndJobs, replace, cancellationToken);
    }

    public ValueTask ScheduleJob(IJobDetail jobDetail, IReadOnlyCollection<ITrigger> triggersForJob, bool replace, CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJob(jobDetail, triggersForJob, replace, cancellationToken);
    }

    public ValueTask<bool> UnscheduleJob(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.UnscheduleJob(triggerKey, cancellationToken);
    }

    public ValueTask<bool> UnscheduleJobs(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.UnscheduleJobs(triggerKeys, cancellationToken);
    }

    public ValueTask<DateTimeOffset?> RescheduleJob(TriggerKey triggerKey, ITrigger newTrigger, CancellationToken cancellationToken = default)
    {
        return scheduler.RescheduleJob(triggerKey, newTrigger, cancellationToken);
    }

    public ValueTask AddJob(IJobDetail jobDetail, bool replace, CancellationToken cancellationToken = default)
    {
        return scheduler.AddJob(jobDetail, replace, cancellationToken);
    }

    public ValueTask AddJob(IJobDetail jobDetail, bool replace, bool storeNonDurableWhileAwaitingScheduling, CancellationToken cancellationToken = default)
    {
        return scheduler.AddJob(jobDetail, replace, storeNonDurableWhileAwaitingScheduling, cancellationToken);
    }

    public ValueTask<bool> DeleteJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.DeleteJob(jobKey, cancellationToken);
    }

    public ValueTask<bool> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.DeleteJobs(jobKeys, cancellationToken);
    }

    public ValueTask TriggerJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.TriggerJob(jobKey, cancellationToken);
    }

    public ValueTask TriggerJob(JobKey jobKey, JobDataMap data, CancellationToken cancellationToken = default)
    {
        return scheduler.TriggerJob(jobKey, data, cancellationToken);
    }

    public ValueTask PauseJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.PauseJob(jobKey, cancellationToken);
    }

    public ValueTask PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.PauseJobs(matcher, cancellationToken);
    }

    public ValueTask PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.PauseTrigger(triggerKey, cancellationToken);
    }

    public ValueTask PauseTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.PauseTriggers(matcher, cancellationToken);
    }

    public ValueTask ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeJob(jobKey, cancellationToken);
    }

    public ValueTask ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeJobs(matcher, cancellationToken);
    }

    public ValueTask ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeTrigger(triggerKey, cancellationToken);
    }

    public ValueTask ResumeTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeTriggers(matcher, cancellationToken);
    }

    public ValueTask PauseAll(CancellationToken cancellationToken = default)
    {
        return scheduler.PauseAll(cancellationToken);
    }

    public ValueTask ResumeAll(CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeAll(cancellationToken);
    }

    public ValueTask<List<JobKey>> GetJobKeys(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.GetJobKeys(matcher, cancellationToken);
    }

    public ValueTask<List<ITrigger>> GetTriggersOfJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.GetTriggersOfJob(jobKey, cancellationToken);
    }

    public ValueTask<List<TriggerKey>> GetTriggerKeys(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.GetTriggerKeys(matcher, cancellationToken);
    }

    public ValueTask<IJobDetail?> GetJobDetail(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.GetJobDetail(jobKey, cancellationToken);
    }

    public ValueTask<ITrigger?> GetTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.GetTrigger(triggerKey, cancellationToken);
    }

    public ValueTask<TriggerState> GetTriggerState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.GetTriggerState(triggerKey, cancellationToken);
    }

    public ValueTask ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.ResetTriggerFromErrorState(triggerKey, cancellationToken);
    }

    public ValueTask AddCalendar(string name, ICalendar calendar, bool replace, bool updateTriggers, CancellationToken cancellationToken = default)
    {
        return scheduler.AddCalendar(name, calendar, replace, updateTriggers, cancellationToken);
    }

    public ValueTask<bool> DeleteCalendar(string name, CancellationToken cancellationToken = default)
    {
        return scheduler.DeleteCalendar(name, cancellationToken);
    }

    public ValueTask<ICalendar?> GetCalendar(string name, CancellationToken cancellationToken = default)
    {
        return scheduler.GetCalendar(name, cancellationToken);
    }

    public ValueTask<List<string>> GetCalendarNames(CancellationToken cancellationToken = default)
    {
        return scheduler.GetCalendarNames(cancellationToken);
    }

    public ValueTask<bool> Interrupt(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.Interrupt(jobKey, cancellationToken);
    }

    public ValueTask<bool> Interrupt(string fireInstanceId, CancellationToken cancellationToken = default)
    {
        return scheduler.Interrupt(fireInstanceId, cancellationToken);
    }

    public ValueTask<bool> CheckExists(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.CheckExists(jobKey, cancellationToken);
    }

    public ValueTask<bool> CheckExists(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.CheckExists(triggerKey, cancellationToken);
    }

    public ValueTask Clear(CancellationToken cancellationToken = default)
    {
        return scheduler.Clear(cancellationToken);
    }
}