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

		/// <summary>
		/// Called by the QuartzScheduler to inform the <code>JobStore</code> that
		/// the scheduler has started.
		/// </summary>
		void SchedulerStarted();

		/// <summary>
		/// Called by the QuartzScheduler to inform the <code>JobStore</code> that
		/// it should free up all of it's resources because the scheduler is
		/// shutting down.
		/// </summary>
		void Shutdown();

	    /// <summary>
	    /// Supports the persistence.
	    /// </summary>
	    /// <returns></returns>
	    bool SupportsPersistence { get; }


        /// <summary>
        /// Store the given <code>JobDetail</code> and <code>Trigger</code>.
        /// </summary>
        /// <param name="ctx">The scheduling context.</param>
        /// <param name="newJob">The <code>JobDetail</code> to be stored.</param>
        /// <param name="newTrigger">The <code>Trigger</code> to be stored.</param>
        /// <throws>  ObjectAlreadyExistsException </throws>
		void StoreJobAndTrigger(SchedulingContext ctx, JobDetail newJob, Trigger newTrigger);

        /// <summary>
        /// Store the given <code>JobDetail</code>.
        /// </summary>
        /// <param name="ctx">The context.</param>
        /// <param name="newJob">The <code>JobDetail</code> to be stored.</param>
        /// <param name="replaceExisting">
        /// If <code>true</code>, any <code>Job</code> existing in the
        /// <code>JobStore</code> with the same name and group should be
        /// over-written.
        /// </param>
		void StoreJob(SchedulingContext ctx, JobDetail newJob, bool replaceExisting);

        /// <summary>
        /// Remove (delete) the <code>IJob</code> with the given
        /// name, and any <code>Trigger</code> s that reference
        /// it.
        /// <p>
        /// If removal of the <code>Job</code> results in an empty group, the
        /// group should be removed from the <code>JobStore</code>'s list of
        /// known group names.
        /// </p>
        /// </summary>
        /// <param name="ctx">The context.</param>
        /// <param name="jobName">The name of the <code>Job</code> to be removed.</param>
        /// <param name="groupName">The group name of the <code>Job</code> to be removed.</param>
        /// <returns>
        /// 	<code>true</code> if a <code>Job</code> with the given name and
        /// group was found and removed from the store.
        /// </returns>
		bool RemoveJob(SchedulingContext ctx, string jobName, string groupName);

        /// <summary>
        /// Retrieve the <code>JobDetail</code> for the given
        /// <code>Job</code>.
        /// </summary>
        /// <param name="ctx">The context.</param>
        /// <param name="jobName">The name of the <code>Job</code> to be retrieved.</param>
        /// <param name="groupName">The group name of the <code>Job</code> to be retrieved.</param>
        /// <returns>
        /// The desired <code>Job</code>, or null if there is no match.
        /// </returns>
		JobDetail RetrieveJob(SchedulingContext ctx, string jobName, string groupName);

        /// <summary>
        /// Store the given <code>Trigger</code>.
        /// </summary>
        /// <param name="ctx">The context.</param>
        /// <param name="newTrigger">The <code>Trigger</code> to be stored.</param>
        /// <param name="replaceExisting">If <code>true</code>, any <code>Trigger</code> existing in
        /// the <code>JobStore</code> with the same name and group should
        /// be over-written.</param>
        /// <throws>  ObjectAlreadyExistsException </throws>
		void StoreTrigger(SchedulingContext ctx, Trigger newTrigger, bool replaceExisting);

        /// <summary>
        /// Remove (delete) the <code>Trigger</code> with the
        /// given name.
        /// <p>
        /// If removal of the <code>Trigger</code> results in an empty group, the
        /// group should be removed from the <code>JobStore</code>'s list of
        /// known group names.
        /// </p>
        /// <p>
        /// If removal of the <code>Trigger</code> results in an 'orphaned' <code>Job</code>
        /// that is not 'durable', then the <code>Job</code> should be deleted
        /// also.
        /// </p>
        /// </summary>
        /// <param name="ctx">The context.</param>
        /// <param name="triggerName">The name of the <code>Trigger</code> to be removed.</param>
        /// <param name="groupName">The group name of the <code>Trigger</code> to be removed.</param>
        /// <returns>
        /// 	<code>true</code> if a <code>Trigger</code> with the given
        /// name and group was found and removed from the store.
        /// </returns>
		bool RemoveTrigger(SchedulingContext ctx, string triggerName, string groupName);

        /// <summary>
        /// Remove (delete) the <code>Trigger</code> with the
        /// given name, and store the new given one - which must be associated
        /// with the same job.
        /// </summary>
        /// <param name="ctx">The context.</param>
        /// <param name="triggerName">The name of the <code>Trigger</code> to be removed.</param>
        /// <param name="groupName">The group name of the <code>Trigger</code> to be removed.</param>
        /// <param name="newTrigger">The new <code>Trigger</code> to be stored.</param>
        /// <returns>
        /// 	<code>true</code> if a <code>Trigger</code> with the given
        /// name and group was found and removed from the store.
        /// </returns>
		bool ReplaceTrigger(SchedulingContext ctx, string triggerName, string groupName, Trigger newTrigger);


        /// <summary>
        /// Retrieve the given <code>Trigger</code>.
        /// </summary>
        /// <param name="ctx">The context.</param>
        /// <param name="triggerName">The name of the <code>Trigger</code> to be retrieved.</param>
        /// <param name="groupName">The group name of the <code>Trigger</code> to be retrieved.</param>
        /// <returns>
        /// The desired <code>Trigger</code>, or null if there is no
        /// match.
        /// </returns>
		Trigger RetrieveTrigger(SchedulingContext ctx, string triggerName, string groupName);

        /// <summary>
        /// Store the given <code>Calendar</code>.
        /// </summary>
        /// <param name="ctx">The context.</param>
        /// <param name="name">The name.</param>
        /// <param name="calendar">The <code>Calendar</code> to be stored.</param>
        /// <param name="replaceExisting">If <code>true</code>, any <code>Calendar</code> existing
        /// in the <code>JobStore</code> with the same name and group
        /// should be over-written.</param>
        /// <param name="updateTriggers">If <code>true</code>, any <code>Trigger</code>s existing
        /// in the <code>JobStore</code> that reference an existing
        /// Calendar with the same name with have their next fire time
        /// re-computed with the new <code>Calendar</code>.</param>
        /// <throws>  ObjectAlreadyExistsException </throws>
		void StoreCalendar(SchedulingContext ctx, string name, ICalendar calendar, bool replaceExisting, bool updateTriggers);

        /// <summary>
        /// Remove (delete) the <code>ICalendar</code> with the
        /// given name.
        /// <p>
        /// If removal of the <code>Calendar</code> would result in
        /// <code>Trigger</code>s pointing to non-existent calendars, then a
        /// <code>JobPersistenceException</code> will be thrown.</p>
        /// *
        /// </summary>
        /// <param name="ctx">The context.</param>
        /// <param name="calName">The name of the <code>Calendar</code> to be removed.</param>
        /// <returns>
        /// 	<code>true</code> if a <code>Calendar</code> with the given name
        /// was found and removed from the store.
        /// </returns>
		bool RemoveCalendar(SchedulingContext ctx, string calName);

        /// <summary>
        /// Retrieve the given <code>Trigger</code>.
        /// </summary>
        /// <param name="ctx">The context.</param>
        /// <param name="calName">The name of the <code>Calendar</code> to be retrieved.</param>
        /// <returns>
        /// The desired <code>Calendar</code>, or null if there is no
        /// match.
        /// </returns>
		ICalendar RetrieveCalendar(SchedulingContext ctx, string calName);


        /// <summary>
        /// Get the number of <code>Job</code>s that are
        /// stored in the <code>JobsStore</code>.
        /// </summary>
        /// <param name="ctx">The context.</param>
        /// <returns></returns>
		int GetNumberOfJobs(SchedulingContext ctx);

        /// <summary>
        /// Get the number of <code>Trigger</code>s that are
        /// stored in the <code>JobsStore</code>.
        /// </summary>
        /// <param name="ctx">The context.</param>
        /// <returns></returns>
		int GetNumberOfTriggers(SchedulingContext ctx);

        /// <summary>
        /// Get the number of <code>Calendar</code> s that are
        /// stored in the <code>JobsStore</code>.
        /// </summary>
        /// <param name="ctx">The context.</param>
        /// <returns></returns>
		int GetNumberOfCalendars(SchedulingContext ctx);

        /// <summary>
        /// Get the names of all of the <code>Job</code> s that
        /// have the given group name.
        /// <p>
        /// If there are no jobs in the given group name, the result should be a
        /// zero-length array (not <code>null</code>).
        /// </p>
        /// </summary>
        /// <param name="ctx">The CTX.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns></returns>
		string[] GetJobNames(SchedulingContext ctx, string groupName);

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
		string[] GetTriggerNames(SchedulingContext ctx, string groupName);

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
		Trigger[] GetTriggersForJob(SchedulingContext ctx, string jobName, string groupName);

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
		int GetTriggerState(SchedulingContext ctx, string triggerName, string triggerGroup);

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
		void PauseTrigger(SchedulingContext ctx, string triggerName, string groupName);

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
		void PauseTriggerGroup(SchedulingContext ctx, string groupName);

		/// <summary> <p>
		/// Pause the <code>{@link org.quartz.Job}</code> with the given name - by
		/// pausing all of its current <code>Trigger</code>s.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="string">
		/// </seealso>
		void PauseJob(SchedulingContext ctx, string jobName, string groupName);

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
		void PauseJobGroup(SchedulingContext ctx, string groupName);

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
		void ResumeTrigger(SchedulingContext ctx, string triggerName, string groupName);

		/// <summary>
		/// Resume (un-pause) all of the <code>Trigger</code>s
		/// in the given group.
		/// <p>
		/// If any <code>Trigger</code> missed one or more fire-times, then the
		/// <code>Trigger</code>'s misfire instruction will be applied.
		/// </p>
		/// </summary>
		void ResumeTriggerGroup(SchedulingContext ctx, string groupName);

        /// <summary>
        /// Gets the paused trigger groups.
        /// </summary>
        /// <param name="ctxt">The context.</param>
        /// <returns></returns>
		ISet GetPausedTriggerGroups(SchedulingContext ctxt);


		/// <summary> 
		/// Resume (un-pause) the <code>Job</code> with the
		/// given name.
		/// <p>
		/// If any of the <code>Job</code>'s<code>Trigger</code> s missed one
		/// or more fire-times, then the <code>Trigger</code>'s misfire
		/// instruction will be applied.
		/// </p>
		/// </summary>
		void ResumeJob(SchedulingContext ctx, string jobName, string groupName);

		/// <summary>
		/// Resume (un-pause) all of the <code>Job</code>s in
		/// the given group.
		/// <p>
		/// If any of the <code>Job</code> s had <code>Trigger</code> s that
		/// missed one or more fire-times, then the <code>Trigger</code>'s
		/// misfire instruction will be applied.
		/// </p> 
		/// </summary>
		void ResumeJobGroup(SchedulingContext ctx, string groupName);

		/// <summary>
		/// Pause all triggers - equivalent of calling <code>PauseTriggerGroup(group)</code>
		/// on every group.
		/// <p>
		/// When <code>ResumeAll()</code> is called (to un-pause), trigger misfire
		/// instructions WILL be applied.
		/// </p>
		/// </summary>
		/// <seealso cref="ResumeAll" />
		void PauseAll(SchedulingContext ctxt);

		/// <summary>
		/// Resume (un-pause) all triggers - equivalent of calling <code>ResumeTriggerGroup(group)</code>
		/// on every group.
		/// <p>
		/// If any <code>Trigger</code> missed one or more fire-times, then the
		/// <code>Trigger</code>'s misfire instruction will be applied.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="PauseAll" />
		void ResumeAll(SchedulingContext ctxt);


        /// <summary>
        /// Get a handle to the next trigger to be fired, and mark it as 'reserved'
        /// by the calling scheduler.
        /// </summary>
        /// <param name="ctx">The context.</param>
        /// <param name="noLaterThan">If &gt; 0, the JobStore should only return a Trigger
        /// that will fire no later than the time represented in this value as
        /// milliseconds.</param>
        /// <returns></returns>
        /// <seealso cref="Trigger">
        /// </seealso>
		Trigger AcquireNextTrigger(SchedulingContext ctx, DateTime noLaterThan);

		/// <summary> 
		/// Inform the <code>JobStore</code> that the scheduler no longer plans to
		/// fire the given <code>Trigger</code>, that it had previously acquired
		/// (reserved).
		/// </summary>
		void ReleaseAcquiredTrigger(SchedulingContext ctx, Trigger trigger);

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
		TriggerFiredBundle TriggerFired(SchedulingContext ctx, Trigger trigger);

		/// <summary> <p>
		/// Inform the <code>JobStore</code> that the scheduler has completed the
		/// firing of the given <code>Trigger</code> (and the execution its
		/// associated <code>Job</code>), and that the <code>{@link org.quartz.JobDataMap}</code>
		/// in the given <code>JobDetail</code> should be updated if the <code>Job</code>
		/// is stateful.
		/// </p>
		/// </summary>
		void TriggeredJobComplete(SchedulingContext ctx, Trigger trigger, JobDetail jobDetail, int triggerInstCode);
	}
}