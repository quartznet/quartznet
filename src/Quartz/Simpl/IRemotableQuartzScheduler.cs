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
using System.Threading.Tasks;

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

        Task Clear();

        IReadOnlyList<IJobExecutionContext> CurrentlyExecutingJobs { get; }

        /// <summary>
        /// Starts this instance.
        /// </summary>
        Task Start();

        Task StartDelayed(TimeSpan delay);

        /// <summary>
        /// Standbies this instance.
        /// </summary>
        Task Standby();

        /// <summary>
        /// Shutdowns this instance.
        /// </summary>
        Task Shutdown();

        Task Shutdown(bool waitForJobsToComplete);

        DateTimeOffset? RunningSince { get; }

        int NumJobsExecuted { get; }

        bool SupportsPersistence { get; }

        bool Clustered { get; }

        Task<DateTimeOffset> ScheduleJob(IJobDetail jobDetail, ITrigger trigger);

        Task<DateTimeOffset> ScheduleJob(ITrigger trigger);

        Task AddJob(IJobDetail jobDetail, bool replace);

        Task AddJob(IJobDetail jobDetail, bool replace, bool storeNonDurableWhileAwaitingScheduling);

        /// <summary>
        /// returns true if the given JobGroup
        /// is paused
        /// </summary>
        /// <param name="groupName"></param>
        /// <returns></returns>
        Task<bool> IsJobGroupPaused(string groupName);

        /// <summary>
        /// returns true if the given TriggerGroup
        /// is paused
        /// </summary>
        /// <param name="groupName"></param>
        /// <returns></returns>
        Task<bool> IsTriggerGroupPaused(string groupName);

        Task<bool> DeleteJob(JobKey jobKey);

        Task<bool> UnscheduleJob(TriggerKey triggerKey);

        Task<DateTimeOffset?> RescheduleJob(TriggerKey triggerKey, ITrigger newTrigger);

        Task TriggerJob(JobKey jobKey, JobDataMap data);

        Task TriggerJob(IOperableTrigger trig);

        Task PauseTrigger(TriggerKey triggerKey);

        Task PauseTriggers(GroupMatcher<TriggerKey> matcher);

        Task PauseJob(JobKey jobKey);

        Task PauseJobs(GroupMatcher<JobKey> matcher);

        Task ResumeTrigger(TriggerKey triggerKey);

        Task ResumeTriggers(GroupMatcher<TriggerKey> matcher);

        Task<ISet<string>> GetPausedTriggerGroups();

        Task ResumeJob(JobKey jobKey);

        Task ResumeJobs(GroupMatcher<JobKey> matcher);

        Task PauseAll();

        Task ResumeAll();

        Task<IReadOnlyList<string>> GetJobGroupNames();

        Task<ISet<JobKey>> GetJobKeys(GroupMatcher<JobKey> matcher);

        Task<IReadOnlyList<ITrigger>> GetTriggersOfJob(JobKey jobKey);

        Task<IReadOnlyList<string>> GetTriggerGroupNames();

        Task<ISet<TriggerKey>> GetTriggerKeys(GroupMatcher<TriggerKey> matcher);

        Task<IJobDetail> GetJobDetail(JobKey jobKey);

        Task<ITrigger> GetTrigger(TriggerKey triggerKey);

        Task<TriggerState> GetTriggerState(TriggerKey triggerKey);

        Task AddCalendar(string calName, ICalendar calendar, bool replace, bool updateTriggers);

        Task<bool> DeleteCalendar(string calName);

        Task<ICalendar> GetCalendar(string calName);

        Task<IReadOnlyList<string>> GetCalendarNames();

        Task<bool> Interrupt(JobKey jobKey);

        Task<bool> Interrupt(string fireInstanceId);

        Task<bool> CheckExists(JobKey jobKey);

        Task<bool> CheckExists(TriggerKey triggerKey);

        Task<bool> DeleteJobs(IList<JobKey> jobKeys);

        Task ScheduleJobs(IDictionary<IJobDetail, ISet<ITrigger>> triggersAndJobs, bool replace);
        
        Task ScheduleJob(IJobDetail jobDetail, ISet<ITrigger> triggersForJob, bool replace);

        Task<bool> UnscheduleJobs(IList<TriggerKey> triggerKeys);
    }
}