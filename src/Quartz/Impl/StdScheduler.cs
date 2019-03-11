#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
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
        public Task<bool> IsJobGroupPaused(
            string groupName,
            CancellationToken cancellationToken = default)
        {
            return sched.IsJobGroupPaused(groupName, cancellationToken);
        }

        /// <summary>
        /// returns true if the given TriggerGroup
        /// is paused
        /// </summary>
        public Task<bool> IsTriggerGroupPaused(
            string groupName,
            CancellationToken cancellationToken = default)
        {
            return sched.IsTriggerGroupPaused(groupName, cancellationToken);
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
        public Task<SchedulerMetaData> GetMetaData(CancellationToken cancellationToken = default)
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
        public Task<IReadOnlyCollection<IJobExecutionContext>> GetCurrentlyExecutingJobs(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(sched.CurrentlyExecutingJobs);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public Task Clear(
            CancellationToken cancellationToken = default)
        {
            return sched.Clear(cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public Task<IReadOnlyCollection<string>> GetPausedTriggerGroups(
            CancellationToken cancellationToken = default)
        {
            return sched.GetPausedTriggerGroups(cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public IListenerManager ListenerManager => sched.ListenerManager;

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<IReadOnlyCollection<string>> GetJobGroupNames(
            CancellationToken cancellationToken = default)
        {
            return sched.GetJobGroupNames(cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<IReadOnlyCollection<string>> GetTriggerGroupNames(
            CancellationToken cancellationToken = default)
        {
            return sched.GetTriggerGroupNames(cancellationToken);
        }

        /// <seealso cref="IScheduler.JobFactory">
        /// </seealso>
        public virtual IJobFactory JobFactory
        {
            set => sched.JobFactory = value;
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task Start(CancellationToken cancellationToken = default)
        {
            return sched.Start(cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public Task StartDelayed(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            return sched.StartDelayed(delay, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task Standby(CancellationToken cancellationToken = default)
        {
            return sched.Standby(cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task Shutdown(CancellationToken cancellationToken = default)
        {
            return sched.Shutdown(cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task Shutdown(
            bool waitForJobsToComplete,
            CancellationToken cancellationToken = default)
        {
            return sched.Shutdown(waitForJobsToComplete, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<DateTimeOffset> ScheduleJob(
            IJobDetail jobDetail,
            ITrigger trigger,
            CancellationToken cancellationToken = default)
        {
            return sched.ScheduleJob(jobDetail, trigger, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<DateTimeOffset> ScheduleJob(
            ITrigger trigger,
            CancellationToken cancellationToken = default)
        {
            return sched.ScheduleJob(trigger, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task AddJob(
            IJobDetail jobDetail,
            bool replace,
            bool storeNonDurableWhileAwaitingScheduling,
            CancellationToken cancellationToken = default)
        {
            return sched.AddJob(jobDetail, replace, storeNonDurableWhileAwaitingScheduling, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task AddJob(
            IJobDetail jobDetail,
            bool replace,
            CancellationToken cancellationToken = default)
        {
            return sched.AddJob(jobDetail, replace, cancellationToken);
        }

        public Task<bool> DeleteJobs(
            IReadOnlyCollection<JobKey> jobKeys,
            CancellationToken cancellationToken = default)
        {
            return sched.DeleteJobs(jobKeys, cancellationToken);
        }

        public Task ScheduleJobs(
            IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs,
            bool replace,
            CancellationToken cancellationToken = default)
        {
            return sched.ScheduleJobs(triggersAndJobs, replace, cancellationToken);
        }

        public Task ScheduleJob(
            IJobDetail jobDetail,
            IReadOnlyCollection<ITrigger> triggersForJob,
            bool replace,
            CancellationToken cancellationToken = default)
        {
            return sched.ScheduleJob(jobDetail, triggersForJob, replace, cancellationToken);
        }

        public Task<bool> UnscheduleJobs(
            IReadOnlyCollection<TriggerKey> triggerKeys,
            CancellationToken cancellationToken = default)
        {
            return sched.UnscheduleJobs(triggerKeys, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<bool> DeleteJob(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            return sched.DeleteJob(jobKey, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<bool> UnscheduleJob(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            return sched.UnscheduleJob(triggerKey, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<DateTimeOffset?> RescheduleJob(
            TriggerKey triggerKey,
            ITrigger newTrigger,
            CancellationToken cancellationToken = default)
        {
            return sched.RescheduleJob(triggerKey, newTrigger, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task TriggerJob(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            return TriggerJob(jobKey, null, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task TriggerJob(
            JobKey jobKey,
            JobDataMap data,
            CancellationToken cancellationToken = default)
        {
            return sched.TriggerJob(jobKey, data, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public Task<bool> CheckExists(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            return sched.CheckExists(jobKey, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public Task<bool> CheckExists(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            return sched.CheckExists(triggerKey, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task PauseTrigger(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            return sched.PauseTrigger(triggerKey, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task PauseTriggers(
            GroupMatcher<TriggerKey> matcher,
            CancellationToken cancellationToken = default)
        {
            return sched.PauseTriggers(matcher, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task PauseJob(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            return sched.PauseJob(jobKey, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task PauseJobs(
            GroupMatcher<JobKey> matcher,
            CancellationToken cancellationToken = default)
        {
            return sched.PauseJobs(matcher, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task ResumeTrigger(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            return sched.ResumeTrigger(triggerKey, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task ResumeTriggers(
            GroupMatcher<TriggerKey> matcher,
            CancellationToken cancellationToken = default)
        {
            return sched.ResumeTriggers(matcher, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task ResumeJob(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            return sched.ResumeJob(jobKey, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task ResumeJobs(
            GroupMatcher<JobKey> matcher,
            CancellationToken cancellationToken = default)
        {
            return sched.ResumeJobs(matcher, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task PauseAll(CancellationToken cancellationToken = default)
        {
            return sched.PauseAll(cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task ResumeAll(CancellationToken cancellationToken = default)
        {
            return sched.ResumeAll(cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<IReadOnlyCollection<ITrigger>> GetTriggersOfJob(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            return sched.GetTriggersOfJob(jobKey, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<IReadOnlyCollection<JobKey>> GetJobKeys(
            GroupMatcher<JobKey> matcher,
            CancellationToken cancellationToken = default)
        {
            return sched.GetJobKeys(matcher, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<IReadOnlyCollection<TriggerKey>> GetTriggerKeys(
            GroupMatcher<TriggerKey> matcher,
            CancellationToken cancellationToken = default)
        {
            return sched.GetTriggerKeys(matcher, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<IJobDetail> GetJobDetail(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            return sched.GetJobDetail(jobKey, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<ITrigger> GetTrigger(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            return sched.GetTrigger(triggerKey, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<TriggerState> GetTriggerState(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            return sched.GetTriggerState(triggerKey, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task AddCalendar(
            string calName,
            ICalendar calendar,
            bool replace,
            bool updateTriggers,
            CancellationToken cancellationToken = default)
        {
            return sched.AddCalendar(calName, calendar, replace, updateTriggers, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<bool> DeleteCalendar(
            string calName,
            CancellationToken cancellationToken = default)
        {
            return sched.DeleteCalendar(calName, cancellationToken);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<ICalendar> GetCalendar(
            string calName,
            CancellationToken cancellationToken = default)
        {
            return sched.GetCalendar(calName, cancellationToken);
        }

        /// <summary>
        /// Get the names of all registered <see cref="ICalendar"/>.
        /// </summary>
        /// <returns></returns>
        public Task<IReadOnlyCollection<string>> GetCalendarNames(
            CancellationToken cancellationToken = default)
        {
            return sched.GetCalendarNames(cancellationToken);
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
        /// <see cref="Interrupt(JobKey, CancellationToken)"/> on one instances throws an exception, all
        /// remaining  instances (that have not yet been interrupted) will not have
        /// their <see cref="Interrupt(JobKey, CancellationToken)"/> method called.
        /// </para>
        /// <para>
        /// If you wish to interrupt a specific instance of a job (when more than
        /// one is executing) you can do so by calling
        /// <see cref="GetCurrentlyExecutingJobs"/> to obtain a handle
        /// to the job instance, and then invoke <see cref="Interrupt(JobKey, CancellationToken)"/> on it
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
        public virtual Task<bool> Interrupt(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            return sched.Interrupt(jobKey, cancellationToken);
        }

        public Task<bool> Interrupt(
            string fireInstanceId,
            CancellationToken cancellationToken = default)
        {
            return sched.Interrupt(fireInstanceId, cancellationToken);
        }
    }
}