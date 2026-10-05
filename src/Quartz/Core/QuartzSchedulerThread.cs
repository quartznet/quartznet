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

using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;

using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.Util;

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
    private readonly Lock sigLock = new();
    private readonly SemaphoreSlim schedulingChangeSignal = new(0);
    private readonly SemaphoreSlim pauseSignal = new(0);

    private bool signaled;
    private DateTimeOffset? signaledNextFireTimeUtc;

    /// <summary>
    /// When the loop will next look at the store, while it is parked until then: the fire time of the
    /// trigger it holds, or the end of its idle wait. <see langword="null" /> whenever it is anywhere
    /// else, which makes every signal wake it. Guarded by <see cref="sigLock" />.
    /// </summary>
    private DateTimeOffset? nextLookUtc;

    /// <summary>
    /// How many signals have released the loop, as against being answered by its next look. Guarded by
    /// <see cref="sigLock" />.
    /// </summary>
    private long schedulingWakes;

    private volatile bool paused;
    private volatile bool halted;

    private readonly ConcurrentDictionary<string, int> runningExecutionGroupCounts = new(StringComparer.Ordinal);

    /// <summary>
    /// What the thread pool is handed for every firing, with the run shell as its state. Static, so a
    /// dispatch closes over nothing.
    /// </summary>
    private static readonly Func<object?, ValueTask> runJobRunShell =
        static state => ((JobRunShell) state!).Run(CancellationToken.None);

    /// <summary>
    /// What every wait's timer does when it comes due: release the semaphore the loop is waiting on,
    /// handed over as the timer's state. Static, so arming a wait closes over nothing.
    /// </summary>
    private static readonly TimerCallback releaseSignal = static state => ((SemaphoreSlim) state!).Release();

    private readonly CancellationTokenSource cancellationTokenSource = new();

    /// <summary>
    /// Guards the <see cref="task"/> and <see cref="shutDown"/> transitions, so that a start racing a
    /// shutdown cannot produce a second processing loop or touch a disposed cancellation source.
    /// </summary>
    private readonly Lock stateLock = new();

    /// <summary>
    /// The processing loop, or <see langword="null" /> until <see cref="Start" /> has been called.
    /// </summary>
    private Task? task;

    /// <summary>
    /// Whether <see cref="Shutdown"/> has run, after which the cancellation source is disposed.
    /// </summary>
    private bool shutDown;

    /// <summary>
    /// How long a paused loop sleeps before it looks at the pause flag again, on the scheduler's clock.
    /// Resuming releases the loop at once; this only bounds a pause nobody resumes.
    /// </summary>
    private static readonly TimeSpan pausedWaitCheckInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long a round that acquired nothing waits before the loop looks again, the first time, once due
    /// work is blocked behind a running firing of a job that disallows concurrent execution. Each such
    /// round after it waits twice as long, up to the idle wait (#3988).
    /// </summary>
    internal static readonly TimeSpan BlockedRetryStart = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// How long the next round that acquires nothing waits before the loop looks again, while due work is
    /// blocked behind a firing whose end this loop may not be told of; <see cref="TimeSpan.Zero" /> when
    /// none is, which leaves such a round its idle wait. Only the loop reads and writes it.
    /// </summary>
    private TimeSpan blockedRetry;

    /// <summary>
    /// When the latest firing holding this node's pinned triggers was fired, as the last round that found
    /// any held said. Only the loop reads and writes it.
    /// </summary>
    private DateTimeOffset? lastBlockingFiredUtc;

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
    /// Gets a value indicating whether the processing loop has been started.
    /// </summary>
    /// <value>
    /// <see langword="true"/> once <see cref="Start"/> has been called; otherwise, <see langword="false"/>.
    /// </value>
    internal bool Running => task is not null;

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
    /// When the loop will next look at the store, if it is parked until then; otherwise
    /// <see langword="null" />.
    /// </summary>
    internal DateTimeOffset? NextLookUtc
    {
        get
        {
            lock (sigLock)
            {
                return nextLookUtc;
            }
        }
    }

    /// <summary>
    /// How many scheduling signals have released the loop. One the loop's next look answers anyway
    /// is not counted.
    /// </summary>
    internal long SchedulingWakes
    {
        get
        {
            lock (sigLock)
            {
                return schedulingWakes;
            }
        }
    }

    /// <summary>
    /// Construct a new <see cref="QuartzSchedulerThread" /> for the given
    /// <see cref="QuartzScheduler" />. The scheduling loop runs as a task on the
    /// thread pool rather than on a dedicated thread.
    /// </summary>
    /// <param name="qs">The scheduler.</param>
    /// <param name="qsRsrcs">The resources.</param>
    internal QuartzSchedulerThread(QuartzScheduler qs, QuartzSchedulerResources qsRsrcs)
    {
        // From the resources rather than from the ambient factory: the loop is built by the container
        // along with everything else, and its acquisition failures are the log lines an application most
        // needs without having had to opt into them.
        this.logger = qsRsrcs.LoggerFactory.CreateLogger<QuartzSchedulerThread>();
        this.qs = qs;
        this.qsRsrcs = qsRsrcs;
        idleWaitVariableness = (int) (qsRsrcs.IdleWaitTime.TotalMilliseconds * 0.2);

        // The ungrouped bucket is where every trigger that names no execution group is counted, and in
        // most schedules that is all of them. Removing its entry when it reaches zero and adding it
        // back on the next firing is a ConcurrentDictionary node allocated and thrown away per firing
        // for a key that is never absent for long, so it is resident instead (#3802). A resident zero
        // is invisible: ExecutionLimits.SubtractInFlight subtracts nothing for a count of zero, which
        // is why the entry can stay without changing a single acquisition's limits. The removal is
        // what keeps a high-cardinality set of *named* groups from growing without bound, and named
        // groups still get it.
        runningExecutionGroupCounts[ExecutionLimits.DefaultGroupKey] = 0;

        // Construction does not start the loop; QuartzScheduler.Start does. Until then this object is
        // in the 'paused' state so that processing does not begin even once it is started.
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
        }

        if (pause)
        {
            // Drain stale resume permits so the paused wait loop blocks properly. CancellationToken.None
            // deliberately: a zero timeout never waits, so there is nothing here to cancel.
            while (pauseSignal.Wait(0, CancellationToken.None)) { }
            SignalSchedulingChange(SchedulerConstants.SchedulingSignalDateTime);
        }
        else
        {
            pauseSignal.Release();
        }
    }

    /// <summary>
    /// Signals the main processing loop to stop at the next possible point.
    /// </summary>
    internal async Task Halt(bool wait)
    {
        bool wasPaused;
        lock (sigLock)
        {
            wasPaused = paused;
            halted = true;
        }

        // Release the pause signal only if the loop is actually paused,
        // otherwise we'd accumulate a stale permit on repeated Halt() calls.
        if (wasPaused)
        {
            pauseSignal.Release();
        }

        Task? running;
        lock (stateLock)
        {
            // Shutdown has already cancelled and disposed the source; cancelling it again would throw.
            if (shutDown)
            {
                return;
            }

            running = task;
        }

        await cancellationTokenSource.CancelAsync().ConfigureAwait(false);

        // There is nothing to wait for when the loop was never started.
        if (wait && running is not null)
        {
            try
            {
                await running.ConfigureAwait(false);
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
    /// <remarks>
    /// <para>
    /// <b>A trigger that fires after the loop's next look does not wake it</b> (#3865). The look asks the
    /// store for everything due within the idle wait time of it, so it finds the trigger without being
    /// told, and a wake would only cost a round: an allocation and a thread hop in memory, a round trip
    /// to the database on a persistent store, for every far-future schedule. Such a signal changes
    /// nothing at all, not even the candidate recorded for an earlier one.
    /// </para>
    /// <para>
    /// Everything else wakes it: <see langword="null" />, which says nothing about a time; the sentinel
    /// that only asks the loop to look; any candidate while the loop is not parked until a known look;
    /// and any candidate once that look is due, which is also what a wall clock stepped forward
    /// past it looks like.
    /// </para>
    /// </remarks>
    public void SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc)
    {
        lock (sigLock)
        {
            if (candidateNewNextFireTimeUtc is { } candidate
                && nextLookUtc is { } nextLook
                && candidate > nextLook
                && candidate != SchedulerConstants.SchedulingSignalDateTime
                && qsRsrcs.TimeProvider.GetUtcNow() < nextLook)
            {
                return;
            }

            // The earlier of this candidate and one already waiting, with no time counting as earliest.
            // A later schedule used to replace an earlier one the loop had not read yet - the sentinel a
            // pause sends included, so a schedule made just after a pause could keep the loop holding,
            // and firing, a trigger it acquired as the pause landed.
            signaledNextFireTimeUtc = !signaled
                ? candidateNewNextFireTimeUtc
                : signaledNextFireTimeUtc is { } waiting && candidateNewNextFireTimeUtc is { } incoming
                    ? incoming < waiting ? incoming : waiting
                    : null;
            signaled = true;
            schedulingWakes++;
        }

        schedulingChangeSignal.Release();
    }

    /// <summary>
    /// Says when the loop will next look at the store, for the wait it is about to park in, or that it
    /// is not parked until a known look.
    /// </summary>
    private void PublishNextLook(DateTimeOffset? nextLook)
    {
        lock (sigLock)
        {
            nextLookUtc = nextLook;
        }
    }

    public void ClearSignaledSchedulingChange()
    {
        lock (sigLock)
        {
            signaled = false;
            signaledNextFireTimeUtc = SchedulerConstants.SchedulingSignalDateTime;
        }

        // Drain any buffered permits so WaitAsync blocks cleanly on next call.
        // Prevents stale permits from causing unbounded spurious wakeups. CancellationToken.None
        // deliberately: a zero timeout never waits, so there is nothing here to cancel.
        while (schedulingChangeSignal.Wait(0, CancellationToken.None)) { }
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
        // Nothing this loop does belongs to the call that started the scheduler, but the task it runs
        // on captured that call's execution context and Activity.Current travels in it — so a loop
        // that lives as long as the process would file every span below under one trace, forever
        // (#3797). Cleared rather than flow-suppressed, which would take every other AsyncLocal with it.
        Activity.Current = null;

        int acquiresFailed = 0;

        // The identity this loop takes job-store locks under, for the lifetime of the loop. No
        // execution context to publish: the loop is not a firing, so what it begins here is only the
        // caller id.
        _ = AmbientJobExecution.Begin(Guid.NewGuid());

        // Everything this loop logs names the scheduler it belongs to. An ILogger category is a type
        // name, so without this two tenants' acquisition failures, misfire reports and shutdown notices
        // are the same line written twice with no way to tell which scheduler wrote which. Opened for the
        // lifetime of the loop, which is the lifetime of the scheduler's own thread.
        using IDisposable? schedulerScope = logger.BeginScope(qsRsrcs.LogScope);

        // What ends each wait when nothing signals it: a timer on the scheduler's clock per semaphore,
        // for the lifetime of the loop, armed by WaitForSignal. Created unarmed.
        using ITimer schedulingWake = qsRsrcs.TimeProvider.CreateTimer(releaseSignal, schedulingChangeSignal, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        using ITimer pauseWake = qsRsrcs.TimeProvider.CreateTimer(releaseSignal, pauseSignal, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        while (!halted)
        {
            cancellationTokenSource.Token.ThrowIfCancellationRequested();

            // What this round acquired and has not yet had fired. Released by the catch at the bottom if
            // something unexpected ends the round first, or the store keeps it reserved for a firing that
            // never comes (#3974). Once the store has answered, its results say what is left to release.
            List<IOperableTrigger>? unfired = null;
            try
            {
                // check if we're supposed to pause...
                while (paused && !halted)
                {
                    try
                    {
                        // wait until togglePause(false) is called...
                        await WaitForSignal(pauseSignal, pauseWake, qsRsrcs.TimeProvider.GetUtcNow(), pausedWaitCheckInterval, cancellationTokenSource.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    // reset failure counter when paused, so that we don't
                    // wait again after unpausing
                    acquiresFailed = 0;
                }

                if (halted)
                {
                    break;
                }

                // wait a bit, if reading from job store is consistently
                // failing (e.g. DB is down or restarting)..
                if (acquiresFailed > 1)
                {
                    try
                    {
                        var delay = ComputeDelayForRepeatedErrors(qsRsrcs.JobStore, acquiresFailed);
                        // Cancellable so that shutdown does not stall for the remainder of the
                        // back-off; the catch swallows the OperationCanceledException and the
                        // halted/cancellation checks right below exit the loop.
                        await Task.Delay(delay, qsRsrcs.TimeProvider, cancellationTokenSource.Token).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }

                cancellationTokenSource.Token.ThrowIfCancellationRequested();
                int availThreadCount = await qsRsrcs.ThreadPool.WaitForAvailableThreads(cancellationTokenSource.Token).ConfigureAwait(false);
                if (halted)
                {
                    break;
                }

                // The reading this round's acquisition is based on. The idle wait at the bottom of the
                // loop counts from it too, rather than from a fresh one: the store was asked about the
                // time as of this reading, so a clock that has moved since must end that wait at once
                // rather than start it again from the new time.
                DateTimeOffset now;

                // Whether the store says due triggers are held back behind a running firing this round,
                // which shortens the wait at the bottom of the loop when the round acquired nothing, and
                // when the latest firing holding them was fired.
                bool heldBack = false;
                DateTimeOffset? blockingFiredUtc = null;
                if (availThreadCount > 0)
                {
                    List<IOperableTrigger> triggers;
                    TriggerAcquisitionResult acquisition;

                    now = qsRsrcs.TimeProvider.GetUtcNow();

                    ClearSignaledSchedulingChange();

                    // A pause that landed since the check at the top signalled a change, and the line
                    // above has just drained it. Without the signal nothing would end the idle wait this
                    // round is heading for, so the loop would only notice the pause when that wait ran
                    // out - never, on a clock nobody advances.
                    if (paused)
                    {
                        continue;
                    }

                    // Zero when nothing is collecting the acquisition instruments, which is the signal to
                    // skip the measurement rather than a timestamp of zero.
                    bool measureAcquisition = false;
                    long acquisitionStarted = 0;
                    try
                    {
                        ExecutionLimits? availableLimits = ComputeAvailableExecutionGroupLimits();
                        TriggerAcquisitionRequest request = new()
                        {
                            NoLaterThan = now + qsRsrcs.IdleWaitTime,
                            MaxCount = Math.Min(availThreadCount, qsRsrcs.MaxBatchSize),
                            TimeWindow = qsRsrcs.BatchTimeWindow,
                            ExecutionLimits = availableLimits,
                        };
                        measureAcquisition = qsRsrcs.Meters.TriggerAcquisitionEnabled;
                        acquisitionStarted = measureAcquisition ? qsRsrcs.TimeProvider.GetTimestamp() : 0;

                        // What is due already comes back fired, in the store's one round trip, and only
                        // what is due later comes back pending, to be waited for and fired below (#3864).
                        // A store that does not fire on acquisition answers everything pending, which is
                        // what the loop has always done with an acquisition.
                        acquisition = await qsRsrcs.JobStore.AcquireNextTriggersAndFireDue(request, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (JobPersistenceException jpe)
                    {
                        if (acquiresFailed == 0)
                        {
                            // No keys: the scan never got as far as a trigger, so there is nothing this
                            // failure is about beyond the store it came from.
                            SchedulerErrorContext error = new()
                            {
                                Message = "An error occurred while scanning for the next trigger to fire.",
                                Exception = jpe,
                            };
                            await qs.NotifySchedulerListenersError(error, CancellationToken.None).ConfigureAwait(false);
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
                            logger.SchedulerThreadLoopFailed(e.Message, e);
                        }
                        if (acquiresFailed < int.MaxValue)
                        {
                            acquiresFailed++;
                        }
                        continue;
                    }

                    // The store has answered. What it fired is committed and what it left pending is this
                    // round's, so from here nothing may end the round without dispatching the one and
                    // releasing the other — not even an instrument or a logger that throws, which is why
                    // they are not in the call's try above, whose catches just go round again (#3864).
                    acquiresFailed = 0;
                    heldBack = acquisition.Blocked > 0;
                    blockingFiredUtc = acquisition.LatestBlockingFiredUtc;

                    // Copied on purpose, and IJobStore.AcquireNextTriggers says so: this loop removes entries
                    // below while it waits out the first trigger's fire time, and the store is allowed to
                    // hand back a list it still holds. The copy is around ten nanoseconds and sixty-four bytes
                    // per attempt, measured in AcquiredTriggerHandoffBenchmark, which is nothing beside the
                    // round trip a persistent store just made — and far less than a caller-owns rule would
                    // cost the stores nobody here can see (#3344). An empty list is never edited, so there is
                    // nothing to copy.
                    List<IOperableTrigger> pending = acquisition.Pending;
                    triggers = pending.Count > 0 ? new List<IOperableTrigger>(pending) : pending;

                    // Released by the catch at the bottom if anything below throws before they are fired.
                    unfired = triggers.Count > 0 ? triggers : null;
                    try
                    {
                        RecordAcquisition(measureAcquisition, acquisitionStarted, acquisition.Due.Count + triggers.Count);
                    }
                    finally
                    {
                        if (acquisition.Due.Count > 0)
                        {
                            // Fired by the store as it acquired them, so they run now, ahead of any wait for
                            // what is pending: the store has committed them, and a firing it committed is never
                            // one nobody runs (#3746).
                            await DispatchFiredTriggers(acquisition.Due, acquisition.Fired).ConfigureAwait(false);
                        }
                    }

                    if (triggers.Count > 0)
                    {
                        unfired = triggers;
                        now = qsRsrcs.TimeProvider.GetUtcNow();
                        DateTimeOffset triggerTime = triggers[0].NextFireTimeUtc!.Value;
                        TimeSpan timeUntilTrigger = triggerTime - now;

                        while (timeUntilTrigger > TimeSpan.Zero)
                        {
                            if (await ReleaseIfScheduleChangedSignificantly(triggers, triggerTime).ConfigureAwait(false))
                            {
                                break;
                            }
                            if (halted)
                            {
                                break;
                            }
                            if (!IsCandidateNewTimeEarlierWithinReason(triggerTime, false))
                            {
                                // we could have blocked a long while
                                // on 'synchronize', so we must recompute
                                now = qsRsrcs.TimeProvider.GetUtcNow();
                                timeUntilTrigger = triggerTime - now;
                                if (timeUntilTrigger > TimeSpan.Zero)
                                {
                                    // The next look is after this batch has fired, and anything due later
                                    // than the trigger held here is found by it.
                                    PublishNextLook(triggerTime);
                                    try
                                    {
                                        // Cap the wait time to recover from system clock backward jumps.
                                        // The outer while loop recomputes timeUntilTrigger from the current clock after each wait.
                                        TimeSpan waitTime = timeUntilTrigger < qsRsrcs.IdleWaitTime ? timeUntilTrigger : qsRsrcs.IdleWaitTime;
                                        await WaitForSignal(schedulingChangeSignal, schedulingWake, now, waitTime, cancellationTokenSource.Token).ConfigureAwait(false);
                                    }
                                    catch (OperationCanceledException)
                                    {
                                        break;
                                    }
                                    finally
                                    {
                                        PublishNextLook(null);
                                    }
                                }
                            }
                            if (halted)
                            {
                                break;
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

                        if (halted)
                        {
                            // Scheduler is shutting down - release acquired triggers
                            // so they don't remain stuck in ACQUIRED state
                            foreach (IOperableTrigger t in triggers)
                            {
                                await SafeReleaseAcquiredTrigger(t, "during shutdown").ConfigureAwait(false);
                            }
                            continue;
                        }

                        // set triggers to 'executing'
                        List<TriggerFiredResult> bundles;
                        try
                        {
                            // The store hands back a list it built for this call and does not keep it,
                            // and nothing below mutates it, so it is read as-is rather than copied.
                            bundles = await qsRsrcs.JobStore.TriggersFired(triggers, CancellationToken.None).ConfigureAwait(false);
                            unfired = null;
                        }
                        catch (SchedulerException se)
                        {
                            var msg = "An error occurred while firing triggers '" + string.Join(", ", triggers.Select(t => t.Key)) + "'";

                            // The whole batch failed, so the trigger key is only reported when the batch
                            // held one trigger. Naming one of several would say the failure was about
                            // that trigger, which it is not — the message enumerates them all instead.
                            SchedulerErrorContext error = new()
                            {
                                Message = msg,
                                Exception = se,
                                TriggerKey = triggers.Count == 1 ? triggers[0].Key : null,
                                JobKey = triggers.Count == 1 ? triggers[0].JobKey : null,
                            };
                            await qs.NotifySchedulerListenersError(error, CancellationToken.None).ConfigureAwait(false);
                            // QTZ-179 : a problem occurred interacting with the triggers from the db
                            // we release them and loop again
                            foreach (IOperableTrigger t in triggers)
                            {
                                await SafeReleaseAcquiredTrigger(t, "after TriggersFired failure").ConfigureAwait(false);
                            }
                            continue;
                        }

                        await DispatchFiredTriggers(triggers, bundles).ConfigureAwait(false);

                        continue; // while (!halted)
                    }

                    // A round that fired everything it acquired goes straight back for more, as one that
                    // fired a batch after waiting for it does.
                    if (acquisition.Due.Count > 0)
                    {
                        continue; // while (!halted)
                    }
                }
                else // if(availThreadCount > 0)
                {
                    continue;
                    // while (!halted)
                }

                TimeSpan timeUntilContinue = GetWaitAfterEmptyRound(heldBack, blockingFiredUtc);
                if (!halted && !IsScheduleChanged())
                {
                    // The next look is when this wait ends.
                    PublishNextLook(now + timeUntilContinue);
                    try
                    {
                        // QTZ-336 A job might have been completed in the mean time and we might have
                        // missed the scheduled changed signal by not waiting for the notify() yet
                        // Check that before waiting for too long in case this very job needs to be
                        // scheduled very soon
                        await WaitForSignal(schedulingChangeSignal, schedulingWake, now, timeUntilContinue, cancellationTokenSource.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    finally
                    {
                        PublishNextLook(null);
                    }
                }
            }
            catch (Exception re)
            {
                logger.TriggerFiringLoopFailed(re);

                // A store that fails the batch with something other than a SchedulerException lands
                // here, and so does anything else between acquiring and firing.
                if (unfired is not null)
                {
                    foreach (IOperableTrigger t in unfired)
                    {
                        await SafeReleaseAcquiredTrigger(t, "after the firing loop failed").ConfigureAwait(false);
                    }
                }
            }
        } // while (!halted)
    }

    /// <summary>
    /// Waits until <paramref name="signal" /> is released, or until <paramref name="wait" /> has passed
    /// on the scheduler's clock since <paramref name="from" />, whichever comes first. This is each of
    /// the loop's three waits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The timeout is <paramref name="wake" />, a timer on the scheduler's
    /// <see cref="TimeProvider" /></b> that releases <paramref name="signal" />, rather than the timeout
    /// <see cref="SemaphoreSlim.WaitAsync(TimeSpan, CancellationToken)" /> arms, which only knows elapsed
    /// time. That is what lets a test advance a fake clock and see the loop act on it (#3869). On
    /// <see cref="TimeProvider.System" /> the timer is a <see cref="System.Threading.Timer" />, which is
    /// what the semaphore's own timeout is built on, so a deployment waits as it always did.
    /// </para>
    /// <para>
    /// <b>The timer is the loop's, re-armed for every wait</b> rather than created for it: the pre-fire
    /// wait is on the path of every firing that is not already late, and a timer made and thrown away
    /// there would be a timer per firing. Re-arming replaces whatever the last wait left armed.
    /// </para>
    /// <para>
    /// <b>The wait is counted from <paramref name="from" /></b>, the clock reading the decision to wait
    /// was based on, so a clock that moves after that reading is noticed rather than waited out: before
    /// the timer is armed the time left comes out as nothing, while it is being armed the second reading
    /// catches it, and once it is armed the move fires it. It is never longer than
    /// <paramref name="wait" />, so a wall clock stepped back since the reading cannot stretch it.
    /// </para>
    /// <para>
    /// <b>A wait a real release ended leaves its timer armed</b>, and a timer that comes due after that
    /// leaves a permit behind. That is a spurious wake, which the loop already survives: each
    /// acquisition round drains the scheduling signal before it asks the store, a pre-fire wait that
    /// returns early is simply waited again, and pausing drains the pause signal.
    /// </para>
    /// <para>
    /// Only the waits for the schedule are on the scheduler's clock. Cancellation still ends a wait at
    /// once, so a shutdown does not depend on anybody advancing a fake clock.
    /// </para>
    /// </remarks>
    private Task WaitForSignal(SemaphoreSlim signal, ITimer wake, DateTimeOffset from, TimeSpan wait, CancellationToken cancellationToken)
    {
        TimeProvider clock = qsRsrcs.TimeProvider;
        DateTimeOffset deadlineUtc = from + wait;

        TimeSpan remaining = deadlineUtc - clock.GetUtcNow();
        if (remaining > wait)
        {
            remaining = wait;
        }

        // The longest a timer can be armed for. An idle wait time beyond it ends there, and the loop
        // looks again, which is all a longer wait would have come to.
        if (remaining > TimerLimits.MaxDelay)
        {
            remaining = TimerLimits.MaxDelay;
        }

        if (remaining <= TimeSpan.Zero)
        {
            return Task.CompletedTask;
        }

        wake.Change(remaining, Timeout.InfiniteTimeSpan);

        // A clock moved between the reading above and the timer being armed has armed it from the new
        // time, so it would come due a whole wait after the deadline.
        if (clock.GetUtcNow() >= deadlineUtc)
        {
            return Task.CompletedTask;
        }

        return signal.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// How long a round that acquired nothing waits before the loop looks again: its idle wait, or less
    /// while due work is blocked behind a running firing whose end this loop may not be told of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A firing of a job that disallows concurrent execution holds the job's other triggers back until it
    /// ends, and its end wakes the scheduler it ran on. On a cluster that may be another node, so this one
    /// would find a trigger held back behind it — one it could not fire, one the store passed over, or one
    /// pinned to it — only after its idle wait, and a pinned trigger, which no other node may fire, waited
    /// that long every time (#3988). So the loop looks again after <see cref="BlockedRetryStart" />, and
    /// after twice as long each time it still finds nothing, until the wait is the idle wait: a job that
    /// runs for hours elsewhere costs a handful of looks, not a poll.
    /// </para>
    /// <para>
    /// A trigger the loop could not fire starts the count again, as it is fresh news. So does a round
    /// whose held triggers are held by a later firing than the last round's: the job ended and was taken
    /// again in between, and a job changing hands that often frees up often too. Otherwise a round the
    /// store says triggers are held back in starts the count only when it is not running already, since
    /// the store says so of the same triggers every round until the firing ends, and while it does the
    /// wait stays at the idle wait rather than starting over. A round with no such news lets the count run
    /// out. Nothing changes for a loop with nothing held back: every wait is the idle wait, as it was.
    /// </para>
    /// </remarks>
    /// <param name="heldBack">Whether the store said due triggers are held back behind a running firing this round.</param>
    /// <param name="blockingFiredUtc">When the latest firing holding this node's pinned triggers was fired, if the store said.</param>
    private TimeSpan GetWaitAfterEmptyRound(bool heldBack, DateTimeOffset? blockingFiredUtc)
    {
        TimeSpan idleWait = GetRandomizedIdleWaitTime();

        if (heldBack)
        {
            bool changedHands = blockingFiredUtc is not null && blockingFiredUtc != lastBlockingFiredUtc;
            lastBlockingFiredUtc = blockingFiredUtc;

            if (blockedRetry == TimeSpan.Zero || changedHands)
            {
                blockedRetry = BlockedRetryStart;
            }
        }

        if (blockedRetry == TimeSpan.Zero)
        {
            return idleWait;
        }

        TimeSpan wait = blockedRetry < idleWait ? blockedRetry : idleWait;

        // Doubled while that is still short of the idle wait; written as a comparison with the remainder
        // so that a configured idle wait near the limit of a TimeSpan cannot overflow it.
        TimeSpan limit = qsRsrcs.IdleWaitTime;
        if (blockedRetry < limit - blockedRetry)
        {
            blockedRetry += blockedRetry;
        }
        else
        {
            blockedRetry = heldBack ? limit : TimeSpan.Zero;
        }

        return wait;
    }

    /// <summary>
    /// Measures and logs an acquisition the store has answered.
    /// </summary>
    /// <param name="measured">Whether anything is collecting the acquisition instruments.</param>
    /// <param name="started">When the acquisition started, if it was measured.</param>
    /// <param name="acquired">How many triggers the store answered with, fired or pending.</param>
    private void RecordAcquisition(bool measured, long started, int acquired)
    {
        if (measured)
        {
            // Only a round that came back. A failed acquisition leaves through one of the catches around the
            // call, where it is a store failure rather than an acquisition latency, and folding the two
            // together would make an unreachable database look like a fast one.
            qsRsrcs.Meters.TriggersAcquired(
                qsRsrcs.Name,
                qsRsrcs.InstanceId,
                acquired,
                qsRsrcs.TimeProvider.GetElapsedTime(started));
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.TriggerBatchAcquired(acquired);
        }
    }

    /// <summary>
    /// Hands each fired trigger of a batch to the thread pool, and releases each that did not fire.
    /// </summary>
    /// <param name="triggers">The acquired triggers the store fired, in the order it answered for them.</param>
    /// <param name="results">What became of each, at the same index: <see cref="IJobStore.TriggersFired" />'s
    /// answer, or the fired part of <see cref="IJobStore.AcquireNextTriggersAndFireDue" />'s.</param>
    private async Task DispatchFiredTriggers(List<IOperableTrigger> triggers, List<TriggerFiredResult> results)
    {
        for (int i = 0; i < results.Count; i++)
        {
            TriggerFiredResult result = results[i];
            var bndle = result.TriggerFiredBundle;
            var exception = result.Exception;

            IOperableTrigger trigger = triggers[i];
            // TODO SQL exception?
            if (exception is not null && (exception is DbException || exception.InnerException is DbException))
            {
                logger.TriggerFireFailedWithDbException(trigger, exception);
                await SafeReleaseAcquiredTrigger(trigger, "after DbException").ConfigureAwait(false);
                continue;
            }

            // it's possible to get 'null' if the triggers was paused,
            // blocked, or other similar occurrences that prevent it being
            // fired at this time...  or if the scheduler was shutdown (halted)
            if (bndle is null)
            {
                // Held back by a running firing of its job, which may be on another node: nothing will
                // tell this loop when that firing ends, so it looks again soon, and then less and less
                // often (#3988). Every such trigger starts the count again, as it is fresh news.
                if (result.IsBlocked)
                {
                    blockedRetry = BlockedRetryStart;
                }

                // A firing its overlap policy declined is one the store has settled
                // already - skipped past, or held behind the running firing - and a
                // release would undo that.
                if (!result.IsDeclined)
                {
                    await SafeReleaseAcquiredTrigger(trigger, "for null fired bundle").ConfigureAwait(false);
                }

                continue;
            }

            // CancelPrevious: the store has recorded this fire, so the firings it
            // replaces are interrupted before it runs. Only ever this node's own.
            if (bndle.SupersededFireInstanceIds is { Count: > 0 } superseded)
            {
                await InterruptSuperseded(bndle.Trigger.Key, superseded).ConfigureAwait(false);
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
                    // use TriggeredJobComplete to properly unblock other triggers
                    // for DisallowConcurrentExecution jobs (TriggersFired already ran).
                    // The bundle's trigger rather than the acquired one, here and in the
                    // three completions below: it is the copy the firing advanced — which
                    // the run shell also completes with — and its fire time is how the
                    // store tells an abandoned firing of a spent trigger from one of a
                    // trigger that will fire again (#3507). The in-memory store advances
                    // the acquired instance itself, so only the ADO store can tell the
                    // two copies apart, and only it was left with the leftover.
                    await qsRsrcs.JobStore.TriggeredJobComplete(bndle.Trigger, bndle.JobDetail, SchedulerInstruction.NoInstruction, CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    // we consider this a serious error and expect that job instantiation will never succeed in the future either
                    await qsRsrcs.JobStore.TriggeredJobComplete(bndle.Trigger, bndle.JobDetail, SchedulerInstruction.SetAllJobTriggersError, CancellationToken.None).ConfigureAwait(false);
                }

                continue;
            }

            // Resolved the same way the stores resolve it when they filter, derivation
            // included: a ledger keyed differently from the filter would subtract from
            // one bucket what the filter had allowed out of another.
            string normalizedGroup = ExecutionLimits.ResolveGroupKey(
                trigger.ExecutionGroup,
                trigger.Key.Group,
                qs.GetExecutionLimits()?.UsesTriggerGroupWhenUnset == true);

            // Always track counts so that limits enabled at runtime
            // will see accurate in-flight counts immediately
            runningExecutionGroupCounts.AddOrUpdate(normalizedGroup, 1, (_, c) => c + 1);

            // Counted the same way, and for the shutdown's benefit: a firing is in flight
            // from here until the run shell's last act, the job store update that
            // completes it. A shutdown that is not waiting for its jobs still gives these
            // a moment to land, because the store refuses a completion once it has closed.
            qs.ExecutionDispatched();

            // The shell gives the two counts above back in its own finally, rather than
            // this loop wrapping the call to it in a lambda that does — which cost a
            // closure, a delegate and a state machine on every firing (#3802).
            shell.DispatchedBy(this, normalizedGroup);

            // Deliberately not this thread's token: TriggersFired has already committed
            // this firing to the job store and advanced the trigger, so refusing to dispatch
            // now loses the occurrence entirely. Only the pool's own shutdown may say no —
            // and a shutdown stops this loop before it closes the pool, so that a firing
            // this thread has already committed is never one nobody runs (#3746).
            var threadPoolRunResult = await qsRsrcs.ThreadPool
                .TryRunWithState(runJobRunShell, shell, CancellationToken.None).ConfigureAwait(false);
            if (!threadPoolRunResult)
            {
                // The shell never ran - decrement the counts we pre-incremented
                DecrementExecutionGroupCount(normalizedGroup);
                qs.ExecutionSettled();

                // Check if the scheduler is being shutdown
                if (halted || cancellationTokenSource.Token.IsCancellationRequested)
                {
                    // Scheduler is shutting down, complete the trigger gracefully
                    // Use TriggeredJobComplete to properly unblock other triggers
                    // for DisallowConcurrentExecution jobs (TriggersFired already ran)
                    logger.ThreadPoolRefusedWorkDuringShutdown();
                    await qsRsrcs.JobStore.TriggeredJobComplete(bndle.Trigger, bndle.JobDetail, SchedulerInstruction.NoInstruction, CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    // this case should never happen, as it is indicative of a bug in the thread pool or
                    // a thread pool being used concurrently - which the docs say not to do...
                    logger.ThreadPoolRefusedWork();
                    await qsRsrcs.JobStore.TriggeredJobComplete(bndle.Trigger, bndle.JobDetail, SchedulerInstruction.SetAllJobTriggersError, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
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

    /// <summary>
    /// What a dispatched firing calls when it ends: the execution-group count and the scheduler's
    /// in-flight tally this loop took before handing it over are both given back.
    /// </summary>
    internal void ExecutionFinished(string normalizedGroup)
    {
        DecrementExecutionGroupCount(normalizedGroup);
        qs.ExecutionSettled();
    }

    private void DecrementExecutionGroupCount(string normalizedGroup)
    {
        int newCount = runningExecutionGroupCounts.AddOrUpdate(normalizedGroup, 0, (_, c) => Math.Max(c - 1, 0));
        // Remove zero entries to prevent unbounded growth with high-cardinality group names. Not the
        // ungrouped bucket, which is resident: it is one key, it cannot grow, and most firings are
        // counted against it.
        if (newCount <= 0 && !string.Equals(normalizedGroup, ExecutionLimits.DefaultGroupKey, StringComparison.Ordinal))
        {
            runningExecutionGroupCounts.TryRemove(new KeyValuePair<string, int>(normalizedGroup, 0));
        }
    }

    /// <summary>
    /// Takes prescribed limits for execution groups (if any) and lowers the node-scoped ones
    /// according to jobs currently executing on this node.
    /// </summary>
    /// <remarks>
    /// A <see cref="ExecutionLimitScope.Cluster" /> limit is deliberately left alone here. This node's
    /// firings are already reservations in the job store — rows in <c>QRTZ_FIRED_TRIGGERS</c> for the
    /// ADO.NET store — and the store subtracts that count when it builds the acquisition ledger.
    /// Subtracting them a second time from this side would charge a busy node twice for its own work and
    /// halve the quota, which no single-node test would show.
    /// </remarks>
    private ExecutionLimits? ComputeAvailableExecutionGroupLimits()
    {
        ExecutionLimits? limits = qs.GetExecutionLimits();
        if (limits is null || limits.IsEmpty)
        {
            return null;
        }

        return limits.LowerByNodeInFlight(runningExecutionGroupCounts);
    }

    /// <summary>
    /// Interrupts the running firings a <see cref="OverlapPolicy.CancelPrevious" /> fire replaces.
    /// </summary>
    /// <remarks>
    /// A failure is logged and the new firing goes ahead: the store has already recorded it, and the
    /// old one running on beside it is what the policy degrades to when a job ignores its token anyway.
    /// </remarks>
    private async Task InterruptSuperseded(TriggerKey triggerKey, IReadOnlyList<string> fireInstanceIds)
    {
        foreach (string fireInstanceId in fireInstanceIds)
        {
            logger.PreviousFiringCancelled(triggerKey, fireInstanceId);

            try
            {
                await qs.InterruptFireInstance(fireInstanceId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.PreviousFiringCancelFailed(triggerKey, fireInstanceId, e);
            }
        }
    }

    private async Task SafeReleaseAcquiredTrigger(IOperableTrigger trigger, string context)
    {
        try
        {
            await qsRsrcs.JobStore.ReleaseAcquiredTrigger(trigger, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception releaseEx)
        {
            logger.AcquiredTriggerReleaseFailed(trigger.Key, context, releaseEx);
        }
    }

    private async ValueTask<bool> ReleaseIfScheduleChangedSignificantly(List<IOperableTrigger> triggers, DateTimeOffset triggerTime)
    {
        if (IsCandidateNewTimeEarlierWithinReason(triggerTime, true))
        {
            // above call does a clearSignaledSchedulingChange()
            foreach (IOperableTrigger trigger in triggers)
            {
                await SafeReleaseAcquiredTrigger(trigger, "after schedule change").ConfigureAwait(false);
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

    /// <summary>
    /// Starts the processing loop. Does nothing if it is already running, or if the loop has already been
    /// shut down.
    /// </summary>
    /// <remarks>
    /// Starting after <see cref="Shutdown"/> is a no-op rather than an error: a scheduler being started
    /// and stopped concurrently is reachable from the hosted services, whose graceful-shutdown deadline
    /// can elapse while a start is still in flight, and the caller of <c>IScheduler.Start()</c> should not
    /// see an exception about a disposed cancellation source because of it.
    /// </remarks>
    public void Start()
    {
        lock (stateLock)
        {
            if (shutDown || task is not null)
            {
                return;
            }

            task = Task.Factory.StartNew(
                static state => ((QuartzSchedulerThread) state!).Run(),
                this,
                cancellationTokenSource.Token,
                TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default
            ).Unwrap();
        }
    }

    /// <summary>
    /// Stops the processing loop and releases its resources. Terminal: the loop cannot be started again
    /// afterwards, which matches the scheduler not being restartable after shutdown.
    /// </summary>
    public async Task Shutdown()
    {
        Task? running;
        lock (stateLock)
        {
            if (shutDown)
            {
                return;
            }

            shutDown = true;
            running = task;
        }

        cancellationTokenSource.Cancel();

        // Nothing to wait for when the loop was never started.
        if (running is not null)
        {
            try
            {
                await running.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        // Disposed only here, once the loop has been awaited and can no longer read the token. Reaching
        // this point twice would dispose it twice, which is why Shutdown returns early above.
        cancellationTokenSource.Dispose();
    }
}