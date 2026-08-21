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
internal sealed class StdScheduler : IScheduler
{
    internal readonly QuartzScheduler scheduler;

    /// <summary>
    /// Construct a <see cref="StdScheduler" /> instance to proxy the given
    /// <see cref="QuartzScheduler" /> instance.
    /// </summary>
    public StdScheduler(QuartzScheduler scheduler)
    {
        this.scheduler = scheduler;
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
            Started = IsStarted,
            InStandbyMode = InStandbyMode,
            Shutdown = IsShutdown,
            RunningSince = scheduler.RunningSince,
            JobsExecuted = scheduler.NumberOfJobsExecuted,
            JobStoreTypeName = scheduler.JobStoreType.AssemblyQualifiedNameWithoutVersion(),
            JobStorePersistent = scheduler.SupportsPersistence,
            JobStoreClustered = scheduler.Clustered,
            ThreadPoolTypeName = scheduler.ThreadPoolType.AssemblyQualifiedNameWithoutVersion(),
            ThreadPoolSize = scheduler.ThreadPoolSize,
            Version = scheduler.Version,
        });
    }

    /// <summary>
    /// Returns the <see cref="SchedulerContext" /> of the <see cref="IScheduler" />.
    /// </summary>
    public SchedulerContext Context => scheduler.SchedulerContext;

    /// <summary>
    /// Whether the scheduler has been started.
    /// </summary>
    /// <value></value>
    /// <remarks>
    /// Note: This only reflects whether <see cref="Start"/> has ever
    /// been called on this Scheduler, so it will return <see langword="true" /> even
    /// if the <see cref="IScheduler" /> is currently in standby mode or has been
    /// since shutdown.
    /// </remarks>
    /// <seealso cref="Start"/>
    /// <seealso cref="IsShutdown"/>
    /// <seealso cref="InStandbyMode"/>
    public bool IsStarted => scheduler.RunningSince.HasValue;

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public bool InStandbyMode => scheduler.InStandbyMode;

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public bool IsShutdown => scheduler.IsShutdown;

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<List<IJobExecutionContext>> GetCurrentlyExecutingJobs(CancellationToken cancellationToken = default)
    {
        return new ValueTask<List<IJobExecutionContext>>(scheduler.GetCurrentlyExecutingJobs());
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask Clear(CancellationToken cancellationToken = default)
    {
        return scheduler.Clear(cancellationToken);
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
        CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJob(jobDetail, trigger, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<DateTimeOffset> ScheduleJob(
        ITrigger trigger,
        CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJob(trigger, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask AddJob(
        IJobDetail jobDetail,
        AddJobOptions options = default,
        CancellationToken cancellationToken = default)
    {
        return scheduler.AddJob(jobDetail, options, cancellationToken);
    }

    public ValueTask<bool> DeleteJobs(
        IReadOnlyCollection<JobKey> jobKeys,
        CancellationToken cancellationToken = default)
    {
        return scheduler.DeleteJobs(jobKeys, cancellationToken);
    }

    public ValueTask ScheduleJobs(
        IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs,
        ScheduleJobOptions options = default,
        CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJobs(triggersAndJobs, options, cancellationToken);
    }

    public ValueTask ScheduleJob(
        IJobDetail jobDetail,
        IReadOnlyCollection<ITrigger> triggersForJob,
        ScheduleJobOptions options = default,
        CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJob(jobDetail, triggersForJob, options, cancellationToken);
    }

    public ValueTask<bool> UnscheduleJobs(
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        return scheduler.UnscheduleJobs(triggerKeys, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<bool> DeleteJob(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.DeleteJob(jobKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<bool> UnscheduleJob(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.UnscheduleJob(triggerKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<DateTimeOffset?> RescheduleJob(
        TriggerKey triggerKey,
        ITrigger newTrigger,
        CancellationToken cancellationToken = default)
    {
        return scheduler.RescheduleJob(triggerKey, newTrigger, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<bool> UpdateTriggerDetails(
        TriggerKey triggerKey,
        TriggerDetailsUpdate update,
        CancellationToken cancellationToken = default)
    {
        return scheduler.UpdateTriggerDetails(triggerKey, update, cancellationToken);
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
        return scheduler.TriggerJob(jobKey, data, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<bool> Exists(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.Exists(jobKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<bool> Exists(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.Exists(triggerKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<bool> PauseTrigger(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.PauseTrigger(triggerKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<List<string>> PauseTriggers(
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        return scheduler.PauseTriggers(matcher, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<bool> PauseJob(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.PauseJob(jobKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<List<string>> PauseJobs(
        GroupMatcher<JobKey> matcher,
        CancellationToken cancellationToken = default)
    {
        return scheduler.PauseJobs(matcher, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<bool> ResumeTrigger(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeTrigger(triggerKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<List<string>> ResumeTriggers(
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeTriggers(matcher, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<bool> ResumeJob(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeJob(jobKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<List<string>> ResumeJobs(
        GroupMatcher<JobKey> matcher,
        CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeJobs(matcher, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask PauseAll(CancellationToken cancellationToken = default)
    {
        return scheduler.PauseAll(cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask ResumeAll(CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeAll(cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<PagedResult<JobHeader>> QueryJobs(JobQuery query, CancellationToken cancellationToken = default)
    {
        return scheduler.QueryJobs(query, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<PagedResult<TriggerHeader>> QueryTriggers(TriggerQuery query, CancellationToken cancellationToken = default)
    {
        return scheduler.QueryTriggers(query, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<PagedResult<JobGroup>> QueryJobGroups(JobGroupQuery query, CancellationToken cancellationToken = default)
    {
        return scheduler.QueryJobGroups(query, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<PagedResult<TriggerGroup>> QueryTriggerGroups(TriggerGroupQuery query, CancellationToken cancellationToken = default)
    {
        return scheduler.QueryTriggerGroups(query, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<PagedResult<string>> QueryCalendarNames(CalendarQuery query, CancellationToken cancellationToken = default)
    {
        return scheduler.QueryCalendarNames(query, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<List<IJobDetail>> GetJobDetails(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.GetJobDetails(jobKeys, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<List<ITrigger>> GetTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        return scheduler.GetTriggers(triggerKeys, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<IJobDetail?> GetJobDetail(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.GetJobDetail(jobKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<ITrigger?> GetTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.GetTrigger(triggerKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<TriggerState> GetTriggerState(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.GetTriggerState(triggerKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<bool> ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.ResetTriggerFromErrorState(triggerKey, cancellationToken);
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
        return scheduler.AddCalendar(calendarName, calendar, options, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<bool> DeleteCalendar(
        string calendarName,
        CancellationToken cancellationToken = default)
    {
        return scheduler.DeleteCalendar(calendarName, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<ICalendar?> GetCalendar(string calendarName, CancellationToken cancellationToken = default)
    {
        return scheduler.GetCalendar(calendarName, cancellationToken);
    }

    /// <summary>
    /// Request the interruption, within this Scheduler instance, of all
    /// currently executing instances of the identified <see cref="IJob" />.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If more than one instance of the identified job is currently executing,
    /// the cancellation token will be set on each instance.
    /// However, there is a limitation that in the case that
    /// <see cref="Interrupt(JobKey, CancellationToken)"/> on one instances throws an exception, all
    /// remaining  instances (that have not yet been interrupted) will not have
    /// their <see cref="Interrupt(JobKey, CancellationToken)"/> method called.
    /// </para>
    /// <para>
    /// If you wish to interrupt a specific instance of a job (when more than
    /// one is executing) you can do so by calling
    /// <see cref="GetCurrentlyExecutingJobs"/> to obtain a handle
    /// to the job instance, and then invoke <see cref="Interrupt(JobKey, CancellationToken)"/> on it
    /// yourself.
    /// </para>
    /// <para>
    /// This method is not cluster aware.  That is, it will only interrupt
    /// instances of the identified InterruptableJob currently executing in this
    /// Scheduler instance, not across the entire cluster.
    /// </para>
    /// </remarks>
    /// <returns>true is at least one instance of the identified job was found and interrupted.</returns>
    /// <seealso cref="GetCurrentlyExecutingJobs"/>
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
