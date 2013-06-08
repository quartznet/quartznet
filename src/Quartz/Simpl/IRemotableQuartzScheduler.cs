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

using Quartz.Impl.Matchers;
using Quartz.Spi;

namespace Quartz.Simpl
{
    /// <summary>
    /// Remote scheduler service interface.
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

        void Clear();

        IList<IJobExecutionContext> CurrentlyExecutingJobs { get; }

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

        DateTimeOffset ScheduleJob(IJobDetail jobDetail, ITrigger trigger);

        DateTimeOffset ScheduleJob(ITrigger trigger);

        void AddJob(IJobDetail jobDetail, bool replace);

        void AddJob(IJobDetail jobDetail, bool replace, bool storeNonDurableWhileAwaitingScheduling);

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

        bool DeleteJob(JobKey jobKey);

        bool UnscheduleJob(TriggerKey triggerKey);

        DateTimeOffset? RescheduleJob(TriggerKey triggerKey, ITrigger newTrigger);

        void TriggerJob(JobKey jobKey, JobDataMap data);

        void TriggerJob(IOperableTrigger trig);

        void PauseTrigger(TriggerKey triggerKey);

        void PauseTriggers(GroupMatcher<TriggerKey> matcher);

        void PauseJob(JobKey jobKey);

        void PauseJobs(GroupMatcher<JobKey> matcher);

        void ResumeTrigger(TriggerKey triggerKey);

        void ResumeTriggers(GroupMatcher<TriggerKey> matcher);

        Collection.ISet<string> GetPausedTriggerGroups();

        void ResumeJob(JobKey jobKey);

        void ResumeJobs(GroupMatcher<JobKey> matcher);

        void PauseAll();

        void ResumeAll();

        IList<string> GetJobGroupNames();

        Collection.ISet<JobKey> GetJobKeys(GroupMatcher<JobKey> matcher);

        IList<ITrigger> GetTriggersOfJob(JobKey jobKey);

        IList<string> GetTriggerGroupNames();

        Collection.ISet<TriggerKey> GetTriggerKeys(GroupMatcher<TriggerKey> matcher);

        IJobDetail GetJobDetail(JobKey jobKey);

        ITrigger GetTrigger(TriggerKey triggerKey);

        TriggerState GetTriggerState(TriggerKey triggerKey);

        void AddCalendar(string calName, ICalendar calendar, bool replace, bool updateTriggers);

        bool DeleteCalendar(string calName);

        ICalendar GetCalendar(string calName);

        IList<string> GetCalendarNames();

        bool Interrupt(JobKey jobKey);

        bool Interrupt(string fireInstanceId);

        bool CheckExists(JobKey jobKey);

        bool CheckExists(TriggerKey triggerKey);

        bool DeleteJobs(IList<JobKey> jobKeys);

        void ScheduleJobs(IDictionary<IJobDetail, Collection.ISet<ITrigger>> triggersAndJobs, bool replace);
        
        void ScheduleJob(IJobDetail jobDetail, Collection.ISet<ITrigger> triggersForJob, bool replace);

        bool UnscheduleJobs(IList<TriggerKey> triggerKeys);
    }
}