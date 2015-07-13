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
        public Task<bool> IsJobGroupPaused(string groupName)
        {
            return sched.IsJobGroupPaused(groupName);
        }

        /// <summary>
        /// returns true if the given TriggerGroup
        /// is paused
        /// </summary>
        /// <param name="groupName"></param>
        /// <returns></returns>
        public Task<bool> IsTriggerGroupPaused(string groupName)
        {
            return sched.IsTriggerGroupPaused(groupName);
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
        public Task<SchedulerMetaData> GetMetaData()
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
        /// Note: This only reflects whether <see cref="Start"/> has ever
        /// been called on this Scheduler, so it will return <see langword="true" /> even
        /// if the <see cref="IScheduler" /> is currently in standby mode or has been
        /// since shutdown.
        /// </remarks>
        /// <seealso cref="Start"/>
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
        public Task<IReadOnlyList<IJobExecutionContext>> GetCurrentlyExecutingJobs()
        {
            return Task.FromResult(sched.CurrentlyExecutingJobs);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public Task Clear()
        {
            return sched.Clear();
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public Task<ISet<string>> GetPausedTriggerGroups()
        {
            return sched.GetPausedTriggerGroups();
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public IListenerManager ListenerManager => sched.ListenerManager;

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<IReadOnlyList<string>> GetJobGroupNames()
        {
            return sched.GetJobGroupNames();
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<IReadOnlyList<string>> GetTriggerGroupNames()
        {
            return sched.GetTriggerGroupNames();
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
        public virtual Task Start()
        {
            return sched.Start();
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public Task StartDelayed(TimeSpan delay)
        {
            return sched.StartDelayed(delay);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task Standby()
        {
            return sched.Standby();
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task Shutdown()
        {
            return sched.Shutdown();
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task Shutdown(bool waitForJobsToComplete)
        {
            return sched.Shutdown(waitForJobsToComplete);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<DateTimeOffset> ScheduleJob(IJobDetail jobDetail, ITrigger trigger)
        {
            return sched.ScheduleJob(jobDetail, trigger);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<DateTimeOffset> ScheduleJob(ITrigger trigger)
        {
            return sched.ScheduleJob(trigger);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task AddJob(IJobDetail jobDetail, bool replace, bool storeNonDurableWhileAwaitingScheduling)
        {
            return sched.AddJob(jobDetail, replace, storeNonDurableWhileAwaitingScheduling);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task AddJob(IJobDetail jobDetail, bool replace)
        {
            return sched.AddJob(jobDetail, replace);
        }

        public Task<bool> DeleteJobs(IList<JobKey> jobKeys)
        {
            return sched.DeleteJobs(jobKeys);
        }

        public Task ScheduleJobs(IDictionary<IJobDetail, ISet<ITrigger>> triggersAndJobs, bool replace)
        {
            return sched.ScheduleJobs(triggersAndJobs, replace);
        }

        public Task ScheduleJob(IJobDetail jobDetail, ISet<ITrigger> triggersForJob, bool replace)
        {
            return sched.ScheduleJob(jobDetail, triggersForJob, replace);
        }

        public Task<bool> UnscheduleJobs(IList<TriggerKey> triggerKeys)
        {
            return sched.UnscheduleJobs(triggerKeys);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<bool> DeleteJob(JobKey jobKey)
        {
            return sched.DeleteJob(jobKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<bool> UnscheduleJob(TriggerKey triggerKey)
        {
            return sched.UnscheduleJob(triggerKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<DateTimeOffset?> RescheduleJob(TriggerKey triggerKey, ITrigger newTrigger)
        {
            return sched.RescheduleJob(triggerKey, newTrigger);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task TriggerJob(JobKey jobKey)
        {
            return TriggerJob(jobKey, null);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task TriggerJob(JobKey jobKey, JobDataMap data)
        {
            return sched.TriggerJob(jobKey, data);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public Task<bool> CheckExists(JobKey jobKey)
        {
            return sched.CheckExists(jobKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public Task<bool> CheckExists(TriggerKey triggerKey)
        {
            return sched.CheckExists(triggerKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task PauseTrigger(TriggerKey triggerKey)
        {
            return sched.PauseTrigger(triggerKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task PauseTriggers(GroupMatcher<TriggerKey> matcher)
        {
            return sched.PauseTriggers(matcher);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task PauseJob(JobKey jobKey)
        {
            return sched.PauseJob(jobKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task PauseJobs(GroupMatcher<JobKey> matcher)
        {
            return sched.PauseJobs(matcher);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task ResumeTrigger(TriggerKey triggerKey)
        {
            return sched.ResumeTrigger(triggerKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task ResumeTriggers(GroupMatcher<TriggerKey> matcher)
        {
            return sched.ResumeTriggers(matcher);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task ResumeJob(JobKey jobKey)
        {
            return sched.ResumeJob(jobKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task ResumeJobs(GroupMatcher<JobKey> matcher)
        {
            return sched.ResumeJobs(matcher);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task PauseAll()
        {
            return sched.PauseAll();
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task ResumeAll()
        {
            return sched.ResumeAll();
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<IReadOnlyList<ITrigger>> GetTriggersOfJob(JobKey jobKey)
        {
            return sched.GetTriggersOfJob(jobKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<ISet<JobKey>> GetJobKeys(GroupMatcher<JobKey> matcher)
        {
            return sched.GetJobKeys(matcher);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<ISet<TriggerKey>> GetTriggerKeys(GroupMatcher<TriggerKey> matcher)
        {
            return sched.GetTriggerKeys(matcher);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<IJobDetail> GetJobDetail(JobKey jobKey)
        {
            return sched.GetJobDetail(jobKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<ITrigger> GetTrigger(TriggerKey triggerKey)
        {
            return sched.GetTrigger(triggerKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<TriggerState> GetTriggerState(TriggerKey triggerKey)
        {
            return sched.GetTriggerState(triggerKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task AddCalendar(string calName, ICalendar calendar, bool replace, bool updateTriggers)
        {
            return sched.AddCalendar(calName, calendar, replace, updateTriggers);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<bool> DeleteCalendar(string calName)
        {
            return sched.DeleteCalendar(calName);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<ICalendar> GetCalendar(string calName)
        {
            return sched.GetCalendar(calName);
        }

        /// <summary>
        /// Get the names of all registered <see cref="ICalendar"/>.
        /// </summary>
        /// <returns></returns>
        public Task<IReadOnlyList<string>> GetCalendarNames()
        {
            return sched.GetCalendarNames();
        }

        /// <summary>
        /// Request the interruption, within this Scheduler instance, of all
        /// currently executing instances of the identified <see cref="IJob" />.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If more than one instance of the identified job is currently executing,
        /// the cancellation token will be set on each instance.
        /// However, there is a limitation that in the case that
        /// <see cref="Interrupt(JobKey)"/> on one instances throws an exception, all
        /// remaining  instances (that have not yet been interrupted) will not have
        /// their <see cref="Interrupt(JobKey)"/> method called.
        /// </para>
        /// <para>
        /// If you wish to interrupt a specific instance of a job (when more than
        /// one is executing) you can do so by calling
        /// <see cref="GetCurrentlyExecutingJobs"/> to obtain a handle
        /// to the job instance, and then invoke <see cref="Interrupt(JobKey)"/> on it
        /// yourself.
        /// </para>
        /// <para>
        /// This method is not cluster aware.  That is, it will only interrupt
        /// instances of the identified InterruptableJob currently executing in this
        /// Scheduler instance, not across the entire cluster.
        /// </para>
        /// </remarks>
        /// <returns>true is at least one instance of the identified job was found and interrupted.</returns>
        /// <throws>  UnableToInterruptJobException if the job does not implement </throws>
        /// <seealso cref="GetCurrentlyExecutingJobs"/>
        public virtual Task<bool> Interrupt(JobKey jobKey)
        {
            return sched.Interrupt(jobKey);
        }

        public Task<bool> Interrupt(string fireInstanceId)
        {
            return sched.Interrupt(fireInstanceId);
        }
    }
}