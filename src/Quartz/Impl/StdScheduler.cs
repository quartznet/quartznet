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
using Quartz.Impl.Matchers;
using Quartz.Extensibility;

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
internal class StdScheduler : IScheduler
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
    /// returns true if the given JobGroup
    /// is paused
    /// </summary>
    public ValueTask<bool> IsJobGroupPaused(
        string groupName,
        CancellationToken cancellationToken = default)
    {
        return scheduler.IsJobGroupPaused(groupName, cancellationToken);
    }

    /// <summary>
    /// returns true if the given TriggerGroup
    /// is paused
    /// </summary>
    public ValueTask<bool> IsTriggerGroupPaused(
        string groupName,
        CancellationToken cancellationToken = default)
    {
        return scheduler.IsTriggerGroupPaused(groupName, cancellationToken);
    }

    /// <summary>
    /// Returns the name of the <see cref="IScheduler" />.
    /// </summary>
    public virtual string SchedulerName => scheduler.SchedulerName;

    /// <summary>
    /// Returns the instance Id of the <see cref="IScheduler" />.
    /// </summary>
    public virtual string SchedulerInstanceId => scheduler.SchedulerInstanceId;

    /// <summary>
    /// Get a <see cref="SchedulerMetaData"/> object describing the settings
    /// and capabilities of the scheduler instance.
    /// <para>
    /// Note that the data returned is an 'instantaneous' snapshot, and that as
    /// soon as it's returned, the metadata values may be different.
    /// </para>
    /// </summary>
    /// <returns></returns>
    public ValueTask<SchedulerMetaData> GetMetaData(CancellationToken cancellationToken = default)
    {
        return new ValueTask<SchedulerMetaData>(new SchedulerMetaData(
            SchedulerName,
            SchedulerInstanceId,
            GetType(),
            false,
            IsStarted,
            InStandbyMode,
            IsShutdown,
            scheduler.RunningSince,
            scheduler.NumJobsExecuted,
            scheduler.JobStoreClass,
            scheduler.SupportsPersistence,
            scheduler.Clustered,
            scheduler.ThreadPoolClass,
            scheduler.ThreadPoolSize,
            scheduler.Version));
    }

    /// <summary>
    /// Returns the <see cref="SchedulerContext" /> of the <see cref="IScheduler" />.
    /// </summary>
    public virtual SchedulerContext Context => scheduler.SchedulerContext;

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
    public virtual bool InStandbyMode => scheduler.InStandbyMode;

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual bool IsShutdown => scheduler.IsShutdown;

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
    public ValueTask<List<string>> GetPausedTriggerGroups(CancellationToken cancellationToken = default)
    {
        return scheduler.GetPausedTriggerGroups(cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public IListenerManager ListenerManager => scheduler.ListenerManager;

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask<List<string>> GetJobGroupNames(CancellationToken cancellationToken = default)
    {
        return scheduler.GetJobGroupNames(cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask<List<string>> GetTriggerGroupNames(CancellationToken cancellationToken = default)
    {
        return scheduler.GetTriggerGroupNames(cancellationToken);
    }

    /// <seealso cref="IScheduler.JobFactory">
    /// </seealso>
    public virtual IJobFactory JobFactory
    {
        set => scheduler.JobFactory = value;
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask Start(CancellationToken cancellationToken = default)
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
    public virtual ValueTask Standby(CancellationToken cancellationToken = default)
    {
        return scheduler.Standby(cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask Shutdown(CancellationToken cancellationToken = default)
    {
        return scheduler.Shutdown(cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask Shutdown(
        bool waitForJobsToComplete,
        CancellationToken cancellationToken = default)
    {
        return scheduler.Shutdown(waitForJobsToComplete, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask<DateTimeOffset> ScheduleJob(
        IJobDetail jobDetail,
        ITrigger trigger,
        CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJob(jobDetail, trigger, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask<DateTimeOffset> ScheduleJob(
        ITrigger trigger,
        CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJob(trigger, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask AddJob(
        IJobDetail jobDetail,
        bool replace,
        bool storeNonDurableWhileAwaitingScheduling,
        CancellationToken cancellationToken = default)
    {
        return scheduler.AddJob(jobDetail, replace, storeNonDurableWhileAwaitingScheduling, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask AddJob(
        IJobDetail jobDetail,
        bool replace,
        CancellationToken cancellationToken = default)
    {
        return scheduler.AddJob(jobDetail, replace, cancellationToken);
    }

    public ValueTask<bool> DeleteJobs(
        IReadOnlyCollection<JobKey> jobKeys,
        CancellationToken cancellationToken = default)
    {
        return scheduler.DeleteJobs(jobKeys, cancellationToken);
    }

    public ValueTask ScheduleJobs(
        IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs,
        bool replace,
        CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJobs(triggersAndJobs, replace, cancellationToken);
    }

    public ValueTask ScheduleJob(
        IJobDetail jobDetail,
        IReadOnlyCollection<ITrigger> triggersForJob,
        bool replace,
        CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleJob(jobDetail, triggersForJob, replace, cancellationToken);
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
    public virtual ValueTask<bool> DeleteJob(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.DeleteJob(jobKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask<bool> UnscheduleJob(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.UnscheduleJob(triggerKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask<DateTimeOffset?> RescheduleJob(
        TriggerKey triggerKey,
        ITrigger newTrigger,
        CancellationToken cancellationToken = default)
    {
        return scheduler.RescheduleJob(triggerKey, newTrigger, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask<bool> UpdateTriggerDetails(
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
        return new ValueTask<ExecutionLimits?>(scheduler.GetExecutionLimits()?.Snapshot());
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask TriggerJob(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return TriggerJob(jobKey, null, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask TriggerJob(
        JobKey jobKey,
        JobDataMap? data,
        CancellationToken cancellationToken = default)
    {
        return scheduler.TriggerJob(jobKey, data, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<bool> CheckExists(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.CheckExists(jobKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public ValueTask<bool> CheckExists(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.CheckExists(triggerKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask PauseTrigger(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.PauseTrigger(triggerKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask PauseTriggers(
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        return scheduler.PauseTriggers(matcher, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask PauseJob(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.PauseJob(jobKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask PauseJobs(
        GroupMatcher<JobKey> matcher,
        CancellationToken cancellationToken = default)
    {
        return scheduler.PauseJobs(matcher, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask ResumeTrigger(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeTrigger(triggerKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask ResumeTriggers(
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeTriggers(matcher, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask ResumeJob(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeJob(jobKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask ResumeJobs(
        GroupMatcher<JobKey> matcher,
        CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeJobs(matcher, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask PauseAll(CancellationToken cancellationToken = default)
    {
        return scheduler.PauseAll(cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask ResumeAll(CancellationToken cancellationToken = default)
    {
        return scheduler.ResumeAll(cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask<List<ITrigger>> GetTriggersOfJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return scheduler.GetTriggersOfJob(jobKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask<List<JobKey>> GetJobKeys(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.GetJobKeys(matcher, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask<List<TriggerKey>> GetTriggerKeys(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        return scheduler.GetTriggerKeys(matcher, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask<IJobDetail?> GetJobDetail(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.GetJobDetail(jobKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask<ITrigger?> GetTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return scheduler.GetTrigger(triggerKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask<TriggerState> GetTriggerState(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.GetTriggerState(triggerKey, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public async ValueTask ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        await scheduler.ResetTriggerFromErrorState(triggerKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask AddCalendar(
        string name,
        ICalendar calendar,
        bool replace,
        bool updateTriggers,
        CancellationToken cancellationToken = default)
    {
        return scheduler.AddCalendar(name, calendar, replace, updateTriggers, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask<bool> DeleteCalendar(
        string name,
        CancellationToken cancellationToken = default)
    {
        return scheduler.DeleteCalendar(name, cancellationToken);
    }

    /// <summary>
    /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
    /// </summary>
    public virtual ValueTask<ICalendar?> GetCalendar(string name, CancellationToken cancellationToken = default)
    {
        return scheduler.GetCalendar(name, cancellationToken);
    }

    /// <summary>
    /// Get the names of all registered <see cref="ICalendar"/>.
    /// </summary>
    /// <returns></returns>
    public ValueTask<List<string>> GetCalendarNames(CancellationToken cancellationToken = default)
    {
        return scheduler.GetCalendarNames(cancellationToken);
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
    /// <throws>  UnableToInterruptJobException if the job does not implement </throws>
    /// <seealso cref="GetCurrentlyExecutingJobs"/>
    public virtual ValueTask<bool> Interrupt(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return scheduler.Interrupt(jobKey, cancellationToken);
    }

    public ValueTask<bool> Interrupt(
        string fireInstanceId,
        CancellationToken cancellationToken = default)
    {
        return scheduler.Interrupt(fireInstanceId, cancellationToken);
    }
}