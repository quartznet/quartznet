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

    public virtual string SchedulerName => scheduler.SchedulerName;
    public virtual string SchedulerInstanceId => scheduler.SchedulerInstanceId;
    public virtual TimeProvider TimeProvider => scheduler.TimeProvider;
    public virtual SchedulerContext Context => scheduler.Context;
    public virtual SchedulerStatus Status => scheduler.Status;

    public virtual ValueTask<SchedulerMetadata> GetMetadata(CancellationToken cancellationToken = default)
    {
        return scheduler.GetMetadata(cancellationToken);
    }

    public virtual ValueTask<PagedResult<FireInstance>> QueryFireInstances(FireInstanceQuery query, CancellationToken cancellationToken = default)
    {
        return scheduler.QueryFireInstances(query, cancellationToken);
    }

    public virtual ValueTask<List<ClusterNode>> QueryClusterNodes(CancellationToken cancellationToken = default)
    {
        return scheduler.QueryClusterNodes(cancellationToken);
    }

    public virtual IListenerManager ListenerManager => scheduler.ListenerManager;

    public virtual ValueTask Start(CancellationToken cancellationToken = default)
    {
        return scheduler.Start(cancellationToken);
    }

    public virtual ValueTask StartDelayed(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        return scheduler.StartDelayed(delay, cancellationToken);
    }

    public virtual ValueTask Standby(CancellationToken cancellationToken = default)
    {
        return scheduler.Standby(cancellationToken);
    }

    public virtual ValueTask Shutdown(bool waitForJobsToComplete = false, CancellationToken cancellationToken = default)
    {
        return scheduler.Shutdown(waitForJobsToComplete, cancellationToken);
    }

    /// <summary>
    /// Disposes the scheduler this one forwards to, whose ownership rule therefore decides what happens.
    /// </summary>
    /// <remarks>
    /// A decorator owns nothing of its own. Override this when a subclass acquires something it has to
    /// release, and forward to <c>base.DisposeAsync()</c>.
    /// </remarks>
    public virtual ValueTask DisposeAsync()
    {
        // This type has no finalizer, but it is public and unsealed, so a subclass may introduce one;
        // suppressing here saves that subclass from re-implementing disposal just to say so (CA1816).
        GC.SuppressFinalize(this);
        return scheduler.DisposeAsync();
    }

    public virtual ValueTask<DateTimeOffset> ScheduleJob(IJobDetail jobDetail, ITrigger trigger, CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJob(jobDetail, trigger, cancellationToken);
    }

    public virtual ValueTask<DateTimeOffset> ScheduleJob(IJobDetail jobDetail, ITrigger trigger, ScheduleJobOptions options, CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJob(jobDetail, trigger, options, cancellationToken);
    }

    public virtual ValueTask<DateTimeOffset> ScheduleJob(ITrigger trigger, CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJob(trigger, cancellationToken);
    }

    public virtual ValueTask<DateTimeOffset> ScheduleJob(ITrigger trigger, ScheduleJobOptions options, CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJob(trigger, options, cancellationToken);
    }

    public virtual ValueTask ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, ScheduleJobOptions options = default, CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJobs(triggersAndJobs, options, cancellationToken);
    }

    public virtual ValueTask ScheduleJob(IJobDetail jobDetail, IReadOnlyCollection<ITrigger> triggersForJob, ScheduleJobOptions options = default, CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJob(jobDetail, triggersForJob, options, cancellationToken);
    }

    public virtual ValueTask<bool> UnscheduleJob(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.UnscheduleJob(triggerKey, cancellationToken);
    }

    public virtual ValueTask<List<TriggerKey>> UnscheduleJobs(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.UnscheduleJobs(triggerKeys, cancellationToken);
    }

    public virtual ValueTask<DateTimeOffset?> RescheduleJob(TriggerKey triggerKey, ITrigger newTrigger, CancellationToken cancellationToken = default)
    {
        return scheduler.RescheduleJob(triggerKey, newTrigger, cancellationToken);
    }

    public virtual ValueTask<bool> UpdateTriggerDetails(TriggerKey triggerKey, TriggerDetailsUpdate update, CancellationToken cancellationToken = default)
    {
        return scheduler.UpdateTriggerDetails(triggerKey, update, cancellationToken);
    }

    public virtual ValueTask SetExecutionLimits(ExecutionLimits? limits, CancellationToken cancellationToken = default)
    {
        return scheduler.SetExecutionLimits(limits, cancellationToken);
    }

    public virtual ValueTask<ExecutionLimits?> GetExecutionLimits(CancellationToken cancellationToken = default)
    {
        return scheduler.GetExecutionLimits(cancellationToken);
    }

    public virtual ValueTask AddJob(IJobDetail jobDetail, AddJobOptions options = default, CancellationToken cancellationToken = default)
    {
        return scheduler.AddJob(jobDetail, options, cancellationToken);
    }

    public virtual ValueTask<bool> DeleteJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.DeleteJob(jobKey, cancellationToken);
    }

    public virtual ValueTask<List<JobKey>> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.DeleteJobs(jobKeys, cancellationToken);
    }

    public virtual ValueTask TriggerJob(JobKey jobKey, JobDataMap? data = null, CancellationToken cancellationToken = default)
    {
        return scheduler.TriggerJob(jobKey, data, cancellationToken);
    }

    public virtual ValueTask<bool> PauseJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.PauseJob(jobKey, cancellationToken);
    }

    public virtual ValueTask<List<string>> PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.PauseJobs(matcher, cancellationToken);
    }

    public virtual ValueTask<List<JobKey>> PauseJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.PauseJobs(jobKeys, cancellationToken);
    }

    public virtual ValueTask<bool> PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.PauseTrigger(triggerKey, cancellationToken);
    }

    public virtual ValueTask<List<string>> PauseTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.PauseTriggers(matcher, cancellationToken);
    }

    public virtual ValueTask<List<TriggerKey>> PauseTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.PauseTriggers(triggerKeys, cancellationToken);
    }

    public virtual ValueTask<bool> ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeJob(jobKey, cancellationToken);
    }

    public virtual ValueTask<List<string>> ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeJobs(matcher, cancellationToken);
    }

    public virtual ValueTask<List<JobKey>> ResumeJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeJobs(jobKeys, cancellationToken);
    }

    public virtual ValueTask<bool> ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeTrigger(triggerKey, cancellationToken);
    }

    public virtual ValueTask<List<string>> ResumeTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeTriggers(matcher, cancellationToken);
    }

    public virtual ValueTask<List<TriggerKey>> ResumeTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeTriggers(triggerKeys, cancellationToken);
    }

    public virtual ValueTask PauseAll(CancellationToken cancellationToken = default)
    {
        return scheduler.PauseAll(cancellationToken);
    }

    public virtual ValueTask ResumeAll(CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeAll(cancellationToken);
    }

    public virtual ValueTask<PagedResult<JobHeader>> QueryJobs(JobQuery query, CancellationToken cancellationToken = default)
    {
        return scheduler.QueryJobs(query, cancellationToken);
    }

    public virtual ValueTask<PagedResult<TriggerHeader>> QueryTriggers(TriggerQuery query, CancellationToken cancellationToken = default)
    {
        return scheduler.QueryTriggers(query, cancellationToken);
    }

    public virtual ValueTask<PagedResult<JobGroup>> QueryJobGroups(JobGroupQuery query, CancellationToken cancellationToken = default)
    {
        return scheduler.QueryJobGroups(query, cancellationToken);
    }

    public virtual ValueTask<PagedResult<TriggerGroup>> QueryTriggerGroups(TriggerGroupQuery query, CancellationToken cancellationToken = default)
    {
        return scheduler.QueryTriggerGroups(query, cancellationToken);
    }

    public virtual ValueTask<PagedResult<string>> QueryCalendarNames(CalendarQuery query, CancellationToken cancellationToken = default)
    {
        return scheduler.QueryCalendarNames(query, cancellationToken);
    }

    public virtual ValueTask<List<IJobDetail>> GetJobDetails(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.GetJobDetails(jobKeys, cancellationToken);
    }

    public virtual ValueTask<List<ITrigger>> GetTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.GetTriggers(triggerKeys, cancellationToken);
    }

    public virtual ValueTask<IJobDetail?> GetJobDetail(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.GetJobDetail(jobKey, cancellationToken);
    }

    public virtual ValueTask<ITrigger?> GetTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.GetTrigger(triggerKey, cancellationToken);
    }

    public virtual ValueTask<TriggerState> GetTriggerState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.GetTriggerState(triggerKey, cancellationToken);
    }

    public virtual ValueTask<bool> ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.ResetTriggerFromErrorState(triggerKey, cancellationToken);
    }

    public virtual ValueTask<List<TriggerKey>> ResetTriggersFromErrorState(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.ResetTriggersFromErrorState(triggerKeys, cancellationToken);
    }

    public virtual ValueTask AddCalendar(string calendarName, ICalendar calendar, AddCalendarOptions options = default, CancellationToken cancellationToken = default)
    {
        return scheduler.AddCalendar(calendarName, calendar, options, cancellationToken);
    }

    public virtual ValueTask<bool> DeleteCalendar(string calendarName, CancellationToken cancellationToken = default)
    {
        return scheduler.DeleteCalendar(calendarName, cancellationToken);
    }

    public virtual ValueTask<ICalendar?> GetCalendar(string calendarName, CancellationToken cancellationToken = default)
    {
        return scheduler.GetCalendar(calendarName, cancellationToken);
    }

    public virtual ValueTask<bool> Interrupt(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.Interrupt(jobKey, cancellationToken);
    }

    public virtual ValueTask<bool> InterruptFireInstance(string fireInstanceId, CancellationToken cancellationToken = default)
    {
        return scheduler.InterruptFireInstance(fireInstanceId, cancellationToken);
    }

    public virtual ValueTask<bool> Exists(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.Exists(jobKey, cancellationToken);
    }

    public virtual ValueTask<bool> Exists(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.Exists(triggerKey, cancellationToken);
    }

    public virtual ValueTask Clear(CancellationToken cancellationToken = default)
    {
        return scheduler.Clear(cancellationToken);
    }
}