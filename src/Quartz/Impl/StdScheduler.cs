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
	/// <seealso cref="SchedulingContext" />
	/// <author>James House</author>
	public class StdScheduler : IScheduler
	{
        private readonly QuartzScheduler sched;
        private readonly SchedulingContext schedCtxt;
        
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
		public IList GetCurrentlyExecutingJobs()
		{
			return sched.CurrentlyExecutingJobs;
		}

		/// <seealso cref="QuartzScheduler.GetPausedTriggerGroups(SchedulingContext)">
		/// </seealso>
		public ISet GetPausedTriggerGroups()
		{
			return sched.GetPausedTriggerGroups(schedCtxt);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual string[] JobGroupNames
		{
			get { return sched.GetJobGroupNames(schedCtxt); }
		}

		/// <summary> 
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual string[] TriggerGroupNames
		{
			get { return sched.GetTriggerGroupNames(schedCtxt); }
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual string[] CalendarNames
		{
			get { return sched.GetCalendarNames(schedCtxt); }
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual IList GlobalJobListeners
		{
			get { return sched.GlobalJobListeners; }
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual ISet JobListenerNames
		{
			get { return sched.JobListenerNames; }
		}

	    public IJobListener GetGlobalJobListener(string name)
	    {
            return sched.GetGlobalJobListener(name);
	    }

	    public ITriggerListener GetGlobalTriggerListener(string name)
	    {
            return sched.GetGlobalTriggerListener(name);
	    }

	    /// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual IList GlobalTriggerListeners
		{
			get { return sched.GlobalTriggerListeners; }
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual ISet TriggerListenerNames
		{
			get { return sched.TriggerListenerNames; }
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual IList SchedulerListeners
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
		/// <see cref="QuartzScheduler" /> instance, and with the given <see cref="SchedulingContext" />.
		/// </summary>
		public StdScheduler(QuartzScheduler sched, SchedulingContext schedCtxt)
		{
			this.sched = sched;
			this.schedCtxt = schedCtxt;
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
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual DateTime ScheduleJob(JobDetail jobDetail, Trigger trigger)
		{
			return sched.ScheduleJob(schedCtxt, jobDetail, trigger);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual DateTime ScheduleJob(Trigger trigger)
		{
			return sched.ScheduleJob(schedCtxt, trigger);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual void AddJob(JobDetail jobDetail, bool replace)
		{
			sched.AddJob(schedCtxt, jobDetail, replace);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual bool DeleteJob(string jobName, string groupName)
		{
			return sched.DeleteJob(schedCtxt, jobName, groupName);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual bool UnscheduleJob(string triggerName, string groupName)
		{
			return sched.UnscheduleJob(schedCtxt, triggerName, groupName);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual NullableDateTime RescheduleJob(string triggerName, string groupName, Trigger newTrigger)
		{
			return sched.RescheduleJob(schedCtxt, triggerName, groupName, newTrigger);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual void TriggerJob(string jobName, string groupName)
		{
			TriggerJob(jobName, groupName, null);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual void TriggerJob(string jobName, string groupName, JobDataMap data)
		{
			sched.TriggerJob(schedCtxt, jobName, groupName, data);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual void TriggerJobWithVolatileTrigger(string jobName, string groupName)
		{
			TriggerJobWithVolatileTrigger(jobName, groupName, null);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual void TriggerJobWithVolatileTrigger(string jobName, string groupName, JobDataMap data)
		{
			sched.TriggerJobWithVolatileTrigger(schedCtxt, jobName, groupName, data);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual void PauseTrigger(string triggerName, string groupName)
		{
			sched.PauseTrigger(schedCtxt, triggerName, groupName);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual void PauseTriggerGroup(string groupName)
		{
			sched.PauseTriggerGroup(schedCtxt, groupName);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual void PauseJob(string jobName, string groupName)
		{
			sched.PauseJob(schedCtxt, jobName, groupName);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual void PauseJobGroup(string groupName)
		{
			sched.PauseJobGroup(schedCtxt, groupName);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual void ResumeTrigger(string triggerName, string groupName)
		{
			sched.ResumeTrigger(schedCtxt, triggerName, groupName);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual void ResumeTriggerGroup(string groupName)
		{
			sched.ResumeTriggerGroup(schedCtxt, groupName);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual void ResumeJob(string jobName, string groupName)
		{
			sched.ResumeJob(schedCtxt, jobName, groupName);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual void ResumeJobGroup(string groupName)
		{
			sched.ResumeJobGroup(schedCtxt, groupName);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual void PauseAll()
		{
			sched.PauseAll(schedCtxt);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual void ResumeAll()
		{
			sched.ResumeAll(schedCtxt);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual Trigger[] GetTriggersOfJob(string jobName, string groupName)
		{
			return sched.GetTriggersOfJob(schedCtxt, jobName, groupName);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual string[] GetJobNames(string groupName)
		{
			return sched.GetJobNames(schedCtxt, groupName);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual string[] GetTriggerNames(string groupName)
		{
			return sched.GetTriggerNames(schedCtxt, groupName);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual JobDetail GetJobDetail(string jobName, string jobGroup)
		{
			return sched.GetJobDetail(schedCtxt, jobName, jobGroup);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual Trigger GetTrigger(string triggerName, string triggerGroup)
		{
			return sched.GetTrigger(schedCtxt, triggerName, triggerGroup);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual TriggerState GetTriggerState(string triggerName, string triggerGroup)
		{
			return sched.GetTriggerState(schedCtxt, triggerName, triggerGroup);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual void AddCalendar(string calName, ICalendar calendar, bool replace, bool updateTriggers)
		{
			sched.AddCalendar(schedCtxt, calName, calendar, replace, updateTriggers);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual bool DeleteCalendar(string calName)
		{
			return sched.DeleteCalendar(schedCtxt, calName);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />,
		/// passing the <see cref="SchedulingContext" /> associated with this
		/// instance.
		/// </summary>
		public virtual ICalendar GetCalendar(string calName)
		{
			return sched.GetCalendar(schedCtxt, calName);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual void AddGlobalJobListener(IJobListener jobListener)
		{
			sched.AddGlobalJobListener(jobListener);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual void AddJobListener(IJobListener jobListener)
		{
			sched.AddJobListener(jobListener);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual bool RemoveGlobalJobListener(IJobListener jobListener)
		{
            return sched.RemoveGlobalJobListener((jobListener == null) ? null : jobListener.Name);
			
		}

	    public bool RemoveGlobalJobListener(string name)
	    {
            return sched.RemoveGlobalJobListener(name);
	    }

	    /// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual bool RemoveJobListener(string name)
		{
			return sched.RemoveJobListener(name);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual IJobListener GetJobListener(string name)
		{
			return sched.GetJobListener(name);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual void AddGlobalTriggerListener(ITriggerListener triggerListener)
		{
			sched.AddGlobalTriggerListener(triggerListener);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual void AddTriggerListener(ITriggerListener triggerListener)
		{
			sched.AddTriggerListener(triggerListener);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual bool RemoveGlobalTriggerListener(ITriggerListener triggerListener)
		{
            return sched.RemoveGlobalTriggerListener((triggerListener == null) ? null : triggerListener.Name);

		}

	    public bool RemoveGlobalTriggerListener(string name)
	    {
            return sched.RemoveGlobalTriggerListener(name);
        }

	    /// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual bool RemoveTriggerListener(string name)
		{
			return sched.RemoveTriggerListener(name);
		}

		/// <summary>
		/// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
		/// </summary>
		public virtual ITriggerListener GetTriggerListener(string name)
		{
			return sched.GetTriggerListener(name);
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

		public virtual bool Interrupt(string jobName, string groupName)
		{
			return sched.Interrupt(schedCtxt, jobName, groupName);
		}
	}
}
