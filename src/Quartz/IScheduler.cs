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

using Nullables;

using Quartz.Collection;
using Quartz.Spi;

namespace Quartz
{
	/// <summary> <p>
	/// This is the main interface of a Quartz Scheduler.
	/// </p>
	/// 
	/// <p>
	/// A <code>Scheduler</code> maintains a registery of <code>{@link org.quartz.JobDetail}</code>
	/// s and <code>{@link Trigger}</code>s. Once registered, the <code>Scheduler</code>
	/// is responible for executing <code>Job</code> s when their associated
	/// <code>Trigger</code> s fire (when their scheduled time arrives).
	/// </p>
	/// 
	/// <p>
	/// <code>Scheduler</code> instances are produced by a <code>{@link SchedulerFactory}</code>.
	/// A scheduler that has already been created/initialized can be found and used
	/// through the same factory that produced it. After a <code>Scheduler</code>
	/// has been created, it is in "stand-by" mode, and must have its 
	/// <code>start()</code> method called before it will fire any <code>Job</code>s.
	/// </p>
	/// 
	/// <p>
	/// <code>Job</code> s are to be created by the 'client program', by defining
	/// a class that implements the <code>{@link org.quartz.Job}</code>
	/// interface. <code>{@link JobDetail}</code> objects are then created (also
	/// by the client) to define a individual instances of the <code>Job</code>.
	/// <code>JobDetail</code> instances can then be registered with the <code>Scheduler</code>
	/// via the <code>scheduleJob(JobDetail, Trigger)</code> or <code>addJob(JobDetail, boolean)</code>
	/// method.
	/// </p>
	/// 
	/// <p>
	/// <code>Trigger</code> s can then be defined to fire individual <code>Job</code>
	/// instances based on given schedules. <code>SimpleTrigger</code> s are most
	/// useful for one-time firings, or firing at an exact moment in time, with N
	/// repeats with a given delay between them. <code>CronTrigger</code> s allow
	/// scheduling based on time of day, day of week, day of month, and month of
	/// year.
	/// </p>
	/// 
	/// <p>
	/// <code>Job</code> s and <code>Trigger</code> s have a name and group
	/// associated with them, which should uniquely identify them within a single
	/// <code>{@link Scheduler}</code>. The 'group' feature may be useful for
	/// creating logical groupings or categorizations of <code>Jobs</code> s and
	/// <code>Triggers</code>s. If you don't have need for assigning a group to a
	/// given <code>Jobs</code> of <code>Triggers</code>, then you can use the
	/// <code>DEFAULT_GROUP</code> constant defined on this interface.
	/// </p>
	/// 
	/// <p>
	/// Stored <code>Job</code> s can also be 'manually' triggered through the use
	/// of the <code>triggerJob(String jobName, string jobGroup)</code> function.
	/// </p>
	/// 
	/// <p>
	/// Client programs may also be interested in the 'listener' interfaces that are
	/// available from Quartz. The <code>{@link JobListener}</code> interface
	/// provides notifications of <code>Job</code> executions. The <code>{@link TriggerListener}</code>
	/// interface provides notifications of <code>Trigger</code> firings. The
	/// <code>{@link SchedulerListener}</code> interface provides notifications of
	/// <code>Scheduler</code> events and errors.
	/// </p>
	/// 
	/// <p>
	/// The setup/configuration of a <code>Scheduler</code> instance is very
	/// customizable. Please consult the documentation distributed with Quartz.
	/// </p>
	/// 
	/// </summary>
	/// <seealso cref="IJob">
	/// </seealso>
	/// <seealso cref="JobDetail">
	/// </seealso>
	/// <seealso cref="Trigger">
	/// </seealso>
	/// <seealso cref="IJobListener">
	/// </seealso>
	/// <seealso cref="ITriggerListener">
	/// </seealso>
	/// <seealso cref="ISchedulerListener">
	/// 
	/// </seealso>
	/// <author>  James House
	/// </author>
	/// <author>  Sharada Jambula
	/// </author>
	public struct Scheduler_Fields
	{
		/// <summary> <p>
		/// A (possibly) usefull constant that can be used for specifying the group
		/// that <code>Job</code> and <code>Trigger</code> instances belong to.
		/// </p>
		/// </summary>
		public static readonly string DEFAULT_GROUP = "DEFAULT";

		/// <summary> <p>
		/// A constant <code>Trigger</code> group name used internally by the
		/// scheduler - clients should not use the value of this constant
		/// ("MANUAL_TRIGGER") for thename of a <code>Trigger</code>'s group.
		/// </p>
		/// </summary>
		public static readonly string DEFAULT_MANUAL_TRIGGERS = "MANUAL_TRIGGER";

		/// <summary> <p>
		/// A constant <code>Trigger</code> group name used internally by the
		/// scheduler - clients should not use the value of this constant
		/// ("RECOVERING_JOBS") for thename of a <code>Trigger</code>'s group.
		/// </p>
		/// </summary>
		public static readonly string DEFAULT_RECOVERY_GROUP = "RECOVERING_JOBS";

		/// <summary> <p>
		/// A constant <code>Trigger</code> group name used internally by the
		/// scheduler - clients should not use the value of this constant
		/// ("FAILED_OVER_JOBS") for thename of a <code>Trigger</code>'s group.
		/// </p>
		/// </summary>
		public static readonly string DEFAULT_FAIL_OVER_GROUP = "FAILED_OVER_JOBS";
	}

	public interface IScheduler
	{
		/// <summary> <p>
		/// Returns the name of the <code>Scheduler</code>.
		/// </p>
		/// </summary>
		string SchedulerName { get; }

		/// <summary> <p>
		/// Returns the instance Id of the <code>Scheduler</code>.
		/// </p>
		/// </summary>
		string SchedulerInstanceId { get; }

		/// <summary> <p>
		/// Returns the <code>SchedulerContext</code> of the <code>Scheduler</code>.
		/// </p>
		/// </summary>
		SchedulerContext Context { get; }

		/// <summary> <p>
		/// Reports whether the <code>Scheduler</code> is in stand-by mode.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="Standby()">
		/// </seealso>
		/// <seealso cref="Start()">
		/// </seealso>
		bool InStandbyMode { get; }

		/// <summary> <p>
		/// Reports whether the <code>Scheduler</code> has been Shutdown.
		/// </p>
		/// </summary>
		bool IsShutdown { get; }

		/// <summary>
		/// Get a <code>SchedulerMetaData</code> object describiing the settings
		/// and capabilities of the scheduler instance.
		/// <p>
		/// Note that the data returned is an 'instantaneous' snap-shot, and that as
		/// soon as it's returned, the meta data values may be different.
		/// </p>
		/// </summary>
		SchedulerMetaData GetMetaData();

		/// <summary>
		/// Return a list of <code>JobExecutionContext</code> objects that
		/// represent all currently executing Jobs.
		/// <p>
		/// Note that the list returned is an 'instantaneous' snap-shot, and that as
		/// soon as it's returned, the true list of executing jobs may be different.
		/// Also please read the doc associated with <code>JobExecutionContext</code>-
		/// especially if you're using remoting.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="JobExecutionContext">
		/// </seealso>
		IList GetCurrentlyExecutingJobs();

		/// <summary>
		/// Set the <code>JobFactory</code> that will be responsible for producing 
		/// instances of <code>Job</code> classes.
		/// <p>
		/// JobFactories may be of use to those wishing to have their application
		/// produce <code>Job</code> instances via some special mechanism, such as to
		/// give the opertunity for dependency injection.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="IJobFactory" />
		/// <throws>  SchedulerException </throws>
		IJobFactory JobFactory { set; }

		/// <summary>
		/// Get the names of all known <code>{@link org.quartz.JobDetail}</code>
		/// groups.
		/// </summary>
		string[] JobGroupNames { get; }

		/// <summary>
		/// Get the names of all known <code>{@link Trigger}</code> groups.
		/// </summary>
		string[] TriggerGroupNames { get; }

		/// <summary> 
		/// Get the names of all <code>Trigger</code> groups that are paused.
		/// </summary>
		/// <returns>
		/// </returns>
		/// <throws>  SchedulerException </throws>
		ISet GetPausedTriggerGroups();

		/// <summary>
		/// Get the names of all registered <code>ICalendar</code>s.
		/// </summary>
		string[] CalendarNames { get; }

		/// <summary>
		/// Get a List containing all of the <code>JobListener</code> s in
		/// the <code>Scheduler</code>'s<i>global</i> list.
		/// </summary>
		IList GlobalJobListeners { get; }

		/// <summary>
		/// Get a Set containing the names of all the <i>non-global</i><code>JobListener</code>
		/// s registered with the <code>Scheduler</code>.
		/// </summary>
		ISet JobListenerNames { get; }

		/// <summary>
		/// Get a List containing all of the <code>TriggerListener</code>
		/// s in the <code>Scheduler</code>'s<i>global</i> list.
		/// </summary>
		IList GlobalTriggerListeners { get; }

		/// <summary> <p>
		/// Get a Set containing the names of all the <i>non-global</i><code>{@link TriggerListener}</code>
		/// s registered with the <code>Scheduler</code>.
		/// </p>
		/// </summary>
		ISet TriggerListenerNames { get; }

		/// <summary> <p>
		/// Get a List containing all of the <code>{@link SchedulerListener}</code>
		/// s registered with the <code>Scheduler</code>.
		/// </p>
		/// </summary>
		IList SchedulerListeners { get; }


		/// <summary>
		/// Starts the <code>Scheduler</code>'s threads that fire <code>{@link Trigger}s</code>.
		/// When a scheduler is first created it is in "stand-by" mode, and will not
		/// fire triggers.  The scheduler can also be put into stand-by mode by
		/// calling the <code>standby()</code> method. 
		/// <p>
		/// All <code>{@link Trigger}s</code> that have misfired will be passed
		/// to the appropriate TriggerListener(s).
		/// </p>
		/// 
		/// </summary>
		/// <throws>  SchedulerException </throws>
		/// <summary>           if <code>Shutdown()</code> has been called, or there is an
		/// error within the <code>Scheduler</code>.
		/// 
		/// </summary>
		/// <seealso cref="Standby">
		/// </seealso>
		/// <seealso cref="Shutdown(bool)">
		/// </seealso>
		void Start();

		/// <summary>
		/// Temporarily halts the <code>Scheduler</code>'s firing of <code>{@link Trigger}s</code>.
		/// <p>
		/// When <code>start()</code> is called (to bring the scheduler out of 
		/// stand-by mode), trigger misfire instructions will NOT be applied.
		/// </p>
		/// <p>
		/// The scheduler is not destroyed, and can be re-started at any time.
		/// </p>
		/// </summary>
		/// <seealso cref="Start()">
		/// </seealso>
		/// <seealso cref="PauseAll()">
		/// </seealso>
		void Standby();


		/// <summary> <p>
		/// Halts the <code>Scheduler</code>'s firing of <code>{@link Trigger}s</code>,
		/// and cleans up all resources associated with the Scheduler. Equivalent to
		/// <code>Shutdown(false)</code>.
		/// </p>
		/// 
		/// <p>
		/// The scheduler cannot be re-started.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="Shutdown(bool)">
		/// </seealso>
		void Shutdown();

		/// <summary>
		/// Halts the <code>Scheduler</code>'s firing of <code>{@link Trigger}s</code>,
		/// and cleans up all resources associated with the Scheduler. 
		/// <p>
		/// The scheduler cannot be re-started.
		/// </p>
		/// 
		/// </summary>
		/// <param name="waitForJobsToComplete">
		/// if <code>true</code> the scheduler will not allow this method
		/// to return until all currently executing jobs have completed.
		/// 
		/// </param>
		/// <seealso cref="Shutdown()">
		/// </seealso>
		void Shutdown(bool waitForJobsToComplete);


		/// <summary>
		/// Add the given <code>{@link org.quartz.JobDetail}</code> to the
		/// Scheduler, and associate the given <code>{@link Trigger}</code> with
		/// it.
		/// <p>
		/// If the given Trigger does not reference any <code>Job</code>, then it
		/// will be set to reference the Job passed with it into this method.
		/// </p>
		/// 
		/// </summary>
		/// <throws>  SchedulerException </throws>
		/// <summary>           if the Job or Trigger cannot be added to the Scheduler, or
		/// there is an internal Scheduler error.
		/// </summary>
		DateTime ScheduleJob(JobDetail jobDetail, Trigger trigger);

		/// <summary> <p>
		/// Schedule the given <code>{@link org.quartz.Trigger}</code> with the
		/// <code>Job</code> identified by the <code>Trigger</code>'s settings.
		/// </p>
		/// 
		/// </summary>
		/// <throws>  SchedulerException </throws>
		/// <summary>           if the indicated Job does not exist, or the Trigger cannot be
		/// added to the Scheduler, or there is an internal Scheduler
		/// error.
		/// </summary>
		DateTime ScheduleJob(Trigger trigger);

		/// <summary> <p>
		/// Remove the indicated <code>{@link Trigger}</code> from the scheduler.
		/// </p>
		/// </summary>
		bool UnscheduleJob(string triggerName, string groupName);

		/// <summary>
		/// Remove (delete) the <code>Trigger</code> with the
		/// given name, and store the new given one - which must be associated
		/// with the same job - however, the new trigger need not have the same 
		/// name as the old trigger.
		/// </summary>
		/// <param name="triggerName">
		/// The name of the <code>Trigger</code> to be replaced.
		/// </param>
		/// <param name="groupName">
		/// The group name of the <code>Trigger</code> to be replaced.
		/// </param>
		/// <param name="newTrigger">
		/// The new <code>Trigger</code> to be stored.
		/// </param>
		/// <returns> <code>null</code> if a <code>Trigger</code> with the given
		/// name and group was not found and removed from the store, otherwise
		/// the first fire time of the newly scheduled trigger.
		/// </returns>
		NullableDateTime RescheduleJob(string triggerName, string groupName, Trigger newTrigger);


		/// <summary> <p>
		/// Add the given <code>Job</code> to the Scheduler - with no associated
		/// <code>Trigger</code>. The <code>Job</code> will be 'dormant' until
		/// it is scheduled with a <code>Trigger</code>, or <code>Scheduler.triggerJob()</code>
		/// is called for it.
		/// </p>
		/// 
		/// <p>
		/// The <code>Job</code> must by definition be 'durable', if it is not,
		/// SchedulerException will be thrown.
		/// </p>
		/// 
		/// </summary>
		/// <throws>  SchedulerException </throws>
		/// <summary>           if there is an internal Scheduler error, or if the Job is not
		/// durable, or a Job with the same name already exists, and
		/// <code>replace</code> is <code>false</code>.
		/// </summary>
		void AddJob(JobDetail jobDetail, bool replace);

		/// <summary> <p>
		/// Delete the identified <code>Job</code> from the Scheduler - and any
		/// associated <code>Trigger</code>s.
		/// </p>
		/// 
		/// </summary>
		/// <returns> true if the Job was found and deleted.
		/// </returns>
		/// <throws>  SchedulerException </throws>
		/// <summary>           if there is an internal Scheduler error.
		/// </summary>
		bool DeleteJob(string jobName, string groupName);

		/// <summary> <p>
		/// Trigger the identified <code>{@link org.quartz.JobDetail}</code>
		/// (Execute it now) - the generated trigger will be non-volatile.
		/// </p>
		/// </summary>
		void TriggerJob(string jobName, string groupName);

		/// <summary> <p>
		/// Trigger the identified <code>{@link org.quartz.JobDetail}</code>
		/// (Execute it now) - the generated trigger will be volatile.
		/// </p>
		/// </summary>
		void TriggerJobWithVolatileTrigger(string jobName, string groupName);

		/// <summary> <p>
		/// Trigger the identified <code>{@link org.quartz.JobDetail}</code>
		/// (Execute it now) - the generated trigger will be non-volatile.
		/// </p>
		/// 
		/// </summary>
		/// <param name="jobName">the name of the Job to trigger
		/// </param>
		/// <param name="groupName">the group name of the Job to trigger
		/// </param>
		/// <param name="data">the (possibly <code>null</code>) JobDataMap to be 
		/// associated with the trigger that fires the job immediately. 
		/// </param>
		void TriggerJob(string jobName, string groupName, JobDataMap data);

		/// <summary> <p>
		/// Trigger the identified <code>{@link org.quartz.JobDetail}</code>
		/// (Execute it now) - the generated trigger will be volatile.
		/// </p>
		/// 
		/// </summary>
		/// <param name="jobName">the name of the Job to trigger
		/// </param>
		/// <param name="groupName">the group name of the Job to trigger
		/// </param>
		/// <param name="data">the (possibly <code>null</code>) JobDataMap to be 
		/// associated with the trigger that fires the job immediately. 
		/// </param>
		void TriggerJobWithVolatileTrigger(string jobName, string groupName, JobDataMap data);

		/// <summary>
		/// Pause the <code>{@link org.quartz.JobDetail}</code> with the given
		/// name - by pausing all of its current <code>Trigger</code>s.
		/// </summary>
		void PauseJob(string jobName, string groupName);

		/// <summary> <p>
		/// Pause all of the <code>{@link org.quartz.JobDetail}s</code> in the
		/// given group - by pausing all of their <code>Trigger</code>s.
		/// </p>
		/// 
		/// <p>
		/// The Scheduler will "remember" that the group is paused, and impose the
		/// pause on any new jobs that are added to the group while the group is
		/// paused.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="ResumeJobGroup(String)">
		/// </seealso>
		void PauseJobGroup(string groupName);

		/// <summary> 
		/// Pause the <code>{@link Trigger}</code> with the given name.
		/// </summary>
		void PauseTrigger(string triggerName, string groupName);

		/// <summary> <p>
		/// Pause all of the <code>{@link Trigger}s</code> in the given group.
		/// </p>
		/// 
		/// <p>
		/// The Scheduler will "remember" that the group is paused, and impose the
		/// pause on any new triggers that are added to the group while the group is
		/// paused.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="ResumeTriggerGroup(String)">
		/// </seealso>
		void PauseTriggerGroup(string groupName);

		/// <summary>
		/// Resume (un-pause) the <code>{@link org.quartz.JobDetail}</code> with
		/// the given name.
		/// <p>
		/// If any of the <code>Job</code>'s<code>Trigger</code> s missed one
		/// or more fire-times, then the <code>Trigger</code>'s misfire
		/// instruction will be applied.
		/// </p>
		/// 
		/// </summary>
		void ResumeJob(string jobName, string groupName);

		/// <summary> <p>
		/// Resume (un-pause) all of the <code>{@link org.quartz.JobDetail}s</code>
		/// in the given group.
		/// </p>
		/// 
		/// <p>
		/// If any of the <code>Job</code> s had <code>Trigger</code> s that
		/// missed one or more fire-times, then the <code>Trigger</code>'s
		/// misfire instruction will be applied.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="PauseJobGroup(String)">
		/// </seealso>
		void ResumeJobGroup(string groupName);

		/// <summary> <p>
		/// Resume (un-pause) the <code>{@link Trigger}</code> with the given
		/// name.
		/// </p>
		/// 
		/// <p>
		/// If the <code>Trigger</code> missed one or more fire-times, then the
		/// <code>Trigger</code>'s misfire instruction will be applied.
		/// </p>
		/// 
		/// </summary>
		void ResumeTrigger(string triggerName, string groupName);

		/// <summary> <p>
		/// Resume (un-pause) all of the <code>{@link Trigger}s</code> in the
		/// given group.
		/// </p>
		/// 
		/// <p>
		/// If any <code>Trigger</code> missed one or more fire-times, then the
		/// <code>Trigger</code>'s misfire instruction will be applied.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="PauseTriggerGroup(String)">
		/// </seealso>
		void ResumeTriggerGroup(string groupName);

		/// <summary> <p>
		/// Pause all triggers - similar to calling <code>PauseTriggerGroup(group)</code>
		/// on every group, however, after using this method <code>ResumeAll()</code> 
		/// must be called to clear the scheduler's state of 'remembering' that all 
		/// new triggers will be paused as they are added. 
		/// </p>
		/// 
		/// <p>
		/// When <code>ResumeAll()</code> is called (to un-pause), trigger misfire
		/// instructions WILL be applied.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="ResumeAll()">
		/// </seealso>
		/// <seealso cref="PauseTriggerGroup(String)">
		/// </seealso>
		/// <seealso cref="Standby()">
		/// </seealso>
		void PauseAll();

		/// <summary> <p>
		/// Resume (un-pause) all triggers - similar to calling 
		/// <code>ResumeTriggerGroup(group)</code> on every group.
		/// </p>
		/// 
		/// <p>
		/// If any <code>Trigger</code> missed one or more fire-times, then the
		/// <code>Trigger</code>'s misfire instruction will be applied.
		/// </p>
		/// 
		/// </summary>
		/// <seealso cref="PauseAll()">
		/// </seealso>
		void ResumeAll();

		/// <summary> <p>
		/// Get the names of all the <code>{@link org.quartz.JobDetail}s</code>
		/// in the given group.
		/// </p>
		/// </summary>
		string[] GetJobNames(string groupName);

		/// <summary> <p>
		/// Get all <code>{@link Trigger}</code> s that are associated with the
		/// identified <code>{@link org.quartz.JobDetail}</code>.
		/// </p>
		/// </summary>
		Trigger[] GetTriggersOfJob(string jobName, string groupName);

		/// <summary> <p>
		/// Get the names of all the <code>{@link Trigger}s</code> in the given
		/// group.
		/// </p>
		/// </summary>
		string[] GetTriggerNames(string groupName);

		/// <summary> <p>
		/// Get the <code>{@link JobDetail}</code> for the <code>Job</code>
		/// instance with the given name and group.
		/// </p>
		/// </summary>
		JobDetail GetJobDetail(string jobName, string jobGroup);

		/// <summary> <p>
		/// Get the <code>{@link Trigger}</code> instance with the given name and
		/// group.
		/// </p>
		/// </summary>
		Trigger GetTrigger(string triggerName, string triggerGroup);

		/// <summary>
		/// Get the current state of the identified <code>{@link Trigger}</code>.
		/// </summary>
		/// <seealso cref="Trigger.STATE_NORMAL">
		/// </seealso>
		/// <seealso cref="Trigger.STATE_PAUSED">
		/// </seealso>
		/// <seealso cref="Trigger.STATE_COMPLETE">
		/// </seealso>
		/// <seealso cref="Trigger.STATE_ERROR">
		/// </seealso>
		/// <seealso cref="Trigger.STATE_BLOCKED">
		/// </seealso>
		/// <seealso cref="Trigger.STATE_NONE">
		/// </seealso>
		int GetTriggerState(string triggerName, string triggerGroup);

		/// <summary> <p>
		/// Add (register) the given <code>Calendar</code> to the Scheduler.
		/// </p>
		/// 
		/// </summary>
		/// <param name="updateTriggers">whether or not to update existing triggers that
		/// referenced the already existing calendar so that they are 'correct'
		/// based on the new trigger. 
		/// 
		/// 
		/// </param>
		/// <throws>  SchedulerException </throws>
		/// <summary>           if there is an internal Scheduler error, or a Calendar with
		/// the same name already exists, and <code>replace</code> is
		/// <code>false</code>.
		/// </summary>
		void AddCalendar(string calName, ICalendar calendar, bool replace, bool updateTriggers);

		/// <summary> <p>
		/// Delete the identified <code>Calendar</code> from the Scheduler.
		/// </p>
		/// 
		/// </summary>
		/// <returns> true if the Calendar was found and deleted.
		/// </returns>
		/// <throws>  SchedulerException </throws>
		/// <summary>           if there is an internal Scheduler error.
		/// </summary>
		bool DeleteCalendar(string calName);

		/// <summary> <p>
		/// Get the <code>{@link Calendar}</code> instance with the given name.
		/// </p>
		/// </summary>
		ICalendar GetCalendar(string calName);

		/// <summary> <p>
		/// Request the interruption of all currently executing instances of the 
		/// identified <code>Job</code>, which must be an implementor of the 
		/// <code>InterruptableJob</code> interface.
		/// </p>
		/// 
		/// <p>
		/// If more than one instance of the identified job is currently executing,
		/// the <code>InterruptableJob#interrupt()</code> method will be called on
		/// each instance.  However, there is a limitation that in the case that  
		/// <code>interrupt()</code> on one instances throws an exception, all 
		/// remaining  instances (that have not yet been interrupted) will not have 
		/// their <code>interrupt()</code> method called.
		/// </p>
		/// 
		/// <p>
		/// If you wish to interrupt a specific instance of a job (when more than
		/// one is executing) you can do so by calling 
		/// <code>{@link #getCurrentlyExecutingJobs()}</code> to obtain a handle 
		/// to the job instance, and then invoke <code>interrupt()</code> on it
		/// yourself.
		/// </p>
		/// 
		/// </summary>
		/// <param name="jobName">
		/// </param>
		/// <param name="groupName">
		/// </param>
		/// <returns> true is at least one instance of the identified job was found
		/// and interrupted.
		/// </returns>
		/// <throws>  UnableToInterruptJobException if the job does not implement </throws>
		/// <summary> <code>InterruptableJob</code>, or there is an exception while 
		/// interrupting the job.
		/// </summary>
		/// <seealso cref="IInterruptableJob" />
		/// <seealso cref="GetCurrentlyExecutingJobs" />
		bool Interrupt(string jobName, string groupName);

		/// <summary> <p>
		/// Add the given <code>{@link JobListener}</code> to the <code>Scheduler</code>'s
		/// <i>global</i> list.
		/// </p>
		/// 
		/// <p>
		/// Listeners in the 'global' list receive notification of execution events
		/// for ALL <code>{@link org.quartz.JobDetail}</code>s.
		/// </p>
		/// </summary>
		void AddGlobalJobListener(IJobListener jobListener);

		/// <summary> 
		/// Add the given <code>JobListener</code> to the <code>Scheduler</code>'s
		/// list, of registered <code>JobListener</code>s.
		/// </summary>
		void AddJobListener(IJobListener jobListener);

		/// <summary>
		/// Remove the given <code>{@link JobListener}</code> from the <code>Scheduler</code>'s
		/// list of <i>global</i> listeners.
		/// </summary>
		/// <returns> true if the identifed listener was found in the list, and
		/// removed.
		/// </returns>
		bool RemoveGlobalJobListener(IJobListener jobListener);

		/// <summary> <p>
		/// Remove the identifed <code>{@link JobListener}</code> from the <code>Scheduler</code>'s
		/// list of registered listeners.
		/// </p>
		/// 
		/// </summary>
		/// <returns> true if the identifed listener was found in the list, and
		/// removed.
		/// </returns>
		bool RemoveJobListener(string name);

		/// <summary> <p>
		/// Get the <i>non-global</i><code>{@link JobListener}</code> that has
		/// the given name.
		/// </p>
		/// </summary>
		IJobListener GetJobListener(string name);

		/// <summary>
		/// Add the given <code>TriggerListener</code> to the <code>Scheduler</code>'s
		/// <i>global</i> list.
		/// <p>
		/// Listeners in the 'global' list receive notification of execution events
		/// for ALL <code>Trigger</code>s.
		/// </p>
		/// </summary>
		void AddGlobalTriggerListener(ITriggerListener triggerListener);

		/// <summary>
		/// Add the given <code>TriggerListener</code> to the <code>Scheduler</code>'s
		/// list, of registered <code>TriggerListener</code>s.
		/// </summary>
		void AddTriggerListener(ITriggerListener triggerListener);

		/// <summary>
		/// Remove the given <code>TriggerListener</code> from the <code>Scheduler</code>'s
		/// list of <i>global</i> listeners.
		/// </summary>
		/// <returns> true if the identifed listener was found in the list, and
		/// removed.
		/// </returns>
		bool RemoveGlobalTriggerListener(ITriggerListener triggerListener);

		/// <summary> <p>
		/// Remove the identifed <code>{@link TriggerListener}</code> from the
		/// <code>Scheduler</code>'s list of registered listeners.
		/// </p>
		/// 
		/// </summary>
		/// <returns> true if the identifed listener was found in the list, and
		/// removed.
		/// </returns>
		bool RemoveTriggerListener(string name);

		/// <summary> <p>
		/// Get the <i>non-global</i><code>{@link TriggerListener}</code> that
		/// has the given name.
		/// </p>
		/// </summary>
		ITriggerListener GetTriggerListener(string name);

		/// <summary> <p>
		/// Register the given <code>{@link SchedulerListener}</code> with the
		/// <code>Scheduler</code>.
		/// </p>
		/// </summary>
		void AddSchedulerListener(ISchedulerListener schedulerListener);

		/// <summary> <p>
		/// Remove the given <code>{@link SchedulerListener}</code> from the
		/// <code>Scheduler</code>.
		/// </p>
		/// 
		/// </summary>
		/// <returns> true if the identifed listener was found in the list, and
		/// removed.
		/// </returns>
		bool RemoveSchedulerListener(ISchedulerListener schedulerListener);
	}
}