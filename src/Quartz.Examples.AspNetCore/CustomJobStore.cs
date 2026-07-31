using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Matchers;

namespace Quartz.Examples.AspNetCore;

/// <summary>
/// Shows a job store of your own, registered through <c>UsePersistentStore&lt;T&gt;</c> and taking
/// dependencies of its own from the container.
/// </summary>
/// <remarks>
/// The stores Quartz ships are sealed, so a job store is written against <see cref="IJobStore" /> rather
/// than derived from one. This one composes <see cref="RAMJobStore" /> and forwards to it, which is all a
/// store that only wants to add behaviour around the edges has to do.
/// </remarks>
public sealed class CustomJobStore : IJobStore
{
    private readonly RAMJobStore inner;
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<CustomJobStore> logger;

    public CustomJobStore(
        ILoggerFactory loggerFactory,
        ISchedulerSignaler signaler,
        TimeProvider timeProvider,
        IServiceProvider serviceProvider,
        ILogger<CustomJobStore> logger)
    {
        inner = new RAMJobStore(loggerFactory, signaler, timeProvider);
        this.serviceProvider = serviceProvider;
        this.logger = logger;
    }

    public async ValueTask Initialize(CancellationToken cancellationToken = default)
    {
        await inner.Initialize(cancellationToken);
        logger.LogInformation("CustomJobStore has been initialized, service provider is {ServiceProviderType}", serviceProvider.GetType());
    }

    public bool SupportsPersistence => inner.SupportsPersistence;

    public TimeSpan EstimatedTimeToReleaseAndAcquireTrigger => inner.EstimatedTimeToReleaseAndAcquireTrigger;

    public bool Clustered => inner.Clustered;

    public ValueTask SchedulerStarted(CancellationToken cancellationToken = default)
        => inner.SchedulerStarted(cancellationToken);

    public ValueTask SchedulerPaused(CancellationToken cancellationToken = default)
        => inner.SchedulerPaused(cancellationToken);

    public ValueTask SchedulerResumed(CancellationToken cancellationToken = default)
        => inner.SchedulerResumed(cancellationToken);

    public ValueTask Shutdown(CancellationToken cancellationToken = default)
        => inner.Shutdown(cancellationToken);

    public ValueTask ScheduleJob(IJobDetail job, IOperableTrigger trigger, CancellationToken cancellationToken = default)
        => inner.ScheduleJob(job, trigger, cancellationToken);

    public ValueTask AddJob(IJobDetail job, bool replace, CancellationToken cancellationToken = default)
        => inner.AddJob(job, replace, cancellationToken);

    public ValueTask ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, bool replace, CancellationToken cancellationToken = default)
        => inner.ScheduleJobs(triggersAndJobs, replace, cancellationToken);

    public ValueTask<bool> DeleteJob(JobKey jobKey, CancellationToken cancellationToken = default)
        => inner.DeleteJob(jobKey, cancellationToken);

    public ValueTask<bool> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
        => inner.DeleteJobs(jobKeys, cancellationToken);

    public ValueTask<IJobDetail?> GetJob(JobKey jobKey, CancellationToken cancellationToken = default)
        => inner.GetJob(jobKey, cancellationToken);

    public ValueTask AddTrigger(IOperableTrigger trigger, bool replace, CancellationToken cancellationToken = default)
        => inner.AddTrigger(trigger, replace, cancellationToken);

    public ValueTask<bool> DeleteTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        => inner.DeleteTrigger(triggerKey, cancellationToken);

    public ValueTask<bool> DeleteTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
        => inner.DeleteTriggers(triggerKeys, cancellationToken);

    public ValueTask<bool> ReplaceTrigger(TriggerKey triggerKey, IOperableTrigger trigger, CancellationToken cancellationToken = default)
        => inner.ReplaceTrigger(triggerKey, trigger, cancellationToken);

    public ValueTask<bool> UpdateTriggerDetails(TriggerKey triggerKey, TriggerDetailsUpdate update, CancellationToken cancellationToken = default)
        => inner.UpdateTriggerDetails(triggerKey, update, cancellationToken);

    public ValueTask<IOperableTrigger?> GetTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        => inner.GetTrigger(triggerKey, cancellationToken);

    public ValueTask<bool> CheckExists(JobKey jobKey, CancellationToken cancellationToken = default)
        => inner.CheckExists(jobKey, cancellationToken);

    public ValueTask<bool> CheckExists(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        => inner.CheckExists(triggerKey, cancellationToken);

    public ValueTask Clear(CancellationToken cancellationToken = default)
        => inner.Clear(cancellationToken);

    public ValueTask AddCalendar(string calendarName, ICalendar calendar, AddCalendarOptions? options = null, CancellationToken cancellationToken = default)
        => inner.AddCalendar(calendarName, calendar, options, cancellationToken);

    public ValueTask<bool> DeleteCalendar(string calendarName, CancellationToken cancellationToken = default)
        => inner.DeleteCalendar(calendarName, cancellationToken);

    public ValueTask<ICalendar?> GetCalendar(string calendarName, CancellationToken cancellationToken = default)
        => inner.GetCalendar(calendarName, cancellationToken);

    public ValueTask<PagedResult<JobHeader>> QueryJobs(JobQuery query, CancellationToken cancellationToken = default)
        => inner.QueryJobs(query, cancellationToken);

    public ValueTask<PagedResult<TriggerHeader>> QueryTriggers(TriggerQuery query, CancellationToken cancellationToken = default)
        => inner.QueryTriggers(query, cancellationToken);

    public ValueTask<PagedResult<JobGroup>> QueryJobGroups(JobGroupQuery query, CancellationToken cancellationToken = default)
        => inner.QueryJobGroups(query, cancellationToken);

    public ValueTask<PagedResult<TriggerGroup>> QueryTriggerGroups(TriggerGroupQuery query, CancellationToken cancellationToken = default)
        => inner.QueryTriggerGroups(query, cancellationToken);

    public ValueTask<PagedResult<string>> QueryCalendarNames(CalendarQuery query, CancellationToken cancellationToken = default)
        => inner.QueryCalendarNames(query, cancellationToken);

    public ValueTask<List<IJobDetail>> GetJobDetails(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
        => inner.GetJobDetails(jobKeys, cancellationToken);

    public ValueTask<List<IOperableTrigger>> GetTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
        => inner.GetTriggers(triggerKeys, cancellationToken);

    public ValueTask<List<IOperableTrigger>> GetTriggersForJob(JobKey jobKey, CancellationToken cancellationToken = default)
        => inner.GetTriggersForJob(jobKey, cancellationToken);

    public ValueTask<TriggerState> GetTriggerState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        => inner.GetTriggerState(triggerKey, cancellationToken);

    public ValueTask ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        => inner.ResetTriggerFromErrorState(triggerKey, cancellationToken);

    public ValueTask PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        => inner.PauseTrigger(triggerKey, cancellationToken);

    public ValueTask<List<string>> PauseTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
        => inner.PauseTriggers(matcher, cancellationToken);

    public ValueTask PauseJob(JobKey jobKey, CancellationToken cancellationToken = default)
        => inner.PauseJob(jobKey, cancellationToken);

    public ValueTask<List<string>> PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
        => inner.PauseJobs(matcher, cancellationToken);

    public ValueTask ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        => inner.ResumeTrigger(triggerKey, cancellationToken);

    public ValueTask<List<string>> ResumeTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
        => inner.ResumeTriggers(matcher, cancellationToken);

    public ValueTask ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default)
        => inner.ResumeJob(jobKey, cancellationToken);

    public ValueTask<List<string>> ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
        => inner.ResumeJobs(matcher, cancellationToken);

    public ValueTask PauseAll(CancellationToken cancellationToken = default)
        => inner.PauseAll(cancellationToken);

    public ValueTask ResumeAll(CancellationToken cancellationToken = default)
        => inner.ResumeAll(cancellationToken);

    public ValueTask<List<IOperableTrigger>> AcquireNextTriggers(TriggerAcquisitionRequest request, CancellationToken cancellationToken = default)
        => inner.AcquireNextTriggers(request, cancellationToken);

    public ValueTask ReleaseAcquiredTrigger(IOperableTrigger trigger, CancellationToken cancellationToken = default)
        => inner.ReleaseAcquiredTrigger(trigger, cancellationToken);

    public ValueTask<List<TriggerFiredResult>> TriggersFired(IReadOnlyCollection<IOperableTrigger> triggers, CancellationToken cancellationToken = default)
        => inner.TriggersFired(triggers, cancellationToken);

    public ValueTask TriggeredJobComplete(IOperableTrigger trigger, IJobDetail jobDetail, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default)
        => inner.TriggeredJobComplete(trigger, jobDetail, triggerInstructionCode, cancellationToken);

    public TimeSpan GetAcquireRetryDelay(int failureCount)
        => inner.GetAcquireRetryDelay(failureCount);
}
