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
using System.Globalization;
using System.Runtime.Remoting;

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
        public virtual bool IsJobGroupPaused(string groupName)
        {
            return CallInGuard(x => x.IsJobGroupPaused(groupName));
        }

        /// <summary>
        /// returns true if the given TriggerGroup
        /// is paused
        /// </summary>
        /// <param name="groupName"></param>
        /// <returns></returns>
        public virtual bool IsTriggerGroupPaused(string groupName)
        {
            return CallInGuard(x => x.IsTriggerGroupPaused(groupName));
        }

        /// <summary>
        /// Returns the name of the <see cref="IScheduler" />.
        /// </summary>
        public virtual string SchedulerName
        {
            get { return CallInGuard(x => x.SchedulerName); }
        }

        /// <summary>
        /// Returns the instance Id of the <see cref="IScheduler" />.
        /// </summary>
        public virtual string SchedulerInstanceId
        {
            get { return CallInGuard(x => x.SchedulerInstanceId); }
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
        public virtual SchedulerMetaData GetMetaData()
        {
            return CallInGuard(x => new SchedulerMetaData(SchedulerName, SchedulerInstanceId, GetType(), true, IsStarted, InStandbyMode,
                                                          IsShutdown, x.RunningSince, x.NumJobsExecuted, x.JobStoreClass,
                                                          x.SupportsPersistence, x.Clustered, x.ThreadPoolClass, x.ThreadPoolSize, x.Version));
        }

        /// <summary> 
        /// Returns the <see cref="SchedulerContext" /> of the <see cref="IScheduler" />.
        /// </summary>
        public virtual SchedulerContext Context
        {
            get { return CallInGuard(x => x.SchedulerContext); }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual bool InStandbyMode
        {
            get { return CallInGuard(x => x.InStandbyMode); }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual bool IsShutdown
        {
            get { return CallInGuard(x => x.IsShutdown); }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual IList<IJobExecutionContext> GetCurrentlyExecutingJobs()
        {
            return CallInGuard(x => x.CurrentlyExecutingJobs);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual IList<string> GetJobGroupNames()
        {
            return CallInGuard(x => x.GetJobGroupNames());
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual IList<string> GetTriggerGroupNames()
        {
            return CallInGuard(x => x.GetTriggerGroupNames());
        }

        /// <summary>
        /// Get the names of all <see cref="ITrigger" /> groups that are paused.
        /// </summary>
        /// <value></value>
        public virtual Collection.ISet<string> GetPausedTriggerGroups()
        {
            return CallInGuard(x => x.GetPausedTriggerGroups());
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
        public virtual void Start()
        {
            CallInGuard(x => x.Start());
        }

        /// <summary> 
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public void StartDelayed(TimeSpan delay)
        {
            CallInGuard(x => x.StartDelayed(delay));
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
        public virtual bool IsStarted
        {
            get { return CallInGuard(x => x.RunningSince.HasValue); }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void Standby()
        {
            CallInGuard(x => x.Standby());
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void Shutdown()
        {
            try
            {
                string schedulerName = SchedulerName;
                GetRemoteScheduler().Shutdown();
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
        public virtual void Shutdown(bool waitForJobsToComplete)
        {
            CallInGuard(x => x.Shutdown(waitForJobsToComplete));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual DateTimeOffset ScheduleJob(IJobDetail jobDetail, ITrigger trigger)
        {
            return CallInGuard(x => x.ScheduleJob(jobDetail, trigger));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual DateTimeOffset ScheduleJob(ITrigger trigger)
        {
            return CallInGuard(x => x.ScheduleJob(trigger));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void AddJob(IJobDetail jobDetail, bool replace)
        {
            CallInGuard(x => x.AddJob(jobDetail, replace));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void AddJob(IJobDetail jobDetail, bool replace, bool storeNonDurableWhileAwaitingScheduling)
        {
            CallInGuard(x => x.AddJob(jobDetail, replace, storeNonDurableWhileAwaitingScheduling));
        }

        public virtual bool DeleteJobs(IList<JobKey> jobKeys)
        {
            return CallInGuard(x => x.DeleteJobs(jobKeys));
        }

        public virtual void ScheduleJobs(IDictionary<IJobDetail, Collection.ISet<ITrigger>> triggersAndJobs, bool replace)
        {
            CallInGuard(x => x.ScheduleJobs(triggersAndJobs, replace));
        }

        public void ScheduleJob(IJobDetail jobDetail, Collection.ISet<ITrigger> triggersForJob, bool replace)
        {
            CallInGuard(x => x.ScheduleJob(jobDetail, triggersForJob, replace));
        }

        public virtual bool UnscheduleJobs(IList<TriggerKey> triggerKeys)
        {
            return CallInGuard(x => x.UnscheduleJobs(triggerKeys));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual bool DeleteJob(JobKey jobKey)
        {
            return CallInGuard(x => x.DeleteJob(jobKey));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual bool UnscheduleJob(TriggerKey triggerKey)
        {
            return CallInGuard(x => x.UnscheduleJob(triggerKey));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual DateTimeOffset? RescheduleJob(TriggerKey triggerKey, ITrigger newTrigger)
        {
            return CallInGuard(x => x.RescheduleJob(triggerKey, newTrigger));
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
            CallInGuard(x => x.TriggerJob(jobKey, data));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void PauseTrigger(TriggerKey triggerKey)
        {
            CallInGuard(x => x.PauseTrigger(triggerKey));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void PauseTriggers(GroupMatcher<TriggerKey> matcher)
        {
            CallInGuard(x => x.PauseTriggers(matcher));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void PauseJob(JobKey jobKey)
        {
            CallInGuard(x => x.PauseJob(jobKey));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void PauseJobs(GroupMatcher<JobKey> matcher)
        {
            CallInGuard(x => x.PauseJobs(matcher));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void ResumeTrigger(TriggerKey triggerKey)
        {
            CallInGuard(x => x.ResumeTrigger(triggerKey));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void ResumeTriggers(GroupMatcher<TriggerKey> matcher)
        {
            CallInGuard(x => x.ResumeTriggers(matcher));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void ResumeJob(JobKey jobKey)
        {
            CallInGuard(x => x.ResumeJob(jobKey));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void ResumeJobs(GroupMatcher<JobKey> matcher)
        {
            CallInGuard(x => x.ResumeJobs(matcher));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void PauseAll()
        {
            CallInGuard(x => x.PauseAll());
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void ResumeAll()
        {
            CallInGuard(x => x.ResumeAll());
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Collection.ISet<JobKey> GetJobKeys(GroupMatcher<JobKey> matcher)
        {
            return CallInGuard(x => x.GetJobKeys(matcher));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual IList<ITrigger> GetTriggersOfJob(JobKey jobKey)
        {
            return CallInGuard(x => x.GetTriggersOfJob(jobKey));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Collection.ISet<TriggerKey> GetTriggerKeys(GroupMatcher<TriggerKey> matcher)
        {
            return CallInGuard(x => x.GetTriggerKeys(matcher));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual IJobDetail GetJobDetail(JobKey jobKey)
        {
            return CallInGuard(x => x.GetJobDetail(jobKey));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual bool CheckExists(JobKey jobKey)
        {
            return CallInGuard(x => x.CheckExists(jobKey));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual bool CheckExists(TriggerKey triggerKey)
        {
            return CallInGuard(x => x.CheckExists(triggerKey));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void Clear()
        {
            CallInGuard(x => x.Clear());
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual ITrigger GetTrigger(TriggerKey triggerKey)
        {
            return CallInGuard(x => x.GetTrigger(triggerKey));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual TriggerState GetTriggerState(TriggerKey triggerKey)
        {
            return CallInGuard(x => x.GetTriggerState(triggerKey));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void AddCalendar(string calName, ICalendar calendar, bool replace, bool updateTriggers)
        {
            CallInGuard(x => x.AddCalendar(calName, calendar, replace, updateTriggers));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual bool DeleteCalendar(string calName)
        {
            return CallInGuard(x => x.DeleteCalendar(calName));
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual ICalendar GetCalendar(string calName)
        {
            return CallInGuard(x => x.GetCalendar(calName));
        }

        /// <summary>
        /// Get the names of all registered <see cref="ICalendar"/>.
        /// </summary>
        /// <returns></returns>
        public IList<string> GetCalendarNames()
        {
            return CallInGuard(x => x.GetCalendarNames());
        }

        public IListenerManager ListenerManager
        {
            get { throw new SchedulerException("Operation not supported for remote schedulers."); }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual bool Interrupt(JobKey jobKey)
        {
            try
            {
                return GetRemoteScheduler().Interrupt(jobKey);
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

        public bool Interrupt(string fireInstanceId)
        {
            try
            {
                return GetRemoteScheduler().Interrupt(fireInstanceId);
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

        protected virtual void CallInGuard(Action<IRemotableQuartzScheduler> action)
        {
            try
            {
                action(GetRemoteScheduler());
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        protected virtual T CallInGuard<T>(Func<IRemotableQuartzScheduler, T> func)
        {
            try
            {
                return func(GetRemoteScheduler());
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
                string errorMessage = string.Format(CultureInfo.InvariantCulture, "Could not get handle to remote scheduler: {0}", e.Message);
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