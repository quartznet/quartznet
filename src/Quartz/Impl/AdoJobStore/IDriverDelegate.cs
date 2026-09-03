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

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// This is the base interface for all driver delegate classes.
/// </summary>
/// <remarks>
/// <para>
/// This interface is very similar to the <see cref="IJobStore" />
/// interface except each method has an additional <see cref="ConnectionAndTransactionHolder" />
/// parameter.
/// </para>
/// <para>
/// Unless a database driver has some <strong>extremely-DB-specific</strong>
/// requirements, any IDriverDelegate implementation classes should extend the
/// <see cref="StdAdoDelegate" /> class.
/// </para>
/// </remarks>
/// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public interface IDriverDelegate
{
    /// <summary>
    /// Initializes the driver delegate with the settings it works from.
    /// </summary>
    /// <remarks>
    /// Called once by the job store before the delegate is used. There is no default implementation:
    /// a delegate that does not read the context has no table prefix, provider or serializer, and
    /// would fail at its first statement rather than at startup.
    /// </remarks>
    /// <param name="context">The settings the store was configured with.</param>
    void Initialize(DriverDelegateContext context);

    /// <summary>
    /// Update all triggers having one of the given states, to the given new state.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="newState">The new state for the triggers</param>
    /// <param name="oldStates">The states a trigger must be in to be updated. Must not be empty.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>Number of rows updated</returns>
    ValueTask<int> UpdateTriggerStatesFromOtherStates(
        ConnectionAndTransactionHolder conn,
        StoredTriggerState newState,
        IReadOnlyCollection<StoredTriggerState> oldStates,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Select all of the triggers for jobs that are requesting recovery. The
    /// returned trigger objects will have unique "recoverXXX" trigger names and
    /// will be in the <see cref="SchedulerConstants.DefaultRecoveryGroup" /> trigger group.
    /// </summary>
    /// <remarks>
    /// In order to preserve the ordering of the triggers, the fire time will be
    /// set from the <i>ColumnFiredTime</i> column in the <i>TableFiredTriggers</i>
    /// table. The caller is responsible for calling <see cref="IOperableTrigger.ComputeFirstFireTimeUtc" />
    /// on each returned trigger. It is also up to the caller to insert the
    /// returned triggers to ensure that they are fired.
    /// </remarks>
    /// <param name="conn">The DB Connection</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>An array of <see cref="ITrigger" /> objects</returns>
    ValueTask<List<IOperableTrigger>> SelectTriggersForRecoveringJobs(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the fired triggers the query selects. A query with no filter set deletes all of them.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="query">Which fired triggers to delete.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The number of rows deleted</returns>
    ValueTask<int> DeleteFiredTriggers(
        ConnectionAndTransactionHolder conn,
        FiredTriggerQuery query,
        CancellationToken cancellationToken = default);

    //---------------------------------------------------------------------------
    // jobs
    //---------------------------------------------------------------------------

    /// <summary>
    /// Insert the job detail record.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="job">The job to insert.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>Number of rows inserted.</returns>
    ValueTask<int> InsertJobDetail(
        ConnectionAndTransactionHolder conn,
        IJobDetail job,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the job detail record.
    /// </summary>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="job">The job to update.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>Number of rows updated.</returns>
    ValueTask<int> UpdateJobDetail(
        ConnectionAndTransactionHolder conn,
        IJobDetail job,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the keys of all the triggers associated with the given job.
    /// </summary>
    /// <remarks>
    /// Not a listing — <see cref="SelectTriggerHeaders" /> with a job filter is. This one serves the
    /// pause/resume and removal mutation paths, which need every key of the job in one go so that they
    /// can update them under the same lock, and which therefore must not be paged.
    /// </remarks>
    /// <param name="conn">The DB Connection</param>
    /// <param name="jobKey">The key identifying the job.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<List<TriggerKey>> SelectTriggerKeysForJob(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the keys of the triggers of the given job that are in the given state.
    /// </summary>
    /// <remarks>
    /// The filtered form of the overload above, for the callers that want the state rather than the
    /// trigger — completion bookkeeping asking which of a job's triggers it has just unblocked, where
    /// loading each trigger and then asking the database for its state one at a time is a read per
    /// trigger for an answer one read can give.
    /// </remarks>
    /// <param name="conn">The DB Connection</param>
    /// <param name="jobKey">The key identifying the job.</param>
    /// <param name="state">The state the triggers must be in.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<List<TriggerKey>> SelectTriggerKeysForJob(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        StoredTriggerState state,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the keys of every trigger belonging to any of the given jobs.
    /// </summary>
    /// <remarks>
    /// The job-set form of the first overload above, for pausing and resuming a whole matcher's worth
    /// of jobs: what those paths want is one flat set of trigger keys, and reading it a job at a time
    /// is a statement per job for an answer one statement can give. The default implementation is that
    /// per-job loop.
    /// </remarks>
    /// <param name="conn">The DB Connection</param>
    /// <param name="jobKeys">The keys identifying the jobs. May be empty.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    async ValueTask<List<TriggerKey>> SelectTriggerKeysForJobs(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<JobKey> jobKeys,
        CancellationToken cancellationToken = default)
    {
        List<TriggerKey> keys = [];
        foreach (JobKey jobKey in jobKeys)
        {
            keys.AddRange(await SelectTriggerKeysForJob(conn, jobKey, cancellationToken).ConfigureAwait(false));
        }

        return keys;
    }

    /// <summary>
    /// Delete the job detail record for the given job.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="jobKey">The key identifying the job.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>the number of rows deleted</returns>
    ValueTask<int> DeleteJobDetail(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check whether or not the given job exists.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="jobKey">The key identifying the job.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if the job exists, false otherwise</returns>
    ValueTask<bool> JobExists(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the job data map for the given job.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="job">The job.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>the number of rows updated</returns>
    ValueTask<int> UpdateJobData(
        ConnectionAndTransactionHolder conn,
        IJobDetail job,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Select the JobDetail object for a given job name / group name.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="jobKey">The key identifying the job.</param>
    /// <param name="typeLoader">The type loader.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The populated JobDetail object</returns>
    ValueTask<IJobDetail?> SelectJobDetail(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        ITypeLoader typeLoader,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Select the keys of all the jobs a group matcher selects.
    /// </summary>
    /// <remarks>
    /// Not a listing — <see cref="SelectJobHeaders" /> is. This one serves the mutation paths, which
    /// need every matching key in one go so that they can update them under the same lock, and which
    /// therefore must not be paged.
    /// </remarks>
    /// <param name="conn">The DB Connection </param>
    /// <param name="matcher">Criteria for matching groups.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The keys of the matching jobs.</returns>
    ValueTask<List<JobKey>> SelectJobKeysInGroup(ConnectionAndTransactionHolder conn, GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default);

    //---------------------------------------------------------------------------
    // triggers
    //---------------------------------------------------------------------------

    /// <summary>
    /// Insert the base trigger data.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="trigger">The trigger to insert.</param>
    /// <param name="state">The state that the trigger should be stored in.</param>
    /// <param name="jobDetail">The job detail.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The number of rows inserted</returns>
    ValueTask<int> InsertTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        StoredTriggerState state,
        IJobDetail jobDetail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Insert the blob trigger data.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="trigger">The trigger to insert</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The number of rows inserted</returns>
    ValueTask<int> InsertBlobTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the base trigger data.
    /// </summary>
    /// <param name="conn">the DB Connection</param>
    /// <param name="trigger">The trigger.</param>
    /// <param name="state">The state.</param>
    /// <param name="jobDetail">The job detail.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>the number of rows updated</returns>
    ValueTask<int> UpdateTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        StoredTriggerState state,
        IJobDetail jobDetail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the blob trigger data.
    /// </summary>
    /// <param name="conn">the DB Connection</param>
    /// <param name="trigger">The trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>the number of rows updated</returns>
    ValueTask<int> UpdateBlobTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check whether or not a trigger exists.
    /// </summary>
    /// <param name="conn">the DB Connection</param>
    /// <param name="triggerKey">The key identifying the trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>the number of rows updated</returns>
    ValueTask<bool> TriggerExists(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the state for a given trigger.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="triggerKey">The key identifying the trigger.</param>
    /// <param name="state">The new state for the trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns> the number of rows updated</returns>
    ValueTask<int> UpdateTriggerState(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        StoredTriggerState state,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the given trigger to the given new state, if it is in the given
    /// old state.
    /// </summary>
    /// <param name="conn">The DB connection</param>
    /// <param name="triggerKey">The key identifying the trigger.</param>
    /// <param name="newState">The new state for the trigger </param>
    /// <param name="oldState">The old state the trigger must be in</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns> int the number of rows updated</returns>
    ValueTask<int> UpdateTriggerStateFromOtherState(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        StoredTriggerState newState,
        StoredTriggerState oldState,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the given trigger to the given new state, if it is one of the
    /// given old states.
    /// </summary>
    /// <param name="conn">The DB connection</param>
    /// <param name="triggerKey">The key identifying the trigger.</param>
    /// <param name="newState">The new state for the trigger</param>
    /// <param name="oldStates">The states the trigger must be in to be updated. Must not be empty.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns> int the number of rows updated
    /// </returns>
    ValueTask<int> UpdateTriggerStateFromOtherStates(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        StoredTriggerState newState,
        IReadOnlyCollection<StoredTriggerState> oldStates,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a whole set of triggers to the given new state, each one only if it is in one of the given
    /// old states.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The key-set form of the member above, for the paths that decide one transition for a set of keys
    /// and then have no reason to issue it a key at a time: pause, resume, and cluster recovery
    /// releasing every trigger a dead node had reserved. A set of one old state is how a caller that
    /// wants exactly one asks for it.
    /// </para>
    /// <para>
    /// The default implementation is the per-key loop, so a delegate that says nothing about key sets
    /// keeps behaving as it did. <see cref="StdAdoDelegate" /> issues one <c>UPDATE</c> with a key-set
    /// predicate per chunk, so the row count is the database's own and not a tally of what was asked
    /// about.
    /// </para>
    /// </remarks>
    /// <param name="conn">The DB connection</param>
    /// <param name="triggerKeys">The keys identifying the triggers. May be empty.</param>
    /// <param name="newState">The new state for the triggers</param>
    /// <param name="oldStates">The states a trigger must be in to be updated. Must not be empty.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The number of rows updated.</returns>
    async ValueTask<int> UpdateTriggerStatesFromOtherStates(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<TriggerKey> triggerKeys,
        StoredTriggerState newState,
        IReadOnlyCollection<StoredTriggerState> oldStates,
        CancellationToken cancellationToken = default)
    {
        int updated = 0;
        foreach (TriggerKey triggerKey in triggerKeys)
        {
            updated += await UpdateTriggerStateFromOtherStates(conn, triggerKey, newState, oldStates, cancellationToken).ConfigureAwait(false);
        }

        return updated;
    }

    /// <summary>
    /// Update the given trigger to the given new state, if it is in the given
    /// old state and has the given next fire time.
    /// </summary>
    /// <param name="conn">The DB connection</param>
    /// <param name="triggerKey">The key identifying the trigger.</param>
    /// <param name="newState">The new state for the trigger </param>
    /// <param name="oldState">The old state the trigger must be in</param>
    /// <param name="nextFireTime">The next fire time the trigger must have</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns> int the number of rows updated</returns>
    ValueTask<int> UpdateTriggerStateFromOtherStateWithNextFireTime(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        StoredTriggerState newState,
        StoredTriggerState oldState,
        DateTimeOffset nextFireTime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update all triggers the group matcher selects to the given new state, if they are in one of the
    /// given old states.
    /// </summary>
    /// <remarks>
    /// Matcher-based rather than query-based on purpose: this is the pause/resume mutation path, which
    /// has to move every matching trigger under one lock and therefore cannot be paged.
    /// </remarks>
    /// <param name="conn">The DB connection</param>
    /// <param name="matcher">Criteria for matching groups.</param>
    /// <param name="newState">The new state for the trigger</param>
    /// <param name="oldStates">The states a trigger must be in to be updated. Must not be empty.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The number of rows updated</returns>
    ValueTask<int> UpdateTriggerGroupStateFromOtherStates(
        ConnectionAndTransactionHolder conn,
        GroupMatcher<TriggerKey> matcher,
        StoredTriggerState newState,
        IReadOnlyCollection<StoredTriggerState> oldStates,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update all of the triggers the group matcher selects to the given new state, if they are in the
    /// given old state.
    /// </summary>
    /// <remarks>
    /// Matcher-based rather than query-based on purpose: this is the pause/resume mutation path, which
    /// has to move every matching trigger under one lock and therefore cannot be paged.
    /// </remarks>
    /// <param name="conn">The DB connection</param>
    /// <param name="matcher">Criteria for matching groups.</param>
    /// <param name="newState">The new state for the trigger group</param>
    /// <param name="oldState">The old state the triggers must be in.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns> int the number of rows updated</returns>
    ValueTask<int> UpdateTriggerGroupStateFromOtherState(
        ConnectionAndTransactionHolder conn,
        GroupMatcher<TriggerKey> matcher,
        StoredTriggerState newState,
        StoredTriggerState oldState,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the states of all triggers associated with the given job.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="jobKey">The key identifying the job.</param>
    /// <param name="state">The new state for the triggers.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The number of rows updated</returns>
    ValueTask<int> UpdateTriggerStatesForJob(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        StoredTriggerState state,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the states of any triggers associated with the given job, that
    /// are the given current state.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="jobKey">The key identifying the job.</param>
    /// <param name="newState">The new state for the triggers</param>
    /// <param name="oldState">The old state of the triggers</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns> the number of rows updated</returns>
    ValueTask<int> UpdateTriggerStatesForJobFromOtherState(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        StoredTriggerState newState,
        StoredTriggerState oldState,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply the same conditional state change to the triggers of each of several jobs, in as few round
    /// trips as the provider allows.
    /// </summary>
    /// <remarks>
    /// The same statement as the single-job overload, once per job key, issued as one
    /// <see cref="System.Data.Common.DbBatch" /> where the provider supports batching. Cluster recovery
    /// unblocks the siblings of every interrupted execution, which is one or two of these per
    /// fired-trigger row when they travel separately — and the same job over and over when a dead node
    /// left several rows behind. No row count comes back: a batch does not report one per command in
    /// any portable way, and the caller counts the rows it asked about rather than the rows that moved.
    /// </remarks>
    /// <param name="conn">The DB Connection</param>
    /// <param name="jobKeys">The keys identifying the jobs. An empty collection does nothing.</param>
    /// <param name="newState">The new state for the triggers</param>
    /// <param name="oldState">The old state a trigger must be in to be updated</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask UpdateTriggerStatesForJobsFromOtherState(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<JobKey> jobKeys,
        StoredTriggerState newState,
        StoredTriggerState oldState,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply several conditional state changes to a job's triggers in as few round trips as the
    /// provider allows.
    /// </summary>
    /// <remarks>
    /// The transitions are applied in the order given, so a set of them that overlaps means what
    /// issuing them one after another meant. Blocking and unblocking a job's triggers is always two or
    /// three of these at once, which is two or three round trips inside the trigger-access lock when
    /// they travel separately.
    /// </remarks>
    /// <param name="conn">The DB Connection</param>
    /// <param name="jobKey">The key identifying the job.</param>
    /// <param name="transitions">The state changes to apply, in order.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask UpdateTriggerStatesForJobFromOtherState(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        IReadOnlyList<TriggerStateTransition> transitions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply every row change one trigger fire makes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fire path has read everything it decides on before it writes anything, so what is left is a
    /// run of writes with no read between them: the fired-trigger row, the misfire original fire time
    /// where one was recorded, the sibling triggers of a job that disallows concurrent execution, the
    /// trigger's own row, and its schedule in whichever type table holds it. They go out as one
    /// <see cref="System.Data.Common.DbBatch" /> where the provider supports batching, and one command
    /// at a time where it does not.
    /// </para>
    /// <para>
    /// All of it is one transaction either way, and a batch executes its commands in sequence, so the
    /// order the writes were issued in is the order they still happen in.
    /// </para>
    /// </remarks>
    /// <param name="conn">The DB Connection</param>
    /// <param name="update">The changes the fire makes.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask ApplyTriggerFired(
        ConnectionAndTransactionHolder conn,
        TriggerFiredUpdate update,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete the BLOB trigger data for a trigger.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="triggerKey">The key identifying the trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The number of rows deleted</returns>
    ValueTask<int> DeleteBlobTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete the base trigger data for a trigger.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="triggerKey">The key identifying the trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns> the number of rows deleted </returns>
    ValueTask<int> DeleteTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Select the number of triggers associated with a given job.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="jobKey">The key identifying the job.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns> the number of triggers for the given job </returns>
    ValueTask<int> CountTriggersForJob(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Select the job to which the trigger is associated.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="triggerKey">The key identifying the trigger.</param>
    /// <param name="typeLoader">The type loader.</param>
    /// <param name="loadJobType">
    /// Whether to load the job's actual type. Removal does not need it, and in many cases the type no
    /// longer exists by then, so removal passes <c>false</c> and gets a job detail carrying only the
    /// recorded type name.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<IJobDetail?> SelectJobForTrigger(ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        ITypeLoader typeLoader,
        bool loadJobType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Select the triggers for a job>
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="jobKey">The key identifying the job.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns> an array of <see cref="ITrigger" /> objects associated with a given job. </returns>
    ValueTask<List<IOperableTrigger>> SelectTriggersForJob(ConnectionAndTransactionHolder conn, JobKey jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Select the triggers for a calendar
    /// </summary>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="calendarName">Name of the calendar.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// An array of <see cref="ITrigger" /> objects associated with a given job.
    /// </returns>
    ValueTask<List<IOperableTrigger>> SelectTriggersForCalendar(ConnectionAndTransactionHolder conn, string calendarName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Select a trigger.
    /// </summary>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="triggerKey">The key identifying the trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The <see cref="ITrigger" /> object.
    /// </returns>
    ValueTask<IOperableTrigger?> SelectTrigger(ConnectionAndTransactionHolder conn, TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Select a trigger's JobDataMap.
    /// </summary>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="triggerKey">The key identifying the trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The <see cref="JobDataMap" /> of the Trigger, never null, but possibly empty.</returns>
    ValueTask<JobDataMap> SelectTriggerJobDataMap(ConnectionAndTransactionHolder conn, TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Select a trigger's state value.
    /// </summary>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="triggerKey">The key identifying the trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// The trigger's stored state, or <see cref="StoredTriggerState.Deleted" /> when no such trigger
    /// exists.
    /// </returns>
    ValueTask<StoredTriggerState> SelectTriggerState(ConnectionAndTransactionHolder conn, TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Select the stored state, next fire time and job of one trigger, in a single statement.
    /// </summary>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="triggerKey">The key identifying the trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// The trigger's header, or <see langword="null" /> when no such trigger exists.
    /// </returns>
    ValueTask<StoredTriggerHeader?> SelectTriggerHeader(ConnectionAndTransactionHolder conn, TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects the stored headers of a whole set of triggers, chunking the keys into as few statements
    /// as the provider's parameter ceiling allows. Keys that have no row are simply absent from the
    /// result.
    /// </summary>
    /// <remarks>
    /// The plural of <see cref="SelectTriggerHeader" /> — the storage-side header a state transition
    /// decides on, not the listing entry <see cref="SelectTriggerHeaders" /> pages over. Pause and
    /// resume read one of these per key and then decide the whole set's transitions at once; the
    /// default implementation is the per-key loop they used to make.
    /// </remarks>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="triggerKeys">The keys identifying the triggers. May be empty.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    async ValueTask<List<StoredTriggerHeader>> SelectStoredTriggerHeaders(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        List<StoredTriggerHeader> headers = new(triggerKeys.Count);
        foreach (TriggerKey triggerKey in triggerKeys)
        {
            StoredTriggerHeader? header = await SelectTriggerHeader(conn, triggerKey, cancellationToken).ConfigureAwait(false);
            if (header is not null)
            {
                headers.Add(header);
            }
        }

        return headers;
    }

    /// <summary>
    /// Select all trigger group names a group matcher selects. Pass
    /// <see cref="GroupMatcher{TKey}.AnyGroup" /> for every group.
    /// </summary>
    /// <remarks>
    /// Not a listing — <see cref="SelectTriggerGroups" /> with a <see cref="TriggerGroupQuery" /> is.
    /// This one serves the pause/resume mutation paths, which need every matching group in one go so
    /// that they can update them under the same lock, and which therefore must not be paged.
    /// </remarks>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="matcher">The matcher to apply for searching.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The names of the matching groups.</returns>
    ValueTask<List<string>> SelectTriggerGroupNames(ConnectionAndTransactionHolder conn, GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default);

    /// <summary>
    /// Select the keys of all the triggers a group matcher selects.
    /// </summary>
    /// <remarks>
    /// Not a listing — <see cref="SelectTriggerHeaders" /> is. This one serves the pause/resume
    /// mutation paths, which need every matching key in one go so that they can update them under the
    /// same lock, and which therefore must not be paged.
    /// </remarks>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="matcher">Criteria for matching groups.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The keys of the matching triggers.</returns>
    ValueTask<List<TriggerKey>> SelectTriggerKeysInGroup(ConnectionAndTransactionHolder conn, GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default);

    /// <summary>
    /// Select all the triggers in a given state.
    /// </summary>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="state">The state the triggers must be in.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>An array of trigger <see cref="TriggerKey" />s.</returns>
    ValueTask<List<TriggerKey>> SelectTriggersInState(ConnectionAndTransactionHolder conn, StoredTriggerState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Select which of the given triggers are in the given state.
    /// </summary>
    /// <remarks>
    /// The set-taking form of <see cref="SelectTriggerState" />, for a caller that already holds the
    /// keys — cluster recovery asking which of a failed node's triggers ran to COMPLETE, where a state
    /// per key is a round trip per key for an answer one read gives. A key with no row, or with a row in
    /// another state, is simply absent from the result.
    /// </remarks>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="triggerKeys">The keys to ask about.</param>
    /// <param name="state">The state the triggers must be in.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The keys of those triggers that are in the state.</returns>
    ValueTask<List<TriggerKey>> SelectTriggerKeysInState(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<TriggerKey> triggerKeys,
        StoredTriggerState state,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts the paused trigger group.
    /// </summary>
    /// <param name="conn">The conn.</param>
    /// <param name="groupName">Name of the group.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<int> InsertPausedTriggerGroup(
        ConnectionAndTransactionHolder conn,
        string groupName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the paused trigger groups the matcher selects. A single group is
    /// <see cref="GroupMatcher{TKey}.GroupEquals" />.
    /// </summary>
    /// <param name="conn">The database connection.</param>
    /// <param name="matcher">Criteria for matching groups.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<int> DeletePausedTriggerGroup(
        ConnectionAndTransactionHolder conn,
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether the specified trigger group is paused.
    /// </summary>
    /// <param name="conn">The conn.</param>
    /// <param name="groupName">Name of the group.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// 	<c>true</c> if trigger group is paused; otherwise, <c>false</c>.
    /// </returns>
    ValueTask<bool> IsTriggerGroupPaused(
        ConnectionAndTransactionHolder conn,
        string groupName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a job group is paused.
    /// </summary>
    /// <remarks>
    /// The row is what makes the pause outlive the process and reach the other nodes of a cluster,
    /// and what a caller adding a job to the group later reads. The table's primary key rejects a
    /// duplicate, so callers check first — under the trigger-access lock, which is what keeps two
    /// nodes pausing the same group from racing.
    /// </remarks>
    /// <param name="conn">The database connection.</param>
    /// <param name="groupName">Name of the group.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The number of rows inserted.</returns>
    ValueTask<int> InsertPausedJobGroup(
        ConnectionAndTransactionHolder conn,
        string groupName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the paused job groups the matcher selects. A single group is
    /// <see cref="GroupMatcher{TKey}.GroupEquals" />.
    /// </summary>
    /// <param name="conn">The database connection.</param>
    /// <param name="matcher">Criteria for matching groups.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The number of rows deleted.</returns>
    ValueTask<int> DeletePausedJobGroup(
        ConnectionAndTransactionHolder conn,
        GroupMatcher<JobKey> matcher,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether the specified job group is paused.
    /// </summary>
    /// <param name="conn">The database connection.</param>
    /// <param name="groupName">Name of the group.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// 	<c>true</c> if the job group is paused; otherwise, <c>false</c>.
    /// </returns>
    ValueTask<bool> IsJobGroupPaused(
        ConnectionAndTransactionHolder conn,
        string groupName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Of the given job groups, which already have a paused row.
    /// </summary>
    /// <remarks>
    /// The set form of <see cref="IsJobGroupPaused" />, which is what pausing a matcher's worth of job
    /// groups actually asks: it needs the groups that do <em>not</em> have a row, so that it can write
    /// exactly those. The default implementation asks one group at a time, as that path used to.
    /// </remarks>
    /// <param name="conn">The database connection.</param>
    /// <param name="groupNames">The groups to ask about. May be empty.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The subset of <paramref name="groupNames" /> that is paused.</returns>
    async ValueTask<List<string>> SelectPausedJobGroups(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<string> groupNames,
        CancellationToken cancellationToken = default)
    {
        List<string> paused = [];
        foreach (string groupName in groupNames)
        {
            if (await IsJobGroupPaused(conn, groupName, cancellationToken).ConfigureAwait(false))
            {
                paused.Add(groupName);
            }
        }

        return paused;
    }

    /// <summary>
    /// Writes a paused row for each of the given job groups, in as few round trips as the provider
    /// allows.
    /// </summary>
    /// <remarks>
    /// Every group is expected to have no row yet — the caller established that with
    /// <see cref="SelectPausedJobGroups" /> under the same lock — so this is a plain insert per group
    /// rather than an upsert. The default implementation issues them one at a time.
    /// </remarks>
    /// <param name="conn">The database connection.</param>
    /// <param name="groupNames">The groups to pause. May be empty.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    async ValueTask InsertPausedJobGroups(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<string> groupNames,
        CancellationToken cancellationToken = default)
    {
        foreach (string groupName in groupNames)
        {
            await InsertPausedJobGroup(conn, groupName, cancellationToken).ConfigureAwait(false);
        }
    }

    //---------------------------------------------------------------------------
    // calendars
    //---------------------------------------------------------------------------

    /// <summary>
    /// Insert a new calendar.
    /// </summary>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="calendarName">The name for the new calendar.</param>
    /// <param name="calendar">The calendar.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The number of rows inserted.</returns>
    ValueTask<int> InsertCalendar(
        ConnectionAndTransactionHolder conn,
        string calendarName,
        ICalendar calendar,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a calendar.
    /// </summary>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="calendarName">The name for the new calendar.</param>
    /// <param name="calendar">The calendar.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The number of rows updated.</returns>
    ValueTask<int> UpdateCalendar(
        ConnectionAndTransactionHolder conn,
        string calendarName,
        ICalendar calendar,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check whether or not a calendar exists.
    /// </summary>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="calendarName">The name of the calendar.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if the trigger exists, false otherwise.</returns>
    ValueTask<bool> CalendarExists(
        ConnectionAndTransactionHolder conn,
        string calendarName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Select a calendar.
    /// </summary>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="calendarName">The name of the calendar.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The Calendar.</returns>
    ValueTask<ICalendar?> SelectCalendar(ConnectionAndTransactionHolder conn,
        string calendarName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check whether or not a calendar is referenced by any triggers.
    /// </summary>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="calendarName">The name of the calendar.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if any triggers reference the calendar, false otherwise</returns>
    ValueTask<bool> CalendarIsReferenced(
        ConnectionAndTransactionHolder conn,
        string calendarName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a calendar.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="calendarName">The name of the trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The number of rows deleted.</returns>
    ValueTask<int> DeleteCalendar(
        ConnectionAndTransactionHolder conn,
        string calendarName,
        CancellationToken cancellationToken = default);

    //---------------------------------------------------------------------------
    // trigger firing
    //---------------------------------------------------------------------------

    /// <summary>
    /// Insert a fired trigger.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="trigger">The trigger.</param>
    /// <param name="state">The state that the trigger should be stored in.</param>
    /// <param name="jobDetail">The job detail.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The number of rows inserted.</returns>
    ValueTask<int> InsertFiredTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        StoredTriggerState state,
        IJobDetail? jobDetail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts the fired-trigger rows of one acquisition round in as few round trips as the provider
    /// allows.
    /// </summary>
    /// <remarks>
    /// The rows are all in the same state and belong to the same job detail, which is what an
    /// acquisition round produces: every row goes in as <see cref="StoredTriggerState.Acquired" />
    /// with no job named yet. The default implementation is the loop the store used to make itself,
    /// so a delegate that says nothing about batching keeps behaving exactly as it did.
    /// </remarks>
    /// <param name="conn">The DB Connection</param>
    /// <param name="triggers">The triggers to record, in the order they were acquired.</param>
    /// <param name="state">The state that the triggers should be stored in.</param>
    /// <param name="jobDetail">The job detail, or <see langword="null" /> when no job is named yet.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    async ValueTask InsertFiredTriggers(
        ConnectionAndTransactionHolder conn,
        IReadOnlyList<IOperableTrigger> triggers,
        StoredTriggerState state,
        IJobDetail? jobDetail,
        CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < triggers.Count; i++)
        {
            await InsertFiredTrigger(conn, triggers[i], state, jobDetail, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Selects the states of the fired-trigger records the query selects. A query with no filter set
    /// selects all of them.
    /// </summary>
    /// <remarks>
    /// Not a listing — <see cref="SelectFireInstances" /> is. This one is the whole set, every column,
    /// for the maintenance passes that have to see all of it.
    /// </remarks>
    /// <param name="conn">The DB Connection</param>
    /// <param name="query">Which fired triggers to select.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>A list of FiredTriggerRecord objects.</returns>
    ValueTask<List<FiredTriggerRecord>> SelectFiredTriggerRecords(
        ConnectionAndTransactionHolder conn,
        FiredTriggerQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a job is currently being executed (has a fired trigger in EXECUTING state).
    /// Used to enforce <see cref="DisallowConcurrentExecutionAttribute"/> across cluster nodes.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="jobKey">The key identifying the job.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<bool> IsJobCurrentlyExecuting(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects a trigger's stored state together with whether it currently has an execution in flight.
    /// </summary>
    /// <returns>
    /// The trigger's state and execution, or <see cref="TriggerExecutionState.NotFound" /> when no such
    /// trigger exists.
    /// </returns>
    ValueTask<TriggerExecutionState> SelectTriggerStateWithExecuting(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a fired trigger.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="entryId">The fired trigger entry to delete.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The number of rows deleted.</returns>
    ValueTask<int> DeleteFiredTrigger(
        ConnectionAndTransactionHolder conn,
        string entryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete several fired triggers by entry id, in as few round trips as the provider allows.
    /// </summary>
    /// <remarks>
    /// The same statement as the single-entry overload, once per id, issued as one
    /// <see cref="System.Data.Common.DbBatch" /> where the provider supports batching. This is the shape
    /// cluster recovery needs when it is clearing a dead node's rows but holding some of them back: the
    /// whole-instance <see cref="DeleteFiredTriggers(ConnectionAndTransactionHolder, FiredTriggerQuery, CancellationToken)" />
    /// would take the preserved rows with it. No row count comes back, for the reason
    /// <see cref="UpdateTriggerStatesForJobsFromOtherState" /> gives.
    /// </remarks>
    /// <param name="conn">The DB Connection</param>
    /// <param name="entryIds">The fired trigger entries to delete. An empty collection does nothing.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask DeleteFiredTriggers(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<string> entryIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Insert a scheduler-instance state record.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="instanceId">The instance id.</param>
    /// <param name="checkInTime">The check in time.</param>
    /// <param name="interval">The interval.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The number of inserted rows.</returns>
    ValueTask<int> InsertSchedulerState(
        ConnectionAndTransactionHolder conn,
        string instanceId,
        DateTimeOffset checkInTime,
        TimeSpan interval,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a scheduler-instance state record.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="instanceId">The instance id.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The number of deleted rows.</returns>
    ValueTask<int> DeleteSchedulerState(
        ConnectionAndTransactionHolder conn,
        string instanceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a scheduler-instance state record.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="instanceId">The instance id.</param>
    /// <param name="checkInTime">The check in time.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The number of updated rows.</returns>
    ValueTask<int> UpdateSchedulerState(
        ConnectionAndTransactionHolder conn,
        string instanceId,
        DateTimeOffset checkInTime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A List of all current <see cref="SchedulerStateRecord" />s.
    /// <para>
    /// If instanceId is not null, then only the record for the identified
    /// instance will be returned.
    /// </para>
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="instanceId">The instance id, or <see langword="null" /> for every instance.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<List<SchedulerStateRecord>> SelectSchedulerStateRecords(
        ConnectionAndTransactionHolder conn,
        string? instanceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects the next triggers to fire, in ascending order of fire time and then descending by
    /// priority.
    /// </summary>
    /// <param name="conn">The DB connection.</param>
    /// <param name="criteria">What to acquire, and how much of it.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>A (never null, possibly empty) list of the next triggers to be fired.</returns>
    ValueTask<List<TriggerAcquireResult>> SelectTriggersToAcquire(
        ConnectionAndTransactionHolder conn,
        TriggerAcquisitionCriteria criteria,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether <see cref="SelectTriggersToAcquire" /> already keeps
    /// <see cref="TriggerAcquisitionCriteria.ExcludedJobTypeNames" /> out of the rows it returns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other property of <see cref="TriggerAcquisitionCriteria" /> may be ignored by a delegate
    /// with no worse consequence than a wider result than asked for. This one is different: ignoring it
    /// means the node <em>runs</em> job types the deployment excluded. So the ADO store keeps a
    /// backstop — it drops an excluded candidate ordinally on
    /// <see cref="TriggerAcquireResult.JobTypeName" /> before reading the trigger back — and this
    /// property is how a delegate says the backstop has nothing left to do.
    /// </para>
    /// <para>
    /// It defaults to <see langword="false" />, because a delegate written before the exclusions
    /// existed cannot have honoured them. <see cref="StdAdoDelegate" /> answers
    /// <see langword="true" />: its acquisition statement carries the <c>NOT IN</c> clause, and every
    /// dialect Quartz ships builds its statement from that one. A delegate of somebody's own that
    /// replaces the acquisition statement without the clause — or overrides
    /// <c>StdAdoDelegate.GetSelectNextTriggerToAcquireSql</c> to drop it — must answer
    /// <see langword="false" />, or the exclusions stop being enforced anywhere.
    /// </para>
    /// </remarks>
    bool FiltersAcquisitionJobTypeExclusions => false;

    /// <summary>
    /// Counts what the cluster currently holds in flight for each (execution group, trigger group)
    /// pair, which is what a <see cref="ExecutionLimitScope.Cluster" /> execution limit is counted
    /// against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The answer is an aggregate over the fired-triggers table, whose rows already have exactly the
    /// lifetime a reservation needs: written when a trigger is acquired, updated when it fires, deleted
    /// when it completes or when cluster recovery cleans up after the node that owned it. So the count
    /// includes a peer's reservation that has not started yet, and a dead node's rows keep holding
    /// slots until recovery clears them — under-serving the quota rather than over-serving it, which is
    /// the safe direction.
    /// </para>
    /// <para>
    /// Both group names are returned because the limits derive their key from the pair; see
    /// <see cref="ExecutionGroupInFlight" />. Rows are bounded by the distinct pairs currently in
    /// flight, not by the size of the schedule.
    /// </para>
    /// </remarks>
    /// <param name="conn">The DB connection.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>One entry per distinct pair with work in flight; empty when the cluster is idle.</returns>
    ValueTask<List<ExecutionGroupInFlight>> SelectExecutionGroupsInFlight(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Select the distinct instance names of all fired-trigger records.
    /// </summary>
    /// <remarks>
    /// This is useful when trying to identify orphaned fired triggers (a
    /// fired trigger without a scheduler state record.)
    /// </remarks>
    /// <param name="conn">The conn.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<List<string>> SelectFiredTriggerInstanceNames(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the triggers in the given state that missed their scheduled fire time.
    /// </summary>
    /// <param name="conn">The DB connection.</param>
    /// <param name="state">The trigger state to scan.</param>
    /// <param name="misfireTime">Triggers whose next fire time is at or before this are misfired.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<int> CountMisfiredTriggersInState(
        ConnectionAndTransactionHolder conn,
        StoredTriggerState state,
        DateTimeOffset misfireTime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear (delete!) all scheduling data - all <see cref="IJob"/>s, <see cref="ITrigger" />s
    /// <see cref="ICalendar" />s.
    /// </summary>
    /// <param name="conn">The connection and transaction this delete runs in.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask ClearData(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the misfire original fire time for the given trigger.
    /// </summary>
    ValueTask UpdateMisfireOriginalFireTime(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        DateTimeOffset? fireTime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the misfire original fire time for the given trigger.
    /// </summary>
    /// <remarks>
    /// The fire path clears it as one command of <see cref="ApplyTriggerFired" />'s batch rather than
    /// through this, so overriding this alone no longer changes what a fire clears.
    /// </remarks>
    ValueTask ClearMisfireOriginalFireTime(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a targeted UPDATE of only the columns that change during misfire recovery,
    /// bypassing the heavyweight <c>AddTrigger</c> / <c>UpdateTrigger</c> path which
    /// performs many unnecessary SELECTs (existence check, pause-group checks, job retrieval,
    /// trigger-type lookup) that are redundant for a trigger known to be in WAITING state.
    /// </summary>
    /// <param name="conn">The DB connection.</param>
    /// <param name="trigger">The trigger after <c>UpdateAfterMisfire</c> has been applied in-memory.</param>
    /// <param name="newState">The new trigger state to persist (e.g. WAITING, COMPLETE, BLOCKED).</param>
    /// <param name="misfireOriginalFireTime">
    /// The original scheduled fire time for "fire now" misfire policies. When non-<c>null</c>,
    /// the value is written to the MISFIRE_ORIG_FIRE_TIME column. <c>null</c> leaves the column
    /// unchanged, preserving any previously stored original fire time.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask UpdateMisfiredTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        StoredTriggerState newState,
        DateTimeOffset? misfireOriginalFireTime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the retry a completing trigger has just scheduled for itself: where it fires next, how
    /// many attempts at the current occurrence are behind it, and the state it waits in.
    /// </summary>
    /// <remarks>
    /// Narrow on purpose. The trigger's schedule has not changed — only which instant it fires at
    /// next — so the generic trigger UPDATE would write a row's worth of columns to move one, and
    /// would write back a preferred node and a job data map this path has no business touching.
    /// </remarks>
    /// <param name="conn">The DB connection.</param>
    /// <param name="trigger">
    /// The completing trigger, carrying the retry instant as its next fire time and the incremented
    /// attempt.
    /// </param>
    /// <param name="newState">The state to leave the trigger in — waiting, or paused if its group is.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>How many rows were updated.</returns>
    /// <seealso cref="SchedulerInstruction.RetryTrigger" />
    ValueTask<int> UpdateTriggerForRetry(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        StoredTriggerState newState,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a trigger's retry attempt back to zero, leaving everything else about the row alone.
    /// </summary>
    /// <remarks>
    /// The other end of <see cref="UpdateTriggerForRetry" />: the occurrence is done with, whether it
    /// succeeded or spent its attempts. Issued only when the attempt was actually non-zero, so an
    /// ordinary completion still costs no extra statement.
    /// </remarks>
    /// <param name="conn">The DB connection.</param>
    /// <param name="triggerKey">The trigger to clear.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>How many rows were updated.</returns>
    ValueTask<int> ClearTriggerRetryAttempt(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects the misfired triggers to recover as fully populated triggers, rather than as keys that the
    /// caller then has to read back one at a time. Same predicate and ordering as
    /// <see cref="CountMisfiredTriggersInState" />.
    /// </summary>
    /// <param name="conn">The DB connection.</param>
    /// <param name="state">The trigger state to scan (<see cref="StoredTriggerState.Waiting" />).</param>
    /// <param name="misfireTime">Triggers whose next fire time is at or before this are misfired.</param>
    /// <param name="count">Maximum number of triggers to return, or -1 for all of them.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<MisfiredTriggerBatch> SelectMisfiredTriggersToRecover(
        ConnectionAndTransactionHolder conn,
        StoredTriggerState state,
        DateTimeOffset misfireTime,
        int count,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a whole batch of misfire updates. Semantically identical to calling
    /// <see cref="UpdateMisfiredTrigger" /> once per entry, but collapses the statements into a single
    /// round-trip on providers that support batching.
    /// </summary>
    /// <param name="conn">The DB connection.</param>
    /// <param name="updates">The triggers, after <c>UpdateAfterMisfire</c> has been applied in-memory.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask UpdateMisfiredTriggers(
        ConnectionAndTransactionHolder conn,
        IReadOnlyList<MisfiredTriggerUpdate> updates,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the preferred node columns for a trigger only when they still hold the expected
    /// values (compare-and-swap). Used by the auto-pin claim/steal in TriggerFired so a concurrent
    /// re-pin or clear (e.g. via UpdateTriggerDetails between acquisition and firing) wins over the
    /// claim instead of being clobbered by it.
    /// </summary>
    /// <param name="conn">The DB connection.</param>
    /// <param name="triggerKey">The key identifying the trigger.</param>
    /// <param name="transition">The pin the row must still hold, and the one to put in its place.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>Number of rows updated: 1 when the claim succeeded, 0 when the value changed concurrently.</returns>
    ValueTask<int> UpdateTriggerPreferredNodeConditional(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        PreferredNodeTransition transition,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases all auto-claimed pins belonging to a dead node back to the given value
    /// (batch UPDATE). Explicit pins are left untouched. Used during ClusterRecover to implement
    /// sticky failover.
    /// </summary>
    ValueTask<int> RepinTriggersFromDeadNode(
        ConnectionAndTransactionHolder conn,
        string oldPreferredNode,
        string newPreferredNode,
        CancellationToken cancellationToken = default);

    //---------------------------------------------------------------------------
    // paged listings and batch reads
    //---------------------------------------------------------------------------

    /// <summary>
    /// Selects one page of job listing entries, ordered by group and then name.
    /// </summary>
    /// <remarks>
    /// The listing does not read or deserialize JOB_DATA, and does not load the job type — the header
    /// carries the recorded type name as a string.
    /// </remarks>
    /// <param name="conn">The DB connection.</param>
    /// <param name="query">What to select and which page of it to return.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<PagedResult<JobHeader>> SelectJobHeaders(
        ConnectionAndTransactionHolder conn,
        JobQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects one page of trigger listing entries, ordered by group and then name.
    /// </summary>
    /// <remarks>
    /// The listing reads the TRIGGERS row only: no type table, no BLOB, no JOB_DATA.
    /// </remarks>
    /// <param name="conn">The DB connection.</param>
    /// <param name="query">What to select and which page of it to return.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<PagedResult<TriggerHeader>> SelectTriggerHeaders(
        ConnectionAndTransactionHolder conn,
        TriggerQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects one page of fire instances, ordered by trigger group, then trigger name, then entry id.
    /// </summary>
    /// <remarks>
    /// The listing reads the FIRED_TRIGGERS row only, and only the columns
    /// <see cref="FireInstance" /> projects — not the concurrency and recovery flags that
    /// <see cref="SelectFiredTriggerRecords" /> reads for the recovery passes. The entry id is part of
    /// the ordering because trigger group and name do not identify a row here: one trigger can have
    /// several firings at once.
    /// </remarks>
    /// <param name="conn">The DB connection.</param>
    /// <param name="query">What to select and which page of it to return.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<PagedResult<FireInstance>> SelectFireInstances(
        ConnectionAndTransactionHolder conn,
        FireInstanceQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects one page of job groups, ordered by name.
    /// </summary>
    /// <remarks>
    /// A query for paused groups only reads PAUSED_JOB_GRPS, so it reports a paused group that
    /// currently holds no jobs as well. That table has no counterpart to
    /// <see cref="AdoConstants.AllGroupsPaused" />: pause-all is a trigger operation and writes no
    /// marker row here.
    /// </remarks>
    /// <param name="conn">The DB connection.</param>
    /// <param name="query">What to select and which page of it to return.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<PagedResult<JobGroup>> SelectJobGroups(
        ConnectionAndTransactionHolder conn,
        JobGroupQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects one page of trigger groups, ordered by name.
    /// </summary>
    /// <remarks>
    /// A query for paused groups only reads PAUSED_TRIGGER_GRPS, so it reports a paused group that
    /// currently has no triggers as well. It must not report
    /// <see cref="AdoConstants.AllGroupsPaused" />: that row records "everything is paused" and is not
    /// a group any trigger can belong to.
    /// </remarks>
    /// <param name="conn">The DB connection.</param>
    /// <param name="query">What to select and which page of it to return.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<PagedResult<TriggerGroup>> SelectTriggerGroups(
        ConnectionAndTransactionHolder conn,
        TriggerGroupQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects one page of calendar names, ordered by name.
    /// </summary>
    /// <param name="conn">The DB connection.</param>
    /// <param name="query">Which names to select and which page of them to return.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<PagedResult<string>> SelectCalendarNames(
        ConnectionAndTransactionHolder conn,
        CalendarQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects several jobs at once, chunking the keys into as few statements as the provider's
    /// parameter ceiling allows. Keys that have no row are simply absent from the result.
    /// </summary>
    /// <param name="conn">The DB connection.</param>
    /// <param name="jobKeys">The keys of the jobs to select.</param>
    /// <param name="typeLoader">The type loader.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<List<IJobDetail>> SelectJobDetails(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<JobKey> jobKeys,
        ITypeLoader typeLoader,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects several triggers at once, chunking the keys into as few statements as the provider's
    /// parameter ceiling allows. Keys that have no row are simply absent from the result.
    /// </summary>
    /// <param name="conn">The DB connection.</param>
    /// <param name="triggerKeys">The keys of the triggers to select.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<List<IOperableTrigger>> SelectTriggers(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies that the schema objects this delegate reads and writes are there, and reports how many
    /// were checked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called at startup for every <see cref="SchemaProvisioning"/> but
    /// <see cref="SchemaProvisioning.None"/>, so that a missing or mis-prefixed table is reported once,
    /// by name, instead of as the first failing operation. A delegate that owns tables of its own
    /// checks those too.
    /// </para>
    /// <para>
    /// Tables, and the columns 4.x added to the tables 3.x already had: a schema that took none of the
    /// 4.0 migration is missing one table and six columns, and a table-level check alone would pass a
    /// database that then fails every acquisition for ever. What it does not check is the shape of a
    /// column — a type or a width that a hand-built table got wrong is still found by the statement
    /// that binds it.
    /// </para>
    /// </remarks>
    /// <param name="conn">The DB connection.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The number of schema objects validated.</returns>
    /// <exception cref="JobPersistenceException">An object could not be queried.</exception>
    ValueTask<int> ValidateSchema(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates whatever this delegate's schema is missing, and leaves everything that is already there
    /// exactly as it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called at startup, before <see cref="ValidateSchema"/>, when
    /// <see cref="SchemaProvisioning.CreateIfMissing"/> is configured. Every statement it runs is
    /// guarded, so this is a no-op against a schema that already exists; nothing is altered and nothing
    /// is dropped, which is what makes it safe to leave on and what keeps a mis-typed table prefix from
    /// costing data.
    /// </para>
    /// <para>
    /// It is not a migration. A schema that has every table but is missing a column a later release
    /// added is left exactly as it is; upgrading a schema is what <c>database/migrations/</c> is for.
    /// The store will not call this against a schema that is partly there for that reason — creating
    /// the tables a 3.x schema is missing would leave one that starts and fires nothing — so a
    /// delegate's script only ever meets a database with no Quartz tables under the prefix, or one it
    /// has nothing to do to.
    /// </para>
    /// <para>
    /// A delegate that has no script for its database throws rather than pretending to have created
    /// anything, naming itself and the script to run by hand.
    /// </para>
    /// </remarks>
    /// <param name="conn">The DB connection.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <exception cref="JobPersistenceException">
    /// A statement failed, or this delegate has no schema script.
    /// </exception>
    ValueTask CreateSchema(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default);
}