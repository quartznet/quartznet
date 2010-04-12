#region License
/* 
 * Copyright 2001-2009 Terracotta, Inc. 
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

using System;
using System.Collections.Generic;

using Quartz.Collection;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// This is the base interface for all driver delegate classes.
    /// </summary>
    /// <remarks>
    /// <p>
    /// This interface is very similar to the <see cref="IJobStore" />
    /// interface except each method has an additional <see cref="ConnectionAndTransactionHolder" />
    /// parameter.
    /// </p>
    /// <p>
    /// Unless a database driver has some <strong>extremely-DB-specific</strong>
    /// requirements, any IDriverDelegate implementation classes should extend the
    /// <see cref="StdAdoDelegate" /> class.
    /// </p>
    /// </remarks>
    /// <author><a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
	public interface IDriverDelegate
	{
		/// <summary>
		/// Update all triggers having one of the two given states, to the given new
		/// state.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <param name="newState">The new state for the triggers</param>
		/// <param name="oldState1">The first old state to update</param>
		/// <param name="oldState2">The second old state to update</param>
		/// <returns>Number of rows updated</returns>
		int UpdateTriggerStatesFromOtherStates(ConnectionAndTransactionHolder conn, string newState, string oldState1, string oldState2);

        /// <summary>
        /// Get the names of all of the triggers that have misfired - according to
        /// the given timestamp.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="timestamp">The timestamp.</param>
        /// <returns>An array of <see cref="Key" /> objects</returns>
		IList<Key> SelectMisfiredTriggers(ConnectionAndTransactionHolder conn, long timestamp);

        /// <summary>
        /// Get the names of all of the triggers in the given state that have
        /// misfired - according to the given timestamp.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="state">The state.</param>
        /// <param name="ts">The time stamp.</param>
        /// <returns>An array of <see cref="Key" /> objects</returns>
        IList<Key> SelectMisfiredTriggersInState(ConnectionAndTransactionHolder conn, string state, long ts);

        /// <summary>
        /// Get the names of all of the triggers in the given group and state that
        /// have misfired - according to the given timestamp.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="groupName">Name of the group.</param>
        /// <param name="state">The state.</param>
        /// <param name="ts">The timestamp.</param>
        /// <returns>An array of <see cref="Key" /> objects</returns>
        IList<Key> SelectMisfiredTriggersInGroupInState(ConnectionAndTransactionHolder conn, string groupName, string state, long ts);

		/// <summary> 
		/// Select all of the triggers for jobs that are requesting recovery. The
		/// returned trigger objects will have unique "recoverXXX" trigger names and
        /// will be in the <see cref="SchedulerConstants.DefaultRecoveryGroup" /> trigger group.
        /// </summary>
		/// <remarks>
		/// In order to preserve the ordering of the triggers, the fire time will be
        /// set from the <i>ColumnFiredTime</i> column in the <i>TableFiredTriggers</i>
		/// table. The caller is responsible for calling <see cref="Trigger.ComputeFirstFireTimeUtc" />
		/// on each returned trigger. It is also up to the caller to insert the
        /// returned triggers to ensure that they are fired.
        /// </remarks>
		/// <param name="conn">The DB Connection</param>
		/// <returns>An array of <see cref="Trigger" /> objects</returns>
		IList<Trigger> SelectTriggersForRecoveringJobs(ConnectionAndTransactionHolder conn);

		/// <summary>
		/// Delete all fired triggers.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <returns>The number of rows deleted</returns>
		int DeleteFiredTriggers(ConnectionAndTransactionHolder conn);

        /// <summary>
        /// Delete all fired triggers of the given instance.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="instanceId">The instance id.</param>
        /// <returns>The number of rows deleted</returns>
		int DeleteFiredTriggers(ConnectionAndTransactionHolder conn, string instanceId);

		/// <summary>
		/// Delete all volatile fired triggers.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <returns>The number of rows deleted</returns>
		int DeleteVolatileFiredTriggers(ConnectionAndTransactionHolder conn);

		/// <summary>
		/// Get the names of all of the triggers that are volatile.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <returns>An array of see cref="Key" /> objects.</returns>
        IList<Key> SelectVolatileTriggers(ConnectionAndTransactionHolder conn);

		/// <summary>
		/// Get the names of all of the jobs that are volatile.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <returns>An array of <see cref="Key" /> objects.</returns>
        IList<Key> SelectVolatileJobs(ConnectionAndTransactionHolder conn);

		//---------------------------------------------------------------------------
		// jobs
		//---------------------------------------------------------------------------

		/// <summary>
		/// Insert the job detail record.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <param name="job">The job to insert.</param>
		/// <returns>Number of rows inserted.</returns>
		int InsertJobDetail(ConnectionAndTransactionHolder conn, JobDetail job);

		/// <summary>
		/// Update the job detail record.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="job">The job to update.</param>
		/// <returns>Number of rows updated.</returns>
		int UpdateJobDetail(ConnectionAndTransactionHolder conn, JobDetail job);

		/// <summary> <p>
		/// Get the names of all of the triggers associated with the given job.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="jobName">The job name</param>
        /// <param name="groupName">The job group</param>
        IList<Key> SelectTriggerNamesForJob(ConnectionAndTransactionHolder conn, string jobName, string groupName);

		/// <summary>
		/// Delete all job listeners for the given job.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="jobName">The name of the job</param>
        /// <param name="groupName">The group containing the job</param>
		/// <returns>The number of rows deleted</returns>
		int DeleteJobListeners(ConnectionAndTransactionHolder conn, string jobName, string groupName);

		/// <summary>
		/// Delete the job detail record for the given job.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <param name="jobName">the name of the job</param>
		/// <param name="groupName">Name of the group.</param>
		/// <returns>the number of rows deleted</returns>
		int DeleteJobDetail(ConnectionAndTransactionHolder conn, string jobName, string groupName);

		/// <summary>
		/// Check whether or not the given job is stateful.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="jobName">The name of the job</param>
        /// <param name="groupName">The group containing the job</param>
		/// <returns> true if the job exists and is stateful, false otherwise</returns>
		bool IsJobStateful(ConnectionAndTransactionHolder conn, string jobName, string groupName);

		/// <summary>
		/// Check whether or not the given job exists.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <param name="jobName">Name of the job.</param>
		/// <param name="groupName">Name of the group.</param>
		/// <returns>true if the job exists, false otherwise</returns>
		bool JobExists(ConnectionAndTransactionHolder conn, string jobName, string groupName);

		/// <summary>
		/// Update the job data map for the given job.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <param name="job">The job.</param>
		/// <returns>the number of rows updated</returns>
		int UpdateJobData(ConnectionAndTransactionHolder conn, JobDetail job);

		/// <summary>
		/// Associate a listener with a job.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <param name="job">The job to associate with the listener.</param>
		/// <param name="listener">The listener to insert.</param>
		/// <returns>The number of rows inserted.</returns>
		int InsertJobListener(ConnectionAndTransactionHolder conn, JobDetail job, string listener);

		/// <summary> <p>
		/// Get all of the listeners for a given job.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="jobName">The job name whose listeners are wanted</param>
        /// <param name="groupName">The group containing the job</param>
		/// <returns> array of <see cref="String" /> listener names</returns>
        IList<string> SelectJobListeners(ConnectionAndTransactionHolder conn, string jobName, string groupName);

        /// <summary>
        /// Select the JobDetail object for a given job name / group name.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="jobName">The job name whose listeners are wanted</param>
        /// <param name="groupName">The group containing the job</param>
        /// <param name="classLoadHelper">The class load helper.</param>
        /// <returns>The populated JobDetail object</returns>
		JobDetail SelectJobDetail(ConnectionAndTransactionHolder conn, string jobName, string groupName, ITypeLoadHelper classLoadHelper);

		/// <summary>
		/// Select the total number of jobs stored.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <returns> the total number of jobs stored</returns>
		int SelectNumJobs(ConnectionAndTransactionHolder conn);

		/// <summary> 
		/// Select all of the job group names that are stored.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <returns> an array of <see cref="String" /> group names</returns>
        IList<string> SelectJobGroups(ConnectionAndTransactionHolder conn);

		/// <summary>
		/// Select all of the jobs contained in a given group.
		/// </summary>
		/// <param name="conn">The DB Connection </param>
        /// <param name="groupName">The group containing the jobs</param>
		/// <returns> an array of <see cref="String" /> job names</returns>
        IList<string> SelectJobsInGroup(ConnectionAndTransactionHolder conn, string groupName);

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
        /// <returns>The number of rows inserted</returns>
		int InsertTrigger(ConnectionAndTransactionHolder conn, Trigger trigger, string state, JobDetail jobDetail);

		/// <summary>
		/// Insert the simple trigger data.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="trigger">The trigger to insert</param>
		/// <returns>The number of rows inserted</returns>
		int InsertSimpleTrigger(ConnectionAndTransactionHolder conn, SimpleTrigger trigger);

		/// <summary>
		/// Insert the blob trigger data.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="trigger">The trigger to insert</param>
		/// <returns>The number of rows inserted</returns>
		int InsertBlobTrigger(ConnectionAndTransactionHolder conn, Trigger trigger);

        /// <summary>
        /// Insert the cron trigger data.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="trigger">The trigger.</param>
        /// <returns>the number of rows inserted</returns>
		int InsertCronTrigger(ConnectionAndTransactionHolder conn, CronTrigger trigger);

        /// <summary>
        /// Update the base trigger data.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="trigger">The trigger.</param>
        /// <param name="state">The state.</param>
        /// <param name="jobDetail">The job detail.</param>
        /// <returns>the number of rows updated</returns>
		int UpdateTrigger(ConnectionAndTransactionHolder conn, Trigger trigger, string state, JobDetail jobDetail);

        /// <summary>
        /// Update the simple trigger data.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="trigger">The trigger.</param>
        /// <returns>the number of rows updated</returns>
		int UpdateSimpleTrigger(ConnectionAndTransactionHolder conn, SimpleTrigger trigger);

        /// <summary>
        /// Update the cron trigger data.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="trigger">The trigger.</param>
        /// <returns>the number of rows updated</returns>
		int UpdateCronTrigger(ConnectionAndTransactionHolder conn, CronTrigger trigger);

        /// <summary>
        /// Update the blob trigger data.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="trigger">The trigger.</param>
        /// <returns>the number of rows updated</returns>
		int UpdateBlobTrigger(ConnectionAndTransactionHolder conn, Trigger trigger);

        /// <summary>
        /// Check whether or not a trigger exists.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="triggerName">Name of the trigger.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns>the number of rows updated</returns>
		bool TriggerExists(ConnectionAndTransactionHolder conn, string triggerName, string groupName);

		/// <summary>
		/// Update the state for a given trigger.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="triggerName">The name of the trigger.</param>
        /// <param name="groupName">The group containing the trigger.</param>
        /// <param name="state">The new state for the trigger.</param>
		/// <returns> the number of rows updated</returns>
		int UpdateTriggerState(ConnectionAndTransactionHolder conn, string triggerName, string groupName, string state);

		/// <summary>
		/// Update the given trigger to the given new state, if it is in the given
		/// old state.
		/// </summary>
		/// <param name="conn">The DB connection</param>
        /// <param name="triggerName">The name of the trigger.</param>
        /// <param name="groupName">The group containing the trigger</param>
        /// <param name="newState">The new state for the trigger </param>
        /// <param name="oldState">The old state the trigger must be in</param>
		/// <returns> int the number of rows updated</returns>
		int UpdateTriggerStateFromOtherState(ConnectionAndTransactionHolder conn, string triggerName, string groupName, string newState,
		                                     string oldState);

		/// <summary>
		/// Update the given trigger to the given new state, if it is one of the
		/// given old states.
		/// </summary>
		/// <param name="conn">The DB connection</param>
        /// <param name="triggerName">The name of the trigger</param>
        /// <param name="groupName">The group containing the trigger</param>
        /// <param name="newState">The new state for the trigger</param>
        /// <param name="oldState1">One of the old state the trigger must be in</param>
        /// <param name="oldState2">One of the old state the trigger must be in</param>
        /// <param name="oldState3">One of the old state the trigger must be in
		/// </param>
		/// <returns> int the number of rows updated
		/// </returns>
		/// <throws>  SQLException </throws>
		
		int UpdateTriggerStateFromOtherStates(ConnectionAndTransactionHolder conn, string triggerName, string groupName, string newState,
		                                      string oldState1, string oldState2, string oldState3);

		/// <summary>
		/// Update the all triggers to the given new state, if they are in one of
		/// the given old states AND its next fire time is before the given time.
		/// </summary>
		/// <param name="conn">The DB connection</param>
        /// <param name="newState">The new state for the trigger</param>
        /// <param name="oldState1">One of the old state the trigger must be in</param>
        /// <param name="oldState2">One of the old state the trigger must be in</param>
        /// <param name="time">The time before which the trigger's next fire time must be</param>
		/// <returns> int the number of rows updated</returns>
		int UpdateTriggerStateFromOtherStatesBeforeTime(ConnectionAndTransactionHolder conn, string newState, string oldState1,
		                                                string oldState2, long time);

		/// <summary>
		/// Update all triggers in the given group to the given new state, if they
		/// are in one of the given old states.
		/// </summary>
		/// <param name="conn">The DB connection</param>
        /// <param name="groupName">The group containing the trigger</param>
        /// <param name="newState">The new state for the trigger</param>
        /// <param name="oldState1">One of the old state the trigger must be in</param>
        /// <param name="oldState2">One of the old state the trigger must be in</param>
        /// <param name="oldState3">One of the old state the trigger must be in</param>
		/// <returns>The number of rows updated</returns>
		int UpdateTriggerGroupStateFromOtherStates(ConnectionAndTransactionHolder conn, string groupName, string newState, string oldState1,
		                                           string oldState2, string oldState3);

		/// <summary>
		/// Update all of the triggers of the given group to the given new state, if
		/// they are in the given old state.
		/// </summary>
		/// <param name="conn">The DB connection</param>
        /// <param name="groupName">The group containing the triggers</param>
        /// <param name="newState">The new state for the trigger group</param>
        /// <param name="oldState">The old state the triggers must be in.</param>
		/// <returns> int the number of rows updated</returns>
		int UpdateTriggerGroupStateFromOtherState(ConnectionAndTransactionHolder conn, string groupName, string newState, string oldState);

		/// <summary>
		/// Update the states of all triggers associated with the given job.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="jobName">The name of the job.</param>
        /// <param name="groupName">The group containing the job.</param>
        /// <param name="state">The new state for the triggers.</param>
		/// <returns>The number of rows updated</returns>
		int UpdateTriggerStatesForJob(ConnectionAndTransactionHolder conn, string jobName, string groupName, string state);

		/// <summary>
		/// Update the states of any triggers associated with the given job, that
		/// are the given current state.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="jobName">The name of the job</param>
        /// <param name="groupName">The group containing the job</param>
        /// <param name="state">The new state for the triggers</param>
        /// <param name="oldState">The old state of the triggers</param>
		/// <returns> the number of rows updated</returns>
		int UpdateTriggerStatesForJobFromOtherState(ConnectionAndTransactionHolder conn, string jobName, string groupName, string state,
		                                            string oldState);

		/// <summary>
		/// Delete all of the listeners associated with a given trigger.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="triggerName">The name of the trigger whose listeners will be deleted</param>
        /// <param name="groupName">The name of the group containing the trigger</param>
		/// <returns> the number of rows deleted</returns>
		int DeleteTriggerListeners(ConnectionAndTransactionHolder conn, string triggerName, string groupName);

		/// <summary>
		/// Associate a listener with the given trigger.
		/// </summary>
		/// <param name="conn">The DB Connectio</param>
        /// <param name="trigger">The trigger</param>
        /// <param name="listener">The name of the listener to associate with the trigger</param>
		/// <returns> the number of rows inserted </returns>
		int InsertTriggerListener(ConnectionAndTransactionHolder conn, Trigger trigger, string listener);

		/// <summary>
		/// Select the listeners associated with a given trigger.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="triggerName">The name of the trigger</param>
        /// <param name="groupName">The group containing the trigger</param>
		/// <returns> array of <see cref="String" /> trigger listener names </returns>
        IList<string> SelectTriggerListeners(ConnectionAndTransactionHolder conn, string triggerName, string groupName);

		/// <summary>
		/// Delete the simple trigger data for a trigger.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="triggerName">The name of the trigger</param>
        /// <param name="groupName">The group containing the trigger</param>
		/// <returns>The number of rows deleted</returns>
		int DeleteSimpleTrigger(ConnectionAndTransactionHolder conn, string triggerName, string groupName);

		/// <summary>
		/// Delete the BLOB trigger data for a trigger.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <param name="triggerName">The name of the trigger</param>
		/// <param name="groupName">The group containing the trigger</param>
		/// <returns>The number of rows deleted</returns>
		int DeleteBlobTrigger(ConnectionAndTransactionHolder conn, string triggerName, string groupName);

		/// <summary>
		/// Delete the cron trigger data for a trigger.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <param name="triggerName">The name of the trigger</param>
		/// <param name="groupName">The group containing the trigger </param>
		/// <returns> the number of rows deleted </returns>		
		int DeleteCronTrigger(ConnectionAndTransactionHolder conn, string triggerName, string groupName);

		/// <summary>
		/// Delete the base trigger data for a trigger.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="triggerName">The name of the trigger</param>
        /// <param name="groupName">The group containing the trigger</param>
		/// <returns> the number of rows deleted </returns>
		int DeleteTrigger(ConnectionAndTransactionHolder conn, string triggerName, string groupName);

		/// <summary>
		/// Select the number of triggers associated with a given job.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="jobName">The name of the job</param>
        /// <param name="groupName">The group containing the job</param>
		/// <returns> the number of triggers for the given job </returns>
		int SelectNumTriggersForJob(ConnectionAndTransactionHolder conn, string jobName, string groupName);

        /// <summary>
        /// Select the job to which the trigger is associated.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="triggerName">The name of the trigger</param>
        /// <param name="groupName">The group containing the trigger</param>
        /// <param name="loadHelper">The load helper.</param>
        /// <returns>
        /// The <see cref="JobDetail" /> object associated with the given trigger
        /// </returns>
		JobDetail SelectJobForTrigger(ConnectionAndTransactionHolder conn, string triggerName, string groupName, ITypeLoadHelper loadHelper);

		/// <summary>
		/// Select the stateful jobs which are referenced by triggers in the given
		/// trigger group.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="groupName">The trigger group.</param>
		/// <returns> a List of Keys to jobs. </returns>
		IList<Key> SelectStatefulJobsOfTriggerGroup(ConnectionAndTransactionHolder conn, string groupName);

		/// <summary>
		/// Select the triggers for a job>
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="jobName">The name of the trigger</param>
        /// <param name="groupName">The group containing the trigger</param>
		/// <returns> an array of <see cref="Trigger" /> objects associated with a given job. </returns>
		IList<Trigger> SelectTriggersForJob(ConnectionAndTransactionHolder conn, string jobName, string groupName);

        /// <summary>
        /// Select the triggers for a calendar
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="calName">Name of the calendar.</param>
        /// <returns>
        /// An array of <see cref="Trigger" /> objects associated with a given job.
        /// </returns>
		IList<Trigger> SelectTriggersForCalendar(ConnectionAndTransactionHolder conn, string calName);

		/// <summary>
		/// Select a trigger.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="triggerName">The name of the trigger.</param>
		/// <param name="groupName">The group containing the trigger.</param>
		/// <returns>The <see cref="Trigger" /> object.
		/// </returns>
		Trigger SelectTrigger(ConnectionAndTransactionHolder conn, string triggerName, string groupName);

		/// <summary>
		/// Select a trigger's JobDataMap.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="triggerName">The name of the trigger.</param>
		/// <param name="groupName">The group containing the trigger.</param>
		/// <returns>The <see cref="JobDataMap" /> of the Trigger, never null, but possibly empty.</returns>
		JobDataMap SelectTriggerJobDataMap(ConnectionAndTransactionHolder conn, string triggerName, string groupName);

		/// <summary>
		/// Select a trigger's state value.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="triggerName">The name of the trigger.</param>
		/// <param name="groupName">The group containing the trigger.</param>
		/// <returns>The <see cref="Trigger" /> object.</returns>
		string SelectTriggerState(ConnectionAndTransactionHolder conn, string triggerName, string groupName);

		/// <summary> 
		/// Select a triggers status (state and next fire time).
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="triggerName">The name of the trigger.</param>
		/// <param name="groupName">The group containing the trigger.</param>
		/// <returns>A <see cref="TriggerStatus" /> object, or null</returns>
		TriggerStatus SelectTriggerStatus(ConnectionAndTransactionHolder conn, string triggerName, string groupName);

		/// <summary>
		/// Select the total number of triggers stored.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <returns>The total number of triggers stored.</returns>
		int SelectNumTriggers(ConnectionAndTransactionHolder conn);

		/// <summary>
		/// Select all of the trigger group names that are stored.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <returns>An array of <see cref="String" /> group names.</returns>
		IList<string> SelectTriggerGroups(ConnectionAndTransactionHolder conn);

		/// <summary>
		/// Select all of the triggers contained in a given group. 
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="groupName">The group containing the triggers.</param>
		/// <returns>An array of <see cref="String" /> trigger names.</returns>
        IList<string> SelectTriggersInGroup(ConnectionAndTransactionHolder conn, string groupName);

		/// <summary>
		/// Select all of the triggers in a given state.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="state">The state the triggers must be in.</param>
		/// <returns>An array of trigger <see cref="Key" />s.</returns>
		IList<Key> SelectTriggersInState(ConnectionAndTransactionHolder conn, string state);


        /// <summary>
        /// Inserts the paused trigger group.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns></returns>
		int InsertPausedTriggerGroup(ConnectionAndTransactionHolder conn, string groupName);


        /// <summary>
        /// Deletes the paused trigger group.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns></returns>
		int DeletePausedTriggerGroup(ConnectionAndTransactionHolder conn, string groupName);


        /// <summary>
        /// Deletes all paused trigger groups.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <returns></returns>
		int DeleteAllPausedTriggerGroups(ConnectionAndTransactionHolder conn);


        /// <summary>
        /// Determines whether the specified trigger group is paused.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns>
        /// 	<c>true</c> if trigger group is paused; otherwise, <c>false</c>.
        /// </returns>
		bool IsTriggerGroupPaused(ConnectionAndTransactionHolder conn, string groupName);


        /// <summary>
        /// Selects the paused trigger groups.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <returns></returns>
        ISet<string> SelectPausedTriggerGroups(ConnectionAndTransactionHolder conn);

        /// <summary>
        /// Determines whether given trigger group already exists.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns>
        /// 	<c>true</c> if trigger group exists; otherwise, <c>false</c>.
        /// </returns>
		bool IsExistingTriggerGroup(ConnectionAndTransactionHolder conn, string groupName);

		//---------------------------------------------------------------------------
		// calendars
		//---------------------------------------------------------------------------

		/// <summary>
		/// Insert a new calendar.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="calendarName">The name for the new calendar.</param>
		/// <param name="calendar">The calendar.</param>
		/// <returns>The number of rows inserted.</returns>
		int InsertCalendar(ConnectionAndTransactionHolder conn, string calendarName, ICalendar calendar);

		/// <summary> 
		/// Update a calendar.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="calendarName">The name for the new calendar.</param>
		/// <param name="calendar">The calendar.</param>
		/// <returns>The number of rows updated.</returns>
		int UpdateCalendar(ConnectionAndTransactionHolder conn, string calendarName, ICalendar calendar);

		/// <summary>
		/// Check whether or not a calendar exists.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="calendarName">The name of the calendar.</param>
		/// <returns>true if the trigger exists, false otherwise.</returns>
		bool CalendarExists(ConnectionAndTransactionHolder conn, string calendarName);

		/// <summary>
		/// Select a calendar.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="calendarName">The name of the calendar.</param>
		/// <returns>The Calendar.</returns>
		ICalendar SelectCalendar(ConnectionAndTransactionHolder conn, string calendarName);

		/// <summary>
		/// Check whether or not a calendar is referenced by any triggers.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="calendarName">The name of the calendar.</param>
		/// <returns>true if any triggers reference the calendar, false otherwise</returns>
		bool CalendarIsReferenced(ConnectionAndTransactionHolder conn, string calendarName);

		/// <summary>
		/// Delete a calendar.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <param name="calendarName">The name of the trigger.</param>
		/// <returns>The number of rows deleted.</returns>
		int DeleteCalendar(ConnectionAndTransactionHolder conn, string calendarName);

		/// <summary> 
		/// Select the total number of calendars stored.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <returns>The total number of calendars stored.</returns>
		int SelectNumCalendars(ConnectionAndTransactionHolder conn);

		/// <summary>
		/// Select all of the stored calendars.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <returns>An array of <see cref="String" /> calendar names.</returns>
		IList<string> SelectCalendars(ConnectionAndTransactionHolder conn);

		//---------------------------------------------------------------------------
		// trigger firing
		//---------------------------------------------------------------------------

		/// <summary>
		/// Select the trigger that will be fired at the given fire time.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <param name="fireTime">The time that the trigger will be fired.</param>
		/// <returns> 
		/// A <see cref="Key" /> representing the
		/// trigger that will be fired at the given fire time, or null if no
		/// trigger will be fired at that time
		/// </returns>
		Key SelectTriggerForFireTime(ConnectionAndTransactionHolder conn, DateTime fireTime);

        /// <summary>
        /// Insert a fired trigger.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="trigger">The trigger.</param>
        /// <param name="state">The state that the trigger should be stored in.</param>
        /// <param name="jobDetail">The job detail.</param>
        /// <returns>The number of rows inserted.</returns>
		int InsertFiredTrigger(ConnectionAndTransactionHolder conn, Trigger trigger, string state, JobDetail jobDetail);

        /// <summary>
        /// Select the states of all fired-trigger records for a given trigger, or
        /// trigger group if trigger name is <see langword="null" />.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="triggerName">Name of the trigger.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns>A list of FiredTriggerRecord objects.</returns>
		IList<FiredTriggerRecord> SelectFiredTriggerRecords(ConnectionAndTransactionHolder conn, string triggerName, string groupName);

        /// <summary>
        /// Select the states of all fired-trigger records for a given job, or job
        /// group if job name is <see langword="null" />.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="jobName">Name of the job.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns>A List of FiredTriggerRecord objects.</returns>
        IList<FiredTriggerRecord> SelectFiredTriggerRecordsByJob(ConnectionAndTransactionHolder conn, string jobName, string groupName);

        /// <summary>
        /// Select the states of all fired-trigger records for a given scheduler
        /// instance.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="instanceName">Name of the instance.</param>
        /// <returns>A list of FiredTriggerRecord objects.</returns>
        IList<FiredTriggerRecord> SelectInstancesFiredTriggerRecords(ConnectionAndTransactionHolder conn, string instanceName);

		/// <summary>
		/// Delete a fired trigger.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <param name="entryId">The fired trigger entry to delete.</param>
		/// <returns>The number of rows deleted.</returns>
		int DeleteFiredTrigger(ConnectionAndTransactionHolder conn, string entryId);

        /// <summary>
        /// Get the number instances of the identified job currently executing.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="jobName">Name of the job.</param>
        /// <param name="jobGroup">The job group.</param>
        /// <returns>
        /// The number instances of the identified job currently executing.
        /// </returns>
		int SelectJobExecutionCount(ConnectionAndTransactionHolder conn, string jobName, string jobGroup);

        /// <summary>
        /// Insert a scheduler-instance state record.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="instanceId">The instance id.</param>
        /// <param name="checkInTime">The check in time.</param>
        /// <param name="interval">The interval.</param>
        /// <returns>The number of inserted rows.</returns>
		int InsertSchedulerState(ConnectionAndTransactionHolder conn, string instanceId, DateTime checkInTime, TimeSpan interval);

        /// <summary>
        /// Delete a scheduler-instance state record.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="instanceId">The instance id.</param>
        /// <returns>The number of deleted rows.</returns>
		int DeleteSchedulerState(ConnectionAndTransactionHolder conn, string instanceId);


		/// <summary>
		/// Update a scheduler-instance state record.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <param name="instanceId">The instance id.</param>
		/// <param name="checkInTime">The check in time.</param>
		/// <returns>The number of updated rows.</returns>
		int UpdateSchedulerState(ConnectionAndTransactionHolder conn, string instanceId, DateTime checkInTime);

        /// <summary>
        /// A List of all current <see cref="SchedulerStateRecord" />s.
        /// <p>
        /// If instanceId is not null, then only the record for the identified
        /// instance will be returned.
        /// </p>
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="instanceName">The instance id.</param>
        /// <returns></returns>
		IList<SchedulerStateRecord> SelectSchedulerStateRecords(ConnectionAndTransactionHolder conn, string instanceName);

        /// <summary>
        /// Select the next trigger which will fire to fire between the two given timestamps 
        /// in ascending order of fire time, and then descending by priority.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="noLaterThan">highest value of <see cref="Trigger.GetNextFireTimeUtc" /> of the triggers (exclusive)</param>
        /// <param name="noEarlierThan">highest value of <see cref="Trigger.GetNextFireTimeUtc" /> of the triggers (inclusive)</param>
        /// <returns>A (never null, possibly empty) list of the identifiers (Key objects) of the next triggers to be fired.</returns>
        IList<Key> SelectTriggerToAcquire(ConnectionAndTransactionHolder conn, DateTime noLaterThan, DateTime noEarlierThan);

        /// <summary>
        /// Select the distinct instance names of all fired-trigger records.
        /// </summary>
        /// <remarks>
        /// This is useful when trying to identify orphaned fired triggers (a 
        /// fired trigger without a scheduler state record.) 
        /// </remarks>
        /// <param name="conn">The conn.</param>
        /// <returns></returns>
        ISet<string> SelectFiredTriggerInstanceNames(ConnectionAndTransactionHolder conn);

        /// <summary>
        /// Counts the misfired triggers in states.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="state1">The state1.</param>
        /// <param name="state2">The state2.</param>
        /// <param name="ts">The ts.</param>
        /// <returns></returns>
        int CountMisfiredTriggersInStates(ConnectionAndTransactionHolder conn, string state1, string state2, DateTime ts);

        /// <summary>
        /// Selects the misfired triggers in states.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="state1">The state1.</param>
        /// <param name="state2">The state2.</param>
        /// <param name="ts">The ts.</param>
        /// <param name="count">The count.</param>
        /// <param name="resultList">The result list.</param>
        /// <returns></returns>
        bool SelectMisfiredTriggersInStates(ConnectionAndTransactionHolder conn, string state1, string state2, DateTime ts, int count, IList<Key> resultList);
	}
}