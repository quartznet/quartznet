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
    /// Initializes the driver delegate with configuration data.
    /// </summary>
    /// <param name="args"></param>
    void Initialize(DelegateInitializationArgs args);

    /// <summary>
    /// Update all triggers having one of the two given states, to the given new
    /// state.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="newState">The new state for the triggers</param>
    /// <param name="oldState1">The first old state to update</param>
    /// <param name="oldState2">The second old state to update</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>Number of rows updated</returns>
    ValueTask<int> UpdateTriggerStatesFromOtherStates(
        ConnectionAndTransactionHolder conn,
        string newState,
        string oldState1,
        string oldState2,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the names of all the triggers that have misfired - according to
    /// the given timestamp.
    /// </summary>
    /// <returns>An array of <see cref="TriggerKey" /> objects</returns>
    ValueTask<List<TriggerKey>> SelectMisfiredTriggers(
        ConnectionAndTransactionHolder conn,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the names of all the triggers in the given state that have
    /// misfired - according to the given timestamp.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="state">The state.</param>
    /// <param name="ts">The time stamp.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>An array of <see cref="TriggerKey" /> objects</returns>
    ValueTask<List<TriggerKey>> HasMisfiredTriggersInState(
        ConnectionAndTransactionHolder conn,
        string state,
        DateTimeOffset ts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the names of all the triggers in the given group and state that
    /// have misfired - according to the given timestamp.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="groupName">Name of the group.</param>
    /// <param name="state">The state.</param>
    /// <param name="ts">The timestamp.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>An array of <see cref="TriggerKey" /> objects</returns>
    ValueTask<List<TriggerKey>> SelectMisfiredTriggersInGroupInState(ConnectionAndTransactionHolder conn,
        string groupName,
        string state,
        DateTimeOffset ts,
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
    /// Delete all fired triggers.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The number of rows deleted</returns>
    ValueTask<int> DeleteFiredTriggers(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all fired triggers of the given instance.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="instanceId">The instance id.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The number of rows deleted</returns>
    ValueTask<int> DeleteFiredTriggers(ConnectionAndTransactionHolder conn, string instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all fired triggers for the given trigger.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="triggerKey">The trigger key.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The number of rows deleted</returns>
    ValueTask<int> DeleteFiredTriggers(ConnectionAndTransactionHolder conn, TriggerKey triggerKey, CancellationToken cancellationToken = default);

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
    /// Get the names of all the triggers associated with the given job.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="jobKey">The key identifying the job.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<List<TriggerKey>> SelectTriggerNamesForJob(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default);

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
    /// <param name="classLoadHelper">The class load helper.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The populated JobDetail object</returns>
    ValueTask<IJobDetail?> SelectJobDetail(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        ITypeLoadHelper classLoadHelper,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Select the total number of jobs stored.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns> the total number of jobs stored</returns>
    ValueTask<int> SelectNumJobs(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Select all the job group names that are stored.
    /// </summary>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns> an array of <see cref="String" /> group names</returns>
    ValueTask<List<string>> SelectJobGroups(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Select all the jobs contained in a given group.
    /// </summary>
    /// <param name="conn">The DB Connection </param>
    /// <param name="matcher"></param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns> an array of <see cref="String" /> job names</returns>
    ValueTask<List<JobKey>> SelectJobsInGroup(ConnectionAndTransactionHolder conn, GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default);

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
        string state,
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
        string state,
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
        string state,
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
        string newState,
        string oldState,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the given trigger to the given new state, if it is one of the
    /// given old states.
    /// </summary>
    /// <param name="conn">The DB connection</param>
    /// <param name="triggerKey">The key identifying the trigger.</param>
    /// <param name="newState">The new state for the trigger</param>
    /// <param name="oldState1">One of the old state the trigger must be in</param>
    /// <param name="oldState2">One of the old state the trigger must be in</param>
    /// <param name="oldState3">One of the old state the trigger must be in</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns> int the number of rows updated
    /// </returns>
    /// <throws>  SQLException </throws>
    ValueTask<int> UpdateTriggerStateFromOtherStates(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        string newState,
        string oldState1,
        string oldState2,
        string oldState3,
        CancellationToken cancellationToken = default);

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
        string newState,
        string oldState,
        DateTimeOffset nextFireTime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update all triggers in the given group to the given new state, if they
    /// are in one of the given old states.
    /// </summary>
    /// <param name="conn">The DB connection</param>
    /// <param name="matcher"></param>
    /// <param name="newState">The new state for the trigger</param>
    /// <param name="oldState1">One of the old state the trigger must be in</param>
    /// <param name="oldState2">One of the old state the trigger must be in</param>
    /// <param name="oldState3">One of the old state the trigger must be in</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The number of rows updated</returns>
    ValueTask<int> UpdateTriggerGroupStateFromOtherStates(
        ConnectionAndTransactionHolder conn,
        GroupMatcher<TriggerKey> matcher,
        string newState,
        string oldState1,
        string oldState2,
        string oldState3,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update all of the triggers of the given group to the given new state, if
    /// they are in the given old state.
    /// </summary>
    /// <param name="conn">The DB connection</param>
    /// <param name="matcher"></param>
    /// <param name="newState">The new state for the trigger group</param>
    /// <param name="oldState">The old state the triggers must be in.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns> int the number of rows updated</returns>
    ValueTask<int> UpdateTriggerGroupStateFromOtherState(
        ConnectionAndTransactionHolder conn,
        GroupMatcher<TriggerKey> matcher,
        string newState,
        string oldState,
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
        string state,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the states of any triggers associated with the given job, that
    /// are the given current state.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="jobKey">The key identifying the job.</param>
    /// <param name="state">The new state for the triggers</param>
    /// <param name="oldState">The old state of the triggers</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns> the number of rows updated</returns>
    ValueTask<int> UpdateTriggerStatesForJobFromOtherState(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        string state,
        string oldState,
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
    ValueTask<int> SelectNumTriggersForJob(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Select the job to which the trigger is associated.
    /// </summary>
    ValueTask<IJobDetail?> SelectJobForTrigger(ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        ITypeLoadHelper loadHelper,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Select the job to which the trigger is associated. Allow option to load actual job class or not. When case of
    /// remove, we do not need to load the type, which in many cases, it's no longer exists.
    /// </summary>
    ValueTask<IJobDetail?> SelectJobForTrigger(ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        ITypeLoadHelper loadHelper,
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
    /// <returns>The <see cref="ITrigger" /> object.</returns>
    ValueTask<string> SelectTriggerState(ConnectionAndTransactionHolder conn, TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Select a triggers status (state and next fire time).
    /// </summary>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="triggerKey">The key identifying the trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>A <see cref="TriggerStatus" /> object, or null</returns>
    ValueTask<TriggerStatus?> SelectTriggerStatus(ConnectionAndTransactionHolder conn, TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Select the total number of triggers stored.
    /// </summary>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The total number of triggers stored.</returns>
    ValueTask<int> SelectNumTriggers(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Select all the trigger group names that are stored.
    /// </summary>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>An array of <see cref="String" /> group names.</returns>
    ValueTask<List<string>> SelectTriggerGroups(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Select all trigger group names that match the criteria.
    /// </summary>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="matcher">The matcher to apply for searching.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>An array of <see cref="String" /> group names.</returns>
    ValueTask<List<string>> SelectTriggerGroups(ConnectionAndTransactionHolder conn, GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default);

    /// <summary>
    /// Select all the triggers contained in a given group.
    /// </summary>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="matcher"></param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>An array of <see cref="String" /> trigger names.</returns>
    Task<List<TriggerKey>> SelectTriggersInGroup(ConnectionAndTransactionHolder conn, GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default);

    /// <summary>
    /// Select all the triggers in a given state.
    /// </summary>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="state">The state the triggers must be in.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>An array of trigger <see cref="TriggerKey" />s.</returns>
    ValueTask<List<TriggerKey>> SelectTriggersInState(ConnectionAndTransactionHolder conn, string state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts the paused trigger group.
    /// </summary>
    /// <param name="conn">The conn.</param>
    /// <param name="groupName">Name of the group.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns></returns>
    ValueTask<int> InsertPausedTriggerGroup(
        ConnectionAndTransactionHolder conn,
        string groupName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the paused trigger group.
    /// </summary>
    /// <param name="conn">The database connection.</param>
    /// <param name="groupName">Name of the group.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns></returns>
    ValueTask<int> DeletePausedTriggerGroup(
        ConnectionAndTransactionHolder conn,
        string groupName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the paused trigger group.
    /// </summary>
    /// <param name="conn">The database connection.</param>
    /// <param name="matcher">Criteria for matching groups.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns></returns>
    ValueTask<int> DeletePausedTriggerGroup(
        ConnectionAndTransactionHolder conn,
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all paused trigger groups.
    /// </summary>
    /// <param name="conn">The conn.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns></returns>
    ValueTask<int> DeleteAllPausedTriggerGroups(
        ConnectionAndTransactionHolder conn,
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
    /// Selects the paused trigger groups.
    /// </summary>
    /// <param name="conn">The DB Connection.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns></returns>
    ValueTask<List<string>> SelectPausedTriggerGroups(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether given trigger group already exists.
    /// </summary>
    /// <param name="conn">The conn.</param>
    /// <param name="groupName">Name of the group.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// 	<c>true</c> if trigger group exists; otherwise, <c>false</c>.
    /// </returns>
    ValueTask<bool> IsExistingTriggerGroup(
        ConnectionAndTransactionHolder conn,
        string groupName,
        CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Select the total number of calendars stored.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The total number of calendars stored.</returns>
    ValueTask<int> SelectNumCalendars(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Select all the stored calendars.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>An array of <see cref="String" /> calendar names.</returns>
    ValueTask<List<string>> SelectCalendars(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default);

    //---------------------------------------------------------------------------
    // trigger firing
    //---------------------------------------------------------------------------

    /// <summary>
    /// Select the trigger that will be fired at the given fire time.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="fireTime">The time that the trigger will be fired.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// A <see cref="TriggerKey" /> representing the
    /// trigger that will be fired at the given fire time, or null if no
    /// trigger will be fired at that time
    /// </returns>
    ValueTask<TriggerKey?> SelectTriggerForFireTime(ConnectionAndTransactionHolder conn,
        DateTimeOffset fireTime,
        CancellationToken cancellationToken = default);

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
        string state,
        IJobDetail? jobDetail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Select the states of all fired-trigger records for a given trigger, or
    /// trigger group if trigger name is <see langword="null" />.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="triggerName">Name of the trigger.</param>
    /// <param name="groupName">Name of the group.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>A list of FiredTriggerRecord objects.</returns>
    ValueTask<List<FiredTriggerRecord>> SelectFiredTriggerRecords(
        ConnectionAndTransactionHolder conn,
        string triggerName,
        string groupName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Select the states of all fired-trigger records for a given job, or job
    /// group if job name is <see langword="null" />.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="jobName">Name of the job.</param>
    /// <param name="groupName">Name of the group.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>A List of FiredTriggerRecord objects.</returns>
    ValueTask<List<FiredTriggerRecord>> SelectFiredTriggerRecordsByJob(
        ConnectionAndTransactionHolder conn,
        string jobName,
        string groupName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Select the states of all fired-trigger records for a given scheduler
    /// instance.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="instanceName">Name of the instance.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>A list of FiredTriggerRecord objects.</returns>
    ValueTask<List<FiredTriggerRecord>> SelectInstancesFiredTriggerRecords(
        ConnectionAndTransactionHolder conn,
        string instanceName,
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
    /// Get the number instances of the identified job currently executing.
    /// </summary>
    /// <param name="conn">The DB Connection</param>
    /// <param name="jobKey">The key identifying the job.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// The number instances of the identified job currently executing.
    /// </returns>
    ValueTask<int> SelectJobExecutionCount(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
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
    /// <param name="instanceName">The instance id.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns></returns>
    ValueTask<List<SchedulerStateRecord>> SelectSchedulerStateRecords(
        ConnectionAndTransactionHolder conn,
        string? instanceName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Select the next trigger which will fire to fire between the two given timestamps
    /// in ascending order of fire time, and then descending by priority.
    /// </summary>
    /// <param name="conn">The conn.</param>
    /// <param name="noLaterThan">highest value of <see cref="ITrigger.GetNextFireTimeUtc" /> of the triggers (exclusive)</param>
    /// <param name="noEarlierThan">lowest value of <see cref="ITrigger.GetNextFireTimeUtc" /> of the triggers (inclusive)</param>
    /// <param name="maxCount">maximum number of trigger keys allow to acquired in the returning list.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>A (never null, possibly empty) list of the identifiers (Key objects) of the next triggers to be fired.</returns>
    ValueTask<List<TriggerAcquireResult>> SelectTriggerToAcquire(
        ConnectionAndTransactionHolder conn,
        DateTimeOffset noLaterThan,
        DateTimeOffset noEarlierThan,
        int maxCount,
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
    /// <returns></returns>
    ValueTask<List<string>> SelectFiredTriggerInstanceNames(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the misfired triggers in states.
    /// </summary>
    /// <param name="conn">The conn.</param>
    /// <param name="state1">The state1.</param>
    /// <param name="ts">The ts.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns></returns>
    ValueTask<int> CountMisfiredTriggersInState(
        ConnectionAndTransactionHolder conn,
        string state1,
        DateTimeOffset ts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects the misfired triggers in states.
    /// </summary>
    /// <param name="conn">The conn.</param>
    /// <param name="state1">The state1.</param>
    /// <param name="ts">The ts.</param>
    /// <param name="count">The count.</param>
    /// <param name="resultList">The result list.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns></returns>
    ValueTask<bool> HasMisfiredTriggersInState(
        ConnectionAndTransactionHolder conn,
        string state1,
        DateTimeOffset ts,
        int count,
        ICollection<TriggerKey> resultList,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates status of a trigger.
    /// </summary>
    /// <param name="conn">The database connection.</param>
    /// <param name="trigger">Trigger to update.</param>
    /// <param name="state">The new state.</param>
    /// <param name="job">Job associated with the trigger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The amount of triggers that matched the update.</returns>
    ValueTask<int> UpdateFiredTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        string state,
        IJobDetail job,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear (delete!) all scheduling data - all <see cref="IJob"/>s, <see cref="ITrigger" />s
    /// <see cref="ICalendar" />s.
    /// </summary>
    /// <param name="conn"></param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask ClearData(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default);
}

public class TriggerAcquireResult
{
    public TriggerAcquireResult(string triggerName, string triggerGroup, string jobType)
    {
        TriggerName = triggerName;
        TriggerGroup = triggerGroup;
        JobType = jobType;
    }

    public string TriggerName { get; }
    public string TriggerGroup { get; }
    public string JobType { get; }
}