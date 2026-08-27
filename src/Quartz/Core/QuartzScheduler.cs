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
using Quartz.Impl.Triggers;
using Quartz.Impl;
using Quartz.Extensibility;

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
internal sealed class QuartzScheduler
{
    private readonly ILogger<QuartzScheduler> logger;
    private static readonly Version version;

    internal readonly QuartzSchedulerResources resources = null!;
    private readonly TimeProvider timeProvider;

    internal readonly QuartzSchedulerThread schedThread = null!;
    private readonly List<ISchedulerListener> internalSchedulerListeners = new List<ISchedulerListener>(10);

    /// <summary>
    /// Guards <see cref="internalSchedulerListeners" />. A dedicated lock rather than the list itself, so
    /// that what is synchronized on is an object nothing outside this class can reach.
    /// </summary>
    private readonly Lock internalSchedulerListenersLock = new();

    private IJobFactory jobFactory = new PropertySettingJobFactory();
    private readonly ExecutingJobsManager jobMgr;
    private readonly List<object> holdToPreventGc = new List<object>(5);

    /// <summary>
    /// Where the scheduler is in its lifecycle - the whole of it, in one field, so that no two readers
    /// can combine flags into different answers.
    /// </summary>
    private volatile SchedulerStatus status = SchedulerStatus.Created;

    private int shutdownInitiated;
    private DateTimeOffset? initialStart;
    private volatile ExecutionLimits? executionLimits;

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
    /// The <see cref="IScheduler" /> facade this scheduler is seen through — the one handed to every
    /// listener callback and to every <see cref="IJobExecutionContext" />.
    /// </summary>
    /// <remarks>
    /// Built once here rather than by whoever assembles the scheduler, so that there is exactly one
    /// facade per scheduler and it exists from the moment the scheduler does. A notification must
    /// never construct one: two facades over one scheduler would compare unequal, and a listener that
    /// remembers the scheduler it was told about would remember a different object each time.
    /// </remarks>
    public IScheduler Scheduler { get; }

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
    /// Where the scheduler is in its lifecycle.
    /// </summary>
    public SchedulerStatus Status => status;

    /// <summary>
    /// Gets the job store class.
    /// </summary>
    /// <remarks>
    /// The store that keeps the data, not whatever is wrapped around it. Every store a container builds
    /// carries at least a tracing decorator, and this type name is what <see cref="SchedulerMetadata" />
    /// reports, what the dashboard shows and what the startup log line says — none of which is answered
    /// by naming a decorator.
    /// </remarks>
    /// <value>The job store class.</value>
    public Type JobStoreType => JobStores.Unwrap(resources.JobStore).GetType();

    /// <summary>
    /// Gets the thread pool class.
    /// </summary>
    /// <value>The thread pool class.</value>
    public Type ThreadPoolType => resources.ThreadPool.GetType();

    /// <summary>
    /// Gets the size of the thread pool.
    /// </summary>
    /// <value>The size of the thread pool.</value>
    public int ThreadPoolSize => resources.ThreadPool.PoolSize;

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
    public List<IJobExecutionContext> GetCurrentlyExecutingJobs() => jobMgr.GetExecutingJobs;

    /// <summary>
    /// Register the given <see cref="ISchedulerListener" /> with the
    /// <see cref="IScheduler" />'s list of internal listeners.
    /// </summary>
    /// <param name="schedulerListener"></param>
    public void AddInternalSchedulerListener(ISchedulerListener schedulerListener)
    {
        lock (internalSchedulerListenersLock)
        {
            internalSchedulerListeners.Add(schedulerListener);
        }
    }

    /// <summary>
    /// Remove the given <see cref="ISchedulerListener" /> from the
    /// <see cref="IScheduler" />'s list of internal listeners.
    /// </summary>
    /// <param name="schedulerListener"></param>
    /// <returns>true if the identified listener was found in the list, and removed.</returns>
    public bool RemoveInternalSchedulerListener(ISchedulerListener schedulerListener)
    {
        lock (internalSchedulerListenersLock)
        {
            return internalSchedulerListeners.Remove(schedulerListener);
        }
    }

    /// <summary>
    /// Get a List containing all of the <i>internal</i> <see cref="ISchedulerListener" />s
    /// registered with the <see cref="IScheduler" />.
    /// </summary>
    public List<ISchedulerListener> InternalSchedulerListeners
    {
        get
        {
            lock (internalSchedulerListenersLock)
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
                Throw.ArgumentException("JobFactory cannot be set to null!");
            }

            logger.JobFactorySet(value);

            jobFactory = value;
        }
    }

    /// <summary>
    /// Create a <see cref="QuartzScheduler" /> with the given configuration
    /// properties.
    /// </summary>
    /// <seealso cref="QuartzSchedulerResources" />
    public QuartzScheduler(QuartzSchedulerResources resources) : this(resources, TimeProvider.System) { }

    /// <summary>
    /// Create a <see cref="QuartzScheduler" /> with the given configuration
    /// properties.
    /// </summary>
    /// <seealso cref="QuartzSchedulerResources" />
    public QuartzScheduler(QuartzSchedulerResources resources, TimeProvider timeProvider)
    {
        this.resources = resources;
        this.timeProvider = timeProvider;

        // Everything below is handed a logger from the same factory, which the container fills in when
        // it builds the resources. Nothing here reads the ambient LogProvider any more: an application
        // that configured logging and never heard of that static still sees the scheduler's own lines.
        ILoggerFactory loggerFactory = resources.LoggerFactory;
        logger = loggerFactory.CreateLogger<QuartzScheduler>();

        // The thread is created here but not started: constructing a scheduler must not have the side
        // effect of starting a thread, since the container constructs it. Start does that instead.
        schedThread = new QuartzSchedulerThread(this, resources);

        jobMgr = new ExecutingJobsManager();
        var errLogger = new ErrorLogger(loggerFactory.CreateLogger<ErrorLogger>());
        AddInternalSchedulerListener(errLogger);

        SchedulerSignaler = new SchedulerSignalerImpl(this, schedThread, loggerFactory.CreateLogger<SchedulerSignalerImpl>());
        Scheduler = new StdScheduler(this);

        logger.SchedulerCreated();
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
        SchedulerStatus current = status;
        if (current is SchedulerStatus.ShuttingDown or SchedulerStatus.Shutdown)
        {
            Throw.SchedulerException("The Scheduler cannot be restarted after Shutdown() has been called.");
        }

        if (current == SchedulerStatus.Running)
        {
            // Nothing to start, so nothing to announce. Re-emitting Starting/Started and telling the job
            // store the scheduler resumed would report transitions that did not happen, and a listener
            // counting starts would count this one.
            return;
        }

        await NotifySchedulerListenersStarting(cancellationToken).ConfigureAwait(false);

        if (!initialStart.HasValue)
        {
            initialStart = this.resources.TimeProvider.GetUtcNow();
            try
            {
                await resources.JobStore.SchedulerStarted(cancellationToken).ConfigureAwait(false);
                await StartPlugins(cancellationToken).ConfigureAwait(false);
            }
            catch (SchedulerStartRefusedException)
            {
                // Refused before the job store set anything up, so starting again is safe once the
                // caller has fixed the cause. Leaving the marker latched would send the retry down the
                // "already started" path, skipping job recovery, the misfire handler and cluster
                // check-in while still starting the acquire loop. Only this one exception un-latches: a
                // failure further in may already have created those, and re-running would orphan them.
                initialStart = null;
                throw;
            }
        }
        else
        {
            await resources.JobStore.SchedulerResumed(cancellationToken).ConfigureAwait(false);
        }

        // Idempotent, so restarting after Standby is fine.
        schedThread.Start();
        schedThread.TogglePause(pause: false);

        status = SchedulerStatus.Running;

        logger.SchedulerStarted(resources.GetUniqueIdentifier());

        await NotifySchedulerListenersStarted(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask StartDelayed(
        TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        if (status is SchedulerStatus.ShuttingDown or SchedulerStatus.Shutdown)
        {
            Throw.SchedulerException(
                "The Scheduler cannot be restarted after Shutdown() has been called.");
        }
#pragma warning disable MA0134
        Task.Run(async () =>
        {
            await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);

            try
            {
                await Start(cancellationToken).ConfigureAwait(false);
            }
            catch (SchedulerException se)
            {
                logger.DelayedStartFailed(se);
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
        ValidateState();

        if (status != SchedulerStatus.Running)
        {
            // Nothing to stand down, so nothing to announce. A scheduler that has never started is born
            // with its thread paused and a job store that was never told it was running, and one already
            // in standby is in the state being asked for - telling the listeners either way would report
            // a transition that did not happen. A never-started scheduler also stays Created rather than
            // becoming Standby: "built but never run" is the more precise of the two answers.
            return;
        }

        await StopFiring(cancellationToken).ConfigureAwait(false);

        status = SchedulerStatus.Standby;

        await NotifySchedulerListenersInStandbyMode(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops the scheduler firing triggers, without saying why.
    /// </summary>
    /// <remarks>
    /// Shared by <see cref="Standby" /> and <see cref="Shutdown" />, which differ in what they tell the
    /// world afterwards: standby announces itself and is reversible, a shutdown neither.
    /// </remarks>
    private async ValueTask StopFiring(CancellationToken cancellationToken)
    {
        await resources.JobStore.SchedulerPaused(cancellationToken).ConfigureAwait(false);
        schedThread.TogglePause(pause: true);
        logger.SchedulerPaused(resources.GetUniqueIdentifier());
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
    public int NumberOfJobsExecuted => jobMgr.NumJobsFired;

    /// <summary>
    /// Gets the number of jobs running in this process right now.
    /// </summary>
    /// <value>
    /// The number of executions this scheduler instance is hosting. Node-local by construction: it counts
    /// the contexts this process holds, so a cluster-wide answer comes from
    /// <see cref="IScheduler.QueryFireInstances" /> instead.
    /// </value>
    public int NumberOfJobsExecutingHere => jobMgr.NumJobsCurrentlyExecuting;

    /// <summary>
    /// Gets a value indicating whether this scheduler supports persistence.
    /// </summary>
    /// <value><c>true</c> if supports persistence; otherwise, <c>false</c>.</value>
    public bool SupportsPersistence => resources.JobStore.SupportsPersistence;

    public bool Clustered => resources.JobStore.Clustered;

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
    /// <param name="cancellationToken">
    /// Bounds the wait for running jobs, and nothing else. Cancelling it stops the scheduler waiting —
    /// it does not cancel the jobs, and it does not abandon the shutdown, which always runs to the end
    /// so that the job store, the plugins and the listeners are all told the scheduler has stopped.
    /// </param>
    public async ValueTask Shutdown(
        bool waitForJobsToComplete = false,
        CancellationToken cancellationToken = default)
    {
        // Atomic claim: two concurrent callers (say a hosted service's StopAsync and user code)
        // must not both run the shutdown sequence — the steps below are not idempotent.
        if (status == SchedulerStatus.Shutdown || Interlocked.Exchange(ref shutdownInitiated, 1) == 1)
        {
            return;
        }

        status = SchedulerStatus.ShuttingDown;

        try
        {
            logger.SchedulerShuttingDown(resources.GetUniqueIdentifier());

            // Firing stops here, but the scheduler does not pass through standby on its way down and no
            // listener is told that it did: a scheduler being torn down is not one waiting to be started
            // again, and saying so was inherited from Java rather than true.
            //
            // Not the caller's token, for the same reason as the teardown below: the shutdown is claimed
            // atomically and cannot be retried, so a step that gave up here would leave the scheduler
            // neither running nor shut down. The token bounds the wait for running jobs and nothing else.
            await StopFiring(CancellationToken.None).ConfigureAwait(false);

            await schedThread.Halt(waitForJobsToComplete).ConfigureAwait(false);

            await NotifySchedulerListenersShuttingDown(CancellationToken.None).ConfigureAwait(false);

            bool interruptRunningJobs = resources.ShutdownJobInterruption switch
            {
                ShutdownJobInterruption.Always => true,
                ShutdownJobInterruption.WhenWaitingForJobs => waitForJobsToComplete,
                ShutdownJobInterruption.WhenNotWaitingForJobs => !waitForJobsToComplete,
                _ => false
            };

            if (interruptRunningJobs)
            {
                var jobs = GetCurrentlyExecutingJobs().OfType<IInterruptableJobExecutionContext>();
                foreach (var job in jobs)
                {
                    try
                    {
                        job.Interrupt();
                    }
                    catch (ObjectDisposedException)
                    {

                    }
                }
            }

            if (waitForJobsToComplete)
            {
                // The caller's token bounds the wait for running jobs and nothing else: Drain reports
                // that it gave up rather than throwing, so everything below still runs. The barrier
                // covers each job's job store update as well as the job itself, because the pool was
                // handed the whole of the execution, of which that update is the last act.
                if (!await resources.ThreadPool.Drain(cancellationToken).ConfigureAwait(false))
                {
                    logger.GaveUpWaitingForRunningJobs(resources.GetUniqueIdentifier());
                }
            }
            else
            {
                await resources.ThreadPool.Shutdown(waitForJobsToComplete: false, CancellationToken.None).ConfigureAwait(false);
            }

            // Scheduler thread may have be waiting for the fire time of an acquired
            // trigger and need time to release the trigger once halted, so make sure
            // the thread is dead before continuing to shutdown the job store.
            await schedThread.Shutdown().ConfigureAwait(false);

            // Same reasoning as the pool shutdown above: in hosted shutdown the caller's token is
            // the graceful-deadline token, which by design may already have fired while waiting for
            // jobs. A plugin, job store or listener that honoured it would abort the remaining
            // teardown and leave the scheduler wedged with no listener ever told it shut down.
            await ShutdownPlugins(CancellationToken.None).ConfigureAwait(false);

            await resources.JobStore.Shutdown(CancellationToken.None).ConfigureAwait(false);

            // Here rather than only in the finally below, so that "shut down" is a fact rather than an
            // intention: everything the scheduler owns is down by the time anything can read it, and the
            // listener about to hear the news reads the same answer as everyone else.
            status = SchedulerStatus.Shutdown;

            await NotifySchedulerListenersShutdown(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            // Whatever happened above, the scheduler is down: the claim is one-shot, so a plugin or job
            // store that threw during teardown would otherwise leave it ShuttingDown for the rest of the
            // process - refusing work, and never reaching the state that says why. The happy path assigns
            // this earlier, so the SchedulerShutdown listener still reads Shutdown.
            status = SchedulerStatus.Shutdown;

            resources.SchedulerRepository.Remove(resources.Name, resources.InstanceId);
            holdToPreventGc.Clear();
        }

        logger.SchedulerShutdownComplete(resources.GetUniqueIdentifier());
    }

    /// <summary>
    /// Refuses work once the scheduler is on its way down.
    /// </summary>
    /// <remarks>
    /// <see cref="SchedulerStatus.ShuttingDown" /> counts as shut down here: the shutdown has been
    /// claimed and cannot be abandoned, so accepting work would be accepting it into a scheduler whose
    /// job store is about to close under it.
    /// </remarks>
    public void ValidateState()
    {
        if (status is SchedulerStatus.ShuttingDown or SchedulerStatus.Shutdown)
        {
            Throw.SchedulerException("The Scheduler has been Shutdown.");
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
            Throw.SchedulerException("JobDetail cannot be null");
        }

        if (trigger is null)
        {
            Throw.SchedulerException("Trigger cannot be null");
        }

        if (jobDetail.Key is null)
        {
            Throw.SchedulerException("Job's key cannot be null");
        }

        if (jobDetail.JobType is null)
        {
            Throw.SchedulerException("Job's class cannot be null");
        }

        IOperableTrigger trig = AsOperableTrigger(trigger);

        if (trigger.JobKey is null)
        {
            trig.JobKey = jobDetail.Key;
        }
        else if (!trigger.JobKey.Equals(jobDetail.Key))
        {
            Throw.SchedulerException("Trigger does not reference given job!");
        }

        AdjustSimpleTriggerStartTimeIfInPast(trig);
        trig.Validate();

        ICalendar? calendar = null;
        if (trigger.CalendarName is not null)
        {
            calendar = await resources.JobStore.GetCalendar(trigger.CalendarName, cancellationToken).ConfigureAwait(false);
            if (calendar is null)
            {
                Throw.SchedulerException($"Calendar not found: {trigger.CalendarName}");
            }
        }

        DateTimeOffset? ft = trig.ComputeFirstFireTimeUtc(calendar);

        if (!ft.HasValue)
        {
            var message = $"Based on configured schedule, the given trigger '{trigger.Key}' will never fire.";
            Throw.SchedulerException(message);
        }

        await resources.JobStore.ScheduleJob(jobDetail, trig, cancellationToken).ConfigureAwait(false);
        await NotifySchedulerListenersJobAdded(jobDetail, cancellationToken).ConfigureAwait(false);
        NotifySchedulerThread(trigger.NextFireTimeUtc);
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
            Throw.SchedulerException("Trigger cannot be null");
        }

        IOperableTrigger trig = AsOperableTrigger(trigger);
        AdjustSimpleTriggerStartTimeIfInPast(trig);
        trig.Validate();

        ICalendar? calendar = null;
        if (trigger.CalendarName is not null)
        {
            calendar = await resources.JobStore.GetCalendar(trigger.CalendarName, cancellationToken).ConfigureAwait(false);
            if (calendar is null)
            {
                Throw.SchedulerException($"Calendar not found: {trigger.CalendarName}");
            }
        }

        DateTimeOffset? ft = trig.ComputeFirstFireTimeUtc(calendar);

        if (!ft.HasValue)
        {
            var message = $"Based on configured schedule, the given trigger '{trigger.Key}' will never fire.";
            Throw.SchedulerException(message);
        }

        await resources.JobStore.AddTrigger(trig, false, cancellationToken).ConfigureAwait(false);
        NotifySchedulerThread(trigger.NextFireTimeUtc);
        await NotifySchedulerListenersScheduled(trigger, cancellationToken).ConfigureAwait(false);

        return ft.Value;
    }

    /// <summary>
    /// Add the given <see cref="IJob" /> to the Scheduler - with no associated
    /// <see cref="ITrigger" />. The <see cref="IJob" /> will be 'dormant' until
    /// it is scheduled with a <see cref="ITrigger" />, or <see cref="IScheduler.TriggerJob" />
    /// is called for it.
    /// <para>
    /// The <see cref="IJob" /> must by definition be 'durable' unless
    /// <see cref="AddJobOptions.StoreNonDurableWhileAwaitingScheduling" /> is set, otherwise
    /// SchedulerException will be thrown.
    /// </para>
    /// </summary>
    public async ValueTask AddJob(
        IJobDetail jobDetail,
        AddJobOptions options = default,
        CancellationToken cancellationToken = default)
    {
        ValidateState();

        if (!options.StoreNonDurableWhileAwaitingScheduling && !jobDetail.Durable)
        {
            Throw.SchedulerException("Jobs added with no trigger must be durable.");
        }

        await resources.JobStore.AddJob(jobDetail, options.Replace, cancellationToken).ConfigureAwait(false);
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
                Throw.SchedulerException(sb.ToString());
            }
            result = true;
        }

        result = await resources.JobStore.DeleteJob(jobKey, cancellationToken).ConfigureAwait(false) || result;
        if (result)
        {
            NotifySchedulerThread(null);
            await NotifySchedulerListenersJobDeleted(jobKey, cancellationToken).ConfigureAwait(false);
        }
        return result;
    }

    public async ValueTask<List<JobKey>> DeleteJobs(
        IReadOnlyCollection<JobKey> jobKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobKeys);
        ValidateState();

        if (jobKeys.Count == 0)
        {
            return [];
        }

        List<JobKey> deleted = await resources.JobStore.DeleteJobs(jobKeys, cancellationToken).ConfigureAwait(false);
        if (deleted.Count > 0)
        {
            NotifySchedulerThread(null);
            foreach (JobKey key in deleted)
            {
                await NotifySchedulerListenersJobDeleted(key, cancellationToken).ConfigureAwait(false);
            }
        }

        return deleted;
    }

    public async ValueTask ScheduleJobs(
        IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs,
        ScheduleJobOptions options = default,
        CancellationToken cancellationToken = default)
    {
        ValidateState();

        // make sure all triggers refer to their associated job, materializing the operable
        // shape the store contract takes while validating each trigger
        Dictionary<IJobDetail, IReadOnlyCollection<IOperableTrigger>> validated = new(triggersAndJobs.Count);
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
                validated.Add(job, []);
                continue;
            }

            List<IOperableTrigger> operableTriggers = new(triggers.Count);
            foreach (var t in triggers)
            {
                var trigger = AsOperableTrigger(t);
                trigger.JobKey = job.Key;

                AdjustSimpleTriggerStartTimeIfInPast(trigger);
                trigger.Validate();

                ICalendar? calendar = null;
                if (trigger.CalendarName is not null)
                {
                    calendar = await resources.JobStore.GetCalendar(trigger.CalendarName, cancellationToken).ConfigureAwait(false);
                    if (calendar is null)
                    {
                        var message = $"Calendar '{trigger.CalendarName}' not found for trigger: {trigger.Key}";
                        Throw.SchedulerException(message);
                    }
                }

                DateTimeOffset? ft = trigger.ComputeFirstFireTimeUtc(calendar);

                if (ft is null)
                {
                    var message = $"Based on configured schedule, the given trigger '{trigger.Key}' will never fire.";
                    Throw.SchedulerException(message);
                }

                operableTriggers.Add(trigger);
            }

            validated.Add(job, operableTriggers);
        }

        await resources.JobStore.ScheduleJobs(validated, options.Replace, cancellationToken).ConfigureAwait(false);
        NotifySchedulerThread(null);
        foreach (var pair in validated)
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
        ScheduleJobOptions options = default,
        CancellationToken cancellationToken = default)
    {
        var triggersAndJobs = new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>>();
        triggersAndJobs.Add(jobDetail, triggersForJob);
        return ScheduleJobs(triggersAndJobs, options, cancellationToken);
    }

    public async ValueTask<List<TriggerKey>> UnscheduleJobs(
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triggerKeys);
        ValidateState();

        if (triggerKeys.Count == 0)
        {
            return [];
        }

        List<TriggerKey> unscheduled = await resources.JobStore.DeleteTriggers(triggerKeys, cancellationToken).ConfigureAwait(false);
        if (unscheduled.Count > 0)
        {
            NotifySchedulerThread(null);
            foreach (TriggerKey key in unscheduled)
            {
                await NotifySchedulerListenersUnscheduled(key, cancellationToken).ConfigureAwait(false);
            }
        }

        return unscheduled;
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

        if (await resources.JobStore.DeleteTrigger(triggerKey, cancellationToken).ConfigureAwait(false))
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
            Throw.ArgumentException("triggerKey cannot be null");
        }
        if (newTrigger is null)
        {
            Throw.ArgumentException("newTrigger cannot be null");
        }

        var trigger = AsOperableTrigger(newTrigger);
        ITrigger? oldTrigger = await GetTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
        if (oldTrigger is null)
        {
            return null;
        }

        trigger.JobKey = oldTrigger.JobKey;
        AdjustSimpleTriggerStartTimeIfInPast(trigger);
        trigger.Validate();

        ICalendar? calendar = null;
        if (newTrigger.CalendarName is not null)
        {
            calendar = await resources.JobStore.GetCalendar(newTrigger.CalendarName, cancellationToken).ConfigureAwait(false);
            if (calendar is null)
            {
                // Rescheduling validates the calendar the same way scheduling does. Storing a
                // trigger whose calendar cannot be found leaves it in place but never fires it,
                // which is far harder to diagnose than a failed call.
                Throw.SchedulerException($"Calendar not found: {newTrigger.CalendarName}");
            }
        }

        DateTimeOffset? ft;
        if (trigger.NextFireTimeUtc is not null)
        {
            // use a cloned trigger so that we don't lose possible forcefully set next fire time
            var clonedTrigger = (IOperableTrigger) trigger.Clone();
            ft = clonedTrigger.ComputeFirstFireTimeUtc(calendar);
        }
        else
        {
            ft = trigger.ComputeFirstFireTimeUtc(calendar);
        }

        if (!ft.HasValue)
        {
            var message = $"Based on configured schedule, the given trigger '{trigger.Key}' will never fire.";
            Throw.SchedulerException(message);
        }

        if (await resources.JobStore.ReplaceTrigger(triggerKey, trigger, cancellationToken).ConfigureAwait(false))
        {
            NotifySchedulerThread(newTrigger.NextFireTimeUtc);
            await NotifySchedulerListenersUnscheduled(triggerKey, cancellationToken).ConfigureAwait(false);
            await NotifySchedulerListenersScheduled(newTrigger, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            return null;
        }

        return ft;
    }

    /// <summary>
    /// Updates trigger metadata and selected settings without rescheduling.
    /// Fire times and trigger state are preserved. Supported properties include
    /// Description, Priority, JobDataMap, CalendarName, and MisfireInstruction.
    /// </summary>
    /// <param name="triggerKey">The key identifying the trigger to update.</param>
    /// <param name="update">The details to update. See <see cref="TriggerDetailsUpdate"/> for available properties.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns><see langword="true"/> if the trigger was found and updated, <see langword="false"/> if not found.</returns>
    public async ValueTask<bool> UpdateTriggerDetails(
        TriggerKey triggerKey,
        TriggerDetailsUpdate update,
        CancellationToken cancellationToken = default)
    {
        ValidateState();

        ArgumentNullException.ThrowIfNull(triggerKey);
        ArgumentNullException.ThrowIfNull(update);

        return await resources.JobStore.UpdateTriggerDetails(triggerKey, update, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets the execution group limits for this scheduler node.
    /// </summary>
    internal void SetExecutionLimits(ExecutionLimits? limits)
    {
        executionLimits = limits;
    }

    /// <summary>
    /// Gets the currently configured execution group limits, or <see langword="null"/> if none are configured.
    /// </summary>
    internal ExecutionLimits? GetExecutionLimits() => executionLimits;

    /// <summary>
    /// The scheduler and the job stores operate on <see cref="IOperableTrigger" />, and Quartz owns
    /// the implementations of <see cref="ITrigger" /> — so a trigger that implements only the read
    /// model is rejected with a clear error rather than an invalid-cast exception.
    /// </summary>
    private static IOperableTrigger AsOperableTrigger(ITrigger trigger)
    {
        if (trigger is not IOperableTrigger operableTrigger)
        {
            Throw.SchedulerException(
                $"Trigger '{trigger.Key}' of type {trigger.GetType().FullName} cannot be scheduled: " +
                "Quartz owns the implementations of ITrigger. Build triggers with TriggerBuilder, and " +
                "derive custom trigger types from TriggerBase; an object implementing only ITrigger is a read model.");
            return null!;
        }

        return operableTrigger;
    }

    /// <summary>
    /// For a SimpleTrigger whose StartTimeUtc is in the past and has never fired,
    /// advance the start time to the current time so that ComputeFirstFireTimeUtc
    /// will produce a future fire time. This handles the case where a trigger is
    /// created well before it is actually scheduled.
    /// </summary>
    private void AdjustSimpleTriggerStartTimeIfInPast(IOperableTrigger trigger)
    {
        if (trigger is ISimpleTrigger simpleTrigger
            && trigger.PreviousFireTimeUtc is null
            && simpleTrigger.RepeatCount != 0
            && simpleTrigger.RepeatInterval > TimeSpan.Zero)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            if (trigger.StartTimeUtc < now)
            {
                // Advance start time to the current time. ComputeFirstFireTimeUtc
                // will then correctly compute the first fire time from now, and
                // RepeatCount still controls how many times the trigger fires from
                // this new start.
                trigger.StartTimeUtc = now;
            }
        }
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
        JobDataMap? data = null,
        CancellationToken cancellationToken = default)
    {
        ValidateState();

        // TODO: use builder
        SimpleTriggerImpl trig = new SimpleTriggerImpl(
            NewTriggerId(),
            TriggerKey.DefaultGroup,
            jobKey.Name,
            jobKey.Group,
            this.resources.TimeProvider.GetUtcNow(),
            null,
            0,
            TimeSpan.Zero,
            this.resources.TimeProvider);

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
                await resources.JobStore.AddTrigger(trig, false, cancellationToken).ConfigureAwait(false);
                collision = false;
            }
            catch (ObjectAlreadyExistsException)
            {
                trig.Key = new TriggerKey(NewTriggerId(), TriggerKey.DefaultGroup);
            }
        }

        NotifySchedulerThread(trig.NextFireTimeUtc);
        await NotifySchedulerListenersScheduled(trig, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Store and schedule the identified <see cref="IOperableTrigger"/>
    /// </summary>
    public async ValueTask TriggerJob(
        IOperableTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        ValidateState();

        trigger.ComputeFirstFireTimeUtc(null);

        bool collision = true;
        while (collision)
        {
            try
            {
                await resources.JobStore.AddTrigger(trigger, false, cancellationToken).ConfigureAwait(false);
                collision = false;
            }
            catch (ObjectAlreadyExistsException)
            {
                trigger.Key = new TriggerKey(NewTriggerId(), TriggerKey.DefaultGroup);
            }
        }

        NotifySchedulerThread(trigger.NextFireTimeUtc);
        await NotifySchedulerListenersScheduled(trigger, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Pause the <see cref="ITrigger" /> with the given name.
    /// </summary>
    public async ValueTask<bool> PauseTrigger(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        ValidateState();

        bool paused = await resources.JobStore.PauseTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
        if (paused)
        {
            NotifySchedulerThread(null);
            await NotifySchedulerListenersPausedTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
        }

        return paused;
    }

    /// <summary>
    /// Pause the <see cref="ITrigger" />s with the given keys, signalling the change once.
    /// </summary>
    public async ValueTask<List<TriggerKey>> PauseTriggers(
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triggerKeys);
        ValidateState();

        if (triggerKeys.Count == 0)
        {
            return [];
        }

        var paused = await resources.JobStore.PauseTriggers(triggerKeys, cancellationToken).ConfigureAwait(false);
        if (paused.Count > 0)
        {
            // One signal for the whole set: the scheduler thread reads a scheduling change as a
            // level, not an edge, so the recomputation it triggers covers every key at once.
            NotifySchedulerThread(null);
            foreach (TriggerKey triggerKey in paused)
            {
                await NotifySchedulerListenersPausedTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
            }
        }

        return paused;
    }

    /// <summary>
    /// Pause all of the <see cref="ITrigger" />s in the given group.
    /// </summary>
    public async ValueTask<List<string>> PauseTriggers(
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        ValidateState();

        if (matcher is null)
        {
            matcher = GroupMatcher<TriggerKey>.GroupEquals(TriggerKey.DefaultGroup);
        }

        var pausedGroups = await resources.JobStore.PauseTriggers(matcher, cancellationToken).ConfigureAwait(false);
        NotifySchedulerThread(null);
        await Task.WhenAll(pausedGroups.Select(x => NotifySchedulerListenersPausedTriggers(x, cancellationToken).AsTask())).ConfigureAwait(false);
        return pausedGroups;
    }

    /// <summary>
    /// Pause the <see cref="IJobDetail" /> with the given
    /// name - by pausing all of its current <see cref="ITrigger" />s.
    /// </summary>
    public async ValueTask<bool> PauseJob(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        ValidateState();

        bool found = await resources.JobStore.PauseJob(jobKey, cancellationToken).ConfigureAwait(false);
        if (found)
        {
            NotifySchedulerThread(null);
            await NotifySchedulerListenersPausedJob(jobKey, cancellationToken).ConfigureAwait(false);
        }

        return found;
    }

    /// <summary>
    /// Pause the <see cref="IJobDetail" />s with the given keys, signalling the change once.
    /// </summary>
    public async ValueTask<List<JobKey>> PauseJobs(
        IReadOnlyCollection<JobKey> jobKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobKeys);
        ValidateState();

        if (jobKeys.Count == 0)
        {
            return [];
        }

        var paused = await resources.JobStore.PauseJobs(jobKeys, cancellationToken).ConfigureAwait(false);
        if (paused.Count > 0)
        {
            NotifySchedulerThread(null);
            foreach (JobKey jobKey in paused)
            {
                await NotifySchedulerListenersPausedJob(jobKey, cancellationToken).ConfigureAwait(false);
            }
        }

        return paused;
    }

    /// <summary>
    /// Pause all of the <see cref="IJobDetail" />s in the
    /// given group - by pausing all of their <see cref="ITrigger" />s.
    /// </summary>
    public async ValueTask<List<string>> PauseJobs(
        GroupMatcher<JobKey> groupMatcher,
        CancellationToken cancellationToken = default)
    {
        ValidateState();

        if (groupMatcher is null)
        {
            groupMatcher = GroupMatcher<JobKey>.GroupEquals(JobKey.DefaultGroup);
        }

        var pausedGroups = await resources.JobStore.PauseJobs(groupMatcher, cancellationToken).ConfigureAwait(false);
        NotifySchedulerThread(null);
        await Task.WhenAll(pausedGroups.Select(x => NotifySchedulerListenersPausedJobs(x, cancellationToken).AsTask())).ConfigureAwait(false);
        return pausedGroups;
    }

    /// <summary>
    /// Resume (un-pause) the <see cref="ITrigger" /> with the given
    /// name.
    /// <para>
    /// If the <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </para>
    /// </summary>
    public async ValueTask<bool> ResumeTrigger(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        ValidateState();

        bool resumed = await resources.JobStore.ResumeTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
        if (resumed)
        {
            NotifySchedulerThread(null);
            await NotifySchedulerListenersResumedTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
        }

        return resumed;
    }

    /// <summary>
    /// Resume (un-pause) the <see cref="ITrigger" />s with the given keys, signalling the change
    /// once.
    /// </summary>
    public async ValueTask<List<TriggerKey>> ResumeTriggers(
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triggerKeys);
        ValidateState();

        if (triggerKeys.Count == 0)
        {
            return [];
        }

        var resumed = await resources.JobStore.ResumeTriggers(triggerKeys, cancellationToken).ConfigureAwait(false);
        if (resumed.Count > 0)
        {
            NotifySchedulerThread(null);
            foreach (TriggerKey triggerKey in resumed)
            {
                await NotifySchedulerListenersResumedTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
            }
        }

        return resumed;
    }

    /// <summary>
    /// Resume (un-pause) all of the <see cref="ITrigger" />s in the
    /// matching groups.
    /// <para>
    /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </para>
    /// </summary>
    public async ValueTask<List<string>> ResumeTriggers(
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        ValidateState();

        if (matcher is null)
        {
            matcher = GroupMatcher<TriggerKey>.GroupEquals(TriggerKey.DefaultGroup);
        }

        var resumedGroups = await resources.JobStore.ResumeTriggers(matcher, cancellationToken).ConfigureAwait(false);
        NotifySchedulerThread(null);
        await Task.WhenAll(resumedGroups.Select(x => NotifySchedulerListenersResumedTriggers(x, cancellationToken).AsTask())).ConfigureAwait(false);
        return resumedGroups;
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
    public async ValueTask<bool> ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        ValidateState();

        bool found = await resources.JobStore.ResumeJob(jobKey, cancellationToken).ConfigureAwait(false);
        if (found)
        {
            NotifySchedulerThread(candidateNewNextFireTimeUtc: null);
            await NotifySchedulerListenersResumedJob(jobKey, cancellationToken).ConfigureAwait(false);
        }

        return found;
    }

    /// <summary>
    /// Resume (un-pause) the <see cref="IJobDetail" />s with the given keys, signalling the change
    /// once.
    /// </summary>
    public async ValueTask<List<JobKey>> ResumeJobs(
        IReadOnlyCollection<JobKey> jobKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobKeys);
        ValidateState();

        if (jobKeys.Count == 0)
        {
            return [];
        }

        var resumed = await resources.JobStore.ResumeJobs(jobKeys, cancellationToken).ConfigureAwait(false);
        if (resumed.Count > 0)
        {
            NotifySchedulerThread(candidateNewNextFireTimeUtc: null);
            foreach (JobKey jobKey in resumed)
            {
                await NotifySchedulerListenersResumedJob(jobKey, cancellationToken).ConfigureAwait(false);
            }
        }

        return resumed;
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
    public async ValueTask<List<string>> ResumeJobs(
        GroupMatcher<JobKey> matcher,
        CancellationToken cancellationToken = default)
    {
        ValidateState();

        if (matcher is null)
        {
            matcher = GroupMatcher<JobKey>.GroupEquals(JobKey.DefaultGroup);
        }

        var resumedGroups = await resources.JobStore.ResumeJobs(matcher, cancellationToken).ConfigureAwait(false);
        NotifySchedulerThread(null);
        await Task.WhenAll(resumedGroups.Select(x => NotifySchedulerListenersResumedJobs(x, cancellationToken).AsTask())).ConfigureAwait(false);
        return resumedGroups;
    }

    /// <summary>
    /// Pause all triggers - equivalent of calling <see cref="PauseTriggers(GroupMatcher{TriggerKey}, CancellationToken)" />
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
    /// Resume (un-pause) all triggers - equivalent of calling <see cref="ResumeTriggers(GroupMatcher{TriggerKey}, CancellationToken)" />
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
    /// Lists jobs matching the query, as <see cref="JobHeader" />s.
    /// </summary>
    public ValueTask<PagedResult<JobHeader>> QueryJobs(JobQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateState();

        return resources.JobStore.QueryJobs(query, cancellationToken);
    }

    /// <summary>
    /// Lists triggers matching the query, as <see cref="TriggerHeader" />s.
    /// </summary>
    public ValueTask<PagedResult<TriggerHeader>> QueryTriggers(TriggerQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateState();

        return resources.JobStore.QueryTriggers(query, cancellationToken);
    }

    /// <summary>
    /// Lists job groups matching the query.
    /// </summary>
    public ValueTask<PagedResult<JobGroup>> QueryJobGroups(JobGroupQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateState();

        return resources.JobStore.QueryJobGroups(query, cancellationToken);
    }

    /// <summary>
    /// Lists trigger groups matching the query.
    /// </summary>
    public ValueTask<PagedResult<TriggerGroup>> QueryTriggerGroups(TriggerGroupQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateState();

        return resources.JobStore.QueryTriggerGroups(query, cancellationToken);
    }

    /// <summary>
    /// Lists calendar names matching the query.
    /// </summary>
    public ValueTask<PagedResult<string>> QueryCalendarNames(CalendarQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateState();

        return resources.JobStore.QueryCalendarNames(query, cancellationToken);
    }

    /// <summary>
    /// Lists firings matching the query, from the job store — so with a persistent store, from the whole
    /// cluster rather than only this node.
    /// </summary>
    public ValueTask<PagedResult<FireInstance>> QueryFireInstances(FireInstanceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateState();

        return resources.JobStore.QueryFireInstances(query, cancellationToken);
    }

    /// <summary>
    /// Lists the scheduler nodes the job store knows about, this node first.
    /// </summary>
    public ValueTask<List<ClusterNode>> QueryClusterNodes(CancellationToken cancellationToken = default)
    {
        ValidateState();

        return resources.JobStore.QueryClusterNodes(cancellationToken);
    }

    /// <summary>
    /// Retrieves the given jobs in one round trip.
    /// </summary>
    public ValueTask<List<IJobDetail>> GetJobDetails(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobKeys);
        ValidateState();

        return resources.JobStore.GetJobs(jobKeys, cancellationToken);
    }

    /// <summary>
    /// Retrieves the given triggers in one round trip.
    /// </summary>
    public async ValueTask<List<ITrigger>> GetTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triggerKeys);
        ValidateState();

        var triggers = await resources.JobStore.GetTriggers(triggerKeys, cancellationToken).ConfigureAwait(false);

        var retValue = new List<ITrigger>(triggers.Count);
        foreach (var trigger in triggers)
        {
            retValue.Add(trigger);
        }
        return retValue;
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

        return resources.JobStore.GetJob(jobKey, cancellationToken);
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

        return await resources.JobStore.GetTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
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
    public ValueTask<bool> Exists(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        ValidateState();

        return resources.JobStore.Exists(jobKey, cancellationToken);
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
    public ValueTask<bool> Exists(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        ValidateState();

        return resources.JobStore.Exists(triggerKey, cancellationToken);
    }

    /// <summary>
    /// Clears (deletes!) all scheduling data - all <see cref="IJob"/>s, <see cref="ITrigger" />s
    /// <see cref="ICalendar" />s.
    /// </summary>
    public async ValueTask Clear(CancellationToken cancellationToken = default)
    {
        ValidateState();

        await resources.JobStore.Clear(cancellationToken).ConfigureAwait(false);
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

    public ValueTask<bool> ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        ValidateState();

        return resources.JobStore.ResetTriggerFromErrorState(triggerKey, cancellationToken);
    }

    /// <summary>
    /// Reset the <see cref="ITrigger" />s with the given keys from the error state, in one pass.
    /// </summary>
    /// <remarks>
    /// Resetting raises no listener event and signals no scheduling change, here as in the
    /// single-key form: the triggers it returns to the waiting state are picked up by the next
    /// acquisition cycle.
    /// </remarks>
    public ValueTask<List<TriggerKey>> ResetTriggersFromErrorState(
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triggerKeys);
        ValidateState();

        if (triggerKeys.Count == 0)
        {
            return new ValueTask<List<TriggerKey>>([]);
        }

        return resources.JobStore.ResetTriggersFromErrorState(triggerKeys, cancellationToken);
    }

    /// <summary>
    /// Add (register) the given <see cref="ICalendar" /> to the Scheduler.
    /// </summary>
    public ValueTask AddCalendar(
        string calendarName,
        ICalendar calendar,
        AddCalendarOptions options = default,
        CancellationToken cancellationToken = default)
    {
        ValidateState();
        return resources.JobStore.AddCalendar(calendarName, calendar, options, cancellationToken);
    }

    /// <summary>
    /// Delete the identified <see cref="ICalendar" /> from the Scheduler.
    /// </summary>
    /// <returns> true if the Calendar was found and deleted.</returns>
    public ValueTask<bool> DeleteCalendar(string calendarName, CancellationToken cancellationToken = default)
    {
        ValidateState();
        return resources.JobStore.DeleteCalendar(calendarName, cancellationToken);
    }

    /// <summary>
    /// Get the <see cref="ICalendar" /> instance with the given name.
    /// </summary>
    public ValueTask<ICalendar?> GetCalendar(string calendarName, CancellationToken cancellationToken = default)
    {
        ValidateState();
        return resources.JobStore.GetCalendar(calendarName, cancellationToken);
    }

    public IListenerManager ListenerManager { get; } = new ListenerManagerImpl();

    public ValueTask NotifyJobStoreJobVetoed(
        IOperableTrigger trigger,
        IJobDetail detail,
        SchedulerInstruction instructionCode,
        CancellationToken cancellationToken = default)
    {
        return resources.JobStore.TriggeredJobComplete(trigger, detail, instructionCode, cancellationToken);
    }

    /// <summary>
    /// Notifies the job store job complete.
    /// </summary>
    public ValueTask NotifyJobStoreJobComplete(
        IOperableTrigger trigger,
        IJobDetail detail,
        SchedulerInstruction instructionCode,
        CancellationToken cancellationToken = default)
    {
        return resources.JobStore.TriggeredJobComplete(trigger, detail, instructionCode, cancellationToken);
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
        if (matchers.Count == 0)
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
        if (matchers.Count == 0)
        {
            return true;
        }
        return matchers.Any(matcher => matcher.IsMatch(key));
    }

    /// <summary>
    /// Notifies the trigger listeners about fired trigger.
    /// </summary>
    /// <param name="context">The job execution context.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// <see langword="true"/> to vetoe the execution of the triggers; otherwise, <see langword="false"/>.
    /// </returns>
    public ValueTask<bool> NotifyTriggerListenersFired(
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var listeners = ListenerManager.GetTriggerListeners();

        return listeners.Count == 0 ? new ValueTask<bool>(false)
            : NotifyAwaited(ListenerManager, listeners, context, cancellationToken);

        static async ValueTask<bool> NotifyAwaited(IListenerManager listenerManager,
            IReadOnlyList<ITriggerListener> listeners,
            IJobExecutionContext context,
            CancellationToken cancellationToken)
        {
            var vetoedExecution = false;
            foreach (ITriggerListener tl in listeners)
            {
                if (!MatchTriggerListener(listenerManager, tl, context.Trigger.Key))
                {
                    continue;
                }

                try
                {
                    await tl.TriggerFired(context.Trigger, context, cancellationToken).ConfigureAwait(false);

                    if (await tl.VetoJobExecution(context.Trigger, context, cancellationToken).ConfigureAwait(false))
                    {
                        vetoedExecution = true;
                    }
                }
                catch (Exception e)
                {
                    throw new JobExecutionProcessException(tl, context, e);
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
        // Counted here rather than in a store, because this is the one place every store's misfire
        // handling arrives at: the in-memory store's own scan and the database store's misfire handler
        // both signal through here. Before the listeners, and outside the "are there any" check below,
        // so the count is of misfires rather than of misfires somebody was listening for.
        resources.Meters.TriggerMisfired(resources.Name, resources.InstanceId, trigger);

        var listeners = ListenerManager.GetTriggerListeners();

        return listeners.Count == 0 ? default
            : NotifyAwaited(Scheduler, ListenerManager, listeners, trigger, cancellationToken);

        static async ValueTask NotifyAwaited(
            IScheduler scheduler,
            IListenerManager listenerManager,
            IReadOnlyList<ITriggerListener> listeners,
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
                    await tl.TriggerMisfired(scheduler, trigger, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    throw new SchedulerException($"TriggerListener '{tl.Name}' threw exception: {e.Message}", e);
                }
            }
        }
    }

    /// <summary>
    /// Notifies the trigger listeners of completion.
    /// </summary>
    /// <param name="context">The job execution context.</param>
    /// <param name="instructionCode">The instruction code to report to triggers.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public ValueTask NotifyTriggerListenersComplete(
        IJobExecutionContext context,
        SchedulerInstruction instructionCode,
        CancellationToken cancellationToken = default)
    {
        var listeners = ListenerManager.GetTriggerListeners();

        return listeners.Count == 0 ? default
            : NotifyAwaited(ListenerManager, listeners, context, instructionCode, cancellationToken);

        static async ValueTask NotifyAwaited(IListenerManager listenerManager,
            IReadOnlyList<ITriggerListener> listeners,
            IJobExecutionContext context,
            SchedulerInstruction instructionCode,
            CancellationToken cancellationToken)
        {
            foreach (var tl in listeners)
            {
                if (!MatchTriggerListener(listenerManager, tl, context.Trigger.Key))
                {
                    continue;
                }

                try
                {
                    await tl.TriggerComplete(context.Trigger, context, instructionCode, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    throw new JobExecutionProcessException(tl, context, e);
                }
            }
        }
    }

    /// <summary>
    /// Notifies the job listeners about job to be executed.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public ValueTask NotifyJobListenersToBeExecuted(
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // The scheduler's own record of the firing is kept outside the listener loop below, because a
        // listener that throws abandons that loop. JobRunShell then completes the firing without the
        // job having run and without anyone being told it was executed, so a record kept inside the
        // loop would list the firing as executing for as long as the process lived (#3502).
        jobMgr.FiringStarted(context);

        ValueTask notification = NotifyJobListeners(
            static (jl, context, jobExecutionException, cancellationToken) => jl.JobToBeExecuted(context, cancellationToken),
            context,
            null,
            cancellationToken);

        return notification.IsCompletedSuccessfully
            ? default
            : EndFiringIfNotificationFails(notification, jobMgr, context);

        static async ValueTask EndFiringIfNotificationFails(
            ValueTask notification,
            ExecutingJobsManager jobMgr,
            IJobExecutionContext context)
        {
            try
            {
                await notification.ConfigureAwait(false);
            }
            catch
            {
                // Nothing further will be notified for this firing, so this is the only place the
                // record of it can be taken back out.
                jobMgr.FiringEnded(context);
                throw;
            }
        }
    }

    /// <summary>
    /// Notifies the job listeners that job execution was vetoed.
    /// </summary>
    /// <param name="context">The job execution context.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public ValueTask NotifyJobListenersWasVetoed(
        IJobExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        return NotifyJobListeners(static (jl, context, jobExecutionException, cancellationToken) => jl.JobExecutionVetoed(context, cancellationToken),
            context,
            null,
            cancellationToken);
    }

    /// <summary>
    /// Notifies the job listeners that job was executed.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="jobExecutionException">The jobExecutionException.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public ValueTask NotifyJobListenersWasExecuted(
        IJobExecutionContext context,
        JobExecutionException? jobExecutionException,
        CancellationToken cancellationToken = default)
    {
        // Taken out of the record before the listeners are told, and not by one of them: a listener
        // that throws here must not leave the firing showing as executing (#3502). It is also what
        // ShutdownDrainTest pins — the count drops before the job store update the firing ends with.
        jobMgr.FiringEnded(context);

        return NotifyJobListeners(static (jl, context, jobExecutionException, cancellationToken) => jl.JobWasExecuted(context, jobExecutionException, cancellationToken),
            context,
            jobExecutionException,
            cancellationToken);
    }

    // optimized version to reduce state machine creations
    private ValueTask NotifyJobListeners(
        Func<IJobListener, IJobExecutionContext, JobExecutionException?, CancellationToken, ValueTask> notifyAction,
        IJobExecutionContext context,
        JobExecutionException? jobExecutionException,
        CancellationToken cancellationToken)
    {
        var listeners = ListenerManager.GetJobListeners();
        if (listeners.Count == 0)
        {
            return default;
        }

        return NotifyAwaited(ListenerManager, listeners, notifyAction, context, jobExecutionException, cancellationToken);

        static async ValueTask NotifyAwaited(IListenerManager listenerManager,
            IReadOnlyList<IJobListener> listeners,
            Func<IJobListener, IJobExecutionContext, JobExecutionException?, CancellationToken, ValueTask> notifyAction,
            IJobExecutionContext context,
            JobExecutionException? jobExecutionException,
            CancellationToken cancellationToken)
        {
            foreach (var jl in listeners)
            {
                if (!MatchJobListener(listenerManager, jl, context.JobDetail.Key))
                {
                    continue;
                }

                // The call to the listener is inside the guard, not an argument to it, so a listener
                // that throws before it hands anything back — a guard clause in a method that is not
                // async — is wrapped in the same exception as one that hands back a faulted task.
                // JobRunShell catches SchedulerException, and a raw exception escaping it left the
                // firing stranded and the job's other triggers blocked behind it (#3502).
                try
                {
                    await notifyAction(jl, context, jobExecutionException, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    throw new JobExecutionProcessException(jl, context, e);
                }
            }
        }
    }

    /// <summary>
    /// Notifies the scheduler listeners about scheduler error.
    /// </summary>
    /// <param name="error">What went wrong, and what it went wrong for.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public async ValueTask NotifySchedulerListenersError(
        SchedulerErrorContext error,
        CancellationToken cancellationToken = default)
    {
        // build a list of all scheduler listeners that are to be notified...
        var schedListeners = BuildSchedulerListenerList();

        // notify all scheduler listeners
        foreach (var sl in schedListeners)
        {
            try
            {
                await sl.SchedulerError(Scheduler, error, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.ListenerNotificationOfErrorFailed(e);
                logger.OriginalErrorForNotification(error.Message, error.Exception);
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
        return NotifySchedulerListeners(l => l.JobScheduled(Scheduler, trigger, cancellationToken), $"scheduled job. Trigger={trigger.Key}");
    }

    /// <summary>
    /// Notifies the scheduler listeners about job that was unscheduled.
    /// </summary>
    public async ValueTask NotifySchedulerListenersUnscheduled(
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
                    await sl.SchedulingDataCleared(Scheduler, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await sl.JobUnscheduled(Scheduler, triggerKey, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                logger.ListenerNotificationOfUnscheduledJobFailed(triggerKey?.ToString() ?? "ALL DATA", e);
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
        return NotifySchedulerListeners(l => l.TriggerFinalized(Scheduler, trigger, cancellationToken), $"finalized trigger. Trigger={trigger.Key}");
    }

    /// <summary>
    /// Notifies the scheduler listeners about paused trigger.
    /// </summary>
    /// <param name="group">The group.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public async ValueTask NotifySchedulerListenersPausedTriggers(
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
                await sl.TriggersPaused(Scheduler, @group, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.ListenerNotificationOfPausedGroupFailed(group, e);
            }
        }
    }

    /// <summary>
    /// Notifies the scheduler listeners that a trigger has been parked in the error state.
    /// </summary>
    public async ValueTask NotifySchedulerListenersTriggerInError(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        var schedListeners = BuildSchedulerListenerList();

        foreach (var sl in schedListeners)
        {
            try
            {
                await sl.TriggerInError(Scheduler, triggerKey, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.ListenerNotificationOfTriggerInErrorFailed(triggerKey, e);
            }
        }
    }

    /// <summary>
    /// Notifies the scheduler listeners that every trigger of a job has been parked in the error state.
    /// </summary>
    public async ValueTask NotifySchedulerListenersTriggersInError(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        var schedListeners = BuildSchedulerListenerList();

        foreach (var sl in schedListeners)
        {
            try
            {
                await sl.TriggersInError(Scheduler, jobKey, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.ListenerNotificationOfJobTriggersInErrorFailed(jobKey, e);
            }
        }
    }

    /// <summary>
    /// Notifies the scheduler listeners about paused trigger.
    /// </summary>
    public async ValueTask NotifySchedulerListenersPausedTrigger(
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
                await sl.TriggerPaused(Scheduler, triggerKey, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.ListenerNotificationOfPausedTriggerFailed(triggerKey, e);
            }
        }
    }

    /// <summary>
    /// Notifies the scheduler listeners resumed trigger.
    /// </summary>
    /// <param name="group">The group.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public ValueTask NotifySchedulerListenersResumedTriggers(
        string? group,
        CancellationToken cancellationToken = default)
    {
        return NotifySchedulerListeners(l => l.TriggersResumed(Scheduler, group, cancellationToken), $"resumed group: {group}");
    }

    /// <summary>
    /// Notifies the scheduler listeners resumed trigger.
    /// </summary>
    public async ValueTask NotifySchedulerListenersResumedTrigger(
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
                await sl.TriggerResumed(Scheduler, triggerKey, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.ListenerNotificationOfResumedTriggerFailed(triggerKey, e);
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
                await sl.JobPaused(Scheduler, jobKey, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.ListenerNotificationOfPausedJobFailed(jobKey, e);
            }
        }
    }

    /// <summary>
    /// Notifies the scheduler listeners about paused job.
    /// </summary>
    public async ValueTask NotifySchedulerListenersPausedJobs(
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
                await sl.JobsPaused(Scheduler, @group, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.ListenerNotificationOfPausedGroupFailed(group, e);
            }
        }
    }

    /// <summary>
    /// Notifies the scheduler listeners about resumed job.
    /// </summary>
    public async ValueTask NotifySchedulerListenersResumedJob(
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
                await sl.JobResumed(Scheduler, jobKey, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.ListenerNotificationOfResumedJobFailed(jobKey, e);
            }
        }
    }

    /// <summary>
    /// Notifies the scheduler listeners about resumed job.
    /// </summary>
    public async ValueTask NotifySchedulerListenersResumedJobs(
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
                await sl.JobsResumed(Scheduler, @group, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.ListenerNotificationOfResumedGroupFailed(group, e);
            }
        }
    }

    public ValueTask NotifySchedulerListenersInStandbyMode(
        CancellationToken cancellationToken = default)
    {
        return NotifySchedulerListeners(l => l.SchedulerInStandbyMode(Scheduler, cancellationToken), "inStandByMode");
    }

    public ValueTask NotifySchedulerListenersStarted(
        CancellationToken cancellationToken = default)
    {
        return NotifySchedulerListeners(l => l.SchedulerStarted(Scheduler, cancellationToken), "startup");
    }

    public ValueTask NotifySchedulerListenersStarting(
        CancellationToken cancellationToken = default)
    {
        return NotifySchedulerListeners(l => l.SchedulerStarting(Scheduler, cancellationToken), "scheduler starting");
    }

    /// <summary>
    /// Notifies the scheduler listeners about scheduler shutdown.
    /// </summary>
    public ValueTask NotifySchedulerListenersShutdown(
        CancellationToken cancellationToken = default)
    {
        return NotifySchedulerListeners(l => l.SchedulerShutdown(Scheduler, cancellationToken), "shutdown");
    }

    public ValueTask NotifySchedulerListenersShuttingDown(
        CancellationToken cancellationToken = default)
    {
        return NotifySchedulerListeners(l => l.SchedulerShuttingDown(Scheduler, cancellationToken), "shutting down");
    }

    public ValueTask NotifySchedulerListenersJobAdded(
        IJobDetail jobDetail,
        CancellationToken cancellationToken = default)
    {
        return NotifySchedulerListeners(l => l.JobAdded(Scheduler, jobDetail, cancellationToken), "job addition");
    }

    public ValueTask NotifySchedulerListenersJobDeleted(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return NotifySchedulerListeners(l => l.JobDeleted(Scheduler, jobKey, cancellationToken), "job deletion");
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
                logger.ListenerNotificationFailed(action, e);
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
        var interruptableJobs = GetCurrentlyExecutingJobs().OfType<IInterruptableJobExecutionContext>();

        bool interrupted = false;

        foreach (var interruptableContext in interruptableJobs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var jobDetail = interruptableContext.JobDetail;
            if (jobKey.Equals(jobDetail.Key))
            {
                interruptableContext.Interrupt();
                interrupted = true;
            }
        }

        if (interrupted)
        {
            await NotifySchedulerListeners(l => l.JobInterrupted(Scheduler, jobKey, cancellationToken), "job interruption").ConfigureAwait(false);
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
    public async ValueTask<bool> InterruptFireInstance(
        string fireInstanceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Looked up rather than scanned: the running executions are already keyed by fire instance id,
        // which is what this method is given.
        if (!jobMgr.TryGetExecutingJob(fireInstanceId, out var context)
            || context is not IInterruptableJobExecutionContext interruptableContext)
        {
            return false;
        }

        interruptableContext.Interrupt();
        var jobKey = interruptableContext.JobDetail.Key;
        await NotifySchedulerListeners(l => l.JobInterrupted(Scheduler, jobKey, cancellationToken), "job interruption").ConfigureAwait(false);
        return true;
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

}