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
		IList<ITriggerListener> GlobalTriggerListeners { get; }
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

        DateTimeOffset? RunningSince { get; }

        int NumJobsExecuted { get; }

		bool SupportsPersistence { get; }

        bool Clustered { get; }

        DateTimeOffset ScheduleJob(JobDetail jobDetail, Trigger trigger);

        DateTimeOffset ScheduleJob(Trigger trigger);

		void AddJob(JobDetail jobDetail, bool replace);

        /// <summary>
        /// returns true if the given JobGroup
        /// is paused
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
        
	    bool DeleteJob(string jobName, string groupName);

		bool UnscheduleJob(string triggerName, string groupName);

        DateTimeOffset? RescheduleJob(string triggerName, string groupName, Trigger newTrigger);

		void TriggerJob(string jobName, string groupName, JobDataMap data);

		void TriggerJobWithVolatileTrigger(string jobName, string groupName, JobDataMap data);

		void PauseTrigger(string triggerName, string groupName);

		void PauseTriggerGroup(string groupName);

		void PauseJob(string jobName, string groupName);

		void PauseJobGroup(string groupName);

		void ResumeTrigger(string triggerName, string groupName);

		void ResumeTriggerGroup(string groupName);

        Collection.ISet<string> GetPausedTriggerGroups();

		void ResumeJob(string jobName, string groupName);

		void ResumeJobGroup(string groupName);

		void PauseAll();

		void ResumeAll();

        IList<string> GetJobGroupNames();

		IList<string> GetJobNames(string groupName);

		IList<Trigger> GetTriggersOfJob(string jobName, string groupName);

		IList<string> GetTriggerGroupNames();

		IList<string> GetTriggerNames(string groupName);

		JobDetail GetJobDetail(string jobName, string jobGroup);

		Trigger GetTrigger(string triggerName, string triggerGroup);

		TriggerState GetTriggerState(string triggerName, string triggerGroup);

		void AddCalendar(string calName, ICalendar calendar, bool replace, bool updateTriggers);

		bool DeleteCalendar(string calName);

		ICalendar GetCalendar(string calName);

		IList<string> GetCalendarNames();

		void AddGlobalJobListener(IJobListener jobListener);

		void AddGlobalTriggerListener(ITriggerListener triggerListener);

		void AddSchedulerListener(ISchedulerListener schedulerListener);

		bool RemoveSchedulerListener(ISchedulerListener schedulerListener);

		bool Interrupt(string jobName, string groupName);
	}
}
