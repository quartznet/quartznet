#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using Quartz.Extensibility;

namespace Quartz.Impl;

/// <summary>
/// An <see cref="IJobStore" /> that forwards every operation to another one, so a store of your own can
/// override the few it cares about and inherit the rest.
/// </summary>
/// <remarks>
/// <para>
/// The stores Quartz ships are sealed - <see cref="RAMJobStore" /> holds a lock while it mutates several
/// indexes in a fixed order and raises notifications after releasing it, none of which an override can be
/// asked to preserve. Decorating one is composition instead: wrap it, and change what you meant to change.
/// </para>
/// <para>
/// This is the store-level counterpart of <see cref="DelegatingScheduler" />. It suits logging, metrics,
/// tenant routing, fault injection and the like. A store that keeps scheduling data somewhere new should
/// implement <see cref="IJobStore" /> directly rather than derive from this.
/// </para>
/// </remarks>
public class DelegatingJobStore : IJobStore
{
    private readonly IJobStore jobStore;

    /// <summary>
    /// Wraps the job store this one forwards to.
    /// </summary>
    /// <param name="jobStore">The store every member is forwarded to.</param>
    public DelegatingJobStore(IJobStore jobStore)
    {
        this.jobStore = jobStore;
    }

    /// <summary>
    /// The store this one forwards to, so that code which needs the real store - rather than the
    /// behaviour a decorator adds - can reach it through however many layers are in the way.
    /// </summary>
    protected IJobStore InnerJobStore => jobStore;

    /// <summary>
    /// The same thing <see cref="InnerJobStore" /> is, reachable from outside the inheritance chain so
    /// that <see cref="JobStores.Unwrap" /> can walk a stack of decorators it did not build.
    /// </summary>
    internal IJobStore Inner => jobStore;

    /// <inheritdoc />
    public virtual bool SupportsPersistence => jobStore.SupportsPersistence;

    /// <inheritdoc />
    public virtual TimeSpan EstimatedTimeToReleaseAndAcquireTrigger => jobStore.EstimatedTimeToReleaseAndAcquireTrigger;

    /// <inheritdoc />
    public virtual bool Clustered => jobStore.Clustered;

    /// <inheritdoc />
    public virtual ValueTask Initialize(SchedulerIdentity identity, CancellationToken cancellationToken = default)
    {
        return jobStore.Initialize(identity, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask SchedulerStarted(CancellationToken cancellationToken = default)
    {
        return jobStore.SchedulerStarted(cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask SchedulerPaused(CancellationToken cancellationToken = default)
    {
        return jobStore.SchedulerPaused(cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask SchedulerResumed(CancellationToken cancellationToken = default)
    {
        return jobStore.SchedulerResumed(cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask Shutdown(CancellationToken cancellationToken = default)
    {
        return jobStore.Shutdown(cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask ScheduleJob(IJobDetail job, IOperableTrigger trigger, CancellationToken cancellationToken = default)
    {
        return jobStore.ScheduleJob(job, trigger, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask AddJob(IJobDetail job, AddJobOptions options = default, CancellationToken cancellationToken = default)
    {
        return jobStore.AddJob(job, options, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<IOperableTrigger>> triggersAndJobs, ScheduleJobOptions options = default, CancellationToken cancellationToken = default)
    {
        return jobStore.ScheduleJobs(triggersAndJobs, options, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> DeleteJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return jobStore.DeleteJob(jobKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<JobKey>> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        return jobStore.DeleteJobs(jobKeys, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<JobKey>> DeleteJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        return jobStore.DeleteJobs(matcher, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<IJobDetail?> GetJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return jobStore.GetJob(jobKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask AddTrigger(IOperableTrigger trigger, AddTriggerOptions options = default, CancellationToken cancellationToken = default)
    {
        return jobStore.AddTrigger(trigger, options, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> DeleteTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return jobStore.DeleteTrigger(triggerKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<TriggerKey>> DeleteTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        return jobStore.DeleteTriggers(triggerKeys, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<TriggerKey>> DeleteTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        return jobStore.DeleteTriggers(matcher, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> ReplaceTrigger(TriggerKey triggerKey, IOperableTrigger trigger, CancellationToken cancellationToken = default)
    {
        return jobStore.ReplaceTrigger(triggerKey, trigger, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> UpdateTriggerDetails(TriggerKey triggerKey, TriggerDetailsUpdate update, CancellationToken cancellationToken = default)
    {
        return jobStore.UpdateTriggerDetails(triggerKey, update, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<IOperableTrigger?> GetTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return jobStore.GetTrigger(triggerKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> Exists(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return jobStore.Exists(jobKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> Exists(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return jobStore.Exists(triggerKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> Exists(string calendarName, CancellationToken cancellationToken = default)
    {
        return jobStore.Exists(calendarName, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask Clear(CancellationToken cancellationToken = default)
    {
        return jobStore.Clear(cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask AddCalendar(string calendarName, ICalendar calendar, AddCalendarOptions options = default, CancellationToken cancellationToken = default)
    {
        return jobStore.AddCalendar(calendarName, calendar, options, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> DeleteCalendar(string calendarName, CancellationToken cancellationToken = default)
    {
        return jobStore.DeleteCalendar(calendarName, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<ICalendar?> GetCalendar(string calendarName, CancellationToken cancellationToken = default)
    {
        return jobStore.GetCalendar(calendarName, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<PagedResult<JobHeader>> QueryJobs(JobQuery query, CancellationToken cancellationToken = default)
    {
        return jobStore.QueryJobs(query, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<PagedResult<TriggerHeader>> QueryTriggers(TriggerQuery query, CancellationToken cancellationToken = default)
    {
        return jobStore.QueryTriggers(query, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<PagedResult<JobGroup>> QueryJobGroups(JobGroupQuery query, CancellationToken cancellationToken = default)
    {
        return jobStore.QueryJobGroups(query, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<PagedResult<TriggerGroup>> QueryTriggerGroups(TriggerGroupQuery query, CancellationToken cancellationToken = default)
    {
        return jobStore.QueryTriggerGroups(query, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<PagedResult<string>> QueryCalendarNames(CalendarQuery query, CancellationToken cancellationToken = default)
    {
        return jobStore.QueryCalendarNames(query, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<PagedResult<FireInstance>> QueryFireInstances(FireInstanceQuery query, CancellationToken cancellationToken = default)
    {
        return jobStore.QueryFireInstances(query, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<ClusterNode>> QueryClusterNodes(CancellationToken cancellationToken = default)
    {
        return jobStore.QueryClusterNodes(cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<IJobDetail>> GetJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        return jobStore.GetJobs(jobKeys, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<IOperableTrigger>> GetTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        return jobStore.GetTriggers(triggerKeys, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<IOperableTrigger>> GetTriggersForJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return jobStore.GetTriggersForJob(jobKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<TriggerState> GetTriggerState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return jobStore.GetTriggerState(triggerKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return jobStore.ResetTriggerFromErrorState(triggerKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<TriggerKey>> ResetTriggersFromErrorState(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        return jobStore.ResetTriggersFromErrorState(triggerKeys, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return jobStore.PauseTrigger(triggerKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<string>> PauseTriggerGroups(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        return jobStore.PauseTriggerGroups(matcher, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<TriggerKey>> PauseTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        return jobStore.PauseTriggers(triggerKeys, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> PauseJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return jobStore.PauseJob(jobKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<string>> PauseJobGroups(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        return jobStore.PauseJobGroups(matcher, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<JobKey>> PauseJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        return jobStore.PauseJobs(jobKeys, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return jobStore.ResumeTrigger(triggerKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<string>> ResumeTriggerGroups(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        return jobStore.ResumeTriggerGroups(matcher, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<TriggerKey>> ResumeTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        return jobStore.ResumeTriggers(triggerKeys, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<bool> ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return jobStore.ResumeJob(jobKey, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<string>> ResumeJobGroups(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        return jobStore.ResumeJobGroups(matcher, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<JobKey>> ResumeJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        return jobStore.ResumeJobs(jobKeys, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask PauseAll(CancellationToken cancellationToken = default)
    {
        return jobStore.PauseAll(cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask ResumeAll(CancellationToken cancellationToken = default)
    {
        return jobStore.ResumeAll(cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<IOperableTrigger>> AcquireNextTriggers(TriggerAcquisitionRequest request, CancellationToken cancellationToken = default)
    {
        return jobStore.AcquireNextTriggers(request, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask ReleaseAcquiredTrigger(IOperableTrigger trigger, CancellationToken cancellationToken = default)
    {
        return jobStore.ReleaseAcquiredTrigger(trigger, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask<List<TriggerFiredResult>> TriggersFired(IReadOnlyCollection<IOperableTrigger> triggers, CancellationToken cancellationToken = default)
    {
        return jobStore.TriggersFired(triggers, cancellationToken);
    }

    /// <inheritdoc />
    public virtual ValueTask TriggeredJobComplete(IOperableTrigger trigger, IJobDetail jobDetail, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default)
    {
        return jobStore.TriggeredJobComplete(trigger, jobDetail, triggerInstructionCode, cancellationToken);
    }

    /// <inheritdoc />
    public virtual TimeSpan GetAcquireRetryDelay(int failureCount)
    {
        return jobStore.GetAcquireRetryDelay(failureCount);
    }
}
