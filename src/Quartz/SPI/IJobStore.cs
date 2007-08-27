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
	/// The interface to be implemented by classes that want to provide a <see cref="IJob" />
	/// and <see cref="Trigger" /> storage mechanism for the
	/// <see cref="QuartzScheduler" />'s use.
	/// </summary>
	/// <remarks>
	/// Storage of <see cref="IJob" /> s and <see cref="Trigger" /> s should be keyed
	/// on the combination of their name and group for uniqueness.
	/// </remarks>
	/// <seealso cref="QuartzScheduler" />
	/// <seealso cref="Trigger" />
	/// <seealso cref="IJob" />
	/// <seealso cref="JobDetail" />
	/// <seealso cref="JobDataMap" />
	/// <seealso cref="ICalendar" />
	/// <author>James House</author>
	public interface IJobStore
	{
		/// <summary>
		/// Called by the QuartzScheduler before the <see cref="IJobStore" /> is
		/// used, in order to give the it a chance to Initialize.
		/// </summary>
		void Initialize(IClassLoadHelper loadHelper, ISchedulerSignaler signaler);

		/// <summary>
		/// Called by the QuartzScheduler to inform the <see cref="IJobStore" /> that
		/// the scheduler has started.
		/// </summary>
		void SchedulerStarted();

		/// <summary>
		/// Called by the QuartzScheduler to inform the <see cref="IJobStore" /> that
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
        /// Store the given <see cref="JobDetail" /> and <see cref="Trigger" />.
        /// </summary>
        /// <param name="ctx">The scheduling context.</param>
        /// <param name="newJob">The <see cref="JobDetail" /> to be stored.</param>
        /// <param name="newTrigger">The <see cref="Trigger" /> to be stored.</param>
        /// <throws>  ObjectAlreadyExistsException </throws>
		void StoreJobAndTrigger(SchedulingContext ctx, JobDetail newJob, Trigger newTrigger);


        /// <summary>
        /// returns true if the given JobGroup
        /// is paused
        /// </summary>
        /// <param name="groupName"></param>
        /// <returns></returns>
        bool IsJobGroupPaused(SchedulingContext ctxt, string groupName);
        /// <summary>
        /// returns true if the given TriggerGroup
        /// is paused
        /// </summary>
        /// <param name="groupName"></param>
        /// <returns></returns>
        bool IsTriggerGroupPaused(SchedulingContext ctxt, string groupName);
        

	    
        /// <summary>
        /// Store the given <see cref="JobDetail" />.
        /// </summary>
        /// <param name="ctx">The context.</param>
        /// <param name="newJob">The <see cref="JobDetail" /> to be stored.</param>
        /// <param name="replaceExisting">
        /// If <see langword="true" />, any <see cref="IJob" /> existing in the
        /// <see cref="IJobStore" /> with the same name and group should be
        /// over-written.
        /// </param>
		void StoreJob(SchedulingContext ctx, JobDetail newJob, bool replaceExisting);

        /// <summary>
        /// Remove (delete) the <see cref="IJob" /> with the given
        /// name, and any <see cref="Trigger" /> s that reference
        /// it.
        /// </summary>
        /// <remarks>
        /// If removal of the <see cref="IJob" /> results in an empty group, the
        /// group should be removed from the <see cref="IJobStore" />'s list of
        /// known group names.
        /// </remarks>
        /// <param name="ctx">The context.</param>
        /// <param name="jobName">The name of the <see cref="IJob" /> to be removed.</param>
        /// <param name="groupName">The group name of the <see cref="IJob" /> to be removed.</param>
        /// <returns>
        /// 	<see langword="true" /> if a <see cref="IJob" /> with the given name and
        /// group was found and removed from the store.
        /// </returns>
		bool RemoveJob(SchedulingContext ctx, string jobName, string groupName);

        /// <summary>
        /// Retrieve the <see cref="JobDetail" /> for the given
        /// <see cref="IJob" />.
        /// </summary>
        /// <param name="ctx">The context.</param>
        /// <param name="jobName">The name of the <see cref="IJob" /> to be retrieved.</param>
        /// <param name="groupName">The group name of the <see cref="IJob" /> to be retrieved.</param>
        /// <returns>
        /// The desired <see cref="IJob" />, or null if there is no match.
        /// </returns>
		JobDetail RetrieveJob(SchedulingContext ctx, string jobName, string groupName);

        /// <summary>
        /// Store the given <see cref="Trigger" />.
        /// </summary>
        /// <param name="ctx">The context.</param>
        /// <param name="newTrigger">The <see cref="Trigger" /> to be stored.</param>
        /// <param name="replaceExisting">If <see langword="true" />, any <see cref="Trigger" /> existing in
        /// the <see cref="IJobStore" /> with the same name and group should
        /// be over-written.</param>
        /// <throws>  ObjectAlreadyExistsException </throws>
		void StoreTrigger(SchedulingContext ctx, Trigger newTrigger, bool replaceExisting);

        /// <summary>
        /// Remove (delete) the <see cref="Trigger" /> with the
        /// given name.
        /// </summary>
        /// <remarks>
        /// <p>
        /// If removal of the <see cref="Trigger" /> results in an empty group, the
        /// group should be removed from the <see cref="IJobStore" />'s list of
        /// known group names.
        /// </p>
        /// <p>
        /// If removal of the <see cref="Trigger" /> results in an 'orphaned' <see cref="IJob" />
        /// that is not 'durable', then the <see cref="IJob" /> should be deleted
        /// also.
        /// </p>
        /// </remarks>
        /// <param name="ctx">The context.</param>
        /// <param name="triggerName">The name of the <see cref="Trigger" /> to be removed.</param>
        /// <param name="groupName">The group name of the <see cref="Trigger" /> to be removed.</param>
        /// <returns>
        /// 	<see langword="true" /> if a <see cref="Trigger" /> with the given
        /// name and group was found and removed from the store.
        /// </returns>
		bool RemoveTrigger(SchedulingContext ctx, string triggerName, string groupName);

        /// <summary>
        /// Remove (delete) the <see cref="Trigger" /> with the
        /// given name, and store the new given one - which must be associated
        /// with the same job.
        /// </summary>
        /// <param name="ctx">The context.</param>
        /// <param name="triggerName">The name of the <see cref="Trigger" /> to be removed.</param>
        /// <param name="groupName">The group name of the <see cref="Trigger" /> to be removed.</param>
        /// <param name="newTrigger">The new <see cref="Trigger" /> to be stored.</param>
        /// <returns>
        /// 	<see langword="true" /> if a <see cref="Trigger" /> with the given
        /// name and group was found and removed from the store.
        /// </returns>
		bool ReplaceTrigger(SchedulingContext ctx, string triggerName, string groupName, Trigger newTrigger);


        /// <summary>
        /// Retrieve the given <see cref="Trigger" />.
        /// </summary>
        /// <param name="ctx">The context.</param>
        /// <param name="triggerName">The name of the <see cref="Trigger" /> to be retrieved.</param>
        /// <param name="groupName">The group name of the <see cref="Trigger" /> to be retrieved.</param>
        /// <returns>
        /// The desired <see cref="Trigger" />, or null if there is no
        /// match.
        /// </returns>
		Trigger RetrieveTrigger(SchedulingContext ctx, string triggerName, string groupName);

        /// <summary>
        /// Store the given <see cref="ICalendar" />.
        /// </summary>
        /// <param name="ctx">The context.</param>
        /// <param name="name">The name.</param>
        /// <param name="calendar">The <see cref="ICalendar" /> to be stored.</param>
        /// <param name="replaceExisting">If <see langword="true" />, any <see cref="ICalendar" /> existing
        /// in the <see cref="IJobStore" /> with the same name and group
        /// should be over-written.</param>
        /// <param name="updateTriggers">If <see langword="true" />, any <see cref="Trigger" />s existing
        /// in the <see cref="IJobStore" /> that reference an existing
        /// Calendar with the same name with have their next fire time
        /// re-computed with the new <see cref="ICalendar" />.</param>
        /// <throws>  ObjectAlreadyExistsException </throws>
		void StoreCalendar(SchedulingContext ctx, string name, ICalendar calendar, bool replaceExisting, bool updateTriggers);

        /// <summary>
        /// Remove (delete) the <see cref="ICalendar" /> with the
        /// given name.
        /// </summary>
        /// <remarks>
        /// If removal of the <see cref="ICalendar" /> would result in
        /// <see cref="Trigger" />s pointing to non-existent calendars, then a
        /// <see cref="JobPersistenceException" /> will be thrown.
        /// </remarks>
        /// <param name="ctx">The context.</param>
        /// <param name="calName">The name of the <see cref="ICalendar" /> to be removed.</param>
        /// <returns>
        /// 	<see langword="true" /> if a <see cref="ICalendar" /> with the given name
        /// was found and removed from the store.
        /// </returns>
		bool RemoveCalendar(SchedulingContext ctx, string calName);

        /// <summary>
        /// Retrieve the given <see cref="Trigger" />.
        /// </summary>
        /// <param name="ctx">The context.</param>
        /// <param name="calName">The name of the <see cref="ICalendar" /> to be retrieved.</param>
        /// <returns>
        /// The desired <see cref="ICalendar" />, or null if there is no
        /// match.
        /// </returns>
		ICalendar RetrieveCalendar(SchedulingContext ctx, string calName);


        /// <summary>
        /// Get the number of <see cref="IJob" />s that are
        /// stored in the <see cref="IJobStore" />.
        /// </summary>
        /// <param name="ctx">The context.</param>
        /// <returns></returns>
		int GetNumberOfJobs(SchedulingContext ctx);

        /// <summary>
        /// Get the number of <see cref="Trigger" />s that are
        /// stored in the <see cref="IJobStore" />.
        /// </summary>
        /// <param name="ctx">The context.</param>
        /// <returns></returns>
		int GetNumberOfTriggers(SchedulingContext ctx);

        /// <summary>
        /// Get the number of <see cref="ICalendar" /> s that are
        /// stored in the <see cref="IJobStore" />.
        /// </summary>
        /// <param name="ctx">The context.</param>
        /// <returns></returns>
		int GetNumberOfCalendars(SchedulingContext ctx);

        /// <summary>
        /// Get the names of all of the <see cref="IJob" /> s that
        /// have the given group name.
        /// <p>
        /// If there are no jobs in the given group name, the result should be a
        /// zero-length array (not <see langword="null" />).
        /// </p>
        /// </summary>
        /// <param name="ctx">The CTX.</param>
        /// <param name="groupName">Name of the group.</param>
        /// <returns></returns>
		string[] GetJobNames(SchedulingContext ctx, string groupName);

		/// <summary>
		/// Get the names of all of the <see cref="Trigger" />s
		/// that have the given group name.
		/// <p>
		/// If there are no triggers in the given group name, the result should be a
		/// zero-length array (not <see langword="null" />).
		/// </p>
		/// </summary>
		string[] GetTriggerNames(SchedulingContext ctx, string groupName);

		/// <summary>
		/// Get the names of all of the <see cref="IJob" />
		/// groups.
		/// <p>
		/// If there are no known group names, the result should be a zero-length
		/// array (not <see langword="null" />).
		/// </p>
		/// </summary>
		string[] GetJobGroupNames(SchedulingContext ctxt);

		/// <summary>
		/// Get the names of all of the <see cref="Trigger" />
		/// groups.
		/// <p>
		/// If there are no known group names, the result should be a zero-length
		/// array (not <see langword="null" />).
		/// </p>
		/// </summary>
		string[] GetTriggerGroupNames(SchedulingContext ctxt);

		/// <summary>
		/// Get the names of all of the <see cref="ICalendar" /> s
		/// in the <see cref="IJobStore" />.
    	/// 
		/// <p>
		/// If there are no Calendars in the given group name, the result should be
		/// a zero-length array (not <see langword="null" />).
		/// </p>
		/// </summary>
		string[] GetCalendarNames(SchedulingContext ctxt);

		/// <summary>
		/// Get all of the Triggers that are associated to the given Job.
		/// </summary>
		/// <remarks>
		/// If there are no matches, a zero-length array should be returned.
		/// </remarks>
		Trigger[] GetTriggersForJob(SchedulingContext ctx, string jobName, string groupName);

		/// <summary>
		/// Get the current state of the identified <see cref="Trigger" />.
		/// </summary>
		/// <seealso cref="TriggerState.Normal" />
        /// <seealso cref="TriggerState.Paused" />
        /// <seealso cref="TriggerState.Complete" />
        /// <seealso cref="TriggerState.Error" />
        /// <seealso cref="TriggerState.None" />
		TriggerState GetTriggerState(SchedulingContext ctx, string triggerName, string triggerGroup);

		/////////////////////////////////////////////////////////////////////////////
		//
		// Trigger State manipulation methods
		//
		/////////////////////////////////////////////////////////////////////////////

        /// <summary>
		/// Pause the <see cref="Trigger" /> with the given name.
		/// </summary>
		void PauseTrigger(SchedulingContext ctx, string triggerName, string groupName);

		/// <summary>
		/// Pause all of the <see cref="Trigger" />s in the
		/// given group.
		/// </summary>
		/// <remarks>
		/// The JobStore should "remember" that the group is paused, and impose the
		/// pause on any new triggers that are added to the group while the group is
		/// paused.
		/// </remarks>
		void PauseTriggerGroup(SchedulingContext ctx, string groupName);

		/// <summary>
		/// Pause the <see cref="IJob" /> with the given name - by
		/// pausing all of its current <see cref="Trigger" />s.
		/// </summary>
		void PauseJob(SchedulingContext ctx, string jobName, string groupName);

		/// <summary>
		/// Pause all of the <see cref="IJob" />s in the given
		/// group - by pausing all of their <see cref="Trigger" />s.
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
		/// Resume (un-pause) the <see cref="Trigger" /> with the
		/// given name.
		/// 
		/// <p>
		/// If the <see cref="Trigger" /> missed one or more fire-times, then the
		/// <see cref="Trigger" />'s misfire instruction will be applied.
		/// </p>
		/// </summary>
		/// <seealso cref="string">
		/// </seealso>
		void ResumeTrigger(SchedulingContext ctx, string triggerName, string groupName);

		/// <summary>
		/// Resume (un-pause) all of the <see cref="Trigger" />s
		/// in the given group.
		/// <p>
		/// If any <see cref="Trigger" /> missed one or more fire-times, then the
		/// <see cref="Trigger" />'s misfire instruction will be applied.
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
		/// Resume (un-pause) the <see cref="IJob" /> with the
		/// given name.
		/// <p>
		/// If any of the <see cref="IJob" />'s<see cref="Trigger" /> s missed one
		/// or more fire-times, then the <see cref="Trigger" />'s misfire
		/// instruction will be applied.
		/// </p>
		/// </summary>
		void ResumeJob(SchedulingContext ctx, string jobName, string groupName);

		/// <summary>
		/// Resume (un-pause) all of the <see cref="IJob" />s in
		/// the given group.
		/// <p>
		/// If any of the <see cref="IJob" /> s had <see cref="Trigger" /> s that
		/// missed one or more fire-times, then the <see cref="Trigger" />'s
		/// misfire instruction will be applied.
		/// </p> 
		/// </summary>
		void ResumeJobGroup(SchedulingContext ctx, string groupName);

		/// <summary>
		/// Pause all triggers - equivalent of calling <see cref="PauseTriggerGroup" />
		/// on every group.
		/// <p>
		/// When <see cref="ResumeAll" /> is called (to un-pause), trigger misfire
		/// instructions WILL be applied.
		/// </p>
		/// </summary>
		/// <seealso cref="ResumeAll" />
		void PauseAll(SchedulingContext ctxt);

		/// <summary>
		/// Resume (un-pause) all triggers - equivalent of calling <see cref="ResumeTriggerGroup" />
		/// on every group.
		/// <p>
		/// If any <see cref="Trigger" /> missed one or more fire-times, then the
		/// <see cref="Trigger" />'s misfire instruction will be applied.
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
		/// Inform the <see cref="IJobStore" /> that the scheduler no longer plans to
		/// fire the given <see cref="Trigger" />, that it had previously acquired
		/// (reserved).
		/// </summary>
		void ReleaseAcquiredTrigger(SchedulingContext ctx, Trigger trigger);

		/// <summary>
		/// Inform the <see cref="IJobStore" /> that the scheduler is now firing the
		/// given <see cref="Trigger" /> (executing its associated <see cref="IJob" />),
		/// that it had previously acquired (reserved).
		/// </summary>
		/// <returns> null if the trigger or it's job or calendar no longer exist, or
		/// if the trigger was not successfully put into the 'executing'
		/// state.
		/// </returns>
		TriggerFiredBundle TriggerFired(SchedulingContext ctx, Trigger trigger);

		/// <summary>
		/// Inform the <see cref="IJobStore" /> that the scheduler has completed the
		/// firing of the given <see cref="Trigger" /> (and the execution its
		/// associated <see cref="IJob" />), and that the <see cref="JobDataMap" />
		/// in the given <see cref="JobDetail" /> should be updated if the <see cref="IJob" />
		/// is stateful.
		/// </summary>
        void TriggeredJobComplete(SchedulingContext ctx, Trigger trigger, JobDetail jobDetail, SchedulerInstruction triggerInstCode);
	}
}
