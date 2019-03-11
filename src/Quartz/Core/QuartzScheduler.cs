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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Impl;
using Quartz.Impl.Matchers;
using Quartz.Impl.Triggers;
using Quartz.Logging;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

#if REMOTING
using System.Runtime.Remoting;
#endif // REMOTING

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
    public class QuartzScheduler :
#if REMOTING
        MarshalByRefObject,
#endif // REMOTING
        IRemotableQuartzScheduler
    {
        private readonly ILog log;
        private static readonly Version version;

        private readonly QuartzSchedulerResources resources;

        private readonly QuartzSchedulerThread schedThread;

        private readonly ConcurrentDictionary<string, IJobListener> internalJobListeners = new ConcurrentDictionary<string, IJobListener>();
        private readonly ConcurrentDictionary<string, ITriggerListener> internalTriggerListeners = new ConcurrentDictionary<string, ITriggerListener>();
        private readonly List<ISchedulerListener> internalSchedulerListeners = new List<ISchedulerListener>(10);

        private IJobFactory jobFactory = new PropertySettingJobFactory();
        private readonly ExecutingJobsManager jobMgr;
        private readonly QuartzRandom random = new QuartzRandom();
        private readonly List<object> holdToPreventGc = new List<object>(5);
        private volatile bool closed;
        private volatile bool shuttingDown;
        private DateTimeOffset? initialStart;
        private bool boundRemotely;

        /// <summary>
        /// Initializes the <see cref="QuartzScheduler"/> class.
        /// </summary>
        static QuartzScheduler()
        {
            var asm = typeof (QuartzScheduler).GetTypeInfo().Assembly;
            version = asm.GetName().Version;
        }

        /// <summary>
        /// Gets the version of the Quartz Scheduler.
        /// </summary>
        /// <value>The version.</value>
        public string Version => version.ToString();

        /// <summary>
        /// Gets the version major.
        /// </summary>
        /// <value>The version major.</value>
        public static string VersionMajor => version.Major.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Gets the version minor.
        /// </summary>
        /// <value>The version minor.</value>
        public static string VersionMinor => version.Minor.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Gets the version iteration.
        /// </summary>
        /// <value>The version iteration.</value>
        public static string VersionIteration => version.Build.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Gets the scheduler signaler.
        /// </summary>
        /// <value>The scheduler signaler.</value>
        public virtual ISchedulerSignaler SchedulerSignaler { get; }

        /// <summary>
        /// Returns the name of the <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual string SchedulerName => resources.Name;

        /// <summary>
        /// Returns the instance Id of the <see cref="QuartzScheduler" />.
        /// </summary>
        public virtual string SchedulerInstanceId => resources.InstanceId;

        /// <summary>
        /// Returns the <see cref="SchedulerContext" /> of the <see cref="IScheduler" />.
        /// </summary>
        public virtual SchedulerContext SchedulerContext { get; } = new SchedulerContext();

        /// <summary>
        /// Gets or sets a value indicating whether to signal on scheduling change.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if scheduler should signal on scheduling change; otherwise, <c>false</c>.
        /// </value>
        public virtual bool SignalOnSchedulingChange { get; set; } = true;

        /// <summary>
        /// Reports whether the <see cref="IScheduler" /> is paused.
        /// </summary>
        public virtual bool InStandbyMode => schedThread.Paused;

        /// <summary>
        /// Gets the job store class.
        /// </summary>
        /// <value>The job store class.</value>
        public virtual Type JobStoreClass => resources.JobStore.GetType();

        /// <summary>
        /// Gets the thread pool class.
        /// </summary>
        /// <value>The thread pool class.</value>
        public virtual Type ThreadPoolClass => resources.ThreadPool.GetType();

        /// <summary>
        /// Gets the size of the thread pool.
        /// </summary>
        /// <value>The size of the thread pool.</value>
        public virtual int ThreadPoolSize => resources.ThreadPool.PoolSize;

        /// <summary>
        /// Reports whether the <see cref="IScheduler" /> has been Shutdown.
        /// </summary>
        public virtual bool IsShutdown => closed;

        public virtual bool IsShuttingDown => shuttingDown;

        public virtual bool IsStarted => !shuttingDown && !closed && !InStandbyMode && initialStart != null;

        /// <summary>
        /// Return a list of <see cref="ICancellableJobExecutionContext" /> objects that
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
        public virtual IReadOnlyCollection<IJobExecutionContext> CurrentlyExecutingJobs => jobMgr.ExecutingJobs;

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
        public IReadOnlyList<ISchedulerListener> InternalSchedulerListeners
        {
            get
            {
                lock (internalSchedulerListeners)
                {
                    return new List<ISchedulerListener>(internalSchedulerListeners);
                }
            }
        }

        /// <summary>
        /// Gets or sets the job factory.
        /// </summary>
        /// <value>The job factory.</value>
        public virtual IJobFactory JobFactory
        {
            get => jobFactory;
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
            log = LogProvider.GetLogger(GetType());
        }

        /// <summary>
        /// Create a <see cref="QuartzScheduler" /> with the given configuration
        /// properties.
        /// </summary>
        /// <seealso cref="QuartzSchedulerResources" />
        public QuartzScheduler(QuartzSchedulerResources resources, TimeSpan idleWaitTime) : this()
        {
            this.resources = resources;

            if (resources.JobStore is IJobListener listener)
            {
                AddInternalJobListener(listener);
            }

            schedThread = new QuartzSchedulerThread(this, resources);
            schedThread.Start();

            if (idleWaitTime > TimeSpan.Zero)
            {
                schedThread.IdleWaitTime = idleWaitTime;
            }

            jobMgr = new ExecutingJobsManager();
            AddInternalJobListener(jobMgr);
            var errLogger = new ErrorLogger();
            AddInternalSchedulerListener(errLogger);

            SchedulerSignaler = new SchedulerSignalerImpl(this, schedThread);

            log.InfoFormat("Quartz Scheduler v.{0} created.", Version);
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
                     new SchedulerMetaData(SchedulerName, SchedulerInstanceId, GetType(), boundRemotely, RunningSince != null,
                         InStandbyMode, IsShutdown, RunningSince,
                         NumJobsExecuted, JobStoreClass,
                         SupportsPersistence, Clustered, ThreadPoolClass,
                         ThreadPoolSize, Version));
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
            resources.SchedulerExporter?.UnBind(this);
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
        public virtual async Task Start(CancellationToken cancellationToken = default)
        {
            if (shuttingDown || closed)
            {
                throw new SchedulerException("The Scheduler cannot be restarted after Shutdown() has been called.");
            }

            await NotifySchedulerListenersStarting(cancellationToken).ConfigureAwait(false);

            if (!initialStart.HasValue)
            {
                initialStart = SystemTime.UtcNow();
                await resources.JobStore.SchedulerStarted(cancellationToken).ConfigureAwait(false);
                await StartPlugins(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await resources.JobStore.SchedulerResumed(cancellationToken).ConfigureAwait(false);
            }

            schedThread.TogglePause(false);

            log.Info($"Scheduler {resources.GetUniqueIdentifier()} started.");

            await NotifySchedulerListenersStarted(cancellationToken).ConfigureAwait(false);
        }

        public virtual Task StartDelayed(
            TimeSpan delay,
            CancellationToken cancellationToken = default)
        {
            if (shuttingDown || closed)
            {
                throw new SchedulerException(
                    "The Scheduler cannot be restarted after Shutdown() has been called.");
            }
            return Task.Run(async () =>
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

                try
                {
                    await Start(cancellationToken).ConfigureAwait(false);
                }
                catch (SchedulerException se)
                {
                    log.ErrorException("Unable to start scheduler after startup delay.", se);
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Temporarily halts the <see cref="QuartzScheduler" />'s firing of <see cref="ITrigger" />s.
        /// <para>
        /// The scheduler is not destroyed, and can be re-started at any time.
        /// </para>
        /// </summary>
        public virtual async Task Standby(CancellationToken cancellationToken = default)
        {
            await resources.JobStore.SchedulerPaused(cancellationToken).ConfigureAwait(false);
            schedThread.TogglePause(true);
            log.Info($"Scheduler {resources.GetUniqueIdentifier()} paused.");
            await NotifySchedulerListenersInStandbyMode(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the running since.
        /// </summary>
        /// <value>The running since.</value>
        public virtual DateTimeOffset? RunningSince => initialStart;

        /// <summary>
        /// Gets the number of jobs executed.
        /// </summary>
        /// <value>The number of jobs executed.</value>
        public virtual int NumJobsExecuted => jobMgr.NumJobsFired;

        /// <summary>
        /// Gets a value indicating whether this scheduler supports persistence.
        /// </summary>
        /// <value><c>true</c> if supports persistence; otherwise, <c>false</c>.</value>
        public virtual bool SupportsPersistence => resources.JobStore.SupportsPersistence;

        public virtual bool Clustered => resources.JobStore.Clustered;

        /// <summary>
        /// Halts the <see cref="QuartzScheduler" />'s firing of <see cref="ITrigger" />s,
        /// and cleans up all resources associated with the QuartzScheduler.
        /// Equivalent to <see cref="Shutdown(bool, CancellationToken)" />.
        /// <para>
        /// The scheduler cannot be re-started.
        /// </para>
        /// </summary>
        public virtual Task Shutdown(CancellationToken cancellationToken = default)
        {
            return Shutdown(false, cancellationToken);
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
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual async Task Shutdown(
            bool waitForJobsToComplete,
            CancellationToken cancellationToken = default)
        {
            if (shuttingDown || closed)
            {
                return;
            }

            shuttingDown = true;

            log.InfoFormat("Scheduler {0} shutting down.", resources.GetUniqueIdentifier());

            await Standby(cancellationToken).ConfigureAwait(false);

            await schedThread.Halt(waitForJobsToComplete).ConfigureAwait(false);

            await NotifySchedulerListenersShuttingdown(cancellationToken).ConfigureAwait(false);

            if (resources.InterruptJobsOnShutdown && !waitForJobsToComplete
                || resources.InterruptJobsOnShutdownWithWait && waitForJobsToComplete)
            {
                var jobs = CurrentlyExecutingJobs.OfType<ICancellableJobExecutionContext>();
                foreach (var job in jobs)
                {
                    job.Cancel();
                }
            }

            resources.ThreadPool.Shutdown(waitForJobsToComplete);

            // Scheduler thread may have be waiting for the fire time of an acquired
            // trigger and need time to release the trigger once halted, so make sure
            // the thread is dead before continuing to shutdown the job store.
            await schedThread.Shutdown().ConfigureAwait(false);

            closed = true;

            if (boundRemotely)
            {
                try
                {
                    UnBind();
                }
#if REMOTING
                catch (RemotingException)
#else // REMOTING
                catch (Exception) // TODO (NetCore Port): Determine the correct exception type
#endif // REMOTING
                {
                }
            }

            await ShutdownPlugins(cancellationToken).ConfigureAwait(false);

            await resources.JobStore.Shutdown(cancellationToken).ConfigureAwait(false);

            await NotifySchedulerListenersShutdown(cancellationToken).ConfigureAwait(false);

            SchedulerRepository.Instance.Remove(resources.Name);

            holdToPreventGc.Clear();

            log.Info($"Scheduler {resources.GetUniqueIdentifier()} Shutdown complete.");
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
        public virtual async Task<DateTimeOffset> ScheduleJob(
            IJobDetail jobDetail,
            ITrigger trigger,
            CancellationToken cancellationToken = default)
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
                cal = await resources.JobStore.RetrieveCalendar(trigger.CalendarName, cancellationToken).ConfigureAwait(false);
                if (cal == null)
                {
                    throw new SchedulerException($"Calendar not found: {trigger.CalendarName}");
                }
            }

            DateTimeOffset? ft = trig.ComputeFirstFireTimeUtc(cal);

            if (!ft.HasValue)
            {
                var message = $"Based on configured schedule, the given trigger '{trigger.Key}' will never fire.";
                throw new SchedulerException(message);
            }

            await resources.JobStore.StoreJobAndTrigger(jobDetail, trig, cancellationToken).ConfigureAwait(false);
            await NotifySchedulerListenersJobAdded(jobDetail, cancellationToken).ConfigureAwait(false);
            NotifySchedulerThread(trigger.GetNextFireTimeUtc());
            await NotifySchedulerListenersScheduled(trigger, cancellationToken).ConfigureAwait(false);

            return ft.Value;
        }

        /// <summary>
        /// Schedule the given <see cref="ITrigger" /> with the
        /// <see cref="IJob" /> identified by the <see cref="ITrigger" />'s settings.
        /// </summary>
        public virtual async Task<DateTimeOffset> ScheduleJob(
            ITrigger trigger,
            CancellationToken cancellationToken = default)
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
                cal = await resources.JobStore.RetrieveCalendar(trigger.CalendarName, cancellationToken).ConfigureAwait(false);
                if (cal == null)
                {
                    throw new SchedulerException($"Calendar not found: {trigger.CalendarName}");
                }
            }

            DateTimeOffset? ft = trig.ComputeFirstFireTimeUtc(cal);

            if (!ft.HasValue)
            {
                var message = $"Based on configured schedule, the given trigger '{trigger.Key}' will never fire.";
                throw new SchedulerException(message);
            }

            await resources.JobStore.StoreTrigger(trig, false, cancellationToken).ConfigureAwait(false);
            NotifySchedulerThread(trigger.GetNextFireTimeUtc());
            await NotifySchedulerListenersScheduled(trigger, cancellationToken).ConfigureAwait(false);

            return ft.Value;
        }

        /// <summary>
        /// Add the given <see cref="IJob" /> to the Scheduler - with no associated
        /// <see cref="ITrigger" />. The <see cref="IJob" /> will be 'dormant' until
        /// it is scheduled with a <see cref="ITrigger" />, or <see cref="IScheduler.TriggerJob(Quartz.JobKey, CancellationToken)" />
        /// is called for it.
        /// <para>
        /// The <see cref="IJob" /> must by definition be 'durable', if it is not,
        /// SchedulerException will be thrown.
        /// </para>
        /// </summary>
        public virtual Task AddJob(
            IJobDetail jobDetail,
            bool replace,
            CancellationToken cancellationToken = default)
        {
            return AddJob(jobDetail, replace, false, cancellationToken);
        }

        public virtual async Task AddJob(
            IJobDetail jobDetail,
            bool replace,
            bool storeNonDurableWhileAwaitingScheduling,
            CancellationToken cancellationToken = default)
        {
            ValidateState();

            if (!storeNonDurableWhileAwaitingScheduling && !jobDetail.Durable)
            {
                throw new SchedulerException("Jobs added with no trigger must be durable.");
            }

            await resources.JobStore.StoreJob(jobDetail, replace, cancellationToken).ConfigureAwait(false);
            NotifySchedulerThread(null);
            await NotifySchedulerListenersJobAdded(jobDetail, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Delete the identified <see cref="IJob" /> from the Scheduler - and any
        /// associated <see cref="ITrigger" />s.
        /// </summary>
        /// <returns> true if the Job was found and deleted.</returns>
        public virtual async Task<bool> DeleteJob(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            ValidateState();

            bool result = false;
            var triggers = await GetTriggersOfJob(jobKey, cancellationToken).ConfigureAwait(false);
            foreach (ITrigger trigger in triggers)
            {
                if (!await UnscheduleJob(trigger.Key, cancellationToken).ConfigureAwait(false))
                {
                    StringBuilder sb = new StringBuilder()
                        .Append("Unable to unschedule trigger [")
                        .Append(trigger.Key).Append("] while deleting job [")
                        .Append(jobKey).Append("]");
                    throw new SchedulerException(sb.ToString());
                }
                result = true;
            }

            result = await resources.JobStore.RemoveJob(jobKey, cancellationToken).ConfigureAwait(false) || result;
            if (result)
            {
                NotifySchedulerThread(null);
                await NotifySchedulerListenersJobDeleted(jobKey, cancellationToken).ConfigureAwait(false);
            }
            return result;
        }

        public virtual async Task<bool> DeleteJobs(
            IReadOnlyCollection<JobKey> jobKeys,
            CancellationToken cancellationToken = default)
        {
            ValidateState();

            bool result = await resources.JobStore.RemoveJobs(jobKeys, cancellationToken).ConfigureAwait(false);
            NotifySchedulerThread(null);
            foreach (JobKey key in jobKeys)
            {
                await NotifySchedulerListenersJobDeleted(key, cancellationToken).ConfigureAwait(false);
            }
            return result;
        }

        public virtual async Task ScheduleJobs(
            IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs,
            bool replace,
            CancellationToken cancellationToken = default)
        {
            ValidateState();

            // make sure all triggers refer to their associated job
            foreach (var pair in triggersAndJobs)
            {
                var job = pair.Key;
                var triggers = pair.Value;
                if (job == null) // there can be one of these (for adding a bulk set of triggers for pre-existing jobs)
                {
                    continue;
                }
                if (triggers == null) // this is possible because the job may be durable, and not yet be having triggers
                {
                    continue;
                }
                foreach (var t in triggers)
                {
                    var trigger = (IOperableTrigger) t;
                    trigger.JobKey = job.Key;

                    trigger.Validate();

                    ICalendar cal = null;
                    if (trigger.CalendarName != null)
                    {
                        cal = await resources.JobStore.RetrieveCalendar(trigger.CalendarName, cancellationToken).ConfigureAwait(false);
                        if (cal == null)
                        {
                            var message = $"Calendar '{trigger.CalendarName}' not found for trigger: {trigger.Key}";
                            throw new SchedulerException(message);
                        }
                    }

                    DateTimeOffset? ft = trigger.ComputeFirstFireTimeUtc(cal);

                    if (ft == null)
                    {
                        var message = $"Based on configured schedule, the given trigger '{trigger.Key}' will never fire.";
                        throw new SchedulerException(message);
                    }
                }
            }

            await resources.JobStore.StoreJobsAndTriggers(triggersAndJobs, replace, cancellationToken).ConfigureAwait(false);
            NotifySchedulerThread(null);
            await Task.WhenAll(triggersAndJobs.Keys.Select(x => NotifySchedulerListenersJobAdded(x, cancellationToken))).ConfigureAwait(false);
            await Task.WhenAll(triggersAndJobs.SelectMany(x => x.Value.Select(t => NotifySchedulerListenersScheduled(t, cancellationToken)))).ConfigureAwait(false);
        }

        public virtual Task ScheduleJob(
            IJobDetail jobDetail,
            IReadOnlyCollection<ITrigger> triggersForJob,
            bool replace,
            CancellationToken cancellationToken = default)
        {
            var triggersAndJobs = new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>>();
            triggersAndJobs.Add(jobDetail, triggersForJob);
            return ScheduleJobs(triggersAndJobs, replace, cancellationToken);
        }

        public virtual async Task<bool> UnscheduleJobs(
            IReadOnlyCollection<TriggerKey> triggerKeys,
            CancellationToken cancellationToken = default)
        {
            ValidateState();

            bool result = await resources.JobStore.RemoveTriggers(triggerKeys, cancellationToken).ConfigureAwait(false);
            NotifySchedulerThread(null);
            await Task.WhenAll(triggerKeys.Select(x => NotifySchedulerListenersUnscheduled(x, cancellationToken))).ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Remove the indicated <see cref="ITrigger" /> from the
        /// scheduler.
        /// </summary>
        public virtual async Task<bool> UnscheduleJob(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            ValidateState();

            if (await resources.JobStore.RemoveTrigger(triggerKey, cancellationToken).ConfigureAwait(false))
            {
                NotifySchedulerThread(null);
                await NotifySchedulerListenersUnscheduled(triggerKey, cancellationToken).ConfigureAwait(false);
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
        /// <param name="cancellationToken">The cancellation instruction.</param>
        /// <returns>
        /// 	<see langword="null" /> if a <see cref="ITrigger" /> with the given
        /// name and group was not found and removed from the store, otherwise
        /// the first fire time of the newly scheduled trigger.
        /// </returns>
        public virtual async Task<DateTimeOffset?> RescheduleJob(
            TriggerKey triggerKey,
            ITrigger newTrigger,
            CancellationToken cancellationToken = default)
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
            ITrigger oldTrigger = await GetTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
            if (oldTrigger == null)
            {
                return null;
            }

            trigger.JobKey = oldTrigger.JobKey;
            trigger.Validate();

            ICalendar cal = null;
            if (newTrigger.CalendarName != null)
            {
                cal = await resources.JobStore.RetrieveCalendar(newTrigger.CalendarName, cancellationToken).ConfigureAwait(false);
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
                var message = $"Based on configured schedule, the given trigger '{trigger.Key}' will never fire.";
                throw new SchedulerException(message);
            }

            if (await resources.JobStore.ReplaceTrigger(triggerKey, trigger, cancellationToken).ConfigureAwait(false))
            {
                NotifySchedulerThread(newTrigger.GetNextFireTimeUtc());
                await NotifySchedulerListenersUnscheduled(triggerKey, cancellationToken).ConfigureAwait(false);
                await NotifySchedulerListenersScheduled(newTrigger, cancellationToken).ConfigureAwait(false);
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
        public static long NextLong(QuartzRandom random)
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
        public virtual async Task TriggerJob(
            JobKey jobKey,
            JobDataMap data,
            CancellationToken cancellationToken = default)
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
                    await resources.JobStore.StoreTrigger(trig, false, cancellationToken).ConfigureAwait(false);
                    collision = false;
                }
                catch (ObjectAlreadyExistsException)
                {
                    trig.Key = new TriggerKey(NewTriggerId(), SchedulerConstants.DefaultGroup);
                }
            }

            NotifySchedulerThread(trig.GetNextFireTimeUtc());
            await NotifySchedulerListenersScheduled(trig, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Store and schedule the identified <see cref="IOperableTrigger"/>
        /// </summary>
        public virtual async Task TriggerJob(
            IOperableTrigger trig,
            CancellationToken cancellationToken = default)
        {
            ValidateState();

            trig.ComputeFirstFireTimeUtc(null);

            bool collision = true;
            while (collision)
            {
                try
                {
                    await resources.JobStore.StoreTrigger(trig, false, cancellationToken).ConfigureAwait(false);
                    collision = false;
                }
                catch (ObjectAlreadyExistsException)
                {
                    trig.Key = new TriggerKey(NewTriggerId(), SchedulerConstants.DefaultGroup);
                }
            }

            NotifySchedulerThread(trig.GetNextFireTimeUtc());
            await NotifySchedulerListenersScheduled(trig, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Pause the <see cref="ITrigger" /> with the given name.
        /// </summary>
        public virtual async Task PauseTrigger(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            ValidateState();

            await resources.JobStore.PauseTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
            NotifySchedulerThread(null);
            await NotifySchedulerListenersPausedTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Pause all of the <see cref="ITrigger" />s in the given group.
        /// </summary>
        public virtual async Task PauseTriggers(
            GroupMatcher<TriggerKey> matcher,
            CancellationToken cancellationToken = default)
        {
            ValidateState();

            if (matcher == null)
            {
                matcher = GroupMatcher<TriggerKey>.GroupEquals(SchedulerConstants.DefaultGroup);
            }

            var pausedGroups = await resources.JobStore.PauseTriggers(matcher, cancellationToken).ConfigureAwait(false);
            NotifySchedulerThread(null);
            await Task.WhenAll(pausedGroups.Select(x => NotifySchedulerListenersPausedTriggers(x, cancellationToken))).ConfigureAwait(false);
        }

        /// <summary>
        /// Pause the <see cref="IJobDetail" /> with the given
        /// name - by pausing all of its current <see cref="ITrigger" />s.
        /// </summary>
        public virtual async Task PauseJob(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            ValidateState();

            await resources.JobStore.PauseJob(jobKey, cancellationToken).ConfigureAwait(false);
            NotifySchedulerThread(null);
            NotifySchedulerListenersPausedJob(jobKey, cancellationToken);
        }

        /// <summary>
        /// Pause all of the <see cref="IJobDetail" />s in the
        /// given group - by pausing all of their <see cref="ITrigger" />s.
        /// </summary>
        public virtual async Task PauseJobs(
            GroupMatcher<JobKey> groupMatcher,
            CancellationToken cancellationToken = default)
        {
            ValidateState();

            if (groupMatcher == null)
            {
                groupMatcher = GroupMatcher<JobKey>.GroupEquals(SchedulerConstants.DefaultGroup);
            }

            var pausedGroups = await resources.JobStore.PauseJobs(groupMatcher, cancellationToken).ConfigureAwait(false);
            NotifySchedulerThread(null);
            await Task.WhenAll(pausedGroups.Select(x => NotifySchedulerListenersPausedJobs(x, cancellationToken))).ConfigureAwait(false);
        }

        /// <summary>
        /// Resume (un-pause) the <see cref="ITrigger" /> with the given
        /// name.
        /// <para>
        /// If the <see cref="ITrigger" /> missed one or more fire-times, then the
        /// <see cref="ITrigger" />'s misfire instruction will be applied.
        /// </para>
        /// </summary>
        public virtual async Task ResumeTrigger(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            ValidateState();

            await resources.JobStore.ResumeTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
            NotifySchedulerThread(null);
            await NotifySchedulerListenersResumedTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Resume (un-pause) all of the <see cref="ITrigger" />s in the
        /// matching groups.
        /// <para>
        /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
        /// <see cref="ITrigger" />'s misfire instruction will be applied.
        /// </para>
        /// </summary>
        public virtual async Task ResumeTriggers(
            GroupMatcher<TriggerKey> matcher,
            CancellationToken cancellationToken = default)
        {
            ValidateState();

            if (matcher == null)
            {
                matcher = GroupMatcher<TriggerKey>.GroupEquals(SchedulerConstants.DefaultGroup);
            }

            var pausedGroups = await resources.JobStore.ResumeTriggers(matcher, cancellationToken).ConfigureAwait(false);
            NotifySchedulerThread(null);
            await Task.WhenAll(pausedGroups.Select(x => NotifySchedulerListenersResumedTriggers(x, cancellationToken))).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the paused trigger groups.
        /// </summary>
        /// <returns></returns>
        public virtual Task<IReadOnlyCollection<string>> GetPausedTriggerGroups(
            CancellationToken cancellationToken = default)
        {
            return resources.JobStore.GetPausedTriggerGroups(cancellationToken);
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
        public virtual async Task ResumeJob(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            ValidateState();

            await resources.JobStore.ResumeJob(jobKey, cancellationToken).ConfigureAwait(false);
            NotifySchedulerThread(null);
            await NotifySchedulerListenersResumedJob(jobKey, cancellationToken).ConfigureAwait(false);
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
        public virtual async Task ResumeJobs(
            GroupMatcher<JobKey> matcher,
            CancellationToken cancellationToken = default)
        {
            ValidateState();

            if (matcher == null)
            {
                matcher = GroupMatcher<JobKey>.GroupEquals(SchedulerConstants.DefaultGroup);
            }

            var resumedGroups = await resources.JobStore.ResumeJobs(matcher, cancellationToken).ConfigureAwait(false);
            NotifySchedulerThread(null);
            await Task.WhenAll(resumedGroups.Select(x => NotifySchedulerListenersResumedJobs(x, cancellationToken))).ConfigureAwait(false);
        }

        /// <summary>
        /// Pause all triggers - equivalent of calling <see cref="PauseTriggers" />
        /// with a matcher matching all known groups.
        /// <para>
        /// When <see cref="ResumeAll" /> is called (to un-pause), trigger misfire
        /// instructions WILL be applied.
        /// </para>
        /// </summary>
        /// <seealso cref="ResumeAll" />
        /// <seealso cref="PauseJob" />
        public virtual async Task PauseAll(CancellationToken cancellationToken = default)
        {
            ValidateState();

            await resources.JobStore.PauseAll(cancellationToken).ConfigureAwait(false);
            NotifySchedulerThread(null);
            await NotifySchedulerListenersPausedTriggers(null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Resume (un-pause) all triggers - equivalent of calling <see cref="ResumeTriggers" />
        /// on every group.
        /// <para>
        /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
        /// <see cref="ITrigger" />'s misfire instruction will be applied.
        /// </para>
        /// </summary>
        /// <seealso cref="PauseAll" />
        public virtual async Task ResumeAll(CancellationToken cancellationToken = default)
        {
            ValidateState();

            await resources.JobStore.ResumeAll(cancellationToken).ConfigureAwait(false);
            NotifySchedulerThread(null);
            await NotifySchedulerListenersResumedTriggers(null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Get the names of all known <see cref="IJob" /> groups.
        /// </summary>
        public virtual Task<IReadOnlyCollection<string>> GetJobGroupNames(
            CancellationToken cancellationToken = default)
        {
            ValidateState();

            return resources.JobStore.GetJobGroupNames(cancellationToken);
        }

        /// <summary>
        /// Get the names of all the <see cref="IJob" />s in the
        /// given group.
        /// </summary>
        public virtual Task<IReadOnlyCollection<JobKey>> GetJobKeys(
            GroupMatcher<JobKey> matcher,
            CancellationToken cancellationToken = default)
        {
            ValidateState();

            if (matcher == null)
            {
                matcher = GroupMatcher<JobKey>.GroupEquals(SchedulerConstants.DefaultGroup);
            }

            return resources.JobStore.GetJobKeys(matcher, cancellationToken);
        }

        /// <summary>
        /// Get all <see cref="ITrigger" /> s that are associated with the
        /// identified <see cref="IJobDetail" />.
        /// </summary>
        public virtual async Task<IReadOnlyCollection<ITrigger>> GetTriggersOfJob(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            ValidateState();

            var triggersForJob = await resources.JobStore.GetTriggersForJob(jobKey, cancellationToken).ConfigureAwait(false);

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
        public virtual Task<IReadOnlyCollection<string>> GetTriggerGroupNames(
            CancellationToken cancellationToken = default)
        {
            ValidateState();
            return resources.JobStore.GetTriggerGroupNames(cancellationToken);
        }

        /// <summary>
        /// Get the names of all the <see cref="ITrigger" />s in
        /// the matching groups.
        /// </summary>
        public virtual Task<IReadOnlyCollection<TriggerKey>> GetTriggerKeys(
            GroupMatcher<TriggerKey> matcher,
            CancellationToken cancellationToken = default)
        {
            ValidateState();

            if (matcher == null)
            {
                matcher = GroupMatcher<TriggerKey>.GroupEquals(SchedulerConstants.DefaultGroup);
            }

            return resources.JobStore.GetTriggerKeys(matcher, cancellationToken);
        }

        /// <summary>
        /// Get the <see cref="IJobDetail" /> for the <see cref="IJob" />
        /// instance with the given name and group.
        /// </summary>
        public virtual Task<IJobDetail> GetJobDetail(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            ValidateState();

            return resources.JobStore.RetrieveJob(jobKey, cancellationToken);
        }

#pragma warning disable AsyncFixer01 // Unnecessary async/await usage
        /// <summary>
        /// Get the <see cref="ITrigger" /> instance with the given name and
        /// group.
        /// </summary>
        public virtual async Task<ITrigger> GetTrigger(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            ValidateState();

            return await resources.JobStore.RetrieveTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
        }
#pragma warning restore AsyncFixer01 // Unnecessary async/await usage

        /// <summary>
        /// Determine whether a <see cref="IJob"/> with the given identifier already
        /// exists within the scheduler.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="jobKey">the identifier to check for</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        /// <returns>true if a Job exists with the given identifier</returns>
        public virtual Task<bool> CheckExists(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            ValidateState();

            return resources.JobStore.CheckExists(jobKey, cancellationToken);
        }

        /// <summary>
        /// Determine whether a <see cref="ITrigger" /> with the given identifier already
        /// exists within the scheduler.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="triggerKey">the identifier to check for</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        /// <returns>true if a Trigger exists with the given identifier</returns>
        public virtual Task<bool> CheckExists(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            ValidateState();

            return resources.JobStore.CheckExists(triggerKey, cancellationToken);
        }

        /// <summary>
        /// Clears (deletes!) all scheduling data - all <see cref="IJob"/>s, <see cref="ITrigger" />s
        /// <see cref="ICalendar" />s.
        /// </summary>
        public virtual async Task Clear(CancellationToken cancellationToken = default)
        {
            ValidateState();

            await resources.JobStore.ClearAllSchedulingData(cancellationToken).ConfigureAwait(false);
            await NotifySchedulerListenersUnscheduled(null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Get the current state of the identified <see cref="ITrigger" />.
        /// </summary>
        /// <seealso cref="TriggerState" />
        public virtual Task<TriggerState> GetTriggerState(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            ValidateState();

            return resources.JobStore.GetTriggerState(triggerKey, cancellationToken);
        }

        /// <summary>
        /// Add (register) the given <see cref="ICalendar" /> to the Scheduler.
        /// </summary>
        public virtual Task AddCalendar(
            string calName,
            ICalendar calendar,
            bool replace,
            bool updateTriggers,
            CancellationToken cancellationToken = default)
        {
            ValidateState();
            return resources.JobStore.StoreCalendar(calName, calendar, replace, updateTriggers, cancellationToken);
        }

        /// <summary>
        /// Delete the identified <see cref="ICalendar" /> from the Scheduler.
        /// </summary>
        /// <returns> true if the Calendar was found and deleted.</returns>
        public virtual Task<bool> DeleteCalendar(
            string calName,
            CancellationToken cancellationToken = default)
        {
            ValidateState();
            return resources.JobStore.RemoveCalendar(calName, cancellationToken);
        }

        /// <summary>
        /// Get the <see cref="ICalendar" /> instance with the given name.
        /// </summary>
        public virtual Task<ICalendar> GetCalendar(
            string calName,
            CancellationToken cancellationToken = default)
        {
            ValidateState();
            return resources.JobStore.RetrieveCalendar(calName, cancellationToken);
        }

        /// <summary>
        /// Get the names of all registered <see cref="ICalendar" />s.
        /// </summary>
        public virtual Task<IReadOnlyCollection<string>> GetCalendarNames(
            CancellationToken cancellationToken = default)
        {
            ValidateState();
            return resources.JobStore.GetCalendarNames(cancellationToken);
        }

        public IListenerManager ListenerManager { get; } = new ListenerManagerImpl();

        /// <summary>
        /// Add the given <see cref="IJobListener" /> to the
        /// <see cref="IScheduler" />'s <i>internal</i> list.
        /// </summary>
        /// <param name="jobListener"></param>
        public void AddInternalJobListener(IJobListener jobListener)
        {
            if (jobListener.Name.IsNullOrWhiteSpace())
            {
                throw new ArgumentException("JobListener name cannot be empty.", nameof(jobListener));
            }
            internalJobListeners[jobListener.Name] = jobListener;
        }

        /// <summary>
        /// Remove the identified <see cref="IJobListener" /> from the <see cref="IScheduler" />'s
        /// list of <i>internal</i> listeners.
        /// </summary>
        /// <param name="name"></param>
        /// <returns>true if the identified listener was found in the list, and removed.</returns>
        public bool RemoveInternalJobListener(string name)
        {
            return internalJobListeners.TryRemove(name, out _);
        }

        /// <summary>
        /// Get a List containing all of the <see cref="IJobListener" />s
        /// in the <see cref="IScheduler" />'s <i>internal</i> list.
        /// </summary>
        /// <returns></returns>
        public IReadOnlyList<IJobListener> InternalJobListeners => new List<IJobListener>(internalJobListeners.Values);

        /// <summary>
        /// Get the <i>internal</i> <see cref="IJobListener" />
        /// that has the given name.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public IJobListener GetInternalJobListener(string name)
        {
            internalJobListeners.TryGetValue(name, out var listener);
            return listener;
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
                throw new ArgumentException("TriggerListener name cannot be empty.", nameof(triggerListener));
            }
            internalTriggerListeners[triggerListener.Name] = triggerListener;
        }

        /// <summary>
        /// Remove the identified <see cref="ITriggerListener" /> from the <see cref="IScheduler" />'s
        /// list of <i>internal</i> listeners.
        /// </summary>
        /// <param name="name"></param>
        /// <returns>true if the identified listener was found in the list, and removed.</returns>
        public bool RemoveinternalTriggerListener(string name)
        {
            return internalTriggerListeners.TryRemove(name, out _);
        }

        /// <summary>
        /// Get a list containing all of the <see cref="ITriggerListener" />s
        /// in the <see cref="IScheduler" />'s <i>internal</i> list.
        /// </summary>
        public IReadOnlyCollection<ITriggerListener> InternalTriggerListeners => new List<ITriggerListener>(internalTriggerListeners.Values);

        /// <summary>
        /// Get the <i>internal</i> <see cref="ITriggerListener" /> that
        /// has the given name.
        /// </summary>
        public ITriggerListener GetInternalTriggerListener(string name)
        {
            return internalTriggerListeners.TryGetAndReturn(name);
        }

        public virtual Task NotifyJobStoreJobVetoed(
            IOperableTrigger trigger,
            IJobDetail detail,
            SchedulerInstruction instCode,
            CancellationToken cancellationToken = default)
        {
            return resources.JobStore.TriggeredJobComplete(trigger, detail, instCode, cancellationToken);
        }

        /// <summary>
        /// Notifies the job store job complete.
        /// </summary>
        public virtual Task NotifyJobStoreJobComplete(
            IOperableTrigger trigger,
            IJobDetail detail,
            SchedulerInstruction instCode,
            CancellationToken cancellationToken = default)
        {
            return resources.JobStore.TriggeredJobComplete(trigger, detail, instCode, cancellationToken);
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

        private List<ITriggerListener> BuildTriggerListenerList()
        {
            var listeners = new List<ITriggerListener>();
            listeners.AddRange(ListenerManager.GetTriggerListeners());
            listeners.AddRange(InternalTriggerListeners);
            return listeners;
        }

        private List<IJobListener> BuildJobListenerList()
        {
            var listeners = new List<IJobListener>();
            listeners.AddRange(ListenerManager.GetJobListeners());
            listeners.AddRange(InternalJobListeners);
            return listeners;
        }

        private List<ISchedulerListener> BuildSchedulerListenerList()
        {
            var allListeners = new List<ISchedulerListener>();
            allListeners.AddRange(ListenerManager.GetSchedulerListeners());
            allListeners.AddRange(InternalSchedulerListeners);
            return allListeners;
        }

        private bool MatchJobListener(IJobListener listener, JobKey key)
        {
            var matchers = ListenerManager.GetJobListenerMatchers(listener.Name);
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
            var matchers = ListenerManager.GetTriggerListenerMatchers(listener.Name);
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
        /// <param name="cancellationToken">The cancellation instruction.</param>
        /// <returns></returns>
        public virtual async Task<bool> NotifyTriggerListenersFired(
            IJobExecutionContext jec,
            CancellationToken cancellationToken = default)
        {
            bool vetoedExecution = false;

            // build a list of all trigger listeners that are to be notified...
            var listeners = BuildTriggerListenerList();

            // notify all trigger listeners in the list
            foreach (ITriggerListener tl in listeners)
            {
                if (!MatchTriggerListener(tl, jec.Trigger.Key))
                {
                    continue;
                }
                try
                {
                    await tl.TriggerFired(jec.Trigger, jec, cancellationToken).ConfigureAwait(false);

                    if (await tl.VetoJobExecution(jec.Trigger, jec, cancellationToken).ConfigureAwait(false))
                    {
                        vetoedExecution = true;
                    }
                }
                catch (Exception e)
                {
                    SchedulerException se = new SchedulerException($"TriggerListener '{tl.Name}' threw exception: {e.Message}", e);
                    throw se;
                }
            }

            return vetoedExecution;
        }

        /// <summary>
        /// Notifies the trigger listeners about misfired trigger.
        /// </summary>
        /// <param name="trigger">The trigger.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual async Task NotifyTriggerListenersMisfired(
            ITrigger trigger,
            CancellationToken cancellationToken = default)
        {
            // build a list of all trigger listeners that are to be notified...
            var listeners = BuildTriggerListenerList();

            // notify all trigger listeners in the list
            foreach (ITriggerListener tl in listeners)
            {
                if (!MatchTriggerListener(tl, trigger.Key))
                {
                    continue;
                }
                try
                {
                    await tl.TriggerMisfired(trigger, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    SchedulerException se = new SchedulerException($"TriggerListener '{tl.Name}' threw exception: {e.Message}", e);
                    throw se;
                }
            }
        }

        /// <summary>
        /// Notifies the trigger listeners of completion.
        /// </summary>
        /// <param name="jec">The job execution context.</param>
        /// <param name="instCode">The instruction code to report to triggers.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual async Task NotifyTriggerListenersComplete(
            IJobExecutionContext jec,
            SchedulerInstruction instCode,
            CancellationToken cancellationToken = default)
        {
            // build a list of all trigger listeners that are to be notified...
            var listeners = BuildTriggerListenerList();

            // notify all trigger listeners in the list
            foreach (ITriggerListener tl in listeners)
            {
                if (!MatchTriggerListener(tl, jec.Trigger.Key))
                {
                    continue;
                }
                try
                {
                    await tl.TriggerComplete(jec.Trigger, jec, instCode, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    SchedulerException se = new SchedulerException($"TriggerListener '{tl.Name}' threw exception: {e.Message}", e);
                    throw se;
                }
            }
        }

        /// <summary>
        /// Notifies the job listeners about job to be executed.
        /// </summary>
        /// <param name="jec">The jec.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual async Task NotifyJobListenersToBeExecuted(
            IJobExecutionContext jec,
            CancellationToken cancellationToken = default)
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
                    await jl.JobToBeExecuted(jec, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    SchedulerException se = new SchedulerException($"JobListener '{jl.Name}' threw exception: {e.Message}", e);
                    throw se;
                }
            }
        }

        /// <summary>
        /// Notifies the job listeners that job execution was vetoed.
        /// </summary>
        /// <param name="jec">The job execution context.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual async Task NotifyJobListenersWasVetoed(
            IJobExecutionContext jec,
            CancellationToken cancellationToken = default)
        {
            // build a list of all job listeners that are to be notified...
            var listeners = BuildJobListenerList();

            // notify all job listeners
            foreach (IJobListener jl in listeners)
            {
                if (!MatchJobListener(jl, jec.JobDetail.Key))
                {
                    continue;
                }
                try
                {
                    await jl.JobExecutionVetoed(jec, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    SchedulerException se = new SchedulerException($"JobListener '{jl.Name}' threw exception: {e.Message}", e);
                    throw se;
                }
            }
        }

        /// <summary>
        /// Notifies the job listeners that job was executed.
        /// </summary>
        /// <param name="jec">The jec.</param>
        /// <param name="je">The je.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual async Task NotifyJobListenersWasExecuted(
            IJobExecutionContext jec,
            JobExecutionException je,
            CancellationToken cancellationToken = default)
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
                    await jl.JobWasExecuted(jec, je, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    SchedulerException se = new SchedulerException($"JobListener '{jl.Name}' threw exception: {e.Message}", e);
                    throw se;
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about scheduler error.
        /// </summary>
        /// <param name="msg">The MSG.</param>
        /// <param name="se">The se.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual async Task NotifySchedulerListenersError(
            string msg,
            SchedulerException se,
            CancellationToken cancellationToken = default)
        {
            // build a list of all scheduler listeners that are to be notified...
            IEnumerable<ISchedulerListener> schedListeners = BuildSchedulerListenerList();

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    await sl.SchedulerError(msg, se, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    log.ErrorException("Error while notifying SchedulerListener of error: ", e);
                    log.ErrorException("  Original error (for notification) was: " + msg, se);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about job that was scheduled.
        /// </summary>
        /// <param name="trigger">The trigger.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual Task NotifySchedulerListenersScheduled(
            ITrigger trigger,
            CancellationToken cancellationToken = default)
        {
            return NotifySchedulerListeners(l => l.JobScheduled(trigger, cancellationToken), $"scheduled job. Trigger={trigger.Key}");
        }

        /// <summary>
        /// Notifies the scheduler listeners about job that was unscheduled.
        /// </summary>
        public virtual async Task NotifySchedulerListenersUnscheduled(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            // build a list of all scheduler listeners that are to be notified...
            IEnumerable<ISchedulerListener> schedListeners = BuildSchedulerListenerList();

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    if (triggerKey == null)
                    {
                        await sl.SchedulingDataCleared(cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await sl.JobUnscheduled(triggerKey, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception e)
                {
                    log.ErrorFormat(
                        "Error while notifying SchedulerListener of unscheduled job. Trigger={0}",
                        e,
                        triggerKey?.ToString() ?? "ALL DATA");
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about finalized trigger.
        /// </summary>
        /// <param name="trigger">The trigger.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual Task NotifySchedulerListenersFinalized(
            ITrigger trigger,
            CancellationToken cancellationToken = default)
        {
            return NotifySchedulerListeners(l => l.TriggerFinalized(trigger, cancellationToken), $"finalized trigger. Trigger={trigger.Key}");
        }

        /// <summary>
        /// Notifies the scheduler listeners about paused trigger.
        /// </summary>
        /// <param name="group">The group.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual async Task NotifySchedulerListenersPausedTriggers(
            string group,
            CancellationToken cancellationToken = default)
        {
            // build a list of all job listeners that are to be notified...
            IEnumerable<ISchedulerListener> schedListeners = BuildSchedulerListenerList();

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    await sl.TriggersPaused(group, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    log.ErrorException($"Error while notifying SchedulerListener of paused group: {group}", e);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about paused trigger.
        /// </summary>
        public virtual async Task NotifySchedulerListenersPausedTrigger(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            // build a list of all job listeners that are to be notified...
            IEnumerable<ISchedulerListener> schedListeners = BuildSchedulerListenerList();

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    await sl.TriggerPaused(triggerKey, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    log.ErrorException($"Error while notifying SchedulerListener of paused trigger. Trigger={triggerKey}", e);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners resumed trigger.
        /// </summary>
        /// <param name="group">The group.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual Task NotifySchedulerListenersResumedTriggers(
            string group,
            CancellationToken cancellationToken = default)
        {
            return NotifySchedulerListeners(l => l.TriggersResumed(group, cancellationToken), $"resumed group: {group}");
        }

        /// <summary>
        /// Notifies the scheduler listeners resumed trigger.
        /// </summary>
        public virtual async Task NotifySchedulerListenersResumedTrigger(
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            // build a list of all job listeners that are to be notified...
            IEnumerable<ISchedulerListener> schedListeners = BuildSchedulerListenerList();

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    await sl.TriggerResumed(triggerKey, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    log.ErrorException($"Error while notifying SchedulerListener of resumed trigger. Trigger={triggerKey}", e);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about paused job.
        /// </summary>
        public virtual void NotifySchedulerListenersPausedJob(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            // build a list of all job listeners that are to be notified...
            IEnumerable<ISchedulerListener> schedListeners = BuildSchedulerListenerList();

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    sl.JobPaused(jobKey, cancellationToken);
                }
                catch (Exception e)
                {
                    log.ErrorException($"Error while notifying SchedulerListener of paused job. Job={jobKey}", e);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about paused job.
        /// </summary>
        public virtual async Task NotifySchedulerListenersPausedJobs(
            string group,
            CancellationToken cancellationToken = default)
        {
            // build a list of all job listeners that are to be notified...
            IEnumerable<ISchedulerListener> schedListeners = BuildSchedulerListenerList();

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    await sl.JobsPaused(group, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    log.ErrorException($"Error while notifying SchedulerListener of paused group: {group}", e);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about resumed job.
        /// </summary>
        public virtual async Task NotifySchedulerListenersResumedJob(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            // build a list of all job listeners that are to be notified...
            IEnumerable<ISchedulerListener> schedListeners = BuildSchedulerListenerList();

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    await sl.JobResumed(jobKey, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    log.ErrorException($"Error while notifying SchedulerListener of resumed job: {jobKey}", e);
                }
            }
        }

        /// <summary>
        /// Notifies the scheduler listeners about resumed job.
        /// </summary>
        public virtual async Task NotifySchedulerListenersResumedJobs(
            string group,
            CancellationToken cancellationToken = default)
        {
            // build a list of all job listeners that are to be notified...
            IEnumerable<ISchedulerListener> schedListeners = BuildSchedulerListenerList();

            // notify all scheduler listeners
            foreach (ISchedulerListener sl in schedListeners)
            {
                try
                {
                    await sl.JobsResumed(group, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    log.ErrorException($"Error while notifying SchedulerListener of resumed group: {group}", e);
                }
            }
        }

        public virtual Task NotifySchedulerListenersInStandbyMode(
            CancellationToken cancellationToken = default)
        {
            return NotifySchedulerListeners(l => l.SchedulerInStandbyMode(cancellationToken), "inStandByMode");
        }

        public virtual Task NotifySchedulerListenersStarted(
            CancellationToken cancellationToken = default)
        {
            return NotifySchedulerListeners(l => l.SchedulerStarted(cancellationToken), "startup");
        }

        public virtual Task NotifySchedulerListenersStarting(
            CancellationToken cancellationToken = default)
        {
            return NotifySchedulerListeners(l => l.SchedulerStarting(cancellationToken), "scheduler starting");
        }

        /// <summary>
        /// Notifies the scheduler listeners about scheduler shutdown.
        /// </summary>
        public virtual Task NotifySchedulerListenersShutdown(
            CancellationToken cancellationToken = default)
        {
            return NotifySchedulerListeners(l => l.SchedulerShutdown(cancellationToken), "shutdown");
        }

        public virtual Task NotifySchedulerListenersShuttingdown(
            CancellationToken cancellationToken = default)
        {
            return NotifySchedulerListeners(l => l.SchedulerShuttingdown(cancellationToken), "shutting down");
        }

        public virtual Task NotifySchedulerListenersJobAdded(
            IJobDetail jobDetail,
            CancellationToken cancellationToken = default)
        {
            return NotifySchedulerListeners(l => l.JobAdded(jobDetail, cancellationToken), "job addition");
        }

        public virtual Task NotifySchedulerListenersJobDeleted(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            return NotifySchedulerListeners(l => l.JobDeleted(jobKey, cancellationToken), "job deletion");
        }

        protected virtual async Task NotifySchedulerListeners(
            Func<ISchedulerListener, Task> notifier, string action)
        {
            // notify all scheduler listeners
            var listeners = BuildSchedulerListenerList();
            foreach (var listener in listeners)
            {
                try
                {
                    await notifier(listener).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    log.ErrorException("Error while notifying SchedulerListener of " + action + ".", e);
                }
            }
        }

        /// <summary>
        /// Interrupt all instances of the identified InterruptableJob.
        /// </summary>
        public virtual async Task<bool> Interrupt(
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            var cancellableJobs = CurrentlyExecutingJobs.OfType<ICancellableJobExecutionContext>();

            bool interrupted = false;

            foreach (var cancellableJobExecutionContext in cancellableJobs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var jobDetail = cancellableJobExecutionContext.JobDetail;
                if (jobKey.Equals(jobDetail.Key))
                {
                    cancellableJobExecutionContext.Cancel();
                    interrupted = true;
                    break;
                }
            }

            if (interrupted)
            {
                await NotifySchedulerListeners(l => l.JobInterrupted(jobKey, cancellationToken), "job interruption").ConfigureAwait(false);
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
        /// <param name="cancellationToken">The cancellation instruction.</param>
        /// <returns></returns>
        public Task<bool> Interrupt(
            string fireInstanceId,
            CancellationToken cancellationToken = default)
        {
            var cancellableJobs = CurrentlyExecutingJobs.OfType<ICancellableJobExecutionContext>();

            bool interrupted = false;

            foreach (var cancellableJobExecutionContext in cancellableJobs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (cancellableJobExecutionContext.FireInstanceId.Equals(fireInstanceId))
                {
                    cancellableJobExecutionContext.Cancel();
                    interrupted = true;
                    break;
                }
            }

            return Task.FromResult(interrupted);
        }

        private async Task ShutdownPlugins(
            CancellationToken cancellationToken = default)
        {
            foreach (ISchedulerPlugin plugin in resources.SchedulerPlugins)
            {
                await plugin.Shutdown(cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task StartPlugins(
            CancellationToken cancellationToken = default)
        {
            foreach (ISchedulerPlugin plugin in resources.SchedulerPlugins)
            {
                await plugin.Start(cancellationToken).ConfigureAwait(false);
            }
        }

        public virtual Task<bool> IsJobGroupPaused(
            string groupName,
            CancellationToken cancellationToken = default)
        {
            return resources.JobStore.IsJobGroupPaused(groupName, cancellationToken);
        }

        public virtual Task<bool> IsTriggerGroupPaused(
            string groupName,
            CancellationToken cancellationToken = default)
        {
            return resources.JobStore.IsTriggerGroupPaused(groupName, cancellationToken);
        }

        ///<summary>
        ///Obtains a lifetime service object to control the lifetime policy for this instance.
        ///</summary>
        [SecurityCritical]
        public
#if REMOTING
            override
#else // REMOTING
            virtual
#endif // REMOTING
            object InitializeLifetimeService()
        {
            // overridden to initialize null life time service,
            // this basically means that remoting object will live as long
            // as the application lives
            return null;
        }

        void IRemotableQuartzScheduler.Clear()
        {
            Clear().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        void IRemotableQuartzScheduler.Start()
        {
            Start().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        void IRemotableQuartzScheduler.StartDelayed(TimeSpan delay)
        {
            StartDelayed(delay).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        void IRemotableQuartzScheduler.Standby()
        {
            Standby().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        void IRemotableQuartzScheduler.Shutdown()
        {
            Shutdown().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        void IRemotableQuartzScheduler.Shutdown(bool waitForJobsToComplete)
        {
            Shutdown(waitForJobsToComplete).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        DateTimeOffset IRemotableQuartzScheduler.ScheduleJob(IJobDetail jobDetail, ITrigger trigger)
        {
            return ScheduleJob(jobDetail, trigger).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        DateTimeOffset IRemotableQuartzScheduler.ScheduleJob(ITrigger trigger)
        {
            return ScheduleJob(trigger).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        void IRemotableQuartzScheduler.AddJob(IJobDetail jobDetail, bool replace)
        {
            AddJob(jobDetail, replace).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        void IRemotableQuartzScheduler.AddJob(IJobDetail jobDetail, bool replace, bool storeNonDurableWhileAwaitingScheduling)
        {
            AddJob(jobDetail, replace, storeNonDurableWhileAwaitingScheduling).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        bool IRemotableQuartzScheduler.IsJobGroupPaused(string groupName)
        {
            return IsJobGroupPaused(groupName).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        bool IRemotableQuartzScheduler.IsTriggerGroupPaused(string groupName)
        {
            return IsTriggerGroupPaused(groupName).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        bool IRemotableQuartzScheduler.DeleteJob(JobKey jobKey)
        {
            return DeleteJob(jobKey).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        bool IRemotableQuartzScheduler.UnscheduleJob(TriggerKey triggerKey)
        {
            return UnscheduleJob(triggerKey).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        DateTimeOffset? IRemotableQuartzScheduler.RescheduleJob(TriggerKey triggerKey, ITrigger newTrigger)
        {
            return RescheduleJob(triggerKey, newTrigger).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        void IRemotableQuartzScheduler.TriggerJob(JobKey jobKey, JobDataMap data)
        {
            TriggerJob(jobKey, data).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        void IRemotableQuartzScheduler.TriggerJob(IOperableTrigger trig)
        {
            TriggerJob(trig).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        void IRemotableQuartzScheduler.PauseTrigger(TriggerKey triggerKey)
        {
            PauseTrigger(triggerKey).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        void IRemotableQuartzScheduler.PauseTriggers(GroupMatcher<TriggerKey> matcher)
        {
            PauseTriggers(matcher).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        void IRemotableQuartzScheduler.PauseJob(JobKey jobKey)
        {
            PauseJob(jobKey).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        void IRemotableQuartzScheduler.PauseJobs(GroupMatcher<JobKey> matcher)
        {
            PauseJobs(matcher).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        void IRemotableQuartzScheduler.ResumeTrigger(TriggerKey triggerKey)
        {
            ResumeTrigger(triggerKey).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        void IRemotableQuartzScheduler.ResumeTriggers(GroupMatcher<TriggerKey> matcher)
        {
            ResumeTriggers(matcher).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        IReadOnlyCollection<string> IRemotableQuartzScheduler.GetPausedTriggerGroups()
        {
            return GetPausedTriggerGroups().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        void IRemotableQuartzScheduler.ResumeJob(JobKey jobKey)
        {
            ResumeJob(jobKey).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        void IRemotableQuartzScheduler.ResumeJobs(GroupMatcher<JobKey> matcher)
        {
            ResumeJobs(matcher).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        void IRemotableQuartzScheduler.PauseAll()
        {
            PauseAll().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        void IRemotableQuartzScheduler.ResumeAll()
        {
            ResumeAll().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        IReadOnlyCollection<string> IRemotableQuartzScheduler.GetJobGroupNames()
        {
            return GetJobGroupNames().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        IReadOnlyCollection<JobKey> IRemotableQuartzScheduler.GetJobKeys(GroupMatcher<JobKey> matcher)
        {
            return GetJobKeys(matcher).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        IReadOnlyCollection<ITrigger> IRemotableQuartzScheduler.GetTriggersOfJob(JobKey jobKey)
        {
            return GetTriggersOfJob(jobKey).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        IReadOnlyCollection<string> IRemotableQuartzScheduler.GetTriggerGroupNames()
        {
            return GetTriggerGroupNames().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        IReadOnlyCollection<TriggerKey> IRemotableQuartzScheduler.GetTriggerKeys(GroupMatcher<TriggerKey> matcher)
        {
            return GetTriggerKeys(matcher).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        IJobDetail IRemotableQuartzScheduler.GetJobDetail(JobKey jobKey)
        {
            return GetJobDetail(jobKey).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        ITrigger IRemotableQuartzScheduler.GetTrigger(TriggerKey triggerKey)
        {
            return GetTrigger(triggerKey).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        TriggerState IRemotableQuartzScheduler.GetTriggerState(TriggerKey triggerKey)
        {
            return GetTriggerState(triggerKey).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        void IRemotableQuartzScheduler.AddCalendar(string calName, ICalendar calendar, bool replace, bool updateTriggers)
        {
            AddCalendar(calName, calendar, replace, updateTriggers).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        bool IRemotableQuartzScheduler.DeleteCalendar(string calName)
        {
            return DeleteCalendar(calName).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        ICalendar IRemotableQuartzScheduler.GetCalendar(string calName)
        {
            return GetCalendar(calName).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        IReadOnlyCollection<string> IRemotableQuartzScheduler.GetCalendarNames()
        {
            return GetCalendarNames().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        bool IRemotableQuartzScheduler.Interrupt(JobKey jobKey)
        {
            return Interrupt(jobKey).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        bool IRemotableQuartzScheduler.Interrupt(string fireInstanceId)
        {
            return Interrupt(fireInstanceId).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        bool IRemotableQuartzScheduler.CheckExists(JobKey jobKey)
        {
            return CheckExists(jobKey).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        bool IRemotableQuartzScheduler.CheckExists(TriggerKey triggerKey)
        {
            return CheckExists(triggerKey).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        bool IRemotableQuartzScheduler.DeleteJobs(IReadOnlyCollection<JobKey> jobKeys)
        {
            return DeleteJobs(jobKeys).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        void IRemotableQuartzScheduler.ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, bool replace)
        {
            ScheduleJobs(triggersAndJobs, replace).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        void IRemotableQuartzScheduler.ScheduleJob(IJobDetail jobDetail, IReadOnlyCollection<ITrigger> triggersForJob, bool replace)
        {
            ScheduleJob(jobDetail, triggersForJob, replace).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        bool IRemotableQuartzScheduler.UnscheduleJobs(IReadOnlyCollection<TriggerKey> triggerKeys)
        {
            return UnscheduleJobs(triggerKeys).ConfigureAwait(false).GetAwaiter().GetResult();
        }
    }
}