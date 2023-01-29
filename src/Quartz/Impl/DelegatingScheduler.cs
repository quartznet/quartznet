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

    public Task<bool> IsJobGroupPaused(string groupName, CancellationToken cancellationToken = default)
    {
        return scheduler.IsJobGroupPaused(groupName, cancellationToken);
    }

    public Task<bool> IsTriggerGroupPaused(string groupName, CancellationToken cancellationToken = default)
    {
        return scheduler.IsTriggerGroupPaused(groupName, cancellationToken);
    }

    public string SchedulerName => scheduler.SchedulerName;
    public string SchedulerInstanceId => scheduler.SchedulerInstanceId;
    public SchedulerContext Context => scheduler.Context;
    public bool InStandbyMode => scheduler.InStandbyMode;
    public bool IsShutdown => scheduler.IsShutdown;

    public Task<SchedulerMetaData> GetMetaData(CancellationToken cancellationToken = default)
    {
        return scheduler.GetMetaData(cancellationToken);
    }

    public Task<IReadOnlyCollection<IJobExecutionContext>> GetCurrentlyExecutingJobs(CancellationToken cancellationToken = default)
    {
        return scheduler.GetCurrentlyExecutingJobs(cancellationToken);
    }

    public IJobFactory JobFactory
    {
        set => scheduler.JobFactory = value;
    }

    public IListenerManager ListenerManager => scheduler.ListenerManager;

    public Task<IReadOnlyCollection<string>> GetJobGroupNames(CancellationToken cancellationToken = default)
    {
        return scheduler.GetJobGroupNames(cancellationToken);
    }

    public Task<IReadOnlyCollection<string>> GetTriggerGroupNames(CancellationToken cancellationToken = default)
    {
        return scheduler.GetTriggerGroupNames(cancellationToken);
    }

    public Task<IReadOnlyCollection<string>> GetPausedTriggerGroups(CancellationToken cancellationToken = default)
    {
        return scheduler.GetPausedTriggerGroups(cancellationToken);
    }

    public Task Start(CancellationToken cancellationToken = default)
    {
        return scheduler.Start(cancellationToken);
    }

    public Task StartDelayed(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        return scheduler.StartDelayed(delay, cancellationToken);
    }

    public bool IsStarted
    {
        get { return scheduler.IsStarted; }
    }

    public Task Standby(CancellationToken cancellationToken = default)
    {
        return scheduler.Standby(cancellationToken);
    }

    public Task Shutdown(CancellationToken cancellationToken = default)
    {
        return scheduler.Shutdown(cancellationToken);
    }

    public Task Shutdown(bool waitForJobsToComplete, CancellationToken cancellationToken = default)
    {
        return scheduler.Shutdown(waitForJobsToComplete, cancellationToken);
    }

    public Task<DateTimeOffset> ScheduleJob(IJobDetail jobDetail, ITrigger trigger, CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJob(jobDetail, trigger, cancellationToken);
    }

    public Task<DateTimeOffset> ScheduleJob(ITrigger trigger, CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJob(trigger, cancellationToken);
    }

    public Task ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, bool replace, CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJobs(triggersAndJobs, replace, cancellationToken);
    }

    public Task ScheduleJob(IJobDetail jobDetail, IReadOnlyCollection<ITrigger> triggersForJob, bool replace, CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJob(jobDetail, triggersForJob, replace, cancellationToken);
    }

    public Task<bool> UnscheduleJob(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.UnscheduleJob(triggerKey, cancellationToken);
    }

    public Task<bool> UnscheduleJobs(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.UnscheduleJobs(triggerKeys, cancellationToken);
    }

    public Task<DateTimeOffset?> RescheduleJob(TriggerKey triggerKey, ITrigger newTrigger, CancellationToken cancellationToken = default)
    {
        return scheduler.RescheduleJob(triggerKey, newTrigger, cancellationToken);
    }

    public Task AddJob(IJobDetail jobDetail, bool replace, CancellationToken cancellationToken = default)
    {
        return scheduler.AddJob(jobDetail, replace, cancellationToken);
    }

    public Task AddJob(IJobDetail jobDetail, bool replace, bool storeNonDurableWhileAwaitingScheduling, CancellationToken cancellationToken = default)
    {
        return scheduler.AddJob(jobDetail, replace, storeNonDurableWhileAwaitingScheduling, cancellationToken);
    }

    public Task<bool> DeleteJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.DeleteJob(jobKey, cancellationToken);
    }

    public Task<bool> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.DeleteJobs(jobKeys, cancellationToken);
    }

    public Task TriggerJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.TriggerJob(jobKey, cancellationToken);
    }

    public Task TriggerJob(JobKey jobKey, JobDataMap data, CancellationToken cancellationToken = default)
    {
        return scheduler.TriggerJob(jobKey, data, cancellationToken);
    }

    public Task PauseJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.PauseJob(jobKey, cancellationToken);
    }

    public Task PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.PauseJobs(matcher, cancellationToken);
    }

    public Task PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.PauseTrigger(triggerKey, cancellationToken);
    }

    public Task PauseTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.PauseTriggers(matcher, cancellationToken);
    }

    public Task ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeJob(jobKey, cancellationToken);
    }

    public Task ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeJobs(matcher, cancellationToken);
    }

    public Task ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeTrigger(triggerKey, cancellationToken);
    }

    public Task ResumeTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeTriggers(matcher, cancellationToken);
    }

    public Task PauseAll(CancellationToken cancellationToken = default)
    {
        return scheduler.PauseAll(cancellationToken);
    }

    public Task ResumeAll(CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeAll(cancellationToken);
    }

    public Task<IReadOnlyCollection<JobKey>> GetJobKeys(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.GetJobKeys(matcher, cancellationToken);
    }

    public Task<IReadOnlyCollection<ITrigger>> GetTriggersOfJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.GetTriggersOfJob(jobKey, cancellationToken);
    }

    public Task<IReadOnlyCollection<TriggerKey>> GetTriggerKeys(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.GetTriggerKeys(matcher, cancellationToken);
    }

    public Task<IJobDetail?> GetJobDetail(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.GetJobDetail(jobKey, cancellationToken);
    }

    public Task<ITrigger?> GetTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.GetTrigger(triggerKey, cancellationToken);
    }

    public Task<TriggerState> GetTriggerState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.GetTriggerState(triggerKey, cancellationToken);
    }

    public Task ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.ResetTriggerFromErrorState(triggerKey, cancellationToken);
    }

    public Task AddCalendar(string calName, ICalendar calendar, bool replace, bool updateTriggers, CancellationToken cancellationToken = default)
    {
        return scheduler.AddCalendar(calName, calendar, replace, updateTriggers, cancellationToken);
    }

    public Task<bool> DeleteCalendar(string calName, CancellationToken cancellationToken = default)
    {
        return scheduler.DeleteCalendar(calName, cancellationToken);
    }

    public Task<ICalendar?> GetCalendar(string calName, CancellationToken cancellationToken = default)
    {
        return scheduler.GetCalendar(calName, cancellationToken);
    }

    public Task<IReadOnlyCollection<string>> GetCalendarNames(CancellationToken cancellationToken = default)
    {
        return scheduler.GetCalendarNames(cancellationToken);
    }

    public Task<bool> Interrupt(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.Interrupt(jobKey, cancellationToken);
    }

    public Task<bool> Interrupt(string fireInstanceId, CancellationToken cancellationToken = default)
    {
        return scheduler.Interrupt(fireInstanceId, cancellationToken);
    }

    public Task<bool> CheckExists(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.CheckExists(jobKey, cancellationToken);
    }

    public Task<bool> CheckExists(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.CheckExists(triggerKey, cancellationToken);
    }

    public Task Clear(CancellationToken cancellationToken = default)
    {
        return scheduler.Clear(cancellationToken);
    }
}