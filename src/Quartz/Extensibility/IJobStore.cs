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

namespace Quartz.Extensibility;

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
    /// How long the <see cref="IJobStore" /> implementation estimates that it will take to
    /// release a trigger and acquire a new one.
    /// </summary>
    TimeSpan EstimatedTimeToReleaseAndAcquireTrigger { get; }

    /// <summary>
    /// Whether the <see cref="IJobStore" /> implementation is clustered.
    /// </summary>
    /// <remarks>
    /// Read-only, because being clustered is something a store is rather than something it is told:
    /// the ADO.NET store reports what <see cref="ClusteringOptions.Enabled" /> says, and a store that
    /// cannot cluster answers <see langword="false" /> and means it.
    /// </remarks>
    bool Clustered { get; }

    /// <summary>
    /// Called before the <see cref="IJobStore" /> is used, to give it a chance to initialize.
    /// </summary>
    /// <remarks>
    /// Everything a job store needs — its scheduler's identity, the type loader, the signaler,
    /// the time provider — is supplied through its constructor. What remains here is work that has to
    /// happen before the scheduler runs and that cannot be done during construction, such as verifying
    /// a database schema.
    /// </remarks>
    ValueTask Initialize(CancellationToken cancellationToken = default);

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
    ValueTask ScheduleJob(IJobDetail job, IOperableTrigger trigger, CancellationToken cancellationToken = default);

    /// <summary>
    /// Store the given <see cref="IJobDetail" />.
    /// </summary>
    /// <param name="job">The <see cref="IJobDetail" /> to be stored.</param>
    /// <param name="replace">
    ///     If <see langword="true" />, any <see cref="IJob" /> existing in the
    ///     <see cref="IJobStore" /> with the same name and group should be
    ///     over-written.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask AddJob(IJobDetail job, bool replace, CancellationToken cancellationToken = default);

    /// <summary>
    /// Store all the given jobs with their related triggers.
    /// </summary>
    /// <param name="triggersAndJobs">
    ///     The jobs to store, each with the triggers that fire it. <see cref="IOperableTrigger" />,
    ///     like the rest of the store contract — the scheduler validates and downcasts the caller's
    ///     triggers before they reach the store.
    /// </param>
    /// <param name="replace">
    ///     If <see langword="true" />, any <see cref="IJob" /> or <see cref="ITrigger" /> existing in
    ///     the <see cref="IJobStore" /> with the same key should be over-written.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <throws>  ObjectAlreadyExistsException </throws>
    ValueTask ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<IOperableTrigger>> triggersAndJobs, bool replace, CancellationToken cancellationToken = default);

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
    ValueTask<bool> DeleteJob(JobKey jobKey, CancellationToken cancellationToken = default);

    ValueTask<bool> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve the <see cref="IJobDetail" /> for the given
    /// <see cref="IJob" />.
    /// </summary>
    /// <returns>
    /// The desired <see cref="IJob" />, or null if there is no match.
    /// </returns>
    ValueTask<IJobDetail?> GetJob(JobKey jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Store the given <see cref="ITrigger" />.
    /// </summary>
    /// <param name="trigger">The <see cref="ITrigger" /> to be stored.</param>
    /// <param name="replace">If <see langword="true" />, any <see cref="ITrigger" /> existing in
    ///     the <see cref="IJobStore" /> with the same name and group should
    ///     be over-written.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <throws>  ObjectAlreadyExistsException </throws>
    ValueTask AddTrigger(IOperableTrigger trigger, bool replace, CancellationToken cancellationToken = default);

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
    ValueTask<bool> DeleteTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default);

    ValueTask<bool> DeleteTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default);

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
    /// Updates trigger metadata and selected settings without deleting/recreating
    /// the trigger and without resetting fire times or trigger state.
    /// </summary>
    /// <param name="triggerKey">The key identifying the trigger to update.</param>
    /// <param name="update">
    /// The details to update. Only properties explicitly set will be changed.
    /// May include the calendar name and the misfire instruction, which can affect firing behavior.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns><see langword="true"/> if the trigger was found and updated, <see langword="false"/> if not found.</returns>
    ValueTask<bool> UpdateTriggerDetails(TriggerKey triggerKey, TriggerDetailsUpdate update, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve the given <see cref="ITrigger" />.
    /// </summary>
    /// <returns>
    /// The desired <see cref="ITrigger" />, or null if there is no
    /// match.
    /// </returns>
    ValueTask<IOperableTrigger?> GetTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determine whether a <see cref="IJob" /> with the given identifier already
    /// exists within the scheduler.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="jobKey">the identifier to check for</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if a job exists with the given identifier</returns>
    ValueTask<bool> Exists(JobKey jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determine whether a <see cref="ITrigger" /> with the given identifier already
    /// exists within the scheduler.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="triggerKey">the identifier to check for</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if a trigger exists with the given identifier</returns>
    ValueTask<bool> Exists(TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear (delete!) all scheduling data - all <see cref="IJob"/>s, <see cref="ITrigger" />s
    /// <see cref="ICalendar" />s.
    /// </summary>
    /// <remarks>
    /// </remarks>
    ValueTask Clear(CancellationToken cancellationToken = default);

    /// <summary>
    /// Store the given <see cref="ICalendar" />.
    /// </summary>
    /// <param name="calendarName">The name.</param>
    /// <param name="calendar">The <see cref="ICalendar" /> to be stored.</param>
    /// <param name="options">
    /// Whether an existing calendar of the same name may be over-written, and whether the triggers
    /// referencing it have their next fire time re-computed. Defaults to neither.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <throws>  ObjectAlreadyExistsException </throws>
    ValueTask AddCalendar(string calendarName, ICalendar calendar, AddCalendarOptions options = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove (delete) the <see cref="ICalendar" /> with the
    /// given name.
    /// </summary>
    /// <remarks>
    /// If removal of the <see cref="ICalendar" /> would result in
    /// <see cref="ITrigger" />s pointing to non-existent calendars, then a
    /// <see cref="JobPersistenceException" /> will be thrown.
    /// </remarks>
    /// <param name="calendarName">The name of the <see cref="ICalendar" /> to be removed.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// 	<see langword="true" /> if a <see cref="ICalendar" /> with the given name
    /// was found and removed from the store.
    /// </returns>
    ValueTask<bool> DeleteCalendar(string calendarName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve the given <see cref="ITrigger" />.
    /// </summary>
    /// <param name="calendarName">The name of the <see cref="ICalendar" /> to be retrieved.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// The desired <see cref="ICalendar" />, or null if there is no
    /// match.
    /// </returns>
    ValueTask<ICalendar?> GetCalendar(string calendarName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists jobs matching the query, as <see cref="JobHeader" />s, ordered by group and
    /// then name (ordinal).
    /// </summary>
    /// <remarks>
    /// A listing must not load or deserialize job data. When the query sets
    /// <see cref="PagedQuery.IncludeTotalCount" />, the result carries the total number of
    /// matching jobs regardless of paging.
    /// </remarks>
    /// <param name="query">What to select and which page of it to return.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<PagedResult<JobHeader>> QueryJobs(JobQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists triggers matching the query, as <see cref="TriggerHeader" />s, ordered by group
    /// and then name (ordinal).
    /// </summary>
    /// <remarks>
    /// A listing must not materialize triggers or their job data. The header carries the
    /// trigger's current state and execution group, so listing callers need no further
    /// round trips.
    /// </remarks>
    /// <param name="query">What to select and which page of it to return.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<PagedResult<TriggerHeader>> QueryTriggers(TriggerQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists job groups matching the query, ordered by name (ordinal).
    /// </summary>
    /// <param name="query">What to select and which page of it to return.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<PagedResult<JobGroup>> QueryJobGroups(JobGroupQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists trigger groups matching the query, ordered by name (ordinal).
    /// </summary>
    /// <remarks>
    /// With <see cref="TriggerGroupQuery.Paused" /> set to true, the listing reports every
    /// paused group, including a group that is paused but currently has no triggers.
    /// </remarks>
    /// <param name="query">What to select and which page of it to return.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<PagedResult<TriggerGroup>> QueryTriggerGroups(TriggerGroupQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists calendar names matching the query, ordered by name (ordinal).
    /// </summary>
    /// <param name="query">Which page to return.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<PagedResult<string>> QueryCalendarNames(CalendarQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the given jobs in one round trip. Keys that do not exist are simply
    /// absent from the result.
    /// </summary>
    /// <param name="jobKeys">The keys of the jobs to retrieve.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<List<IJobDetail>> GetJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the given triggers in one round trip. Keys that do not exist are simply
    /// absent from the result.
    /// </summary>
    /// <param name="triggerKeys">The keys of the triggers to retrieve.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<List<IOperableTrigger>> GetTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default);

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
    /// <returns>
    /// <see langword="true" /> if the trigger existed in the error state and was reset by this
    /// call, <see langword="false" /> if there is no trigger with the given key or it was not
    /// in the error state.
    /// </returns>
    /// <seealso cref="TriggerState"/>
    ValueTask<bool> ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /////////////////////////////////////////////////////////////////////////////
    //
    // Trigger State manipulation methods
    //
    /////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// Pause the <see cref="ITrigger" /> with the given key.
    /// </summary>
    /// <returns>
    /// <see langword="true" /> if the trigger exists and was moved into the paused state by this
    /// call, <see langword="false" /> if there is no trigger with the given key, it was already
    /// paused, or it is in a state that cannot be paused (e.g. complete).
    /// </returns>
    ValueTask<bool> PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default);

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
    /// <returns>
    /// <see langword="true" /> if the job exists — including a job that currently has no
    /// triggers — <see langword="false" /> if there is no job with the given key.
    /// </returns>
    ValueTask<bool> PauseJob(JobKey jobKey, CancellationToken cancellationToken = default);

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
    /// <returns>
    /// <see langword="true" /> if the trigger existed in a paused state and was resumed by this
    /// call, <see langword="false" /> if there is no trigger with the given key or it was not
    /// paused.
    /// </returns>
    ValueTask<bool> ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default);

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
    /// Resume (un-pause) the <see cref="IJob" /> with the
    /// given key.
    /// <para>
    /// If any of the <see cref="IJob" />'s<see cref="ITrigger" /> s missed one
    /// or more fire-times, then the <see cref="ITrigger" />'s misfire
    /// instruction will be applied.
    /// </para>
    /// </summary>
    /// <returns>
    /// <see langword="true" /> if the job exists — including a job that currently has no
    /// triggers — <see langword="false" /> if there is no job with the given key.
    /// </returns>
    ValueTask<bool> ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default);

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
    /// Acquires the next triggers to be fired, respecting execution group limits.
    /// </summary>
    /// <param name="request">What to acquire: the cut-off time, how many, the batching window
    /// and the per-execution-group capacity still available.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The acquired triggers.</returns>
    ValueTask<List<IOperableTrigger>> AcquireNextTriggers(
        TriggerAcquisitionRequest request,
        CancellationToken cancellationToken = default);

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
    ValueTask TriggeredJobComplete(IOperableTrigger trigger, IJobDetail jobDetail, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the amount of time to wait when accessing this job store repeatedly fails.
    /// </summary>
    /// <remarks>
    /// Called by the executor thread(s) when calls to <c>AcquireNextTriggers</c> fail more than once in succession,
    /// and the thread thus wants to wait a bit before trying again, to not consume 100% CPU,
    /// write huge amounts of errors into logs, etc. in cases like the DB being offline/restarting.
    ///
    /// The delay returned by implementations should be between 20 milliseconds and 10 minutes.
    /// </remarks>
    /// <param name="failureCount">the number of successive failures seen so far</param>
    /// <returns>the time to wait before trying again</returns>
    TimeSpan GetAcquireRetryDelay(int failureCount);
}