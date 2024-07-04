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

using Quartz.Impl.Matchers;
using Quartz.Spi;

namespace Quartz;

/// <summary>
/// This is the main interface of a Quartz Scheduler.
/// </summary>
/// <remarks>
/// 	<para>
///         A <see cref="IScheduler"/> maintains a registry of
///         <see cref="IJobDetail"/>s and <see cref="ITrigger"/>s. Once
///         registered, the <see cref="IScheduler"/> is responsible for executing
///         <see cref="IJob"/> s when their associated <see cref="ITrigger"/> s
///         fire (when their scheduled time arrives).
///     </para>
/// 	<para>
/// 		<see cref="IScheduler"/> instances are produced by a
///         <see cref="ISchedulerFactory"/>. A scheduler that has already been
///         created/initialized can be found and used through the same factory that
///         produced it. After a <see cref="IScheduler"/> has been created, it is in
///         "stand-by" mode, and must have its <see cref="IScheduler.Start"/> method
///         called before it will fire any <see cref="IJob"/>s.
///     </para>
/// 	<para>
/// 		<see cref="IJob"/> s are to be created by the 'client program', by
///         defining a class that implements the <see cref="IJob"/> interface.
///         <see cref="IJobDetail"/> objects are then created (also by the client) to
///         define a individual instances of the <see cref="IJob"/>.
///         <see cref="IJobDetail"/> instances can then be registered with the
///         <see cref="IScheduler"/> via the %IScheduler.ScheduleJob(JobDetail,
///         Trigger)% or %IScheduler.AddJob(JobDetail, bool)% method.
///     </para>
/// 	<para>
/// 		<see cref="ITrigger"/> s can then be defined to fire individual
///         <see cref="IJob"/> instances based on given schedules.
///         <see cref="ISimpleTrigger"/> s are most useful for one-time firings, or
///         firing at an exact moment in time, with N repeats with a given delay between
///         them. <see cref="ICronTrigger"/> s allow scheduling based on time of day,
///         day of week, day of month, and month of year.
///     </para>
/// 	<para>
/// 		<see cref="IJob"/> s and <see cref="ITrigger"/> s have a name and
///         group associated with them, which should uniquely identify them within a single
///         <see cref="IScheduler"/>. The 'group' feature may be useful for creating
///         logical groupings or categorizations of <see cref="IJob"/>s and
///         <see cref="ITrigger"/>s. If you don't have need for assigning a group to a
///         given <see cref="IJob"/>s of <see cref="ITrigger"/>s, then you can use
///         the <see cref="SchedulerConstants.DefaultGroup"/> constant defined on
///         this interface.
///     </para>
/// 	<para>
///         Stored <see cref="IJob"/> s can also be 'manually' triggered through the
///         use of the %IScheduler.TriggerJob(string, string)% function.
///     </para>
/// 	<para>
///         Client programs may also be interested in the 'listener' interfaces that are
///         available from Quartz. The <see cref="IJobListener"/> interface provides
///         notifications of <see cref="IJob"/> executions. The
///         <see cref="ITriggerListener"/> interface provides notifications of
///         <see cref="ITrigger"/> firings. The <see cref="ISchedulerListener"/>
///         interface provides notifications of <see cref="IScheduler"/> events and
///         errors.  Listeners can be associated with local schedulers through the
///         <see cref="IListenerManager" /> interface.
///     </para>
/// 	<para>
///         The setup/configuration of a <see cref="IScheduler"/> instance is very
///         customizable. Please consult the documentation distributed with Quartz.
///     </para>
/// </remarks>
/// <seealso cref="IJob"/>
/// <seealso cref="IJobDetail"/>
/// <seealso cref="ITrigger"/>
/// <seealso cref="IJobListener"/>
/// <seealso cref="ITriggerListener"/>
/// <seealso cref="ISchedulerListener"/>
/// <author>Marko Lahma (.NET)</author>
public interface IScheduler
{
    /// <summary>
    /// returns true if the given JobGroup
    /// is paused
    /// </summary>
    /// <param name="groupName"></param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns></returns>
    ValueTask<bool> IsJobGroupPaused(
        string groupName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// returns true if the given TriggerGroup
    /// is paused
    /// </summary>
    /// <param name="groupName"></param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns></returns>
    ValueTask<bool> IsTriggerGroupPaused(
        string groupName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the name of the <see cref="IScheduler" />.
    /// </summary>
    string SchedulerName { get; }

    /// <summary>
    /// Returns the instance Id of the <see cref="IScheduler" />.
    /// </summary>
    string SchedulerInstanceId { get; }

    /// <summary>
    /// Returns the <see cref="SchedulerContext" /> of the <see cref="IScheduler" />.
    /// </summary>
    SchedulerContext Context { get; }

    /// <summary>
    /// Reports whether the <see cref="IScheduler" /> is in stand-by mode.
    /// </summary>
    /// <seealso cref="Standby" />
    /// <seealso cref="Start" />
    bool InStandbyMode { get; }

    /// <summary>
    /// Reports whether the <see cref="IScheduler" /> has been Shutdown.
    /// </summary>
    bool IsShutdown { get; }

    /// <summary>
    /// Get a <see cref="SchedulerMetaData" /> object describing the settings
    /// and capabilities of the scheduler instance.
    /// </summary>
    /// <remarks>
    /// Note that the data returned is an 'instantaneous' snap-shot, and that as
    /// soon as it's returned, the meta data values may be different.
    /// </remarks>
    ValueTask<SchedulerMetaData> GetMetaData(CancellationToken cancellationToken = default);

    /// <summary>
    /// Return a list of <see cref="IJobExecutionContext" /> objects that
    /// represent all currently executing Jobs in this Scheduler instance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method is not cluster aware.  That is, it will only return Jobs
    /// currently executing in this Scheduler instance, not across the entire
    /// cluster.
    /// </para>
    /// <para>
    /// Note that the list returned is an 'instantaneous' snapshot, and that as
    /// soon as it's returned, the true list of executing jobs may be different.
    /// </para>
    /// </remarks>
    /// <seealso cref="IJobExecutionContext" />
    ValueTask<List<IJobExecutionContext>> GetCurrentlyExecutingJobs(CancellationToken cancellationToken = default);

    /// <summary>
    /// Set the <see cref="JobFactory" /> that will be responsible for producing
    /// instances of <see cref="IJob" /> classes.
    /// </summary>
    /// <remarks>
    /// JobFactories may be of use to those wishing to have their application
    /// produce <see cref="IJob" /> instances via some special mechanism, such as to
    /// give the opportunity for dependency injection.
    /// </remarks>
    /// <seealso cref="IJobFactory" />
    IJobFactory JobFactory { set; }

    /// <summary>
    /// Get a reference to the scheduler's <see cref="IListenerManager" />,
    /// through which listeners may be registered.
    /// </summary>
    /// <returns>the scheduler's <see cref="IListenerManager" /></returns>
    /// <seealso cref="ListenerManager" />
    /// <seealso cref="IJobListener" />
    /// <seealso cref="ITriggerListener" />
    /// <seealso cref="ISchedulerListener" />
    IListenerManager ListenerManager { get; }

    /// <summary>
    /// Get the names of all known <see cref="IJobDetail" /> groups.
    /// </summary>
    ValueTask<List<string>> GetJobGroupNames(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the names of all known <see cref="ITrigger" /> groups.
    /// </summary>
    ValueTask<List<string>> GetTriggerGroupNames(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the names of all <see cref="ITrigger" /> groups that are paused.
    /// </summary>
    ValueTask<List<string>> GetPausedTriggerGroups(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the <see cref="IScheduler" />'s threads that fire <see cref="ITrigger" />s.
    /// When a scheduler is first created it is in "stand-by" mode, and will not
    /// fire triggers.  The scheduler can also be put into stand-by mode by
    /// calling the <see cref="Standby" /> method.
    /// </summary>
    /// <remarks>
    /// The misfire/recovery process will be started, if it is the initial call
    /// to this method on this scheduler instance.
    /// </remarks>
    /// <seealso cref="StartDelayed(TimeSpan, CancellationToken)"/>
    /// <seealso cref="Standby"/>
    /// <seealso cref="Shutdown(bool, CancellationToken)"/>
    ValueTask Start(CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls <see cref="Start" /> after the indicated delay.
    /// (This call does not block). This can be useful within applications that
    /// have initializers that create the scheduler immediately, before the
    /// resources needed by the executing jobs have been fully initialized.
    /// </summary>
    /// <seealso cref="Start"/>
    /// <seealso cref="Standby"/>
    /// <seealso cref="Shutdown(bool, CancellationToken)"/>
    ValueTask StartDelayed(TimeSpan delay, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the scheduler has been started.
    /// </summary>
    /// <remarks>
    /// Note: This only reflects whether <see cref="Start" /> has ever
    /// been called on this Scheduler, so it will return <see langword="true" /> even
    /// if the <see cref="IScheduler" /> is currently in standby mode or has been
    /// since shutdown.
    /// </remarks>
    /// <seealso cref="Start" />
    /// <seealso cref="IsShutdown" />
    /// <seealso cref="InStandbyMode" />
    bool IsStarted { get; }

    /// <summary>
    /// Temporarily halts the <see cref="IScheduler" />'s firing of <see cref="ITrigger" />s.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <see cref="Start" /> is called (to bring the scheduler out of
    /// stand-by mode), trigger misfire instructions will NOT be applied
    /// during the execution of the <see cref="Start" /> method - any misfires
    /// will be detected immediately afterward (by the <see cref="IJobStore" />'s
    /// normal process).
    /// </para>
    /// <para>
    /// The scheduler is not destroyed, and can be re-started at any time.
    /// </para>
    /// </remarks>
    /// <seealso cref="Start"/>
    /// <seealso cref="PauseAll"/>
    ValueTask Standby(CancellationToken cancellationToken = default);

    /// <summary>
    /// Halts the <see cref="IScheduler" />'s firing of <see cref="ITrigger" />s,
    /// and cleans up all resources associated with the Scheduler. Equivalent to Shutdown(false).
    /// </summary>
    /// <remarks>
    /// The scheduler cannot be re-started.
    /// </remarks>
    /// <seealso cref="Shutdown(bool, CancellationToken)" />
    ValueTask Shutdown(CancellationToken cancellationToken = default);

    /// <summary>
    /// Halts the <see cref="IScheduler" />'s firing of <see cref="ITrigger" />s,
    /// and cleans up all resources associated with the Scheduler.
    /// </summary>
    /// <remarks>
    /// The scheduler cannot be re-started.
    /// </remarks>
    /// <param name="waitForJobsToComplete">
    /// if <see langword="true" /> the scheduler will not allow this method
    /// to return until all currently executing jobs have completed.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <seealso cref="Shutdown(CancellationToken)" />
    ValueTask Shutdown(bool waitForJobsToComplete, CancellationToken cancellationToken = default);


    /// <summary>
    /// Add the given <see cref="IJobDetail" /> to the
    /// Scheduler, and associate the given <see cref="ITrigger" /> with
    /// it.
    /// </summary>
    /// <remarks>
    /// If the given Trigger does not reference any <see cref="IJob" />, then it
    /// will be set to reference the Job passed with it into this method.
    /// </remarks>
    ValueTask<DateTimeOffset> ScheduleJob(
        IJobDetail jobDetail,
        ITrigger trigger,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedule the given <see cref="ITrigger" /> with the
    /// <see cref="IJob" /> identified by the <see cref="ITrigger" />'s settings.
    /// </summary>
    ValueTask<DateTimeOffset> ScheduleJob(
        ITrigger trigger,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedule all the given jobs with the related set of triggers.
    /// </summary>
    /// <remarks>
    /// <para>If any of the given jobs or triggers already exist (or more
    /// specifically, if the keys are not unique) and the replace
    /// parameter is not set to true then an exception will be thrown.</para>
    /// </remarks>
    ValueTask ScheduleJobs(
        IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs,
        bool replace,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedule the given job with the related set of triggers.
    /// </summary>
    /// <remarks>
    /// If any of the given job or triggers already exist (or more
    /// specifically, if the keys are not unique) and the replace
    /// parameter is not set to true then an exception will be thrown.
    /// </remarks>
    ValueTask ScheduleJob(
        IJobDetail jobDetail,
        IReadOnlyCollection<ITrigger> triggersForJob,
        bool replace,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove the indicated <see cref="ITrigger" /> from the scheduler.
    /// <para>If the related job does not have any other triggers, and the job is
    /// not durable, then the job will also be deleted.</para>
    /// </summary>
    ValueTask<bool> UnscheduleJob(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove all of the indicated <see cref="ITrigger" />s from the scheduler.
    /// </summary>
    /// <remarks>
    /// <para>If the related job does not have any other triggers, and the job is
    /// not durable, then the job will also be deleted.</para>
    /// Note that while this bulk operation is likely more efficient than
    /// invoking <see cref="UnscheduleJob" /> several
    /// times, it may have the adverse affect of holding data locks for a
    /// single long duration of time (rather than lots of small durations
    /// of time).
    /// </remarks>
    ValueTask<bool> UnscheduleJobs(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove (delete) the <see cref="ITrigger" /> with the
    /// given key, and store the new given one - which must be associated
    /// with the same job (the new trigger must have the job name &amp; group specified)
    /// - however, the new trigger need not have the same name as the old trigger.
    /// </summary>
    /// <param name="triggerKey">The <see cref="ITrigger" /> to be replaced.</param>
    /// <param name="newTrigger">
    ///     The new <see cref="ITrigger" /> to be stored.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// <see langword="null" /> if a <see cref="ITrigger" /> with the given
    /// name and group was not found and removed from the store (and the
    /// new trigger is therefore not stored),  otherwise
    /// the first fire time of the newly scheduled trigger.
    /// </returns>
    ValueTask<DateTimeOffset?> RescheduleJob(
        TriggerKey triggerKey,
        ITrigger newTrigger,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Add the given <see cref="IJob" /> to the Scheduler - with no associated
    /// <see cref="ITrigger" />. The <see cref="IJob" /> will be 'dormant' until
    /// it is scheduled with a <see cref="ITrigger" />, or <see cref="TriggerJob(Quartz.JobKey, CancellationToken)" />
    /// is called for it.
    /// </summary>
    /// <remarks>
    /// The <see cref="IJob" /> must by definition be 'durable', if it is not,
    /// SchedulerException will be thrown.
    /// </remarks>
    ValueTask AddJob(
        IJobDetail jobDetail,
        bool replace,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Add the given <see cref="IJob" /> to the Scheduler - with no associated
    /// <see cref="ITrigger" />. The <see cref="IJob" /> will be 'dormant' until
    /// it is scheduled with a <see cref="ITrigger" />, or <see cref="TriggerJob(Quartz.JobKey, CancellationToken)" />
    /// is called for it.
    /// </summary>
    /// <remarks>
    /// With the <paramref name="storeNonDurableWhileAwaitingScheduling"/> parameter
    /// set to <code>true</code>, a non-durable job can be stored.  Once it is
    /// scheduled, it will resume normal non-durable behavior (i.e. be deleted
    /// once there are no remaining associated triggers).
    /// </remarks>
    ValueTask AddJob(
        IJobDetail jobDetail,
        bool replace,
        bool storeNonDurableWhileAwaitingScheduling,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete the identified <see cref="IJob" /> from the Scheduler - and any
    /// associated <see cref="ITrigger" />s.
    /// </summary>
    /// <returns> true if the Job was found and deleted.</returns>
    ValueTask<bool> DeleteJob(
        JobKey jobKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete the identified jobs from the Scheduler - and any
    /// associated <see cref="ITrigger" />s.
    /// </summary>
    /// <remarks>
    /// <para>Note that while this bulk operation is likely more efficient than
    /// invoking <see cref="DeleteJob" /> several
    /// times, it may have the adverse affect of holding data locks for a
    /// single long duration of time (rather than lots of small durations
    /// of time).</para>
    /// </remarks>
    /// <returns>
    /// true if all of the Jobs were found and deleted, false if
    /// one or more were not deleted.
    /// </returns>
    ValueTask<bool> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Trigger the identified <see cref="IJobDetail" />
    /// (Execute it now).
    /// </summary>
    ValueTask TriggerJob(
        JobKey jobKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Trigger the identified <see cref="IJobDetail" /> (Execute it now).
    /// </summary>
    /// <param name="data">
    /// the (possibly <see langword="null" />) JobDataMap to be
    /// associated with the trigger that fires the job immediately.
    /// </param>
    /// <param name="jobKey">
    /// The <see cref="JobKey"/> of the <see cref="IJob" /> to be executed.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask TriggerJob(
        JobKey jobKey,
        JobDataMap data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pause the <see cref="IJobDetail" /> with the given
    /// key - by pausing all of its current <see cref="ITrigger" />s.
    /// </summary>
    ValueTask PauseJob(
        JobKey jobKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pause all of the <see cref="IJobDetail" />s in the
    /// matching groups - by pausing all of their <see cref="ITrigger" />s.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Scheduler will "remember" that the groups are paused, and impose the
    /// pause on any new jobs that are added to any of those groups until it is resumed.
    /// </para>
    /// <para>NOTE: There is a limitation that only exactly matched groups
    /// can be remembered as paused.  For example, if there are pre-existing
    /// job in groups "aaa" and "bbb" and a matcher is given to pause
    /// groups that start with "a" then the group "aaa" will be remembered
    /// as paused and any subsequently added jobs in group "aaa" will be paused,
    /// however if a job is added to group "axx" it will not be paused,
    /// as "axx" wasn't known at the time the "group starts with a" matcher
    /// was applied.  HOWEVER, if there are pre-existing groups "aaa" and
    /// "bbb" and a matcher is given to pause the group "axx" (with a
    /// group equals matcher) then no jobs will be paused, but it will be
    /// remembered that group "axx" is paused and later when a job is added
    /// in that group, it will become paused.</para>
    /// </remarks>
    /// <seealso cref="ResumeJobs" />
    ValueTask PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pause the <see cref="ITrigger" /> with the given key.
    /// </summary>
    ValueTask PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pause all of the <see cref="ITrigger" />s in the groups matching.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Scheduler will "remember" all the groups paused, and impose the
    /// pause on any new triggers that are added to any of those groups until it is resumed.
    /// </para>
    /// <para>NOTE: There is a limitation that only exactly matched groups
    /// can be remembered as paused.  For example, if there are pre-existing
    /// triggers in groups "aaa" and "bbb" and a matcher is given to pause
    /// groups that start with "a" then the group "aaa" will be remembered as
    /// paused and any subsequently added triggers in that group be paused,
    /// however if a trigger is added to group "axx" it will not be paused,
    /// as "axx" wasn't known at the time the "group starts with a" matcher
    /// was applied.  HOWEVER, if there are pre-existing groups "aaa" and
    /// "bbb" and a matcher is given to pause the group "axx" (with a
    /// group equals matcher) then no triggers will be paused, but it will be
    /// remembered that group "axx" is paused and later when a trigger is added
    /// in that group, it will become paused.</para>
    /// </remarks>
    /// <seealso cref="ResumeTriggers" />
    ValueTask PauseTriggers(
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resume (un-pause) the <see cref="IJobDetail" /> with
    /// the given key.
    /// </summary>
    /// <remarks>
    /// If any of the <see cref="IJob" />'s<see cref="ITrigger" /> s missed one
    /// or more fire-times, then the <see cref="ITrigger" />'s misfire
    /// instruction will be applied.
    /// </remarks>
    ValueTask ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resume (un-pause) all of the <see cref="IJobDetail" />s
    /// in matching groups.
    /// </summary>
    /// <remarks>
    /// If any of the <see cref="IJob" /> s had <see cref="ITrigger" /> s that
    /// missed one or more fire-times, then the <see cref="ITrigger" />'s
    /// misfire instruction will be applied.
    /// </remarks>
    /// <seealso cref="PauseJobs" />
    ValueTask ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resume (un-pause) the <see cref="ITrigger" /> with the given
    /// key.
    /// </summary>
    /// <remarks>
    /// If the <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </remarks>
    ValueTask ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resume (un-pause) all of the <see cref="ITrigger" />s in matching groups.
    /// </summary>
    /// <remarks>
    /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </remarks>
    /// <seealso cref="PauseTriggers" />
    ValueTask ResumeTriggers(
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pause all triggers - similar to calling <see cref="PauseTriggers" />
    /// on every group, however, after using this method <see cref="ResumeAll" />
    /// must be called to clear the scheduler's state of 'remembering' that all
    /// new triggers will be paused as they are added.
    /// </summary>
    /// <remarks>
    /// When <see cref="ResumeAll" /> is called (to un-pause), trigger misfire
    /// instructions WILL be applied.
    /// </remarks>
    /// <seealso cref="ResumeAll" />
    /// <seealso cref="PauseTriggers" />
    /// <seealso cref="Standby" />
    ValueTask PauseAll(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resume (un-pause) all triggers - similar to calling
    /// <see cref="ResumeTriggers" /> on every group.
    /// </summary>
    /// <remarks>
    /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </remarks>
    /// <seealso cref="PauseAll" />
    ValueTask ResumeAll(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the keys of all the <see cref="IJobDetail" />s in the matching groups.
    /// </summary>
    ValueTask<List<JobKey>> GetJobKeys(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all <see cref="ITrigger" /> s that are associated with the
    /// identified <see cref="IJobDetail" />.
    /// </summary>
    /// <remarks>
    /// The returned Trigger objects will be snapshots of the actual stored
    /// triggers.  If you wish to modify a trigger, you must re-store the
    /// trigger afterward (e.g. see <see cref="RescheduleJob(TriggerKey, ITrigger, CancellationToken)" />).
    /// </remarks>
    ValueTask<List<ITrigger>> GetTriggersOfJob(JobKey jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the names of all the <see cref="ITrigger" />s in the given
    /// groups.
    /// </summary>
    ValueTask<List<TriggerKey>> GetTriggerKeys(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the <see cref="IJobDetail" /> for the <see cref="IJob" />
    /// instance with the given key .
    /// </summary>
    /// <remarks>
    /// The returned JobDetail object will be a snapshot of the actual stored
    /// JobDetail.  If you wish to modify the JobDetail, you must re-store the
    /// JobDetail afterward (e.g. see <see cref="AddJob(IJobDetail, bool, CancellationToken)" />).
    /// </remarks>
    ValueTask<IJobDetail?> GetJobDetail(JobKey jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the <see cref="ITrigger" /> instance with the given key.
    /// </summary>
    /// <remarks>
    /// The returned Trigger object will be a snap-shot of the actual stored
    /// trigger.  If you wish to modify the trigger, you must re-store the
    /// trigger afterward (e.g. see <see cref="RescheduleJob(TriggerKey, ITrigger, CancellationToken)" />).
    /// </remarks>
    ValueTask<ITrigger?> GetTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the current state of the identified <see cref="ITrigger" />.
    /// </summary>
    /// <seealso cref="TriggerState.Normal" />
    /// <seealso cref="TriggerState.Paused" />
    /// <seealso cref="TriggerState.Complete" />
    /// <seealso cref="TriggerState.Blocked" />
    /// <seealso cref="TriggerState.Error" />
    /// <seealso cref="TriggerState.None" />
    ValueTask<TriggerState> GetTriggerState(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reset the current state of the identified <see cref="ITrigger" /> from <see cref="TriggerState.Error" />
    /// to <see cref="TriggerState.Normal" /> or <see cref="TriggerState.Paused" /> as appropriate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only affects triggers that are in <see cref="TriggerState.Error" /> state - if identified trigger is not
    /// in that state then the result is a no-op.
    /// </para>
    /// <para>
    /// The result will be the trigger returning to the normal, waiting to be fired state, unless the trigger's
    /// group has been paused, in which case it will go into the <see cref="TriggerState.Paused" /> state.
    /// </para>
    /// </remarks>
    /// <seealso cref="TriggerState"/>
    ValueTask ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add (register) the given <see cref="ICalendar" /> to the Scheduler.
    /// </summary>
    /// <param name="name">Name of the calendar.</param>
    /// <param name="calendar">The calendar.</param>
    /// <param name="replace">if set to <c>true</c> [replace].</param>
    /// <param name="updateTriggers">whether or not to update existing triggers that
    /// referenced the already existing calendar so that they are 'correct'
    /// based on the new trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask AddCalendar(
        string name,
        ICalendar calendar,
        bool replace,
        bool updateTriggers,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete the identified <see cref="ICalendar" /> from the Scheduler.
    /// </summary>
    /// <remarks>
    /// If removal of the <code>Calendar</code> would result in
    /// <see cref="ITrigger" />s pointing to non-existent calendars, then a
    /// <see cref="SchedulerException" /> will be thrown.
    /// </remarks>
    /// <param name="name">Name of the calendar.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if the Calendar was found and deleted.</returns>
    ValueTask<bool> DeleteCalendar(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the <see cref="ICalendar" /> instance with the given name.
    /// </summary>
    ValueTask<ICalendar?> GetCalendar(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the names of all registered <see cref="ICalendar" />.
    /// </summary>
    ValueTask<List<string>> GetCalendarNames(CancellationToken cancellationToken = default);

    /// <summary>
    /// Request the cancellation, within this Scheduler instance, of all
    /// currently executing instances of the identified <see cref="IJob" />.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If more than one instance of the identified job is currently executing,
    /// the cancellation token will be set on each instance.  However, there is a limitation that in the case that
    /// <see cref="Interrupt(JobKey, CancellationToken)" /> on one instances throws an exception, all
    /// remaining  instances (that have not yet been interrupted) will not have
    /// their <see cref="Interrupt(JobKey, CancellationToken)" /> method called.
    /// </para>
    ///
    /// <para>
    /// If you wish to interrupt a specific instance of a job (when more than
    /// one is executing) you can do so by calling
    /// <see cref="GetCurrentlyExecutingJobs" /> to obtain a handle
    /// to the job instance, and then invoke <see cref="Interrupt(JobKey, CancellationToken)" /> on it
    /// yourself.
    /// </para>
    /// <para>
    /// This method is not cluster aware.  That is, it will only interrupt
    /// instances of the identified InterruptableJob currently executing in this
    /// Scheduler instance, not across the entire cluster.
    /// </para>
    /// </remarks>
    /// <returns>
    /// true is at least one instance of the identified job was found and interrupted.
    /// </returns>
    /// <seealso cref="GetCurrentlyExecutingJobs" />
    ValueTask<bool> Interrupt(JobKey jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Request the cancellation, within this Scheduler instance, of the
    /// identified executing job instance.
    /// </summary>
    /// <remarks>
    /// This method is not cluster aware.  That is, it will only interrupt
    /// instances of the identified InterruptableJob currently executing in this
    /// Scheduler instance, not across the entire cluster.
    /// </remarks>
    /// <seealso cref="GetCurrentlyExecutingJobs" />
    /// <seealso cref="IJobExecutionContext.FireInstanceId" />
    /// <seealso cref="Interrupt(JobKey, CancellationToken)" />
    /// <param name="fireInstanceId">
    /// the unique identifier of the job instance to  be interrupted (see <see cref="IJobExecutionContext.FireInstanceId" />)
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if the identified job instance was found and interrupted.</returns>
    ValueTask<bool> Interrupt(string fireInstanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determine whether a <see cref="IJob" /> with the given identifier already
    /// exists within the scheduler.
    /// </summary>
    /// <param name="jobKey">the identifier to check for</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if a Job exists with the given identifier</returns>
    ValueTask<bool> CheckExists(JobKey jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determine whether a <see cref="ITrigger" /> with the given identifier already
    /// exists within the scheduler.
    /// </summary>
    /// <param name="triggerKey">the identifier to check for</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if a Trigger exists with the given identifier</returns>
    ValueTask<bool> CheckExists(TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears (deletes!) all scheduling data - all <see cref="IJob"/>s, <see cref="ITrigger" />s
    /// <see cref="ICalendar"/>s.
    /// </summary>
    ValueTask Clear(CancellationToken cancellationToken = default);
}