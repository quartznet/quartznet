using Quartz.Extensibility;

namespace Quartz.Impl;

/// <summary>
/// A scheduler that forwards every member to another one, so that a decorator overrides only what it
/// changes.
/// </summary>
/// <remarks>
/// Every member is <c>virtual</c>, and a member added to <see cref="IScheduler" /> lands here as one
/// more forwarder — which is what keeps a decorator in somebody else's codebase compiling.
/// </remarks>
public class DelegatingScheduler : IScheduler
{
    private readonly IScheduler scheduler;

    /// <summary>
    /// Wraps the scheduler this one forwards to.
    /// </summary>
    /// <param name="scheduler">The scheduler every member is forwarded to.</param>
    public DelegatingScheduler(IScheduler scheduler)
    {
        this.scheduler = scheduler;
    }

    /// <summary>
    /// The scheduler this one forwards to, so that code which needs the real scheduler - rather than
    /// the behaviour a decorator adds - can reach it through however many layers are in the way.
    /// </summary>
    protected internal IScheduler InnerScheduler => scheduler;

    /// <inheritdoc />
    public virtual string SchedulerName => scheduler.SchedulerName;

    /// <inheritdoc />
    public virtual string SchedulerInstanceId => scheduler.SchedulerInstanceId;

    /// <inheritdoc />
    public virtual TimeProvider TimeProvider => scheduler.TimeProvider;

    /// <inheritdoc />
    public virtual SchedulerContext Context => scheduler.Context;

    /// <inheritdoc />
    public virtual SchedulerStatus Status => scheduler.Status;

    /// <inheritdoc />
    public virtual ValueTask<SchedulerMetadata> GetMetadata(CancellationToken cancellationToken = default)
    {
        return scheduler.GetMetadata(cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<PagedResult<FireInstance>> QueryFireInstances(FireInstanceQuery query, CancellationToken cancellationToken = default)
    {
        return scheduler.QueryFireInstances(query, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<ClusterNode>> QueryClusterNodes(CancellationToken cancellationToken = default)
    {
        return scheduler.QueryClusterNodes(cancellationToken);
    }

    /// <inheritdoc />
    public virtual IListenerManager ListenerManager => scheduler.ListenerManager;

    /// <inheritdoc />
    public virtual ValueTask Start(CancellationToken cancellationToken = default)
    {
        return scheduler.Start(cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask StartDelayed(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        return scheduler.StartDelayed(delay, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask Standby(CancellationToken cancellationToken = default)
    {
        return scheduler.Standby(cancellationToken);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public virtual ValueTask<DateTimeOffset> ScheduleJob(IJobDetail jobDetail, ITrigger trigger, ScheduleJobOptions options = default, CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJob(jobDetail, trigger, options, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<DateTimeOffset> ScheduleJob(ITrigger trigger, ScheduleJobOptions options = default, CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJob(trigger, options, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, ScheduleJobOptions options = default, CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJobs(triggersAndJobs, options, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask ScheduleJob(IJobDetail jobDetail, IReadOnlyCollection<ITrigger> triggersForJob, ScheduleJobOptions options = default, CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJob(jobDetail, triggersForJob, options, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> UnscheduleJob(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.UnscheduleJob(triggerKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<TriggerKey>> UnscheduleJobs(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.UnscheduleJobs(triggerKeys, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<TriggerKey>> UnscheduleJobs(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.UnscheduleJobs(matcher, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<DateTimeOffset?> RescheduleJob(TriggerKey triggerKey, ITrigger newTrigger, CancellationToken cancellationToken = default)
    {
        return scheduler.RescheduleJob(triggerKey, newTrigger, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> UpdateTriggerDetails(TriggerKey triggerKey, TriggerDetailsUpdate update, CancellationToken cancellationToken = default)
    {
        return scheduler.UpdateTriggerDetails(triggerKey, update, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask SetExecutionLimits(ExecutionLimits? limits, CancellationToken cancellationToken = default)
    {
        return scheduler.SetExecutionLimits(limits, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<ExecutionLimits?> GetExecutionLimits(CancellationToken cancellationToken = default)
    {
        return scheduler.GetExecutionLimits(cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask AddJob(IJobDetail jobDetail, AddJobOptions options = default, CancellationToken cancellationToken = default)
    {
        return scheduler.AddJob(jobDetail, options, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> DeleteJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.DeleteJob(jobKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<JobKey>> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.DeleteJobs(jobKeys, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<JobKey>> DeleteJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.DeleteJobs(matcher, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask TriggerJob(JobKey jobKey, JobDataMap? data = null, CancellationToken cancellationToken = default)
    {
        return scheduler.TriggerJob(jobKey, data, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> PauseJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.PauseJob(jobKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<string>> PauseJobGroups(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.PauseJobGroups(matcher, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<JobKey>> PauseJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.PauseJobs(jobKeys, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.PauseTrigger(triggerKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<string>> PauseTriggerGroups(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.PauseTriggerGroups(matcher, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<TriggerKey>> PauseTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.PauseTriggers(triggerKeys, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeJob(jobKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<string>> ResumeJobGroups(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeJobGroups(matcher, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<JobKey>> ResumeJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeJobs(jobKeys, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeTrigger(triggerKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<string>> ResumeTriggerGroups(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeTriggerGroups(matcher, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<TriggerKey>> ResumeTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeTriggers(triggerKeys, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask PauseAll(CancellationToken cancellationToken = default)
    {
        return scheduler.PauseAll(cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask ResumeAll(CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeAll(cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<PagedResult<JobHeader>> QueryJobs(JobQuery query, CancellationToken cancellationToken = default)
    {
        return scheduler.QueryJobs(query, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<PagedResult<TriggerHeader>> QueryTriggers(TriggerQuery query, CancellationToken cancellationToken = default)
    {
        return scheduler.QueryTriggers(query, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<PagedResult<JobGroup>> QueryJobGroups(JobGroupQuery query, CancellationToken cancellationToken = default)
    {
        return scheduler.QueryJobGroups(query, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<PagedResult<TriggerGroup>> QueryTriggerGroups(TriggerGroupQuery query, CancellationToken cancellationToken = default)
    {
        return scheduler.QueryTriggerGroups(query, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<PagedResult<string>> QueryCalendarNames(CalendarQuery query, CancellationToken cancellationToken = default)
    {
        return scheduler.QueryCalendarNames(query, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<IJobDetail>> GetJobDetails(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.GetJobDetails(jobKeys, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<ITrigger>> GetTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.GetTriggers(triggerKeys, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<IJobDetail?> GetJobDetail(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.GetJobDetail(jobKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<ITrigger?> GetTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.GetTrigger(triggerKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<TriggerState> GetTriggerState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.GetTriggerState(triggerKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.ResetTriggerFromErrorState(triggerKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<TriggerKey>> ResetTriggersFromErrorState(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.ResetTriggersFromErrorState(triggerKeys, cancellationToken);
    }

    /// <summary>
    /// Forwards the group form rather than letting <see cref="IScheduler" />'s default implementation
    /// run here.
    /// </summary>
    /// <remarks>
    /// A default interface member is not inherited into a class's member set, so a decorator that does
    /// not declare it lets the interface default execute *on the decorator* — decomposing into a query
    /// and a second call, both of which come back through this class. That happens to give the right
    /// answer for this member, and it is the wrong shape to rely on: an inner scheduler that can reset a
    /// group in one statement never gets asked to, a listening decorator sees two calls where one was
    /// made, and the first default whose decomposition is not equivalent would be a live bug.
    /// <c>DelegatingForwardingTest</c> holds every member of the interface to being declared here.
    /// </remarks>
    public virtual ValueTask<List<TriggerKey>> ResetTriggersFromErrorState(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.ResetTriggersFromErrorState(matcher, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask AddCalendar(string calendarName, ICalendar calendar, AddCalendarOptions options = default, CancellationToken cancellationToken = default)
    {
        return scheduler.AddCalendar(calendarName, calendar, options, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> DeleteCalendar(string calendarName, CancellationToken cancellationToken = default)
    {
        return scheduler.DeleteCalendar(calendarName, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<ICalendar?> GetCalendar(string calendarName, CancellationToken cancellationToken = default)
    {
        return scheduler.GetCalendar(calendarName, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> Interrupt(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.Interrupt(jobKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> InterruptFireInstance(string fireInstanceId, CancellationToken cancellationToken = default)
    {
        return scheduler.InterruptFireInstance(fireInstanceId, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> Exists(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.Exists(jobKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> Exists(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.Exists(triggerKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> Exists(string calendarName, CancellationToken cancellationToken = default)
    {
        return scheduler.Exists(calendarName, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask Clear(CancellationToken cancellationToken = default)
    {
        return scheduler.Clear(cancellationToken);
    }
}