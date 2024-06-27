using Quartz.Impl.Matchers;
using Quartz.Spi;

namespace Quartz.Impl;

public class DelegatingScheduler(IScheduler scheduler) : IScheduler
{
    private readonly IScheduler scheduler = scheduler;

    public ValueTask<bool> IsJobGroupPaused(string groupName, CancellationToken cancellationToken = default) =>
        scheduler.IsJobGroupPaused(groupName, cancellationToken);

    public ValueTask<bool> IsTriggerGroupPaused(string groupName, CancellationToken cancellationToken = default) =>
        scheduler.IsTriggerGroupPaused(groupName, cancellationToken);

    public string SchedulerName => scheduler.SchedulerName;
    public string SchedulerInstanceId => scheduler.SchedulerInstanceId;
    public SchedulerContext Context => scheduler.Context;
    public bool InStandbyMode => scheduler.InStandbyMode;
    public bool IsShutdown => scheduler.IsShutdown;

    public ValueTask<SchedulerMetaData> GetMetaData(CancellationToken cancellationToken = default) =>
        scheduler.GetMetaData(cancellationToken);

    public ValueTask<IReadOnlyCollection<IJobExecutionContext>> GetCurrentlyExecutingJobs(CancellationToken cancellationToken = default) =>
        scheduler.GetCurrentlyExecutingJobs(cancellationToken);

    public IJobFactory JobFactory
    {
        set => scheduler.JobFactory = value;
    }

    public IListenerManager ListenerManager => scheduler.ListenerManager;

    public ValueTask<IReadOnlyCollection<string>> GetJobGroupNames(CancellationToken cancellationToken = default) =>
        scheduler.GetJobGroupNames(cancellationToken);

    public ValueTask<IReadOnlyCollection<string>> GetTriggerGroupNames(CancellationToken cancellationToken = default) =>
        scheduler.GetTriggerGroupNames(cancellationToken);

    public ValueTask<IReadOnlyCollection<string>> GetPausedTriggerGroups(CancellationToken cancellationToken = default) =>
        scheduler.GetPausedTriggerGroups(cancellationToken);

    public ValueTask Start(CancellationToken cancellationToken = default) => scheduler.Start(cancellationToken);

    public ValueTask StartDelayed(TimeSpan delay, CancellationToken cancellationToken = default) => scheduler.StartDelayed(delay, cancellationToken);

    public bool IsStarted
    {
        get { return scheduler.IsStarted; }
    }

    public ValueTask Standby(CancellationToken cancellationToken = default) => scheduler.Standby(cancellationToken);

    public ValueTask Shutdown(CancellationToken cancellationToken = default) => scheduler.Shutdown(cancellationToken);

    public ValueTask Shutdown(bool waitForJobsToComplete, CancellationToken cancellationToken = default) =>
        scheduler.Shutdown(waitForJobsToComplete, cancellationToken);

    public ValueTask<DateTimeOffset> ScheduleJob(IJobDetail jobDetail, ITrigger trigger, CancellationToken cancellationToken = default) =>
        scheduler.ScheduleJob(jobDetail, trigger, cancellationToken);

    public ValueTask<DateTimeOffset> ScheduleJob(ITrigger trigger, CancellationToken cancellationToken = default) =>
        scheduler.ScheduleJob(trigger, cancellationToken);

    public ValueTask ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, bool replace, CancellationToken cancellationToken = default) =>
        scheduler.ScheduleJobs(triggersAndJobs, replace, cancellationToken);

    public ValueTask ScheduleJob(IJobDetail jobDetail, IReadOnlyCollection<ITrigger> triggersForJob, bool replace, CancellationToken cancellationToken = default) =>
        scheduler.ScheduleJob(jobDetail, triggersForJob, replace, cancellationToken);

    public ValueTask<bool> UnscheduleJob(TriggerKey triggerKey, CancellationToken cancellationToken = default) =>
        scheduler.UnscheduleJob(triggerKey, cancellationToken);

    public ValueTask<bool> UnscheduleJobs(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default) =>
        scheduler.UnscheduleJobs(triggerKeys, cancellationToken);

    public ValueTask<DateTimeOffset?> RescheduleJob(TriggerKey triggerKey, ITrigger newTrigger, CancellationToken cancellationToken = default) =>
        scheduler.RescheduleJob(triggerKey, newTrigger, cancellationToken);

    public ValueTask AddJob(IJobDetail jobDetail, bool replace, CancellationToken cancellationToken = default) =>
        scheduler.AddJob(jobDetail, replace, cancellationToken);

    public ValueTask AddJob(IJobDetail jobDetail, bool replace, bool storeNonDurableWhileAwaitingScheduling, CancellationToken cancellationToken = default) =>
        scheduler.AddJob(jobDetail, replace, storeNonDurableWhileAwaitingScheduling, cancellationToken);

    public ValueTask<bool> DeleteJob(JobKey jobKey, CancellationToken cancellationToken = default) =>
        scheduler.DeleteJob(jobKey, cancellationToken);

    public ValueTask<bool> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default) =>
        scheduler.DeleteJobs(jobKeys, cancellationToken);

    public ValueTask TriggerJob(JobKey jobKey, CancellationToken cancellationToken = default) =>
        scheduler.TriggerJob(jobKey, cancellationToken);

    public ValueTask TriggerJob(JobKey jobKey, JobDataMap data, CancellationToken cancellationToken = default) =>
        scheduler.TriggerJob(jobKey, data, cancellationToken);

    public ValueTask PauseJob(JobKey jobKey, CancellationToken cancellationToken = default) =>
        scheduler.PauseJob(jobKey, cancellationToken);

    public ValueTask PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default) =>
        scheduler.PauseJobs(matcher, cancellationToken);

    public ValueTask PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default) =>
        scheduler.PauseTrigger(triggerKey, cancellationToken);

    public ValueTask PauseTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default) =>
        scheduler.PauseTriggers(matcher, cancellationToken);

    public ValueTask ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default) =>
        scheduler.ResumeJob(jobKey, cancellationToken);

    public ValueTask ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default) =>
        scheduler.ResumeJobs(matcher, cancellationToken);

    public ValueTask ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default) =>
        scheduler.ResumeTrigger(triggerKey, cancellationToken);

    public ValueTask ResumeTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default) =>
        scheduler.ResumeTriggers(matcher, cancellationToken);

    public ValueTask PauseAll(CancellationToken cancellationToken = default) => scheduler.PauseAll(cancellationToken);

    public ValueTask ResumeAll(CancellationToken cancellationToken = default) => scheduler.ResumeAll(cancellationToken);

    public ValueTask<IReadOnlyCollection<JobKey>> GetJobKeys(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default) =>
        scheduler.GetJobKeys(matcher, cancellationToken);

    public ValueTask<IReadOnlyCollection<ITrigger>> GetTriggersOfJob(JobKey jobKey, CancellationToken cancellationToken = default) =>
        scheduler.GetTriggersOfJob(jobKey, cancellationToken);

    public ValueTask<IReadOnlyCollection<TriggerKey>> GetTriggerKeys(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default) =>
        scheduler.GetTriggerKeys(matcher, cancellationToken);

    public ValueTask<IJobDetail?> GetJobDetail(JobKey jobKey, CancellationToken cancellationToken = default) =>
        scheduler.GetJobDetail(jobKey, cancellationToken);

    public ValueTask<ITrigger?> GetTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default) =>
        scheduler.GetTrigger(triggerKey, cancellationToken);

    public ValueTask<TriggerState> GetTriggerState(TriggerKey triggerKey, CancellationToken cancellationToken = default) =>
        scheduler.GetTriggerState(triggerKey, cancellationToken);

    public ValueTask ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default) =>
        scheduler.ResetTriggerFromErrorState(triggerKey, cancellationToken);

    public ValueTask AddCalendar(string calName, ICalendar calendar, bool replace, bool updateTriggers, CancellationToken cancellationToken = default) =>
        scheduler.AddCalendar(calName, calendar, replace, updateTriggers, cancellationToken);

    public ValueTask<bool> DeleteCalendar(string calName, CancellationToken cancellationToken = default) =>
        scheduler.DeleteCalendar(calName, cancellationToken);

    public ValueTask<ICalendar?> GetCalendar(string calName, CancellationToken cancellationToken = default) =>
        scheduler.GetCalendar(calName, cancellationToken);

    public ValueTask<IReadOnlyCollection<string>> GetCalendarNames(CancellationToken cancellationToken = default) =>
        scheduler.GetCalendarNames(cancellationToken);

    public ValueTask<bool> Interrupt(JobKey jobKey, CancellationToken cancellationToken = default) =>
        scheduler.Interrupt(jobKey, cancellationToken);

    public ValueTask<bool> Interrupt(string fireInstanceId, CancellationToken cancellationToken = default) =>
        scheduler.Interrupt(fireInstanceId, cancellationToken);

    public ValueTask<bool> CheckExists(JobKey jobKey, CancellationToken cancellationToken = default) =>
        scheduler.CheckExists(jobKey, cancellationToken);

    public ValueTask<bool> CheckExists(TriggerKey triggerKey, CancellationToken cancellationToken = default) =>
        scheduler.CheckExists(triggerKey, cancellationToken);

    public ValueTask Clear(CancellationToken cancellationToken = default) => scheduler.Clear(cancellationToken);
}