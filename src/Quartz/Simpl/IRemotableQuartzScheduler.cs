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

using Quartz.Core;

namespace Quartz.Simpl
{
    /// <summary>
    /// 
    /// </summary>
    /// <author>Marko Lahma (.NET)</author>
    public interface IRemotableQuartzScheduler
	{
		string SchedulerName { get; }
		string SchedulerInstanceId { get; }
		SchedulerContext SchedulerContext { get; }
		bool InStandbyMode { get; }
		bool IsShutdown { get; }
		string Version { get; }
		Type JobStoreClass { get; }
		Type ThreadPoolClass { get; }
		int ThreadPoolSize { get; }
		IList<JobExecutionContext> CurrentlyExecutingJobs { get; }
		IList<IJobListener> GlobalJobListeners { get; }
		ICollection<string> JobListenerNames { get; }
		IList<ITriggerListener> GlobalTriggerListeners { get; }
		ICollection<string> TriggerListenerNames { get; }
		IList<ISchedulerListener> SchedulerListeners { get; }

		/// <summary>
		/// Starts this instance.
		/// </summary>
		void Start();

	    void StartDelayed(TimeSpan delay);

		/// <summary>
		/// Standbies this instance.
		/// </summary>
		void Standby();

		/// <summary>
		/// Shutdowns this instance.
		/// </summary>
		void Shutdown();

        void Shutdown(bool waitForJobsToComplete);

        DateTime? RunningSince { get; }

        int NumJobsExecuted { get; }

		bool SupportsPersistence { get; }

        bool Clustered { get; }

		DateTime ScheduleJob(SchedulingContext ctxt, JobDetail jobDetail, Trigger trigger);

		DateTime ScheduleJob(SchedulingContext ctxt, Trigger trigger);

		void AddJob(SchedulingContext ctxt, JobDetail jobDetail, bool replace);

        /// <summary>
        /// returns true if the given JobGroup
        /// is paused
        /// </summary>
        /// <param name="ctxt">The scheduling context.</param>
        /// <param name="groupName"></param>
        /// <returns></returns>
        bool IsJobGroupPaused(SchedulingContext ctxt,string groupName);

        /// <summary>
        /// returns true if the given TriggerGroup
        /// is paused
        /// </summary>
        /// <param name="ctxt"></param>
        /// <param name="groupName"></param>
        /// <returns></returns>
        bool IsTriggerGroupPaused(SchedulingContext ctxt,string groupName);
        
	    bool DeleteJob(SchedulingContext ctxt, string jobName, string groupName);

		bool UnscheduleJob(SchedulingContext ctxt, string triggerName, string groupName);

        DateTime? RescheduleJob(SchedulingContext ctxt, string triggerName, string groupName, Trigger newTrigger);

		void TriggerJob(SchedulingContext ctxt, string jobName, string groupName, JobDataMap data);

		void TriggerJobWithVolatileTrigger(SchedulingContext ctxt, string jobName, string groupName, JobDataMap data);

		void PauseTrigger(SchedulingContext ctxt, string triggerName, string groupName);

		void PauseTriggerGroup(SchedulingContext ctxt, string groupName);

		void PauseJob(SchedulingContext ctxt, string jobName, string groupName);

		void PauseJobGroup(SchedulingContext ctxt, string groupName);

		void ResumeTrigger(SchedulingContext ctxt, string triggerName, string groupName);

		void ResumeTriggerGroup(SchedulingContext ctxt, string groupName);

		ICollection<string> GetPausedTriggerGroups(SchedulingContext ctxt);

		void ResumeJob(SchedulingContext ctxt, string jobName, string groupName);

		void ResumeJobGroup(SchedulingContext ctxt, string groupName);

		void PauseAll(SchedulingContext ctxt);

		void ResumeAll(SchedulingContext ctxt);

		string[] GetJobGroupNames(SchedulingContext ctxt);

		string[] GetJobNames(SchedulingContext ctxt, string groupName);

		Trigger[] GetTriggersOfJob(SchedulingContext ctxt, string jobName, string groupName);

		string[] GetTriggerGroupNames(SchedulingContext ctxt);

		string[] GetTriggerNames(SchedulingContext ctxt, string groupName);

		JobDetail GetJobDetail(SchedulingContext ctxt, string jobName, string jobGroup);

		Trigger GetTrigger(SchedulingContext ctxt, string triggerName, string triggerGroup);

		TriggerState GetTriggerState(SchedulingContext ctxt, string triggerName, string triggerGroup);

		void AddCalendar(SchedulingContext ctxt, string calName, ICalendar calendar, bool replace, bool updateTriggers);

		bool DeleteCalendar(SchedulingContext ctxt, string calName);

		ICalendar GetCalendar(SchedulingContext ctxt, string calName);

		string[] GetCalendarNames(SchedulingContext ctxt);

		void AddGlobalJobListener(IJobListener jobListener);

		void AddJobListener(IJobListener jobListener);

		bool RemoveJobListener(string name);

		IJobListener GetJobListener(string name);

		void AddGlobalTriggerListener(ITriggerListener triggerListener);

		void AddTriggerListener(ITriggerListener triggerListener);

		bool RemoveTriggerListener(string name);

		ITriggerListener GetTriggerListener(string name);

		void AddSchedulerListener(ISchedulerListener schedulerListener);

		bool RemoveSchedulerListener(ISchedulerListener schedulerListener);

		bool Interrupt(SchedulingContext ctxt, string jobName, string groupName);
	}
}
