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

namespace Quartz.Spi;

/// <summary>
/// The interface to be implemented by classes that want to provide a <see cref="IJob" />
/// and <see cref="ITrigger" /> storage mechanism for the
/// <see cref="QuartzScheduler" />'s use.
/// </summary>
/// <remarks>
/// Storage of <see cref="IJob" /> s and <see cref="ITrigger" /> s should be keyed
/// on the combination of their name and group for uniqueness.
/// </remarks>
/// <seealso cref="QuartzScheduler" />
/// <seealso cref="ITrigger" />
/// <seealso cref="IJob" />
/// <seealso cref="IJobDetail" />
/// <seealso cref="JobDataMap" />
/// <seealso cref="ICalendar" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public interface IJobStore
{
    /// <summary>
    /// Indicates whether job store supports persistence.
    /// </summary>
    /// <returns></returns>
    bool SupportsPersistence { get; }

    /// <summary>
    /// How long (in milliseconds) the <see cref="IJobStore" /> implementation
    /// estimates that it will take to release a trigger and acquire a new one.
    /// </summary>
    long EstimatedTimeToReleaseAndAcquireTrigger { get; }

    /// <summary>
    /// Whether the <see cref="IJobStore" /> implementation is clustered.
    /// </summary>
    /// <returns></returns>
    bool Clustered { get; }

    /// <summary>
    /// Inform the <see cref="IJobStore" /> of the Scheduler instance's Id,
    /// prior to initialize being invoked.
    /// </summary>
    string InstanceId { set; }

    /// <summary>
    /// Inform the <see cref="IJobStore" /> of the Scheduler instance's name,
    /// prior to initialize being invoked.
    /// </summary>
    string InstanceName { set; }

    /// <summary>
    /// Tells the JobStore the pool size used to execute jobs.
    /// </summary>
    int ThreadPoolSize { set; }

    /// <summary>
    /// Time provider to use.
    /// </summary>
    TimeProvider TimeProvider { set; }

    /// <summary>
    /// Called by the QuartzScheduler before the <see cref="IJobStore" /> is
    /// used, in order to give it a chance to Initialize.
    /// </summary>
    ValueTask Initialize(ITypeLoadHelper loadHelper, ISchedulerSignaler signaler, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the QuartzScheduler to inform the <see cref="IJobStore" /> that
    /// the scheduler has started.
    /// </summary>
    ValueTask SchedulerStarted(CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the QuartzScheduler to inform the JobStore that
    /// the scheduler has been paused.
    /// </summary>
    ValueTask SchedulerPaused(CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the QuartzScheduler to inform the JobStore that
    /// the scheduler has resumed after being paused.
    /// </summary>
    ValueTask SchedulerResumed(CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the QuartzScheduler to inform the <see cref="IJobStore" /> that
    /// it should free up all of its resources because the scheduler is shutting down.
    /// </summary>
    ValueTask Shutdown(CancellationToken cancellationToken = default);

    /// <summary>
    /// Store the given <see cref="IJobDetail" /> and <see cref="ITrigger" />.
    /// </summary>
    /// <param name="job">The <see cref="IJobDetail" /> to be stored.</param>
    /// <param name="trigger">The <see cref="ITrigger" /> to be stored.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <throws>  ObjectAlreadyExistsException </throws>
    ValueTask StoreJobAndTrigger(IJobDetail job, IOperableTrigger trigger, CancellationToken cancellationToken = default);

    /// <summary>
    /// returns true if the given JobGroup is paused
    /// </summary>
    /// <param name="group"></param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns></returns>
    ValueTask<bool> IsJobGroupPaused(string group, CancellationToken cancellationToken = default);

    /// <summary>
    /// returns true if the given TriggerGroup
    /// is paused
    /// </summary>
    /// <param name="group"></param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns></returns>
    ValueTask<bool> IsTriggerGroupPaused(string group, CancellationToken cancellationToken = default);
    /// <summary>
    /// Store the given <see cref="IJobDetail" />.
    /// </summary>
    /// <param name="job">The <see cref="IJobDetail" /> to be stored.</param>
    /// <param name="replaceExisting">
    ///     If <see langword="true" />, any <see cref="IJob" /> existing in the
    ///     <see cref="IJobStore" /> with the same name and group should be
    ///     over-written.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask StoreJob(IJobDetail job, bool replaceExisting, CancellationToken cancellationToken = default);

    ValueTask StoreJobsAndTriggers(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, bool replace, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove (delete) the <see cref="IJob" /> with the given
    /// key, and any <see cref="ITrigger" /> s that reference
    /// it.
    /// </summary>
    /// <remarks>
    /// If removal of the <see cref="IJob" /> results in an empty group, the
    /// group should be removed from the <see cref="IJobStore" />'s list of
    /// known group names.
    /// </remarks>
    /// <returns>
    /// 	<see langword="true" /> if a <see cref="IJob" /> with the given name and
    /// group was found and removed from the store.
    /// </returns>
    ValueTask<bool> RemoveJob(JobKey jobKey, CancellationToken cancellationToken = default);

    ValueTask<bool> RemoveJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve the <see cref="IJobDetail" /> for the given
    /// <see cref="IJob" />.
    /// </summary>
    /// <returns>
    /// The desired <see cref="IJob" />, or null if there is no match.
    /// </returns>
    ValueTask<IJobDetail?> RetrieveJob(JobKey jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Store the given <see cref="ITrigger" />.
    /// </summary>
    /// <param name="trigger">The <see cref="ITrigger" /> to be stored.</param>
    /// <param name="replaceExisting">If <see langword="true" />, any <see cref="ITrigger" /> existing in
    ///     the <see cref="IJobStore" /> with the same name and group should
    ///     be over-written.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <throws>  ObjectAlreadyExistsException </throws>
    ValueTask StoreTrigger(IOperableTrigger trigger, bool replaceExisting, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove (delete) the <see cref="ITrigger" /> with the given key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If removal of the <see cref="ITrigger" /> results in an empty group, the
    /// group should be removed from the <see cref="IJobStore" />'s list of
    /// known group names.
    /// </para>
    /// <para>
    /// If removal of the <see cref="ITrigger" /> results in an 'orphaned' <see cref="IJob" />
    /// that is not 'durable', then the <see cref="IJob" /> should be deleted
    /// also.
    /// </para>
    /// </remarks>
    /// <returns>
    /// 	<see langword="true" /> if a <see cref="ITrigger" /> with the given
    /// name and group was found and removed from the store.
    /// </returns>
    ValueTask<bool> RemoveTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default);

    ValueTask<bool> RemoveTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove (delete) the <see cref="ITrigger" /> with the
    /// given name, and store the new given one - which must be associated
    /// with the same job.
    /// </summary>
    /// <param name="triggerKey">The <see cref="ITrigger"/> to be replaced.</param>
    /// <param name="trigger">The new <see cref="ITrigger" /> to be stored.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// 	<see langword="true" /> if a <see cref="ITrigger" /> with the given
    /// name and group was found and removed from the store.
    /// </returns>
    ValueTask<bool> ReplaceTrigger(TriggerKey triggerKey, IOperableTrigger trigger, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve the given <see cref="ITrigger" />.
    /// </summary>
    /// <returns>
    /// The desired <see cref="ITrigger" />, or null if there is no
    /// match.
    /// </returns>
    ValueTask<IOperableTrigger?> RetrieveTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determine whether a <see cref="ICalendar" /> with the given identifier already
    /// exists within the scheduler.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="name">the identifier to check for</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if a calendar exists with the given identifier</returns>
    ValueTask<bool> CalendarExists(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determine whether a <see cref="IJob" /> with the given identifier already
    /// exists within the scheduler.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="jobKey">the identifier to check for</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if a job exists with the given identifier</returns>
    ValueTask<bool> CheckExists(JobKey jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determine whether a <see cref="ITrigger" /> with the given identifier already
    /// exists within the scheduler.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="triggerKey">the identifier to check for</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if a trigger exists with the given identifier</returns>
    ValueTask<bool> CheckExists(TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear (delete!) all scheduling data - all <see cref="IJob"/>s, <see cref="ITrigger" />s
    /// <see cref="ICalendar" />s.
    /// </summary>
    /// <remarks>
    /// </remarks>
    ValueTask ClearAllSchedulingData(CancellationToken cancellationToken = default);

    /// <summary>
    /// Store the given <see cref="ICalendar" />.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="calendar">The <see cref="ICalendar" /> to be stored.</param>
    /// <param name="replaceExisting">If <see langword="true" />, any <see cref="ICalendar" /> existing
    /// in the <see cref="IJobStore" /> with the same name and group
    /// should be over-written.</param>
    /// <param name="updateTriggers">If <see langword="true" />, any <see cref="ITrigger" />s existing
    /// in the <see cref="IJobStore" /> that reference an existing
    /// Calendar with the same name with have their next fire time
    /// re-computed with the new <see cref="ICalendar" />.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <throws>  ObjectAlreadyExistsException </throws>
    ValueTask StoreCalendar(string name, ICalendar calendar, bool replaceExisting, bool updateTriggers, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove (delete) the <see cref="ICalendar" /> with the
    /// given name.
    /// </summary>
    /// <remarks>
    /// If removal of the <see cref="ICalendar" /> would result in
    /// <see cref="ITrigger" />s pointing to non-existent calendars, then a
    /// <see cref="JobPersistenceException" /> will be thrown.
    /// </remarks>
    /// <param name="name">The name of the <see cref="ICalendar" /> to be removed.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// 	<see langword="true" /> if a <see cref="ICalendar" /> with the given name
    /// was found and removed from the store.
    /// </returns>
    ValueTask<bool> RemoveCalendar(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve the given <see cref="ITrigger" />.
    /// </summary>
    /// <param name="name">The name of the <see cref="ICalendar" /> to be retrieved.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// The desired <see cref="ICalendar" />, or null if there is no
    /// match.
    /// </returns>
    ValueTask<ICalendar?> RetrieveCalendar(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the number of <see cref="IJob" />s that are
    /// stored in the <see cref="IJobStore" />.
    /// </summary>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns></returns>
    ValueTask<int> GetNumberOfJobs(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the number of <see cref="ITrigger" />s that are
    /// stored in the <see cref="IJobStore" />.
    /// </summary>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns></returns>
    ValueTask<int> GetNumberOfTriggers(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the number of <see cref="ICalendar" /> s that are
    /// stored in the <see cref="IJobStore" />.
    /// </summary>
    /// <returns></returns>
    ValueTask<int> GetNumberOfCalendars(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the names of all of the <see cref="IJob" /> s that
    /// have the given group name.
    /// <para>
    /// If there are no jobs in the given group name, the result should be a
    /// zero-length array (not <see langword="null" />).
    /// </para>
    /// </summary>
    /// <param name="matcher"></param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns></returns>
    ValueTask<List<JobKey>> GetJobKeys(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the names of all of the <see cref="ITrigger" />s
    /// that have the given group name.
    /// <para>
    /// If there are no triggers in the given group name, the result should be a
    /// zero-length array (not <see langword="null" />).
    /// </para>
    /// </summary>
    ValueTask<List<TriggerKey>> GetTriggerKeys(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the names of all of the <see cref="IJob" />
    /// groups.
    /// <para>
    /// If there are no known group names, the result should be a zero-length
    /// array (not <see langword="null" />).
    /// </para>
    /// </summary>
    ValueTask<List<string>> GetJobGroupNames(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the names of all of the <see cref="ITrigger" />
    /// groups.
    /// <para>
    /// If there are no known group names, the result should be a zero-length
    /// array (not <see langword="null" />).
    /// </para>
    /// </summary>
    ValueTask<List<string>> GetTriggerGroupNames(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the names of all of the <see cref="ICalendar" />s in the <see cref="IJobStore" />.
    ///
    /// <para>
    /// If there are no Calendars in the given group name, the result should be
    /// a zero-length array (not <see langword="null" />).
    /// </para>
    /// </summary>
    ValueTask<List<string>> GetCalendarNames(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all the Triggers that are associated to the given Job.
    /// </summary>
    /// <remarks>
    /// If there are no matches, a zero-length array should be returned.
    /// </remarks>
    ValueTask<List<IOperableTrigger>> GetTriggersForJob(JobKey jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the current state of the identified <see cref="ITrigger" />.
    /// </summary>
    /// <seealso cref="TriggerState" />
    ValueTask<TriggerState> GetTriggerState(TriggerKey triggerKey, CancellationToken cancellationToken = default);

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

    /////////////////////////////////////////////////////////////////////////////
    //
    // Trigger State manipulation methods
    //
    /////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// Pause the <see cref="ITrigger" /> with the given key.
    /// </summary>
    ValueTask PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pause all of the <see cref="ITrigger" />s in the
    /// given group.
    /// </summary>
    /// <remarks>
    /// The JobStore should "remember" that the group is paused, and impose the
    /// pause on any new triggers that are added to the group while the group is
    /// paused.
    /// </remarks>
    ValueTask<List<string>> PauseTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pause the <see cref="IJob" /> with the given key - by
    /// pausing all of its current <see cref="ITrigger" />s.
    /// </summary>
    ValueTask PauseJob(JobKey jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pause all of the <see cref="IJob" />s in the given
    /// group - by pausing all of their <see cref="ITrigger" />s.
    /// <para>
    /// The JobStore should "remember" that the group is paused, and impose the
    /// pause on any new jobs that are added to the group while the group is
    /// paused.
    /// </para>
    /// </summary>
    /// <seealso cref="string">
    /// </seealso>
    ValueTask<List<string>> PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resume (un-pause) the <see cref="ITrigger" /> with the
    /// given key.
    ///
    /// <para>
    /// If the <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </para>
    /// </summary>
    /// <seealso cref="string">
    /// </seealso>
    ValueTask ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resume (un-pause) all of the <see cref="ITrigger" />s
    /// in the given group.
    /// <para>
    /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </para>
    /// </summary>
    ValueTask<List<string>> ResumeTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the paused trigger groups.
    /// </summary>
    /// <returns></returns>
    ValueTask<List<string>> GetPausedTriggerGroups(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resume (un-pause) the <see cref="IJob" /> with the
    /// given key.
    /// <para>
    /// If any of the <see cref="IJob" />'s<see cref="ITrigger" /> s missed one
    /// or more fire-times, then the <see cref="ITrigger" />'s misfire
    /// instruction will be applied.
    /// </para>
    /// </summary>
    ValueTask ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resume (un-pause) all of the <see cref="IJob" />s in
    /// the given group.
    /// <para>
    /// If any of the <see cref="IJob" /> s had <see cref="ITrigger" /> s that
    /// missed one or more fire-times, then the <see cref="ITrigger" />'s
    /// misfire instruction will be applied.
    /// </para>
    /// </summary>
    ValueTask<List<string>> ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pause all triggers - equivalent of calling <see cref="PauseTriggers" />
    /// on every group.
    /// <para>
    /// When <see cref="ResumeAll" /> is called (to un-pause), trigger misfire
    /// instructions WILL be applied.
    /// </para>
    /// </summary>
    /// <seealso cref="ResumeAll" />
    ValueTask PauseAll(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resume (un-pause) all triggers - equivalent of calling <see cref="ResumeTriggers" />
    /// on every group.
    /// <para>
    /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </para>
    ///
    /// </summary>
    /// <seealso cref="PauseAll" />
    ValueTask ResumeAll(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a handle to the next trigger to be fired, and mark it as 'reserved'
    /// by the calling scheduler.
    /// </summary>
    /// <param name="noLaterThan">If &gt; 0, the JobStore should only return a Trigger
    /// that will fire no later than the time represented in this value as
    /// milliseconds.</param>
    /// <param name="maxCount"></param>
    /// <param name="timeWindow"></param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns></returns>
    /// <seealso cref="ITrigger">
    /// </seealso>
    ValueTask<List<IOperableTrigger>> AcquireNextTriggers(DateTimeOffset noLaterThan, int maxCount, TimeSpan timeWindow, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inform the <see cref="IJobStore" /> that the scheduler no longer plans to
    /// fire the given <see cref="ITrigger" />, that it had previously acquired
    /// (reserved).
    /// </summary>
    ValueTask ReleaseAcquiredTrigger(IOperableTrigger trigger, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inform the <see cref="IJobStore" /> that the scheduler is now firing the
    /// given <see cref="ITrigger" /> (executing its associated <see cref="IJob" />),
    /// that it had previously acquired (reserved).
    /// </summary>
    /// <returns>
    /// May return null if all the triggers or their calendars no longer exist, or
    /// if the trigger was not successfully put into the 'executing'
    /// state.  Preference is to return an empty list if none of the triggers
    /// could be fired.
    /// </returns>
    ValueTask<List<TriggerFiredResult>> TriggersFired(IReadOnlyCollection<IOperableTrigger> triggers, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inform the <see cref="IJobStore" /> that the scheduler has completed the
    /// firing of the given <see cref="ITrigger" /> (and the execution its
    /// associated <see cref="IJob" />), and that the <see cref="JobDataMap" />
    /// in the given <see cref="IJobDetail" /> should be updated if the <see cref="IJob" />
    /// is stateful.
    /// </summary>
    ValueTask TriggeredJobComplete(IOperableTrigger trigger, IJobDetail jobDetail, SchedulerInstruction triggerInstCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the amount of time (in ms) to wait when accessing this job store repeatedly fails.
    /// </summary>
    /// <remarks>
    /// Called by the executor thread(s) when calls to <see cref="AcquireNextTriggers"/> fail more than once in succession,
    /// and the thread thus wants to wait a bit before trying again, to not consume 100% CPU,
    /// write huge amounts of errors into logs, etc. in cases like the DB being offline/restarting.
    ///
    /// The delay returned by implementations should be between 20 and 600000 milliseconds.* @param failureCount
    /// </remarks>
    /// <param name="failureCount">the number of successive failures seen so far</param>
    /// <returns>the time (in milliseconds) to wait before trying again</returns>
    TimeSpan GetAcquireRetryDelay(int failureCount);
}