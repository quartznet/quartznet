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

using Quartz.Extensibility;
using Quartz.Impl.Triggers;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Impl.AdoJobStore;

public abstract partial class AdoJobStoreBase
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

    protected internal async ValueTask<RecoverMisfiredJobsResult> RecoverMisfiredJobs(
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

        List<MisfiredTriggerUpdate> updates = new(misfiredTriggers.Count);
        List<IOperableTrigger>? finalized = null;

        foreach (IOperableTrigger trigger in misfiredTriggers)
        {
            try
            {
                updates.Add(await PrepareMisfiredTriggerUpdate(conn, trigger, StoredTriggerState.Waiting, batchCalendarCache, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception e)
            {
                Logger.MisfireUpdatePreparationFailed(trigger.Key, e);
                continue;
            }

            DateTimeOffset? nextTime = trigger.NextFireTimeUtc;
            if (nextTime.HasValue)
            {
                if (nextTime.Value < earliestNewTime)
                {
                    earliestNewTime = nextTime.Value;
                }
            }
            else
            {
                (finalized ??= []).Add(trigger);
            }
        }

        try
        {
            await Delegate.UpdateMisfiredTriggers(conn, updates, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logger.MisfiredTriggerUpdateFailed(updates.Count, e);
            return new RecoverMisfiredJobsResult(hasMoreMisfiredTriggers, misfiredTriggers.Count, earliestNewTime);
        }

        if (finalized is not null)
        {
            foreach (IOperableTrigger trigger in finalized)
            {
                await signaler.NotifySchedulerListenersFinalized(trigger, cancellationToken).ConfigureAwait(false);
            }
        }

        return new RecoverMisfiredJobsResult(hasMoreMisfiredTriggers, misfiredTriggers.Count, earliestNewTime);
    }

    /// <summary>
    /// Runs the in-memory half of misfire handling for one trigger — notify the listeners, apply the
    /// trigger's misfire policy, and work out the state and misfire-original-fire-time to persist — and
    /// returns the resulting update for the caller to apply as part of a batch.
    /// </summary>
    /// <remarks>
    /// Shares its logic with <see cref="ApplyMisfiredTriggerUpdate" />, which is the same thing
    /// for a single trigger that is written immediately.
    /// </remarks>
    private async ValueTask<MisfiredTriggerUpdate> PrepareMisfiredTriggerUpdate(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        StoredTriggerState newStateIfNotComplete,
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

        await signaler.NotifyTriggerListenersMisfired(trigger, cancellationToken).ConfigureAwait(false);

        DateTimeOffset? originalFireTime = trigger.NextFireTimeUtc;
        DateTimeOffset now = timeProvider.GetUtcNow();

        trigger.UpdateAfterMisfire(calendar);

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

        return new MisfiredTriggerUpdate(trigger, newState, misfireOrigFireTime);
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
                    // Mirror ReleaseAcquiredTrigger: update from both ACQUIRED and BLOCKED,
                    // because TriggersFired may have moved the trigger to BLOCKED state (for
                    // DisallowConcurrentExecution jobs) while the fired record is still ACQUIRED.
                    await Delegate.UpdateTriggerStateFromOtherState(conn, firedTrigger.TriggerKey, StoredTriggerState.Waiting, StoredTriggerState.Acquired, cancellationToken).ConfigureAwait(false);
                    await Delegate.UpdateTriggerStateFromOtherState(conn, firedTrigger.TriggerKey, StoredTriggerState.Waiting, StoredTriggerState.Blocked, cancellationToken).ConfigureAwait(false);
                    await Delegate.DeleteFiredTrigger(conn, firedTrigger.FireInstanceId, cancellationToken).ConfigureAwait(false);
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

                DateTimeOffset misfireTime = timeProvider.GetUtcNow();
                if (MisfireThreshold > TimeSpan.Zero)
                {
                    misfireTime = misfireTime.AddMilliseconds(-1 * MisfireThreshold.TotalMilliseconds);
                }

                if (trigger.NextFireTimeUtc.GetValueOrDefault() > misfireTime)
                {
                    return false;
                }

                await ApplyMisfiredTriggerUpdate(conn, trigger, newStateIfNotComplete, cancellationToken).ConfigureAwait(false);

                return true;
            },
            $"update misfired trigger '{triggerKey}'");
    }

    /// <summary>
    /// Optimized misfire update path that bypasses the AddTrigger method's unnecessary
    /// queries (existence check, pause-group checks, job retrieval, blocked-state check,
    /// trigger-type lookup). Safe when the caller already holds <c>SchedulerLock.TriggerAccess</c>
    /// and has determined the trigger's persisted state and corresponding
    /// <paramref name="newStateIfNotComplete"/> to use across the misfire update.
    /// This covers triggers found in WAITING state during batch recovery as well as
    /// single-trigger misfire handling in the acquisition and resume paths.
    /// </summary>
    private async ValueTask ApplyMisfiredTriggerUpdate(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        StoredTriggerState newStateIfNotComplete,
        CancellationToken cancellationToken)
    {
        MisfiredTriggerUpdate update = await PrepareMisfiredTriggerUpdate(conn, trigger, newStateIfNotComplete, calendarCache: null, cancellationToken).ConfigureAwait(false);

        // Single targeted UPDATE (1-2 DB round-trips) instead of AddTrigger's 7-12.
        await Delegate.UpdateMisfiredTrigger(conn, trigger, update.NewState, update.MisfireOriginalFireTime, cancellationToken).ConfigureAwait(false);

        if (!trigger.NextFireTimeUtc.HasValue)
        {
            await signaler.NotifySchedulerListenersFinalized(trigger, cancellationToken).ConfigureAwait(false);
        }
    }

    protected internal async ValueTask<RecoverMisfiredJobsResult> RecoverMisfires(
        Guid requestorId,
        CancellationToken cancellationToken = default)
    {
        // Misfire recovery is the scheduler own work and commits on its own schedule, so it must not
        // run inside a transaction the application owns.
        using var suppression = AmbientConnection.Suppress();

        bool transOwner = false;
        ConnectionAndTransactionHolder? conn = null;
        try
        {
            RecoverMisfiredJobsResult result = RecoverMisfiredJobsResult.NoOp;
            int staleCount = 0;

            if (LockAllOperations)
            {
                // For SQLite: acquire lock before opening connection to avoid
                // "database is locked" errors from concurrent serializable transactions.
                // Skip the double-check optimization since in-memory lock is cheap.
                transOwner = await LockHandler.ObtainLock(requestorId, null, SchedulerLock.TriggerAccess, cancellationToken).ConfigureAwait(false);
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
                    transOwner = await LockHandler.ObtainLock(requestorId, conn, SchedulerLock.TriggerAccess, cancellationToken).ConfigureAwait(false);

                    result = await RecoverMisfiredJobs(conn, false, cancellationToken).ConfigureAwait(false);
                    staleCount = await RecoverStaleAcquiredTriggers(conn, cancellationToken).ConfigureAwait(false);
                }
                else if (await HasStaleAcquiredTriggers(conn, cancellationToken).ConfigureAwait(false))
                {
                    // Even when no misfired triggers exist, check for triggers stuck
                    // in ACQUIRED state (e.g., from a failed ReleaseAcquiredTrigger call)
                    transOwner = await LockHandler.ObtainLock(requestorId, conn, SchedulerLock.TriggerAccess, cancellationToken).ConfigureAwait(false);
                    staleCount = await RecoverStaleAcquiredTriggers(conn, cancellationToken).ConfigureAwait(false);
                }
            }

            // Include stale recovery count so the caller signals the scheduler thread
            if (staleCount > 0)
            {
                int totalCount = result.ProcessedMisfiredTriggerCount + staleCount;
                DateTimeOffset earliestNewTime = result.EarliestNewTimeUtc < timeProvider.GetUtcNow()
                    ? result.EarliestNewTimeUtc
                    : timeProvider.GetUtcNow();
                result = new RecoverMisfiredJobsResult(result.HasMoreMisfiredTriggers, totalCount, earliestNewTime);
            }

            await CommitConnection(conn, false, cancellationToken).ConfigureAwait(false);
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
        }
    }
}
