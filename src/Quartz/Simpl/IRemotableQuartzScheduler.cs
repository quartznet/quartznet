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

        Task ClearAsync();

        IReadOnlyList<IJobExecutionContext> CurrentlyExecutingJobs { get; }

        /// <summary>
        /// Starts this instance.
        /// </summary>
        Task StartAsync();

        Task StartDelayedAsync(TimeSpan delay);

        /// <summary>
        /// Standbies this instance.
        /// </summary>
        Task StandbyAsync();

        /// <summary>
        /// Shutdowns this instance.
        /// </summary>
        Task ShutdownAsync();

        Task ShutdownAsync(bool waitForJobsToComplete);

        DateTimeOffset? RunningSince { get; }

        int NumJobsExecuted { get; }

        bool SupportsPersistence { get; }

        bool Clustered { get; }

        Task<DateTimeOffset> ScheduleJobAsync(IJobDetail jobDetail, ITrigger trigger);

        Task<DateTimeOffset> ScheduleJobAsync(ITrigger trigger);

        Task AddJobAsync(IJobDetail jobDetail, bool replace);

        Task AddJobAsync(IJobDetail jobDetail, bool replace, bool storeNonDurableWhileAwaitingScheduling);

        /// <summary>
        /// returns true if the given JobGroup
        /// is paused
        /// </summary>
        /// <param name="groupName"></param>
        /// <returns></returns>
        Task<bool> IsJobGroupPausedAsync(string groupName);

        /// <summary>
        /// returns true if the given TriggerGroup
        /// is paused
        /// </summary>
        /// <param name="groupName"></param>
        /// <returns></returns>
        Task<bool> IsTriggerGroupPausedAsync(string groupName);

        Task<bool> DeleteJobAsync(JobKey jobKey);

        Task<bool> UnscheduleJobAsync(TriggerKey triggerKey);

        Task<DateTimeOffset?> RescheduleJobAsync(TriggerKey triggerKey, ITrigger newTrigger);

        Task TriggerJobAsync(JobKey jobKey, JobDataMap data);

        Task TriggerJobAsync(IOperableTrigger trig);

        Task PauseTriggerAsync(TriggerKey triggerKey);

        Task PauseTriggersAsync(GroupMatcher<TriggerKey> matcher);

        Task PauseJobAsync(JobKey jobKey);

        Task PauseJobsAsync(GroupMatcher<JobKey> matcher);

        Task ResumeTriggerAsync(TriggerKey triggerKey);

        Task ResumeTriggersAsync(GroupMatcher<TriggerKey> matcher);

        Task<ISet<string>> GetPausedTriggerGroupsAsync();

        Task ResumeJobAsync(JobKey jobKey);

        Task ResumeJobsAsync(GroupMatcher<JobKey> matcher);

        Task PauseAllAsync();

        Task ResumeAllAsync();

        Task<IReadOnlyList<string>> GetJobGroupNamesAsync();

        Task<ISet<JobKey>> GetJobKeysAsync(GroupMatcher<JobKey> matcher);

        Task<IReadOnlyList<ITrigger>> GetTriggersOfJobAsync(JobKey jobKey);

        Task<IReadOnlyList<string>> GetTriggerGroupNamesAsync();

        Task<ISet<TriggerKey>> GetTriggerKeysAsync(GroupMatcher<TriggerKey> matcher);

        Task<IJobDetail> GetJobDetailAsync(JobKey jobKey);

        Task<ITrigger> GetTriggerAsync(TriggerKey triggerKey);

        Task<TriggerState> GetTriggerStateAsync(TriggerKey triggerKey);

        Task AddCalendarAsync(string calName, ICalendar calendar, bool replace, bool updateTriggers);

        Task<bool> DeleteCalendarAsync(string calName);

        Task<ICalendar> GetCalendarAsync(string calName);

        Task<IReadOnlyList<string>> GetCalendarNamesAsync();

        Task<bool> InterruptAsync(JobKey jobKey);

        Task<bool> InterruptAsync(string fireInstanceId);

        Task<bool> CheckExistsAsync(JobKey jobKey);

        Task<bool> CheckExistsAsync(TriggerKey triggerKey);

        Task<bool> DeleteJobsAsync(IList<JobKey> jobKeys);

        Task ScheduleJobsAsync(IDictionary<IJobDetail, ISet<ITrigger>> triggersAndJobs, bool replace);
        
        Task ScheduleJobAsync(IJobDetail jobDetail, ISet<ITrigger> triggersForJob, bool replace);

        Task<bool> UnscheduleJobsAsync(IList<TriggerKey> triggerKeys);
    }
}