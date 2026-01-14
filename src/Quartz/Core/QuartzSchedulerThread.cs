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

using System.Data.Common;

using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;
using Quartz.Spi;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Core;

/// <summary>
/// The thread responsible for performing the work of firing <see cref="ITrigger" />
/// s that are registered with the <see cref="QuartzScheduler" />.
/// </summary>
/// <seealso cref="QuartzScheduler" />
/// <seealso cref="IJob" />
/// <seealso cref="ITrigger" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
internal sealed class QuartzSchedulerThread
{
    private readonly ILogger logger;
    private readonly QuartzScheduler qs;
    private readonly QuartzSchedulerResources qsRsrcs;
    private readonly int idleWaitVariableness;
    private readonly object sigLock = new object();

    private bool signaled;
    private DateTimeOffset? signaledNextFireTimeUtc;
    private bool paused;
    private bool halted;

    private CancellationTokenSource cancellationTokenSource = null!;
    private Task task = null!;

    /// <summary>
    /// Gets the randomized idle wait time.
    /// </summary>
    /// <value>The randomized idle wait time.</value>
    private TimeSpan GetRandomizedIdleWaitTime()
    {
        return qsRsrcs.IdleWaitTime - TimeSpan.FromMilliseconds(QuartzRandom.Next(idleWaitVariableness));
    }

    /// <summary>
    /// Gets a value indicating whether this <see cref="QuartzSchedulerThread"/> is paused.
    /// </summary>
    /// <value><c>true</c> if paused; otherwise, <c>false</c>.</value>
    internal bool Paused => paused;

    /// <summary>
    /// Gets a value indicating whether this <see cref="QuartzSchedulerThread"/> is stopped.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if stopped; otherwise, <see langword="false"/>.
    /// </value>
    internal bool Halted => halted;

    /// <summary>
    /// Gets the maximum number of milliseconds to subtract from <see cref="QuartzSchedulerResources.IdleWaitTime"/>
    /// to randomize how long the scheduler should wait before checking again when there is no current trigger to
    /// fire.
    /// </summary>
    /// <value>
    /// The maximum number of milliseconds to subtract from <see cref="QuartzSchedulerResources.IdleWaitTime"/> to
    /// randomize how long the scheduler should wait before checking again when there is no current trigger to fire.
    /// </value>
    internal int IdleWaitVariableness => idleWaitVariableness;

    /// <summary>
    /// Construct a new <see cref="QuartzSchedulerThread" /> for the given
    /// <see cref="QuartzScheduler" /> as a non-daemon <see cref="Thread" />
    /// with normal priority.
    /// </summary>
    /// <param name="qs">The scheduler.</param>
    /// <param name="qsRsrcs">The resources.</param>
    internal QuartzSchedulerThread(QuartzScheduler qs, QuartzSchedulerResources qsRsrcs)
    {
        this.logger = LogProvider.CreateLogger<QuartzSchedulerThread>();
        this.qs = qs;
        this.qsRsrcs = qsRsrcs;
        idleWaitVariableness = (int) (qsRsrcs.IdleWaitTime.TotalMilliseconds * 0.2);

        // start the underlying thread, but put this object into the 'paused'
        // state
        // so processing doesn't start yet...
        paused = true;
        halted = false;
    }

    /// <summary>
    /// Signals the main processing loop to pause at the next possible point.
    /// </summary>
    internal void TogglePause(bool pause)
    {
        lock (sigLock)
        {
            paused = pause;

            if (paused)
            {
                SignalSchedulingChange(SchedulerConstants.SchedulingSignalDateTime);
            }
            else
            {
                Monitor.PulseAll(sigLock);
            }
        }
    }

    /// <summary>
    /// Signals the main processing loop to stop at the next possible point.
    /// </summary>
    internal async Task Halt(bool wait)
    {
        lock (sigLock)
        {
            halted = true;

            if (paused)
            {
                Monitor.PulseAll(sigLock);
            }
            else
            {
                SignalSchedulingChange(SchedulerConstants.SchedulingSignalDateTime);
            }
        }

        await cancellationTokenSource.CancelAsync().ConfigureAwait(false);

        if (wait)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    /// <summary>
    /// Signals the main processing loop that a change in scheduling has been
    /// made - in order to interrupt any sleeping that may be occurring while
    /// waiting for the fire time to arrive.
    /// </summary>
    /// <param name="candidateNewNextFireTimeUtc">
    /// the time when the newly scheduled trigger
    /// will fire.  If this method is being called do to some other even (rather
    /// than scheduling a trigger), the caller should pass null.
    /// </param>
    public void SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc)
    {
        lock (sigLock)
        {
            signaled = true;
            signaledNextFireTimeUtc = candidateNewNextFireTimeUtc;
            Monitor.PulseAll(sigLock);
        }
    }

    public void ClearSignaledSchedulingChange()
    {
        lock (sigLock)
        {
            signaled = false;
            signaledNextFireTimeUtc = SchedulerConstants.SchedulingSignalDateTime;
        }
    }

    public bool IsScheduleChanged()
    {
        lock (sigLock)
        {
            return signaled;
        }
    }

    public DateTimeOffset? GetSignaledNextFireTimeUtc()
    {
        lock (sigLock)
        {
            return signaledNextFireTimeUtc;
        }
    }

    /// <summary>
    /// The main processing loop of the <see cref="QuartzSchedulerThread" />.
    /// </summary>
    public async Task Run()
    {
        int acquiresFailed = 0;
        Context.CallerId.Value = Guid.NewGuid();

        while (!halted)
        {
            cancellationTokenSource.Token.ThrowIfCancellationRequested();
            try
            {
                // check if we're supposed to pause...
                lock (sigLock)
                {
                    while (paused && !halted)
                    {
                        try
                        {
                            // wait until togglePause(false) is called...
                            Monitor.Wait(sigLock, 1000);
                        }
                        catch (ThreadInterruptedException)
                        {
                        }

                        // reset failure counter when paused, so that we don't
                        // wait again after unpausing
                        acquiresFailed = 0;
                    }

                    if (halted)
                    {
                        break;
                    }
                }

                // wait a bit, if reading from job store is consistently
                // failing (e.g. DB is down or restarting)..
                if (acquiresFailed > 1)
                {
                    try
                    {
                        var delay = ComputeDelayForRepeatedErrors(qsRsrcs.JobStore, acquiresFailed);
                        await Task.Delay(delay).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }

                cancellationTokenSource.Token.ThrowIfCancellationRequested();
                int availThreadCount = qsRsrcs.ThreadPool.BlockForAvailableThreads();
                lock (sigLock)
                {
                    if (halted)
                    {
                        break;
                    }
                }
                if (availThreadCount > 0)
                {
                    List<IOperableTrigger> triggers;

                    DateTimeOffset now = qsRsrcs.TimeProvider.GetUtcNow();

                    ClearSignaledSchedulingChange();
                    try
                    {
                        var noLaterThan = now + qsRsrcs.IdleWaitTime;
                        var maxCount = Math.Min(availThreadCount, qsRsrcs.MaxBatchSize);
                        triggers = new List<IOperableTrigger>(await qsRsrcs.JobStore.AcquireNextTriggers(noLaterThan, maxCount, qsRsrcs.BatchTimeWindow, CancellationToken.None).ConfigureAwait(false));
                        acquiresFailed = 0;
                        if (logger.IsEnabled(LogLevel.Debug))
                        {
                            logger.LogDebug("Batch acquisition of {TriggerCount} triggers", triggers.Count);
                        }
                    }
                    catch (JobPersistenceException jpe)
                    {
                        if (acquiresFailed == 0)
                        {
                            var msg = "An error occurred while scanning for the next trigger to fire.";
                            await qs.NotifySchedulerListenersError(msg, jpe, CancellationToken.None).ConfigureAwait(false);
                        }

                        if (acquiresFailed < int.MaxValue)
                        {
                            acquiresFailed++;
                        }

                        continue;
                    }
                    catch (Exception e)
                    {
                        if (acquiresFailed == 0)
                        {
                            logger.LogError(e, "quartzSchedulerThreadLoop: RuntimeException {Message}", e.Message);
                        }
                        if (acquiresFailed < int.MaxValue)
                        {
                            acquiresFailed++;
                        }
                        continue;
                    }

                    if (triggers is not null && triggers.Count > 0)
                    {
                        now = qsRsrcs.TimeProvider.GetUtcNow();
                        DateTimeOffset triggerTime = triggers[0].GetNextFireTimeUtc()!.Value;
                        TimeSpan timeUntilTrigger = triggerTime - now;

                        while (timeUntilTrigger > TimeSpan.Zero)
                        {
                            if (await ReleaseIfScheduleChangedSignificantly(triggers, triggerTime).ConfigureAwait(false))
                            {
                                break;
                            }
                            lock (sigLock)
                            {
                                if (halted)
                                {
                                    break;
                                }
                                if (!IsCandidateNewTimeEarlierWithinReason(triggerTime, false))
                                {
                                    try
                                    {
                                        // we could have blocked a long while
                                        // on 'synchronize', so we must recompute
                                        now = qsRsrcs.TimeProvider.GetUtcNow();
                                        timeUntilTrigger = triggerTime - now;
                                        if (timeUntilTrigger > TimeSpan.Zero)
                                        {
                                            Monitor.Wait(sigLock, timeUntilTrigger);
                                        }
                                    }
                                    catch (ThreadInterruptedException)
                                    {
                                    }
                                }
                            }
                            lock (sigLock)
                            {
                                if (halted)
                                {
                                    break;
                                }
                            }
                            if (await ReleaseIfScheduleChangedSignificantly(triggers, triggerTime).ConfigureAwait(false))
                            {
                                break;
                            }
                            now = qsRsrcs.TimeProvider.GetUtcNow();
                            timeUntilTrigger = triggerTime - now;
                        }

                        // this happens if releaseIfScheduleChangedSignificantly decided to release triggers
                        if (triggers.Count == 0)
                        {
                            continue;
                        }

                        // set triggers to 'executing'
                        List<TriggerFiredResult> bndles = new List<TriggerFiredResult>();

                        bool goAhead;
                        lock (sigLock)
                        {
                            goAhead = !halted;
                        }

                        if (goAhead)
                        {
                            try
                            {
                                var res = await qsRsrcs.JobStore.TriggersFired(triggers, CancellationToken.None).ConfigureAwait(false);
                                if (res is not null)
                                {
                                    bndles = res.ToList();
                                }
                            }
                            catch (SchedulerException se)
                            {
                                var msg = "An error occurred while firing triggers '" + triggers + "'";
                                await qs.NotifySchedulerListenersError(msg, se, CancellationToken.None).ConfigureAwait(false);
                                // QTZ-179 : a problem occurred interacting with the triggers from the db
                                // we release them and loop again
                                foreach (IOperableTrigger t in triggers)
                                {
                                    await qsRsrcs.JobStore.ReleaseAcquiredTrigger(t, CancellationToken.None).ConfigureAwait(false);
                                }
                                continue;
                            }
                        }

                        for (int i = 0; i < bndles.Count; i++)
                        {
                            TriggerFiredResult result = bndles[i];
                            var bndle = result.TriggerFiredBundle;
                            var exception = result.Exception;

                            IOperableTrigger trigger = triggers[i];
                            // TODO SQL exception?
                            if (exception is not null && (exception is DbException || exception.InnerException is DbException))
                            {
                                logger.LogError(exception, "DbException while firing trigger {Trigger}", trigger);
                                await qsRsrcs.JobStore.ReleaseAcquiredTrigger(trigger, CancellationToken.None).ConfigureAwait(false);
                                continue;
                            }

                            // it's possible to get 'null' if the triggers was paused,
                            // blocked, or other similar occurrences that prevent it being
                            // fired at this time...  or if the scheduler was shutdown (halted)
                            if (bndle is null)
                            {
                                await qsRsrcs.JobStore.ReleaseAcquiredTrigger(trigger, CancellationToken.None).ConfigureAwait(false);
                                continue;
                            }

                            // TODO: improvements:
                            //
                            // 2- make sure we can get a job runshell before firing trigger, or
                            //   don't let that throw an exception (right now it never does,
                            //   but the signature says it can).
                            // 3- acquire more triggers at a time (based on num threads available?)

                            JobRunShell shell;
                            try
                            {
                                shell = qsRsrcs.JobRunShellFactory.CreateJobRunShell(bndle);
                                await shell.Initialize(qs, CancellationToken.None).ConfigureAwait(false);
                            }
                            catch (SchedulerException se)
                            {
                                if (se.InnerException is ObjectDisposedException or OperationCanceledException || cancellationTokenSource.Token.IsCancellationRequested)
                                {
                                    // the scheduler is being stopped, so we can't run the job
                                    await qsRsrcs.JobStore.ReleaseAcquiredTrigger(trigger, CancellationToken.None).ConfigureAwait(false);
                                }
                                else
                                {
                                    // we consider this a serious error and expect that job instantiation will never succeed in the future either
                                    await qsRsrcs.JobStore.TriggeredJobComplete(trigger, bndle.JobDetail, SchedulerInstruction.SetAllJobTriggersError, CancellationToken.None).ConfigureAwait(false);
                                }

                                continue;
                            }

                            var threadPoolRunResult = qsRsrcs.ThreadPool.RunInThread(() => shell.Run(CancellationToken.None));
                            if (!threadPoolRunResult)
                            {
                                // Check if the scheduler is being shutdown
                                if (halted || cancellationTokenSource.Token.IsCancellationRequested)
                                {
                                    // Scheduler is shutting down, release the trigger gracefully
                                    logger.LogDebug("ThreadPool.RunInThread() returned false due to scheduler shutdown, releasing trigger");
                                    await qsRsrcs.JobStore.ReleaseAcquiredTrigger(trigger, CancellationToken.None).ConfigureAwait(false);
                                }
                                else
                                {
                                    // this case should never happen, as it is indicative of a bug in the thread pool or
                                    // a thread pool being used concurrently - which the docs say not to do...
                                    logger.LogError("ThreadPool.RunInThread() returned false");
                                    await qsRsrcs.JobStore.TriggeredJobComplete(trigger, bndle.JobDetail, SchedulerInstruction.SetAllJobTriggersError, CancellationToken.None).ConfigureAwait(false);
                                }
                            }
                        }

                        continue; // while (!halted)
                    }
                }
                else // if(availThreadCount > 0)
                {
                    continue;
                    // while (!halted)
                }

                TimeSpan timeUntilContinue = GetRandomizedIdleWaitTime();
                lock (sigLock)
                {
                    if (!halted)
                    {
                        try
                        {
                            // QTZ-336 A job might have been completed in the mean time and we might have
                            // missed the scheduled changed signal by not waiting for the notify() yet
                            // Check that before waiting for too long in case this very job needs to be
                            // scheduled very soon
                            if (!IsScheduleChanged())
                            {
                                Monitor.Wait(sigLock, timeUntilContinue);
                            }
                        }
                        catch (ThreadInterruptedException)
                        {
                        }
                    }
                }
            }
            catch (Exception re)
            {
                logger.LogError(re, "Runtime error occurred in main trigger firing loop.");
            }
        } // while (!halted)
    }

    private static readonly TimeSpan minDelay = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan maxDelay = TimeSpan.FromMinutes(10);

    private static TimeSpan ComputeDelayForRepeatedErrors(IJobStore jobStore, int acquiresFailed)
    {
        var delay = TimeSpan.FromMilliseconds(100);
        try
        {
            delay = jobStore.GetAcquireRetryDelay(acquiresFailed);
        }
        catch
        {
            // we're trying to be useful in case of error states, not cause
            // additional errors..
        }

        // sanity check per getAcquireRetryDelay specification
        if (delay < minDelay)
        {
            delay = minDelay;
        }

        if (delay > maxDelay)
        {
            delay = maxDelay;
        }

        return delay;
    }

    private async ValueTask<bool> ReleaseIfScheduleChangedSignificantly(List<IOperableTrigger> triggers, DateTimeOffset triggerTime)
    {
        if (IsCandidateNewTimeEarlierWithinReason(triggerTime, true))
        {
            foreach (IOperableTrigger trigger in triggers)
            {
                // above call does a clearSignaledSchedulingChange()
                await qsRsrcs.JobStore.ReleaseAcquiredTrigger(trigger).ConfigureAwait(false);
            }
            triggers.Clear();
            return true;
        }

        return false;
    }

    private bool IsCandidateNewTimeEarlierWithinReason(DateTimeOffset oldTimeUtc, bool clearSignal)
    {
        // So here's the deal: We know due to being signaled that 'the schedule'
        // has changed.  We may know (if getSignaledNextFireTime() != DateTimeOffset.MinValue) the
        // new earliest fire time.  We may not (in which case we will assume
        // that the new time is earlier than the trigger we have acquired).
        // In either case, we only want to abandon our acquired trigger and
        // go looking for a new one if "it's worth it".  It's only worth it if
        // the time cost incurred to abandon the trigger and acquire a new one
        // is less than the time until the currently acquired trigger will fire,
        // otherwise we're just "thrashing" the job store (e.g. database).
        //
        // So the question becomes when is it "worth it"?  This will depend on
        // the job store implementation (and of course the particular database
        // or whatever behind it).  Ideally we would depend on the job store
        // implementation to tell us the amount of time in which it "thinks"
        // it can abandon the acquired trigger and acquire a new one.  However
        // we have no current facility for having it tell us that, so we make
        // a somewhat educated but arbitrary guess.

        lock (sigLock)
        {
            if (!IsScheduleChanged())
            {
                return false;
            }

            bool earlier = false;

            if (!GetSignaledNextFireTimeUtc().HasValue)
            {
                earlier = true;
            }
            else if (GetSignaledNextFireTimeUtc()!.Value < oldTimeUtc)
            {
                earlier = true;
            }

            if (earlier)
            {
                // so the new time is considered earlier, but is it enough earlier?
                TimeSpan diff = oldTimeUtc - qsRsrcs.TimeProvider.GetUtcNow();
                if (diff < (qsRsrcs.JobStore.SupportsPersistence ? TimeSpan.FromMilliseconds(70) : TimeSpan.FromMilliseconds(7)))
                {
                    earlier = false;
                }
            }

            if (clearSignal)
            {
                ClearSignaledSchedulingChange();
            }

            return earlier;
        }
    }

    public void Start()
    {
        cancellationTokenSource = new CancellationTokenSource();
        task = Task.Run(Run);
    }

    public async Task Shutdown()
    {
        cancellationTokenSource.Cancel();
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }
}