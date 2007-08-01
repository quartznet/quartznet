/* 
* Copyright 2004-2005 OpenSymphony 
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

/*
* Previously Copyright (c) 2001-2004 James House
*/

using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Remoting;
using System.Threading;
using Common.Logging;
using Nullables;

using Quartz;
using Quartz.Collection;
using Quartz.Impl;
using Quartz.Listener;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Core
{
    /// <summary>
    /// This is the heart of Quartz, an indirect implementation of the <see cref="IScheduler" />
    /// interface, containing methods to schedule <see cref="IJob" />s,
    /// register <see cref="IJobListener" /> instances, etc.
    /// </summary>
    /// <seealso cref="IScheduler" />
    /// <seealso cref="QuartzSchedulerThread" />
    /// <seealso cref="IJobStore" />
    /// <seealso cref="IThreadPool" />
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public class QuartzScheduler : IRemotableQuartzScheduler
    {
        private readonly ILog Log;
        private static readonly FileVersionInfo versionInfo;


        private QuartzSchedulerResources resources;

        private QuartzSchedulerThread schedThread;
        private SchedulerContext context = new SchedulerContext();

        private IDictionary jobListeners = new Hashtable(10);
        private IDictionary globalJobListeners = new Hashtable(10);
        private IDictionary triggerListeners = new Hashtable(10);
        private IDictionary globalTriggerListeners = new Hashtable(10);
        private ArrayList schedulerListeners = new ArrayList(10);
        private IJobFactory jobFactory = new SimpleJobFactory();
        internal ExecutingJobsManager jobMgr = null;
        internal ErrorLogger errLogger = null;
        private ISchedulerSignaler signaler;
        private Random random = new Random();
        private ArrayList holdToPreventGC = new ArrayList(5);
        private bool signalOnSchedulingChange = true;
        private bool closed = false;
        private NullableDateTime initialStart = null;

        static QuartzScheduler()
        {
            Assembly asm = Assembly.GetAssembly(typeof(QuartzScheduler));
            versionInfo = FileVersionInfo.GetVersionInfo(asm.Location);
        }

        /// <summary>
        /// Gets the version of the Quartz Scheduler.
        /// </summary>
        /// <value>The version.</value>
        public string Version
        {
            get { return versionInfo.FileVersion; }
        }

        /// <summary>
        /// Gets the version major.
        /// </summary>
        /// <value>The version major.</value>
        public static string VersionMajor
        {
            get { return versionInfo.FileMajorPart.ToString(); }
        }

        /// <summary>
        /// Gets the version minor.
        /// </summary>
        /// <value>The version minor.</value>
        public static string VersionMinor
        {
            get { return versionInfo.FileMinorPart.ToString(); }
        }

        /// <summary>
        /// Gets the version iteration.
        /// </summary>
        /// <value>The version iteration.</value>
        public static string VersionIteration
        {
            get { return versionInfo.FileBuildPart.ToString(); }
        }

        /// <summary>
        /// Gets the scheduler signaler.
        /// </summary>
        /// <value>The scheduler signaler.</value>
        public virtual ISchedulerSignaler SchedulerSignaler
        {
            get { return signaler; }
        }

        /// <summary>
        /// Returns the name of the <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual string SchedulerName
        {
            get { return resources.Name; }
        }

        /// <summary> 
        /// Returns the instance Id of the <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual string SchedulerInstanceId
        {
            get { return resources.InstanceId; }
        }


        /// <summary>
        /// Returns the <see cref="SchedulerContext" /> of the <see cref="IScheduler" />.
        /// </summary>
        public virtual SchedulerContext SchedulerContext
        {
            get { return context; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether to signal on scheduling change.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if schduler should signal on scheduling change; otherwise, <c>false</c>.
        /// </value>
        public virtual bool SignalOnSchedulingChange
        {
            get { return signalOnSchedulingChange; }
            set { signalOnSchedulingChange = value; }
        }

        /// <summary>
        /// Reports whether the <see cref="IScheduler" /> is paused.
        /// </summary>
        public virtual bool InStandbyMode
        {
            get { return schedThread.Paused; }
        }

        /// <summary>
        /// Gets the job store class.
        /// </summary>
        /// <value>The job store class.</value>
        public virtual Type JobStoreClass
        {
            get { return resources.JobStore.GetType(); }
        }

        /// <summary>
        /// Gets the thread pool class.
        /// </summary>
        /// <value>The thread pool class.</value>
        public virtual Type ThreadPoolClass
        {
            get { return resources.ThreadPool.GetType(); }
        }

        /// <summary>
        /// Gets the size of the thread pool.
        /// </summary>
        /// <value>The size of the thread pool.</value>
        public virtual int ThreadPoolSize
        {
            get { return resources.ThreadPool.PoolSize; }
        }

        /// <summary>
        /// Reports whether the <see cref="IScheduler" /> has been Shutdown.
        /// </summary>
        public virtual bool IsShutdown
        {
            get { return closed; }
        }

        /// <summary>
        /// Return a list of <see cref="JobExecutionContext" /> objects that
        /// represent all currently executing Jobs in this Scheduler instance.
        /// <p>
        /// This method is not cluster aware.  That is, it will only return Jobs
        /// currently executing in this Scheduler instance, not across the entire
        /// cluster.
        /// </p>
        /// <p>
        /// Note that the list returned is an 'instantaneous' snap-shot, and that as
        /// soon as it's returned, the true list of executing jobs may be different.
        /// </p>
        /// </summary>
        public virtual IList CurrentlyExecutingJobs
        {
            get { return jobMgr.ExecutingJobs; }
        }

        /// <summary>
        /// Get a List containing all of the <see cref="IJobListener" />
        /// s in the <see cref="IScheduler" />'s<i>global</i> list.
        /// </summary>
        public virtual IList GlobalJobListeners
        {
            get { return new ArrayList(globalJobListeners.Values); }
        }

        /// <summary>
        /// Get a Set containing the names of all the <i>non-global</i><see cref="IJobListener" />
        /// s registered with the <see cref="IScheduler" />.
        /// </summary>
        public virtual ISet JobListenerNames
        {
            get
            {
                lock (jobListeners)
                {
                    return new HashSet(jobListeners.Keys);
                }
            }
        }


        /// <summary>
        /// Get the <i>global</i><see cref="IJobListener" />
        /// that has the given name.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public IJobListener GetGlobalJobListener(string name)
        {
            lock (globalJobListeners)
            {
                return (IJobListener)globalJobListeners[name];
            }
        }

        /// <summary>
        /// Get a list containing all of the <see cref="ITriggerListener" />
        /// s in the <see cref="IScheduler" />'s<i>global</i> list.
        /// </summary>
        public virtual IList GlobalTriggerListeners
        {
            get 
            { 
                lock (globalTriggerListeners)
                {
                    return new ArrayList(globalTriggerListeners.Values);
                } 
            }
        }

        /// <summary>
        /// Get a Set containing the names of all the <i>non-global</i><see cref="ITriggerListener" />
        /// s registered with the <see cref="IScheduler" />.
        /// </summary>
        public virtual ISet TriggerListenerNames
        {
            get
            {
                lock (triggerListeners)
                {
                    return new HashSet(triggerListeners.Keys);
                }
            }
        }

        /// <summary>
        /// Get a List containing all of the <see cref="ISchedulerListener" />
        /// s registered with the <see cref="IScheduler" />.
        /// </summary>
        public virtual IList SchedulerListeners
        {
            get
            {
                lock (schedulerListeners)
                {
                    return (IList)schedulerListeners.Clone();
                }
            }
        }

        /// <summary>
        /// Gets or sets the job factory.
        /// </summary>
        /// <value>The job factory.</value>
        public virtual IJobFactory JobFactory
        {
            get { return jobFactory; }
            set
            {
                if (value == null)
                {
                    throw new ArgumentException("JobFactory cannot be set to null!");
                }

                Log.Info("JobFactory set to: " + value);

                jobFactory = value;
            }
        }



        /// <summary>
        /// Create a <see cref="QuartzScheduler" /> with the given configuration
        /// properties.
        /// </summary>
        /// <seealso cref="QuartzSchedulerResources" />
        public QuartzScheduler(QuartzSchedulerResources resources, SchedulingContext ctxt, long idleWaitTime,
                               int dbRetryInterval)
        {
            Log = LogManager.GetLogger(GetType());
            this.resources = resources;
            try
            {
                Bind();
            }
            catch (Exception re)
            {
                throw new SchedulerException("Unable to bind scheduler to RMI Registry.", re);
            }

            schedThread = new QuartzSchedulerThread(this, resources, ctxt);
            if (idleWaitTime > 0)
            {
                schedThread.IdleWaitTime = idleWaitTime;
            }
            if (dbRetryInterval > 0)
            {
                schedThread.DbFailureRetryInterval = dbRetryInterval;
            }

            jobMgr = new ExecutingJobsManager();
            AddGlobalJobListener(jobMgr);
            errLogger = new ErrorLogger();
            AddSchedulerListener(errLogger);

            signaler = new SchedulerSignalerImpl(this);

            Log.Info(string.Format("Quartz Scheduler v.{0} created.", Version));
        }

        /// <summary>
        /// Bind the scheduler to remoting infrastructure.
        /// </summary>
        private void Bind()
        {
            // TODO
        }

        /// <summary>
        /// Un-bind the scheduler from remoting infrastructure.
        /// </summary>
        private void UnBind()
        {
            // TODO
        }

        /// <summary>
        /// Adds an object that should be kept as reference to prevent
        /// it from being garbage collected.
        /// </summary>
        /// <param name="obj">The obj.</param>
        public virtual void AddNoGCObject(object obj)
        {
            holdToPreventGC.Add(obj);
        }

        /// <summary>
        /// Removes the object from garbae collection protected list.
        /// </summary>
        /// <param name="obj">The obj.</param>
        /// <returns></returns>
        public virtual bool RemoveNoGCObject(object obj)
        {
            return CollectionUtil.Remove(holdToPreventGC, obj);
        }

        /// <summary>
        /// Starts the <see cref="QuartzScheduler" />'s threads that fire <see cref="Trigger" />s.
        /// <p>
        /// All <see cref="Trigger" />s that have misfired will
        /// be passed to the appropriate TriggerListener(s).
        /// </p>
        /// </summary>
        public virtual void Start()
        {
            if (closed)
            {
                throw new SchedulerException("The Scheduler cannot be restarted after Shutdown() has been called.");
            }

            if (!initialStart.HasValue)
            {
                initialStart = DateTime.Now;
                resources.JobStore.SchedulerStarted();
                StartPlugins();
            }

            schedThread.TogglePause(false);

            Log.Info(string.Format("Scheduler {0} started.", resources.GetUniqueIdentifier()));
        }

        /// <summary>
        /// Temporarily halts the <see cref="QuartzScheduler" />'s firing of <see cref="Trigger" />s.
        /// <p>
        /// The scheduler is not destroyed, and can be re-started at any time.
        /// </p>
        /// </summary>
        public virtual void Standby()
        {
            schedThread.TogglePause(true);
            Log.Info(string.Format("Scheduler {0} paused.", resources.GetUniqueIdentifier()));
        }

        /// <summary>
        /// Gets the running since.
        /// </summary>
        /// <value>The running since.</value>
        public virtual NullableDateTime RunningSince
        {
            get { return initialStart; }
        }

        /// <summary>
        /// Gets the number of jobs executed.
        /// </summary>
        /// <value>The number of jobs executed.</value>
        public virtual int NumJobsExecuted
        {
            get { return jobMgr.NumJobsFired; }
        }

        /// <summary>
        /// Gets a value indicating whether this scheduler supports persistence.
        /// </summary>
        /// <value><c>true</c> if supports persistence; otherwise, <c>false</c>.</value>
        public virtual bool SupportsPersistence
        {
            get { return resources.JobStore.SupportsPersistence; }
        }

        /// <summary>
        /// Halts the <see cref="QuartzScheduler" />'s firing of <see cref="Trigger" />s,
        /// and cleans up all resources associated with the QuartzScheduler.
        /// Equivalent to <see cref="Shutdown(bool)" />.
        /// <p>
        /// The scheduler cannot be re-started.
        /// </p>
        /// </summary>
        public virtual void Shutdown()
        {
            Shutdown(false);
        }

        /// <summary>
        /// Halts the <see cref="QuartzScheduler" />'s firing of <see cref="Trigger" />s,
        /// and cleans up all resources associated with the QuartzScheduler.
        /// <p>
        /// The scheduler cannot be re-started.
        /// </p>
        /// </summary>
        /// <param name="waitForJobsToComplete">
        /// if <see langword="true" /> the scheduler will not allow this method
        /// to return until all currently executing jobs have completed.
        /// </param>
        public virtual void Shutdown(bool waitForJobsToComplete)
        {
            if (closed)
            {
                return;
            }

            Log.Info(string.Format("Scheduler {0} shutting down.", resources.GetUniqueIdentifier()));
            Standby();

            closed = true;

            schedThread.Halt();

            resources.ThreadPool.Shutdown(waitForJobsToComplete);

            if (waitForJobsToComplete)
            {
                while (jobMgr.NumJobsCurrentlyExecuting > 0)
                {
                    try
                    {
                        Thread.Sleep(100);
                    }
                    catch (ThreadInterruptedException)
                    {
                    }
                }
            }

            // Scheduler thread may have be waiting for the fire time of an acquired 
            // trigger and need time to release the trigger once halted, so make sure
            // the thread is dead before continuing to shutdown the job store.
            try
            {
                schedThread.Join();
            }
            catch (Exception)
            {
            }

            resources.JobStore.Shutdown();

            NotifySchedulerListenersShutdown();

            ShutdownPlugins();

            SchedulerRepository.Instance.Remove(resources.Name);

            holdToPreventGC.Clear();

            try
            {
                UnBind();
            }
            catch (RemotingException)
            {
            }

            Log.Info(string.Format("Scheduler {0} Shutdown complete.", resources.GetUniqueIdentifier()));
        }

        /// <summary>
        /// Validates the state.
        /// </summary>
        public virtual void ValidateState()
        {
            if (IsShutdown)
            {
                throw new SchedulerException("The Scheduler has been Shutdown.");
            }

            // other conditions to check (?)
        }

        /// <summary> 
        /// Add the <see cref="IJob" /> identified by the given
        /// <see cref="JobDetail" /> to the Scheduler, and
        /// associate the given <see cref="Trigger" /> with it.
        /// <p>
        /// If the given Trigger does not reference any <see cref="IJob" />, then it
        /// will be set to reference the Job passed with it into this method.
        /// </p>
        /// </summary>
        public virtual DateTime ScheduleJob(SchedulingContext ctxt, JobDetail jobDetail, Trigger trigger)
        {
            ValidateState();


            if (jobDetail == null)
            {
                throw new SchedulerException("JobDetail cannot be null",
                        SchedulerException.ERR_CLIENT_ERROR);
            }

            if (trigger == null)
            {
                throw new SchedulerException("Trigger cannot be null",
                        SchedulerException.ERR_CLIENT_ERROR);
            }

            jobDetail.Validate();

            if (trigger.JobName == null)
            {
                trigger.JobName = jobDetail.Name;
                trigger.JobGroup = jobDetail.Group;
            }
            else if (trigger.JobName != null && !trigger.JobName.Equals(jobDetail.Name))
            {
                throw new SchedulerException("Trigger does not reference given job!", SchedulerException.ERR_CLIENT_ERROR);
            }
            else if (trigger.JobGroup != null && !trigger.JobGroup.Equals(jobDetail.Group))
            {
                throw new SchedulerException("Trigger does not reference given job!", SchedulerException.ERR_CLIENT_ERROR);
            }

            trigger.Validate();

            ICalendar cal = null;
            if (trigger.CalendarName != null)
            {
                cal = resources.JobStore.RetrieveCalendar(ctxt, trigger.CalendarName);
                if (cal == null)
                {
                    throw new SchedulerException(string.Format("Calendar not found: {0}", trigger.CalendarName),
                                                 SchedulerException.ERR_PERSISTENCE_CALENDAR_DOES_NOT_EXIST);
                }
            }
            NullableDateTime ft = trigger.ComputeFirstFireTime(cal);

            if (!ft.HasValue)
            {
                throw new SchedulerException("Based on configured schedule, the given trigger will never fire.",
                                             SchedulerException.ERR_CLIENT_ERROR);
            }

            resources.JobStore.StoreJobAndTrigger(ctxt, jobDetail, trigger);
            NotifySchedulerThread();
            NotifySchedulerListenersScheduled(trigger);

            return ft.Value;
        }

        /// <summary>
        /// Schedule the given <see cref="Trigger" /> with the
        /// <see cref="IJob" /> identified by the <see cref="Trigger" />'s settings.
        /// </summary>
        public virtual DateTime ScheduleJob(SchedulingContext ctxt, Trigger trigger)
        {
            ValidateState();

            if (trigger == null)
            {
                throw new SchedulerException("Trigger cannot be null",
                        SchedulerException.ERR_CLIENT_ERROR);
            }

            trigger.Validate();

            ICalendar cal = null;
            if (trigger.CalendarName != null)
            {
                cal = resources.JobStore.RetrieveCalendar(ctxt, trigger.CalendarName);
                if (cal == null)
                {
                    throw new SchedulerException(string.Format("Calendar not found: {0}", trigger.CalendarName),
                                                 SchedulerException.ERR_PERSISTENCE_CALENDAR_DOES_NOT_EXIST);
                }
            }

            NullableDateTime ft = trigger.ComputeFirstFireTime(cal);
            if (!ft.HasValue)
            {
                throw new SchedulerException("Based on configured schedule, the given trigger will never fire.",
                                             SchedulerException.ERR_CLIENT_ERROR);
            }

            resources.JobStore.StoreTrigger(ctxt, trigger, false);
            NotifySchedulerThread();
            NotifySchedulerListenersScheduled(trigger);

            return ft.Value;
        }

        /// <summary>
        /// Add the given <see cref="IJob" /> to the Scheduler - with no associated
        /// <see cref="Trigger" />. The <see cref="IJob" /> will be 'dormant' until
        /// it is scheduled with a <see cref="Trigger" />, or <see cref="IScheduler.TriggerJob(string ,string)" />
        /// is called for it.
        /// <p>
        /// The <see cref="IJob" /> must by definition be 'durable', if it is not,
        /// SchedulerException will be thrown.
        /// </p>
        /// </summary>
        public virtual void AddJob(SchedulingContext ctxt, JobDetail jobDetail, bool replace)
        {
            ValidateState();

            if (!jobDetail.Durable && !replace)
            {
                throw new SchedulerException("Jobs added with no trigger must be durable.", SchedulerException.ERR_CLIENT_ERROR);
            }

            resources.JobStore.StoreJob(ctxt, jobDetail, replace);
        }

        /// <summary>
        /// Delete the identified <see cref="IJob" /> from the Scheduler - and any
        /// associated <see cref="Trigger" />s.
        /// </summary>
        /// <returns> true if the Job was found and deleted.</returns>
        public virtual bool DeleteJob(SchedulingContext ctxt, string jobName, string groupName)
        {
            ValidateState();

            if (groupName == null)
            {
                groupName = SchedulerConstants.DEFAULT_GROUP;
            }

            return resources.JobStore.RemoveJob(ctxt, jobName, groupName);
        }

        /// <summary>
        /// Remove the indicated <see cref="Trigger" /> from the
        /// scheduler.
        /// </summary>
        public virtual bool UnscheduleJob(SchedulingContext ctxt, string triggerName, string groupName)
        {
            ValidateState();

            if (groupName == null)
            {
                groupName = SchedulerConstants.DEFAULT_GROUP;
            }

            if (resources.JobStore.RemoveTrigger(ctxt, triggerName, groupName))
            {
                NotifySchedulerThread();
                NotifySchedulerListenersUnscheduled(triggerName, groupName);
            }
            else
            {
                return false;
            }

            return true;
        }


        /// <summary>
        /// Remove (delete) the <see cref="Trigger" /> with the
        /// given name, and store the new given one - which must be associated
        /// with the same job.
        /// </summary>
        /// <param name="ctxt">The scheduling context.</param>
        /// <param name="triggerName">The name of the <see cref="Trigger" /> to be removed.</param>
        /// <param name="groupName">The group name of the <see cref="Trigger" /> to be removed.</param>
        /// <param name="newTrigger">The new <see cref="Trigger" /> to be stored.</param>
        /// <returns>
        /// 	<see langword="null" /> if a <see cref="Trigger" /> with the given
        /// name and group was not found and removed from the store, otherwise
        /// the first fire time of the newly scheduled trigger.
        /// </returns>
        public virtual NullableDateTime RescheduleJob(SchedulingContext ctxt, string triggerName, string groupName,
                                                      Trigger newTrigger)
        {
            ValidateState();

            if (groupName == null)
            {
                groupName = SchedulerConstants.DEFAULT_GROUP;
            }

            newTrigger.Validate();

            ICalendar cal = null;
            if (newTrigger.CalendarName != null)
            {
                cal = resources.JobStore.RetrieveCalendar(ctxt, newTrigger.CalendarName);
            }
            NullableDateTime ft = newTrigger.ComputeFirstFireTime(cal);

            if (!ft.HasValue)
            {
                throw new SchedulerException("Based on configured schedule, the given trigger will never fire.",
                                             SchedulerException.ERR_CLIENT_ERROR);
            }

            if (resources.JobStore.ReplaceTrigger(ctxt, triggerName, groupName, newTrigger))
            {
                NotifySchedulerThread();
                NotifySchedulerListenersUnscheduled(triggerName, groupName);
                NotifySchedulerListenersScheduled(newTrigger);
            }
            else
            {
                return NullableDateTime.Default;
            }

            return ft;
        }


        private string NewTriggerId()
        {
            long r = NextLong(random);
            if (r < 0)
            {
                r = -r;
            }
            return "MT_" + Convert.ToString(r);
        }

        /// <summary>
        /// Creates a new positive random number 
        /// </summary>
        /// <param name="random">The last random obtained</param>
        /// <returns>Returns a new positive random number</returns>
        public static long NextLong(Random random)
        {
            long temporaryLong = random.Next();
            temporaryLong = (temporaryLong << 32) + random.Next();
            if (random.Next(-1, 1) < 0)
            {
                return -temporaryLong;
            }
            else
            {
                return temporaryLong;
            }
        }

        /// <summary>
        /// Trigger the identified <see cref="IJob" /> (Execute it now) - with a non-volatile trigger.
        /// </summary>
        public virtual void TriggerJob(SchedulingContext ctxt, string jobName, string groupName, JobDataMap data)
        {
            ValidateState();

            if (groupName == null)
            {
                groupName = SchedulerConstants.DEFAULT_GROUP;
            }

            Trigger trig =
                new SimpleTrigger(NewTriggerId(), SchedulerConstants.DEFAULT_MANUAL_TRIGGERS, jobName, groupName, DateTime.Now,
                                  NullableDateTime.Default, 0, 0);
            trig.Volatility = false;
            trig.ComputeFirstFireTime(null);
            if (data != null)
            {
                trig.JobDataMap = data;
            }

            bool collision = true;
            while (collision)
            {
                try
                {
                    resources.JobStore.StoreTrigger(ctxt, trig, false);
                    collision = false;
                }
                catch (ObjectAlreadyExistsException)
                {
                    trig.Name = NewTriggerId();
                }
            }

            NotifySchedulerThread();
            NotifySchedulerListenersScheduled(trig);
        }

        /// <summary>
        /// Trigger the identified <see cref="IJob" /> (Execute it
        /// now) - with a volatile trigger.
        /// </summary>
        public virtual void TriggerJobWithVolatileTrigger(SchedulingContext ctxt, string jobName, string groupName,
                                                          JobDataMap data)
        {
            ValidateState();

            if (groupName == null)
            {
                groupName = SchedulerConstants.DEFAULT_GROUP;
            }

            Trigger trig =
                new SimpleTrigger(NewTriggerId(), SchedulerConstants.DEFAULT_MANUAL_TRIGGERS, jobName, groupName, DateTime.Now,
                                  NullableDateTime.Default, 0, 0);
            trig.Volatility = true;
            trig.ComputeFirstFireTime(null);
            if (data != null)
            {
                trig.JobDataMap = data;
            }

            bool collision = true;
            while (collision)
            {
                try
                {
                    resources.JobStore.StoreTrigger(ctxt, trig, false);
                    collision = false;
                }
                catch (ObjectAlreadyExistsException)
                {
                    trig.Name = NewTriggerId();
                }
            }

            NotifySchedulerThread();
            NotifySchedulerListenersScheduled(trig);
        }

        /// <summary>
        /// Pause the <see cref="Trigger" /> with the given name.
        /// </summary>
        public virtual void PauseTrigger(SchedulingContext ctxt, string triggerName, string groupName)
        {
            ValidateState();

            if (groupName == null)
            {
                groupName = SchedulerConstants.DEFAULT_GROUP;
            }

            resources.JobStore.PauseTrigger(ctxt, triggerName, groupName);
            NotifySchedulerThread();
            NotifySchedulerListenersPausedTrigger(triggerName, groupName);
        }

        /// <summary>
        /// Pause all of the <see cref="Trigger" />s in the given group.
        /// </summary>
        public virtual void PauseTriggerGroup(SchedulingContext ctxt, string groupName)
        {
            ValidateState();

            if (groupName == null)
            {
                groupName = SchedulerConstants.DEFAULT_GROUP;
            }

            resources.JobStore.PauseTriggerGroup(ctxt, groupName);
            NotifySchedulerThread();
            NotifySchedulerListenersPausedTrigger(null, groupName);
        }

        /// <summary> 
        /// Pause the <see cref="JobDetail" /> with the given
        /// name - by pausing all of its current <see cref="Trigger" />s.
        /// </summary>
        public virtual void PauseJob(SchedulingContext ctxt, string jobName, string groupName)
        {
            ValidateState();

            if (groupName == null)
            {
                groupName = SchedulerConstants.DEFAULT_GROUP;
            }

            resources.JobStore.PauseJob(ctxt, jobName, groupName);
            NotifySchedulerThread();
            NotifySchedulerListenersPausedJob(jobName, groupName);
        }

        /// <summary>
        /// Pause all of the <see cref="JobDetail" />s in the
        /// given group - by pausing all of their <see cref="Trigger" />s.
        /// </summary>
        public virtual void PauseJobGroup(SchedulingContext ctxt, string groupName)
        {
            ValidateState();

            if (groupName == null)
            {
                groupName = SchedulerConstants.DEFAULT_GROUP;
            }

            resources.JobStore.PauseJobGroup(ctxt, groupName);
            NotifySchedulerThread();
            NotifySchedulerListenersPausedJob(null, groupName);
        }

        /// <summary>
        /// Resume (un-pause) the <see cref="Trigger" /> with the given
        /// name.
        /// <p>
        /// If the <see cref="Trigger" /> missed one or more fire-times, then the
        /// <see cref="Trigger" />'s misfire instruction will be applied.
        /// </p>
        /// </summary>
        public virtual void ResumeTrigger(SchedulingContext ctxt, string triggerName, string groupName)
        {
            ValidateState();

            if (groupName == null)
            {
                groupName = SchedulerConstants.DEFAULT_GROUP;
            }

            resources.JobStore.ResumeTrigger(ctxt, triggerName, groupName);
            NotifySchedulerThread();
            NotifySchedulerListenersResumedTrigger(triggerName, groupName);
        }

        /// <summary>
        /// Resume (un-pause) all of the <see cref="Trigger" />s in the
        /// given group.
        /// <p>
        /// If any <see cref="Trigger" /> missed one or more fire-times, then the
        /// <see cref="Trigger" />'s misfire instruction will be applied.
        /// </p>
        /// </summary>
        public virtual void ResumeTriggerGroup(SchedulingContext ctxt, string groupName)
        {
            ValidateState();

            if (groupName == null)
            {
                groupName = SchedulerConstants.DEFAULT_GROUP;
            }

            resources.JobStore.ResumeTriggerGroup(ctxt, groupName);
            NotifySchedulerThread();
            NotifySchedulerListenersResumedTrigger(null, groupName);
        }

        /// <summary>
        /// Gets the paused trigger groups.
        /// </summary>
        /// <param name="ctxt">The the job scheduling context.</param>
        /// <returns></returns>
        public virtual ISet GetPausedTriggerGroups(SchedulingContext ctxt)
        {
            return resources.JobStore.GetPausedTriggerGroups(ctxt);
        }

        /// <summary>
        /// Resume (un-pause) the <see cref="JobDetail" /> with
        /// the given name.
        /// <p>
        /// If any of the <see cref="IJob" />'s<see cref="Trigger" /> s missed one
        /// or more fire-times, then the <see cref="Trigger" />'s misfire
        /// instruction will be applied.
        /// </p>
        /// </summary>
        public virtual void ResumeJob(SchedulingContext ctxt, string jobName, string groupName)
        {
            ValidateState();

            if (groupName == null)
            {
                groupName = SchedulerConstants.DEFAULT_GROUP;
            }

            resources.JobStore.ResumeJob(ctxt, jobName, groupName);
            NotifySchedulerThread();
            NotifySchedulerListenersResumedJob(jobName, groupName);
        }

        /// <summary>
        /// Resume (un-pause) all of the <see cref="JobDetail" />s
        /// in the given group.
        /// <p>
        /// If any of the <see cref="IJob" /> s had <see cref="Trigger" /> s that
        /// missed one or more fire-times, then the <see cref="Trigger" />'s
        /// misfire instruction will be applied.
        /// </p>
        /// </summary>
        public virtual void ResumeJobGroup(SchedulingContext ctxt, string groupName)
        {
            ValidateState();

            if (groupName == null)
            {
                groupName = SchedulerConstants.DEFAULT_GROUP;
            }

            resources.JobStore.ResumeJobGroup(ctxt, groupName);
            NotifySchedulerThread();
            NotifySchedulerListenersResumedJob(null, groupName);
        }

        /// <summary>
        /// Pause all triggers - equivalent of calling <see cref="PauseTriggerGroup(SchedulingContext, string)" />
        /// on every group.
        /// <p>
        /// When <see cref="ResumeAll" /> is called (to un-pause), trigger misfire
        /// instructions WILL be applied.
        /// </p>
        /// </summary>
        /// <seealso cref="ResumeAll(SchedulingContext)" />
        /// <seealso cref="PauseJob" />
        public virtual void PauseAll(SchedulingContext ctxt)
        {
            ValidateState();

            resources.JobStore.PauseAll(ctxt);
            NotifySchedulerThread();
            NotifySchedulerListenersPausedTrigger(null, null);
        }

        /// <summary>
        /// Resume (un-pause) all triggers - equivalent of calling <see cref="ResumeTriggerGroup(SchedulingContext, string)" />
        /// on every group.
        /// <p>
        /// If any <see cref="Trigger" /> missed one or more fire-times, then the
        /// <see cref="Trigger" />'s misfire instruction will be applied.
        /// </p>
        /// </summary>
        /// <seealso cref="PauseAll(SchedulingContext)" />
        public virtual void ResumeAll(SchedulingContext ctxt)
        {
            ValidateState();

            resources.JobStore.ResumeAll(ctxt);
            NotifySchedulerThread();
            NotifySchedulerListenersResumedTrigger(null, null);
        }

        /// <summary>
        /// Get the names of all known <see cref="IJob" /> groups.
        /// </summary>
        public virtual string[] GetJobGroupNames(SchedulingContext ctxt)
        {
            ValidateState();

            return resources.JobStore.GetJobGroupNames(ctxt);
        }

        /// <summary>
        /// Get the names of all the <see cref="IJob" />s in the
        /// given group.
        /// </summary>
        public virtual string[] GetJobNames(SchedulingContext ctxt, string groupName)
        {
            ValidateState();

            if (groupName == null)
            {
                groupName = SchedulerConstants.DEFAULT_GROUP;
            }

            return resources.JobStore.GetJobNames(ctxt, groupName);
        }

        /// <summary> 
        /// Get all <see cref="Trigger" /> s that are associated with the
        /// identified <see cref="JobDetail" />.
        /// </summary>
        public virtual Trigger[] GetTriggersOfJob(SchedulingContext ctxt, string jobName, string groupName)
        {
            ValidateState();

            if (groupName == null)
            {
                groupName = SchedulerConstants.DEFAULT_GROUP;
            }

            return resources.JobStore.GetTriggersForJob(ctxt, jobName, groupName);
        }

        /// <summary>
        /// Get the names of all known <see cref="Trigger" />
        /// groups.
        /// </summary>
        public virtual string[] GetTriggerGroupNames(SchedulingContext ctxt)
        {
            ValidateState();
            return resources.JobStore.GetTriggerGroupNames(ctxt);
        }

        /// <summary>
        /// Get the names of all the <see cref="Trigger" />s in
        /// the given group.
        /// </summary>
        public virtual string[] GetTriggerNames(SchedulingContext ctxt, string groupName)
        {
            ValidateState();

            if (groupName == null)
            {
                groupName = SchedulerConstants.DEFAULT_GROUP;
            }

            return resources.JobStore.GetTriggerNames(ctxt, groupName);
        }

        /// <summary> 
        /// Get the <see cref="JobDetail" /> for the <see cref="IJob" />
        /// instance with the given name and group.
        /// </summary>
        public virtual JobDetail GetJobDetail(SchedulingContext ctxt, string jobName, string jobGroup)
        {
            ValidateState();

            if (jobGroup == null)
            {
                jobGroup = SchedulerConstants.DEFAULT_GROUP;
            }

            return resources.JobStore.RetrieveJob(ctxt, jobName, jobGroup);
        }

        /// <summary>
        /// Get the <see cref="Trigger" /> instance with the given name and
        /// group.
        /// </summary>
        public virtual Trigger GetTrigger(SchedulingContext ctxt, string triggerName, string triggerGroup)
        {
            ValidateState();

            if (triggerGroup == null)
            {
                triggerGroup = SchedulerConstants.DEFAULT_GROUP;
            }

            return resources.JobStore.RetrieveTrigger(ctxt, triggerName, triggerGroup);
        }

        /// <summary>
        /// Get the current state of the identified <see cref="Trigger" />.  
        /// </summary>
        /// <seealso cref="TriggerState.Normal" />
        /// <seealso cref="TriggerState.Paused" />
        /// <seealso cref="TriggerState.Complete" />
        /// <seealso cref="TriggerState.Error" />      
        public virtual TriggerState GetTriggerState(SchedulingContext ctxt, string triggerName, string triggerGroup)
        {
            ValidateState();

            if (triggerGroup == null)
            {
                triggerGroup = SchedulerConstants.DEFAULT_GROUP;
            }

            return resources.JobStore.GetTriggerState(ctxt, triggerName, triggerGroup);
        }

        /// <summary>
        /// Add (register) the given <see cref="ICalendar" /> to the Scheduler.
        /// </summary>
        public virtual void AddCalendar(SchedulingContext ctxt, string calName, ICalendar calendar, bool replace,
                                        bool updateTriggers)
        {
            ValidateState();
            resources.JobStore.StoreCalendar(ctxt, calName, calendar, replace, updateTriggers);
        }

        /// <summary>
        /// Delete the identified <see cref="ICalendar" /> from the Scheduler.
        /// </summary>
        /// <returns> true if the Calendar was found and deleted.</returns>
        public virtual bool DeleteCalendar(SchedulingContext ctxt, string calName)
        {
            ValidateState();
            return resources.JobStore.RemoveCalendar(ctxt, calName);
        }

        /// <summary> 
        /// Get the <see cref="ICalendar" /> instance with the given name.
        /// </summary>
        public virtual ICalendar GetCalendar(SchedulingContext ctxt, string calName)
        {
            ValidateState();
            return resources.JobStore.RetrieveCalendar(ctxt, calName);
        }

        /// <summary>
        /// Get the names of all registered <see cref="ICalendar" />s.
        /// </summary>
        public virtual string[] GetCalendarNames(SchedulingContext ctxt)
        {
            ValidateState();
            return resources.JobStore.GetCalendarNames(ctxt);
        }

        /// <summary>
        /// Add the given <see cref="IJobListener" /> to the
        /// <see cref="IScheduler" />'s<i>global</i> list.
        /// <p>
        /// Listeners in the 'global' list receive notification of execution events
        /// for ALL <see cref="IJob" />s.
        /// </p>
        /// </summary>
        public void AddGlobalJobListener(IJobListener jobListener)
        {
            if (jobListener.Name == null || jobListener.Name.Length == 0)
            {
                throw new ArgumentException("JobListener name cannot be empty.");
            }
            lock (globalJobListeners)
            {
                globalJobListeners.Add(jobListener.Name, jobListener);
            }
        }

        /// <summary>
        /// Add the given <see cref="IJobListener" /> to the
        /// <see cref="IScheduler" />'s list, of registered <see cref="IJobListener" />s.
        /// </summary>
        public virtual void AddJobListener(IJobListener jobListener)
        {
            if (jobListener.Name == null || jobListener.Name.Length == 0)
            {
                throw new ArgumentException("JobListener name cannot be empty.");
            }
            lock (jobListener)
            {
                jobListeners.Add(jobListener.Name, jobListener);
            }
        }

        /// <summary> 
        /// Remove the given <see cref="IJobListener" /> from the
        /// <see cref="IScheduler" />'s list of <i>global</i> listeners.
        /// </summary>
        /// <returns> 
        /// true if the identifed listener was found in the list, and removed.
        /// </returns>
        public virtual bool RemoveGlobalJobListener(IJobListener jobListener)
        {
            return RemoveGlobalJobListener((jobListener == null) ? null : jobListener.Name);
        }


        /// <summary>
        /// Remove the identifed <see cref="IJobListener" /> from the <see cref="IScheduler" />'s
        /// list of <i>global</i> listeners. 
        /// </summary>
        /// <param name="name"></param>
        /// <returns>true if the identifed listener was found in the list, and removed.</returns>
        public bool RemoveGlobalJobListener(string name)
        {
            lock (globalJobListeners)
            {
                if (globalJobListeners.Contains(name))
                {
                    globalJobListeners.Remove(name);
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Remove the identifed <see cref="IJobListener" /> from
        /// the <see cref="IScheduler" />'s list of registered listeners.
        /// </summary>
        /// <returns> 
        /// true if the identifed listener was found in the list, and removed.
        /// </returns>
        public virtual bool RemoveJobListener(string name)
        {
            lock (jobListeners)
            {
                if (jobListeners.Contains(name))
                {
                    jobListeners.Remove(name);
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Get the <i>non-global</i><see cref="IJobListener" /> that has the given name.
        /// </summary>
        public virtual IJobListener GetJobListener(string name)
        {
            lock (jobListeners)
            {
                return (IJobListener) jobListeners[name];
            }
        }

        /// <summary>
        /// Add the given <see cref="ITriggerListener" /> to the
        /// <see cref="IScheduler" />'s<i>global</i> list.
        /// <p>
        /// Listeners in the 'global' list receive notification of execution events
        /// for ALL <see cref="Trigger" />s.
        /// </p>
        /// </summary>
        public virtual void AddGlobalTriggerListener(ITriggerListener triggerListener)
        {
            if (triggerListener.Name == null || triggerListener.Name.Trim().Length == 0)
            {
                throw new ArgumentException("TriggerListener name cannot be empty.");
            }

            lock (globalTriggerListeners)
            {
                globalTriggerListeners[triggerListener.Name] = triggerListener;
            }
        }

        /// <summary> 
        /// Add the given <see cref="ITriggerListener" /> to the
        /// <see cref="IScheduler" />'s list, of registered <see cref="ITriggerListener" />s.
        /// </summary>
        public virtual void AddTriggerListener(ITriggerListener triggerListener)
        {
            if (triggerListener.Name == null || triggerListener.Name.Trim().Length == 0)
            {
                throw new ArgumentException("TriggerListener name cannot be empty.");
            }

            lock (triggerListeners)
            {
                triggerListeners[triggerListener.Name] = triggerListener;
            }
        }

        /// <summary> 
        /// Remove the given <see cref="ITriggerListener" /> from
        /// the <see cref="IScheduler" />'s list of <i>global</i> listeners.
        /// </summary>
        /// <returns> 
        /// true if the identifed listener was found in the list, and removed.
        /// </returns>
        public virtual bool RemoveGlobalTriggerListener(ITriggerListener triggerListener)
        {
            return RemoveGlobalTriggerListener((triggerListener == null) ? null : triggerListener.Name);
        }

        /// <summary>
        ///  Remove the identifed <see cref="ITriggerListener" /> from the <see cref="IScheduler" />'s
        /// list of <i>global</i> listeners.
        /// </summary>
        /// <param name="name"></param>
        /// <returns> true if the identifed listener was found in the list, and removed</returns>
        public bool RemoveGlobalTriggerListener(string name)
        {
            lock (globalTriggerListeners)
            {
                if (globalTriggerListeners.Contains(name))
                {
                    globalTriggerListeners.Remove(name);
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Remove the identifed <see cref="ITriggerListener" />
        /// from the <see cref="IScheduler" />'s list of registered listeners.
        /// </summary>
        /// <returns>
        /// true if the identifed listener was found in the list, and removed.
        /// </returns>
        public virtual bool RemoveTriggerListener(string name)
        {
            lock (triggerListeners)
            {
                if (triggerListeners.Contains(name))
                {
                    triggerListeners.Remove(name);
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Get the <i>non-global</i> <see cref="ITriggerListener" />
        /// that has the given name.
        /// </summary>
        public ITriggerListener GetTriggerListener(string name)
        {
            lock (triggerListeners)
            {
                return (ITriggerListener) triggerListeners[name];
            }
        }


        /// <summary>
        /// Get the <i>global</i> <see cref="ITriggerListener" /> that
        /// has the given name.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public ITriggerListener GetGlobalTriggerListener(string name)
        {
            lock (globalTriggerListeners)
            {
                return (ITriggerListener)globalTriggerListeners[name];
            }
        }
    

        /// <summary>
        /// Register the given <see cref="ISchedulerListener" /> with the
        /// <see cref="IScheduler" />.
        /// </summary>
        public void AddSchedulerListener(ISchedulerListener schedulerListener)
        {
            lock (schedulerListeners)
            {
                schedulerListeners.Add(schedulerListener);
            }
        }

        /// <summary>
        /// Remove the given <see cref="ISchedulerListener" /> from the
        /// <see cref="IScheduler" />.
        /// </summary>
        /// <returns> 
        /// true if the identifed listener was found in the list, and removed.
        /// </returns>
        public virtual bool RemoveSchedulerListener(ISchedulerListener schedulerListener)
        {
            lock (schedulerListeners)
            {
                return CollectionUtil.Remove(schedulerListeners, schedulerListener);
            }
        }


        protected internal void NotifyJobStoreJobVetoed(SchedulingContext ctxt,
                Trigger trigger, JobDetail detail, SchedulerInstruction instCode)
        {

            resources.JobStore.TriggeredJobComplete(ctxt, trigger, detail, instCode);
        }

        /// <summary>
        /// Notifies the job store job complete.
        /// </summary>
        /// <param name="ctxt">The job scheduling context.</param>
        /// <param name="trigger">The trigger.</param>
        /// <param name="detail">The detail.</param>
        /// <param name="instCode">The instruction code.</param>
        protected internal virtual void NotifyJobStoreJobComplete(SchedulingContext ctxt, Trigger trigger, JobDetail detail,
                                                                  SchedulerInstruction instCode)
        {
            resources.JobStore.TriggeredJobComplete(ctxt, trigger, detail, instCode);
        }

        /// <summary>
        /// Notifies the scheduler thread.
        /// </summary>
        protected internal virtual void NotifySchedulerThread()
        {
            if (SignalOnSchedulingChange)
            {
                schedThread.SignalSchedulingChange();
            }
        }

        private IList BuildTriggerListenerList(string[] additionalListeners)
        {
            IList listeners = GlobalTriggerListeners;
            for (int i = 0; i < additionalListeners.Length; i++)
            {
                ITriggerListener tl = GetTriggerListener(additionalListeners[i]);

                if (tl != null)
                {
                    listeners.Add(tl);
                }
                else
                {
                    throw new SchedulerException(string.Format("TriggerListener '{0}' not found.", additionalListeners[i]),
                                                 SchedulerException.ERR_TRIGGER_LISTENER_NOT_FOUND);
                }
            }

            return listeners;
        }

        private IList BuildJobListenerList(string[] additionalListeners)
        {
            IList listeners = GlobalJobListeners;
            for (int i = 0; i < additionalListeners.Length; i++)
            {
                IJobListener jl = GetJobListener(additionalListeners[i]);

                if (jl != null)
                {
                    listeners.Add(jl);
                }
                else
                {
                    throw new SchedulerException(string.Format("JobListener '{0}' not found.", additionalListeners[i]),
                                                 SchedulerException.ERR_JOB_LISTENER_NOT_FOUND);
                }
            }

            return listeners;
        }

        /// <summary>
        /// Notifies the trigger listeners about fired trigger.
        /// </summary>
        /// <param name="jec">The job execution context.</param>
        /// <returns></returns>
        public virtual bool NotifyTriggerListenersFired(JobExecutionContext jec)
        {
            // build a list of all trigger listeners that are to be notified...
            IList listeners = BuildTriggerListenerList(jec.Trigger.TriggerListenerNames);

            bool vetoedExecution = false;

            // notify all trigger listeners in the list
            foreach (ITriggerListener tl in listeners)
            {
                try
                {
                    tl.TriggerFired(jec.Trigger, jec);

                    if (tl.VetoJobExecution(jec.Trigger, jec))
                    {
                        vetoedExecution = true;
                    }
                }
                catch (Exception e)
                {
                    SchedulerException se = new SchedulerException(string.Format("TriggerListener '{0}' threw exception: {1}", tl.Name, e.Message), e);
                    se.ErrorCode = SchedulerException.ERR_TRIGGER_LISTENER;
                    throw se;
                }
            }

            return vetoedExecution;
        }


        /// <summary>
        /// Notifies the trigger listeners about misfired trigger.
        /// </summary>
        /// <param name="trigger">The trigger.</param>
        public virtual void NotifyTriggerListenersMisfired(Trigger trigger)
        {
            // build a list of all trigger listeners that are to be notified...
            IList listeners = BuildTriggerListenerList(trigger.TriggerListenerNames);

            // notify all trigger listeners in the list
            foreach (ITriggerListener tl in listeners)
            {
                try
                {
                    tl.TriggerMisfired(trigger);
                }
                catch (Exception e)
                {
                    SchedulerException se = new SchedulerException(string.Format("TriggerListener '{0}' threw exception: {1}", tl.Name, e.Message), e);
                    se.ErrorCode = SchedulerException.ERR_TRIGGER_LISTENER;
                    throw se;
                }
            }
        }

        /// <summary>
        /// Notifies the trigger listeners of completion.
        /// </summary>
        /// <param name="jec">The job executution context.</param>
        /// <param name="instCode">The instruction code to report to triggers.</param>
        public virtual void NotifyTriggerListenersComplete(JobExecutionContext jec, SchedulerInstruction instCode)
        {
            // build a list of all trigger listeners that are to be notified...
            IList listeners = BuildTriggerListenerList(jec.Trigger.TriggerListenerNames);

            // notify all trigger listeners in the list
            foreach (ITriggerListener tl in listeners)
            {
                try
                {
                    tl.TriggerComplete(jec.Trigger, jec, instCode);
                }
                catch (Exception e)
                {
                    SchedulerException se = new SchedulerException(string.Format("TriggerListener '{0}' threw exception: {1}", tl.Name, e.Message), e);
                    se.ErrorCode = SchedulerException.ERR_TRIGGER_LISTENER;
                    throw se;
                }
            }
        }

        /// <summary>
        /// Notifies the job listeners about job to be executed.
        /// </summary>
        /// <param name="jec">The jec.</param>
        public virtual void NotifyJobListenersToBeExecuted(JobExecutionContext jec)
        {
            // build a list of all job listeners that are to be notified...
            IList listeners = BuildJobListenerList(jec.JobDetail.JobListenerNames);

            // notify all job listeners
            foreach (IJobListener jl in listeners)
            {
                try
                {
                    jl.JobToBeExecuted(jec);
                }
                catch (Exception e)
                {
                    SchedulerException se = new SchedulerException(string.Format("JobListener '{0}' threw exception: {1}", jl.Name, e.Message), e);
                    se.ErrorCode = SchedulerException.ERR_JOB_LISTENER;
                    throw se;
                }
            }
        }

        /// <summary>
        /// Notifies the job listeners that job exucution was vetoed.
        /// </summary>
        /// <param name="jec">The job execution context.</param>
        public virtual void NotifyJobListenersWasVetoed(JobExecutionContext jec)
        {
            // build a list of all job listeners that are to be notified...
            IList listeners = BuildJobListenerList(jec.JobDetail.JobListenerNames);

            // notify all job listeners
            foreach (IJobListener jl in listeners)
            {
                try
                {
                    jl.JobExecutionVetoed(jec);
                }
                catch (Exception e)
                {
                    SchedulerException se = new SchedulerException(string.Format("JobListener '{0}' threw exception: {1}", jl.Name, e.Message), e);
                    se.ErrorCode = SchedulerException.ERR_JOB_LISTENER;
                    throw se;
                }
            }
        }

        /// <summary>
        /// Notifies the job listeners that job was executed.
        /// </summary>
        /// <param name="jec">The jec.</param>
        /// <param name="je">The je.</param>
        public virtual void NotifyJobListenersWasExecuted(JobExecutionContext jec, JobExecutionException je)
        {
            // build a list of all job listeners that are to be notified...
            IList listeners = BuildJobListenerList(jec.JobDetail.JobListenerNames);

            // notify all job listeners
            foreach (IJobListener jl in listeners)
            {
                try
                {
                    jl.JobWasExecuted(jec, je);
                }
                catch (Exception e)
                {
                    SchedulerException se = new SchedulerException(string.Format("JobListener '{0}' threw exception: {1}", jl.Name, e.Message), e);
                    se.ErrorCode = SchedulerException.ERR_JOB_LISTENER;
                    throw se;
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about scheduler error.
        /// </summary>
        /// <param name="msg">The MSG.</param>
        /// <param name="se">The se.</param>
        public virtual void NotifySchedulerListenersError(string msg, SchedulerException se)
        {
            // build a list of all scheduler listeners that are to be notified...
            IList schedListeners = SchedulerListeners;

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    sl.SchedulerError(msg, se);
                }
                catch (Exception e)
                {
                    Log.Error("Error while notifying SchedulerListener of error: ", e);
                    Log.Error("  Original error (for notification) was: " + msg, se);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about job that was scheduled.
        /// </summary>
        /// <param name="trigger">The trigger.</param>
        public virtual void NotifySchedulerListenersScheduled(Trigger trigger)
        {
            // build a list of all scheduler listeners that are to be notified...
            IList schedListeners = SchedulerListeners;

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    sl.JobScheduled(trigger);
                }
                catch (Exception e)
                {
                    Log.Error(string.Format("Error while notifying SchedulerListener of scheduled job.  Triger={0}", trigger.FullName), e);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about job that was unscheduled.
        /// </summary>
        /// <param name="triggerName">Name of the trigger.</param>
        /// <param name="triggerGroup">The trigger group.</param>
        public virtual void NotifySchedulerListenersUnscheduled(string triggerName, string triggerGroup)
        {
            // build a list of all scheduler listeners that are to be notified...
            IList schedListeners = SchedulerListeners;

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    sl.JobUnscheduled(triggerName, triggerGroup);
                }
                catch (Exception e)
                {
                    Log.Error(
                        string.Format("Error while notifying SchedulerListener of unscheduled job.  Triger={0}.{1}", triggerGroup, triggerName), e);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about finalized trigger.
        /// </summary>
        /// <param name="trigger">The trigger.</param>
        public virtual void NotifySchedulerListenersFinalized(Trigger trigger)
        {
            // build a list of all scheduler listeners that are to be notified...
            IList schedListeners = SchedulerListeners;

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    sl.TriggerFinalized(trigger);
                }
                catch (Exception e)
                {
                    Log.Error(string.Format("Error while notifying SchedulerListener of finalized trigger.  Triger={0}", trigger.FullName), e);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about paused trigger.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="group">The group.</param>
        public virtual void NotifySchedulerListenersPausedTrigger(string name, string group)
        {
            // build a list of all job listeners that are to be notified...
            IList schedListeners = SchedulerListeners;

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    sl.TriggersPaused(name, group);
                }
                catch (Exception e)
                {
                    Log.Error(string.Format("Error while notifying SchedulerListener of paused trigger/group.  Triger={0}.{1}", group, name), e);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners resumed trigger.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="group">The group.</param>
        public virtual void NotifySchedulerListenersResumedTrigger(string name, string group)
        {
            // build a list of all job listeners that are to be notified...
            IList schedListeners = SchedulerListeners;

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    sl.TriggersResumed(name, group);
                }
                catch (Exception e)
                {
                    Log.Error(string.Format("Error while notifying SchedulerListener of resumed trigger/group.  Triger={0}.{1}", group, name), e);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about paused job.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="group">The group.</param>
        public virtual void NotifySchedulerListenersPausedJob(string name, string group)
        {
            // build a list of all job listeners that are to be notified...
            IList schedListeners = SchedulerListeners;

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    sl.JobsPaused(name, group);
                }
                catch (Exception e)
                {
                    Log.Error(string.Format("Error while notifying SchedulerListener of paused job/group.  Job={0}.{1}", group, name), e);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about resumed job.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="group">The group.</param>
        public virtual void NotifySchedulerListenersResumedJob(string name, string group)
        {
            // build a list of all job listeners that are to be notified...
            IList schedListeners = SchedulerListeners;

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    sl.JobsResumed(name, group);
                }
                catch (Exception e)
                {
                    Log.Error(string.Format("Error while notifying SchedulerListener of resumed job/group.  Job={0}.{1}", group, name), e);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about scheduler shutdown.
        /// </summary>
        public virtual void NotifySchedulerListenersShutdown()
        {
            // build a list of all job listeners that are to be notified...
            IList schedListeners = SchedulerListeners;

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    sl.SchedulerShutdown();
                }
                catch (Exception e)
                {
                    Log.Error("Error while notifying SchedulerListener of Shutdown.", e);
                }
            }
        }


        /// <summary>
        /// Interrupt all instances of the identified InterruptableJob.
        /// </summary>
        public virtual bool Interrupt(SchedulingContext ctxt, string jobName, string groupName)
        {
            if (groupName == null)
            {
                groupName = SchedulerConstants.DEFAULT_GROUP;
            }

            IList jobs = CurrentlyExecutingJobs;

            JobDetail jobDetail;

            bool interrupted = false;

            foreach (JobExecutionContext jec in jobs)
            {
                jobDetail = jec.JobDetail;
                if (jobName.Equals(jobDetail.Name) && groupName.Equals(jobDetail.Group))
                {
                    IJob job = jec.JobInstance;
                    if (job is IInterruptableJob)
                    {
                        ((IInterruptableJob)job).Interrupt();
                        interrupted = true;
                    }
                    else
                    {
                        throw new UnableToInterruptJobException(string.Format("Job '{0}' of group '{1}' can not be interrupted, since it does not implement {2}", jobName, groupName, typeof(IInterruptableJob).FullName));
                    }
                }
            }

            return interrupted;
        }

        private void ShutdownPlugins()
        {
            foreach (ISchedulerPlugin plugin in resources.SchedulerPlugins)
            {
                plugin.Shutdown();
            }
        }

        private void StartPlugins()
        {
            foreach (ISchedulerPlugin plugin in resources.SchedulerPlugins)
            {
                plugin.Start();
            }
        }
    }

    /// <summary>
    /// ErrorLogger - Scheduler Listener Class
    /// </summary>
    internal class ErrorLogger : SchedulerListenerSupport
    {
        public override void SchedulerError(string msg, SchedulerException cause)
        {
            Log.Error(msg, cause);
        }
    }

    /////////////////////////////////////////////////////////////////////////////
    //
    // ExecutingJobsManager - Job Listener Class
    //
    /////////////////////////////////////////////////////////////////////////////
    internal class ExecutingJobsManager : IJobListener
    {
        public virtual string Name
        {
            get { return GetType().FullName; }
        }

        public virtual int NumJobsCurrentlyExecuting
        {
            get
            {
                lock (executingJobs.SyncRoot)
                {
                    return executingJobs.Count;
                }
            }
        }

        public virtual int NumJobsFired
        {
            get { return numJobsFired; }
        }

        public virtual IList ExecutingJobs
        {
            get
            {
                lock (executingJobs.SyncRoot)
                {
                    return ArrayList.ReadOnly(new ArrayList(new ArrayList(executingJobs.Values)));
                }
            }
        }

        internal IDictionary executingJobs = new Hashtable();

        internal int numJobsFired = 0;

        public virtual void JobToBeExecuted(JobExecutionContext context)
        {
            numJobsFired++;

            lock (executingJobs.SyncRoot)
            {
                executingJobs[context.Trigger.FireInstanceId] = context;
            }
        }

        public virtual void JobWasExecuted(JobExecutionContext context, JobExecutionException jobException)
        {
            lock (executingJobs.SyncRoot)
            {
                executingJobs.Remove(context.Trigger.FireInstanceId);
            }
        }

        public virtual void JobExecutionVetoed(JobExecutionContext context)
        {
        }
    }
}
