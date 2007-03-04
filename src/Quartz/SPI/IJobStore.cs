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
using Quartz.Collection;
using Quartz.Core;

namespace Quartz.Spi
{
	/// <summary> 
	/// The interface to be implemented by classes that want to provide a <code>IJob</code>
	/// and <code>Trigger</code> storage mechanism for the
	/// <code>QuartzScheduler</code>'s use.
	/// <p>
	/// Storage of <code>Job</code> s and <code>Trigger</code> s should be keyed
	/// on the combination of their name and group for uniqueness.
	/// </p>
	/// </summary>
	/// <seealso cref="QuartzScheduler" />
	/// <seealso cref="Trigger" />
	/// <seealso cref="IJob" />
	/// <seealso cref="JobDetail" />
	/// <seealso cref="JobDataMap" />
	/// <seealso cref="ICalendar" />
	/// <author>James House</author>
	public interface IJobStore
	{
		/// <summary> <p>
		/// Called by the QuartzScheduler before the <code>JobStore</code> is
		/// used, in order to give the it a chance to Initialize.
		/// </p>
		/// </summary>
		void Initialize(IClassLoadHelper loadHelper, ISchedulerSignaler signaler);

		/// <summary> <p>
		/// Called by the QuartzScheduler to inform the <code>JobStore</code> that
		/// the scheduler has started.
		/// </p>
		/// </summary>
		void SchedulerStarted();

		/// <summary> <p>
		/// Called by the QuartzScheduler to inform the <code>JobStore</code> that
		/// it should free up all of it's resources because the scheduler is
		/// shutting down.
		/// </p>
		/// </summary>
		void Shutdown();

		bool SupportsPersistence();

		/////////////////////////////////////////////////////////////////////////////
		//
		// Job & Trigger Storage methods
		//
		/////////////////////////////////////////////////////////////////////////////
		/// <summary> <p>
		/// Store the given <code>{@link org.quartz.JobDetail}</code> and <code>{@link org.quartz.Trigger}</code>.
		/// </p>
		/// 
		/// </summary>
		/// <param name="newJob">
		/// The <code>JobDetail</code> to be stored.
		/// </param>
		/// <param name="newTrigger">
		/// The <code>Trigger</code> to be stored.
		/// </param>
		/// <throws>  ObjectAlreadyExistsException </throws>
		/// <summary>           if a <code>Job</code> with the same name/group already
		/// exists.
		/// </summary>
		void StoreJobAndTrigger(SchedulingContext ctxt, JobDetail newJob, Trigger newTrigger);

		/// <summary> <p>
		/// Store the given <code>{@link org.quartz.JobDetail}</code>.
		/// </p>
		/// 
		/// </summary>
		/// <param name="newJob">
		/// The <code>JobDetail</code> to be stored.
		/// </param>
		/// <param name="replaceExisting">
		/// If <code>true</code>, any <code>Job</code> existing in the
		/// <code>JobStore</code> with the same name and group should be
		/// over-written.
		/// </param>
		/// <throws>  ObjectAlreadyExistsException </throws>
		/// <summary>           if a <code>Job</code> with the same name/group already
		/// exists, and replaceExisting is set to false.
		/// </summary>
		void StoreJob(SchedulingContext ctxt, JobDetail newJob, bool replaceExisting);

		/// <summary> <p>
		/// Remove (delete) the <code>{@link org.quartz.Job}</code> with the given
		/// name, and any <code>{@link org.quartz.Trigger}</code> s that reference
		/// it.
		/// </p>
		/// 
		/// <p>
		/// If removal of the <code>Job</code> results in an empty group, the
		/// group should be removed from the <code>JobStore</code>'s list of
		/// known group names.
		/// </p>
		/// 
		/// </summary>
		/// <param name="jobName">
		/// The name of the <code>Job</code> to be removed.
		/// </param>
		/// <param name="groupName">
		/// The group name of the <code>Job</code> to be removed.
		/// </param>
		/// <returns> <code>true</code> if a <code>Job</code> with the given name and
		/// group was found and removed from the store.
		/// </returns>
		bool RemoveJob(SchedulingContext ctxt, string jobName, string groupName);

		/// <summary> <p>
		/// Retrieve the <code>{@link org.quartz.JobDetail}</code> for the given
		/// <code>{@link org.quartz.Job}</code>.
		/// </p>
		/// 
		/// </summary>
		/// <param name="jobName">
		/// The name of the <code>Job</code> to be retrieved.
		/// </param>
		/// <param name="groupName">
		/// The group name of the <code>Job</code> to be retrieved.
		/// </param>
		/// <returns> The desired <code>Job</code>, or null if there is no match.
		/// </returns>
		JobDetail RetrieveJob(SchedulingContext ctxt, string jobName, string groupName);

		/// <summary> <p>
		/// Store the given <code>{@link org.quartz.Trigger}</code>.
		/// </p>
		/// 
		/// </summary>
		/// <param name="newTrigger">
		/// The <code>Trigger</code> to be stored.
		/// </param>
		/// <param name="replaceExisting">
		/// If <code>true</code>, any <code>Trigger</code> existing in
		/// the <code>JobStore</code> with the same name and group should
		/// be over-written.
		/// </param>
		/// <throws>  ObjectAlreadyExistsException </throws>
		/// <summary>           if a <code>Trigger</code> with the same name/group already
		/// exists, and replaceExisting is set to false.
		/// 
		/// </summary>
		/// <seealso cref="string">
		/// </seealso>
		void StoreTrigger(SchedulingContext ctxt, Trigger newTrigger, bool replaceExisting);

		/// <summary> <p>
		/// Remove (delete) the <code>{@link org.quartz.Trigger}</code> with the
		/// given name.
		/// </p>
		/// 
		/// <p>
		/// If removal of the <code>Trigger</code> results in an empty group, the
		/// group should be removed from the <code>JobStore</code>'s list of
		/// known group names.
		/// </p>
		/// 
		/// <p>
		/// If removal of the <code>Trigger</code> results in an 'orphaned' <code>Job</code>
		/// that is not 'durable', then the <code>Job</code> should be deleted
		/// also.
		/// </p>
		/// 
		/// </summary>
		/// <param name="triggerName">
		/// The name of the <code>Trigger</code> to be removed.
		/// </param>
		/// <param name="groupName">
		/// The group name of the <code>Trigger</code> to be removed.
		/// </param>
		/// <returns> <code>true</code> if a <code>Trigger</code> with the given
		/// name and group was found and removed from the store.
		/// </returns>
		bool RemoveTrigger(SchedulingContext ctxt, string triggerName, string groupName);

		/// <summary> <p>
		/// Remove (delete) the <code>{@link org.quartz.Trigger}</code> with the
		/// given name, and store the new given one - which must be associated
		/// with the same job.
		/// </p>
		/// 
		/// </summary>
		/// <param name="triggerName">
		/// The name of the <code>Trigger</code> to be removed.
		/// </param>
		/// <param name="groupName">
		/// The group name of the <code>Trigger</code> to be removed.
		/// </param>
		/// <param name="newTrigger">
		/// The new <code>Trigger</code> to be stored.
		/// </param>
		/// <returns> <code>true</code> if a <code>Trigger</code> with the given
		/// name and group was found and removed from the store.
		/// </returns>
		bool ReplaceTrigger(SchedulingContext ctxt, string triggerName, string groupName, Trigger newTrigger);


		/// <summary> <p>
		/// Retrieve the given <code>{@link org.quartz.Trigger}</code>.
		/// </p>
		/// 
		/// </summary>
		/// <param name="triggerName">
		/// The name of the <code>Trigger</code> to be retrieved.
		/// </param>
		/// <param name="groupName">
		/// The group name of the <code>Trigger</code> to be retrieved.
		/// </param>
		/// <returns> The desired <code>Trigger</code>, or null if there is no
		/// match.
		/// </returns>
		Trigger RetrieveTrigger(SchedulingContext ctxt, string triggerName, string groupName);

		/// <summary> <p>
		/// Store the given <code>{@link org.quartz.Calendar}</code>.
		/// </p>
		/// 
		/// </summary>
		/// <param name="calendar">
		/// The <code>Calendar</code> to be stored.
		/// </param>
		/// <param name="replaceExisting">
		/// If <code>true</code>, any <code>Calendar</code> existing
		/// in the <code>JobStore</code> with the same name and group
		/// should be over-written.
		/// </param>
		/// <param name="updateTriggers">
		/// If <code>true</code>, any <code>Trigger</code>s existing
		/// in the <code>JobStore</code> that reference an existing 
		/// Calendar with the same name with have their next fire time
		/// re-computed with the new <code>Calendar</code>.
		/// </param>
		/// <throws>  ObjectAlreadyExistsException </throws>
		/// <summary>           if a <code>Calendar</code> with the same name already
		/// exists, and replaceExisting is set to false.
		/// </summary>
		void StoreCalendar(SchedulingContext ctxt, string name, ICalendar calendar, bool replaceExisting, bool updateTriggers);

		/// <summary>
		/// Remove (delete) the <code>ICalendar</code> with the
		/// given name.
		/// <p>
		/// If removal of the <code>Calendar</code> would result in
		/// <code>Trigger</code>s pointing to non-existent calendars, then a
		/// <code>JobPersistenceException</code> will be thrown.</p>
		/// *
		/// </summary>
		/// <param name="calName">The name of the <code>Calendar</code> to be removed.
		/// </param>
		/// <returns> <code>true</code> if a <code>Calendar</code> with the given name
		/// was found and removed from the store.
		/// </returns>
		bool RemoveCalendar(SchedulingContext ctxt, string calName);

		/// <summary> <p>
		/// Retrieve the given <code>{@link org.quartz.Trigger}</code>.
		/// </p>
		/// 
		/// </summary>
		/// <param name="calName">
		/// The name of the <code>Calendar</code> to be retrieved.
		/// </param>
		/// <returns> The desired <code>Calendar</code>, or null if there is no
		/// match.
		/// </returns>
		ICalendar RetrieveCalendar(SchedulingContext ctxt, string calName);

		/////////////////////////////////////////////////////////////////////////////
		//
		// Informational methods
		//
		/////////////////////////////////////////////////////////////////////////////
		/// <summary> <p>
		/// Get the number of <code>{@link org.quartz.Job}</code> s that are
		/// stored in the <code>JobsStore</code>.
		/// </p>
		/// </summary>
		int GetNumberOfJobs(SchedulingContext ctxt);

		/// <summary> <p>
		/// Get the number of <code>{@link org.quartz.Trigger}</code> s that are
		/// stored in the <code>JobsStore</code>.
		/// </p>
		/// </summary>
		int GetNumberOfTriggers(SchedulingContext ctxt);

		/// <summary> <p>
		/// Get the number of <code>{@link org.quartz.Calendar}</code> s that are
		/// stored in the <code>JobsStore</code>.
		/// </p>
		/// </summary>
		int GetNumberOfCalendars(SchedulingContext ctxt);

		/// <summary> <p>
		/// Get the names of all of the <code>{@link org.quartz.Job}</code> s that
		/// have the given group name.
		/// </p>
		/// 
		/// <p>
		/// If there are no jobs in the given group name, the result should be a
		/// zero-length array (not <code>null</code>).
		/// </p>
		/// </summary>
		string[] GetJobNames(SchedulingContext ctxt, string groupName);

		/// <summary> <p>
		/// Get the names of all of the <code>{@link org.quartz.Trigger}</code> s
		/// that have the given group name.
		/// </p>
		/// 
		/// <p>
		/// If there are no triggers in the given group name, the result should be a
		/// zero-length array (not <code>null</code>).
		/// </p>
		/// </summary>
		string[] GetTriggerNames(SchedulingContext ctxt, string groupName);

		/// <summary> <p>
		/// Get the names of all of the <code>{@link org.quartz.Job}</code>
		/// groups.
		/// </p>
		/// 
		/// <p>
		/// If there are no known group names, the result should be a zero-length
		/// array (not <code>null</code>).
		/// </p>
		/// </summary>
		string[] GetJobGroupNames(SchedulingContext ctxt);

		/// <summary> <p>
		/// Get the names of all of the <code>{@link org.quartz.Trigger}</code>
		/// groups.
		/// </p>
		/// 
		/// <p>
		/// If there are no known group names, the result should be a zero-length
		/// array (not <code>null</code>).
		/// </p>
		/// </summary>
		string[] GetTriggerGroupNames(SchedulingContext ctxt);

		/// <summary> <p>
		/// Get the names of all of the <code>{@link org.quartz.Calendar}</code> s
		/// in the <code>JobStore</code>.
		/// </p>
		/// 
		/// <p>
		/// If there are no Calendars in the given group name, the result should be
		/// a zero-length array (not <code>null</code>).
		/// </p>
		/// </summary>
		string[] GetCalendarNames(SchedulingContext ctxt);

		/// <summary> <p>
		/// Get all of the Triggers that are associated to the given Job.
		/// </p>
		/// 
		/// <p>
		/// If there are no matches, a zero-length array should be returned.
		/// </p>
		/// </summary>
		Trigger[] GetTriggersForJob(SchedulingContext ctxt, string jobName, string groupName);

		/// <summary> <p>
		/// Get the current state of the identified <code>{@link Trigger}</code>.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="Trigger.STATE_NORMAL">
		/// </seealso>
		/// <seealso cref="Trigger.STATE_PAUSED">
		/// </seealso>
		/// <seealso cref="Trigger.STATE_COMPLETE">
		/// </seealso>
		/// <seealso cref="Trigger.STATE_ERROR">
		/// </seealso>
		/// <seealso cref="Trigger.STATE_NONE">
		/// </seealso>
		int GetTriggerState(SchedulingContext ctxt, string triggerName, string triggerGroup);

		/////////////////////////////////////////////////////////////////////////////
		//
		// Trigger State manipulation methods
		//
		/////////////////////////////////////////////////////////////////////////////
		/// <summary> <p>
		/// Pause the <code>{@link org.quartz.Trigger}</code> with the given name.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="string">
		/// </seealso>
		void PauseTrigger(SchedulingContext ctxt, string triggerName, string groupName);

		/// <summary> <p>
		/// Pause all of the <code>{@link org.quartz.Trigger}s</code> in the
		/// given group.
		/// </p>
		/// 
		/// 
		/// <p>
		/// The JobStore should "remember" that the group is paused, and impose the
		/// pause on any new triggers that are added to the group while the group is
		/// paused.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="string">
		/// </seealso>
		void PauseTriggerGroup(SchedulingContext ctxt, string groupName);

		/// <summary> <p>
		/// Pause the <code>{@link org.quartz.Job}</code> with the given name - by
		/// pausing all of its current <code>Trigger</code>s.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="string">
		/// </seealso>
		void PauseJob(SchedulingContext ctxt, string jobName, string groupName);

		/// <summary>
		/// Pause all of the <code>{@link org.quartz.Job}s</code> in the given
		/// group - by pausing all of their <code>Trigger</code>s.
		/// <p>
		/// The JobStore should "remember" that the group is paused, and impose the
		/// pause on any new jobs that are added to the group while the group is
		/// paused.
		/// </p>
		/// </summary>
		/// <seealso cref="string">
		/// </seealso>
		void PauseJobGroup(SchedulingContext ctxt, string groupName);

		/// <summary>
		/// Resume (un-pause) the <code>{@link org.quartz.Trigger}</code> with the
		/// given name.
		/// 
		/// <p>
		/// If the <code>Trigger</code> missed one or more fire-times, then the
		/// <code>Trigger</code>'s misfire instruction will be applied.
		/// </p>
		/// </summary>
		/// <seealso cref="string">
		/// </seealso>
		void ResumeTrigger(SchedulingContext ctxt, string triggerName, string groupName);

		/// <summary> <p>
		/// Resume (un-pause) all of the <code>{@link org.quartz.Trigger}s</code>
		/// in the given group.
		/// </p>
		/// 
		/// <p>
		/// If any <code>Trigger</code> missed one or more fire-times, then the
		/// <code>Trigger</code>'s misfire instruction will be applied.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="string">
		/// </seealso>
		void ResumeTriggerGroup(SchedulingContext ctxt, string groupName);

		ISet GetPausedTriggerGroups(SchedulingContext ctxt);


		/// <summary> <p>
		/// Resume (un-pause) the <code>{@link org.quartz.Job}</code> with the
		/// given name.
		/// </p>
		/// 
		/// <p>
		/// If any of the <code>Job</code>'s<code>Trigger</code> s missed one
		/// or more fire-times, then the <code>Trigger</code>'s misfire
		/// instruction will be applied.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="string">
		/// </seealso>
		void ResumeJob(SchedulingContext ctxt, string jobName, string groupName);

		/// <summary> <p>
		/// Resume (un-pause) all of the <code>{@link org.quartz.Job}s</code> in
		/// the given group.
		/// </p>
		/// 
		/// <p>
		/// If any of the <code>Job</code> s had <code>Trigger</code> s that
		/// missed one or more fire-times, then the <code>Trigger</code>'s
		/// misfire instruction will be applied.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="string">
		/// </seealso>
		void ResumeJobGroup(SchedulingContext ctxt, string groupName);

		/// <summary> <p>
		/// Pause all triggers - equivalent of calling <code>PauseTriggerGroup(group)</code>
		/// on every group.
		/// </p>
		/// 
		/// <p>
		/// When <code>ResumeAll()</code> is called (to un-pause), trigger misfire
		/// instructions WILL be applied.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="ResumeAll">
		/// </seealso>
		/// <seealso cref="string">
		/// </seealso>
		void PauseAll(SchedulingContext ctxt);

		/// <summary> <p>
		/// Resume (un-pause) all triggers - equivalent of calling <code>ResumeTriggerGroup(group)</code>
		/// on every group.
		/// </p>
		/// 
		/// <p>
		/// If any <code>Trigger</code> missed one or more fire-times, then the
		/// <code>Trigger</code>'s misfire instruction will be applied.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="PauseAll">
		/// </seealso>
		void ResumeAll(SchedulingContext ctxt);

		/////////////////////////////////////////////////////////////////////////////
		//
		// Trigger-Firing methods
		//
		/////////////////////////////////////////////////////////////////////////////
		/// <summary> <p>
		/// Get a handle to the next trigger to be fired, and mark it as 'reserved'
		/// by the calling scheduler.
		/// </p>
		/// 
		/// </summary>
		/// <param name="noLaterThan">If > 0, the JobStore should only return a Trigger 
		/// that will fire no later than the time represented in this value as 
		/// milliseconds.
		/// </param>
		/// <seealso cref="Trigger">
		/// </seealso>
		Trigger AcquireNextTrigger(SchedulingContext ctxt, DateTime noLaterThan);

		/// <summary> <p>
		/// Inform the <code>JobStore</code> that the scheduler no longer plans to
		/// fire the given <code>Trigger</code>, that it had previously acquired
		/// (reserved).
		/// </p>
		/// </summary>
		void ReleaseAcquiredTrigger(SchedulingContext ctxt, Trigger trigger);

		/// <summary> <p>
		/// Inform the <code>JobStore</code> that the scheduler is now firing the
		/// given <code>Trigger</code> (executing its associated <code>Job</code>),
		/// that it had previously acquired (reserved).
		/// </p>
		/// 
		/// </summary>
		/// <returns> null if the trigger or it's job or calendar no longer exist, or
		/// if the trigger was not successfully put into the 'executing'
		/// state.
		/// </returns>
		TriggerFiredBundle TriggerFired(SchedulingContext ctxt, Trigger trigger);

		/// <summary> <p>
		/// Inform the <code>JobStore</code> that the scheduler has completed the
		/// firing of the given <code>Trigger</code> (and the execution its
		/// associated <code>Job</code>), and that the <code>{@link org.quartz.JobDataMap}</code>
		/// in the given <code>JobDetail</code> should be updated if the <code>Job</code>
		/// is stateful.
		/// </p>
		/// </summary>
		void TriggeredJobComplete(SchedulingContext ctxt, Trigger trigger, JobDetail jobDetail, int triggerInstCode);
	}
}