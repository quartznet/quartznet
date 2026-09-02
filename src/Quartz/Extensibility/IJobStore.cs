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
    /// Nearly everything a job store needs — the type loader, the signaler, the time provider — is
    /// supplied through its constructor. What remains here is the scheduler's identity, which is not
    /// settled until the container has built the graph, and work that has to happen before the
    /// scheduler runs and cannot be done during construction, such as verifying a database schema.
    /// </remarks>
    /// <param name="identity">The scheduler this store stores for, and the node it is running on. A
    /// store records the instance id against the firings this node owns, so that
    /// <see cref="QueryFireInstances" /> can say which node is running what.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask Initialize(SchedulerIdentity identity, CancellationToken cancellationToken = default);

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
    /// <exception cref="ObjectAlreadyExistsException">
    /// A job or a trigger is already stored under one of the two keys. Nothing is stored.
    /// </exception>
    ValueTask ScheduleJob(IJobDetail job, IOperableTrigger trigger, CancellationToken cancellationToken = default);

    /// <summary>
    /// Store the given <see cref="IJobDetail" />.
    /// </summary>
    /// <param name="job">The <see cref="IJobDetail" /> to be stored.</param>
    /// <param name="options">
    ///     How to store it. <see cref="AddJobOptions.Replace" /> over-writes a job already stored
    ///     under the same key; without it, storing one whose key exists throws
    ///     <see cref="ObjectAlreadyExistsException" />.
    ///     <see cref="AddJobOptions.StoreNonDurableWhileAwaitingScheduling" /> is a scheduler-level
    ///     rule that <see cref="IScheduler" /> has already applied by the time the store is called, so
    ///     a store neither reads it nor has anything to do about it.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask AddJob(IJobDetail job, AddJobOptions options = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Store all the given jobs with their related triggers.
    /// </summary>
    /// <param name="triggersAndJobs">
    ///     The jobs to store, each with the triggers that fire it. <see cref="IOperableTrigger" />,
    ///     like the rest of the store contract — the scheduler validates and downcasts the caller's
    ///     triggers before they reach the store.
    /// </param>
    /// <param name="options">
    ///     How to store them. <see cref="ScheduleJobOptions.Replace" /> over-writes any job or trigger
    ///     already stored under one of the same keys; without it, a key that exists throws
    ///     <see cref="ObjectAlreadyExistsException" /> and none of the batch is stored.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <exception cref="ObjectAlreadyExistsException">
    /// A key in the batch is already stored and <see cref="ScheduleJobOptions.Replace" /> was not
    /// asked for. None of the batch is stored.
    /// </exception>
    ValueTask ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<IOperableTrigger>> triggersAndJobs, ScheduleJobOptions options = default, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Remove (delete) the <see cref="IJob" />s with the given keys, and any
    /// <see cref="ITrigger" />s that reference them.
    /// </summary>
    /// <remarks>
    /// The default implementation walks the set one key at a time. A store overrides it to do the
    /// walk inside a single lock or connection scope; the answer must not change when it does.
    /// </remarks>
    /// <returns>
    /// The keys this call removed, in the order they were given. A key that names no job is simply
    /// absent — the plural of the single-key <see langword="bool" />, not a failure.
    /// </returns>
    /// <seealso cref="DeleteJob" />
    ValueTask<List<JobKey>> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        return ApplyToEach(jobKeys, DeleteJob, cancellationToken);
    }

    /// <summary>
    /// Remove (delete) every <see cref="IJob" /> in the matching groups, and any
    /// <see cref="ITrigger" />s that reference them.
    /// </summary>
    /// <remarks>
    /// The default implementation lists the matching keys and then deletes them, which is two
    /// operations and so lets a job added in between escape. A store overrides it to resolve the
    /// keys and delete them under the same lock or connection scope; the answer must not change
    /// when it does.
    /// </remarks>
    /// <returns>
    /// The keys this call removed. A group that matched nothing contributes nothing — an empty
    /// list is the plural of the single-key <see langword="false" />, not a failure.
    /// </returns>
    /// <seealso cref="DeleteJobs(IReadOnlyCollection{JobKey}, CancellationToken)" />
    ValueTask<List<JobKey>> DeleteJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(matcher);

        return DeleteMatchingJobs(this, matcher, cancellationToken);
    }

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
    /// <param name="options">
    ///     How to store it. <see cref="AddTriggerOptions.Replace" /> over-writes a trigger already
    ///     stored under the same key; without it, storing one whose key exists throws
    ///     <see cref="ObjectAlreadyExistsException" />.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <exception cref="ObjectAlreadyExistsException">
    /// A trigger is already stored under the same key and <see cref="AddTriggerOptions.Replace" />
    /// was not asked for.
    /// </exception>
    ValueTask AddTrigger(IOperableTrigger trigger, AddTriggerOptions options = default, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Remove (delete) the <see cref="ITrigger" />s with the given keys.
    /// </summary>
    /// <remarks>
    /// The default implementation walks the set one key at a time. A store overrides it to do the
    /// walk inside a single lock or connection scope; the answer must not change when it does.
    /// </remarks>
    /// <returns>
    /// The keys this call removed, in the order they were given. A key that names no trigger is
    /// simply absent — the plural of the single-key <see langword="bool" />, not a failure. A job
    /// left orphaned and non-durable by the removal is deleted too, as in the single-key form, but
    /// the answer names triggers only.
    /// </returns>
    /// <seealso cref="DeleteTrigger" />
    ValueTask<List<TriggerKey>> DeleteTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        return ApplyToEach(triggerKeys, DeleteTrigger, cancellationToken);
    }

    /// <summary>
    /// Remove (delete) every <see cref="ITrigger" /> in the matching groups.
    /// </summary>
    /// <remarks>
    /// The default implementation lists the matching keys and then deletes them, which is two
    /// operations and so lets a trigger added in between escape. A store overrides it to resolve
    /// the keys and delete them under the same lock or connection scope; the answer must not change
    /// when it does.
    /// </remarks>
    /// <returns>
    /// The keys this call removed. A job left orphaned and non-durable by the removal is deleted
    /// too, as in the single-key form, but the answer names triggers only.
    /// </returns>
    /// <seealso cref="DeleteTriggers(IReadOnlyCollection{TriggerKey}, CancellationToken)" />
    ValueTask<List<TriggerKey>> DeleteTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(matcher);

        return DeleteMatchingTriggers(this, matcher, cancellationToken);
    }

    /// <summary>
    /// Remove (delete) the <see cref="ITrigger" /> with the
    /// given name, and store the new given one - which must be associated
    /// with the same job.
    /// </summary>
    /// <param name="triggerKey">The <see cref="ITrigger"/> to be replaced.</param>
    /// <param name="trigger">The new <see cref="ITrigger" /> to be stored.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// 	<see langword="true" /> if a <see cref="ITrigger" /> was stored under
    /// <paramref name="triggerKey" /> and has been replaced by <paramref name="trigger" />.
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
    /// <param name="jobKey">the identifier to check for</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if a job exists with the given identifier</returns>
    ValueTask<bool> Exists(JobKey jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determine whether a <see cref="ITrigger" /> with the given identifier already
    /// exists within the scheduler.
    /// </summary>
    /// <param name="triggerKey">the identifier to check for</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if a trigger exists with the given identifier</returns>
    ValueTask<bool> Exists(TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determine whether an <see cref="ICalendar" /> with the given name already exists within the
    /// store.
    /// </summary>
    /// <remarks>
    /// Answer this without materializing the calendar. <see cref="GetCalendar" /> can answer it too, but
    /// only by reading the stored blob and deserializing it to throw it away — which is what a store
    /// implementing this member as <c>GetCalendar(name) is not null</c> would go on doing.
    /// </remarks>
    /// <param name="calendarName">the name to check for</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if a calendar is stored under the given name</returns>
    ValueTask<bool> Exists(string calendarName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear (delete!) all scheduling data - all <see cref="IJob"/>s, <see cref="ITrigger" />s and
    /// <see cref="ICalendar" />s.
    /// </summary>
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
    /// <exception cref="ObjectAlreadyExistsException">
    /// A calendar is already stored under the same name and
    /// <see cref="AddCalendarOptions.Replace" /> was not asked for.
    /// </exception>
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
    /// Retrieve the given <see cref="ICalendar" />.
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
    /// <param name="query">Which names to select and which page of them to return.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<PagedResult<string>> QueryCalendarNames(CalendarQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists firings matching the query, as <see cref="FireInstance" />s, ordered by trigger group,
    /// then trigger name, then fire instance id (all ordinal).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tiebreaker is what makes a page deterministic here: one trigger can have several firings in
    /// flight, so a store must not collapse them and must not order two of them arbitrarily.
    /// </para>
    /// <para>
    /// A store that keeps firings durably answers for the whole cluster; the in-memory store answers for
    /// its own process, which is the whole of its world. Either way the reported
    /// <see cref="FireInstance.SchedulerInstanceId" /> is the id the owning node was initialized with.
    /// </para>
    /// </remarks>
    /// <param name="query">What to select and which page of it to return.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<PagedResult<FireInstance>> QueryFireInstances(FireInstanceQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the scheduler nodes this store knows about, as <see cref="ClusterNode" />s: the current
    /// node first, then the rest by instance id (ordinal).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The current node is always in the list, whether or not the store has a record of it yet, and it
    /// is the only one whose <see cref="ClusterNode.IsCurrentNode" /> is <see langword="true" />. A
    /// store that keeps no check-in history — the in-memory one, and a persistent one that is not
    /// clustered — answers with that single node, <see cref="ClusterNodeState.Alive" /> and with no
    /// times, because a lone node has nobody to be late for.
    /// </para>
    /// <para>
    /// A clustered store reports every node it has a check-in record for, including nodes that are
    /// dead but not yet swept, and decides <see cref="ClusterNode.State" /> with the same predicate its
    /// recovery pass uses — so a node this listing calls
    /// <see cref="ClusterNodeState.Failed" /> is a node whose work the cluster is about to take over,
    /// rather than one that merely looks late to a second opinion.
    /// </para>
    /// <para>
    /// Unpaged, because a cluster is a handful of nodes rather than a data set; the listing that does
    /// need paging is <see cref="QueryFireInstances" />, which reports what each node is running.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<List<ClusterNode>> QueryClusterNodes(CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Reset every one of the identified <see cref="ITrigger" />s from <see cref="TriggerState.Error" />
    /// to <see cref="TriggerState.Normal" /> or <see cref="TriggerState.Paused" /> as appropriate.
    /// </summary>
    /// <remarks>
    /// The default implementation walks the set one key at a time. A store overrides it to do the
    /// walk inside a single lock or connection scope; the answer must not change when it does.
    /// </remarks>
    /// <returns>
    /// The keys this call reset, in the order they were given. A key that names no trigger, or one
    /// that was not in the error state, is simply absent — the plural of the single-key
    /// <see langword="bool" />, not a failure.
    /// </returns>
    /// <seealso cref="ResetTriggerFromErrorState" />
    ValueTask<List<TriggerKey>> ResetTriggersFromErrorState(
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        return ApplyToEach(triggerKeys, ResetTriggerFromErrorState, cancellationToken);
    }

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
    /// Pause the <see cref="ITrigger" />s with the given keys.
    /// </summary>
    /// <remarks>
    /// The default implementation walks the set one key at a time. A store overrides it to do the
    /// walk inside a single lock or connection scope; the answer must not change when it does.
    /// </remarks>
    /// <returns>
    /// The keys this call moved into the paused state, in the order they were given. A key that
    /// names no trigger, one that was already paused, and one in a state that cannot be paused are
    /// each simply absent — the plural of the single-key <see langword="bool" />, not a failure.
    /// </returns>
    /// <seealso cref="PauseTrigger" />
    ValueTask<List<TriggerKey>> PauseTriggers(
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        return ApplyToEach(triggerKeys, PauseTrigger, cancellationToken);
    }

    /// <summary>
    /// Pause the trigger groups that match, and every <see cref="ITrigger" /> in them.
    /// </summary>
    /// <remarks>
    /// The JobStore should "remember" that the group is paused, and impose the
    /// pause on any new triggers that are added to the group while the group is
    /// paused. That memory is what this answers with — the names of the groups now recorded as
    /// paused, which is not the set of keys that moved: an equality matcher records a group that
    /// holds no trigger yet.
    /// </remarks>
    /// <returns>The names of the trigger groups that are recorded as paused by this call.</returns>
    ValueTask<List<string>> PauseTriggerGroups(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default);

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
    /// Pause the <see cref="IJob" />s with the given keys - by pausing all of their current
    /// <see cref="ITrigger" />s.
    /// </summary>
    /// <remarks>
    /// The default implementation walks the set one key at a time. A store overrides it to do the
    /// walk inside a single lock or connection scope; the answer must not change when it does.
    /// </remarks>
    /// <returns>
    /// The keys this call found, in the order they were given — a job with no triggers is found and
    /// so is present. A key that names no job is simply absent — the plural of the single-key
    /// <see langword="bool" />, not a failure.
    /// </returns>
    /// <seealso cref="PauseJob" />
    ValueTask<List<JobKey>> PauseJobs(
        IReadOnlyCollection<JobKey> jobKeys,
        CancellationToken cancellationToken = default)
    {
        return ApplyToEach(jobKeys, PauseJob, cancellationToken);
    }

    /// <summary>
    /// Pause the job groups that match - by pausing all of the <see cref="ITrigger" />s of the
    /// <see cref="IJob" />s in them.
    /// </summary>
    /// <remarks>
    /// The JobStore should "remember" that the group is paused, and impose the
    /// pause on any new jobs that are added to the group while the group is
    /// paused. That memory is what this answers with — the names of the groups now recorded as
    /// paused, which is not the set of keys that moved: an equality matcher records a group that
    /// holds no job yet.
    /// </remarks>
    /// <returns>The names of the job groups that are recorded as paused by this call.</returns>
    ValueTask<List<string>> PauseJobGroups(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default);

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
    /// Resume (un-pause) the <see cref="ITrigger" />s with the given keys.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If a <see cref="ITrigger" /> missed one or more fire-times, then its misfire instruction
    /// will be applied.
    /// </para>
    /// <para>
    /// The default implementation walks the set one key at a time. A store overrides it to do the
    /// walk inside a single lock or connection scope; the answer must not change when it does.
    /// </para>
    /// </remarks>
    /// <returns>
    /// The keys this call resumed, in the order they were given. A key that names no trigger, and
    /// one that was not paused, are each simply absent — the plural of the single-key
    /// <see langword="bool" />, not a failure.
    /// </returns>
    /// <seealso cref="ResumeTrigger" />
    ValueTask<List<TriggerKey>> ResumeTriggers(
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        return ApplyToEach(triggerKeys, ResumeTrigger, cancellationToken);
    }

    /// <summary>
    /// Resume (un-pause) the trigger groups that match, and every <see cref="ITrigger" /> in them.
    /// <para>
    /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </para>
    /// </summary>
    /// <returns>The names of the trigger groups this call resumed.</returns>
    ValueTask<List<string>> ResumeTriggerGroups(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default);

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
    /// Resume (un-pause) the <see cref="IJob" />s with the given keys.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If any of the jobs' <see cref="ITrigger" />s missed one or more fire-times, then those
    /// triggers' misfire instructions will be applied.
    /// </para>
    /// <para>
    /// The default implementation walks the set one key at a time. A store overrides it to do the
    /// walk inside a single lock or connection scope; the answer must not change when it does.
    /// </para>
    /// </remarks>
    /// <returns>
    /// The keys this call found, in the order they were given — a job with no triggers is found and
    /// so is present. A key that names no job is simply absent — the plural of the single-key
    /// <see langword="bool" />, not a failure.
    /// </returns>
    /// <seealso cref="ResumeJob" />
    ValueTask<List<JobKey>> ResumeJobs(
        IReadOnlyCollection<JobKey> jobKeys,
        CancellationToken cancellationToken = default)
    {
        return ApplyToEach(jobKeys, ResumeJob, cancellationToken);
    }

    /// <summary>
    /// Resume (un-pause) the job groups that match, and the <see cref="IJob" />s in them.
    /// <para>
    /// If any of the <see cref="IJob" /> s had <see cref="ITrigger" /> s that
    /// missed one or more fire-times, then the <see cref="ITrigger" />'s
    /// misfire instruction will be applied.
    /// </para>
    /// </summary>
    /// <returns>The names of the job groups this call resumed.</returns>
    ValueTask<List<string>> ResumeJobGroups(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pause all triggers - equivalent of calling <see cref="PauseTriggerGroups" />
    /// on every group.
    /// <para>
    /// When <see cref="ResumeAll" /> is called (to un-pause), trigger misfire
    /// instructions WILL be applied.
    /// </para>
    /// </summary>
    /// <seealso cref="ResumeAll" />
    ValueTask PauseAll(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resume (un-pause) all triggers - equivalent of calling <see cref="ResumeTriggerGroups" />
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
    /// <remarks>
    /// The returned list stays the store's. <see cref="Quartz.Core.QuartzSchedulerThread" /> copies it
    /// before working with it, because it removes entries from its own copy while it waits out the
    /// first trigger's fire time and would otherwise be editing something the store still holds. A
    /// store is therefore free to hand back a list it keeps a reference to, or to reuse one between
    /// calls; it does not have to build a fresh list to be safe. The copy costs the scheduler around ten
    /// nanoseconds and sixty-four bytes per acquisition attempt (<c>AcquiredTriggerHandoffBenchmark</c>),
    /// against an attempt that is a database round trip, and is kept in preference to a caller-owns rule
    /// that every store would have to keep and that would break silently in one that did not (#3344).
    /// </remarks>
    /// <param name="request">What to acquire: the cut-off time, how many, the batching window
    /// and the per-execution-group capacity still available.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// The acquired triggers. The caller copies this list rather than taking it over, so it may be one
    /// the store keeps.
    /// </returns>
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

    /// <summary>
    /// Walks a key set through a single-key operation, collecting the keys it applied to.
    /// </summary>
    /// <remarks>
    /// This is what every key-set member falls back to when a store has not overridden it: correct
    /// for any store, but one lock or round trip per key. A store that can do the whole set in one
    /// pass overrides the member and keeps this answer.
    /// </remarks>
    private static async ValueTask<List<TKey>> ApplyToEach<TKey>(
        IReadOnlyCollection<TKey> keys,
        Func<TKey, CancellationToken, ValueTask<bool>> apply,
        CancellationToken cancellationToken)
    {
        List<TKey> applied = new List<TKey>(keys.Count);
        foreach (TKey key in keys)
        {
            if (await apply(key, cancellationToken).ConfigureAwait(false))
            {
                applied.Add(key);
            }
        }

        return applied;
    }

    /// <summary>
    /// Lists the jobs in the matching groups and deletes them, for a store that has not overridden
    /// the group form.
    /// </summary>
    /// <remarks>
    /// Correct for any store and atomic for none: the listing and the deletion are two operations,
    /// so a job stored between them is not deleted. Every shipped store resolves the keys inside
    /// the same lock the deletion takes, which is the difference this default cannot express.
    /// </remarks>
    private static async ValueTask<List<JobKey>> DeleteMatchingJobs(
        IJobStore store,
        GroupMatcher<JobKey> matcher,
        CancellationToken cancellationToken)
    {
        PagedResult<JobHeader> matching = await store.QueryJobs(
            new JobQuery { Group = matcher, Take = PagedQuery.All },
            cancellationToken).ConfigureAwait(false);

        List<JobKey> keys = new List<JobKey>(matching.Items.Count);
        foreach (JobHeader header in matching.Items)
        {
            keys.Add(header.Key);
        }

        return await store.DeleteJobs(keys, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="DeleteMatchingJobs" />
    private static async ValueTask<List<TriggerKey>> DeleteMatchingTriggers(
        IJobStore store,
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken)
    {
        PagedResult<TriggerHeader> matching = await store.QueryTriggers(
            new TriggerQuery { Group = matcher, Take = PagedQuery.All },
            cancellationToken).ConfigureAwait(false);

        List<TriggerKey> keys = new List<TriggerKey>(matching.Items.Count);
        foreach (TriggerHeader header in matching.Items)
        {
            keys.Add(header.Key);
        }

        return await store.DeleteTriggers(keys, cancellationToken).ConfigureAwait(false);
    }
}