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
using System.Runtime.Remoting;
using System.Threading.Tasks;

using Quartz.Core;
using Quartz.Impl.Matchers;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Impl
{
    /// <summary>
    /// An implementation of the <see cref="IScheduler" /> interface that remotely
    /// proxies all method calls to the equivalent call on a given <see cref="QuartzScheduler" />
    /// instance, via remoting or similar technology.
    /// </summary>
    /// <seealso cref="IScheduler" />
    /// <seealso cref="QuartzScheduler" />
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public class RemoteScheduler : IScheduler
    {
        private IRemotableQuartzScheduler rsched;
        private readonly string schedId;
        private readonly IRemotableSchedulerProxyFactory proxyFactory;

        /// <summary>
        /// Construct a <see cref="RemoteScheduler" /> instance to proxy the given
        /// RemoteableQuartzScheduler instance.
        /// </summary>
        public RemoteScheduler(string schedId, IRemotableSchedulerProxyFactory proxyFactory)
        {
            this.schedId = schedId;
            this.proxyFactory = proxyFactory;
        }

        /// <summary>
        /// returns true if the given JobGroup
        /// is paused
        /// </summary>
        /// <param name="groupName"></param>
        /// <returns></returns>
        public virtual Task<bool> IsJobGroupPausedAsync(string groupName)
        {
            return CallInGuardAsync(x => x.IsJobGroupPausedAsync(groupName));
        }

        /// <summary>
        /// returns true if the given TriggerGroup
        /// is paused
        /// </summary>
        /// <param name="groupName"></param>
        /// <returns></returns>
        public virtual Task<bool> IsTriggerGroupPausedAsync(string groupName)
        {
            return CallInGuardAsync(x => x.IsTriggerGroupPausedAsync(groupName));
        }

        /// <summary>
        /// Returns the name of the <see cref="IScheduler" />.
        /// </summary>
        public virtual string SchedulerName
        {
            get { return ReadPropertyInGuard(x => x.SchedulerName); }
        }

        /// <summary>
        /// Returns the instance Id of the <see cref="IScheduler" />.
        /// </summary>
        public virtual string SchedulerInstanceId
        {
            get { return ReadPropertyInGuard(x => x.SchedulerInstanceId); }
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
        public virtual Task<SchedulerMetaData> GetMetaDataAsync()
        {
            return CallInGuardAsync(x => Task.FromResult(new SchedulerMetaData(SchedulerName, SchedulerInstanceId, GetType(), true, IsStarted, InStandbyMode,
                                                          IsShutdown, x.RunningSince, x.NumJobsExecuted, x.JobStoreClass,
                                                          x.SupportsPersistence, x.Clustered, x.ThreadPoolClass, x.ThreadPoolSize, x.Version)));
        }

        /// <summary> 
        /// Returns the <see cref="SchedulerContext" /> of the <see cref="IScheduler" />.
        /// </summary>
        public virtual SchedulerContext Context
        {
            get { return ReadPropertyInGuard(x => x.SchedulerContext); }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual bool InStandbyMode
        {
            get { return ReadPropertyInGuard(x => x.InStandbyMode); }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual bool IsShutdown
        {
            get { return ReadPropertyInGuard(x => x.IsShutdown); }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<IReadOnlyList<IJobExecutionContext>> GetCurrentlyExecutingJobsAsync()
        {
            return Task.FromResult(ReadPropertyInGuard(x => x.CurrentlyExecutingJobs));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<IReadOnlyList<string>> GetJobGroupNamesAsync()
        {
            return CallInGuardAsync(x => x.GetJobGroupNamesAsync());
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<IReadOnlyList<string>> GetTriggerGroupNamesAsync()
        {
            return CallInGuardAsync(x => x.GetTriggerGroupNamesAsync());
        }

        /// <summary>
        /// Get the names of all <see cref="ITrigger" /> groups that are paused.
        /// </summary>
        /// <value></value>
        public virtual Task<ISet<string>> GetPausedTriggerGroupsAsync()
        {
            return CallInGuardAsync(x => x.GetPausedTriggerGroupsAsync());
        }

        /// <summary>
        /// Set the <see cref="JobFactory" /> that will be responsible for producing
        /// instances of <see cref="IJob" /> classes.
        /// <para>
        /// JobFactories may be of use to those wishing to have their application
        /// produce <see cref="IJob" /> instances via some special mechanism, such as to
        /// give the opportunity for dependency injection.
        /// </para>
        /// </summary>
        /// <value></value>
        /// <seealso cref="IJobFactory"/>
        /// <throws>  SchedulerException </throws>
        public virtual IJobFactory JobFactory
        {
            set { throw new SchedulerException("Operation not supported for remote schedulers."); }
        }

        /// <summary> 
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task StartAsync()
        {
            return CallInGuardAsync(x => x.StartAsync());
        }

        /// <summary> 
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public Task StartDelayedAsync(TimeSpan delay)
        {
            return CallInGuardAsync(x => x.StartDelayedAsync(delay));
        }

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
        public virtual bool IsStarted
        {
            get { return ReadPropertyInGuard(x => x.RunningSince.HasValue); }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task StandbyAsync()
        {
            return CallInGuardAsync(x => x.StandbyAsync());
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual async Task ShutdownAsync()
        {
            try
            {
                string schedulerName = SchedulerName;
                await GetRemoteScheduler().ShutdownAsync().ConfigureAwait(false);
                SchedulerRepository.Instance.Remove(schedulerName);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task ShutdownAsync(bool waitForJobsToComplete)
        {
            return CallInGuardAsync(x => x.ShutdownAsync(waitForJobsToComplete));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<DateTimeOffset> ScheduleJobAsync(IJobDetail jobDetail, ITrigger trigger)
        {
            return CallInGuardAsync(x => x.ScheduleJobAsync(jobDetail, trigger));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<DateTimeOffset> ScheduleJobAsync(ITrigger trigger)
        {
            return CallInGuardAsync(x => x.ScheduleJobAsync(trigger));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task AddJobAsync(IJobDetail jobDetail, bool replace)
        {
            return CallInGuardAsync(x => x.AddJobAsync(jobDetail, replace));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task AddJobAsync(IJobDetail jobDetail, bool replace, bool storeNonDurableWhileAwaitingScheduling)
        {
            return CallInGuardAsync(x => x.AddJobAsync(jobDetail, replace, storeNonDurableWhileAwaitingScheduling));
        }

        public virtual Task<bool> DeleteJobsAsync(IList<JobKey> jobKeys)
        {
            return CallInGuardAsync(x => x.DeleteJobsAsync(jobKeys));
        }

        public virtual Task ScheduleJobsAsync(IDictionary<IJobDetail, ISet<ITrigger>> triggersAndJobs, bool replace)
        {
            return CallInGuardAsync(x => x.ScheduleJobsAsync(triggersAndJobs, replace));
        }

        public Task ScheduleJobAsync(IJobDetail jobDetail, ISet<ITrigger> triggersForJob, bool replace)
        {
            return CallInGuardAsync(x => x.ScheduleJobAsync(jobDetail, triggersForJob, replace));
        }

        public virtual Task<bool> UnscheduleJobsAsync(IList<TriggerKey> triggerKeys)
        {
            return CallInGuardAsync(x => x.UnscheduleJobsAsync(triggerKeys));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<bool> DeleteJobAsync(JobKey jobKey)
        {
            return CallInGuardAsync(x => x.DeleteJobAsync(jobKey));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<bool> UnscheduleJobAsync(TriggerKey triggerKey)
        {
            return CallInGuardAsync(x => x.UnscheduleJobAsync(triggerKey));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<DateTimeOffset?> RescheduleJobAsync(TriggerKey triggerKey, ITrigger newTrigger)
        {
            return CallInGuardAsync(x => x.RescheduleJobAsync(triggerKey, newTrigger));
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
            return CallInGuardAsync(x => x.TriggerJobAsync(jobKey, data));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task PauseTriggerAsync(TriggerKey triggerKey)
        {
            return CallInGuardAsync(x => x.PauseTriggerAsync(triggerKey));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task PauseTriggersAsync(GroupMatcher<TriggerKey> matcher)
        {
            return CallInGuardAsync(x => x.PauseTriggersAsync(matcher));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task PauseJobAsync(JobKey jobKey)
        {
            return CallInGuardAsync(x => x.PauseJobAsync(jobKey));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task PauseJobsAsync(GroupMatcher<JobKey> matcher)
        {
            return CallInGuardAsync(x => x.PauseJobsAsync(matcher));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task ResumeTriggerAsync(TriggerKey triggerKey)
        {
            return CallInGuardAsync(x => x.ResumeTriggerAsync(triggerKey));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task ResumeTriggersAsync(GroupMatcher<TriggerKey> matcher)
        {
            return CallInGuardAsync(x => x.ResumeTriggersAsync(matcher));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task ResumeJobAsync(JobKey jobKey)
        {
            return CallInGuardAsync(x => x.ResumeJobAsync(jobKey));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task ResumeJobsAsync(GroupMatcher<JobKey> matcher)
        {
            return CallInGuardAsync(x => x.ResumeJobsAsync(matcher));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task PauseAllAsync()
        {
            return CallInGuardAsync(x => x.PauseAllAsync());
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task ResumeAllAsync()
        {
            return CallInGuardAsync(x => x.ResumeAllAsync());
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<ISet<JobKey>> GetJobKeysAsync(GroupMatcher<JobKey> matcher)
        {
            return CallInGuardAsync(x => x.GetJobKeysAsync(matcher));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<IReadOnlyList<ITrigger>> GetTriggersOfJobAsync(JobKey jobKey)
        {
            return CallInGuardAsync(x => x.GetTriggersOfJobAsync(jobKey));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<ISet<TriggerKey>> GetTriggerKeysAsync(GroupMatcher<TriggerKey> matcher)
        {
            return CallInGuardAsync(x => x.GetTriggerKeysAsync(matcher));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<IJobDetail> GetJobDetailAsync(JobKey jobKey)
        {
            return CallInGuardAsync(x => x.GetJobDetailAsync(jobKey));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<bool> CheckExistsAsync(JobKey jobKey)
        {
            return CallInGuardAsync(x => x.CheckExistsAsync(jobKey));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<bool> CheckExistsAsync(TriggerKey triggerKey)
        {
            return CallInGuardAsync(x => x.CheckExistsAsync(triggerKey));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task ClearAsync()
        {
            return CallInGuardAsync(x => x.ClearAsync());
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<ITrigger> GetTriggerAsync(TriggerKey triggerKey)
        {
            return CallInGuardAsync(x => x.GetTriggerAsync(triggerKey));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<TriggerState> GetTriggerStateAsync(TriggerKey triggerKey)
        {
            return CallInGuardAsync(x => x.GetTriggerStateAsync(triggerKey));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task AddCalendarAsync(string calName, ICalendar calendar, bool replace, bool updateTriggers)
        {
            return CallInGuardAsync(x => x.AddCalendarAsync(calName, calendar, replace, updateTriggers));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<bool> DeleteCalendarAsync(string calName)
        {
            return CallInGuardAsync(x => x.DeleteCalendarAsync(calName));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Task<ICalendar> GetCalendarAsync(string calName)
        {
            return CallInGuardAsync(x => x.GetCalendarAsync(calName));
        }

        /// <summary>
        /// Get the names of all registered <see cref="ICalendar"/>.
        /// </summary>
        /// <returns></returns>
        public Task<IReadOnlyList<string>> GetCalendarNamesAsync()
        {
            return CallInGuardAsync(x => x.GetCalendarNamesAsync());
        }

        public IListenerManager ListenerManager
        {
            get { throw new SchedulerException("Operation not supported for remote schedulers."); }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual async Task<bool> InterruptAsync(JobKey jobKey)
        {
            try
            {
                return await GetRemoteScheduler().InterruptAsync(jobKey).ConfigureAwait(false);
            }
            catch (RemotingException re)
            {
                throw new UnableToInterruptJobException(InvalidateHandleCreateException("Error communicating with remote scheduler.", re));
            }
            catch (SchedulerException se)
            {
                throw new UnableToInterruptJobException(se);
            }
        }

        public async Task<bool> InterruptAsync(string fireInstanceId)
        {
            try
            {
                return await GetRemoteScheduler().InterruptAsync(fireInstanceId).ConfigureAwait(false);
            }
            catch (RemotingException re)
            {
                throw new UnableToInterruptJobException(InvalidateHandleCreateException("Error communicating with remote scheduler.", re));
            }
            catch (SchedulerException se)
            {
                throw new UnableToInterruptJobException(se);
            }
        }

        protected virtual async Task CallInGuardAsync(Func<IRemotableQuartzScheduler, Task> action)
        {
            try
            {
                await action(GetRemoteScheduler()).ConfigureAwait(false);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        protected virtual async Task<T> CallInGuardAsync<T>(Func<IRemotableQuartzScheduler, Task<T>> func)
        {
            try
            {
                return await func(GetRemoteScheduler()).ConfigureAwait(false);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        protected virtual T ReadPropertyInGuard<T>(Func<IRemotableQuartzScheduler, T> action)
        {
            try
            {
                return action(GetRemoteScheduler());
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        protected virtual IRemotableQuartzScheduler GetRemoteScheduler()
        {
            if (rsched != null)
            {
                return rsched;
            }

            try
            {
                rsched = proxyFactory.GetProxy();
            }
            catch (Exception e)
            {
                string errorMessage = $"Could not get handle to remote scheduler: {e.Message}";
                SchedulerException initException = new SchedulerException(errorMessage, e);
                throw initException;
            }

            return rsched;
        }

        protected virtual SchedulerException InvalidateHandleCreateException(string msg, Exception cause)
        {
            rsched = null;
            SchedulerException ex = new SchedulerException(msg, cause);
            return ex;
        }

        public void Dispose()
        {
        }
    }
}