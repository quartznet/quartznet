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

#pragma warning disable CA2012

using System.Globalization;
using System.Text;

using Microsoft.Extensions.Logging;

using Quartz.Impl.Matchers;
using Quartz.Impl.Triggers;
using Quartz.Diagnostics;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Core;

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
public sealed class QuartzScheduler
{
    private readonly ILogger<QuartzScheduler> logger;
    private static readonly Version version;

    internal readonly QuartzSchedulerResources resources = null!;

    internal readonly QuartzSchedulerThread schedThread = null!;
    private readonly List<ISchedulerListener> internalSchedulerListeners = new List<ISchedulerListener>(10);

    private IJobFactory jobFactory = new PropertySettingJobFactory();
    private readonly ExecutingJobsManager jobMgr;
    private readonly List<object> holdToPreventGc = new List<object>(5);
    private volatile bool closed;
    private volatile bool shuttingDown;
    private DateTimeOffset? initialStart;

    /// <summary>
    /// Initializes the <see cref="QuartzScheduler"/> class.
    /// </summary>
    static QuartzScheduler()
    {
        var asm = typeof(QuartzScheduler).Assembly;
        version = asm.GetName().Version!;
    }

    /// <summary>
    /// Gets the version of the Quartz Scheduler.
    /// </summary>
    /// <value>The version.</value>
#pragma warning disable CA1822 // Mark members as static
    public string Version => version.ToString();
#pragma warning restore CA1822

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
    public ISchedulerSignaler SchedulerSignaler { get; } = null!;

    /// <summary>
    /// Returns the name of the <see cref="QuartzScheduler" />.
    /// </summary>
    public string SchedulerName => resources.Name;

    /// <summary>
    /// Returns the instance Id of the <see cref="QuartzScheduler" />.
    /// </summary>
    public string SchedulerInstanceId => resources.InstanceId;

    /// <summary>
    /// Returns the <see cref="SchedulerContext" /> of the <see cref="IScheduler" />.
    /// </summary>
    public SchedulerContext SchedulerContext { get; } = new SchedulerContext();

    /// <summary>
    /// Gets or sets a value indicating whether to signal on scheduling change.
    /// </summary>
    /// <value>
    /// 	<c>true</c> if scheduler should signal on scheduling change; otherwise, <c>false</c>.
    /// </value>
    public bool SignalOnSchedulingChange { get; set; } = true;

    /// <summary>
    /// Reports whether the <see cref="IScheduler" /> is paused.
    /// </summary>
    public bool InStandbyMode => schedThread.Paused;

    /// <summary>
    /// Gets the job store class.
    /// </summary>
    /// <value>The job store class.</value>
    public Type JobStoreClass => resources.JobStore.GetType();

    /// <summary>
    /// Gets the thread pool class.
    /// </summary>
    /// <value>The thread pool class.</value>
    public Type ThreadPoolClass => resources.ThreadPool.GetType();

    /// <summary>
    /// Gets the size of the thread pool.
    /// </summary>
    /// <value>The size of the thread pool.</value>
    public int ThreadPoolSize => resources.ThreadPool.PoolSize;

    /// <summary>
    /// Reports whether the <see cref="IScheduler" /> has been Shutdown.
    /// </summary>
    public bool IsShutdown => closed;

    public bool IsShuttingDown => shuttingDown;

    public bool IsStarted => !shuttingDown && !closed && !InStandbyMode && initialStart is not null;

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
    public List<IJobExecutionContext> GetCurrentlyExecutingJobs() => jobMgr.GetExecutingJobs;

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
    public IJobFactory JobFactory
    {
        get => jobFactory;
        set
        {
            if (value is null)
            {
                ThrowHelper.ThrowArgumentException("JobFactory cannot be set to null!");
            }

            logger.LogInformation("JobFactory set to: {Value}", value);

            jobFactory = value;
        }
    }

    /// <summary>
    /// Create a <see cref="QuartzScheduler" /> with the given configuration
    /// properties.
    /// </summary>
    /// <seealso cref="QuartzSchedulerResources" />
    public QuartzScheduler(QuartzSchedulerResources resources)
    {
        this.resources = resources;

        logger = LogProvider.CreateLogger<QuartzScheduler>();

        schedThread = new QuartzSchedulerThread(this, resources);
        schedThread.Start();

        jobMgr = new ExecutingJobsManager();
        var errLogger = new ErrorLogger();
        AddInternalSchedulerListener(errLogger);

        SchedulerSignaler = new SchedulerSignalerImpl(this, schedThread);

        logger.LogInformation("Quartz Scheduler created");
    }

#pragma warning disable CA1822 // Mark members as static
    public void Initialize()
#pragma warning restore CA1822
    {
    }

    /// <summary>
    /// Adds an object that should be kept as reference to prevent
    /// it from being garbage collected.
    /// </summary>
    /// <param name="obj">The obj.</param>
    public void AddNoGCObject(object obj)
    {
        holdToPreventGc.Add(obj);
    }

    /// <summary>
    /// Removes the object from garbage collection protected list.
    /// </summary>
    /// <param name="obj">The obj.</param>
    /// <returns></returns>
    public bool RemoveNoGCObject(object obj)
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
    public async ValueTask Start(CancellationToken cancellationToken = default)
    {
        if (shuttingDown || closed)
        {
            ThrowHelper.ThrowSchedulerException("The Scheduler cannot be restarted after Shutdown() has been called.");
        }

        await NotifySchedulerListenersStarting(cancellationToken).ConfigureAwait(false);

        if (!initialStart.HasValue)
        {
            initialStart = this.resources.TimeProvider.GetUtcNow();
            await resources.JobStore.SchedulerStarted(cancellationToken).ConfigureAwait(false);
            await StartPlugins(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await resources.JobStore.SchedulerResumed(cancellationToken).ConfigureAwait(false);
        }

        schedThread.TogglePause(pause: false);

        logger.LogInformation("Scheduler {SchedulerIdentifier} started.", resources.GetUniqueIdentifier());

        await NotifySchedulerListenersStarted(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask StartDelayed(
        TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        if (shuttingDown || closed)
        {
            ThrowHelper.ThrowSchedulerException(
                "The Scheduler cannot be restarted after Shutdown() has been called.");
        }
#pragma warning disable MA0134
        Task.Run(async () =>
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

            try
            {
                await Start(cancellationToken).ConfigureAwait(false);
            }
            catch (SchedulerException se)
            {
                logger.LogError(se, "Unable to start scheduler after startup delay.");
            }
        }, cancellationToken);
#pragma warning restore MA0134

        return default;
    }

    /// <summary>
    /// Temporarily halts the <see cref="QuartzScheduler" />'s firing of <see cref="ITrigger" />s.
    /// <para>
    /// The scheduler is not destroyed, and can be re-started at any time.
    /// </para>
    /// </summary>
    public async ValueTask Standby(CancellationToken cancellationToken = default)
    {
        await resources.JobStore.SchedulerPaused(cancellationToken).ConfigureAwait(false);
        schedThread.TogglePause(pause: true);
        logger.LogInformation("Scheduler {SchedulerIdentifier} paused.", resources.GetUniqueIdentifier());
        await NotifySchedulerListenersInStandbyMode(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the running since.
    /// </summary>
    /// <value>The running since.</value>
    public DateTimeOffset? RunningSince => initialStart;

    /// <summary>
    /// Gets the number of jobs executed.
    /// </summary>
    /// <value>The number of jobs executed.</value>
    public int NumJobsExecuted => jobMgr.NumJobsFired;

    /// <summary>
    /// Gets a value indicating whether this scheduler supports persistence.
    /// </summary>
    /// <value><c>true</c> if supports persistence; otherwise, <c>false</c>.</value>
    public bool SupportsPersistence => resources.JobStore.SupportsPersistence;

    public bool Clustered => resources.JobStore.Clustered;

    /// <summary>
    /// Halts the <see cref="QuartzScheduler" />'s firing of <see cref="ITrigger" />s,
    /// and cleans up all resources associated with the QuartzScheduler.
    /// Equivalent to <see cref="Shutdown(bool, CancellationToken)" />.
    /// <para>
    /// The scheduler cannot be re-started.
    /// </para>
    /// </summary>
    public ValueTask Shutdown(CancellationToken cancellationToken = default)
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
    public async ValueTask Shutdown(
        bool waitForJobsToComplete,
        CancellationToken cancellationToken = default)
    {
        if (shuttingDown || closed)
        {
            return;
        }

        shuttingDown = true;

        try
        {
            logger.LogInformation("Scheduler {SchedulerIdentifier} shutting down.", resources.GetUniqueIdentifier());

            await Standby(cancellationToken).ConfigureAwait(false);

            await schedThread.Halt(waitForJobsToComplete).ConfigureAwait(false);

            await NotifySchedulerListenersShuttingdown(cancellationToken).ConfigureAwait(false);

            if (resources.InterruptJobsOnShutdown && !waitForJobsToComplete
                || resources.InterruptJobsOnShutdownWithWait && waitForJobsToComplete)
            {
                var jobs = GetCurrentlyExecutingJobs().OfType<ICancellableJobExecutionContext>();
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

            await ShutdownPlugins(cancellationToken).ConfigureAwait(false);

            await resources.JobStore.Shutdown(cancellationToken).ConfigureAwait(false);

            await NotifySchedulerListenersShutdown(cancellationToken).ConfigureAwait(false);

        }
        finally
        {
            resources.SchedulerRepository.Remove(resources.Name);
            holdToPreventGc.Clear();
        }

        logger.LogInformation("Scheduler {SchedulerIdentifier} Shutdown complete.", resources.GetUniqueIdentifier());
    }

    /// <summary>
    /// Validates the state.
    /// </summary>
    public void ValidateState()
    {
        if (IsShutdown)
        {
            ThrowHelper.ThrowSchedulerException("The Scheduler has been Shutdown.");
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
    public async ValueTask<DateTimeOffset> ScheduleJob(
        IJobDetail jobDetail,
        ITrigger trigger,
        CancellationToken cancellationToken = default)
    {
        ValidateState();

        if (jobDetail is null)
        {
            ThrowHelper.ThrowSchedulerException("JobDetail cannot be null");
        }

        if (trigger is null)
        {
            ThrowHelper.ThrowSchedulerException("Trigger cannot be null");
        }

        if (jobDetail.Key is null)
        {
            ThrowHelper.ThrowSchedulerException("Job's key cannot be null");
        }

        if (jobDetail.JobType is null)
        {
            ThrowHelper.ThrowSchedulerException("Job's class cannot be null");
        }

        IOperableTrigger trig = (IOperableTrigger) trigger;

        if (trigger.JobKey is null)
        {
            trig.JobKey = jobDetail.Key;
        }
        else if (!trigger.JobKey.Equals(jobDetail.Key))
        {
            ThrowHelper.ThrowSchedulerException("Trigger does not reference given job!");
        }

        trig.Validate();

        ICalendar? cal = null;
        if (trigger.CalendarName is not null)
        {
            cal = await resources.JobStore.RetrieveCalendar(trigger.CalendarName, cancellationToken).ConfigureAwait(false);
            if (cal is null)
            {
                ThrowHelper.ThrowSchedulerException($"Calendar not found: {trigger.CalendarName}");
            }
        }

        DateTimeOffset? ft = trig.ComputeFirstFireTimeUtc(cal);

        if (!ft.HasValue)
        {
            var message = $"Based on configured schedule, the given trigger '{trigger.Key}' will never fire.";
            ThrowHelper.ThrowSchedulerException(message);
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
    public async ValueTask<DateTimeOffset> ScheduleJob(
        ITrigger trigger,
        CancellationToken cancellationToken = default)
    {
        ValidateState();

        if (trigger is null)
        {
            ThrowHelper.ThrowSchedulerException("Trigger cannot be null");
        }

        IOperableTrigger trig = (IOperableTrigger) trigger;
        trig.Validate();

        ICalendar? cal = null;
        if (trigger.CalendarName is not null)
        {
            cal = await resources.JobStore.RetrieveCalendar(trigger.CalendarName, cancellationToken).ConfigureAwait(false);
            if (cal is null)
            {
                ThrowHelper.ThrowSchedulerException($"Calendar not found: {trigger.CalendarName}");
            }
        }

        DateTimeOffset? ft = trig.ComputeFirstFireTimeUtc(cal);

        if (!ft.HasValue)
        {
            var message = $"Based on configured schedule, the given trigger '{trigger.Key}' will never fire.";
            ThrowHelper.ThrowSchedulerException(message);
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
    public ValueTask AddJob(
        IJobDetail jobDetail,
        bool replace,
        CancellationToken cancellationToken = default)
    {
        return AddJob(jobDetail, replace, false, cancellationToken);
    }

    public async ValueTask AddJob(
        IJobDetail jobDetail,
        bool replace,
        bool storeNonDurableWhileAwaitingScheduling,
        CancellationToken cancellationToken = default)
    {
        ValidateState();

        if (!storeNonDurableWhileAwaitingScheduling && !jobDetail.Durable)
        {
            ThrowHelper.ThrowSchedulerException("Jobs added with no trigger must be durable.");
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
    public async ValueTask<bool> DeleteJob(
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
                    .Append(jobKey).Append(']');
                ThrowHelper.ThrowSchedulerException(sb.ToString());
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

    public async ValueTask<bool> DeleteJobs(
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

    public async ValueTask ScheduleJobs(
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
            if (job is null) // there can be one of these (for adding a bulk set of triggers for pre-existing jobs)
            {
                continue;
            }
            if (triggers is null) // this is possible because the job may be durable, and not yet be having triggers
            {
                continue;
            }
            foreach (var t in triggers)
            {
                var trigger = (IOperableTrigger) t;
                trigger.JobKey = job.Key;

                trigger.Validate();

                ICalendar? cal = null;
                if (trigger.CalendarName is not null)
                {
                    cal = await resources.JobStore.RetrieveCalendar(trigger.CalendarName, cancellationToken).ConfigureAwait(false);
                    if (cal is null)
                    {
                        var message = $"Calendar '{trigger.CalendarName}' not found for trigger: {trigger.Key}";
                        ThrowHelper.ThrowSchedulerException(message);
                    }
                }

                DateTimeOffset? ft = trigger.ComputeFirstFireTimeUtc(cal);

                if (ft is null)
                {
                    var message = $"Based on configured schedule, the given trigger '{trigger.Key}' will never fire.";
                    ThrowHelper.ThrowSchedulerException(message);
                }
            }
        }

        await resources.JobStore.StoreJobsAndTriggers(triggersAndJobs, replace, cancellationToken).ConfigureAwait(false);
        NotifySchedulerThread(null);
        foreach (var pair in triggersAndJobs)
        {
            var job = pair.Key;
            var triggers = pair.Value;

            await NotifySchedulerListenersJobAdded(job, cancellationToken).ConfigureAwait(false);
            foreach (var trigger in triggers)
            {
                await NotifySchedulerListenersScheduled(trigger, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public ValueTask ScheduleJob(
        IJobDetail jobDetail,
        IReadOnlyCollection<ITrigger> triggersForJob,
        bool replace,
        CancellationToken cancellationToken = default)
    {
        var triggersAndJobs = new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>>();
        triggersAndJobs.Add(jobDetail, triggersForJob);
        return ScheduleJobs(triggersAndJobs, replace, cancellationToken);
    }

    public async ValueTask<bool> UnscheduleJobs(
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
    public async ValueTask<bool> UnscheduleJob(
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
    public async ValueTask<DateTimeOffset?> RescheduleJob(
        TriggerKey triggerKey,
        ITrigger newTrigger,
        CancellationToken cancellationToken = default)
    {
        ValidateState();

        if (triggerKey is null)
        {
            ThrowHelper.ThrowArgumentException("triggerKey cannot be null");
        }
        if (newTrigger is null)
        {
            ThrowHelper.ThrowArgumentException("newTrigger cannot be null");
        }

        var trigger = (IOperableTrigger) newTrigger;
        ITrigger? oldTrigger = await GetTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
        if (oldTrigger is null)
        {
            return null;
        }

        trigger.JobKey = oldTrigger.JobKey;
        trigger.Validate();

        ICalendar? cal = null;
        if (newTrigger.CalendarName is not null)
        {
            cal = await resources.JobStore.RetrieveCalendar(newTrigger.CalendarName, cancellationToken).ConfigureAwait(false);
        }

        DateTimeOffset? ft;
        if (trigger.GetNextFireTimeUtc() is not null)
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
            ThrowHelper.ThrowSchedulerException(message);
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

    private static string NewTriggerId()
    {
        long r = NextLong();
        if (r < 0)
        {
            r = -r;
        }
        return "MT_" + Convert.ToString(r, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Creates a new positive random number
    /// </summary>
    /// <returns>Returns a new positive random number</returns>
    private static long NextLong()
    {
        long temporaryLong = QuartzRandom.Next();
        temporaryLong = (temporaryLong << 32) + QuartzRandom.Next();
        if (QuartzRandom.Next(-1, 1) < 0)
        {
            return -temporaryLong;
        }

        return temporaryLong;
    }

    /// <summary>
    /// Trigger the identified <see cref="IJob" /> (Execute it now) - with a non-volatile trigger.
    /// </summary>
    public async ValueTask TriggerJob(
        JobKey jobKey,
        JobDataMap? data,
        CancellationToken cancellationToken = default)
    {
        ValidateState();

        // TODO: use builder
        SimpleTriggerImpl trig = new SimpleTriggerImpl(
            NewTriggerId(),
            SchedulerConstants.DefaultGroup,
            jobKey.Name,
            jobKey.Group,
            this.resources.TimeProvider.GetUtcNow(),
            null,
            0,
            TimeSpan.Zero);

        trig.ComputeFirstFireTimeUtc(null);
        if (data is not null)
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
    public async Task TriggerJob(
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
    public async ValueTask PauseTrigger(
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
    public async ValueTask PauseTriggers(
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        ValidateState();

        if (matcher is null)
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
    public async ValueTask PauseJob(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        ValidateState();

        await resources.JobStore.PauseJob(jobKey, cancellationToken).ConfigureAwait(false);
        NotifySchedulerThread(null);
        await NotifySchedulerListenersPausedJob(jobKey, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Pause all of the <see cref="IJobDetail" />s in the
    /// given group - by pausing all of their <see cref="ITrigger" />s.
    /// </summary>
    public async ValueTask PauseJobs(
        GroupMatcher<JobKey> groupMatcher,
        CancellationToken cancellationToken = default)
    {
        ValidateState();

        if (groupMatcher is null)
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
    public async ValueTask ResumeTrigger(
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
    public async ValueTask ResumeTriggers(
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        ValidateState();

        if (matcher is null)
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
    public ValueTask<List<string>> GetPausedTriggerGroups(CancellationToken cancellationToken = default)
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
    public async ValueTask ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        ValidateState();

        await resources.JobStore.ResumeJob(jobKey, cancellationToken).ConfigureAwait(false);
        NotifySchedulerThread(candidateNewNextFireTimeUtc: null);
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
    public async ValueTask ResumeJobs(
        GroupMatcher<JobKey> matcher,
        CancellationToken cancellationToken = default)
    {
        ValidateState();

        if (matcher is null)
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
    public async ValueTask PauseAll(CancellationToken cancellationToken = default)
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
    public async ValueTask ResumeAll(CancellationToken cancellationToken = default)
    {
        ValidateState();

        await resources.JobStore.ResumeAll(cancellationToken).ConfigureAwait(false);
        NotifySchedulerThread(null);
        await NotifySchedulerListenersResumedTriggers(null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Get the names of all known <see cref="IJob" /> groups.
    /// </summary>
    public ValueTask<List<string>> GetJobGroupNames(CancellationToken cancellationToken = default)
    {
        ValidateState();

        return resources.JobStore.GetJobGroupNames(cancellationToken);
    }

    /// <summary>
    /// Get the names of all the <see cref="IJob" />s in the
    /// given group.
    /// </summary>
    public ValueTask<List<JobKey>> GetJobKeys(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        ValidateState();

        if (matcher is null)
        {
            matcher = GroupMatcher<JobKey>.GroupEquals(SchedulerConstants.DefaultGroup);
        }

        return resources.JobStore.GetJobKeys(matcher, cancellationToken);
    }

    /// <summary>
    /// Get all <see cref="ITrigger" /> s that are associated with the
    /// identified <see cref="IJobDetail" />.
    /// </summary>
    public async ValueTask<List<ITrigger>> GetTriggersOfJob(JobKey jobKey, CancellationToken cancellationToken = default)
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
    public ValueTask<List<string>> GetTriggerGroupNames(
        CancellationToken cancellationToken = default)
    {
        ValidateState();
        return resources.JobStore.GetTriggerGroupNames(cancellationToken);
    }

    /// <summary>
    /// Get the names of all the <see cref="ITrigger" />s in
    /// the matching groups.
    /// </summary>
    public ValueTask<List<TriggerKey>> GetTriggerKeys(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        ValidateState();

        if (matcher is null)
        {
            matcher = GroupMatcher<TriggerKey>.GroupEquals(SchedulerConstants.DefaultGroup);
        }

        return resources.JobStore.GetTriggerKeys(matcher, cancellationToken);
    }

    /// <summary>
    /// Get the <see cref="IJobDetail" /> for the <see cref="IJob" />
    /// instance with the given name and group.
    /// </summary>
    public ValueTask<IJobDetail?> GetJobDetail(
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
    public async ValueTask<ITrigger?> GetTrigger(
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
    public ValueTask<bool> CheckExists(
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
    public ValueTask<bool> CheckExists(
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
    public async ValueTask Clear(CancellationToken cancellationToken = default)
    {
        ValidateState();

        await resources.JobStore.ClearAllSchedulingData(cancellationToken).ConfigureAwait(false);
        await NotifySchedulerListenersUnscheduled(null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Get the current state of the identified <see cref="ITrigger" />.
    /// </summary>
    /// <seealso cref="TriggerState" />
    public ValueTask<TriggerState> GetTriggerState(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        ValidateState();

        return resources.JobStore.GetTriggerState(triggerKey, cancellationToken);
    }

    public ValueTask ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        ValidateState();

        return resources.JobStore.ResetTriggerFromErrorState(triggerKey, cancellationToken);
    }

    /// <summary>
    /// Add (register) the given <see cref="ICalendar" /> to the Scheduler.
    /// </summary>
    public ValueTask AddCalendar(
        string name,
        ICalendar calendar,
        bool replace,
        bool updateTriggers,
        CancellationToken cancellationToken = default)
    {
        ValidateState();
        return resources.JobStore.StoreCalendar(name, calendar, replace, updateTriggers, cancellationToken);
    }

    /// <summary>
    /// Delete the identified <see cref="ICalendar" /> from the Scheduler.
    /// </summary>
    /// <returns> true if the Calendar was found and deleted.</returns>
    public ValueTask<bool> DeleteCalendar(string name, CancellationToken cancellationToken = default)
    {
        ValidateState();
        return resources.JobStore.RemoveCalendar(name, cancellationToken);
    }

    /// <summary>
    /// Get the <see cref="ICalendar" /> instance with the given name.
    /// </summary>
    public ValueTask<ICalendar?> GetCalendar(string name, CancellationToken cancellationToken = default)
    {
        ValidateState();
        return resources.JobStore.RetrieveCalendar(name, cancellationToken);
    }

    /// <summary>
    /// Get the names of all registered <see cref="ICalendar" />s.
    /// </summary>
    public ValueTask<List<string>> GetCalendarNames(CancellationToken cancellationToken = default)
    {
        ValidateState();
        return resources.JobStore.GetCalendarNames(cancellationToken);
    }

    public IListenerManager ListenerManager { get; } = new ListenerManagerImpl();

    public ValueTask NotifyJobStoreJobVetoed(
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
    public ValueTask NotifyJobStoreJobComplete(
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
    private void NotifySchedulerThread(DateTimeOffset? candidateNewNextFireTimeUtc)
    {
        if (SignalOnSchedulingChange)
        {
            schedThread.SignalSchedulingChange(candidateNewNextFireTimeUtc);
        }
    }

    private IEnumerable<ISchedulerListener> BuildSchedulerListenerList()
    {
        return ListenerManager.GetSchedulerListeners().Concat(InternalSchedulerListeners);
    }

    private static bool MatchJobListener(IListenerManager listenerManager, IJobListener listener, JobKey key)
    {
        var matchers = listenerManager.GetJobListenerMatchers(listener.Name);
        if (matchers is null)
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

    private static bool MatchTriggerListener(IListenerManager listenerManager, ITriggerListener listener, TriggerKey key)
    {
        var matchers = listenerManager.GetTriggerListenerMatchers(listener.Name);
        if (matchers is null)
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
    /// <returns>
    /// <see langword="true"/> to vetoe the execution of the triggers; otherwise, <see langword="false"/>.
    /// </returns>
    public ValueTask<bool> NotifyTriggerListenersFired(
        IJobExecutionContext jec,
        CancellationToken cancellationToken = default)
    {
        var listeners = ListenerManager.GetTriggerListeners();

        return listeners.Length == 0 ? new ValueTask<bool>(false)
            : NotifyAwaited(ListenerManager, listeners, jec, cancellationToken);

        static async ValueTask<bool> NotifyAwaited(IListenerManager listenerManager,
            ITriggerListener[] listeners,
            IJobExecutionContext jec,
            CancellationToken cancellationToken)
        {
            var vetoedExecution = false;
            foreach (ITriggerListener tl in listeners)
            {
                if (!MatchTriggerListener(listenerManager, tl, jec.Trigger.Key))
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
    }

    /// <summary>
    /// Notifies the trigger listeners about misfired trigger.
    /// </summary>
    /// <param name="trigger">The trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public ValueTask NotifyTriggerListenersMisfired(
        ITrigger trigger,
        CancellationToken cancellationToken = default)
    {
        var listeners = ListenerManager.GetTriggerListeners();

        return listeners.Length == 0 ? default
            : NotifyAwaited(ListenerManager, listeners, trigger, cancellationToken);

        static async ValueTask NotifyAwaited(IListenerManager listenerManager,
            ITriggerListener[] listeners,
            ITrigger trigger,
            CancellationToken cancellationToken)
        {
            foreach (ITriggerListener tl in listeners)
            {
                if (!MatchTriggerListener(listenerManager, tl, trigger.Key))
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
    }

    /// <summary>
    /// Notifies the trigger listeners of completion.
    /// </summary>
    /// <param name="jec">The job execution context.</param>
    /// <param name="instCode">The instruction code to report to triggers.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public ValueTask NotifyTriggerListenersComplete(
        IJobExecutionContext jec,
        SchedulerInstruction instCode,
        CancellationToken cancellationToken = default)
    {
        var listeners = ListenerManager.GetTriggerListeners();

        return listeners.Length == 0 ? default
            : NotifyAwaited(ListenerManager, listeners, jec, instCode, cancellationToken);

        static async ValueTask NotifyAwaited(IListenerManager listenerManager,
            ITriggerListener[] listeners,
            IJobExecutionContext jec,
            SchedulerInstruction instCode,
            CancellationToken cancellationToken)
        {
            foreach (var tl in listeners)
            {
                if (!MatchTriggerListener(listenerManager, tl, jec.Trigger.Key))
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
    }

    /// <summary>
    /// Notifies the job listeners about job to be executed.
    /// </summary>
    /// <param name="jec">The jec.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public ValueTask NotifyJobListenersToBeExecuted(
        IJobExecutionContext jec,
        CancellationToken cancellationToken = default)
    {
        return NotifyJobListeners(static (jl, jec, je, cancellationToken) => jl.JobToBeExecuted(jec, cancellationToken),
            jec,
            null,
            cancellationToken);
    }

    /// <summary>
    /// Notifies the job listeners that job execution was vetoed.
    /// </summary>
    /// <param name="jec">The job execution context.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public ValueTask NotifyJobListenersWasVetoed(
        IJobExecutionContext jec,
        CancellationToken cancellationToken = default)
    {
        return NotifyJobListeners(static (jl, jec, je, cancellationToken) => jl.JobExecutionVetoed(jec, cancellationToken),
            jec,
            null,
            cancellationToken);
    }

    /// <summary>
    /// Notifies the job listeners that job was executed.
    /// </summary>
    /// <param name="jec">The jec.</param>
    /// <param name="je">The je.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public ValueTask NotifyJobListenersWasExecuted(
        IJobExecutionContext jec,
        JobExecutionException? je,
        CancellationToken cancellationToken = default)
    {
        return NotifyJobListeners(static (jl, jec, je, cancellationToken) => jl.JobWasExecuted(jec, je, cancellationToken),
            jec,
            je,
            cancellationToken);
    }

    // optimized version to reduce state machine creations
    private ValueTask NotifyJobListeners(Func<IJobListener, IJobExecutionContext, JobExecutionException?, CancellationToken, ValueTask> notifyAction,
        IJobExecutionContext jec,
        JobExecutionException? je,
        CancellationToken cancellationToken)
    {
        var listeners = ListenerManager.GetJobListeners();
        if (listeners.Length == 0)
        {
            return NotifyExecutingJobManager(notifyAction, jec, je, cancellationToken, jobMgr);
        }

        return NotifyAllJobListeners(ListenerManager, jobMgr, listeners, notifyAction, jec, je, cancellationToken);

        static ValueTask NotifyExecutingJobManager(Func<IJobListener, IJobExecutionContext, JobExecutionException?, CancellationToken, ValueTask> notifyAction,
            IJobExecutionContext jec,
            JobExecutionException? je,
            CancellationToken cancellationToken,
            ExecutingJobsManager jobsManager)
        {
            var task = notifyAction(jobsManager, jec, je, cancellationToken);
            return task.IsCompletedSuccessfully ? default : NotifySingle(task, jobsManager);
        }

        static ValueTask NotifyAllJobListeners(IListenerManager listenerManager,
            ExecutingJobsManager jobManager,
            IJobListener[] listeners,
            Func<IJobListener, IJobExecutionContext, JobExecutionException?, CancellationToken, ValueTask> notifyAction,
            IJobExecutionContext jec,
            JobExecutionException? je,
            CancellationToken cancellationToken)
        {
            return NotifyAwaited(listenerManager, jobManager, listeners, notifyAction, jec, je, cancellationToken);
        }

        static async ValueTask NotifyAwaited(IListenerManager listenerManager,
            ExecutingJobsManager jobManager,
            IJobListener[] listeners,
            Func<IJobListener, IJobExecutionContext, JobExecutionException?, CancellationToken, ValueTask> notifyAction,
            IJobExecutionContext jec,
            JobExecutionException? je,
            CancellationToken cancellationToken)
        {
            await NotifySingle(notifyAction(jobManager, jec, je, cancellationToken), jobManager).ConfigureAwait(false);

            foreach (var jl in listeners)
            {
                if (!MatchJobListener(listenerManager, jl, jec.JobDetail.Key))
                {
                    continue;
                }

                await NotifySingle(notifyAction(jl, jec, je, cancellationToken), jl).ConfigureAwait(false);
            }
        }

        static async ValueTask NotifySingle(ValueTask t, IJobListener jl)
        {
            try
            {
                await t.ConfigureAwait(false);
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
    public async ValueTask NotifySchedulerListenersError(
        string msg,
        SchedulerException se,
        CancellationToken cancellationToken = default)
    {
        // build a list of all scheduler listeners that are to be notified...
        var schedListeners = BuildSchedulerListenerList();

        // notify all scheduler listeners
        foreach (var sl in schedListeners)
        {
            try
            {
                await sl.SchedulerError(msg, se, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error while notifying SchedulerListener of error");
                logger.LogError(se, "  Original error (for notification) was: {Message}", msg);
            }
        }
    }

    /// <summary>
    /// Notifies the scheduler listeners about job that was scheduled.
    /// </summary>
    /// <param name="trigger">The trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public ValueTask NotifySchedulerListenersScheduled(
        ITrigger trigger,
        CancellationToken cancellationToken = default)
    {
        return NotifySchedulerListeners(l => l.JobScheduled(trigger, cancellationToken), $"scheduled job. Trigger={trigger.Key}");
    }

    /// <summary>
    /// Notifies the scheduler listeners about job that was unscheduled.
    /// </summary>
    public async Task NotifySchedulerListenersUnscheduled(
        TriggerKey? triggerKey,
        CancellationToken cancellationToken = default)
    {
        // build a list of all scheduler listeners that are to be notified...
        var schedListeners = BuildSchedulerListenerList();

        // notify all scheduler listeners
        foreach (var sl in schedListeners)
        {
            try
            {
                if (triggerKey is null)
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
                logger.LogError(e,
                    "Error while notifying SchedulerListener of unscheduled job. Trigger={TriggerKey}",
                    triggerKey?.ToString() ?? "ALL DATA");
            }
        }
    }

    /// <summary>
    /// Notifies the scheduler listeners about finalized trigger.
    /// </summary>
    /// <param name="trigger">The trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public ValueTask NotifySchedulerListenersFinalized(
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
    public async Task NotifySchedulerListenersPausedTriggers(
        string? group,
        CancellationToken cancellationToken = default)
    {
        // build a list of all job listeners that are to be notified...
        var schedListeners = BuildSchedulerListenerList();

        // notify all scheduler listeners
        foreach (var sl in schedListeners)
        {
            try
            {
                await sl.TriggersPaused(@group, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error while notifying SchedulerListener of paused group: {Group}", group);
            }
        }
    }

    /// <summary>
    /// Notifies the scheduler listeners about paused trigger.
    /// </summary>
    public async Task NotifySchedulerListenersPausedTrigger(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        // build a list of all job listeners that are to be notified...
        var schedListeners = BuildSchedulerListenerList();

        // notify all scheduler listeners
        foreach (var sl in schedListeners)
        {
            try
            {
                await sl.TriggerPaused(triggerKey, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error while notifying SchedulerListener of paused trigger. Trigger={TriggerKey}", triggerKey);
            }
        }
    }

    /// <summary>
    /// Notifies the scheduler listeners resumed trigger.
    /// </summary>
    /// <param name="group">The group.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public Task NotifySchedulerListenersResumedTriggers(
        string? group,
        CancellationToken cancellationToken = default)
    {
        return NotifySchedulerListeners(l => l.TriggersResumed(group, cancellationToken), $"resumed group: {group}").AsTask();
    }

    /// <summary>
    /// Notifies the scheduler listeners resumed trigger.
    /// </summary>
    public async Task NotifySchedulerListenersResumedTrigger(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        // build a list of all job listeners that are to be notified...
        var schedListeners = BuildSchedulerListenerList();

        // notify all scheduler listeners
        foreach (var sl in schedListeners)
        {
            try
            {
                await sl.TriggerResumed(triggerKey, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error while notifying SchedulerListener of resumed trigger. Trigger={TriggerKey}", triggerKey);
            }
        }
    }

    /// <summary>
    /// Notifies the scheduler listeners about paused job.
    /// </summary>
    public async ValueTask NotifySchedulerListenersPausedJob(JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        // build a list of all job listeners that are to be notified...
        var schedListeners = BuildSchedulerListenerList();

        // notify all scheduler listeners
        foreach (var sl in schedListeners)
        {
            try
            {
                await sl.JobPaused(jobKey, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error while notifying SchedulerListener of paused job. Job={JobKey}", jobKey);
            }
        }
    }

    /// <summary>
    /// Notifies the scheduler listeners about paused job.
    /// </summary>
    public async Task NotifySchedulerListenersPausedJobs(
        string group,
        CancellationToken cancellationToken = default)
    {
        // build a list of all job listeners that are to be notified...
        var schedListeners = BuildSchedulerListenerList();

        // notify all scheduler listeners
        foreach (var sl in schedListeners)
        {
            try
            {
                await sl.JobsPaused(@group, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error while notifying SchedulerListener of paused group: {Group}", group);
            }
        }
    }

    /// <summary>
    /// Notifies the scheduler listeners about resumed job.
    /// </summary>
    public async Task NotifySchedulerListenersResumedJob(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        // build a list of all job listeners that are to be notified...
        var schedListeners = BuildSchedulerListenerList();

        // notify all scheduler listeners
        foreach (var sl in schedListeners)
        {
            try
            {
                await sl.JobResumed(jobKey, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error while notifying SchedulerListener of resumed job: {JobKey}", jobKey);
            }
        }
    }

    /// <summary>
    /// Notifies the scheduler listeners about resumed job.
    /// </summary>
    public async Task NotifySchedulerListenersResumedJobs(
        string group,
        CancellationToken cancellationToken = default)
    {
        // build a list of all job listeners that are to be notified...
        var schedListeners = BuildSchedulerListenerList();

        // notify all scheduler listeners
        foreach (var sl in schedListeners)
        {
            try
            {
                await sl.JobsResumed(@group, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error while notifying SchedulerListener of resumed group: {Group}", group);
            }
        }
    }

    public ValueTask NotifySchedulerListenersInStandbyMode(
        CancellationToken cancellationToken = default)
    {
        return NotifySchedulerListeners(l => l.SchedulerInStandbyMode(cancellationToken), "inStandByMode");
    }

    public ValueTask NotifySchedulerListenersStarted(
        CancellationToken cancellationToken = default)
    {
        return NotifySchedulerListeners(l => l.SchedulerStarted(cancellationToken), "startup");
    }

    public ValueTask NotifySchedulerListenersStarting(
        CancellationToken cancellationToken = default)
    {
        return NotifySchedulerListeners(l => l.SchedulerStarting(cancellationToken), "scheduler starting");
    }

    /// <summary>
    /// Notifies the scheduler listeners about scheduler shutdown.
    /// </summary>
    public ValueTask NotifySchedulerListenersShutdown(
        CancellationToken cancellationToken = default)
    {
        return NotifySchedulerListeners(l => l.SchedulerShutdown(cancellationToken), "shutdown");
    }

    public ValueTask NotifySchedulerListenersShuttingdown(
        CancellationToken cancellationToken = default)
    {
        return NotifySchedulerListeners(l => l.SchedulerShuttingdown(cancellationToken), "shutting down");
    }

    public ValueTask NotifySchedulerListenersJobAdded(
        IJobDetail jobDetail,
        CancellationToken cancellationToken = default)
    {
        return NotifySchedulerListeners(l => l.JobAdded(jobDetail, cancellationToken), "job addition");
    }

    public ValueTask NotifySchedulerListenersJobDeleted(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return NotifySchedulerListeners(l => l.JobDeleted(jobKey, cancellationToken), "job deletion");
    }

    private async ValueTask NotifySchedulerListeners(
        Func<ISchedulerListener, ValueTask> notifier,
        string action)
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
                logger.LogError(e, "Error while notifying SchedulerListener of {Action}", action);
            }
        }
    }

    /// <summary>
    /// Interrupt all instances of the identified InterruptableJob.
    /// </summary>
    public async ValueTask<bool> Interrupt(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        var cancellableJobs = GetCurrentlyExecutingJobs().OfType<ICancellableJobExecutionContext>();

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
    /// <param name="fireInstanceId"></param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns></returns>
    public ValueTask<bool> Interrupt(
        string fireInstanceId,
        CancellationToken cancellationToken = default)
    {
        var cancellableJobs = GetCurrentlyExecutingJobs().OfType<ICancellableJobExecutionContext>();

        bool interrupted = false;

        foreach (var cancellableJobExecutionContext in cancellableJobs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (cancellableJobExecutionContext.FireInstanceId == fireInstanceId)
            {
                cancellableJobExecutionContext.Cancel();
                interrupted = true;
                break;
            }
        }

        return new ValueTask<bool>(interrupted);
    }

    private async Task ShutdownPlugins(
        CancellationToken cancellationToken = default)
    {
        foreach (ISchedulerPlugin plugin in resources.SchedulerPlugins)
        {
            await plugin.Shutdown(cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask StartPlugins(
        CancellationToken cancellationToken = default)
    {
        foreach (ISchedulerPlugin plugin in resources.SchedulerPlugins)
        {
            await plugin.Start(cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask<bool> IsJobGroupPaused(
        string groupName,
        CancellationToken cancellationToken = default)
    {
        return resources.JobStore.IsJobGroupPaused(groupName, cancellationToken);
    }

    public ValueTask<bool> IsTriggerGroupPaused(
        string groupName,
        CancellationToken cancellationToken = default)
    {
        return resources.JobStore.IsTriggerGroupPaused(groupName, cancellationToken);
    }
}