#nullable enable

using Quartz.Extensibility;
using Quartz.Matchers;

namespace Quartz.Tests;

/// <summary>
/// An <see cref="IJobStore" /> that forwards everything to another one, so a test can override the one
/// operation it wants to interfere with.
/// </summary>
/// <remarks>
/// The stores Quartz ships are sealed: a job store is written against <see cref="IJobStore" />, not
/// derived from an existing implementation. This is the test-side equivalent of that composition.
/// </remarks>
public abstract class DelegatingJobStore : IJobStore
{
    private readonly IJobStore inner;

    protected DelegatingJobStore(IJobStore inner)
    {
        this.inner = inner;
    }

    public virtual bool SupportsPersistence => inner.SupportsPersistence;

    public virtual TimeSpan EstimatedTimeToReleaseAndAcquireTrigger => inner.EstimatedTimeToReleaseAndAcquireTrigger;

    public virtual bool Clustered => inner.Clustered;

    public virtual ValueTask Initialize(CancellationToken cancellationToken = default)
        => inner.Initialize(cancellationToken);

    public virtual ValueTask SchedulerStarted(CancellationToken cancellationToken = default)
        => inner.SchedulerStarted(cancellationToken);

    public virtual ValueTask SchedulerPaused(CancellationToken cancellationToken = default)
        => inner.SchedulerPaused(cancellationToken);

    public virtual ValueTask SchedulerResumed(CancellationToken cancellationToken = default)
        => inner.SchedulerResumed(cancellationToken);

    public virtual ValueTask Shutdown(CancellationToken cancellationToken = default)
        => inner.Shutdown(cancellationToken);

    public virtual ValueTask ScheduleJob(IJobDetail job, IOperableTrigger trigger, CancellationToken cancellationToken = default)
        => inner.ScheduleJob(job, trigger, cancellationToken);

    public virtual ValueTask AddJob(IJobDetail job, bool replace, CancellationToken cancellationToken = default)
        => inner.AddJob(job, replace, cancellationToken);

    public virtual ValueTask ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, bool replace, CancellationToken cancellationToken = default)
        => inner.ScheduleJobs(triggersAndJobs, replace, cancellationToken);

    public virtual ValueTask<bool> DeleteJob(JobKey jobKey, CancellationToken cancellationToken = default)
        => inner.DeleteJob(jobKey, cancellationToken);

    public virtual ValueTask<bool> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
        => inner.DeleteJobs(jobKeys, cancellationToken);

    public virtual ValueTask<IJobDetail?> GetJob(JobKey jobKey, CancellationToken cancellationToken = default)
        => inner.GetJob(jobKey, cancellationToken);

    public virtual ValueTask AddTrigger(IOperableTrigger trigger, bool replace, CancellationToken cancellationToken = default)
        => inner.AddTrigger(trigger, replace, cancellationToken);

    public virtual ValueTask<bool> DeleteTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        => inner.DeleteTrigger(triggerKey, cancellationToken);

    public virtual ValueTask<bool> DeleteTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
        => inner.DeleteTriggers(triggerKeys, cancellationToken);

    public virtual ValueTask<bool> ReplaceTrigger(TriggerKey triggerKey, IOperableTrigger trigger, CancellationToken cancellationToken = default)
        => inner.ReplaceTrigger(triggerKey, trigger, cancellationToken);

    public virtual ValueTask<bool> UpdateTriggerDetails(TriggerKey triggerKey, TriggerDetailsUpdate update, CancellationToken cancellationToken = default)
        => inner.UpdateTriggerDetails(triggerKey, update, cancellationToken);

    public virtual ValueTask<IOperableTrigger?> GetTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        => inner.GetTrigger(triggerKey, cancellationToken);

    public virtual ValueTask<bool> CheckExists(JobKey jobKey, CancellationToken cancellationToken = default)
        => inner.CheckExists(jobKey, cancellationToken);

    public virtual ValueTask<bool> CheckExists(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        => inner.CheckExists(triggerKey, cancellationToken);

    public virtual ValueTask Clear(CancellationToken cancellationToken = default)
        => inner.Clear(cancellationToken);

    public virtual ValueTask AddCalendar(string calendarName, ICalendar calendar, AddCalendarOptions? options = null, CancellationToken cancellationToken = default)
        => inner.AddCalendar(calendarName, calendar, options, cancellationToken);

    public virtual ValueTask<bool> DeleteCalendar(string calendarName, CancellationToken cancellationToken = default)
        => inner.DeleteCalendar(calendarName, cancellationToken);

    public virtual ValueTask<ICalendar?> GetCalendar(string calendarName, CancellationToken cancellationToken = default)
        => inner.GetCalendar(calendarName, cancellationToken);

    public virtual ValueTask<PagedResult<JobHeader>> QueryJobs(JobQuery query, CancellationToken cancellationToken = default)
        => inner.QueryJobs(query, cancellationToken);

    public virtual ValueTask<PagedResult<TriggerHeader>> QueryTriggers(TriggerQuery query, CancellationToken cancellationToken = default)
        => inner.QueryTriggers(query, cancellationToken);

    public virtual ValueTask<PagedResult<JobGroup>> QueryJobGroups(JobGroupQuery query, CancellationToken cancellationToken = default)
        => inner.QueryJobGroups(query, cancellationToken);

    public virtual ValueTask<PagedResult<TriggerGroup>> QueryTriggerGroups(TriggerGroupQuery query, CancellationToken cancellationToken = default)
        => inner.QueryTriggerGroups(query, cancellationToken);

    public virtual ValueTask<PagedResult<string>> QueryCalendarNames(CalendarQuery query, CancellationToken cancellationToken = default)
        => inner.QueryCalendarNames(query, cancellationToken);

    public virtual ValueTask<List<IJobDetail>> GetJobDetails(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
        => inner.GetJobDetails(jobKeys, cancellationToken);

    public virtual ValueTask<List<IOperableTrigger>> GetTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
        => inner.GetTriggers(triggerKeys, cancellationToken);

    public virtual ValueTask<List<IOperableTrigger>> GetTriggersForJob(JobKey jobKey, CancellationToken cancellationToken = default)
        => inner.GetTriggersForJob(jobKey, cancellationToken);

    public virtual ValueTask<TriggerState> GetTriggerState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        => inner.GetTriggerState(triggerKey, cancellationToken);

    public virtual ValueTask ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        => inner.ResetTriggerFromErrorState(triggerKey, cancellationToken);

    public virtual ValueTask PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        => inner.PauseTrigger(triggerKey, cancellationToken);

    public virtual ValueTask<List<string>> PauseTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
        => inner.PauseTriggers(matcher, cancellationToken);

    public virtual ValueTask PauseJob(JobKey jobKey, CancellationToken cancellationToken = default)
        => inner.PauseJob(jobKey, cancellationToken);

    public virtual ValueTask<List<string>> PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
        => inner.PauseJobs(matcher, cancellationToken);

    public virtual ValueTask ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        => inner.ResumeTrigger(triggerKey, cancellationToken);

    public virtual ValueTask<List<string>> ResumeTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
        => inner.ResumeTriggers(matcher, cancellationToken);

    public virtual ValueTask ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default)
        => inner.ResumeJob(jobKey, cancellationToken);

    public virtual ValueTask<List<string>> ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
        => inner.ResumeJobs(matcher, cancellationToken);

    public virtual ValueTask PauseAll(CancellationToken cancellationToken = default)
        => inner.PauseAll(cancellationToken);

    public virtual ValueTask ResumeAll(CancellationToken cancellationToken = default)
        => inner.ResumeAll(cancellationToken);

    public virtual ValueTask<List<IOperableTrigger>> AcquireNextTriggers(TriggerAcquisitionRequest request, CancellationToken cancellationToken = default)
        => inner.AcquireNextTriggers(request, cancellationToken);

    public virtual ValueTask ReleaseAcquiredTrigger(IOperableTrigger trigger, CancellationToken cancellationToken = default)
        => inner.ReleaseAcquiredTrigger(trigger, cancellationToken);

    public virtual ValueTask<List<TriggerFiredResult>> TriggersFired(IReadOnlyCollection<IOperableTrigger> triggers, CancellationToken cancellationToken = default)
        => inner.TriggersFired(triggers, cancellationToken);

    public virtual ValueTask TriggeredJobComplete(IOperableTrigger trigger, IJobDetail jobDetail, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default)
        => inner.TriggeredJobComplete(trigger, jobDetail, triggerInstructionCode, cancellationToken);

    public virtual TimeSpan GetAcquireRetryDelay(int failureCount)
        => inner.GetAcquireRetryDelay(failureCount);
}
