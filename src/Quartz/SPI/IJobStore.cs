#region License
/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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

using Quartz.Core;
using Quartz.Impl.Matchers;

namespace Quartz.Spi
{
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
		/// Called by the QuartzScheduler before the <see cref="IJobStore" /> is
		/// used, in order to give the it a chance to Initialize.
		/// </summary>
		void Initialize(ITypeLoadHelper loadHelper, ISchedulerSignaler signaler);

		/// <summary>
		/// Called by the QuartzScheduler to inform the <see cref="IJobStore" /> that
		/// the scheduler has started.
		/// </summary>
		void SchedulerStarted();

        /// <summary>
        /// Called by the QuartzScheduler to inform the JobStore that
        /// the scheduler has been paused.
        /// </summary>
        void SchedulerPaused();

        /// <summary>
        /// Called by the QuartzScheduler to inform the JobStore that
        /// the scheduler has resumed after being paused.
        /// </summary>
        void SchedulerResumed();

		/// <summary>
		/// Called by the QuartzScheduler to inform the <see cref="IJobStore" /> that
		/// it should free up all of it's resources because the scheduler is
		/// shutting down.
		/// </summary>
		void Shutdown();

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
        /// Whether or not the <see cref="IJobStore" /> implementation is clustered.
        /// </summary>
        /// <returns></returns>
        bool Clustered { get; }

        /// <summary>
        /// Store the given <see cref="IJobDetail" /> and <see cref="ITrigger" />.
        /// </summary>
        /// <param name="newJob">The <see cref="IJobDetail" /> to be stored.</param>
        /// <param name="newTrigger">The <see cref="ITrigger" /> to be stored.</param>
        /// <throws>  ObjectAlreadyExistsException </throws>
        void StoreJobAndTrigger(IJobDetail newJob, IOperableTrigger newTrigger);

        /// <summary>
        /// returns true if the given JobGroup is paused
        /// </summary>
        /// <param name="groupName"></param>
        /// <returns></returns>
        bool IsJobGroupPaused(string groupName);

        /// <summary>
        /// returns true if the given TriggerGroup
        /// is paused
        /// </summary>
        /// <param name="groupName"></param>
        /// <returns></returns>
        bool IsTriggerGroupPaused(string groupName);
        
        /// <summary>
        /// Store the given <see cref="IJobDetail" />.
        /// </summary>
        /// <param name="newJob">The <see cref="IJobDetail" /> to be stored.</param>
        /// <param name="replaceExisting">
        /// If <see langword="true" />, any <see cref="IJob" /> existing in the
        /// <see cref="IJobStore" /> with the same name and group should be
        /// over-written.
        /// </param>
		void StoreJob(IJobDetail newJob, bool replaceExisting);

	    void StoreJobsAndTriggers(IDictionary<IJobDetail, Collection.ISet<ITrigger>> triggersAndJobs, bool replace); 

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
        bool RemoveJob(JobKey jobKey);

	    bool RemoveJobs(IList<JobKey> jobKeys);

        /// <summary>
        /// Retrieve the <see cref="IJobDetail" /> for the given
        /// <see cref="IJob" />.
        /// </summary>
        /// <returns>
        /// The desired <see cref="IJob" />, or null if there is no match.
        /// </returns>
        IJobDetail RetrieveJob(JobKey jobKey);

        /// <summary>
        /// Store the given <see cref="ITrigger" />.
        /// </summary>
        /// <param name="newTrigger">The <see cref="ITrigger" /> to be stored.</param>
        /// <param name="replaceExisting">If <see langword="true" />, any <see cref="ITrigger" /> existing in
        /// the <see cref="IJobStore" /> with the same name and group should
        /// be over-written.</param>
        /// <throws>  ObjectAlreadyExistsException </throws>
        void StoreTrigger(IOperableTrigger newTrigger, bool replaceExisting);

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
        bool RemoveTrigger(TriggerKey triggerKey);

	    bool RemoveTriggers(IList<TriggerKey> triggerKeys);

        /// <summary>
        /// Remove (delete) the <see cref="ITrigger" /> with the
        /// given name, and store the new given one - which must be associated
        /// with the same job.
        /// </summary>
        /// <param name="triggerKey">The <see cref="ITrigger"/> to be replaced.</param>
        /// <param name="newTrigger">The new <see cref="ITrigger" /> to be stored.</param>
        /// <returns>
        /// 	<see langword="true" /> if a <see cref="ITrigger" /> with the given
        /// name and group was found and removed from the store.
        /// </returns>
        bool ReplaceTrigger(TriggerKey triggerKey, IOperableTrigger newTrigger);

        /// <summary>
        /// Retrieve the given <see cref="ITrigger" />.
        /// </summary>
        /// <returns>
        /// The desired <see cref="ITrigger" />, or null if there is no
        /// match.
        /// </returns>
        IOperableTrigger RetrieveTrigger(TriggerKey triggerKey);

        /// <summary>
        /// Determine whether a <see cref="ICalendar" /> with the given identifier already
        /// exists within the scheduler.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="calName">the identifier to check for</param>
        /// <returns>true if a calendar exists with the given identifier</returns>
        bool CalendarExists(string calName);

        /// <summary>
        /// Determine whether a <see cref="IJob" /> with the given identifier already
        /// exists within the scheduler.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="jobKey">the identifier to check for</param>
        /// <returns>true if a job exists with the given identifier</returns>
        bool CheckExists(JobKey jobKey);

        /// <summary>
        /// Determine whether a <see cref="ITrigger" /> with the given identifier already
        /// exists within the scheduler.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="triggerKey">the identifier to check for</param>
        /// <returns>true if a trigger exists with the given identifier</returns>
        bool CheckExists(TriggerKey triggerKey);

        /// <summary>
        /// Clear (delete!) all scheduling data - all <see cref="IJob"/>s, <see cref="ITrigger" />s
        /// <see cref="ICalendar" />s.
        /// </summary>
        /// <remarks>
        /// </remarks>
	    void ClearAllSchedulingData();

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
        /// <throws>  ObjectAlreadyExistsException </throws>
		void StoreCalendar(string name, ICalendar calendar, bool replaceExisting, bool updateTriggers);

        /// <summary>
        /// Remove (delete) the <see cref="ICalendar" /> with the
        /// given name.
        /// </summary>
        /// <remarks>
        /// If removal of the <see cref="ICalendar" /> would result in
        /// <see cref="ITrigger" />s pointing to non-existent calendars, then a
        /// <see cref="JobPersistenceException" /> will be thrown.
        /// </remarks>
        /// <param name="calName">The name of the <see cref="ICalendar" /> to be removed.</param>
        /// <returns>
        /// 	<see langword="true" /> if a <see cref="ICalendar" /> with the given name
        /// was found and removed from the store.
        /// </returns>
		bool RemoveCalendar(string calName);

        /// <summary>
        /// Retrieve the given <see cref="ITrigger" />.
        /// </summary>
        /// <param name="calName">The name of the <see cref="ICalendar" /> to be retrieved.</param>
        /// <returns>
        /// The desired <see cref="ICalendar" />, or null if there is no
        /// match.
        /// </returns>
		ICalendar RetrieveCalendar(string calName);


        /// <summary>
        /// Get the number of <see cref="IJob" />s that are
        /// stored in the <see cref="IJobStore" />.
        /// </summary>
        /// <returns></returns>
		int GetNumberOfJobs();

        /// <summary>
        /// Get the number of <see cref="ITrigger" />s that are
        /// stored in the <see cref="IJobStore" />.
        /// </summary>
        /// <returns></returns>
		int GetNumberOfTriggers();

        /// <summary>
        /// Get the number of <see cref="ICalendar" /> s that are
        /// stored in the <see cref="IJobStore" />.
        /// </summary>
        /// <returns></returns>
		int GetNumberOfCalendars();

	    /// <summary>
	    /// Get the names of all of the <see cref="IJob" /> s that
	    /// have the given group name.
	    /// <para>
	    /// If there are no jobs in the given group name, the result should be a
	    /// zero-length array (not <see langword="null" />).
	    /// </para>
	    /// </summary>
	    /// <param name="matcher"></param>
	    /// <returns></returns>
	    Collection.ISet<JobKey> GetJobKeys(GroupMatcher<JobKey> matcher);

		/// <summary>
		/// Get the names of all of the <see cref="ITrigger" />s
		/// that have the given group name.
		/// <para>
		/// If there are no triggers in the given group name, the result should be a
		/// zero-length array (not <see langword="null" />).
		/// </para>
		/// </summary>
        Collection.ISet<TriggerKey> GetTriggerKeys(GroupMatcher<TriggerKey> matcher);

		/// <summary>
		/// Get the names of all of the <see cref="IJob" />
		/// groups.
		/// <para>
		/// If there are no known group names, the result should be a zero-length
		/// array (not <see langword="null" />).
		/// </para>
		/// </summary>
		IList<string> GetJobGroupNames();

		/// <summary>
		/// Get the names of all of the <see cref="ITrigger" />
		/// groups.
		/// <para>
		/// If there are no known group names, the result should be a zero-length
		/// array (not <see langword="null" />).
		/// </para>
		/// </summary>
		IList<string> GetTriggerGroupNames();

		/// <summary>
		/// Get the names of all of the <see cref="ICalendar" /> s
		/// in the <see cref="IJobStore" />.
    	/// 
		/// <para>
		/// If there are no Calendars in the given group name, the result should be
		/// a zero-length array (not <see langword="null" />).
		/// </para>
		/// </summary>
		IList<string> GetCalendarNames();

		/// <summary>
		/// Get all of the Triggers that are associated to the given Job.
		/// </summary>
		/// <remarks>
		/// If there are no matches, a zero-length array should be returned.
		/// </remarks>
        IList<IOperableTrigger> GetTriggersForJob(JobKey jobKey);

		/// <summary>
		/// Get the current state of the identified <see cref="ITrigger" />.
		/// </summary>
		/// <seealso cref="TriggerState" />
        TriggerState GetTriggerState(TriggerKey triggerKey);

		/////////////////////////////////////////////////////////////////////////////
		//
		// Trigger State manipulation methods
		//
		/////////////////////////////////////////////////////////////////////////////

        /// <summary>
		/// Pause the <see cref="ITrigger" /> with the given key.
		/// </summary>
        void PauseTrigger(TriggerKey triggerKey);

		/// <summary>
		/// Pause all of the <see cref="ITrigger" />s in the
		/// given group.
		/// </summary>
		/// <remarks>
		/// The JobStore should "remember" that the group is paused, and impose the
		/// pause on any new triggers that are added to the group while the group is
		/// paused.
		/// </remarks>
        Collection.ISet<string> PauseTriggers(GroupMatcher<TriggerKey> matcher);

		/// <summary>
		/// Pause the <see cref="IJob" /> with the given key - by
		/// pausing all of its current <see cref="ITrigger" />s.
		/// </summary>
        void PauseJob(JobKey jobKey);

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
		IList<string> PauseJobs(GroupMatcher<JobKey> matcher);

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
        void ResumeTrigger(TriggerKey triggerKey);

		/// <summary>
		/// Resume (un-pause) all of the <see cref="ITrigger" />s
		/// in the given group.
		/// <para>
		/// If any <see cref="ITrigger" /> missed one or more fire-times, then the
		/// <see cref="ITrigger" />'s misfire instruction will be applied.
		/// </para>
		/// </summary>
		IList<string> ResumeTriggers(GroupMatcher<TriggerKey> matcher);

        /// <summary>
        /// Gets the paused trigger groups.
        /// </summary>
        /// <returns></returns>
        Collection.ISet<string> GetPausedTriggerGroups();

		/// <summary> 
		/// Resume (un-pause) the <see cref="IJob" /> with the
		/// given key.
		/// <para>
		/// If any of the <see cref="IJob" />'s<see cref="ITrigger" /> s missed one
		/// or more fire-times, then the <see cref="ITrigger" />'s misfire
		/// instruction will be applied.
		/// </para>
		/// </summary>
		void ResumeJob(JobKey jobKey);

		/// <summary>
		/// Resume (un-pause) all of the <see cref="IJob" />s in
		/// the given group.
		/// <para>
		/// If any of the <see cref="IJob" /> s had <see cref="ITrigger" /> s that
		/// missed one or more fire-times, then the <see cref="ITrigger" />'s
		/// misfire instruction will be applied.
		/// </para> 
		/// </summary>
		Collection.ISet<string> ResumeJobs(GroupMatcher<JobKey> matcher);

		/// <summary>
		/// Pause all triggers - equivalent of calling <see cref="PauseTriggers" />
		/// on every group.
		/// <para>
		/// When <see cref="ResumeAll" /> is called (to un-pause), trigger misfire
		/// instructions WILL be applied.
		/// </para>
		/// </summary>
		/// <seealso cref="ResumeAll" />
		void PauseAll();

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
		void ResumeAll();

        /// <summary>
        /// Get a handle to the next trigger to be fired, and mark it as 'reserved'
        /// by the calling scheduler.
        /// </summary>
        /// <param name="noLaterThan">If &gt; 0, the JobStore should only return a Trigger
        /// that will fire no later than the time represented in this value as
        /// milliseconds.</param>
        /// <param name="maxCount"></param>
        /// <param name="timeWindow"></param>
        /// <returns></returns>
        /// <seealso cref="ITrigger">
        /// </seealso>
        IList<IOperableTrigger> AcquireNextTriggers(DateTimeOffset noLaterThan, int maxCount, TimeSpan timeWindow);

		/// <summary> 
		/// Inform the <see cref="IJobStore" /> that the scheduler no longer plans to
		/// fire the given <see cref="ITrigger" />, that it had previously acquired
		/// (reserved).
		/// </summary>
        void ReleaseAcquiredTrigger(IOperableTrigger trigger);

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
        IList<TriggerFiredResult> TriggersFired(IList<IOperableTrigger> triggers);

		/// <summary>
		/// Inform the <see cref="IJobStore" /> that the scheduler has completed the
		/// firing of the given <see cref="ITrigger" /> (and the execution its
		/// associated <see cref="IJob" />), and that the <see cref="JobDataMap" />
		/// in the given <see cref="IJobDetail" /> should be updated if the <see cref="IJob" />
		/// is stateful.
		/// </summary>
        void TriggeredJobComplete(IOperableTrigger trigger, IJobDetail jobDetail, SchedulerInstruction triggerInstCode);

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
	}
}
