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

using System.Runtime.InteropServices;

using Quartz.Extensibility;
using Quartz.Impl.Triggers;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Impl.AdoJobStore;

internal abstract partial class AdoJobStoreBase
{
    protected DateTimeOffset MisfireTime
    {
        get
        {
            DateTimeOffset misfireTime = timeProvider.GetUtcNow();
            if (MisfireThreshold > TimeSpan.Zero)
            {
                misfireTime = misfireTime.AddMilliseconds(-1 * MisfireThreshold.TotalMilliseconds);
            }

            return misfireTime;
        }
    }

    /// <summary>
    /// Gets the threshold for considering a fired trigger record in ACQUIRED state as stale.
    /// Uses 2x <see cref="MisfireThreshold"/> with a floor of 2 minutes, which is
    /// generous enough to never interfere with normal acquisition (which takes at
    /// most idleWaitTime ~30s plus processing time).
    /// </summary>
    protected TimeSpan StaleAcquiredTriggerThreshold
    {
        get
        {
            TimeSpan threshold = MisfireThreshold + MisfireThreshold;
            return threshold < TimeSpan.FromMinutes(2) ? TimeSpan.FromMinutes(2) : threshold;
        }
    }

    //private int lastRecoverCount = 0;

    internal async ValueTask<RecoverMisfiredJobsResult> RecoverMisfiredJobs(
        ConnectionAndTransactionHolder conn,
        bool recovering,
        CancellationToken cancellationToken = default)
    {
        // If recovering, we want to handle all of the misfired
        // triggers right away.
        int maxMisfiresToHandleAtATime = recovering ? -1 : MaxMisfiresToHandleAtATime;

        DateTimeOffset earliestNewTime = DateTimeOffset.MaxValue;

        // Read the whole batch as fully populated triggers in one round-trip, rather than reading keys
        // and then reading each trigger back individually.
        MisfiredTriggerBatch batch =
            await Delegate.SelectMisfiredTriggersToRecover(conn, StoredTriggerState.Waiting, MisfireTime, maxMisfiresToHandleAtATime, cancellationToken).ConfigureAwait(false);

        List<IOperableTrigger> misfiredTriggers = batch.Triggers;
        bool hasMoreMisfiredTriggers = batch.HasMore;

        if (hasMoreMisfiredTriggers)
        {
            Logger.HandlingFirstMisfiredTriggers(misfiredTriggers.Count);
        }
        else if (misfiredTriggers.Count > 0)
        {
            Logger.HandlingMisfiredTriggers(misfiredTriggers.Count);
        }
        else
        {
            // A healthy scheduler takes this branch on every misfire scan, forever, so it is Debug -
            // "nothing happened" is not news. The branches above, where something did misfire, stay
            // at Information.
            Logger.NoMisfiredTriggers();
            return RecoverMisfiredJobsResult.NoOp;
        }

        // Cache calendars across the batch to avoid redundant DB round-trips
        // when multiple triggers reference the same calendar.
        Dictionary<string, ICalendar?> batchCalendarCache = new();

        List<PreparedMisfire> prepared = new(misfiredTriggers.Count);

        foreach (IOperableTrigger trigger in misfiredTriggers)
        {
            try
            {
                PreparedMisfire misfire = await PrepareMisfiredTriggerUpdate(conn, trigger, StoredTriggerState.Waiting, StoredTriggerState.Waiting, batchCalendarCache, cancellationToken).ConfigureAwait(false);

                // A policy that throws has been settled as that trigger's failure: it stays WAITING for the
                // next scan, or is stored ERROR at the limit, rather than lead every batch for good (#4006).
                if (misfire.Update is not null)
                {
                    prepared.Add(misfire);
                }
            }
            catch (Exception e)
            {
                Logger.MisfireUpdatePreparationFailed(trigger.Key, e);
            }
        }

        // A row a firing round that took no lock fired between this pass's read and its write is left as the
        // round left it (#4067). The pass still counts it as handled — it was looked at — and the scheduler
        // sees its new fire time on its next acquisition.
        List<MisfiredTriggerUpdate> landed;
        try
        {
            landed = await WriteMisfires(conn, prepared, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logger.MisfiredTriggerUpdateFailed(prepared.Count, e);
            return new RecoverMisfiredJobsResult(hasMoreMisfiredTriggers, misfiredTriggers.Count, earliestNewTime);
        }

        foreach (MisfiredTriggerUpdate update in landed)
        {
            if (update.Trigger.NextFireTimeUtc is { } nextTime && nextTime < earliestNewTime)
            {
                earliestNewTime = nextTime;
            }
        }

        return new RecoverMisfiredJobsResult(hasMoreMisfiredTriggers, misfiredTriggers.Count, earliestNewTime);
    }

    /// <summary>
    /// Writes prepared misfires, each held to the fire time it was read with, and raises what each one
    /// that took owes the listeners once the write commits.
    /// </summary>
    /// <remarks>
    /// A row a firing round that took no lock fired between the read and the write is not re-armed, not
    /// reported misfired and not finalized (#4067): it was fired, not missed. It is logged at Debug.
    /// </remarks>
    /// <param name="conn">The unit of work.</param>
    /// <param name="prepared">Misfires that each carry an update.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The updates that took, in order.</returns>
    private async ValueTask<List<MisfiredTriggerUpdate>> WriteMisfires(
        ConnectionAndTransactionHolder conn,
        List<PreparedMisfire> prepared,
        CancellationToken cancellationToken)
    {
        List<MisfiredTriggerUpdate> updates = new(prepared.Count);
        foreach (PreparedMisfire misfire in prepared)
        {
            updates.Add(misfire.Update!.Value);
        }

        HashSet<TriggerKey> took = [.. await Delegate.UpdateMisfiredTriggersIfUnchanged(conn, updates, cancellationToken).ConfigureAwait(false)];

        List<MisfiredTriggerUpdate> landed = new(took.Count);
        for (int i = 0; i < updates.Count; i++)
        {
            MisfiredTriggerUpdate update = updates[i];
            if (!took.Contains(update.Trigger.Key))
            {
                Logger.MisfireWriteSkipped(update.Trigger.Key, update.ExpectedNextFireTimeUtc);
                continue;
            }

            NotifyMisfireAfterCommit(conn, prepared[i]);
            landed.Add(update);
        }

        return landed;
    }

    /// <summary>
    /// Runs the in-memory half of misfire handling for one trigger — notify the listeners, apply the
    /// trigger's misfire policy, and work out the state and misfire-original-fire-time to persist — and
    /// returns the resulting update for the caller to apply as part of a batch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shares its logic with <see cref="ApplyMisfiredTriggerUpdate" />, which is the same thing
    /// for a single trigger that is written immediately.
    /// </para>
    /// <para>
    /// A policy that throws — the trigger's calendar, or a trigger type Quartz did not write — fails this
    /// trigger alone, and <see cref="SettleFailedMisfire" /> settles it in the caller's transaction (#4006).
    /// Only that step is guarded: a database failure, reading the calendar included, propagates, so the
    /// caller's transaction rolls back and is retried as before.
    /// </para>
    /// </remarks>
    /// <param name="conn">The unit of work.</param>
    /// <param name="trigger">The misfired trigger, as read; the policy moves it on.</param>
    /// <param name="newStateIfNotComplete">The state to store it in when it has a fire time left.</param>
    /// <param name="readFromState">
    /// The state the caller read the row in, when a firing round that takes no lock could have moved the
    /// row since (<c>WAITING</c>); <see langword="null" /> for a row no round can claim, such as a paused
    /// one being resumed. Decides how a failed policy's <c>ERROR</c> is written.
    /// </param>
    /// <param name="calendarCache">The calendars read so far in this batch, or <see langword="null" />.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// The update to write, with the trigger as it was before the policy for the listeners, or no update
    /// when the policy threw, with whether that failure stored the trigger <c>ERROR</c>.
    /// </returns>
    private async ValueTask<PreparedMisfire> PrepareMisfiredTriggerUpdate(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        StoredTriggerState newStateIfNotComplete,
        StoredTriggerState? readFromState,
        Dictionary<string, ICalendar?>? calendarCache,
        CancellationToken cancellationToken)
    {
        // Calendar lookup with batch-local cache (when available).
        ICalendar? calendar = null;
        if (trigger.CalendarName is not null)
        {
            if (calendarCache is null || !calendarCache.TryGetValue(trigger.CalendarName, out calendar))
            {
                calendar = await GetCalendar(conn, trigger.CalendarName, cancellationToken).ConfigureAwait(false);
                if (calendarCache is not null)
                {
                    calendarCache[trigger.CalendarName] = calendar;
                }
            }
        }

        // What the listeners are told misfired: the trigger as it was before the policy moved it on, as the
        // in-memory store tells them. Told by the caller once the write has committed, and only if the
        // write took — a row a firing round moved meanwhile was fired, not missed (#4067) — where this
        // used to tell them here, inside the transaction and before anything was written.
        ITrigger misfired = (ITrigger) trigger.Clone();

        DateTimeOffset? originalFireTime = trigger.NextFireTimeUtc;
        DateTimeOffset? previousFireTime = trigger.PreviousFireTimeUtc;
        DateTimeOffset now = timeProvider.GetUtcNow();

        try
        {
            trigger.UpdateAfterMisfire(calendar);
        }
        catch (Exception e)
        {
            bool storedError = await SettleFailedMisfire(conn, trigger.Key, previousFireTime, readFromState, originalFireTime, e, cancellationToken).ConfigureAwait(false);
            return new PreparedMisfire(Update: null, storedError, misfired);
        }

        // A misfire handled ends the trigger's run of failures, once it has committed. One volatile read
        // while nothing has failed.
        if (!fireFailures.IsEmpty && DecidesOwnOutcome(conn))
        {
            TriggerKey handled = trigger.Key;
            conn.NotifyAfterCommit((_, _) =>
            {
                fireFailures.Clear(handled);
                return default;
            });
        }

        // The occurrence that was waiting to be retried has been missed, and misfire handling has just
        // recomputed the trigger from its schedule. Whatever it fires next is a fresh occurrence, so it
        // starts with no retries behind it; the trigger's own misfire instruction decides what that
        // fire is, and there is deliberately no retry-specific misfire policy.
        trigger.RetryAttempt = 0;

        // Determine new state.
        DateTimeOffset? newFireTime = trigger.NextFireTimeUtc;
        StoredTriggerState newState = newFireTime.HasValue ? newStateIfNotComplete : StoredTriggerState.Complete;

        // Compute misfire-original-fire-time for "fire now" policies (folded into the single UPDATE).
        DateTimeOffset? misfireOrigFireTime = null;
        if (originalFireTime.HasValue && newFireTime.HasValue
            && originalFireTime.Value != newFireTime.Value
            && Math.Abs((newFireTime.Value - now).TotalMilliseconds) < TriggerBase.FireNowMisfireDetectionThresholdMs)
        {
            misfireOrigFireTime = originalFireTime;
        }

        // Held to the fire time the misfire was decided from: the write lands only on the row as it was
        // read, so a firing round that took no lock and fired the row in between is left as it left it.
        MisfiredTriggerUpdate update = new(trigger, newState, misfireOrigFireTime)
        {
            ExpectedNextFireTimeUtc = originalFireTime,
        };

        return new PreparedMisfire(update, StoredError: false, misfired);
    }

    /// <summary>
    /// Settles a misfire whose policy threw as a failure of that trigger alone (#4006). Nothing of the
    /// misfire is written: the row keeps its state and fire time, for the misfire handler to try again. The
    /// failure counts toward <see cref="MaxConsecutiveFireFailures" /> in the ledger a failed fire counts in,
    /// at most once per <see cref="MisfireThreshold" />, and the one that reaches it stores the trigger
    /// <c>ERROR</c> in the caller's transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ledger moves only once that transaction has committed: one that rolls back for a database
    /// failure is retried, and the retry runs the policy again, which would count one failure twice. The
    /// caller holds <see cref="SchedulerLock.TriggerAccess" />, but a firing round of work that allows
    /// concurrent execution does not (#4067), so a row the caller read <c>WAITING</c> is parked only while
    /// it is still that row: <c>FROM 'WAITING'</c> at the fire time read. A row no round can claim — a
    /// paused one being resumed — is parked by key, as before.
    /// </para>
    /// <para>
    /// At most once per misfire threshold, because a backlog of misfires has the misfire handler pass every
    /// few milliseconds, and the trigger that failed is first in every pass: counted each time, a calendar
    /// that is briefly unreachable would park its trigger in a fraction of a second.
    /// </para>
    /// <para>
    /// Not counted at all when the transaction is the application's, enlisted or ambient: the callbacks
    /// run when the store's part is done, before the application commits or rolls back, so a count there
    /// could record a failure that never happened. The failure is logged all the same.
    /// </para>
    /// </remarks>
    /// <param name="conn">The unit of work.</param>
    /// <param name="triggerKey">The trigger whose policy threw.</param>
    /// <param name="previousFireTimeUtc">Its previous fire time, which the ledger counts failures against.</param>
    /// <param name="readFromState">
    /// The state the row was read in when a lock-free round could have moved it since, or <see langword="null" />.
    /// </param>
    /// <param name="readFireTimeUtc">The fire time the row was read with.</param>
    /// <param name="failure">What the policy threw.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>Whether the failure stored the trigger <c>ERROR</c>.</returns>
    private async ValueTask<bool> SettleFailedMisfire(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        DateTimeOffset? previousFireTimeUtc,
        StoredTriggerState? readFromState,
        DateTimeOffset? readFireTimeUtc,
        Exception failure,
        CancellationToken cancellationToken)
    {
        Logger.MisfireUpdatePreparationFailed(triggerKey, failure);

        if (MaxConsecutiveFireFailures <= 0 || !DecidesOwnOutcome(conn))
        {
            return false;
        }

        DateTimeOffset nowUtc = timeProvider.GetUtcNow();
        int? failures = fireFailures.MisfireFailuresWithOneMore(triggerKey, previousFireTimeUtc, nowUtc, MisfireThreshold);
        if (failures is null)
        {
            // Counted less than a misfire threshold ago.
            return false;
        }

        if (failures < MaxConsecutiveFireFailures)
        {
            // Not a listener notification, but the one hook that runs only once the work has committed.
            conn.NotifyAfterCommit((_, _) =>
            {
                fireFailures.RecordMisfireFailure(triggerKey, previousFireTimeUtc, nowUtc);
                return default;
            });

            return false;
        }

        int parkedAfter = failures.Value;
        int parked = readFromState is { } fromState && readFireTimeUtc is { } readFireTime
            ? await Delegate.UpdateTriggerStateFromOtherStateWithNextFireTime(conn, triggerKey, StoredTriggerState.Error, fromState, readFireTime, cancellationToken).ConfigureAwait(false)
            : await Delegate.UpdateTriggerState(conn, triggerKey, StoredTriggerState.Error, cancellationToken).ConfigureAwait(false);

        if (parked == 0)
        {
            // The row is not the one this pass read: a firing round that took no lock fired it in between
            // (#4067), so there is nothing to park, and the count stands for the policy's next failure.
            Logger.MisfireWriteSkipped(triggerKey, readFireTimeUtc);
            return false;
        }

        // Three callbacks rather than one, so that a logger that throws costs neither the count nor the
        // listeners their notification.
        conn.NotifyAfterCommit((_, _) =>
        {
            fireFailures.Clear(triggerKey);
            return default;
        });
        conn.NotifyAfterCommit((notifier, token) => notifier.NotifySchedulerListenersTriggerInError(triggerKey, token));
        conn.NotifyAfterCommit((_, _) =>
        {
            Logger.FailingTriggerParkedInError(triggerKey, parkedAfter);
            return default;
        });

        return true;
    }

    /// <summary>
    /// Whether this unit of work's outcome is the store's to decide: it began the transaction itself.
    /// </summary>
    /// <remarks>
    /// Not on a connection the application enlisted, nor inside an ambient transaction, where what the
    /// store records after "commit" runs before the application has committed or rolled back. A connection
    /// with no transaction and no ambient one runs each statement on its own, so its work is done when the
    /// store's part is.
    /// </remarks>
    private static bool DecidesOwnOutcome(ConnectionAndTransactionHolder conn)
    {
        return conn.OwnsResources && (conn.Transaction is not null || System.Transactions.Transaction.Current is null);
    }

    /// <summary>
    /// What handling one trigger's misfire came to: the update to write, or none when the trigger's
    /// misfire policy threw, with whether that failure stored the trigger <c>ERROR</c> (#4006); and the
    /// trigger as it was before the policy, which is what the listeners are told misfired.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct PreparedMisfire(MisfiredTriggerUpdate? Update, bool StoredError, ITrigger Misfired);

    /// <summary>
    /// Raises what a misfire owes the listeners, once its write has committed: that the trigger misfired,
    /// and that it is finalized when the policy left it nothing to fire.
    /// </summary>
    /// <param name="conn">The unit of work the write is in.</param>
    /// <param name="prepared">The misfire, whose update was written.</param>
    private static void NotifyMisfireAfterCommit(ConnectionAndTransactionHolder conn, in PreparedMisfire prepared)
    {
        ITrigger misfired = prepared.Misfired;
        conn.NotifyAfterCommit((notifier, token) => notifier.NotifyTriggerListenersMisfired(misfired, token));

        if (prepared.Update is { } update && !update.Trigger.NextFireTimeUtc.HasValue)
        {
            IOperableTrigger finalized = update.Trigger;
            conn.NotifyAfterCommit((notifier, token) => notifier.NotifySchedulerListenersFinalized(finalized, token));
        }
    }

    /// <summary>
    /// Recover triggers that have been stuck in the ACQUIRED state for longer than
    /// expected. This can happen when <see cref="Extensibility.IJobStore.ReleaseAcquiredTrigger"/>
    /// fails after <see cref="Extensibility.IJobStore.TriggersFired"/> fails, leaving the trigger
    /// in ACQUIRED state with no one to fire or release it.
    /// </summary>
    protected async ValueTask<int> RecoverStaleAcquiredTriggers(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        TimeSpan staleThreshold = StaleAcquiredTriggerThreshold;
        DateTimeOffset staleCutoff = timeProvider.GetUtcNow() - staleThreshold;

        IReadOnlyCollection<FiredTriggerRecord> firedTriggers = await Delegate.SelectFiredTriggerRecords(conn, new FiredTriggerQuery { InstanceId = InstanceId }, cancellationToken).ConfigureAwait(false);

        int recoveredCount = 0;
        foreach (FiredTriggerRecord firedTrigger in firedTriggers)
        {
            // Use the later of scheduled fire time and acquisition time to avoid
            // premature recovery when IdleWaitTime is large (triggers are legitimately
            // ACQUIRED until their scheduled fire time arrives).
            DateTimeOffset effectiveTimestamp = firedTrigger.ScheduleTimestamp > firedTrigger.FireTimestamp
                ? firedTrigger.ScheduleTimestamp
                : firedTrigger.FireTimestamp;

            if (firedTrigger.FireInstanceState == StoredTriggerState.Acquired && effectiveTimestamp < staleCutoff)
            {
                try
                {
                    // The same release the scheduler thread would have made, had it not lost track of the
                    // reservation - including where the trigger goes back to. A reservation's fired row
                    // names no job, so the trigger's own row is asked for the key; a row that is gone is a
                    // trigger that was deleted, and the reservation is all that is left to clean up.
                    StoredTriggerHeader? header = await Delegate.SelectTriggerHeader(conn, firedTrigger.TriggerKey, cancellationToken).ConfigureAwait(false);
                    if (header is null)
                    {
                        await Delegate.DeleteFiredTrigger(conn, firedTrigger.FireInstanceId, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await ReleaseAcquiredTrigger(conn, firedTrigger.TriggerKey, header.JobKey, firedTrigger.FireInstanceId, cancellationToken).ConfigureAwait(false);
                    }

                    recoveredCount++;
                }
                catch (Exception e)
                {
                    Logger.StaleAcquiredTriggerRecoveryFailed(firedTrigger.TriggerKey, e);
                }
            }
        }

        if (recoveredCount > 0)
        {
            Logger.StaleAcquiredTriggersRecovered(recoveredCount, staleThreshold);
        }

        return recoveredCount;
    }

    /// <summary>
    /// Pre-check (no lock required) to see if there are any fired trigger records in
    /// ACQUIRED state that have exceeded the stale threshold. Queries the same data as
    /// <see cref="RecoverStaleAcquiredTriggers"/> but only to decide whether to acquire
    /// the lock; the actual recovery re-queries under lock for correctness.
    /// </summary>
    private async Task<bool> HasStaleAcquiredTriggers(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset staleCutoff = timeProvider.GetUtcNow() - StaleAcquiredTriggerThreshold;

        IReadOnlyCollection<FiredTriggerRecord> firedTriggers = await Delegate.SelectFiredTriggerRecords(conn, new FiredTriggerQuery { InstanceId = InstanceId }, cancellationToken).ConfigureAwait(false);

        foreach (FiredTriggerRecord firedTrigger in firedTriggers)
        {
            DateTimeOffset effectiveTimestamp = firedTrigger.ScheduleTimestamp > firedTrigger.FireTimestamp
                ? firedTrigger.ScheduleTimestamp
                : firedTrigger.FireTimestamp;

            if (firedTrigger.FireInstanceState == StoredTriggerState.Acquired && effectiveTimestamp < staleCutoff)
            {
                return true;
            }
        }

        return false;
    }

    protected ValueTask<bool> UpdateMisfiredTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        StoredTriggerState newStateIfNotComplete,
        bool forceState,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            async () =>
            {
                var trigger = (await GetTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false))!;

                // The trigger's own threshold where it has one (#4070).
                DateTimeOffset misfireTime = MisfireThresholds.MisfireTime(timeProvider.GetUtcNow(), trigger, MisfireThreshold);

                if (trigger.NextFireTimeUtc.GetValueOrDefault() > misfireTime)
                {
                    return false;
                }

                return await ApplyMisfiredTriggerUpdate(conn, trigger, newStateIfNotComplete, cancellationToken).ConfigureAwait(false);
            },
            $"update misfired trigger '{triggerKey}'");
    }

    /// <summary>
    /// Optimized misfire update path that bypasses the AddTrigger method's unnecessary
    /// queries (existence check, pause-group checks, job retrieval, blocked-state check,
    /// trigger-type lookup). Safe when the caller already holds <c>SchedulerLock.TriggerAccess</c>
    /// and has determined the trigger's persisted state and corresponding
    /// <paramref name="newStateIfNotComplete"/> to use across the misfire update.
    /// This covers single-trigger misfire handling in the resume paths, whose row is paused and so
    /// not one a firing round could have claimed; batch recovery has its own loop.
    /// </summary>
    /// <returns>
    /// Whether the row was written: the misfire applied, or the trigger stored <c>ERROR</c> because its
    /// policy threw for the last time allowed. A policy that threw short of that writes nothing, and the
    /// caller goes on as if nothing had misfired (#4006).
    /// </returns>
    private async ValueTask<bool> ApplyMisfiredTriggerUpdate(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        StoredTriggerState newStateIfNotComplete,
        CancellationToken cancellationToken)
    {
        PreparedMisfire prepared = await PrepareMisfiredTriggerUpdate(conn, trigger, newStateIfNotComplete, readFromState: null, calendarCache: null, cancellationToken).ConfigureAwait(false);
        if (prepared.Update is null)
        {
            return prepared.StoredError;
        }

        // Single targeted UPDATE (1-2 DB round-trips) instead of AddTrigger's 7-12, held to the fire time
        // read as every misfire write is (#4067).
        return (await WriteMisfires(conn, [prepared], cancellationToken).ConfigureAwait(false)).Count > 0;
    }

    /// <summary>
    /// How often 3609 repeats while the unserved misfires stay: on the transition, then at most hourly.
    /// </summary>
    private static readonly TimeSpan UnservedMisfireRepeatInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// Whether the last sweep found misfires outside what this node serves, and when that was last
    /// logged. Only the misfire handler's thread writes these.
    /// </summary>
    private bool unservedMisfiresReported;

    private DateTimeOffset unservedMisfiresLastLoggedUtc;

    /// <summary>
    /// Counts the misfired triggers in the groups this node does not serve, and logs the transitions:
    /// 3609 when they appear and again at most once an hour while they stay, 3610 when they are gone.
    /// </summary>
    /// <remarks>
    /// Asked only through a delegate Quartz ships, whose statement carries the complementary clause; a
    /// delegate of somebody's own costs the node nothing but these two lines. One index seek per pass,
    /// and only on a node that routes.
    /// </remarks>
    private async ValueTask ReportUnservedMisfires(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken)
    {
        if (Delegate is not StdAdoDelegate shipped)
        {
            return;
        }

        UnservedMisfires unserved = await shipped.CountUnservedMisfiredTriggers(conn, MisfireTime, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = timeProvider.GetUtcNow();

        if (unserved.Count > 0)
        {
            if (!unservedMisfiresReported || now - unservedMisfiresLastLoggedUtc >= UnservedMisfireRepeatInterval)
            {
                Logger.UnservedTriggersMisfired(unserved.Count, unserved.OldestDueUtc);
                unservedMisfiresReported = true;
                unservedMisfiresLastLoggedUtc = now;
            }
        }
        else if (unservedMisfiresReported)
        {
            Logger.UnservedMisfiresHandled();
            unservedMisfiresReported = false;
        }
    }

    internal async ValueTask<RecoverMisfiredJobsResult> RecoverMisfires(
        Guid requestorId,
        CancellationToken cancellationToken = default)
    {
        // Misfire recovery is the scheduler own work and commits on its own schedule, so it must not
        // run inside a transaction the application owns.
        using var suppression = AmbientConnection.Suppress();

        bool transOwner = false;
        ConnectionAndTransactionHolder? conn = null;

        // What the pass owes the listeners, taken once it has committed and raised once the lock is
        // released, as ExecuteInLocalTransactionLock does: a continuation the sweep below parks is
        // announced after the fact, never from inside the transaction.
        List<Func<ISchedulerSignaler, CancellationToken, ValueTask>>? committedNotifications = null;
        try
        {
            RecoverMisfiredJobsResult result = RecoverMisfiredJobsResult.NoOp;
            int staleCount = 0;
            bool locked = false;

            if (LockAllOperations)
            {
                // For SQLite: acquire lock before opening connection to avoid
                // "database is locked" errors from concurrent serializable transactions.
                // Skip the double-check optimization since in-memory lock is cheap.
                transOwner = await AcquireLock(requestorId, null, SchedulerLock.TriggerAccess, cancellationToken).ConfigureAwait(false);
                locked = true;
                conn = await GetLocalTransactionConnection(cancellationToken).ConfigureAwait(false);
                result = await RecoverMisfiredJobs(conn, false, cancellationToken).ConfigureAwait(false);
                staleCount = await RecoverStaleAcquiredTriggers(conn, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                conn = await GetLocalTransactionConnection(cancellationToken).ConfigureAwait(false);

                // Before we make the potentially expensive call to acquire the
                // trigger lock, peek ahead to see if it is likely we would find
                // misfired triggers requiring recovery.
                int misfireCount = DoubleCheckLockMisfireHandler
                    ? await Delegate.CountMisfiredTriggersInState(conn, StoredTriggerState.Waiting, MisfireTime, cancellationToken).ConfigureAwait(false)
                    : int.MaxValue;

                if (Logger.IsEnabled(LogLevel.Debug))
                {
                    Logger.MisfiredTriggersCounted(misfireCount);
                }

                if (misfireCount > 0)
                {
                    transOwner = await AcquireLock(requestorId, conn, SchedulerLock.TriggerAccess, cancellationToken).ConfigureAwait(false);
                    locked = true;

                    result = await RecoverMisfiredJobs(conn, false, cancellationToken).ConfigureAwait(false);
                    staleCount = await RecoverStaleAcquiredTriggers(conn, cancellationToken).ConfigureAwait(false);
                }
                else if (await HasStaleAcquiredTriggers(conn, cancellationToken).ConfigureAwait(false))
                {
                    // Even when no misfired triggers exist, check for triggers stuck
                    // in ACQUIRED state (e.g., from a failed ReleaseAcquiredTrigger call)
                    transOwner = await AcquireLock(requestorId, conn, SchedulerLock.TriggerAccess, cancellationToken).ConfigureAwait(false);
                    locked = true;
                    staleCount = await RecoverStaleAcquiredTriggers(conn, cancellationToken).ConfigureAwait(false);
                }
            }

            // The continuations whose parent is gone, which a lock-free completion may have missed
            // (#3863). One statement without the lock when the pass has not taken it, and the lock
            // only when that statement found something — the double-check the misfire count makes.
            int strandedCount = 0;
            if (locked || await HasStrandedContinuations(conn, cancellationToken).ConfigureAwait(false))
            {
                if (!locked)
                {
                    transOwner = await AcquireLock(requestorId, conn, SchedulerLock.TriggerAccess, cancellationToken).ConfigureAwait(false);
                }

                strandedCount = await SweepStrandedContinuations(conn, cancellationToken).ConfigureAwait(false);
            }

            // A node that routes also looks at what its sweep leaves alone: the misfires in the groups it
            // does not serve. Under routing no node re-arms those, so ones that stay are ones no live node
            // is serving, and the handler says so once rather than every pass.
            if (routing is not null)
            {
                await ReportUnservedMisfires(conn, cancellationToken).ConfigureAwait(false);
            }

            // Include what the stale recovery and the sweep released, so the caller signals the
            // scheduler thread.
            if (staleCount + strandedCount > 0)
            {
                int totalCount = result.ProcessedMisfiredTriggerCount + staleCount + strandedCount;
                DateTimeOffset earliestNewTime = result.EarliestNewTimeUtc < timeProvider.GetUtcNow()
                    ? result.EarliestNewTimeUtc
                    : timeProvider.GetUtcNow();
                result = new RecoverMisfiredJobsResult(result.HasMoreMisfiredTriggers, totalCount, earliestNewTime);
            }

            await CommitConnection(conn, false, cancellationToken).ConfigureAwait(false);
            committedNotifications = conn.TakeNotificationsAfterCommit();
            return result;
        }
        catch (JobPersistenceException jpe)
        {
            await RollbackConnection(conn, jpe, cancellationToken).ConfigureAwait(false);
            throw;
        }
        catch (Exception e)
        {
            await RollbackConnection(conn, e, cancellationToken).ConfigureAwait(false);
            Throw.JobPersistenceException("Database error recovering from misfires.", e);
            return default;
        }
        finally
        {
            try
            {
                await ReleaseLock(requestorId, SchedulerLock.TriggerAccess, transOwner, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await CleanupConnection(conn, cancellationToken).ConfigureAwait(false);
            }

            await RaiseNotificationsAfterCommit(committedNotifications, cancellationToken).ConfigureAwait(false);
        }
    }
}
