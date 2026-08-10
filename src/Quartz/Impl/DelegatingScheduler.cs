using Quartz.Extensibility;

namespace Quartz.Impl;

public class DelegatingScheduler : IScheduler
{
    private readonly IScheduler scheduler;

    public DelegatingScheduler(IScheduler scheduler)
    {
        this.scheduler = scheduler;
    }

    /// <summary>
    /// The scheduler this one forwards to, so that code which needs the real scheduler - rather than
    /// the behaviour a decorator adds - can reach it through however many layers are in the way.
    /// </summary>
    protected internal IScheduler InnerScheduler => scheduler;

    public string SchedulerName => scheduler.SchedulerName;
    public string SchedulerInstanceId => scheduler.SchedulerInstanceId;
    public SchedulerContext Context => scheduler.Context;
    public bool InStandbyMode => scheduler.InStandbyMode;
    public bool IsShutdown => scheduler.IsShutdown;

    public ValueTask<SchedulerMetadata> GetMetadata(CancellationToken cancellationToken = default)
    {
        return scheduler.GetMetadata(cancellationToken);
    }

    public ValueTask<List<IJobExecutionContext>> GetCurrentlyExecutingJobs(CancellationToken cancellationToken = default)
    {
        return scheduler.GetCurrentlyExecutingJobs(cancellationToken);
    }

    public IListenerManager ListenerManager => scheduler.ListenerManager;

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

    public ValueTask Shutdown(bool waitForJobsToComplete = false, CancellationToken cancellationToken = default)
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

    public ValueTask<bool> UpdateTriggerDetails(TriggerKey triggerKey, TriggerDetailsUpdate update, CancellationToken cancellationToken = default)
    {
        return scheduler.UpdateTriggerDetails(triggerKey, update, cancellationToken);
    }

    public ValueTask SetExecutionLimits(ExecutionLimits? limits, CancellationToken cancellationToken = default)
    {
        return scheduler.SetExecutionLimits(limits, cancellationToken);
    }

    public ValueTask<ExecutionLimits?> GetExecutionLimits(CancellationToken cancellationToken = default)
    {
        return scheduler.GetExecutionLimits(cancellationToken);
    }

    public ValueTask AddJob(IJobDetail jobDetail, AddJobOptions? options = null, CancellationToken cancellationToken = default)
    {
        return scheduler.AddJob(jobDetail, options, cancellationToken);
    }

    public ValueTask<bool> DeleteJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.DeleteJob(jobKey, cancellationToken);
    }

    public ValueTask<bool> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.DeleteJobs(jobKeys, cancellationToken);
    }

    public ValueTask TriggerJob(JobKey jobKey, JobDataMap? data = null, CancellationToken cancellationToken = default)
    {
        return scheduler.TriggerJob(jobKey, data, cancellationToken);
    }

    public ValueTask<bool> PauseJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.PauseJob(jobKey, cancellationToken);
    }

    public ValueTask<List<string>> PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.PauseJobs(matcher, cancellationToken);
    }

    public ValueTask<bool> PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.PauseTrigger(triggerKey, cancellationToken);
    }

    public ValueTask<List<string>> PauseTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.PauseTriggers(matcher, cancellationToken);
    }

    public ValueTask<bool> ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeJob(jobKey, cancellationToken);
    }

    public ValueTask<List<string>> ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeJobs(matcher, cancellationToken);
    }

    public ValueTask<bool> ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeTrigger(triggerKey, cancellationToken);
    }

    public ValueTask<List<string>> ResumeTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
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

    public ValueTask<PagedResult<JobHeader>> QueryJobs(JobQuery query, CancellationToken cancellationToken = default)
    {
        return scheduler.QueryJobs(query, cancellationToken);
    }

    public ValueTask<PagedResult<TriggerHeader>> QueryTriggers(TriggerQuery query, CancellationToken cancellationToken = default)
    {
        return scheduler.QueryTriggers(query, cancellationToken);
    }

    public ValueTask<PagedResult<JobGroup>> QueryJobGroups(JobGroupQuery query, CancellationToken cancellationToken = default)
    {
        return scheduler.QueryJobGroups(query, cancellationToken);
    }

    public ValueTask<PagedResult<TriggerGroup>> QueryTriggerGroups(TriggerGroupQuery query, CancellationToken cancellationToken = default)
    {
        return scheduler.QueryTriggerGroups(query, cancellationToken);
    }

    public ValueTask<PagedResult<string>> QueryCalendarNames(CalendarQuery query, CancellationToken cancellationToken = default)
    {
        return scheduler.QueryCalendarNames(query, cancellationToken);
    }

    public ValueTask<List<IJobDetail>> GetJobDetails(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.GetJobDetails(jobKeys, cancellationToken);
    }

    public ValueTask<List<ITrigger>> GetTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.GetTriggers(triggerKeys, cancellationToken);
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

    public ValueTask<bool> ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.ResetTriggerFromErrorState(triggerKey, cancellationToken);
    }

    public ValueTask AddCalendar(string calendarName, ICalendar calendar, AddCalendarOptions? options = null, CancellationToken cancellationToken = default)
    {
        return scheduler.AddCalendar(calendarName, calendar, options, cancellationToken);
    }

    public ValueTask<bool> DeleteCalendar(string calendarName, CancellationToken cancellationToken = default)
    {
        return scheduler.DeleteCalendar(calendarName, cancellationToken);
    }

    public ValueTask<ICalendar?> GetCalendar(string calendarName, CancellationToken cancellationToken = default)
    {
        return scheduler.GetCalendar(calendarName, cancellationToken);
    }

    public ValueTask<bool> Interrupt(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.Interrupt(jobKey, cancellationToken);
    }

    public ValueTask<bool> InterruptFireInstance(string fireInstanceId, CancellationToken cancellationToken = default)
    {
        return scheduler.InterruptFireInstance(fireInstanceId, cancellationToken);
    }

    public ValueTask<bool> Exists(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.Exists(jobKey, cancellationToken);
    }

    public ValueTask<bool> Exists(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.Exists(triggerKey, cancellationToken);
    }

    public ValueTask Clear(CancellationToken cancellationToken = default)
    {
        return scheduler.Clear(cancellationToken);
    }
}