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
        private string remoteSchedulerAddress;

        /// <summary>
        /// Construct a <see cref="RemoteScheduler" /> instance to proxy the given
        /// RemoteableQuartzScheduler instance.
        /// </summary>
        public RemoteScheduler(string schedId)
        {
            this.schedId = schedId;
        }

        /// <summary>
        /// returns true if the given JobGroup
        /// is paused
        /// </summary>
        /// <param name="groupName"></param>
        /// <returns></returns>
        public virtual bool IsJobGroupPaused(string groupName)
        {
            return GetRemoteScheduler().IsJobGroupPaused(groupName);
        }

        /// <summary>
        /// returns true if the given TriggerGroup
        /// is paused
        /// </summary>
        /// <param name="groupName"></param>
        /// <returns></returns>
        public virtual bool IsTriggerGroupPaused(string groupName)
        {
            return GetRemoteScheduler().IsTriggerGroupPaused(groupName);
        }

        /// <summary>
        /// Returns the name of the <see cref="IScheduler" />.
        /// </summary>
        public virtual string SchedulerName
        {
            get
            {
                try
                {
                    return GetRemoteScheduler().SchedulerName;
                }
                catch (RemotingException re)
                {
                    throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
                }
            }
        }

        /// <summary>
        /// Returns the instance Id of the <see cref="IScheduler" />.
        /// </summary>
        public virtual string SchedulerInstanceId
        {
            get
            {
                try
                {
                    return GetRemoteScheduler().SchedulerInstanceId;
                }
                catch (RemotingException re)
                {
                    throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
                }
            }
        }

        /// <summary>
        /// Gets or sets the remote scheduler address.
        /// </summary>
        /// <value>The remote scheduler address.</value>
        public virtual string RemoteSchedulerAddress
        {
            get { return remoteSchedulerAddress; }
            set { remoteSchedulerAddress = value; }
        }

        /// <summary>
        /// Get a <see cref="SchedulerMetaData"/> object describiing the settings
        /// and capabilities of the scheduler instance.
        /// <p>
        /// Note that the data returned is an 'instantaneous' snap-shot, and that as
        /// soon as it's returned, the meta data values may be different.
        /// </p>
        /// </summary>
        /// <returns></returns>
        public virtual SchedulerMetaData GetMetaData()
        {
            try
            {
                IRemotableQuartzScheduler sched = GetRemoteScheduler();

                return
                    new SchedulerMetaData(SchedulerName, SchedulerInstanceId, GetType(), true, IsStarted, InStandbyMode,
                                          IsShutdown, sched.RunningSince, sched.NumJobsExecuted, sched.JobStoreClass,
                                          sched.SupportsPersistence, sched.Clustered, sched.ThreadPoolClass, sched.ThreadPoolSize, sched.Version);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        /// <summary> 
        /// Returns the <see cref="SchedulerContext" /> of the <see cref="IScheduler" />.
        /// </summary>
        public virtual SchedulerContext Context
        {
            get
            {
                try
                {
                    return GetRemoteScheduler().SchedulerContext;
                }
                catch (RemotingException re)
                {
                    throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
                }
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual bool InStandbyMode
        {
            get
            {
                try
                {
                    return GetRemoteScheduler().InStandbyMode;
                }
                catch (RemotingException re)
                {
                    throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
                }
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual bool IsShutdown
        {
            get
            {
                try
                {
                    return GetRemoteScheduler().IsShutdown;
                }
                catch (RemotingException re)
                {
                    throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
                }
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual IList<JobExecutionContext> GetCurrentlyExecutingJobs()
        {
            try
            {
                return GetRemoteScheduler().CurrentlyExecutingJobs;
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual IList<string> JobGroupNames
        {
            get
            {
                try
                {
                    return GetRemoteScheduler().GetJobGroupNames();
                }
                catch (RemotingException re)
                {
                    throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
                }
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual IList<string> TriggerGroupNames
        {
            get
            {
                try
                {
                    return GetRemoteScheduler().GetTriggerGroupNames();
                }
                catch (RemotingException re)
                {
                    throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
                }
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual IList<string> CalendarNames
        {
            get
            {
                try
                {
                    return GetRemoteScheduler().GetCalendarNames();
                }
                catch (RemotingException re)
                {
                    throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
                }
            }
        }

        /// <summary>
        /// Calls the equialent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual IList<IJobListener> GlobalJobListeners
        {
            get { throw new SchedulerException("Operation not supported for remote schedulers."); }
        }

        /// <summary>
        /// Get the <i>global</i><see cref="IJobListener"/> that has
        /// the given name.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public virtual IJobListener GetGlobalJobListener(string name)
        {
            throw new SchedulerException("Operation not supported for remote schedulers.");
        }

        /// <summary>
        /// Get the <i>global</i><see cref="ITriggerListener"/> that
        /// has the given name.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public virtual ITriggerListener GetGlobalTriggerListener(string name)
        {
            throw new SchedulerException(
                "Operation not supported for remote schedulers.");
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual IList<ITriggerListener> GlobalTriggerListeners
        {
            get { throw new SchedulerException("Operation not supported for remote schedulers."); }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual IList<ISchedulerListener> SchedulerListeners
        {
            get { throw new SchedulerException("Operation not supported for remote schedulers."); }
        }

        /// <summary>
        /// Get the names of all <see cref="Trigger" /> groups that are paused.
        /// </summary>
        /// <value></value>
        public virtual Collection.ISet<string> GetPausedTriggerGroups()
        {
            try
            {
                return GetRemoteScheduler().GetPausedTriggerGroups();
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        /// <summary>
        /// Set the <see cref="JobFactory" /> that will be responsible for producing
        /// instances of <see cref="IJob" /> classes.
        /// <p>
        /// JobFactories may be of use to those wishing to have their application
        /// produce <see cref="IJob" /> instances via some special mechanism, such as to
        /// give the opertunity for dependency injection.
        /// </p>
        /// </summary>
        /// <value></value>
        /// <seealso cref="IJobFactory"/>
        /// <throws>  SchedulerException </throws>
        public virtual IJobFactory JobFactory
        {
            set { throw new SchedulerException("Operation not supported for remote schedulers."); }
        }


        protected virtual IRemotableQuartzScheduler GetRemoteScheduler()
        {
            if (rsched != null)
            {
                return rsched;
            }

            try
            {
                rsched =
                    (IRemotableQuartzScheduler)
                    Activator.GetObject(typeof (IRemotableQuartzScheduler), RemoteSchedulerAddress);
            }
            catch (Exception e)
            {
                SchedulerException initException =
                    new SchedulerException(string.Format(CultureInfo.InvariantCulture, "Could not get handle to remote scheduler: {0}", e.Message), e);
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

        /// <summary> 
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void Start()
        {
            try
            {
                GetRemoteScheduler().Start();
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        /// <summary> 
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public void StartDelayed(TimeSpan delay)
        {
            try
            {
                GetRemoteScheduler().StartDelayed(delay);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
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
            get
            {
                try
                {
                    return GetRemoteScheduler().RunningSince.HasValue;
                }
                catch (Exception re)
                {
                    throw InvalidateHandleCreateException(
                        "Error communicating with remote scheduler.", re);
                }
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void Standby()
        {
            try
            {
                GetRemoteScheduler().Standby();
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
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
            try
            {
                GetRemoteScheduler().Shutdown(waitForJobsToComplete);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual DateTimeOffset ScheduleJob(JobDetailImpl jobDetail, Trigger trigger)
        {
            try
            {
                return GetRemoteScheduler().ScheduleJob(jobDetail, trigger);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual DateTimeOffset ScheduleJob(Trigger trigger)
        {
            try
            {
                return GetRemoteScheduler().ScheduleJob(trigger);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void AddJob(JobDetailImpl jobDetail, bool replace)
        {
            try
            {
                GetRemoteScheduler().AddJob(jobDetail, replace);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }


        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual bool DeleteJob(string jobName, string groupName)
        {
            try
            {
                return GetRemoteScheduler().DeleteJob(jobName, groupName);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual bool UnscheduleJob(string triggerName, string groupName)
        {
            try
            {
                return GetRemoteScheduler().UnscheduleJob(triggerName, groupName);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual DateTimeOffset? RescheduleJob(string triggerName, string groupName, Trigger newTrigger)
        {
            try
            {
                return GetRemoteScheduler().RescheduleJob(triggerName, groupName, newTrigger);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void TriggerJob(string jobName, string groupName)
        {
            TriggerJob(jobName, groupName, null);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void TriggerJob(string jobName, string groupName, JobDataMap data)
        {
            try
            {
                GetRemoteScheduler().TriggerJob(jobName, groupName, data);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void TriggerJobWithVolatileTrigger(string jobName, string groupName)
        {
            TriggerJobWithVolatileTrigger(jobName, groupName, null);
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void TriggerJobWithVolatileTrigger(string jobName, string groupName, JobDataMap data)
        {
            try
            {
                GetRemoteScheduler().TriggerJobWithVolatileTrigger(jobName, groupName, data);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }


        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void PauseTrigger(string triggerName, string groupName)
        {
            try
            {
                GetRemoteScheduler().PauseTrigger(triggerName, groupName);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void PauseTriggerGroup(string groupName)
        {
            try
            {
                GetRemoteScheduler().PauseTriggerGroup(groupName);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void PauseJob(string jobName, string groupName)
        {
            try
            {
                GetRemoteScheduler().PauseJob(jobName, groupName);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }


        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void PauseJobGroup(string groupName)
        {
            try
            {
                GetRemoteScheduler().PauseJobGroup(groupName);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void ResumeTrigger(string triggerName, string groupName)
        {
            try
            {
                GetRemoteScheduler().ResumeTrigger(triggerName, groupName);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void ResumeTriggerGroup(string groupName)
        {
            try
            {
                GetRemoteScheduler().ResumeTriggerGroup(groupName);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }


        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void ResumeJob(string jobName, string groupName)
        {
            try
            {
                GetRemoteScheduler().ResumeJob(jobName, groupName);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }


        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void ResumeJobGroup(string groupName)
        {
            try
            {
                GetRemoteScheduler().ResumeJobGroup(groupName);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }


        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void PauseAll()
        {
            try
            {
                GetRemoteScheduler().PauseAll();
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void ResumeAll()
        {
            try
            {
                GetRemoteScheduler().ResumeAll();
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual IList<string> GetJobNames(string groupName)
        {
            try
            {
                return GetRemoteScheduler().GetJobNames(groupName);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }


        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual IList<Trigger> GetTriggersOfJob(string jobName, string groupName)
        {
            try
            {
                return GetRemoteScheduler().GetTriggersOfJob(jobName, groupName);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual IList<string> GetTriggerKeys(string groupName)
        {
            try
            {
                return GetRemoteScheduler().GetTriggerKeys(groupName);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual JobDetailImpl GetJobDetail(string jobName, string jobGroup)
        {
            try
            {
                return GetRemoteScheduler().GetJobDetail(jobName, jobGroup);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual Trigger GetTrigger(string triggerName, string triggerGroup)
        {
            try
            {
                return GetRemoteScheduler().GetTrigger(triggerName, triggerGroup);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }


        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual TriggerState GetTriggerState(string triggerName, string triggerGroup)
        {
            try
            {
                return GetRemoteScheduler().GetTriggerState(triggerName, triggerGroup);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void AddCalendar(string calName, ICalendar calendar, bool replace, bool updateTriggers)
        {
            try
            {
                GetRemoteScheduler().AddCalendar(calName, calendar, replace, updateTriggers);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }


        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual bool DeleteCalendar(string calName)
        {
            try
            {
                return GetRemoteScheduler().DeleteCalendar(calName);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual ICalendar GetCalendar(string calName)
        {
            try
            {
                return GetRemoteScheduler().GetCalendar(calName);
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        /// <summary>
        /// Get the names of all registered <see cref="ICalendar"/>.
        /// </summary>
        /// <returns></returns>
        public IList<string> GetCalendarNames()
        {
            try
            {
                return GetRemoteScheduler().GetCalendarNames();
            }
            catch (RemotingException re)
            {
                throw InvalidateHandleCreateException("Error communicating with remote scheduler.", re);
            }
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void AddGlobalJobListener(IJobListener jobListener)
        {
            throw new SchedulerException("Operation not supported for remote schedulers.");
        }

        /// <summary>
        /// Remove the identifed <see cref="IJobListener"/> from the <see cref="IScheduler"/>'s
        /// list of <i>global</i> listeners.
        /// </summary>
        /// <param name="name"></param>
        /// <returns>
        /// true if the identifed listener was found in the list, and removed
        /// </returns>
        public virtual bool RemoveGlobalJobListener(string name)
        {
            throw new SchedulerException("Operation not supported for remote schedulers.");
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void AddGlobalTriggerListener(ITriggerListener triggerListener)
        {
            throw new SchedulerException("Operation not supported for remote schedulers.");
        }

        /// <summary>
        /// Remove the identifed <see cref="ITriggerListener"/> from the <see cref="IScheduler"/>'s
        /// list of <i>global</i> listeners.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>
        /// true if the identifed listener was found in the list, and removed.
        /// </returns>
        public virtual bool RemoveGlobalTriggerListener(string name)
        {
            throw new SchedulerException(
                "Operation not supported for remote schedulers.");
        }


        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual void AddSchedulerListener(ISchedulerListener schedulerListener)
        {
            throw new SchedulerException("Operation not supported for remote schedulers.");
        }

        /// <summary> 
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual bool RemoveSchedulerListener(ISchedulerListener schedulerListener)
        {
            throw new SchedulerException("Operation not supported for remote schedulers.");
        }

        /// <summary>
        /// Calls the equivalent method on the 'proxied' <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual bool Interrupt(string jobName, string groupName)
        {
            try
            {
                return GetRemoteScheduler().Interrupt(jobName, groupName);
            }
            catch (RemotingException re)
            {
                throw new UnableToInterruptJobException(
                    InvalidateHandleCreateException("Error communicating with remote scheduler.", re));
            }
            catch (SchedulerException se)
            {
                throw new UnableToInterruptJobException(se);
            }
        }
    }
}