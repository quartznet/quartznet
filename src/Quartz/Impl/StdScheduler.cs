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
        private QuartzScheduler sched;

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
        /// Get a <see cref="SchedulerMetaData"/> object describing the settings
        /// and capabilities of the scheduler instance.
        /// <para>
        /// Note that the data returned is an 'instantaneous' snap-shot, and that as
        /// soon as it's returned, the meta data values may be different.
        /// </para>
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
        public IList<IJobExecutionContext> GetCurrentlyExecutingJobs()
        {
            return sched.CurrentlyExecutingJobs;
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public void Clear()
        {
            sched.Clear();
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public Collection.ISet<string> GetPausedTriggerGroups()
        {
            return sched.GetPausedTriggerGroups();
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public IListenerManager ListenerManager
        {
            get { return sched.ListenerManager; }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual IList<string> GetJobGroupNames()
        {
            return sched.GetJobGroupNames();
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual IList<string> GetTriggerGroupNames()
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
        public virtual DateTimeOffset ScheduleJob(IJobDetail jobDetail, ITrigger trigger)
        {
            return sched.ScheduleJob(jobDetail, trigger);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual DateTimeOffset ScheduleJob(ITrigger trigger)
        {
            return sched.ScheduleJob(trigger);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void AddJob(IJobDetail jobDetail, bool replace, bool storeNonDurableWhileAwaitingScheduling)
        {
            sched.AddJob(jobDetail, replace, storeNonDurableWhileAwaitingScheduling);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void AddJob(IJobDetail jobDetail, bool replace)
        {
            sched.AddJob(jobDetail, replace);
        }

        public bool DeleteJobs(IList<JobKey> jobKeys)
        {
            return sched.DeleteJobs(jobKeys);
        }

        public void ScheduleJobs(IDictionary<IJobDetail, Collection.ISet<ITrigger>> triggersAndJobs, bool replace)
        {
            sched.ScheduleJobs(triggersAndJobs, replace);
        }

        public void ScheduleJob(IJobDetail jobDetail, Collection.ISet<ITrigger> triggersForJob, bool replace)
        {
            sched.ScheduleJob(jobDetail, triggersForJob, replace);
        }

        public bool UnscheduleJobs(IList<TriggerKey> triggerKeys)
        {
            return sched.UnscheduleJobs(triggerKeys);
        } 

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual bool DeleteJob(JobKey jobKey)
        {
            return sched.DeleteJob(jobKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual bool UnscheduleJob(TriggerKey triggerKey)
        {
            return sched.UnscheduleJob(triggerKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual DateTimeOffset? RescheduleJob(TriggerKey triggerKey, ITrigger newTrigger)
        {
            return sched.RescheduleJob(triggerKey, newTrigger);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void TriggerJob(JobKey jobKey)
        {
            TriggerJob(jobKey, null);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void TriggerJob(JobKey jobKey, JobDataMap data)
        {
            sched.TriggerJob(jobKey, data);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public bool CheckExists(JobKey jobKey)
        {
            return sched.CheckExists(jobKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public bool CheckExists(TriggerKey triggerKey)
        {
            return sched.CheckExists(triggerKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void PauseTrigger(TriggerKey triggerKey)
        {
            sched.PauseTrigger(triggerKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void PauseTriggers(GroupMatcher<TriggerKey> matcher)
        {
            sched.PauseTriggers(matcher);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void PauseJob(JobKey jobKey)
        {
            sched.PauseJob(jobKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void PauseJobs(GroupMatcher<JobKey> matcher)
        {
            sched.PauseJobs(matcher);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void ResumeTrigger(TriggerKey triggerKey)
        {
            sched.ResumeTrigger(triggerKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void ResumeTriggers(GroupMatcher<TriggerKey> matcher)
        {
            sched.ResumeTriggers(matcher);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void ResumeJob(JobKey jobKey)
        {
            sched.ResumeJob(jobKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void ResumeJobs(GroupMatcher<JobKey> matcher)
        {
            sched.ResumeJobs(matcher);
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
        public virtual IList<ITrigger> GetTriggersOfJob(JobKey jobKey)
        {
            return sched.GetTriggersOfJob(jobKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Collection.ISet<JobKey> GetJobKeys(GroupMatcher<JobKey> matcher)
        {
            return sched.GetJobKeys(matcher);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Collection.ISet<TriggerKey> GetTriggerKeys(GroupMatcher<TriggerKey> matcher)
        {
            return sched.GetTriggerKeys(matcher);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual IJobDetail GetJobDetail(JobKey jobKey)
        {
            return sched.GetJobDetail(jobKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual ITrigger GetTrigger(TriggerKey triggerKey)
        {
            return sched.GetTrigger(triggerKey);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual TriggerState GetTriggerState(TriggerKey triggerKey)
        {
            return sched.GetTriggerState(triggerKey);
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
        /// Request the interruption, within this Scheduler instance, of all
        /// currently executing instances of the identified <see cref="IJob" />, which
        /// must be an implementor of the <see cref="IInterruptableJob"/> interface.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If more than one instance of the identified job is currently executing,
        /// the <see cref="IInterruptableJob.Interrupt"/> method will be called on
        /// each instance.  However, there is a limitation that in the case that
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
        /// <seealso cref="IInterruptableJob"/>
        /// <seealso cref="GetCurrentlyExecutingJobs"/>
        public virtual bool Interrupt(JobKey jobKey)
        {
            return sched.Interrupt(jobKey);
        }

        public bool Interrupt(string fireInstanceId)
        {
            return sched.Interrupt(fireInstanceId);
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (sched != null)
                {
                    sched.Dispose();
                    sched = null;
                }
            }
        }
    }
}