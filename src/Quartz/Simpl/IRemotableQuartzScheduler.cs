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
using System.Threading;
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

        Task Clear(CancellationToken cancellationToken = default(CancellationToken));

        IReadOnlyCollection<IJobExecutionContext> CurrentlyExecutingJobs { get; }

        /// <summary>
        /// Starts this instance.
        /// </summary>
        Task Start(CancellationToken cancellationToken = default(CancellationToken));

        Task StartDelayed(TimeSpan delay, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Standbies this instance.
        /// </summary>
        Task Standby(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Shutdowns this instance.
        /// </summary>
        Task Shutdown(CancellationToken cancellationToken = default(CancellationToken));

        Task Shutdown(bool waitForJobsToComplete, CancellationToken cancellationToken = default(CancellationToken));

        DateTimeOffset? RunningSince { get; }

        int NumJobsExecuted { get; }

        bool SupportsPersistence { get; }

        bool Clustered { get; }

        Task<DateTimeOffset> ScheduleJob(
            IJobDetail jobDetail, 
            ITrigger trigger, 
            CancellationToken cancellationToken = default(CancellationToken));

        Task<DateTimeOffset> ScheduleJob(
            ITrigger trigger, 
            CancellationToken cancellationToken = default(CancellationToken));

        Task AddJob(
            IJobDetail jobDetail, 
            bool replace,
            CancellationToken cancellationToken = default(CancellationToken));

        Task AddJob(
            IJobDetail jobDetail, 
            bool replace, 
            bool storeNonDurableWhileAwaitingScheduling,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// returns true if the given JobGroup
        /// is paused
        /// </summary>
        Task<bool> IsJobGroupPaused(
            string groupName, 
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// returns true if the given TriggerGroup
        /// is paused
        /// </summary>
        Task<bool> IsTriggerGroupPaused(
            string groupName, 
            CancellationToken cancellationToken = default(CancellationToken));

        Task<bool> DeleteJob(JobKey jobKey, CancellationToken cancellationToken = default(CancellationToken));

        Task<bool> UnscheduleJob(
            TriggerKey triggerKey, 
            CancellationToken cancellationToken = default(CancellationToken));

        Task<DateTimeOffset?> RescheduleJob(
            TriggerKey triggerKey, 
            ITrigger newTrigger,
            CancellationToken cancellationToken = default(CancellationToken));

        Task TriggerJob(
            JobKey jobKey,
            JobDataMap data,
            CancellationToken cancellationToken = default(CancellationToken));

        Task TriggerJob(
            IOperableTrigger trig,
            CancellationToken cancellationToken = default(CancellationToken));

        Task PauseTrigger(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default(CancellationToken));

        Task PauseTriggers(
            GroupMatcher<TriggerKey> matcher,
            CancellationToken cancellationToken = default(CancellationToken));

        Task PauseJob(JobKey jobKey, CancellationToken cancellationToken = default(CancellationToken));

        Task PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default(CancellationToken));

        Task ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default(CancellationToken));

        Task ResumeTriggers(
            GroupMatcher<TriggerKey> matcher,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<IReadOnlyCollection<string>> GetPausedTriggerGroups(CancellationToken cancellationToken = default(CancellationToken));

        Task ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default(CancellationToken));

        Task ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default(CancellationToken));

        Task PauseAll(CancellationToken cancellationToken = default(CancellationToken));

        Task ResumeAll(CancellationToken cancellationToken = default(CancellationToken));

        Task<IReadOnlyCollection<string>> GetJobGroupNames(CancellationToken cancellationToken = default(CancellationToken));

        Task<IReadOnlyCollection<JobKey>> GetJobKeys(
            GroupMatcher<JobKey> matcher, 
            CancellationToken cancellationToken = default(CancellationToken));

        Task<IReadOnlyCollection<ITrigger>> GetTriggersOfJob(
            JobKey jobKey,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<IReadOnlyCollection<string>> GetTriggerGroupNames(
            CancellationToken cancellationToken = default(CancellationToken));

        Task<IReadOnlyCollection<TriggerKey>> GetTriggerKeys(
            GroupMatcher<TriggerKey> matcher,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<IJobDetail> GetJobDetail(
            JobKey jobKey,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<ITrigger> GetTrigger(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<TriggerState> GetTriggerState(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default(CancellationToken));

        Task AddCalendar(
            string calName, 
            ICalendar calendar, 
            bool replace, 
            bool updateTriggers,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<bool> DeleteCalendar(string calName, CancellationToken cancellationToken = default(CancellationToken));

        Task<ICalendar> GetCalendar(string calName, CancellationToken cancellationToken = default(CancellationToken));

        Task<IReadOnlyCollection<string>> GetCalendarNames(CancellationToken cancellationToken = default(CancellationToken));

        Task<bool> Interrupt(JobKey jobKey, CancellationToken cancellationToken = default(CancellationToken));

        Task<bool> Interrupt(string fireInstanceId, CancellationToken cancellationToken = default(CancellationToken));

        Task<bool> CheckExists(JobKey jobKey, CancellationToken cancellationToken = default(CancellationToken));

        Task<bool> CheckExists(TriggerKey triggerKey, CancellationToken cancellationToken = default(CancellationToken));

        Task<bool> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default(CancellationToken));

        Task ScheduleJobs(
            IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, 
            bool replace, 
            CancellationToken cancellationToken = default(CancellationToken));
        
        Task ScheduleJob(
            IJobDetail jobDetail, 
            IReadOnlyCollection<ITrigger> triggersForJob, 
            bool replace,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<bool> UnscheduleJobs(
            IReadOnlyCollection<TriggerKey> triggerKeys,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}