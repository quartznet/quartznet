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

#if !NET_20
using Nullables;
#endif

using Quartz.Collection;
using Quartz.Spi;

namespace Quartz
{	
	/// <summary>
	/// Scheduler constants.
	/// </summary>
	public struct SchedulerConstants
	{
		/// <summary>
		/// A (possibly) usefull constant that can be used for specifying the group
		/// that <see cref="IJob" /> and <see cref="Trigger" /> instances belong to.
		/// </summary>
		public static readonly string DEFAULT_GROUP = "DEFAULT";

		/// <summary>
		/// A constant <see cref="Trigger" /> group name used internally by the
		/// scheduler - clients should not use the value of this constant
		/// ("MANUAL_TRIGGER") for thename of a <see cref="Trigger" />'s group.
		/// </summary>
		public static readonly string DEFAULT_MANUAL_TRIGGERS = "MANUAL_TRIGGER";

		/// <summary>
		/// A constant <see cref="Trigger" /> group name used internally by the
		/// scheduler - clients should not use the value of this constant
		/// ("RECOVERING_JOBS") for thename of a <see cref="Trigger" />'s group.
		/// </summary>
		public static readonly string DEFAULT_RECOVERY_GROUP = "RECOVERING_JOBS";

		/// <summary>
		/// A constant <see cref="Trigger" /> group name used internally by the
		/// scheduler - clients should not use the value of this constant
		/// ("FAILED_OVER_JOBS") for thename of a <see cref="Trigger" />'s group.
		/// </summary>
		public static readonly string DEFAULT_FAIL_OVER_GROUP = "FAILED_OVER_JOBS";


        /// <summary>
        ///  A constant <see cref="JobDataMap" /> key that can be used to retrieve the
        /// name of the original <see cref="Trigger" /> from a recovery trigger's
        /// data map in the case of a job recovering after a failed scheduler
        /// instance.
        /// </summary>
        /// <seealso cref="JobDetail.RequestsRecovery" />
        public static readonly string FAILED_JOB_ORIGINAL_TRIGGER_NAME = "QRTZ_FAILED_JOB_ORIG_TRIGGER_NAME";

        /// <summary>
        /// A constant <see cref="JobDataMap" /> key that can be used to retrieve the
        /// group of the original <see cref="Trigger" /> from a recovery trigger's
        /// data map in the case of a job recovering after a failed scheduler
        /// instance.
        /// </summary>
        /// <seealso cref="JobDetail.RequestsRecovery" />
        public static readonly string FAILED_JOB_ORIGINAL_TRIGGER_GROUP = "QRTZ_FAILED_JOB_ORIG_TRIGGER_GROUP";

        /// <summary>
        ///  A constant <see cref="JobDataMap" /> key that can be used to retrieve the
        /// scheduled fire time of the original <see cref="Trigger" /> from a recovery
         /// trigger's data map in the case of a job recovering after a failed scheduler
         /// instance.
        /// </summary>
        /// <seealso cref="JobDetail.RequestsRecovery" />
        public static readonly string FAILED_JOB_ORIGINAL_TRIGGER_FIRETIME_IN_MILLISECONDS = "QRTZ_FAILED_JOB_ORIG_TRIGGER_FIRETIME_IN_MILLISECONDS_AS_STRING";

	}

    /// <summary>
    /// This is the main interface of a Quartz Scheduler.
    /// </summary>
    /// <remarks>
    /// 	<para>
    ///         A <see cref="IScheduler"/> maintains a registry of
    ///         <see cref="JobDetail"/> s and <see cref="Trigger"/>s. Once
    ///         registered, the <see cref="IScheduler"/> is responsible for executing
    ///         <see cref="IJob"/> s when their associated <see cref="Trigger"/> s
    ///         fire (when their scheduled time arrives).
    ///     </para>
    /// 	<para>
    /// 		<see cref="IScheduler"/> instances are produced by a
    ///         <see cref="ISchedulerFactory"/>. A scheduler that has already been
    ///         created/initialized can be found and used through the same factory that
    ///         produced it. After a <see cref="IScheduler"/> has been created, it is in
    ///         "stand-by" mode, and must have its <see cref="IScheduler.Start"/> method
    ///         called before it will fire any <see cref="IJob"/>s.
    ///     </para>
    /// 	<para>
    /// 		<see cref="IJob"/> s are to be created by the 'client program', by
    ///         defining a class that implements the <see cref="IJob"/> interface.
    ///         <see cref="JobDetail"/> objects are then created (also by the client) to
    ///         define a individual instances of the <see cref="IJob"/>.
    ///         <see cref="JobDetail"/> instances can then be registered with the
    ///         <see cref="IScheduler"/> via the %IScheduler.ScheduleJob(JobDetail,
    ///         Trigger)% or %IScheduler.AddJob(JobDetail, bool)% method.
    ///     </para>
    /// 	<para>
    /// 		<see cref="Trigger"/> s can then be defined to fire individual
    ///         <see cref="IJob"/> instances based on given schedules.
    ///         <see cref="SimpleTrigger"/> s are most useful for one-time firings, or
    ///         firing at an exact moment in time, with N repeats with a given delay between
    ///         them. <see cref="CronTrigger"/> s allow scheduling based on time of day,
    ///         day of week, day of month, and month of year.
    ///     </para>
    /// 	<para>
    /// 		<see cref="IJob"/> s and <see cref="Trigger"/> s have a name and
    ///         group associated with them, which should uniquely identify them within a single
    ///         <see cref="IScheduler"/>. The 'group' feature may be useful for creating
    ///         logical groupings or categorizations of <see cref="IJob"/>s and
    ///         <see cref="Trigger"/>s. If you don't have need for assigning a group to a
    ///         given <see cref="IJob"/>s of <see cref="Trigger"/>s, then you can use
    ///         the <see cref="SchedulerConstants.DEFAULT_GROUP"/> constant defined on
    ///         this interface.
    ///     </para>
    /// 	<para>
    ///         Stored <see cref="IJob"/> s can also be 'manually' triggered through the
    ///         use of the %IScheduler.TriggerJob(string, string)% function.
    ///     </para>
    /// 	<para>
    ///         Client programs may also be interested in the 'listener' interfaces that are
    ///         available from Quartz. The <see cref="IJobListener"/> interface provides
    ///         notifications of <see cref="IJob"/> executions. The
    ///         <see cref="ITriggerListener"/> interface provides notifications of
    ///         <see cref="Trigger"/> firings. The <see cref="ISchedulerListener"/>
    ///         interface provides notifications of <see cref="IScheduler"/> events and
    ///         errors.
    ///     </para>
    /// 	<para>
    ///         The setup/configuration of a <see cref="IScheduler"/> instance is very
    ///         customizable. Please consult the documentation distributed with Quartz.
    ///     </para>
    /// </remarks>
    /// <seealso cref="IJob"/>
    /// <seealso cref="JobDetail"/>
    /// <seealso cref="Trigger"/>
    /// <seealso cref="IJobListener"/>
    /// <seealso cref="ITriggerListener"/>
    /// <seealso cref="ISchedulerListener"/>
	public interface IScheduler
	{
		/// <summary> 
		/// Returns the name of the <see cref="IScheduler" />.
		/// </summary>
		string SchedulerName { get; }

		/// <summary>
		/// Returns the instance Id of the <see cref="IScheduler" />.
		/// </summary>
		string SchedulerInstanceId { get; }

		/// <summary>
		/// Returns the <see cref="SchedulerContext" /> of the <see cref="IScheduler" />.
		/// </summary>
		SchedulerContext Context { get; }

		/// <summary>
		/// Reports whether the <see cref="IScheduler" /> is in stand-by mode.
		/// </summary>
		/// <seealso cref="Standby()" />
		/// <seealso cref="Start()" />
		bool InStandbyMode { get; }

		/// <summary>
		/// Reports whether the <see cref="IScheduler" /> has been Shutdown.
		/// </summary>
		bool IsShutdown { get; }

		/// <summary>
		/// Get a <see cref="SchedulerMetaData" /> object describiing the settings
		/// and capabilities of the scheduler instance.
		/// </summary>
		/// <remarks>
		/// Note that the data returned is an 'instantaneous' snap-shot, and that as
		/// soon as it's returned, the meta data values may be different.
        /// </remarks>
		SchedulerMetaData GetMetaData();

		/// <summary>
        /// Return a list of <see cref="JobExecutionContext" /> objects that
        /// represent all currently executing Jobs in this Scheduler instance.
        /// </summary>
        /// <remarks>
        /// <p>
        /// This method is not cluster aware.  That is, it will only return Jobs
        /// currently executing in this Scheduler instance, not across the entire
        /// cluster.
        /// </p>
		/// <p>
		/// Note that the list returned is an 'instantaneous' snap-shot, and that as
		/// soon as it's returned, the true list of executing jobs may be different.
		/// Also please read the doc associated with <see cref="JobExecutionContext" />-
		/// especially if you're using remoting.
		/// </p>
        /// </remarks>
		/// <seealso cref="JobExecutionContext" />
		IList GetCurrentlyExecutingJobs();

		/// <summary>
		/// Set the <see cref="JobFactory" /> that will be responsible for producing 
		/// instances of <see cref="IJob" /> classes.
		/// </summary>
		/// <remarks>
		/// JobFactories may be of use to those wishing to have their application
		/// produce <see cref="IJob" /> instances via some special mechanism, such as to
		/// give the opertunity for dependency injection.
        /// </remarks>
		/// <seealso cref="IJobFactory" />
		IJobFactory JobFactory { set; }

		/// <summary>
		/// Get the names of all known <see cref="JobDetail" /> groups.
		/// </summary>
		string[] JobGroupNames { get; }

		/// <summary>
		/// Get the names of all known <see cref="Trigger" /> groups.
		/// </summary>
		string[] TriggerGroupNames { get; }

		/// <summary> 
		/// Get the names of all <see cref="Trigger" /> groups that are paused.
		/// </summary>
		ISet GetPausedTriggerGroups();

		/// <summary>
		/// Get the names of all registered <see cref="ICalendar" />s.
		/// </summary>
		string[] CalendarNames { get; }

		/// <summary>
		/// Get a List containing all of the <see cref="IJobListener" /> s in
		/// the <see cref="IScheduler" />'s<i>global</i> list.
		/// </summary>
		IList GlobalJobListeners { get; }

		/// <summary>
		/// Get a Set containing the names of all the <i>non-global</i><see cref="IJobListener" />
		/// s registered with the <see cref="IScheduler" />.
		/// </summary>
		ISet JobListenerNames { get; }

        /// <summary>
        /// Get the <i>global</i><see cref="IJobListener" /> that has
        /// the given name.
        /// </summary>
        /// <param name="name">Global job listener's name</param>
        /// <returns></returns>
        IJobListener GetGlobalJobListener(string name);

        /// <summary>
        /// Get the <i>global</i><see cref="ITriggerListener" /> that
        /// has the given name.
        /// </summary>
        /// <param name="name">Global trigger listener's name</param>
        /// <returns></returns>
        ITriggerListener GetGlobalTriggerListener(string name);

		/// <summary>
		/// Get a List containing all of the <see cref="ITriggerListener" />
		/// s in the <see cref="IScheduler" />'s<i>global</i> list.
		/// </summary>
		IList GlobalTriggerListeners { get; }

		/// <summary>
		/// Get a Set containing the names of all the <i>non-global</i><see cref="ITriggerListener" />
		/// s registered with the <see cref="IScheduler" />.
		/// </summary>
		ISet TriggerListenerNames { get; }

		/// <summary>
		/// Get a List containing all of the <see cref="ISchedulerListener" />
		/// s registered with the <see cref="IScheduler" />.
		/// </summary>
		IList SchedulerListeners { get; }


		/// <summary>
		/// Starts the <see cref="IScheduler" />'s threads that fire <see cref="Trigger" />s.
		/// When a scheduler is first created it is in "stand-by" mode, and will not
		/// fire triggers.  The scheduler can also be put into stand-by mode by
		/// calling the <see cref="Standby" /> method.
		/// </summary>
		/// <remarks>
		/// The misfire/recovery process will be started, if it is the initial call
		/// to this method on this scheduler instance.
		/// </remarks>
		/// <seealso cref="Standby"/>
		/// <seealso cref="Shutdown(bool)"/>
		void Start();

 
        /// <summary>
        /// Whether the scheduler has been started.  
        /// </summary>
        /// <remarks>
        /// Note: This only reflects whether <see cref="Start" /> has ever
        /// been called on this Scheduler, so it will return <code>true</code> even 
        /// if the <code>Scheduler</code> is currently in standby mode or has been 
        /// since shutdown.
        /// </remarks>
        /// <seealso cref="Start" />
        /// <seealso cref="IsShutdown" />
        /// <seealso cref="InStandbyMode" />
        bool IsStarted
        { 
            get;
        }

		/// <summary>
		/// Temporarily halts the <see cref="IScheduler" />'s firing of <see cref="Trigger" />s.
		/// </summary>
		/// <remarks>
		/// <p>
		/// When <see cref="Start" /> is called (to bring the scheduler out of 
		/// stand-by mode), trigger misfire instructions will NOT be applied
		/// during the execution of the <see cref="Start" /> method - any misfires 
		/// will be detected immediately afterward (by the <see cref="IJobStore" />'s 
		/// normal process).
		/// </p>
		/// <p>
		/// The scheduler is not destroyed, and can be re-started at any time.
		/// </p>
		/// </remarks>
		/// <seealso cref="Start()"/>
		/// <seealso cref="PauseAll()"/>
		void Standby();


		/// <summary> 
		/// Halts the <see cref="IScheduler" />'s firing of <see cref="Trigger" />s,
		/// and cleans up all resources associated with the Scheduler. Equivalent to
		/// <see cref="Shutdown(bool)" />.
		/// </summary>
		/// <remarks>
		/// The scheduler cannot be re-started.
		/// </remarks>
		/// <seealso cref="Shutdown(bool)" />
		void Shutdown();

		/// <summary>
		/// Halts the <see cref="IScheduler" />'s firing of <see cref="Trigger" />s,
		/// and cleans up all resources associated with the Scheduler. 
		/// </summary>
		/// <remarks>
		/// The scheduler cannot be re-started.
        /// </remarks>
		/// <param name="waitForJobsToComplete">
		/// if <see langword="true" /> the scheduler will not allow this method
		/// to return until all currently executing jobs have completed.
		/// </param>
		/// <seealso cref="Shutdown()" /> 
		void Shutdown(bool waitForJobsToComplete);


		/// <summary>
		/// Add the given <see cref="JobDetail" /> to the
		/// Scheduler, and associate the given <see cref="Trigger" /> with
		/// it.
		/// </summary>
		/// <remarks>
		/// If the given Trigger does not reference any <see cref="IJob" />, then it
		/// will be set to reference the Job passed with it into this method.
		/// </remarks>
		DateTime ScheduleJob(JobDetail jobDetail, Trigger trigger);

		/// <summary>
		/// Schedule the given <see cref="Trigger" /> with the
		/// <see cref="IJob" /> identified by the <see cref="Trigger" />'s settings.
		/// </summary>
		DateTime ScheduleJob(Trigger trigger);

		/// <summary>
		/// Remove the indicated <see cref="Trigger" /> from the scheduler.
		/// </summary>
		bool UnscheduleJob(string triggerName, string groupName);

		/// <summary>
		/// Remove (delete) the <see cref="Trigger" /> with the
		/// given name, and store the new given one - which must be associated
		/// with the same job (the new trigger must have the job name &amp; group specified) 
		/// - however, the new trigger need not have the same name as the old trigger.
		/// </summary>
		/// <param name="triggerName">
		/// The name of the <see cref="Trigger" /> to be replaced.
		/// </param>
		/// <param name="groupName">
		/// The group name of the <see cref="Trigger" /> to be replaced.
		/// </param>
		/// <param name="newTrigger">
		/// The new <see cref="Trigger" /> to be stored.
		/// </param>
		/// <returns> 
		/// <see langword="null" /> if a <see cref="Trigger" /> with the given
		/// name and group was not found and removed from the store, otherwise
		/// the first fire time of the newly scheduled trigger.
		/// </returns>
#if !NET_20
        NullableDateTime RescheduleJob(string triggerName, string groupName, Trigger newTrigger);
#else
        DateTime? RescheduleJob(string triggerName, string groupName, Trigger newTrigger);
#endif


        /// <summary>
		/// Add the given <see cref="IJob" /> to the Scheduler - with no associated
		/// <see cref="Trigger" />. The <see cref="IJob" /> will be 'dormant' until
		/// it is scheduled with a <see cref="Trigger" />, or <see cref="IScheduler.TriggerJob(string, string)" />
		/// is called for it.
		/// </summary>
		/// <remarks>
		/// The <see cref="IJob" /> must by definition be 'durable', if it is not,
		/// SchedulerException will be thrown.
		/// </remarks>
		void AddJob(JobDetail jobDetail, bool replace);

		/// <summary>
		/// Delete the identified <see cref="IJob" /> from the Scheduler - and any
		/// associated <see cref="Trigger" />s.
		/// </summary>
		/// <returns> true if the Job was found and deleted.</returns>
		bool DeleteJob(string jobName, string groupName);

		/// <summary>
		/// Trigger the identified <see cref="JobDetail" />
		/// (Execute it now) - the generated trigger will be non-volatile.
		/// </summary>
		void TriggerJob(string jobName, string groupName);

		/// <summary>
		/// Trigger the identified <see cref="JobDetail" />
		/// (Execute it now) - the generated trigger will be volatile.
		/// </summary>
		void TriggerJobWithVolatileTrigger(string jobName, string groupName);

		/// <summary>
		/// Trigger the identified <see cref="JobDetail" />
		/// (Execute it now) - the generated trigger will be non-volatile.
		/// </summary>
		/// <param name="jobName">the name of the Job to trigger</param>
		/// <param name="groupName">the group name of the Job to trigger</param>
		/// <param name="data">
		/// the (possibly <see langword="null" />) JobDataMap to be
		/// associated with the trigger that fires the job immediately.
		/// </param>
		void TriggerJob(string jobName, string groupName, JobDataMap data);

		/// <summary>
		/// Trigger the identified <see cref="JobDetail" />
		/// (Execute it now) - the generated trigger will be volatile.
		/// </summary>
		/// <param name="jobName">the name of the Job to trigger</param>
		/// <param name="groupName">the group name of the Job to trigger</param>
		/// <param name="data">
		/// the (possibly <see langword="null" />) JobDataMap to be
		/// associated with the trigger that fires the job immediately.
		/// </param>
		void TriggerJobWithVolatileTrigger(string jobName, string groupName, JobDataMap data);

		/// <summary>
		/// Pause the <see cref="JobDetail" /> with the given
		/// name - by pausing all of its current <see cref="Trigger" />s.
		/// </summary>
		void PauseJob(string jobName, string groupName);

		/// <summary>
		/// Pause all of the <see cref="JobDetail" />s in the
		/// given group - by pausing all of their <see cref="Trigger" />s.
		/// </summary>
		/// <remarks>
		/// The Scheduler will "remember" that the group is paused, and impose the
		/// pause on any new jobs that are added to the group while the group is
		/// paused.
		/// </remarks>
		/// <seealso cref="ResumeJobGroup(string)" />
		void PauseJobGroup(string groupName);

		/// <summary> 
		/// Pause the <see cref="Trigger" /> with the given name.
		/// </summary>
		void PauseTrigger(string triggerName, string groupName);

		/// <summary>
		/// Pause all of the <see cref="Trigger" />s in the given group.
		/// </summary>
		/// <remarks>
		/// The Scheduler will "remember" that the group is paused, and impose the
		/// pause on any new triggers that are added to the group while the group is
		/// paused.
        /// </remarks>
		/// <seealso cref="ResumeTriggerGroup(string)" />
		void PauseTriggerGroup(string groupName);

		/// <summary>
		/// Resume (un-pause) the <see cref="JobDetail" /> with
		/// the given name.
		/// </summary>
		/// <remarks>
		/// If any of the <see cref="IJob" />'s<see cref="Trigger" /> s missed one
		/// or more fire-times, then the <see cref="Trigger" />'s misfire
		/// instruction will be applied.
		/// </remarks>
		void ResumeJob(string jobName, string groupName);

		/// <summary>
		/// Resume (un-pause) all of the <see cref="JobDetail" />s
		/// in the given group.
		/// </summary>
		/// <remarks>
		/// If any of the <see cref="IJob" /> s had <see cref="Trigger" /> s that
		/// missed one or more fire-times, then the <see cref="Trigger" />'s
		/// misfire instruction will be applied.
		/// </remarks>
		/// <seealso cref="PauseJobGroup(string)" />
		void ResumeJobGroup(string groupName);

		/// <summary>
		/// Resume (un-pause) the <see cref="Trigger" /> with the given
		/// name.
		/// </summary>
		/// <remarks>
		/// If the <see cref="Trigger" /> missed one or more fire-times, then the
		/// <see cref="Trigger" />'s misfire instruction will be applied.
		/// </remarks>
		void ResumeTrigger(string triggerName, string groupName);

		/// <summary>
		/// Resume (un-pause) all of the <see cref="Trigger" />s in the
		/// given group.
		/// </summary>
		/// <remarks>
		/// If any <see cref="Trigger" /> missed one or more fire-times, then the
		/// <see cref="Trigger" />'s misfire instruction will be applied.
		/// </remarks>
		/// <seealso cref="PauseTriggerGroup(string)" />
		void ResumeTriggerGroup(string groupName);

		/// <summary>
		/// Pause all triggers - similar to calling <see cref="PauseTriggerGroup(string)" />
		/// on every group, however, after using this method <see cref="ResumeAll()" /> 
		/// must be called to clear the scheduler's state of 'remembering' that all 
		/// new triggers will be paused as they are added. 
		/// </summary>
		/// <remarks>
		/// When <see cref="ResumeAll()" /> is called (to un-pause), trigger misfire
		/// instructions WILL be applied.
		/// </remarks>
		/// <seealso cref="ResumeAll()" />
		/// <seealso cref="PauseTriggerGroup(string)" />
		/// <seealso cref="Standby()" />
		void PauseAll();

		/// <summary> 
		/// Resume (un-pause) all triggers - similar to calling 
		/// <see cref="ResumeTriggerGroup(string)" /> on every group.
		/// </summary>
		/// <remarks>
		/// If any <see cref="Trigger" /> missed one or more fire-times, then the
		/// <see cref="Trigger" />'s misfire instruction will be applied.
		/// </remarks>
		/// <seealso cref="PauseAll()" />
		void ResumeAll();

		/// <summary>
		/// Get the names of all the <see cref="JobDetail" />s in the given group.
		/// </summary>
		string[] GetJobNames(string groupName);
		
		/// <summary>
		/// Get all <see cref="Trigger" /> s that are associated with the
		/// identified <see cref="JobDetail" />.
		/// </summary>
		Trigger[] GetTriggersOfJob(string jobName, string groupName);

		/// <summary>
		/// Get the names of all the <see cref="Trigger" />s in the given
		/// group.
		/// </summary>
		string[] GetTriggerNames(string groupName);

		/// <summary>
		/// Get the <see cref="JobDetail" /> for the <see cref="IJob" />
		/// instance with the given name and group.
		/// </summary>
		JobDetail GetJobDetail(string jobName, string jobGroup);

		/// <summary>
		/// Get the <see cref="Trigger" /> instance with the given name and
		/// group.
		/// </summary>
		Trigger GetTrigger(string triggerName, string triggerGroup);

		/// <summary>
		/// Get the current state of the identified <see cref="Trigger" />.
		/// </summary>
        /// <seealso cref="TriggerState.Normal" />
        /// <seealso cref="TriggerState.Paused" />
        /// <seealso cref="TriggerState.Complete" />
        /// <seealso cref="TriggerState.Blocked" />
        /// <seealso cref="TriggerState.Error" />
        /// <seealso cref="TriggerState.None" />
		TriggerState GetTriggerState(string triggerName, string triggerGroup);

		/// <summary>
		/// Add (register) the given <see cref="ICalendar" /> to the Scheduler.
		/// </summary>
		/// <param name="calName">Name of the calendar.</param>
		/// <param name="calendar">The calendar.</param>
		/// <param name="replace">if set to <c>true</c> [replace].</param>
		/// <param name="updateTriggers">whether or not to update existing triggers that
		/// referenced the already existing calendar so that they are 'correct'
		/// based on the new trigger.</param>
		void AddCalendar(string calName, ICalendar calendar, bool replace, bool updateTriggers);

		/// <summary>
		/// Delete the identified <see cref="ICalendar" /> from the Scheduler.
		/// </summary>
		/// <param name="calName">Name of the calendar.</param>
		/// <returns>
		/// true if the Calendar was found and deleted.
		/// </returns>
		bool DeleteCalendar(string calName);

		/// <summary>
		/// Get the <see cref="ICalendar" /> instance with the given name.
		/// </summary>
		ICalendar GetCalendar(string calName);

        /// <summary>
        /// Get the names of all registered <see cref="ICalendar" />.
        /// </summary>
        /// <returns>An array of calendar names.</returns>
        string[] GetCalendarNames();

		/// <summary>
		/// Request the interruption, within this Scheduler instance, of all 
        /// currently executing instances of the identified <code>Job</code>, which 
        /// must be an implementor of the <see cref="IInterruptableJob" /> interface.
		/// </summary>
		/// <remarks>
		/// <p>
		/// If more than one instance of the identified job is currently executing,
		/// the <see cref="IInterruptableJob.Interrupt" /> method will be called on
		/// each instance.  However, there is a limitation that in the case that  
		/// <see cref="Interrupt" /> on one instances throws an exception, all 
		/// remaining  instances (that have not yet been interrupted) will not have 
		/// their <see cref="Interrupt" /> method called.
		/// </p>
		/// 
		/// <p>
		/// If you wish to interrupt a specific instance of a job (when more than
		/// one is executing) you can do so by calling 
		/// <see cref="GetCurrentlyExecutingJobs" /> to obtain a handle 
		/// to the job instance, and then invoke <see cref="Interrupt" /> on it
		/// yourself.
		/// </p>
		/// <p>
        /// This method is not cluster aware.  That is, it will only interrupt 
        /// instances of the identified InterruptableJob currently executing in this 
        /// Scheduler instance, not across the entire cluster.
        /// </p>
        /// </remarks>
		/// <param name="jobName"> </param>
		/// <param name="groupName"> </param>
		/// <returns> 
		/// true is at least one instance of the identified job was found and interrupted.
		/// </returns>
		/// <seealso cref="IInterruptableJob" />
		/// <seealso cref="GetCurrentlyExecutingJobs" />
		bool Interrupt(string jobName, string groupName);

		/// <summary>
		/// Add the given <see cref="IJobListener" /> to the <see cref="IScheduler" />'s
		/// <i>global</i> list.
		/// </summary>
		/// <remarks>
		/// Listeners in the 'global' list receive notification of execution events
		/// for ALL <see cref="JobDetail" />s.
		/// </remarks>
		void AddGlobalJobListener(IJobListener jobListener);

		/// <summary> 
		/// Add the given <see cref="IJobListener" /> to the <see cref="IScheduler" />'s
		/// list, of registered <see cref="IJobListener" />s.
		/// </summary>
		void AddJobListener(IJobListener jobListener);

		/// <summary>
		/// Remove the given <see cref="IJobListener" /> from the <see cref="IScheduler" />'s
		/// list of <i>global</i> listeners.
		/// </summary>
		/// <returns>
		/// true if the identifed listener was found in the list, and removed.
		/// </returns>
		bool RemoveGlobalJobListener(IJobListener jobListener);

        /// <summary>
        /// Remove the identifed <see cref="IJobListener" /> from the <see cref="IScheduler" />'s
        /// list of <i>global</i> listeners.
        /// </summary>
        /// <param name="name">Global job listener's name</param>
        /// <returns>true if the identifed listener was found in the list, and removed</returns>
        bool RemoveGlobalJobListener(string name);

		/// <summary>
		/// Remove the identifed <see cref="IJobListener" /> from the <see cref="IScheduler" />'s
		/// list of registered listeners.
		/// </summary>
		/// <returns> 
		/// true if the identifed listener was found in the list, and removed.
		/// </returns>
		bool RemoveJobListener(string name);

		/// <summary>
		/// Get the <i>non-global</i><see cref="IJobListener" /> that has
		/// the given name.
		/// </summary>
		IJobListener GetJobListener(string name);

		/// <summary>
		/// Add the given <see cref="ITriggerListener" /> to the <see cref="IScheduler" />'s
		/// <i>global</i> list.
		/// </summary>
		/// <remarks>
		/// Listeners in the 'global' list receive notification of execution events
		/// for ALL <see cref="Trigger" />s.
		/// </remarks>
		void AddGlobalTriggerListener(ITriggerListener triggerListener);

		/// <summary>
		/// Add the given <see cref="ITriggerListener" /> to the <see cref="IScheduler" />'s
		/// list, of registered <see cref="ITriggerListener" />s.
		/// </summary>
		void AddTriggerListener(ITriggerListener triggerListener);

		/// <summary>
		/// Remove the given <see cref="ITriggerListener" /> from the <see cref="IScheduler" />'s
		/// list of <i>global</i> listeners.
		/// </summary>
		/// <returns> 
		/// true if the identifed listener was found in the list, and removed.
		/// </returns>
		bool RemoveGlobalTriggerListener(ITriggerListener triggerListener);


        /// <summary>
        /// Remove the identifed <see cref="ITriggerListener" /> from the <see cref="IScheduler" />'s
        /// list of <i>global</i> listeners.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>true if the identifed listener was found in the list, and removed.</returns>
        bool RemoveGlobalTriggerListener(string name);
        
		/// <summary>
		/// Remove the identifed <see cref="ITriggerListener" /> from the
		/// <see cref="IScheduler" />'s list of registered listeners.
		/// </summary>
		/// <returns> 
		/// true if the identifed listener was found in the list, and removed.
		/// </returns>
		bool RemoveTriggerListener(string name);

		/// <summary>
		/// Get the <i>non-global</i><see cref="ITriggerListener" /> that
		/// has the given name.
		/// </summary>
		ITriggerListener GetTriggerListener(string name);

		/// <summary>
		/// Register the given <see cref="ISchedulerListener" /> with the
		/// </summary>
		void AddSchedulerListener(ISchedulerListener schedulerListener);

		/// <summary> 
		/// Remove the given <see cref="ISchedulerListener" /> from the
		/// <see cref="IScheduler" />.
		/// </summary>
		/// <returns> true if the identifed listener was found in the list, and
		/// removed.
		/// </returns>
		bool RemoveSchedulerListener(ISchedulerListener schedulerListener);
	}
}
