/* 
* Copyright 2004-2005 OpenSymphony 
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

/*
* Previously Copyright (c) 2001-2004 James House
*/
using System;
using System.Collections;
using System.Data;

using Nullables;

using Quartz.Collection;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// This is the base interface for all driver delegate classes.
    /// <p>
    /// This interface is very similar to the <code>IJobStore</code>
    /// interface except each method has an additional <code>Connection}</code>
    /// parameter.
    /// </p>
    /// 	<p>
    /// Unless a database driver has some <strong>extremely-DB-specific</strong>
    /// requirements, any IDriverDelegate implementation classes should extend the
    /// <code>StdAdoDelegate</code> class.
    /// </p>
    /// </summary>
    /// <author>
    /// 	<a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a>
    /// </author>
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
		int UpdateTriggerStatesFromOtherStates(IDbConnection conn, string newState, string oldState1, string oldState2);

        /// <summary>
        /// Get the names of all of the triggers that have misfired - according to
        /// the given timestamp.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="timestamp">The timestamp.</param>
        /// <returns>An array of <code>Key</code> objects</returns>
		Key[] SelectMisfiredTriggers(IDbConnection conn, long timestamp);

        /// <summary>
        /// Get the names of all of the triggers in the given state that have
        /// misfired - according to the given timestamp.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="state">The state.</param>
        /// <param name="ts">The time stamp.</param>
        /// <returns>An array of <code>Key</code> objects</returns>
		Key[] SelectMisfiredTriggersInState(IDbConnection conn, string state, long ts);

        /// <summary>
        /// Get the names of all of the triggers in the given group and state that
        /// have misfired - according to the given timestamp.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="groupName">Name of the group.</param>
        /// <param name="state">The state.</param>
        /// <param name="ts">The timestamp.</param>
        /// <returns>An array of <code>Key</code> objects</returns>
		Key[] SelectMisfiredTriggersInGroupInState(IDbConnection conn, string groupName, string state, long ts);

		/// <summary> 
		/// Select all of the triggers for jobs that are requesting recovery. The
		/// returned trigger objects will have unique "recoverXXX" trigger names and
		/// will be in the <code>Scheduler.DEFAULT_RECOVERY_GROUP</code> trigger group.
		///
		/// <p>
		/// In order to preserve the ordering of the triggers, the fire time will be
		/// set from the <code>COL_FIRED_TIME</code> column in the <code>TABLE_FIRED_TRIGGERS</code>
		/// table. The caller is responsible for calling <code>computeFirstFireTime</code>
		/// on each returned trigger. It is also up to the caller to insert the
		/// returned triggers to ensure that they are fired.
		/// </p>
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <returns>An array of <code>Trigger</code> objects</returns>
		Trigger[] SelectTriggersForRecoveringJobs(IDbConnection conn);

		/// <summary>
		/// Delete all fired triggers.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <returns>The number of rows deleted</returns>
		int DeleteFiredTriggers(IDbConnection conn);

        /// <summary>
        /// Delete all fired triggers of the given instance.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="instanceId">The instance id.</param>
        /// <returns>The number of rows deleted</returns>
		int DeleteFiredTriggers(IDbConnection conn, string instanceId);

		/// <summary>
		/// Delete all volatile fired triggers.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <returns>The number of rows deleted</returns>
		int DeleteVolatileFiredTriggers(IDbConnection conn);

		/// <summary>
		/// Get the names of all of the triggers that are volatile.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <returns>An array of <code>Key</code> objects.</returns>
		Key[] SelectVolatileTriggers(IDbConnection conn);

		/// <summary>
		/// Get the names of all of the jobs that are volatile.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <returns>An array of <code>Key</code> objects.</returns>
		Key[] SelectVolatileJobs(IDbConnection conn);

		//---------------------------------------------------------------------------
		// jobs
		//---------------------------------------------------------------------------

		/// <summary>
		/// Insert the job detail record.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <param name="job">The job to insert.</param>
		/// <returns>Number of rows inserted.</returns>
		int InsertJobDetail(IDbConnection conn, JobDetail job);

		/// <summary>
		/// Update the job detail record.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="job">The job to update.</param>
		/// <returns>Number of rows updated.</returns>
		int UpdateJobDetail(IDbConnection conn, JobDetail job);

		/// <summary> <p>
		/// Get the names of all of the triggers associated with the given job.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="jobName">The job name</param>
        /// <param name="groupName">The job group</param>
		Key[] SelectTriggerNamesForJob(IDbConnection conn, string jobName, string groupName);

		/// <summary>
		/// Delete all job listeners for the given job.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="jobName">The name of the job</param>
        /// <param name="groupName">The group containing the job</param>
		/// <returns>The number of rows deleted</returns>
		int DeleteJobListeners(IDbConnection conn, string jobName, string groupName);

		/// <summary>
		/// Delete the job detail record for the given job.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <param name="jobName">the name of the job</param>
		/// <param name="groupName">Name of the group.</param>
		/// <returns>the number of rows deleted</returns>
		int DeleteJobDetail(IDbConnection conn, string jobName, string groupName);

		/// <summary>
		/// Check whether or not the given job is stateful.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="jobName">The name of the job</param>
        /// <param name="groupName">The group containing the job</param>
		/// <returns> true if the job exists and is stateful, false otherwise</returns>
		bool IsJobStateful(IDbConnection conn, string jobName, string groupName);

		/// <summary>
		/// Check whether or not the given job exists.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <param name="jobName">Name of the job.</param>
		/// <param name="groupName">Name of the group.</param>
		/// <returns>true if the job exists, false otherwise</returns>
		bool JobExists(IDbConnection conn, string jobName, string groupName);

		/// <summary>
		/// Update the job data map for the given job.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <param name="job">The job.</param>
		/// <returns>the number of rows updated</returns>
		int UpdateJobData(IDbConnection conn, JobDetail job);

		/// <summary>
		/// Associate a listener with a job.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <param name="job">The job to associate with the listener.</param>
		/// <param name="listener">The listener to insert.</param>
		/// <returns>The number of rows inserted.</returns>
		int InsertJobListener(IDbConnection conn, JobDetail job, string listener);

		/// <summary> <p>
		/// Get all of the listeners for a given job.
		/// </p>
		/// 
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="jobName">The job name whose listeners are wanted</param>
        /// <param name="groupName">The group containing the job</param>
		/// <returns> array of <code>String</code> listener names</returns>
		string[] SelectJobListeners(IDbConnection conn, string jobName, string groupName);

        /// <summary>
        /// Select the JobDetail object for a given job name / group name.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="jobName">The job name whose listeners are wanted</param>
        /// <param name="groupName">The group containing the job</param>
        /// <param name="classLoadHelper">The class load helper.</param>
        /// <returns>The populated JobDetail object</returns>
		JobDetail SelectJobDetail(IDbConnection conn, string jobName, string groupName, IClassLoadHelper classLoadHelper);

		/// <summary>
		/// Select the total number of jobs stored.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <returns> the total number of jobs stored</returns>
		int SelectNumJobs(IDbConnection conn);

		/// <summary> 
		/// Select all of the job group names that are stored.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <returns> an array of <code>String</code> group names</returns>
		string[] SelectJobGroups(IDbConnection conn);

		/// <summary>
		/// Select all of the jobs contained in a given group.
		/// </summary>
		/// <param name="conn">The DB Connection </param>
        /// <param name="groupName">The group containing the jobs</param>
		/// <returns> an array of <code>String</code> job names</returns>
		string[] SelectJobsInGroup(IDbConnection conn, string groupName);

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
		int InsertTrigger(IDbConnection conn, Trigger trigger, string state, JobDetail jobDetail);

		/// <summary>
		/// Insert the simple trigger data.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="trigger">The trigger to insert</param>
		/// <returns>The number of rows inserted</returns>
		int InsertSimpleTrigger(IDbConnection conn, SimpleTrigger trigger);

		/// <summary>
		/// Insert the blob trigger data.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="trigger">The trigger to insert</param>
		/// <returns>The number of rows inserted</returns>
		int InsertBlobTrigger(IDbConnection conn, Trigger trigger);

        /// <summary>
        /// Insert the cron trigger data.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="trigger">The trigger.</param>
        /// <returns>the number of rows inserted</returns>
		int InsertCronTrigger(IDbConnection conn, CronTrigger trigger);

        /// <summary>
        /// Update the base trigger data.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="trigger">The trigger.</param>
        /// <param name="state">The state.</param>
        /// <param name="jobDetail">The job detail.</param>
        /// <returns>the number of rows updated</returns>
		int UpdateTrigger(IDbConnection conn, Trigger trigger, string state, JobDetail jobDetail);

        /// <summary>
        /// Update the simple trigger data.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="trigger">The trigger.</param>
        /// <returns>the number of rows updated</returns>
		int UpdateSimpleTrigger(IDbConnection conn, SimpleTrigger trigger);

        /// <summary>
        /// Update the cron trigger data.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="trigger">The trigger.</param>
        /// <returns>the number of rows updated</returns>
		int UpdateCronTrigger(IDbConnection conn, CronTrigger trigger);

        /// <summary>
        /// Update the blob trigger data.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="trigger">The trigger.</param>
        /// <returns>the number of rows updated</returns>
		int UpdateBlobTrigger(IDbConnection conn, Trigger trigger);

        /// <summary>
        /// Check whether or not a trigger exists.
        /// </summary>
        /// <param name="conn">the DB Connection</param>
        /// <param name="triggerName">Name of the trigger.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns>the number of rows updated</returns>
		bool TriggerExists(IDbConnection conn, string triggerName, string groupName);

		/// <summary>
		/// Update the state for a given trigger.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="triggerName">The name of the trigger.</param>
        /// <param name="groupName">The group containing the trigger.</param>
        /// <param name="state">The new state for the trigger.</param>
		/// <returns> the number of rows updated</returns>
		int UpdateTriggerState(IDbConnection conn, string triggerName, string groupName, string state);

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
		int UpdateTriggerStateFromOtherState(IDbConnection conn, string triggerName, string groupName, string newState,
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
		
		int UpdateTriggerStateFromOtherStates(IDbConnection conn, string triggerName, string groupName, string newState,
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
		int UpdateTriggerStateFromOtherStatesBeforeTime(IDbConnection conn, string newState, string oldState1,
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
		int UpdateTriggerGroupStateFromOtherStates(IDbConnection conn, string groupName, string newState, string oldState1,
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
		int UpdateTriggerGroupStateFromOtherState(IDbConnection conn, string groupName, string newState, string oldState);

		/// <summary>
		/// Update the states of all triggers associated with the given job.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="jobName">The name of the job.</param>
        /// <param name="groupName">The group containing the job.</param>
        /// <param name="state">The new state for the triggers.</param>
		/// <returns>The number of rows updated</returns>
		int UpdateTriggerStatesForJob(IDbConnection conn, string jobName, string groupName, string state);

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
		int UpdateTriggerStatesForJobFromOtherState(IDbConnection conn, string jobName, string groupName, string state,
		                                            string oldState);

		/// <summary>
		/// Delete all of the listeners associated with a given trigger.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="triggerName">The name of the trigger whose listeners will be deleted</param>
        /// <param name="groupName">The name of the group containing the trigger</param>
		/// <returns> the number of rows deleted</returns>
		int DeleteTriggerListeners(IDbConnection conn, string triggerName, string groupName);

		/// <summary>
		/// Associate a listener with the given trigger.
		/// </summary>
		/// <param name="conn">The DB Connectio</param>
        /// <param name="trigger">The trigger</param>
        /// <param name="listener">The name of the listener to associate with the trigger</param>
		/// <returns> the number of rows inserted </returns>
		int InsertTriggerListener(IDbConnection conn, Trigger trigger, string listener);

		/// <summary>
		/// Select the listeners associated with a given trigger.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="triggerName">The name of the trigger</param>
        /// <param name="groupName">The group containing the trigger</param>
		/// <returns> array of <code>String</code> trigger listener names </returns>
		string[] SelectTriggerListeners(IDbConnection conn, string triggerName, string groupName);

		/// <summary>
		/// Delete the simple trigger data for a trigger.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="triggerName">The name of the trigger</param>
        /// <param name="groupName">The group containing the trigger</param>
		/// <returns>The number of rows deleted</returns>
		int DeleteSimpleTrigger(IDbConnection conn, string triggerName, string groupName);

		/// <summary>
		/// Delete the BLOB trigger data for a trigger.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <param name="triggerName">The name of the trigger</param>
		/// <param name="groupName">The group containing the trigger</param>
		/// <returns>The number of rows deleted</returns>
		int DeleteBlobTrigger(IDbConnection conn, string triggerName, string groupName);

		/// <summary>
		/// Delete the cron trigger data for a trigger.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <param name="triggerName">The name of the trigger</param>
		/// <param name="groupName">The group containing the trigger </param>
		/// <returns> the number of rows deleted </returns>		
		int DeleteCronTrigger(IDbConnection conn, string triggerName, string groupName);

		/// <summary>
		/// Delete the base trigger data for a trigger.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="triggerName">The name of the trigger</param>
        /// <param name="groupName">The group containing the trigger</param>
		/// <returns> the number of rows deleted </returns>
		int DeleteTrigger(IDbConnection conn, string triggerName, string groupName);

		/// <summary>
		/// Select the number of triggers associated with a given job.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="jobName">The name of the job</param>
        /// <param name="groupName">The group containing the job</param>
		/// <returns> the number of triggers for the given job </returns>
		int SelectNumTriggersForJob(IDbConnection conn, string jobName, string groupName);

        /// <summary>
        /// Select the job to which the trigger is associated.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="triggerName">The name of the trigger</param>
        /// <param name="groupName">The group containing the trigger</param>
        /// <param name="loadHelper">The load helper.</param>
        /// <returns>
        /// The <code>JobDetail}</code> object associated with the given trigger
        /// </returns>
		JobDetail SelectJobForTrigger(IDbConnection conn, string triggerName, string groupName, IClassLoadHelper loadHelper);

		/// <summary>
		/// Select the stateful jobs which are referenced by triggers in the given
		/// trigger group.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="groupName">The trigger group.</param>
		/// <returns> a List of Keys to jobs. </returns>
		IList SelectStatefulJobsOfTriggerGroup(IDbConnection conn, string groupName);

		/// <summary>
		/// Select the triggers for a job>
		/// </summary>
		/// <param name="conn">The DB Connection</param>
        /// <param name="jobName">The name of the trigger</param>
        /// <param name="groupName">The group containing the trigger</param>
		/// <returns> an array of <code>Trigger</code> objects associated with a given job. </returns>
		Trigger[] SelectTriggersForJob(IDbConnection conn, string jobName, string groupName);

        /// <summary>
        /// Select the triggers for a calendar
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <param name="calName">Name of the calebdar.</param>
        /// <returns>
        /// An array of <code>Trigger</code> objects associated with a given job.
        /// </returns>
		Trigger[] SelectTriggersForCalendar(IDbConnection conn, string calName);

		/// <summary>
		/// Select a trigger.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="triggerName">The name of the trigger.</param>
		/// <param name="groupName">The group containing the trigger.</param>
		/// <returns>The <code>Trigger</code> object.
		/// </returns>
		Trigger SelectTrigger(IDbConnection conn, string triggerName, string groupName);

		/// <summary>
		/// Select a trigger's JobDataMap.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="triggerName">The name of the trigger.</param>
		/// <param name="groupName">The group containing the trigger.</param>
		/// <returns>The <code>JobDataMap</code> of the Trigger, never null, but possibly empty.</returns>
		JobDataMap SelectTriggerJobDataMap(IDbConnection conn, string triggerName, string groupName);

		/// <summary>
		/// Select a trigger's state value.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="triggerName">The name of the trigger.</param>
		/// <param name="groupName">The group containing the trigger.</param>
		/// <returns>The <code>Trigger</code> object.</returns>
		string SelectTriggerState(IDbConnection conn, string triggerName, string groupName);

		/// <summary> 
		/// Select a triggers status (state and next fire time).
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="triggerName">The name of the trigger.</param>
		/// <param name="groupName">The group containing the trigger.</param>
		/// <returns>A <code>TriggerStatus</code> object, or null</returns>
		TriggerStatus SelectTriggerStatus(IDbConnection conn, string triggerName, string groupName);

		/// <summary>
		/// Select the total number of triggers stored.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <returns>The total number of triggers stored.</returns>
		int SelectNumTriggers(IDbConnection conn);

		/// <summary>
		/// Select all of the trigger group names that are stored.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <returns>An array of <code>String</code> group names.</returns>
		string[] SelectTriggerGroups(IDbConnection conn);

		/// <summary>
		/// Select all of the triggers contained in a given group. 
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="groupName">The group containing the triggers.</param>
		/// <returns>An array of <code>String</code> trigger names.</returns>
		string[] SelectTriggersInGroup(IDbConnection conn, string groupName);

		/// <summary>
		/// Select all of the triggers in a given state.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="state">The state the triggers must be in.</param>
		/// <returns>An array of trigger <code>Key</code>s.</returns>
		Key[] SelectTriggersInState(IDbConnection conn, string state);


        /// <summary>
        /// Inserts the paused trigger group.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns></returns>
		int InsertPausedTriggerGroup(IDbConnection conn, string groupName);


        /// <summary>
        /// Deletes the paused trigger group.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns></returns>
		int DeletePausedTriggerGroup(IDbConnection conn, string groupName);


        /// <summary>
        /// Deletes all paused trigger groups.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <returns></returns>
		int DeleteAllPausedTriggerGroups(IDbConnection conn);


        /// <summary>
        /// Determines whether the specified trigger group is paused.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns>
        /// 	<c>true</c> if trigger group is paused; otherwise, <c>false</c>.
        /// </returns>
		bool IsTriggerGroupPaused(IDbConnection conn, string groupName);


        /// <summary>
        /// Selects the paused trigger groups.
        /// </summary>
        /// <param name="conn">The DB Connection.</param>
        /// <returns></returns>
		ISet SelectPausedTriggerGroups(IDbConnection conn);

        /// <summary>
        /// Determines whether given trigger group already exists.
        /// </summary>
        /// <param name="conn">The conn.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns>
        /// 	<c>true</c> if trigger group exists; otherwise, <c>false</c>.
        /// </returns>
		bool IsExistingTriggerGroup(IDbConnection conn, string groupName);

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
		int InsertCalendar(IDbConnection conn, string calendarName, ICalendar calendar);

		/// <summary> 
		/// Update a calendar.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="calendarName">The name for the new calendar.</param>
		/// <param name="calendar">The calendar.</param>
		/// <returns>The number of rows updated.</returns>
		int UpdateCalendar(IDbConnection conn, string calendarName, ICalendar calendar);

		/// <summary>
		/// Check whether or not a calendar exists.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="calendarName">The name of the calendar.</param>
		/// <returns>true if the trigger exists, false otherwise.</returns>
		bool CalendarExists(IDbConnection conn, string calendarName);

		/// <summary>
		/// Select a calendar.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="calendarName">The name of the calendar.</param>
		/// <returns>The Calendar.</returns>
		ICalendar SelectCalendar(IDbConnection conn, string calendarName);

		/// <summary>
		/// Check whether or not a calendar is referenced by any triggers.
		/// </summary>
		/// <param name="conn">The DB Connection.</param>
		/// <param name="calendarName">The name of the calendar.</param>
		/// <returns>true if any triggers reference the calendar, false otherwise</returns>
		bool CalendarIsReferenced(IDbConnection conn, string calendarName);

		/// <summary>
		/// Delete a calendar.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <param name="calendarName">The name of the trigger.</param>
		/// <returns>The number of rows deleted.</returns>
		int DeleteCalendar(IDbConnection conn, string calendarName);

		/// <summary> 
		/// Select the total number of calendars stored.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <returns>The total number of calendars stored.</returns>
		int SelectNumCalendars(IDbConnection conn);

		/// <summary>
		/// Select all of the stored calendars.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <returns>An array of <code>String</code> calendar names.</returns>
		string[] SelectCalendars(IDbConnection conn);

		//---------------------------------------------------------------------------
		// trigger firing
		//---------------------------------------------------------------------------

		/// <summary>
		/// Select the next time that a trigger will be fired.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <returns>The next fire time, or 0 if no trigger will be fired.</returns>
		NullableDateTime SelectNextFireTime(IDbConnection conn);

		/// <summary>
		/// Select the trigger that will be fired at the given fire time.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <param name="fireTime">The time that the trigger will be fired.</param>
		/// <returns> 
		/// A <code>Key</code> representing the
		/// trigger that will be fired at the given fire time, or null if no
		/// trigger will be fired at that time
		/// </returns>
		Key SelectTriggerForFireTime(IDbConnection conn, DateTime fireTime);

        /// <summary>
        /// Insert a fired trigger.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="trigger">The trigger.</param>
        /// <param name="state">The state that the trigger should be stored in.</param>
        /// <param name="jobDetail">The job detail.</param>
        /// <returns>The number of rows inserted.</returns>
		int InsertFiredTrigger(IDbConnection conn, Trigger trigger, string state, JobDetail jobDetail);

        /// <summary>
        /// Select the states of all fired-trigger records for a given trigger, or
        /// trigger group if trigger name is <code>null</code>.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="triggerName">Name of the trigger.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns>A list of FiredTriggerRecord objects.</returns>
		IList SelectFiredTriggerRecords(IDbConnection conn, string triggerName, string groupName);

        /// <summary>
        /// Select the states of all fired-trigger records for a given job, or job
        /// group if job name is <code>null</code>.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="jobName">Name of the job.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns>A List of FiredTriggerRecord objects.</returns>
		IList SelectFiredTriggerRecordsByJob(IDbConnection conn, string jobName, string groupName);

        /// <summary>
        /// Select the states of all fired-trigger records for a given scheduler
        /// instance.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="instanceName">Name of the instance.</param>
        /// <returns>A list of FiredTriggerRecord objects.</returns>
		IList SelectInstancesFiredTriggerRecords(IDbConnection conn, string instanceName);

		/// <summary>
		/// Delete a fired trigger.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <param name="entryId">The fired trigger entry to delete.</param>
		/// <returns>The number of rows deleted.</returns>
		int DeleteFiredTrigger(IDbConnection conn, string entryId);

        /// <summary>
        /// Get the number instances of the identified job currently executing.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="jobName">Name of the job.</param>
        /// <param name="jobGroup">The job group.</param>
        /// <returns>
        /// The number instances of the identified job currently executing.
        /// </returns>
		int SelectJobExecutionCount(IDbConnection conn, string jobName, string jobGroup);

        /// <summary>
        /// Insert a scheduler-instance state record.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="instanceId">The instance id.</param>
        /// <param name="checkInTime">The check in time.</param>
        /// <param name="interval">The interval.</param>
        /// <param name="recoverer">The recoverer.</param>
        /// <returns>The number of inserted rows.</returns>
		int InsertSchedulerState(IDbConnection conn, string instanceId, long checkInTime, long interval, string recoverer);

        /// <summary>
        /// Delete a scheduler-instance state record.
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="instanceId">The instance id.</param>
        /// <returns>The number of deleted rows.</returns>
		int DeleteSchedulerState(IDbConnection conn, string instanceId);


		/// <summary>
		/// Update a scheduler-instance state record.
		/// </summary>
		/// <param name="conn">The DB Connection</param>
		/// <param name="instanceId">The instance id.</param>
		/// <param name="checkInTime">The check in time.</param>
		/// <param name="recoverer">The recoverer.</param>
		/// <returns>The number of updated rows.</returns>
		int UpdateSchedulerState(IDbConnection conn, string instanceId, long checkInTime, string recoverer);

        /// <summary>
        /// A List of all current <code>SchedulerStateRecords</code>.
        /// <p>
        /// If instanceId is not null, then only the record for the identified
        /// instance will be returned.
        /// </p>
        /// </summary>
        /// <param name="conn">The DB Connection</param>
        /// <param name="instanceId">The instance id.</param>
        /// <returns></returns>
		IList SelectSchedulerStateRecords(IDbConnection conn, string instanceId);
	}
}