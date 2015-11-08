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

using Quartz.Core;
using Quartz.Impl.Matchers;
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
        /// Construct a <see cref="StdScheduler" /> instance to proxy the given
        /// <see cref="QuartzScheduler" /> instance.
        /// </summary>
        public StdScheduler(QuartzScheduler sched)
        {
            this.sched = sched;
        }

        /// <summary>
        /// returns true if the given JobGroup
        /// is paused
        /// </summary>
        /// <param name="groupName"></param>
        /// <returns></returns>
        public Task<bool> IsJobGroupPausedAsync(string groupName)
        {
            return sched.IsJobGroupPausedAsync(groupName);
        }

        /// <summary>
        /// returns true if the given TriggerGroup
        /// is paused
        /// </summary>
        /// <param name="groupName"></param>
        /// <returns></returns>
        public Task<bool> IsTriggerGroupPausedAsync(string groupName)
        {
            return sched.IsTriggerGroupPausedAsync(groupName);
        }

        /// <summary>
        /// Returns the name of the <see cref="IScheduler" />.
        /// </summary>
        public virtual string SchedulerName => sched.SchedulerName;

        /// <summary>
        /// Returns the instance Id of the <see cref="IScheduler" />.
        /// </summary>
        public virtual string SchedulerInstanceId => sched.SchedulerInstanceId;

        /// <summary>
        /// Get a <see cref="SchedulerMetaData"/> object describing the settings
        /// and capabilities of the scheduler instance.
        /// <para>
        /// Note that the data returned is an 'instantaneous' snap-shot, and that as
        /// soon as it's returned, the meta data values may be different.
        /// </para>
        /// </summary>
        /// <returns></returns>
        public Task<SchedulerMetaData> GetMetaDataAsync()
        {
            return Task.FromResult(new SchedulerMetaData(
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
                sched.Version));
        }

        /// <summary>
        /// Returns the <see cref="SchedulerContext" /> of the <see cref="IScheduler" />.
        /// </summary>
        public virtual SchedulerContext Context => sched.SchedulerContext;

        /// <summary>
        /// Whether the scheduler has been started.
        /// </summary>
        /// <value></value>
        /// <remarks>
        /// Note: This only reflects whether <see cref="StartAsync"/> has ever
        /// been called on this Scheduler, so it will return <see langword="true" /> even
        /// if the <see cref="IScheduler" /> is currently in standby mode or has been
        /// since shutdown.
        /// </remarks>
        /// <seealso cref="StartAsync"/>
        /// <seealso cref="IsShutdown"/>
        /// <seealso cref="InStandbyMode"/>
        public bool IsStarted => sched.RunningSince.HasValue;

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual bool InStandbyMode => sched.InStandbyMode;

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual bool IsShutdown => sched.IsShutdown;

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public Task<IReadOnlyList<IJobExecutionContext>> GetCurrentlyExecutingJobsAsync()
        {
            return Task.FromResult(sched.CurrentlyExecutingJobs);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public Task ClearAsync()
        {
            return sched.ClearAsync();
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public Task<ISet<string>> GetPausedTriggerGroupsAsync()
        {
            return sched.GetPausedTriggerGroupsAsync();
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public IListenerManager ListenerManager => sched.ListenerManager;

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<IReadOnlyList<string>> GetJobGroupNamesAsync()
        {
            return sched.GetJobGroupNamesAsync();
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<IReadOnlyList<string>> GetTriggerGroupNamesAsync()
        {
            return sched.GetTriggerGroupNamesAsync();
        }

        /// <seealso cref="IScheduler.JobFactory">
        /// </seealso>
        public virtual IJobFactory JobFactory
        {
            set { sched.JobFactory = value; }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task StartAsync()
        {
            return sched.StartAsync();
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public Task StartDelayedAsync(TimeSpan delay)
        {
            return sched.StartDelayedAsync(delay);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task StandbyAsync()
        {
            return sched.StandbyAsync();
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task ShutdownAsync()
        {
            return sched.ShutdownAsync();
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task ShutdownAsync(bool waitForJobsToComplete)
        {
            return sched.ShutdownAsync(waitForJobsToComplete);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<DateTimeOffset> ScheduleJobAsync(IJobDetail jobDetail, ITrigger trigger)
        {
            return sched.ScheduleJobAsync(jobDetail, trigger);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<DateTimeOffset> ScheduleJobAsync(ITrigger trigger)
        {
            return sched.ScheduleJobAsync(trigger);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task AddJobAsync(IJobDetail jobDetail, bool replace, bool storeNonDurableWhileAwaitingScheduling)
        {
            return sched.AddJobAsync(jobDetail, replace, storeNonDurableWhileAwaitingScheduling);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task AddJobAsync(IJobDetail jobDetail, bool replace)
        {
            return sched.AddJobAsync(jobDetail, replace);
        }

        public Task<bool> DeleteJobsAsync(IList<JobKey> jobKeys)
        {
            return sched.DeleteJobsAsync(jobKeys);
        }

        public Task ScheduleJobsAsync(IDictionary<IJobDetail, ISet<ITrigger>> triggersAndJobs, bool replace)
        {
            return sched.ScheduleJobsAsync(triggersAndJobs, replace);
        }

        public Task ScheduleJobAsync(IJobDetail jobDetail, ISet<ITrigger> triggersForJob, bool replace)
        {
            return sched.ScheduleJobAsync(jobDetail, triggersForJob, replace);
        }

        public Task<bool> UnscheduleJobsAsync(IList<TriggerKey> triggerKeys)
        {
            return sched.UnscheduleJobsAsync(triggerKeys);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<bool> DeleteJobAsync(JobKey jobKey)
        {
            return sched.DeleteJobAsync(jobKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<bool> UnscheduleJobAsync(TriggerKey triggerKey)
        {
            return sched.UnscheduleJobAsync(triggerKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<DateTimeOffset?> RescheduleJobAsync(TriggerKey triggerKey, ITrigger newTrigger)
        {
            return sched.RescheduleJobAsync(triggerKey, newTrigger);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task TriggerJobAsync(JobKey jobKey)
        {
            return TriggerJobAsync(jobKey, null);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task TriggerJobAsync(JobKey jobKey, JobDataMap data)
        {
            return sched.TriggerJobAsync(jobKey, data);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public Task<bool> CheckExistsAsync(JobKey jobKey)
        {
            return sched.CheckExistsAsync(jobKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public Task<bool> CheckExistsAsync(TriggerKey triggerKey)
        {
            return sched.CheckExistsAsync(triggerKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task PauseTriggerAsync(TriggerKey triggerKey)
        {
            return sched.PauseTriggerAsync(triggerKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task PauseTriggersAsync(GroupMatcher<TriggerKey> matcher)
        {
            return sched.PauseTriggersAsync(matcher);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task PauseJobAsync(JobKey jobKey)
        {
            return sched.PauseJobAsync(jobKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task PauseJobsAsync(GroupMatcher<JobKey> matcher)
        {
            return sched.PauseJobsAsync(matcher);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task ResumeTriggerAsync(TriggerKey triggerKey)
        {
            return sched.ResumeTriggerAsync(triggerKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task ResumeTriggersAsync(GroupMatcher<TriggerKey> matcher)
        {
            return sched.ResumeTriggersAsync(matcher);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task ResumeJobAsync(JobKey jobKey)
        {
            return sched.ResumeJobAsync(jobKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task ResumeJobsAsync(GroupMatcher<JobKey> matcher)
        {
            return sched.ResumeJobsAsync(matcher);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task PauseAllAsync()
        {
            return sched.PauseAllAsync();
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task ResumeAllAsync()
        {
            return sched.ResumeAllAsync();
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<IReadOnlyList<ITrigger>> GetTriggersOfJobAsync(JobKey jobKey)
        {
            return sched.GetTriggersOfJobAsync(jobKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<ISet<JobKey>> GetJobKeysAsync(GroupMatcher<JobKey> matcher)
        {
            return sched.GetJobKeysAsync(matcher);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<ISet<TriggerKey>> GetTriggerKeysAsync(GroupMatcher<TriggerKey> matcher)
        {
            return sched.GetTriggerKeysAsync(matcher);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<IJobDetail> GetJobDetailAsync(JobKey jobKey)
        {
            return sched.GetJobDetailAsync(jobKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<ITrigger> GetTriggerAsync(TriggerKey triggerKey)
        {
            return sched.GetTriggerAsync(triggerKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<TriggerState> GetTriggerStateAsync(TriggerKey triggerKey)
        {
            return sched.GetTriggerStateAsync(triggerKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task AddCalendarAsync(string calName, ICalendar calendar, bool replace, bool updateTriggers)
        {
            return sched.AddCalendarAsync(calName, calendar, replace, updateTriggers);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<bool> DeleteCalendarAsync(string calName)
        {
            return sched.DeleteCalendarAsync(calName);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<ICalendar> GetCalendarAsync(string calName)
        {
            return sched.GetCalendarAsync(calName);
        }

        /// <summary>
        /// Get the names of all registered <see cref="ICalendar"/>.
        /// </summary>
        /// <returns></returns>
        public Task<IReadOnlyList<string>> GetCalendarNamesAsync()
        {
            return sched.GetCalendarNamesAsync();
        }

        /// <summary>
        /// Request the interruption, within this Scheduler instance, of all
        /// currently executing instances of the identified <see cref="IJob" />.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If more than one instance of the identified job is currently executing,
        /// the cancellation token will be set on each instance.  However, there is a limitation that in the case that  
        /// cancellation on one instances throws an exception, all 
        /// remaining  instances (that have not yet been interrupted) will not have 
        /// their cancellation called.
        /// </para>
        /// <para>
        /// If you wish to interrupt a specific instance of a job (when more than
        /// one is executing) you can do so by calling 
        /// <see cref="GetCurrentlyExecutingJobsAsync" /> to obtain a handle 
        /// to the job instance, and then invoke job cancellation token's cancellation.
        /// </para>
        /// <para>
        /// This method is not cluster aware.  That is, it will only interrupt 
        /// instances of the identified InterruptableJob currently executing in this 
        /// Scheduler instance, not across the entire cluster.
        /// </para>
        /// </remarks>
        /// <returns>true is at least one instance of the identified job was found and interrupted.</returns>
        /// <throws>  UnableToInterruptJobException if the job does not implement </throws>
        /// <seealso cref="GetCurrentlyExecutingJobsAsync"/>
        public virtual Task<bool> InterruptAsync(JobKey jobKey)
        {
            return sched.InterruptAsync(jobKey);
        }

        public Task<bool> InterruptAsync(string fireInstanceId)
        {
            return sched.InterruptAsync(fireInstanceId);
        }
    }
}