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
using Quartz.Spi;

namespace Quartz.Impl
{
	/// <summary>
	/// An implementation of the <see cref="IScheduler" /> interface that directly
	/// proxies all method calls to the equivalent call on a given <see cref="QuartzScheduler" />
	/// instance.
	/// </summary>
	/// <seealso cref="IScheduler" />
	/// <seealso cref="QuartzScheduler" />
	/// <author>James House</author>
	/// <author>Marko Lahma (.NET)</author>
	public class StdScheduler : IScheduler
	{
        private readonly QuartzScheduler sched;
        
	    /// <summary>
	    /// returns true if the given JobGroup
	    /// is paused
	    /// </summary>
	    /// <param name="groupName"></param>
	    /// <returns></returns>
	    public bool IsJobGroupPaused(string groupName)
	    {
            return sched.IsJobGroupPaused(groupName);
	    }

	    /// <summary>
	    /// returns true if the given TriggerGroup
	    /// is paused
	    /// </summary>
	    /// <param name="groupName"></param>
	    /// <returns></returns>
	    public bool IsTriggerGroupPaused(string groupName)
	    {
            return sched.IsTriggerGroupPaused(groupName);
	    }

	    /// <summary>
		/// Returns the name of the <see cref="IScheduler" />.
		/// </summary>
		public virtual string SchedulerName
		{
			get { return sched.SchedulerName; }
		}

		/// <summary>
		/// Returns the instance Id of the <see cref="IScheduler" />.
		/// </summary>
		public virtual string SchedulerInstanceId
		{
			get { return sched.SchedulerInstanceId; }
		}

        /// <summary>
        /// Get a <see cref="SchedulerMetaData"/> object describiing the settings
        /// and capabilities of the scheduler instance.
        /// <p>
        /// Note that the data returned is an 'instantaneous' snap-shot, and that as
        /// soon as it's returned, the meta data values may be different.
        /// </p>
        /// </summary>
        /// <returns></returns>
		public SchedulerMetaData GetMetaData()
		{
			return new SchedulerMetaData(
				SchedulerName,
				SchedulerInstanceId,
				GetType(),
				false,
				IsStarted,
				InStandbyMode,
				IsShutdown,
				sched.RunningSince,
				sched.NumJobsExecuted,
				sched.JobStoreClass,
				sched.SupportsPersistence,
                sched.Clustered,
				sched.ThreadPoolClass,
				sched.ThreadPoolSize,
				sched.Version);
		}

		/// <summary>
		/// Returns the <see cref="SchedulerContext" /> of the <see cref="IScheduler" />.
		/// </summary>
		public virtual SchedulerContext Context
		{
			get { return sched.SchedulerContext; }
		}

        /// <summary>
        /// Whether the scheduler has been started.
        /// </summary>
        /// <value></value>
        /// <remarks>
        /// Note: This only reflects whether <see cref="Start"/> has ever
        /// been called on this Scheduler, so it will return <see langword="true" /> even
        /// if the <see cref="IScheduler" /> is currently in standby mode or has been
        /// since shutdown.
        /// </remarks>
        /// <seealso cref="Start"/>
        /// <seealso cref="IsShutdown"/>
        /// <seealso cref="InStandbyMode"/>
	    public bool IsStarted
	    {
	        get { return sched.RunningSince.HasValue; }
	    }

	    /// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual bool InStandbyMode
		{
			get { return sched.InStandbyMode; }
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual bool IsShutdown
		{
			get { return sched.IsShutdown; }
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public IList<JobExecutionContext> GetCurrentlyExecutingJobs()
		{
			return sched.CurrentlyExecutingJobs;
		}

		/// <seealso cref="QuartzScheduler.GetPausedTriggerGroups()" />
        public Collection.ISet<string> GetPausedTriggerGroups()
		{
			return sched.GetPausedTriggerGroups();
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual IList<string> JobGroupNames
		{
			get { return sched.GetJobGroupNames(); }
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual IList<string> TriggerGroupNames
		{
			get { return sched.GetTriggerGroupNames(); }
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual IList<string> CalendarNames
		{
			get { return sched.GetCalendarNames(); }
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual IList<IJobListener> GlobalJobListeners
		{
			get { return sched.GlobalJobListeners; }
		}

        /// <summary>
        /// Get the <i>global</i><see cref="IJobListener"/> that has
        /// the given name.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
	    public IJobListener GetGlobalJobListener(string name)
	    {
            return sched.GetGlobalJobListener(name);
	    }

        /// <summary>
        /// Get the <i>global</i><see cref="ITriggerListener"/> that
        /// has the given name.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
	    public ITriggerListener GetGlobalTriggerListener(string name)
	    {
            return sched.GetGlobalTriggerListener(name);
	    }

	    /// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual IList<ITriggerListener> GlobalTriggerListeners
		{
			get { return sched.GlobalTriggerListeners; }
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual IList<ISchedulerListener> SchedulerListeners
		{
			get { return sched.SchedulerListeners; }
		}

		/// <seealso cref="IScheduler.JobFactory">
		/// </seealso>
		public virtual IJobFactory JobFactory
		{
			set { sched.JobFactory = value; }
		}


		/// <summary>
		/// Construct a <see cref="StdScheduler" /> instance to proxy the given
		/// <see cref="QuartzScheduler" /> instance.
		/// </summary>
		public StdScheduler(QuartzScheduler sched)
		{
			this.sched = sched;
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual void Start()
		{
			sched.Start();
		}

        /// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
        public void StartDelayed(TimeSpan delay)
        {
            sched.StartDelayed(delay);
        }

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual void Standby()
		{
			sched.Standby();
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual void Shutdown()
		{
			sched.Shutdown();
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual void Shutdown(bool waitForJobsToComplete)
		{
			sched.Shutdown(waitForJobsToComplete);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual DateTimeOffset ScheduleJob(JobDetail jobDetail, Trigger trigger)
		{
			return sched.ScheduleJob(jobDetail, trigger);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual DateTimeOffset ScheduleJob(Trigger trigger)
		{
			return sched.ScheduleJob(trigger);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void AddJob(JobDetail jobDetail, bool replace)
		{
			sched.AddJob(jobDetail, replace);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual bool DeleteJob(string jobName, string groupName)
		{
			return sched.DeleteJob(jobName, groupName);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual bool UnscheduleJob(string triggerName, string groupName)
		{
			return sched.UnscheduleJob(triggerName, groupName);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual DateTimeOffset? RescheduleJob(string triggerName, string groupName, Trigger newTrigger)
		{
			return sched.RescheduleJob(triggerName, groupName, newTrigger);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void TriggerJob(string jobName, string groupName)
		{
			TriggerJob(jobName, groupName, null);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void TriggerJob(string jobName, string groupName, JobDataMap data)
		{
			sched.TriggerJob(jobName, groupName, data);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void TriggerJobWithVolatileTrigger(string jobName, string groupName)
		{
			TriggerJobWithVolatileTrigger(jobName, groupName, null);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void TriggerJobWithVolatileTrigger(string jobName, string groupName, JobDataMap data)
		{
			sched.TriggerJobWithVolatileTrigger(jobName, groupName, data);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void PauseTrigger(string triggerName, string groupName)
		{
			sched.PauseTrigger(triggerName, groupName);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void PauseTriggerGroup(string groupName)
		{
			sched.PauseTriggerGroup(groupName);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void PauseJob(string jobName, string groupName)
		{
			sched.PauseJob(jobName, groupName);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void PauseJobGroup(string groupName)
		{
			sched.PauseJobGroup(groupName);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void ResumeTrigger(string triggerName, string groupName)
		{
			sched.ResumeTrigger(triggerName, groupName);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void ResumeTriggerGroup(string groupName)
		{
			sched.ResumeTriggerGroup(groupName);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void ResumeJob(string jobName, string groupName)
		{
			sched.ResumeJob(jobName, groupName);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void ResumeJobGroup(string groupName)
		{
			sched.ResumeJobGroup(groupName);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void PauseAll()
		{
			sched.PauseAll();
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void ResumeAll()
		{
			sched.ResumeAll();
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual IList<Trigger> GetTriggersOfJob(string jobName, string groupName)
		{
			return sched.GetTriggersOfJob(jobName, groupName);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual IList<string> GetJobNames(string groupName)
		{
			return sched.GetJobNames(groupName);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual IList<string> GetTriggerNames(string groupName)
		{
			return sched.GetTriggerNames(groupName);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual JobDetail GetJobDetail(string jobName, string jobGroup)
		{
			return sched.GetJobDetail(jobName, jobGroup);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Trigger GetTrigger(string triggerName, string triggerGroup)
		{
			return sched.GetTrigger(triggerName, triggerGroup);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual TriggerState GetTriggerState(string triggerName, string triggerGroup)
		{
			return sched.GetTriggerState(triggerName, triggerGroup);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void AddCalendar(string calName, ICalendar calendar, bool replace, bool updateTriggers)
		{
			sched.AddCalendar(calName, calendar, replace, updateTriggers);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual bool DeleteCalendar(string calName)
		{
			return sched.DeleteCalendar(calName);
		}

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual ICalendar GetCalendar(string calName)
		{
			return sched.GetCalendar(calName);
		}

        /// <summary>
        /// Get the names of all registered <see cref="ICalendar"/>.
        /// </summary>
        /// <returns></returns>
	    public IList<string> GetCalendarNames()
	    {
	        return sched.GetCalendarNames();
	    }

	    /// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual void AddGlobalJobListener(IJobListener jobListener)
		{
			sched.AddGlobalJobListener(jobListener);
		}

        /// <summary>
        /// Remove the identifed <see cref="IJobListener"/> from the <see cref="IScheduler"/>'s
        /// list of <i>global</i> listeners.
        /// </summary>
        /// <param name="name"></param>
        /// <returns>
        /// true if the identifed listener was found in the list, and removed
        /// </returns>
	    public bool RemoveGlobalJobListener(string name)
	    {
            return sched.RemoveGlobalJobListener(name);
	    }

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual void AddGlobalTriggerListener(ITriggerListener triggerListener)
		{
			sched.AddGlobalTriggerListener(triggerListener);
		}

        /// <summary>
        /// Remove the identifed <see cref="ITriggerListener"/> from the <see cref="IScheduler"/>'s
        /// list of <i>global</i> listeners.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>
        /// true if the identifed listener was found in the list, and removed.
        /// </returns>
	    public bool RemoveGlobalTriggerListener(string name)
	    {
            return sched.RemoveGlobalTriggerListener(name);
        }

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual void AddSchedulerListener(ISchedulerListener schedulerListener)
		{
			sched.AddSchedulerListener(schedulerListener);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual bool RemoveSchedulerListener(ISchedulerListener schedulerListener)
		{
			return sched.RemoveSchedulerListener(schedulerListener);
		}

        /// <summary>
        /// Request the interruption, within this Scheduler instance, of all
        /// currently executing instances of the identified <see cref="IJob" />, which
        /// must be an implementor of the <see cref="IInterruptableJob"/> interface.
        /// </summary>
        /// <remarks>
        /// <p>
        /// If more than one instance of the identified job is currently executing,
        /// the <see cref="IInterruptableJob.Interrupt"/> method will be called on
        /// each instance.  However, there is a limitation that in the case that
        /// <see cref="Interrupt"/> on one instances throws an exception, all
        /// remaining  instances (that have not yet been interrupted) will not have
        /// their <see cref="Interrupt"/> method called.
        /// </p>
        /// <p>
        /// If you wish to interrupt a specific instance of a job (when more than
        /// one is executing) you can do so by calling
        /// <see cref="GetCurrentlyExecutingJobs"/> to obtain a handle
        /// to the job instance, and then invoke <see cref="Interrupt"/> on it
        /// yourself.
        /// </p>
        /// <p>
        /// This method is not cluster aware.  That is, it will only interrupt
        /// instances of the identified InterruptableJob currently executing in this
        /// Scheduler instance, not across the entire cluster.
        /// </p>
        /// </remarks>
        /// <param name="jobName"></param>
        /// <param name="groupName"></param>
        /// <returns>true is at least one instance of the identified job was found and interrupted.</returns>
        /// <throws>  UnableToInterruptJobException if the job does not implement </throws>
        /// <seealso cref="IInterruptableJob"/>
        /// <seealso cref="GetCurrentlyExecutingJobs"/>
		public virtual bool Interrupt(string jobName, string groupName)
		{
			return sched.Interrupt(jobName, groupName);
		}
	}
}
