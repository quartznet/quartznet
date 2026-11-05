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

using Quartz.Core;
using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Util;

namespace Quartz.Impl;

/// <summary>
/// An implementation of the <see cref="IScheduler" /> interface that directly
/// proxies all method calls to the equivalent call on a given <see cref="QuartzScheduler" />
/// instance.
/// </summary>
/// <seealso cref="IScheduler" />
/// <seealso cref="QuartzScheduler" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
internal sealed partial class StdScheduler : IScheduler, ISharedExecutionGroupSource
{
    internal readonly QuartzScheduler scheduler;

    /// <summary>
    /// Construct a <see cref="StdScheduler" /> instance to proxy the given
    /// <see cref="QuartzScheduler" /> instance.
    /// </summary>
    public StdScheduler(QuartzScheduler scheduler)
    {
        this.scheduler = scheduler;

        // Read once, here, so a scheduler with nothing to consult pays one null check per call. Through
        // the decorators every store is wrapped in, as with every question about what the store is.
        enlistingStore = JobStores.Unwrap(scheduler.resources.JobStore) as AdoJobStoreBase;
        enlistmentSource = enlistingStore?.EnlistmentSource;
    }

    /// <summary>
    /// Returns the name of the <see cref="IScheduler" />.
    /// </summary>
    public string SchedulerName => scheduler.SchedulerName;

    /// <summary>
    /// Returns the instance Id of the <see cref="IScheduler" />.
    /// </summary>
    public string SchedulerInstanceId => scheduler.SchedulerInstanceId;

    /// <summary>
    /// The proxied <see cref="QuartzScheduler" />'s instance Id, which is already in this process.
    /// </summary>
    public ValueTask<string> GetSchedulerInstanceId(CancellationToken cancellationToken = default)
    {
        return new ValueTask<string>(scheduler.SchedulerInstanceId);
    }

    /// <summary>
    /// The clock the proxied <see cref="QuartzScheduler" /> was built with.
    /// </summary>
    public TimeProvider TimeProvider => scheduler.resources.TimeProvider;

    /// <summary>
    /// Get a <see cref="SchedulerMetadata"/> object describing the settings
    /// and capabilities of the scheduler instance.
    /// <para>
    /// Note that the data returned is an 'instantaneous' snapshot, and that as
    /// soon as it's returned, the metadata values may be different.
    /// </para>
    /// </summary>
    public ValueTask<SchedulerMetadata> GetMetadata(CancellationToken cancellationToken = default)
    {
        return new ValueTask<SchedulerMetadata>(new SchedulerMetadata
        {
            SchedulerName = SchedulerName,
            SchedulerInstanceId = SchedulerInstanceId,
            SchedulerTypeName = GetType().AssemblyQualifiedNameWithoutVersion(),
            IsProxy = false,
            Status = Status,
            RunningSince = scheduler.RunningSince,
            JobsExecuted = scheduler.NumberOfJobsExecuted,
            LocalExecutingJobs = scheduler.NumberOfJobsExecutingHere,
            JobStoreTypeName = scheduler.JobStoreType.AssemblyQualifiedNameWithoutVersion(),
            JobStorePersistent = scheduler.SupportsPersistence,
            JobStoreClustered = scheduler.Clustered,
            ThreadPoolTypeName = scheduler.ThreadPoolType.AssemblyQualifiedNameWithoutVersion(),
            ThreadPoolSize = scheduler.ThreadPoolSize,
            Version = scheduler.Version,
            ServedExecutionGroups = scheduler.ExecutionGroupRouting is { IsServeList: true } served ? served.Spellings : [],
            RefusedExecutionGroups = scheduler.ExecutionGroupRouting is { IsServeList: false } refused ? refused.Spellings : [],
        });
    }

    /// <summary>
    /// Returns the <see cref="SchedulerContext" /> of the <see cref="IScheduler" />.
    /// </summary>
    public SchedulerContext Context => scheduler.SchedulerContext;

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public SchedulerStatus Status => scheduler.Status;

    /// <summary>
    /// Reads the same field of the proxied <see cref="QuartzScheduler" /> that <see cref="Status" />
    /// does, since nothing has to be waited for to answer here.
    /// </summary>
    public ValueTask<SchedulerStatus> GetStatus(CancellationToken cancellationToken = default)
    {
        return new ValueTask<SchedulerStatus>(scheduler.Status);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<PagedResult<FireInstance>> QueryFireInstances(FireInstanceQuery query, CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.QueryFireInstances(query, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.QueryFireInstances(state, ct), query, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<List<ClusterNode>> QueryClusterNodes(CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.QueryClusterNodes(cancellationToken)
            : WithEnlistment(static (qs, _, ct) => qs.QueryClusterNodes(ct), default(NoState), cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask Clear(CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.Clear(cancellationToken)
            : WithEnlistment(static (qs, _, ct) => qs.Clear(ct), default(NoState), cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public IListenerManager ListenerManager => scheduler.ListenerManager;

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask Start(CancellationToken cancellationToken = default)
    {
        return scheduler.Start(cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask StartDelayed(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        return scheduler.StartDelayed(delay, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask Standby(CancellationToken cancellationToken = default)
    {
        return scheduler.Standby(cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask Shutdown(
        bool waitForJobsToComplete = false,
        CancellationToken cancellationToken = default)
    {
        return scheduler.Shutdown(waitForJobsToComplete, cancellationToken);
    }

    /// <summary>
    /// Shuts the scheduler down without waiting for running jobs, which is what a local scheduler owns.
    /// </summary>
    /// <remarks>
    /// <see cref="QuartzScheduler.Shutdown"/> returns immediately once a shutdown has been started, so
    /// disposing twice — or disposing after an explicit shutdown, waiting or not — does nothing.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        return scheduler.Shutdown(waitForJobsToComplete: false, CancellationToken.None);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<DateTimeOffset> ScheduleJob(
        IJobDetail jobDetail,
        ITrigger trigger,
        ScheduleJobOptions options = default,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.ScheduleJob(jobDetail, trigger, options, cancellationToken)
            : WithEnlistment(static (qs, s, ct) => qs.ScheduleJob(s.jobDetail, s.trigger, s.options, ct), (jobDetail, trigger, options), cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<DateTimeOffset> ScheduleJob(
        ITrigger trigger,
        ScheduleJobOptions options = default,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.ScheduleJob(trigger, options, cancellationToken)
            : WithEnlistment(static (qs, s, ct) => qs.ScheduleJob(s.trigger, s.options, ct), (trigger, options), cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<ScheduleTriggerResult> ScheduleTrigger(
        ITrigger trigger,
        TriggerConflict onConflict,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.ScheduleTrigger(trigger, onConflict, cancellationToken)
            : WithEnlistment(static (qs, s, ct) => qs.ScheduleTrigger(s.trigger, s.onConflict, ct), (trigger, onConflict), cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask AddJob(
        IJobDetail jobDetail,
        AddJobOptions options = default,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.AddJob(jobDetail, options, cancellationToken)
            : WithEnlistment(static (qs, s, ct) => qs.AddJob(s.jobDetail, s.options, ct), (jobDetail, options), cancellationToken);
    }

    public ValueTask<List<JobKey>> DeleteJobs(
        IReadOnlyCollection<JobKey> jobKeys,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.DeleteJobs(jobKeys, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.DeleteJobs(state, ct), jobKeys, cancellationToken);
    }

    public ValueTask<List<JobKey>> DeleteJobs(
        GroupMatcher<JobKey> matcher,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.DeleteJobs(matcher, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.DeleteJobs(state, ct), matcher, cancellationToken);
    }

    public ValueTask ScheduleJobs(
        IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs,
        ScheduleJobOptions options = default,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.ScheduleJobs(triggersAndJobs, options, cancellationToken)
            : WithEnlistment(static (qs, s, ct) => qs.ScheduleJobs(s.triggersAndJobs, s.options, ct), (triggersAndJobs, options), cancellationToken);
    }

    public ValueTask ScheduleJob(
        IJobDetail jobDetail,
        IReadOnlyCollection<ITrigger> triggersForJob,
        ScheduleJobOptions options = default,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.ScheduleJob(jobDetail, triggersForJob, options, cancellationToken)
            : WithEnlistment(static (qs, s, ct) => qs.ScheduleJob(s.jobDetail, s.triggersForJob, s.options, ct), (jobDetail, triggersForJob, options), cancellationToken);
    }

    public ValueTask<List<TriggerKey>> UnscheduleJobs(
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.UnscheduleJobs(triggerKeys, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.UnscheduleJobs(state, ct), triggerKeys, cancellationToken);
    }

    public ValueTask<List<TriggerKey>> UnscheduleJobs(
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.UnscheduleJobs(matcher, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.UnscheduleJobs(state, ct), matcher, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<bool> DeleteJob(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.DeleteJob(jobKey, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.DeleteJob(state, ct), jobKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<bool> UnscheduleJob(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.UnscheduleJob(triggerKey, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.UnscheduleJob(state, ct), triggerKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<DateTimeOffset?> RescheduleJob(
        TriggerKey triggerKey,
        ITrigger newTrigger,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.RescheduleJob(triggerKey, newTrigger, cancellationToken)
            : WithEnlistment(static (qs, s, ct) => qs.RescheduleJob(s.triggerKey, s.newTrigger, ct), (triggerKey, newTrigger), cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<bool> UpdateTriggerDetails(
        TriggerKey triggerKey,
        TriggerDetailsUpdate update,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.UpdateTriggerDetails(triggerKey, update, cancellationToken)
            : WithEnlistment(static (qs, s, ct) => qs.UpdateTriggerDetails(s.triggerKey, s.update, ct), (triggerKey, update), cancellationToken);
    }

    /// <summary>
    /// Sets the execution group limits for this scheduler node.
    /// </summary>
    public ValueTask SetExecutionLimits(ExecutionLimits? limits, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        scheduler.SetExecutionLimits(limits);
        return default;
    }

    /// <summary>
    /// Gets the currently configured execution group limits.
    /// </summary>
    public ValueTask<ExecutionLimits?> GetExecutionLimits(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<ExecutionLimits?>(scheduler.GetExecutionLimits());
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask TriggerJob(
        JobKey jobKey,
        JobDataMap? data = null,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.TriggerJob(jobKey, data, cancellationToken)
            : WithEnlistment(static (qs, s, ct) => qs.TriggerJob(s.jobKey, s.data, ct), (jobKey, data), cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<bool> Exists(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.Exists(jobKey, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.Exists(state, ct), jobKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<bool> Exists(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.Exists(triggerKey, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.Exists(state, ct), triggerKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<bool> Exists(
        string calendarName,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.Exists(calendarName, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.Exists(state, ct), calendarName, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<bool> PauseTrigger(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.PauseTrigger(triggerKey, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.PauseTrigger(state, ct), triggerKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<List<string>> PauseTriggerGroups(
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.PauseTriggerGroups(matcher, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.PauseTriggerGroups(state, ct), matcher, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<bool> PauseTriggerWith(TriggerKey triggerKey, PauseDetails? details, CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.PauseTriggerWith(triggerKey, details, cancellationToken)
            : WithEnlistment(static (qs, s, ct) => qs.PauseTriggerWith(s.triggerKey, s.details, ct), (triggerKey, details), cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<bool> PauseJobWith(JobKey jobKey, PauseDetails? details, CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.PauseJobWith(jobKey, details, cancellationToken)
            : WithEnlistment(static (qs, s, ct) => qs.PauseJobWith(s.jobKey, s.details, ct), (jobKey, details), cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<List<string>> PauseTriggerGroupsWith(GroupMatcher<TriggerKey> matcher, PauseDetails? details, CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.PauseTriggerGroupsWith(matcher, details, cancellationToken)
            : WithEnlistment(static (qs, s, ct) => qs.PauseTriggerGroupsWith(s.matcher, s.details, ct), (matcher, details), cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<List<string>> PauseJobGroupsWith(GroupMatcher<JobKey> matcher, PauseDetails? details, CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.PauseJobGroupsWith(matcher, details, cancellationToken)
            : WithEnlistment(static (qs, s, ct) => qs.PauseJobGroupsWith(s.matcher, s.details, ct), (matcher, details), cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask PauseAllWith(PauseDetails? details, CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.PauseAllWith(details, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.PauseAllWith(state, ct), details, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<List<TriggerKey>> PauseTriggersWith(IReadOnlyCollection<TriggerKey> triggerKeys, PauseDetails? details, CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.PauseTriggersWith(triggerKeys, details, cancellationToken)
            : WithEnlistment(static (qs, s, ct) => qs.PauseTriggersWith(s.triggerKeys, s.details, ct), (triggerKeys, details), cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<List<JobKey>> PauseJobsWith(IReadOnlyCollection<JobKey> jobKeys, PauseDetails? details, CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.PauseJobsWith(jobKeys, details, cancellationToken)
            : WithEnlistment(static (qs, s, ct) => qs.PauseJobsWith(s.jobKeys, s.details, ct), (jobKeys, details), cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<PauseInfo?> GetTriggerPause(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.GetTriggerPause(triggerKey, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.GetTriggerPause(state, ct), triggerKey, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<PauseInfo?> GetTriggerGroupPause(string groupName, CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.GetTriggerGroupPause(groupName, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.GetTriggerGroupPause(state, ct), groupName, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<PauseInfo?> GetJobGroupPause(string groupName, CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.GetJobGroupPause(groupName, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.GetJobGroupPause(state, ct), groupName, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<List<TriggerKey>> PauseTriggers(
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.PauseTriggers(triggerKeys, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.PauseTriggers(state, ct), triggerKeys, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<bool> PauseJob(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.PauseJob(jobKey, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.PauseJob(state, ct), jobKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<List<string>> PauseJobGroups(
        GroupMatcher<JobKey> matcher,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.PauseJobGroups(matcher, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.PauseJobGroups(state, ct), matcher, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<List<JobKey>> PauseJobs(
        IReadOnlyCollection<JobKey> jobKeys,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.PauseJobs(jobKeys, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.PauseJobs(state, ct), jobKeys, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<bool> ResumeTrigger(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.ResumeTrigger(triggerKey, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.ResumeTrigger(state, ct), triggerKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<List<string>> ResumeTriggerGroups(
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.ResumeTriggerGroups(matcher, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.ResumeTriggerGroups(state, ct), matcher, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<List<TriggerKey>> ResumeTriggers(
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.ResumeTriggers(triggerKeys, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.ResumeTriggers(state, ct), triggerKeys, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<bool> ResumeJob(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.ResumeJob(jobKey, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.ResumeJob(state, ct), jobKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<List<string>> ResumeJobGroups(
        GroupMatcher<JobKey> matcher,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.ResumeJobGroups(matcher, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.ResumeJobGroups(state, ct), matcher, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<List<JobKey>> ResumeJobs(
        IReadOnlyCollection<JobKey> jobKeys,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.ResumeJobs(jobKeys, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.ResumeJobs(state, ct), jobKeys, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask PauseAll(CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.PauseAll(cancellationToken)
            : WithEnlistment(static (qs, _, ct) => qs.PauseAll(ct), default(NoState), cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask ResumeAll(CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.ResumeAll(cancellationToken)
            : WithEnlistment(static (qs, _, ct) => qs.ResumeAll(ct), default(NoState), cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<PagedResult<JobHeader>> QueryJobs(JobQuery query, CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.QueryJobs(query, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.QueryJobs(state, ct), query, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<PagedResult<TriggerHeader>> QueryTriggers(TriggerQuery query, CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.QueryTriggers(query, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.QueryTriggers(state, ct), query, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />, which asks its store.
    /// </summary>
    ValueTask<string?> ISharedExecutionGroupSource.GetSharedExecutionGroup(JobKey jobKey, CancellationToken cancellationToken)
    {
        return enlistmentSource is null
            ? scheduler.GetSharedExecutionGroup(jobKey, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.GetSharedExecutionGroup(state, ct), jobKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<PagedResult<JobGroup>> QueryJobGroups(JobGroupQuery query, CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.QueryJobGroups(query, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.QueryJobGroups(state, ct), query, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<PagedResult<TriggerGroup>> QueryTriggerGroups(TriggerGroupQuery query, CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.QueryTriggerGroups(query, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.QueryTriggerGroups(state, ct), query, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<PagedResult<string>> QueryCalendarNames(CalendarQuery query, CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.QueryCalendarNames(query, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.QueryCalendarNames(state, ct), query, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<List<IJobDetail>> GetJobDetails(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.GetJobDetails(jobKeys, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.GetJobDetails(state, ct), jobKeys, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<List<ITrigger>> GetTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.GetTriggers(triggerKeys, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.GetTriggers(state, ct), triggerKeys, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<IJobDetail?> GetJobDetail(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.GetJobDetail(jobKey, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.GetJobDetail(state, ct), jobKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<ITrigger?> GetTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.GetTrigger(triggerKey, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.GetTrigger(state, ct), triggerKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<TriggerState> GetTriggerState(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.GetTriggerState(triggerKey, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.GetTriggerState(state, ct), triggerKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<bool> ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.ResetTriggerFromErrorState(triggerKey, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.ResetTriggerFromErrorState(state, ct), triggerKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<List<TriggerKey>> ResetTriggersFromErrorState(
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.ResetTriggersFromErrorState(triggerKeys, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.ResetTriggersFromErrorState(state, ct), triggerKeys, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask AddCalendar(
        string calendarName,
        ICalendar calendar,
        AddCalendarOptions options = default,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.AddCalendar(calendarName, calendar, options, cancellationToken)
            : WithEnlistment(static (qs, s, ct) => qs.AddCalendar(s.calendarName, s.calendar, s.options, ct), (calendarName, calendar, options), cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<bool> DeleteCalendar(
        string calendarName,
        CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.DeleteCalendar(calendarName, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.DeleteCalendar(state, ct), calendarName, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<ICalendar?> GetCalendar(string calendarName, CancellationToken cancellationToken = default)
    {
        return enlistmentSource is null
            ? scheduler.GetCalendar(calendarName, cancellationToken)
            : WithEnlistment(static (qs, state, ct) => qs.GetCalendar(state, ct), calendarName, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<bool> Interrupt(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.Interrupt(jobKey, cancellationToken);
    }

    public ValueTask<bool> InterruptFireInstance(
        string fireInstanceId,
        CancellationToken cancellationToken = default)
    {
        return scheduler.InterruptFireInstance(fireInstanceId, cancellationToken);
    }
}
