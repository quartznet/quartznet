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
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting;
using System.Security;
using System.Text;
using System.Threading;

using Common.Logging;

using Quartz.Impl;
using Quartz.Impl.Matchers;
using Quartz.Impl.Triggers;
using Quartz.Listener;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

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
    public class QuartzScheduler : MarshalByRefObject, IRemotableQuartzScheduler
    {
        private readonly ILog log;
        private static readonly Version version;

        private readonly QuartzSchedulerResources resources;

        private readonly QuartzSchedulerThread schedThread;
        private readonly SchedulerContext context = new SchedulerContext();

        private readonly IListenerManager listenerManager = new ListenerManagerImpl();

        private readonly IDictionary<string, IJobListener> internalJobListeners = new Dictionary<string, IJobListener>(10);
        private readonly IDictionary<string, ITriggerListener> internalTriggerListeners = new Dictionary<string, ITriggerListener>(10);
        private readonly IList<ISchedulerListener> internalSchedulerListeners = new List<ISchedulerListener>(10);

        private IJobFactory jobFactory = new PropertySettingJobFactory();
        private readonly ExecutingJobsManager jobMgr;
        private readonly ISchedulerSignaler signaler;
        private readonly Random random = new Random();
        private readonly List<object> holdToPreventGc = new List<object>(5);
        private bool signalOnSchedulingChange = true;
        private volatile bool closed;
        private volatile bool shuttingDown;
        private DateTimeOffset? initialStart;
        private bool boundRemotely;

        /// <summary>
        /// Initializes the <see cref="QuartzScheduler"/> class.
        /// </summary>
        static QuartzScheduler()
        {
            var asm = Assembly.GetAssembly(typeof (QuartzScheduler));

            if (asm != null)
            {
                version = asm.GetName().Version;
            }
        }

        /// <summary>
        /// Gets the version of the Quartz Scheduler.
        /// </summary>
        /// <value>The version.</value>
        public string Version
        {
            get { return version.ToString(); }
        }

        /// <summary>
        /// Gets the version major.
        /// </summary>
        /// <value>The version major.</value>
        public static string VersionMajor
        {
            get { return version.Major.ToString(CultureInfo.InvariantCulture); }
        }

        /// <summary>
        /// Gets the version minor.
        /// </summary>
        /// <value>The version minor.</value>
        public static string VersionMinor
        {
            get { return version.Minor.ToString(CultureInfo.InvariantCulture); }
        }

        /// <summary>
        /// Gets the version iteration.
        /// </summary>
        /// <value>The version iteration.</value>
        public static string VersionIteration
        {
            get { return version.Build.ToString(CultureInfo.InvariantCulture); }
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
        /// 	<c>true</c> if scheduler should signal on scheduling change; otherwise, <c>false</c>.
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

        public virtual bool IsShuttingDown
        {
            get { return shuttingDown; }
        }

        public virtual bool IsStarted
        {
            get { return !shuttingDown && !closed && !InStandbyMode && initialStart != null; }
        }

        /// <summary>
        /// Return a list of <see cref="IJobExecutionContext" /> objects that
        /// represent all currently executing Jobs in this Scheduler instance.
        /// <para>
        /// This method is not cluster aware.  That is, it will only return Jobs
        /// currently executing in this Scheduler instance, not across the entire
        /// cluster.
        /// </para>
        /// <para>
        /// Note that the list returned is an 'instantaneous' snap-shot, and that as
        /// soon as it's returned, the true list of executing jobs may be different.
        /// </para>
        /// </summary>
        public virtual IList<IJobExecutionContext> CurrentlyExecutingJobs
        {
            get { return jobMgr.ExecutingJobs; }
        }

        /// <summary>
        /// Register the given <see cref="ISchedulerListener" /> with the
        /// <see cref="IScheduler" />'s list of internal listeners.
        /// </summary>
        /// <param name="schedulerListener"></param>
        public void AddInternalSchedulerListener(ISchedulerListener schedulerListener)
        {
            lock (internalSchedulerListeners)
            {
                internalSchedulerListeners.Add(schedulerListener);
            }
        }

        /// <summary>
        /// Remove the given <see cref="ISchedulerListener" /> from the
        /// <see cref="IScheduler" />'s list of internal listeners.
        /// </summary>
        /// <param name="schedulerListener"></param>
        /// <returns>true if the identified listener was found in the list, andremoved.</returns>
        public bool RemoveInternalSchedulerListener(ISchedulerListener schedulerListener)
        {
            lock (internalSchedulerListeners)
            {
                return internalSchedulerListeners.Remove(schedulerListener);
            }
        }

        /// <summary>
        /// Get a List containing all of the <i>internal</i> <see cref="ISchedulerListener" />s
        /// registered with the <see cref="IScheduler" />.
        /// </summary>
        public IList<ISchedulerListener> InternalSchedulerListeners
        {
            get
            {
                lock (internalSchedulerListeners)
                {
                    return new List<ISchedulerListener>(internalSchedulerListeners).AsReadOnly();
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

                log.Info("JobFactory set to: " + value);

                jobFactory = value;
            }
        }

        // ReSharper disable once MemberCanBePrivate.Global
        protected QuartzScheduler()
        {
            log = LogManager.GetLogger(GetType());
        }

        /// <summary>
        /// Create a <see cref="QuartzScheduler" /> with the given configuration
        /// properties.
        /// </summary>
        /// <seealso cref="QuartzSchedulerResources" />
        public QuartzScheduler(QuartzSchedulerResources resources, TimeSpan idleWaitTime) : this()
        {
            this.resources = resources;

            if (resources.JobStore is IJobListener)
            {
                AddInternalJobListener((IJobListener) resources.JobStore);
            }

            schedThread = new QuartzSchedulerThread(this, resources);
            IThreadExecutor schedThreadExecutor = resources.ThreadExecutor;
            schedThreadExecutor.Execute(schedThread);

            if (idleWaitTime > TimeSpan.Zero)
            {
                schedThread.IdleWaitTime = idleWaitTime;
            }

            jobMgr = new ExecutingJobsManager();
            AddInternalJobListener(jobMgr);
            var errLogger = new ErrorLogger();
            AddInternalSchedulerListener(errLogger);

            signaler = new SchedulerSignalerImpl(this, schedThread);

            log.InfoFormat(CultureInfo.InvariantCulture, "Quartz Scheduler v.{0} created.", Version);
        }

        public void Initialize()
        {
            try
            {
                Bind();
            }
            catch (Exception re)
            {
                throw new SchedulerException(
                    "Unable to bind scheduler to remoting.", re);
            }

            log.Info("Scheduler meta-data: " +
                     (new SchedulerMetaData(SchedulerName, SchedulerInstanceId, GetType(), boundRemotely, RunningSince != null,
                         InStandbyMode, IsShutdown, RunningSince,
                         NumJobsExecuted, JobStoreClass,
                         SupportsPersistence, Clustered, ThreadPoolClass,
                         ThreadPoolSize, Version)));
        }

        /// <summary>
        /// Bind the scheduler to remoting infrastructure.
        /// </summary>
        private void Bind()
        {
            if (resources.SchedulerExporter != null)
            {
                resources.SchedulerExporter.Bind(this);
                boundRemotely = true;
            }
        }

        /// <summary>
        /// Un-bind the scheduler from remoting infrastructure.
        /// </summary>
        private void UnBind()
        {
            if (resources.SchedulerExporter != null)
            {
                resources.SchedulerExporter.UnBind(this);
            }
        }

        /// <summary>
        /// Adds an object that should be kept as reference to prevent
        /// it from being garbage collected.
        /// </summary>
        /// <param name="obj">The obj.</param>
        public virtual void AddNoGCObject(object obj)
        {
            holdToPreventGc.Add(obj);
        }

        /// <summary>
        /// Removes the object from garbage collection protected list.
        /// </summary>
        /// <param name="obj">The obj.</param>
        /// <returns></returns>
        public virtual bool RemoveNoGCObject(object obj)
        {
            return holdToPreventGc.Remove(obj);
        }

        /// <summary>
        /// Starts the <see cref="QuartzScheduler" />'s threads that fire <see cref="ITrigger" />s.
        /// <para>
        /// All <see cref="ITrigger" />s that have misfired will
        /// be passed to the appropriate TriggerListener(s).
        /// </para>
        /// </summary>
        public virtual void Start()
        {
            if (shuttingDown || closed)
            {
                throw new SchedulerException("The Scheduler cannot be restarted after Shutdown() has been called.");
            }

            NotifySchedulerListenersStarting();

            if (!initialStart.HasValue)
            {
                initialStart = SystemTime.UtcNow();
                resources.JobStore.SchedulerStarted();
                StartPlugins();
            }
            else
            {
                resources.JobStore.SchedulerResumed();
            }

            schedThread.TogglePause(false);

            log.Info(string.Format(CultureInfo.InvariantCulture, "Scheduler {0} started.", resources.GetUniqueIdentifier()));

            NotifySchedulerListenersStarted();
        }

        public virtual void StartDelayed(TimeSpan delay)
        {
            if (shuttingDown || closed)
            {
                throw new SchedulerException(
                    "The Scheduler cannot be restarted after Shutdown() has been called.");
            }

            DelayedSchedulerStarter starter = new DelayedSchedulerStarter(this, delay, log);
            Thread t = new Thread(starter.Run);
            t.Start();
        }

        /// <summary>
        /// Helper class to start scheduler in a delayed fashion.
        /// </summary>
        private class DelayedSchedulerStarter
        {
            private readonly QuartzScheduler scheduler;
            private readonly TimeSpan delay;
            private readonly ILog logger;

            public DelayedSchedulerStarter(QuartzScheduler scheduler, TimeSpan delay, ILog logger)
            {
                this.scheduler = scheduler;
                this.delay = delay;
                this.logger = logger;
            }

            public void Run()
            {
                try
                {
                    Thread.Sleep(delay);
                }
                catch (ThreadInterruptedException)
                {
                }
                try
                {
                    scheduler.Start();
                }
                catch (SchedulerException se)
                {
                    logger.Error("Unable to start scheduler after startup delay.", se);
                }
            }
        }

        /// <summary>
        /// Temporarily halts the <see cref="QuartzScheduler" />'s firing of <see cref="ITrigger" />s.
        /// <para>
        /// The scheduler is not destroyed, and can be re-started at any time.
        /// </para>
        /// </summary>
        public virtual void Standby()
        {
            resources.JobStore.SchedulerPaused();
            schedThread.TogglePause(true);
            log.Info(string.Format(CultureInfo.InvariantCulture, "Scheduler {0} paused.", resources.GetUniqueIdentifier()));
            NotifySchedulerListenersInStandbyMode();
        }

        /// <summary>
        /// Gets the running since.
        /// </summary>
        /// <value>The running since.</value>
        public virtual DateTimeOffset? RunningSince
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

        public virtual bool Clustered
        {
            get { return resources.JobStore.Clustered; }
        }

        /// <summary>
        /// Halts the <see cref="QuartzScheduler" />'s firing of <see cref="ITrigger" />s,
        /// and cleans up all resources associated with the QuartzScheduler.
        /// Equivalent to <see cref="Shutdown(bool)" />.
        /// <para>
        /// The scheduler cannot be re-started.
        /// </para>
        /// </summary>
        public virtual void Shutdown()
        {
            Shutdown(false);
        }

        /// <summary>
        /// Halts the <see cref="QuartzScheduler" />'s firing of <see cref="ITrigger" />s,
        /// and cleans up all resources associated with the QuartzScheduler.
        /// <para>
        /// The scheduler cannot be re-started.
        /// </para>
        /// </summary>
        /// <param name="waitForJobsToComplete">
        /// if <see langword="true" /> the scheduler will not allow this method
        /// to return until all currently executing jobs have completed.
        /// </param>
        public virtual void Shutdown(bool waitForJobsToComplete)
        {
            if (shuttingDown || closed)
            {
                return;
            }

            shuttingDown = true;

            log.InfoFormat(CultureInfo.InvariantCulture, "Scheduler {0} shutting down.", resources.GetUniqueIdentifier());

            Standby();

            schedThread.Halt(waitForJobsToComplete);

            NotifySchedulerListenersShuttingdown();

            if ((resources.InterruptJobsOnShutdown && !waitForJobsToComplete) || (resources.InterruptJobsOnShutdownWithWait && waitForJobsToComplete))
            {
                IList<IJobExecutionContext> jobs = CurrentlyExecutingJobs;
                foreach (IJobExecutionContext job in jobs)
                {
                    IInterruptableJob jobInstance = job.JobInstance as IInterruptableJob;
                    if (jobInstance != null)
                    {
                        try
                        {
                            jobInstance.Interrupt();
                        }
                        catch (Exception ex)
                        {
                            // do nothing, this was just a courtesy effort
                            log.WarnFormat("Encountered error when interrupting job {0} during shutdown: {1}", job.JobDetail.Key, ex);
                        }
                    }
                }
            }

            resources.ThreadPool.Shutdown(waitForJobsToComplete);

            // Scheduler thread may have be waiting for the fire time of an acquired 
            // trigger and need time to release the trigger once halted, so make sure
            // the thread is dead before continuing to shutdown the job store.
            try
            {
                schedThread.Join();
            }
            catch (ThreadInterruptedException)
            {
            }

            closed = true;

            if (boundRemotely)
            {
                try
                {
                    UnBind();
                }
                catch (RemotingException)
                {
                }
            }

            ShutdownPlugins();

            resources.JobStore.Shutdown();

            NotifySchedulerListenersShutdown();

            SchedulerRepository.Instance.Remove(resources.Name);

            holdToPreventGc.Clear();

            log.Info(string.Format(CultureInfo.InvariantCulture, "Scheduler {0} Shutdown complete.", resources.GetUniqueIdentifier()));
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
        /// <see cref="IJobDetail" /> to the Scheduler, and
        /// associate the given <see cref="ITrigger" /> with it.
        /// <para>
        /// If the given Trigger does not reference any <see cref="IJob" />, then it
        /// will be set to reference the Job passed with it into this method.
        /// </para>
        /// </summary>
        public virtual DateTimeOffset ScheduleJob(IJobDetail jobDetail, ITrigger trigger)
        {
            ValidateState();

            if (jobDetail == null)
            {
                throw new SchedulerException("JobDetail cannot be null");
            }

            if (trigger == null)
            {
                throw new SchedulerException("Trigger cannot be null");
            }

            if (jobDetail.Key == null)
            {
                throw new SchedulerException("Job's key cannot be null");
            }

            if (jobDetail.JobType == null)
            {
                throw new SchedulerException("Job's class cannot be null");
            }

            IOperableTrigger trig = (IOperableTrigger) trigger;

            if (trigger.JobKey == null)
            {
                trig.JobKey = jobDetail.Key;
            }
            else if (!trigger.JobKey.Equals(jobDetail.Key))
            {
                throw new SchedulerException("Trigger does not reference given job!");
            }

            trig.Validate();

            ICalendar cal = null;
            if (trigger.CalendarName != null)
            {
                cal = resources.JobStore.RetrieveCalendar(trigger.CalendarName);
                if (cal == null)
                {
                    throw new SchedulerException(string.Format(CultureInfo.InvariantCulture, "Calendar not found: {0}", trigger.CalendarName));
                }
            }

            DateTimeOffset? ft = trig.ComputeFirstFireTimeUtc(cal);

            if (!ft.HasValue)
            {
                var message = string.Format("Based on configured schedule, the given trigger '{0}' will never fire.", trigger.Key);
                throw new SchedulerException(message);
            }

            resources.JobStore.StoreJobAndTrigger(jobDetail, trig);
            NotifySchedulerListenersJobAdded(jobDetail);
            NotifySchedulerThread(trigger.GetNextFireTimeUtc());
            NotifySchedulerListenersScheduled(trigger);

            return ft.Value;
        }

        /// <summary>
        /// Schedule the given <see cref="ITrigger" /> with the
        /// <see cref="IJob" /> identified by the <see cref="ITrigger" />'s settings.
        /// </summary>
        public virtual DateTimeOffset ScheduleJob(ITrigger trigger)
        {
            ValidateState();

            if (trigger == null)
            {
                throw new SchedulerException("Trigger cannot be null");
            }

            IOperableTrigger trig = (IOperableTrigger) trigger;
            trig.Validate();

            ICalendar cal = null;
            if (trigger.CalendarName != null)
            {
                cal = resources.JobStore.RetrieveCalendar(trigger.CalendarName);
                if (cal == null)
                {
                    throw new SchedulerException(string.Format(CultureInfo.InvariantCulture, "Calendar not found: {0}", trigger.CalendarName));
                }
            }

            DateTimeOffset? ft = trig.ComputeFirstFireTimeUtc(cal);

            if (!ft.HasValue)
            {
                var message = string.Format("Based on configured schedule, the given trigger '{0}' will never fire.", trigger.Key);
                throw new SchedulerException(message);
            }

            resources.JobStore.StoreTrigger(trig, false);
            NotifySchedulerThread(trigger.GetNextFireTimeUtc());
            NotifySchedulerListenersScheduled(trigger);

            return ft.Value;
        }

        /// <summary>
        /// Add the given <see cref="IJob" /> to the Scheduler - with no associated
        /// <see cref="ITrigger" />. The <see cref="IJob" /> will be 'dormant' until
        /// it is scheduled with a <see cref="ITrigger" />, or <see cref="IScheduler.TriggerJob(Quartz.JobKey)" />
        /// is called for it.
        /// <para>
        /// The <see cref="IJob" /> must by definition be 'durable', if it is not,
        /// SchedulerException will be thrown.
        /// </para>
        /// </summary>
        public virtual void AddJob(IJobDetail jobDetail, bool replace)
        {
            AddJob(jobDetail, replace, false);
        }

        public virtual void AddJob(IJobDetail jobDetail, bool replace, bool storeNonDurableWhileAwaitingScheduling)
        {
            ValidateState();

            if (!storeNonDurableWhileAwaitingScheduling && !jobDetail.Durable)
            {
                throw new SchedulerException("Jobs added with no trigger must be durable.");
            }

            resources.JobStore.StoreJob(jobDetail, replace);
            NotifySchedulerThread(null);
            NotifySchedulerListenersJobAdded(jobDetail);
        }

        /// <summary>
        /// Delete the identified <see cref="IJob" /> from the Scheduler - and any
        /// associated <see cref="ITrigger" />s.
        /// </summary>
        /// <returns> true if the Job was found and deleted.</returns>
        public virtual bool DeleteJob(JobKey jobKey)
        {
            ValidateState();

            bool result = false;
            IList<ITrigger> triggers = GetTriggersOfJob(jobKey);
            foreach (ITrigger trigger in triggers)
            {
                if (!UnscheduleJob(trigger.Key))
                {
                    StringBuilder sb = new StringBuilder()
                        .Append("Unable to unschedule trigger [")
                        .Append(trigger.Key).Append("] while deleting job [")
                        .Append(jobKey).Append("]");
                    throw new SchedulerException(sb.ToString());
                }
                result = true;
            }

            result = resources.JobStore.RemoveJob(jobKey) || result;
            if (result)
            {
                NotifySchedulerThread(null);
                NotifySchedulerListenersJobDeleted(jobKey);
            }
            return result;
        }

        public virtual bool DeleteJobs(IList<JobKey> jobKeys)
        {
            ValidateState();

            bool result = resources.JobStore.RemoveJobs(jobKeys);
            NotifySchedulerThread(null);
            foreach (JobKey key in jobKeys)
            {
                NotifySchedulerListenersJobDeleted(key);
            }
            return result;
        }

        public virtual void ScheduleJobs(IDictionary<IJobDetail, Collection.ISet<ITrigger>> triggersAndJobs, bool replace)
        {
            ValidateState();

            // make sure all triggers refer to their associated job
            foreach (IJobDetail job in triggersAndJobs.Keys)
            {
                if (job == null) // there can be one of these (for adding a bulk set of triggers for pre-existing jobs)
                {
                    continue;
                }
                Collection.ISet<ITrigger> triggers = triggersAndJobs[job];
                if (triggers == null) // this is possible because the job may be durable, and not yet be having triggers
                {
                    continue;
                }
                foreach (IOperableTrigger trigger in triggers)
                {
                    trigger.JobKey = job.Key;

                    trigger.Validate();

                    ICalendar cal = null;
                    if (trigger.CalendarName != null)
                    {
                        cal = resources.JobStore.RetrieveCalendar(trigger.CalendarName);
                        if (cal == null)
                        {
                            throw new SchedulerException(
                                "Calendar '" + trigger.CalendarName + "' not found for trigger: " + trigger.Key);
                        }
                    }

                    DateTimeOffset? ft = trigger.ComputeFirstFireTimeUtc(cal);

                    if (ft == null)
                    {
                        var message = string.Format("Based on configured schedule, the given trigger '{0}' will never fire.", trigger.Key);
                        throw new SchedulerException(message);
                    }
                }
            }

            resources.JobStore.StoreJobsAndTriggers(triggersAndJobs, replace);
            NotifySchedulerThread(null);
            foreach (IJobDetail job in triggersAndJobs.Keys)
            {
                NotifySchedulerListenersJobAdded(job);
            }
        }

        public virtual void ScheduleJob(IJobDetail jobDetail, Collection.ISet<ITrigger> triggersForJob, bool replace)
        {
            var triggersAndJobs = new Dictionary<IJobDetail, Collection.ISet<ITrigger>>();
            triggersAndJobs.Add(jobDetail, triggersForJob);
            ScheduleJobs(triggersAndJobs, replace);
        }

        public virtual bool UnscheduleJobs(IList<TriggerKey> triggerKeys)
        {
            ValidateState();

            bool result = resources.JobStore.RemoveTriggers(triggerKeys);
            NotifySchedulerThread(null);
            foreach (TriggerKey key in triggerKeys)
            {
                NotifySchedulerListenersUnscheduled(key);
            }
            return result;
        }

        /// <summary>
        /// Remove the indicated <see cref="ITrigger" /> from the
        /// scheduler.
        /// </summary>
        public virtual bool UnscheduleJob(TriggerKey triggerKey)
        {
            ValidateState();

            if (resources.JobStore.RemoveTrigger(triggerKey))
            {
                NotifySchedulerThread(null);
                NotifySchedulerListenersUnscheduled(triggerKey);
            }
            else
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Remove (delete) the <see cref="ITrigger" /> with the
        /// given name, and store the new given one - which must be associated
        /// with the same job.
        /// </summary>
        /// <param name="triggerKey">the key of the trigger</param>
        /// <param name="newTrigger">The new <see cref="ITrigger" /> to be stored.</param>
        /// <returns>
        /// 	<see langword="null" /> if a <see cref="ITrigger" /> with the given
        /// name and group was not found and removed from the store, otherwise
        /// the first fire time of the newly scheduled trigger.
        /// </returns>
        public virtual DateTimeOffset? RescheduleJob(TriggerKey triggerKey, ITrigger newTrigger)
        {
            ValidateState();

            if (triggerKey == null)
            {
                throw new ArgumentException("triggerKey cannot be null");
            }
            if (newTrigger == null)
            {
                throw new ArgumentException("newTrigger cannot be null");
            }

            var trigger = (IOperableTrigger) newTrigger;
            ITrigger oldTrigger = GetTrigger(triggerKey);
            if (oldTrigger == null)
            {
                return null;
            }

            trigger.JobKey = oldTrigger.JobKey;
            trigger.Validate();

            ICalendar cal = null;
            if (newTrigger.CalendarName != null)
            {
                cal = resources.JobStore.RetrieveCalendar(newTrigger.CalendarName);
            }

            DateTimeOffset? ft;
            if (trigger.GetNextFireTimeUtc() != null)
            {
                // use a cloned trigger so that we don't lose possible forcefully set next fire time
                var clonedTrigger = (IOperableTrigger) trigger.Clone();
                ft = clonedTrigger.ComputeFirstFireTimeUtc(cal);
            }
            else
            {
                ft = trigger.ComputeFirstFireTimeUtc(cal);
            }

            if (!ft.HasValue)
            {
                var message = string.Format("Based on configured schedule, the given trigger '{0}' will never fire.", trigger.Key);
                throw new SchedulerException(message);
            }

            if (resources.JobStore.ReplaceTrigger(triggerKey, trigger))
            {
                NotifySchedulerThread(newTrigger.GetNextFireTimeUtc());
                NotifySchedulerListenersUnscheduled(triggerKey);
                NotifySchedulerListenersScheduled(newTrigger);
            }
            else
            {
                return null;
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
            return "MT_" + Convert.ToString(r, CultureInfo.InvariantCulture);
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

            return temporaryLong;
        }

        /// <summary>
        /// Trigger the identified <see cref="IJob" /> (Execute it now) - with a non-volatile trigger.
        /// </summary>
        public virtual void TriggerJob(JobKey jobKey, JobDataMap data)
        {
            ValidateState();

            // TODO: use builder
            IOperableTrigger trig = new SimpleTriggerImpl(
                NewTriggerId(), SchedulerConstants.DefaultGroup, jobKey.Name, jobKey.Group, SystemTime.UtcNow(), null, 0, TimeSpan.Zero);

            trig.ComputeFirstFireTimeUtc(null);
            if (data != null)
            {
                trig.JobDataMap = data;
            }

            bool collision = true;
            while (collision)
            {
                try
                {
                    resources.JobStore.StoreTrigger(trig, false);
                    collision = false;
                }
                catch (ObjectAlreadyExistsException)
                {
                    trig.Key = new TriggerKey(NewTriggerId(), SchedulerConstants.DefaultGroup);
                }
            }

            NotifySchedulerThread(trig.GetNextFireTimeUtc());
            NotifySchedulerListenersScheduled(trig);
        }

        /// <summary>
        /// Store and schedule the identified <see cref="IOperableTrigger"/>
        /// </summary>
        /// <param name="trig"></param>
        public virtual void TriggerJob(IOperableTrigger trig)
        {
            ValidateState();

            trig.ComputeFirstFireTimeUtc(null);

            bool collision = true;
            while (collision)
            {
                try
                {
                    resources.JobStore.StoreTrigger(trig, false);
                    collision = false;
                }
                catch (ObjectAlreadyExistsException)
                {
                    trig.Key = new TriggerKey(NewTriggerId(), SchedulerConstants.DefaultGroup);
                }
            }

            NotifySchedulerThread(trig.GetNextFireTimeUtc());
            NotifySchedulerListenersScheduled(trig);
        }

        /// <summary>
        /// Pause the <see cref="ITrigger" /> with the given name.
        /// </summary>
        public virtual void PauseTrigger(TriggerKey triggerKey)
        {
            ValidateState();

            resources.JobStore.PauseTrigger(triggerKey);
            NotifySchedulerThread(null);
            NotifySchedulerListenersPausedTrigger(triggerKey);
        }

        /// <summary>
        /// Pause all of the <see cref="ITrigger" />s in the given group.
        /// </summary>
        public virtual void PauseTriggers(GroupMatcher<TriggerKey> matcher)
        {
            ValidateState();

            if (matcher == null)
            {
                matcher = GroupMatcher<TriggerKey>.GroupEquals(SchedulerConstants.DefaultGroup);
            }

            ICollection<string> pausedGroups = resources.JobStore.PauseTriggers(matcher);
            NotifySchedulerThread(null);
            foreach (string pausedGroup in pausedGroups)
            {
                NotifySchedulerListenersPausedTriggers(pausedGroup);
            }
        }

        /// <summary> 
        /// Pause the <see cref="IJobDetail" /> with the given
        /// name - by pausing all of its current <see cref="ITrigger" />s.
        /// </summary>
        public virtual void PauseJob(JobKey jobKey)
        {
            ValidateState();

            resources.JobStore.PauseJob(jobKey);
            NotifySchedulerThread(null);
            NotifySchedulerListenersPausedJob(jobKey);
        }

        /// <summary>
        /// Pause all of the <see cref="IJobDetail" />s in the
        /// given group - by pausing all of their <see cref="ITrigger" />s.
        /// </summary>
        public virtual void PauseJobs(GroupMatcher<JobKey> groupMatcher)
        {
            ValidateState();

            if (groupMatcher == null)
            {
                groupMatcher = GroupMatcher<JobKey>.GroupEquals(SchedulerConstants.DefaultGroup);
            }

            ICollection<string> pausedGroups = resources.JobStore.PauseJobs(groupMatcher);
            NotifySchedulerThread(null);
            foreach (string pausedGroup in pausedGroups)
            {
                NotifySchedulerListenersPausedJobs(pausedGroup);
            }
        }

        /// <summary>
        /// Resume (un-pause) the <see cref="ITrigger" /> with the given
        /// name.
        /// <para>
        /// If the <see cref="ITrigger" /> missed one or more fire-times, then the
        /// <see cref="ITrigger" />'s misfire instruction will be applied.
        /// </para>
        /// </summary>
        public virtual void ResumeTrigger(TriggerKey triggerKey)
        {
            ValidateState();

            resources.JobStore.ResumeTrigger(triggerKey);
            NotifySchedulerThread(null);
            NotifySchedulerListenersResumedTrigger(triggerKey);
        }

        /// <summary>
        /// Resume (un-pause) all of the <see cref="ITrigger" />s in the
        /// matching groups.
        /// <para>
        /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
        /// <see cref="ITrigger" />'s misfire instruction will be applied.
        /// </para>
        /// </summary>
        public virtual void ResumeTriggers(GroupMatcher<TriggerKey> matcher)
        {
            ValidateState();

            if (matcher == null)
            {
                matcher = GroupMatcher<TriggerKey>.GroupEquals(SchedulerConstants.DefaultGroup);
            }

            ICollection<string> pausedGroups = resources.JobStore.ResumeTriggers(matcher);
            NotifySchedulerThread(null);
            foreach (string pausedGroup in pausedGroups)
            {
                NotifySchedulerListenersResumedTriggers(pausedGroup);
            }
        }

        /// <summary>
        /// Gets the paused trigger groups.
        /// </summary>
        /// <returns></returns>
        public virtual Collection.ISet<string> GetPausedTriggerGroups()
        {
            return resources.JobStore.GetPausedTriggerGroups();
        }

        /// <summary>
        /// Resume (un-pause) the <see cref="IJobDetail" /> with
        /// the given name.
        /// <para>
        /// If any of the <see cref="IJob" />'s<see cref="ITrigger" /> s missed one
        /// or more fire-times, then the <see cref="ITrigger" />'s misfire
        /// instruction will be applied.
        /// </para>
        /// </summary>
        public virtual void ResumeJob(JobKey jobKey)
        {
            ValidateState();

            resources.JobStore.ResumeJob(jobKey);
            NotifySchedulerThread(null);
            NotifySchedulerListenersResumedJob(jobKey);
        }

        /// <summary>
        /// Resume (un-pause) all of the <see cref="IJobDetail" />s
        /// in the matching groups.
        /// <para>
        /// If any of the <see cref="IJob" /> s had <see cref="ITrigger" /> s that
        /// missed one or more fire-times, then the <see cref="ITrigger" />'s
        /// misfire instruction will be applied.
        /// </para>
        /// </summary>
        public virtual void ResumeJobs(GroupMatcher<JobKey> matcher)
        {
            ValidateState();

            if (matcher == null)
            {
                matcher = GroupMatcher<JobKey>.GroupEquals(SchedulerConstants.DefaultGroup);
            }

            ICollection<string> resumedGroups = resources.JobStore.ResumeJobs(matcher);
            NotifySchedulerThread(null);
            foreach (string pausedGroup in resumedGroups)
            {
                NotifySchedulerListenersResumedJobs(pausedGroup);
            }
        }

        /// <summary>
        /// Pause all triggers - equivalent of calling <see cref="PauseTriggers" />
        /// with a matcher matching all known groups.
        /// <para>
        /// When <see cref="ResumeAll" /> is called (to un-pause), trigger misfire
        /// instructions WILL be applied.
        /// </para>
        /// </summary>
        /// <seealso cref="ResumeAll()" />
        /// <seealso cref="PauseJob" />
        public virtual void PauseAll()
        {
            ValidateState();

            resources.JobStore.PauseAll();
            NotifySchedulerThread(null);
            NotifySchedulerListenersPausedTriggers(null);
        }

        /// <summary>
        /// Resume (un-pause) all triggers - equivalent of calling <see cref="ResumeTriggers" />
        /// on every group.
        /// <para>
        /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
        /// <see cref="ITrigger" />'s misfire instruction will be applied.
        /// </para>
        /// </summary>
        /// <seealso cref="PauseAll()" />
        public virtual void ResumeAll()
        {
            ValidateState();

            resources.JobStore.ResumeAll();
            NotifySchedulerThread(null);
            NotifySchedulerListenersResumedTriggers(null);
        }

        /// <summary>
        /// Get the names of all known <see cref="IJob" /> groups.
        /// </summary>
        public virtual IList<string> GetJobGroupNames()
        {
            ValidateState();

            return resources.JobStore.GetJobGroupNames();
        }

        /// <summary>
        /// Get the names of all the <see cref="IJob" />s in the
        /// given group.
        /// </summary>
        public virtual Collection.ISet<JobKey> GetJobKeys(GroupMatcher<JobKey> matcher)
        {
            ValidateState();

            if (matcher == null)
            {
                matcher = GroupMatcher<JobKey>.GroupEquals(SchedulerConstants.DefaultGroup);
            }

            return resources.JobStore.GetJobKeys(matcher);
        }

        /// <summary> 
        /// Get all <see cref="ITrigger" /> s that are associated with the
        /// identified <see cref="IJobDetail" />.
        /// </summary>
        public virtual IList<ITrigger> GetTriggersOfJob(JobKey jobKey)
        {
            ValidateState();

            IList<IOperableTrigger> triggersForJob = resources.JobStore.GetTriggersForJob(jobKey);

            var retValue = new List<ITrigger>(triggersForJob.Count);
            foreach (var trigger in triggersForJob)
            {
                retValue.Add(trigger);
            }
            return retValue;
        }

        /// <summary>
        /// Get the names of all known <see cref="ITrigger" />
        /// groups.
        /// </summary>
        public virtual IList<string> GetTriggerGroupNames()
        {
            ValidateState();
            return resources.JobStore.GetTriggerGroupNames();
        }

        /// <summary>
        /// Get the names of all the <see cref="ITrigger" />s in
        /// the matching groups.
        /// </summary>
        public virtual Collection.ISet<TriggerKey> GetTriggerKeys(GroupMatcher<TriggerKey> matcher)
        {
            ValidateState();

            if (matcher == null)
            {
                matcher = GroupMatcher<TriggerKey>.GroupEquals(SchedulerConstants.DefaultGroup);
            }

            return resources.JobStore.GetTriggerKeys(matcher);
        }

        /// <summary> 
        /// Get the <see cref="IJobDetail" /> for the <see cref="IJob" />
        /// instance with the given name and group.
        /// </summary>
        public virtual IJobDetail GetJobDetail(JobKey jobKey)
        {
            ValidateState();

            return resources.JobStore.RetrieveJob(jobKey);
        }

        /// <summary>
        /// Get the <see cref="ITrigger" /> instance with the given name and
        /// group.
        /// </summary>
        public virtual ITrigger GetTrigger(TriggerKey triggerKey)
        {
            ValidateState();

            return resources.JobStore.RetrieveTrigger(triggerKey);
        }

        /// <summary>
        /// Determine whether a <see cref="IJob"/> with the given identifier already
        /// exists within the scheduler.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="jobKey">the identifier to check for</param>
        /// <returns>true if a Job exists with the given identifier</returns>
        public virtual bool CheckExists(JobKey jobKey)
        {
            ValidateState();

            return resources.JobStore.CheckExists(jobKey);
        }

        /// <summary>
        /// Determine whether a <see cref="ITrigger" /> with the given identifier already
        /// exists within the scheduler.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="triggerKey">the identifier to check for</param>
        /// <returns>true if a Trigger exists with the given identifier</returns>
        public virtual bool CheckExists(TriggerKey triggerKey)
        {
            ValidateState();

            return resources.JobStore.CheckExists(triggerKey);
        }

        /// <summary>
        /// Clears (deletes!) all scheduling data - all <see cref="IJob"/>s, <see cref="ITrigger" />s
        /// <see cref="ICalendar" />s.
        /// </summary>
        public virtual void Clear()
        {
            ValidateState();

            resources.JobStore.ClearAllSchedulingData();
            NotifySchedulerListenersUnscheduled(null);
        }

        /// <summary>
        /// Get the current state of the identified <see cref="ITrigger" />.  
        /// </summary>
        /// <seealso cref="TriggerState" />
        public virtual TriggerState GetTriggerState(TriggerKey triggerKey)
        {
            ValidateState();

            return resources.JobStore.GetTriggerState(triggerKey);
        }

        /// <summary>
        /// Add (register) the given <see cref="ICalendar" /> to the Scheduler.
        /// </summary>
        public virtual void AddCalendar(string calName, ICalendar calendar, bool replace,
            bool updateTriggers)
        {
            ValidateState();
            resources.JobStore.StoreCalendar(calName, calendar, replace, updateTriggers);
        }

        /// <summary>
        /// Delete the identified <see cref="ICalendar" /> from the Scheduler.
        /// </summary>
        /// <returns> true if the Calendar was found and deleted.</returns>
        public virtual bool DeleteCalendar(string calName)
        {
            ValidateState();
            return resources.JobStore.RemoveCalendar(calName);
        }

        /// <summary> 
        /// Get the <see cref="ICalendar" /> instance with the given name.
        /// </summary>
        public virtual ICalendar GetCalendar(string calName)
        {
            ValidateState();
            return resources.JobStore.RetrieveCalendar(calName);
        }

        /// <summary>
        /// Get the names of all registered <see cref="ICalendar" />s.
        /// </summary>
        public virtual IList<string> GetCalendarNames()
        {
            ValidateState();
            return resources.JobStore.GetCalendarNames();
        }

        public IListenerManager ListenerManager
        {
            get { return listenerManager; }
        }

        /// <summary>
        /// Add the given <see cref="IJobListener" /> to the
        /// <see cref="IScheduler" />'s <i>internal</i> list.
        /// </summary>
        /// <param name="jobListener"></param>
        public void AddInternalJobListener(IJobListener jobListener)
        {
            if (jobListener.Name.IsNullOrWhiteSpace())
            {
                throw new ArgumentException("JobListener name cannot be empty.", "jobListener");
            }

            lock (internalJobListeners)
            {
                internalJobListeners[jobListener.Name] = jobListener;
            }
        }

        /// <summary>
        /// Remove the identified <see cref="IJobListener" /> from the <see cref="IScheduler" />'s
        /// list of <i>internal</i> listeners.
        /// </summary>
        /// <param name="name"></param>
        /// <returns>true if the identified listener was found in the list, and removed.</returns>
        public bool RemoveInternalJobListener(string name)
        {
            lock (internalJobListeners)
            {
                return internalJobListeners.Remove(name);
            }
        }

        /// <summary>
        /// Get a List containing all of the <see cref="IJobListener" />s
        /// in the <see cref="IScheduler" />'s <i>internal</i> list.
        /// </summary>
        /// <returns></returns>
        public IList<IJobListener> InternalJobListeners
        {
            get
            {
                lock (internalJobListeners)
                {
                    return new List<IJobListener>(internalJobListeners.Values).AsReadOnly();
                }
            }
        }

        /// <summary>
        /// Get the <i>internal</i> <see cref="IJobListener" />
        /// that has the given name.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public IJobListener GetInternalJobListener(string name)
        {
            lock (internalJobListeners)
            {
                IJobListener listener;
                internalJobListeners.TryGetValue(name, out listener);
                return listener;
            }
        }

        /// <summary>
        /// Add the given <see cref="ITriggerListener" /> to the
        /// <see cref="IScheduler" />'s <i>internal</i> list.
        /// </summary>
        /// <param name="triggerListener"></param>
        public void AddInternalTriggerListener(ITriggerListener triggerListener)
        {
            if (triggerListener.Name.IsNullOrWhiteSpace())
            {
                throw new ArgumentException("TriggerListener name cannot be empty.", "triggerListener");
            }

            lock (internalTriggerListeners)
            {
                internalTriggerListeners[triggerListener.Name] = triggerListener;
            }
        }

        /// <summary>
        /// Remove the identified <see cref="ITriggerListener" /> from the <see cref="IScheduler" />'s
        /// list of <i>internal</i> listeners.
        /// </summary>
        /// <param name="name"></param>
        /// <returns>true if the identified listener was found in the list, and removed.</returns>
        public bool RemoveinternalTriggerListener(string name)
        {
            lock (internalTriggerListeners)
            {
                return internalTriggerListeners.Remove(name);
            }
        }

        /// <summary>
        /// Get a list containing all of the <see cref="ITriggerListener" />s
        /// in the <see cref="IScheduler" />'s <i>internal</i> list.
        /// </summary>
        public IList<ITriggerListener> InternalTriggerListeners
        {
            get
            {
                lock (internalTriggerListeners)
                {
                    return new List<ITriggerListener>(internalTriggerListeners.Values).AsReadOnly();
                }
            }
        }

        /// <summary>
        /// Get the <i>internal</i> <see cref="ITriggerListener" /> that
        /// has the given name.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public ITriggerListener GetInternalTriggerListener(string name)
        {
            lock (internalTriggerListeners)
            {
                ITriggerListener triggerListener;
                internalTriggerListeners.TryGetValue(name, out triggerListener);
                return triggerListener;
            }
        }

        public virtual void NotifyJobStoreJobVetoed(IOperableTrigger trigger, IJobDetail detail, SchedulerInstruction instCode)
        {
            resources.JobStore.TriggeredJobComplete(trigger, detail, instCode);
        }

        /// <summary>
        /// Notifies the job store job complete.
        /// </summary>
        /// <param name="trigger">The trigger.</param>
        /// <param name="detail">The detail.</param>
        /// <param name="instCode">The instruction code.</param>
        public virtual void NotifyJobStoreJobComplete(IOperableTrigger trigger, IJobDetail detail, SchedulerInstruction instCode)
        {
            resources.JobStore.TriggeredJobComplete(trigger, detail, instCode);
        }

        /// <summary>
        /// Notifies the scheduler thread.
        /// </summary>
        protected virtual void NotifySchedulerThread(DateTimeOffset? candidateNewNextFireTimeUtc)
        {
            if (SignalOnSchedulingChange)
            {
                schedThread.SignalSchedulingChange(candidateNewNextFireTimeUtc);
            }
        }

        private IEnumerable<ITriggerListener> BuildTriggerListenerList()
        {
            List<ITriggerListener> listeners = new List<ITriggerListener>();
            listeners.AddRange(ListenerManager.GetTriggerListeners());
            listeners.AddRange(InternalTriggerListeners);
            return listeners;
        }

        private IEnumerable<IJobListener> BuildJobListenerList()
        {
            List<IJobListener> listeners = new List<IJobListener>();
            listeners.AddRange(ListenerManager.GetJobListeners());
            listeners.AddRange(InternalJobListeners);
            return listeners;
        }

        private IList<ISchedulerListener> BuildSchedulerListenerList()
        {
            List<ISchedulerListener> allListeners = new List<ISchedulerListener>();
            allListeners.AddRange(ListenerManager.GetSchedulerListeners());
            allListeners.AddRange(InternalSchedulerListeners);
            return allListeners;
        }

        private bool MatchJobListener(IJobListener listener, JobKey key)
        {
            IList<IMatcher<JobKey>> matchers = ListenerManager.GetJobListenerMatchers(listener.Name);
            if (matchers == null)
            {
                return true;
            }
            foreach (IMatcher<JobKey> matcher in matchers)
            {
                if (matcher.IsMatch(key))
                {
                    return true;
                }
            }
            return false;
        }

        private bool MatchTriggerListener(ITriggerListener listener, TriggerKey key)
        {
            IList<IMatcher<TriggerKey>> matchers = ListenerManager.GetTriggerListenerMatchers(listener.Name);
            if (matchers == null)
            {
                return true;
            }
            return matchers.Any(matcher => matcher.IsMatch(key));
        }

        /// <summary>
        /// Notifies the trigger listeners about fired trigger.
        /// </summary>
        /// <param name="jec">The job execution context.</param>
        /// <returns></returns>
        public virtual bool NotifyTriggerListenersFired(IJobExecutionContext jec)
        {
            bool vetoedExecution = false;

            // build a list of all trigger listeners that are to be notified...
            IEnumerable<ITriggerListener> listeners = BuildTriggerListenerList();

            // notify all trigger listeners in the list
            foreach (ITriggerListener tl in listeners)
            {
                if (!MatchTriggerListener(tl, jec.Trigger.Key))
                {
                    continue;
                }
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
                    SchedulerException se = new SchedulerException(string.Format(CultureInfo.InvariantCulture, "TriggerListener '{0}' threw exception: {1}", tl.Name, e.Message), e);
                    throw se;
                }
            }

            return vetoedExecution;
        }

        /// <summary>
        /// Notifies the trigger listeners about misfired trigger.
        /// </summary>
        /// <param name="trigger">The trigger.</param>
        public virtual void NotifyTriggerListenersMisfired(ITrigger trigger)
        {
            // build a list of all trigger listeners that are to be notified...
            IEnumerable<ITriggerListener> listeners = BuildTriggerListenerList();

            // notify all trigger listeners in the list
            foreach (ITriggerListener tl in listeners)
            {
                if (!MatchTriggerListener(tl, trigger.Key))
                {
                    continue;
                }
                try
                {
                    tl.TriggerMisfired(trigger);
                }
                catch (Exception e)
                {
                    SchedulerException se = new SchedulerException(string.Format(CultureInfo.InvariantCulture, "TriggerListener '{0}' threw exception: {1}", tl.Name, e.Message), e);
                    throw se;
                }
            }
        }

        /// <summary>
        /// Notifies the trigger listeners of completion.
        /// </summary>
        /// <param name="jec">The job execution context.</param>
        /// <param name="instCode">The instruction code to report to triggers.</param>
        public virtual void NotifyTriggerListenersComplete(IJobExecutionContext jec, SchedulerInstruction instCode)
        {
            // build a list of all trigger listeners that are to be notified...
            IEnumerable<ITriggerListener> listeners = BuildTriggerListenerList();

            // notify all trigger listeners in the list
            foreach (ITriggerListener tl in listeners)
            {
                if (!MatchTriggerListener(tl, jec.Trigger.Key))
                {
                    continue;
                }
                try
                {
                    tl.TriggerComplete(jec.Trigger, jec, instCode);
                }
                catch (Exception e)
                {
                    SchedulerException se = new SchedulerException(string.Format(CultureInfo.InvariantCulture, "TriggerListener '{0}' threw exception: {1}", tl.Name, e.Message), e);
                    throw se;
                }
            }
        }

        /// <summary>
        /// Notifies the job listeners about job to be executed.
        /// </summary>
        /// <param name="jec">The jec.</param>
        public virtual void NotifyJobListenersToBeExecuted(IJobExecutionContext jec)
        {
            // build a list of all job listeners that are to be notified...
            IEnumerable<IJobListener> listeners = BuildJobListenerList();

            // notify all job listeners
            foreach (IJobListener jl in listeners)
            {
                if (!MatchJobListener(jl, jec.JobDetail.Key))
                {
                    continue;
                }
                try
                {
                    jl.JobToBeExecuted(jec);
                }
                catch (Exception e)
                {
                    SchedulerException se = new SchedulerException(string.Format(CultureInfo.InvariantCulture, "JobListener '{0}' threw exception: {1}", jl.Name, e.Message), e);
                    throw se;
                }
            }
        }

        /// <summary>
        /// Notifies the job listeners that job execution was vetoed.
        /// </summary>
        /// <param name="jec">The job execution context.</param>
        public virtual void NotifyJobListenersWasVetoed(IJobExecutionContext jec)
        {
            // build a list of all job listeners that are to be notified...
            IEnumerable<IJobListener> listeners = BuildJobListenerList();

            // notify all job listeners
            foreach (IJobListener jl in listeners)
            {
                if (!MatchJobListener(jl, jec.JobDetail.Key))
                {
                    continue;
                }
                try
                {
                    jl.JobExecutionVetoed(jec);
                }
                catch (Exception e)
                {
                    SchedulerException se = new SchedulerException(string.Format(CultureInfo.InvariantCulture, "JobListener '{0}' threw exception: {1}", jl.Name, e.Message), e);
                    throw se;
                }
            }
        }

        /// <summary>
        /// Notifies the job listeners that job was executed.
        /// </summary>
        /// <param name="jec">The jec.</param>
        /// <param name="je">The je.</param>
        public virtual void NotifyJobListenersWasExecuted(IJobExecutionContext jec, JobExecutionException je)
        {
            // build a list of all job listeners that are to be notified...
            IEnumerable<IJobListener> listeners = BuildJobListenerList();

            // notify all job listeners
            foreach (IJobListener jl in listeners)
            {
                if (!MatchJobListener(jl, jec.JobDetail.Key))
                {
                    continue;
                }
                try
                {
                    jl.JobWasExecuted(jec, je);
                }
                catch (Exception e)
                {
                    SchedulerException se = new SchedulerException(string.Format(CultureInfo.InvariantCulture, "JobListener '{0}' threw exception: {1}", jl.Name, e.Message), e);
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
            IList<ISchedulerListener> schedListeners = BuildSchedulerListenerList();

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    sl.SchedulerError(msg, se);
                }
                catch (Exception e)
                {
                    log.Error("Error while notifying SchedulerListener of error: ", e);
                    log.Error("  Original error (for notification) was: " + msg, se);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about job that was scheduled.
        /// </summary>
        /// <param name="trigger">The trigger.</param>
        public virtual void NotifySchedulerListenersScheduled(ITrigger trigger)
        {
            // build a list of all scheduler listeners that are to be notified...
            IList<ISchedulerListener> schedListeners = BuildSchedulerListenerList();

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    sl.JobScheduled(trigger);
                }
                catch (Exception e)
                {
                    log.Error(string.Format(CultureInfo.InvariantCulture, "Error while notifying SchedulerListener of scheduled job. Trigger={0}", trigger.Key), e);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about job that was unscheduled.
        /// </summary>
        public virtual void NotifySchedulerListenersUnscheduled(TriggerKey triggerKey)
        {
            // build a list of all scheduler listeners that are to be notified...
            IList<ISchedulerListener> schedListeners = BuildSchedulerListenerList();

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    if (triggerKey == null)
                    {
                        sl.SchedulingDataCleared();
                    }
                    else
                    {
                        sl.JobUnscheduled(triggerKey);
                    }
                }
                catch (Exception e)
                {
                    log.ErrorFormat(
                        CultureInfo.InvariantCulture,
                        "Error while notifying SchedulerListener of unscheduled job. Trigger={0}",
                        e,
                        (triggerKey == null ? "ALL DATA" : triggerKey.ToString()));
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about finalized trigger.
        /// </summary>
        /// <param name="trigger">The trigger.</param>
        public virtual void NotifySchedulerListenersFinalized(ITrigger trigger)
        {
            // build a list of all scheduler listeners that are to be notified...
            IList<ISchedulerListener> schedListeners = BuildSchedulerListenerList();

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    sl.TriggerFinalized(trigger);
                }
                catch (Exception e)
                {
                    log.Error(string.Format(CultureInfo.InvariantCulture, "Error while notifying SchedulerListener of finalized trigger. Trigger={0}", trigger.Key), e);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about paused trigger.
        /// </summary>
        /// <param name="group">The group.</param>
        public virtual void NotifySchedulerListenersPausedTriggers(string group)
        {
            // build a list of all job listeners that are to be notified...
            IList<ISchedulerListener> schedListeners = BuildSchedulerListenerList();

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    sl.TriggersPaused(group);
                }
                catch (Exception e)
                {
                    log.Error(string.Format(CultureInfo.InvariantCulture, "Error while notifying SchedulerListener of paused group: {0}", group), e);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about paused trigger.
        /// </summary>
        public virtual void NotifySchedulerListenersPausedTrigger(TriggerKey triggerKey)
        {
            // build a list of all job listeners that are to be notified...
            IList<ISchedulerListener> schedListeners = BuildSchedulerListenerList();

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    sl.TriggerPaused(triggerKey);
                }
                catch (Exception e)
                {
                    log.ErrorFormat(CultureInfo.InvariantCulture, "Error while notifying SchedulerListener of paused trigger. Trigger={0}", e, triggerKey);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners resumed trigger.
        /// </summary>
        /// <param name="group">The group.</param>
        public virtual void NotifySchedulerListenersResumedTriggers(string group)
        {
            // build a list of all job listeners that are to be notified...
            IList<ISchedulerListener> schedListeners = BuildSchedulerListenerList();

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    sl.TriggersResumed(group);
                }
                catch (Exception e)
                {
                    log.Error(string.Format(CultureInfo.InvariantCulture, "Error while notifying SchedulerListener of resumed group: {0}", group), e);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners resumed trigger.
        /// </summary>
        public virtual void NotifySchedulerListenersResumedTrigger(TriggerKey triggerKey)
        {
            // build a list of all job listeners that are to be notified...
            IList<ISchedulerListener> schedListeners = BuildSchedulerListenerList();

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    sl.TriggerResumed(triggerKey);
                }
                catch (Exception e)
                {
                    log.Error(string.Format(CultureInfo.InvariantCulture, "Error while notifying SchedulerListener of resumed trigger. Trigger={0}", triggerKey), e);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about paused job.
        /// </summary>
        public virtual void NotifySchedulerListenersPausedJob(JobKey jobKey)
        {
            // build a list of all job listeners that are to be notified...
            IList<ISchedulerListener> schedListeners = BuildSchedulerListenerList();

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    sl.JobPaused(jobKey);
                }
                catch (Exception e)
                {
                    log.Error(string.Format(CultureInfo.InvariantCulture, "Error while notifying SchedulerListener of paused job. Job={0}", jobKey), e);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about paused job.
        /// </summary>
        /// <param name="group">The group.</param>
        public virtual void NotifySchedulerListenersPausedJobs(string group)
        {
            // build a list of all job listeners that are to be notified...
            IList<ISchedulerListener> schedListeners = BuildSchedulerListenerList();

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    sl.JobsPaused(group);
                }
                catch (Exception e)
                {
                    log.Error(string.Format(CultureInfo.InvariantCulture, "Error while notifying SchedulerListener of paused group: {0}", group), e);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about resumed job.
        /// </summary>
        public virtual void NotifySchedulerListenersResumedJob(JobKey jobKey)
        {
            // build a list of all job listeners that are to be notified...
            IList<ISchedulerListener> schedListeners = BuildSchedulerListenerList();

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    sl.JobResumed(jobKey);
                }
                catch (Exception e)
                {
                    log.Error(string.Format(CultureInfo.InvariantCulture, "Error while notifying SchedulerListener of resumed job: {0}", jobKey), e);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about resumed job.
        /// </summary>
        /// <param name="group">The group.</param>
        public virtual void NotifySchedulerListenersResumedJobs(string group)
        {
            // build a list of all job listeners that are to be notified...
            IList<ISchedulerListener> schedListeners = BuildSchedulerListenerList();

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    sl.JobsResumed(group);
                }
                catch (Exception e)
                {
                    log.Error(string.Format(CultureInfo.InvariantCulture, "Error while notifying SchedulerListener of resumed group: {0}", group), e);
                }
            }
        }

        public virtual void NotifySchedulerListenersInStandbyMode()
        {
            // notify all scheduler listeners
            foreach (ISchedulerListener listener in BuildSchedulerListenerList())
            {
                try
                {
                    listener.SchedulerInStandbyMode();
                }
                catch (Exception e)
                {
                    log.Error("Error while notifying SchedulerListener of inStandByMode.", e);
                }
            }
        }

        public virtual void NotifySchedulerListenersStarted()
        {
            // notify all scheduler listeners
            foreach (ISchedulerListener listener in BuildSchedulerListenerList())
            {
                try
                {
                    listener.SchedulerStarted();
                }
                catch (Exception e)
                {
                    log.Error("Error while notifying SchedulerListener of startup.", e);
                }
            }
        }

        public virtual void NotifySchedulerListenersStarting()
        {
            // build a list of all scheduler listeners that are to be notified...
            IList<ISchedulerListener> schedListeners = BuildSchedulerListenerList();

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    sl.SchedulerStarting();
                }
                catch (Exception e)
                {
                    log.Error("Error while notifying SchedulerListener of scheduler starting.", e);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about scheduler shutdown.
        /// </summary>
        public virtual void NotifySchedulerListenersShutdown()
        {
            // build a list of all job listeners that are to be notified...
            IList<ISchedulerListener> schedListeners = BuildSchedulerListenerList();

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    sl.SchedulerShutdown();
                }
                catch (Exception e)
                {
                    log.Error("Error while notifying SchedulerListener of Shutdown.", e);
                }
            }
        }

        public virtual void NotifySchedulerListenersShuttingdown()
        {
            // build a list of all job listeners that are to be notified...
            IList<ISchedulerListener> schedListeners = BuildSchedulerListenerList();

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    sl.SchedulerShuttingdown();
                }
                catch (Exception e)
                {
                    log.Error("Error while notifying SchedulerListener of shutdown.", e);
                }
            }
        }

        public virtual void NotifySchedulerListenersJobAdded(IJobDetail jobDetail)
        {
            // notify all scheduler listeners
            foreach (ISchedulerListener listener in BuildSchedulerListenerList())
            {
                try
                {
                    listener.JobAdded(jobDetail);
                }
                catch (Exception e)
                {
                    log.Error("Error while notifying SchedulerListener of JobAdded.", e);
                }
            }
        }

        public virtual void NotifySchedulerListenersJobDeleted(JobKey jobKey)
        {
            // notify all scheduler listeners
            foreach (ISchedulerListener listener in BuildSchedulerListenerList())
            {
                try
                {
                    listener.JobDeleted(jobKey);
                }
                catch (Exception e)
                {
                    log.Error("Error while notifying SchedulerListener of job deletion.", e);
                }
            }
        }

        /// <summary>
        /// Interrupt all instances of the identified InterruptableJob.
        /// </summary>
        public virtual bool Interrupt(JobKey jobKey)
        {
            IList<IJobExecutionContext> jobs = CurrentlyExecutingJobs;

            bool interrupted = false;

            foreach (IJobExecutionContext jec in jobs)
            {
                var jobDetail = jec.JobDetail;
                if (jobKey.Equals(jobDetail.Key))
                {
                    IInterruptableJob jobInstance = jec.JobInstance as IInterruptableJob;
                    if (jobInstance != null)
                    {
                        jobInstance.Interrupt();
                        interrupted = true;
                    }
                    else
                    {
                        throw new UnableToInterruptJobException(string.Format(CultureInfo.InvariantCulture, "Job '{0}' can not be interrupted, since it does not implement {1}", jobDetail.Key, typeof (IInterruptableJob).FullName));
                    }
                }
            }

            return interrupted;
        }

        /// <summary>
        /// Interrupt all instances of the identified InterruptableJob executing in this Scheduler instance.
        /// </summary>
        /// <remarks>
        /// This method is not cluster aware.  That is, it will only interrupt 
        /// instances of the identified InterruptableJob currently executing in this 
        /// Scheduler instance, not across the entire cluster.
        /// </remarks>
        /// <seealso cref="IRemotableQuartzScheduler.Interrupt(JobKey)" />
        /// <param name="fireInstanceId"></param>
        /// <returns></returns>
        public bool Interrupt(string fireInstanceId)
        {
            IList<IJobExecutionContext> jobs = CurrentlyExecutingJobs;

            foreach (IJobExecutionContext jec in jobs)
            {
                if (jec.FireInstanceId.Equals(fireInstanceId))
                {
                    IInterruptableJob jobInstance = jec.JobInstance as IInterruptableJob;
                    if (jobInstance != null)
                    {
                        jobInstance.Interrupt();
                        return true;
                    }
                    throw new UnableToInterruptJobException("Job " + jec.JobDetail.Key + " can not be interrupted, since it does not implement " + typeof (IInterruptableJob).Name);
                }
            }

            return false;
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

        public virtual bool IsJobGroupPaused(string groupName)
        {
            return resources.JobStore.IsJobGroupPaused(groupName);
        }

        public virtual bool IsTriggerGroupPaused(string groupName)
        {
            return resources.JobStore.IsTriggerGroupPaused(groupName);
        }

        ///<summary>
        ///Obtains a lifetime service object to control the lifetime policy for this instance.
        ///</summary>
        [SecurityCritical]
        public override object InitializeLifetimeService()
        {
            // overridden to initialize null life time service,
            // this basically means that remoting object will live as long
            // as the application lives
            return null;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                var executingJobCount = CurrentlyExecutingJobs.Count;
                if (executingJobCount > 0)
                {
                    log.WarnFormat("disposing scheduler without waiting the currently running jobs (count = {0})", executingJobCount);
                }
                Shutdown(false);
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
                lock (executingJobs)
                {
                    return executingJobs.Count;
                }
            }
        }

        public virtual int NumJobsFired
        {
            get { return numJobsFired; }
        }

        public virtual IList<IJobExecutionContext> ExecutingJobs
        {
            get
            {
                lock (executingJobs)
                {
                    return new List<IJobExecutionContext>(executingJobs.Values).AsReadOnly();
                }
            }
        }

        private readonly Dictionary<string, IJobExecutionContext> executingJobs = new Dictionary<string, IJobExecutionContext>();

        private int numJobsFired;

        public virtual void JobToBeExecuted(IJobExecutionContext context)
        {
            Interlocked.Increment(ref numJobsFired);

            lock (executingJobs)
            {
                executingJobs[((IOperableTrigger) context.Trigger).FireInstanceId] = context;
            }
        }

        public virtual void JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException)
        {
            lock (executingJobs)
            {
                executingJobs.Remove(((IOperableTrigger) context.Trigger).FireInstanceId);
            }
        }

        public virtual void JobExecutionVetoed(IJobExecutionContext context)
        {
        }
    }
}