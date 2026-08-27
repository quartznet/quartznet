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

using System.Globalization;
using Quartz.Extensibility;
using Quartz.Impl.Triggers;

namespace Quartz.Impl.AdoJobStore;

public abstract partial class AdoJobStoreBase
{
    /// <summary>
    /// Will recover any failed or misfired jobs and clean up the data store as
    /// appropriate.
    /// </summary>
    protected ValueTask RecoverJobs(CancellationToken cancellationToken = default)
    {
        return ExecuteInLocalTransactionLock(
            SchedulerLock.TriggerAccess,
            conn => RecoverJobs(conn, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Will recover any failed or misfired jobs and clean up the data store as
    /// appropriate.
    /// </summary>
    protected ValueTask RecoverJobs(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            async () =>
            {
                // update inconsistent job states
                int rows = await Delegate.UpdateTriggerStatesFromOtherStates(conn, StoredTriggerState.Waiting, [StoredTriggerState.Acquired, StoredTriggerState.Blocked], cancellationToken).ConfigureAwait(false);

                rows += await Delegate.UpdateTriggerStatesFromOtherStates(conn, StoredTriggerState.Paused, [StoredTriggerState.PausedBlocked], cancellationToken).ConfigureAwait(false);

                Logger.TriggersFreedFromAcquiredOrBlocked(rows);

                // clean up misfired jobs
                await RecoverMisfiredJobs(conn, true, cancellationToken).ConfigureAwait(false);

                // recover jobs marked for recovery that were not fully executed
                var recoveringJobTriggers = await Delegate.SelectTriggersForRecoveringJobs(conn, cancellationToken).ConfigureAwait(false);
                Logger.RecoveringInProgressJobs(recoveringJobTriggers.Count);

                foreach (IOperableTrigger trigger in recoveringJobTriggers)
                {
                    if (await JobExists(conn, trigger.JobKey, cancellationToken).ConfigureAwait(false))
                    {
                        trigger.ComputeFirstFireTimeUtc(null);
                        await AddTrigger(conn, trigger, null, false, StoredTriggerState.Waiting, false, true, cancellationToken).ConfigureAwait(false);
                    }
                }
                Logger.RecoveryComplete();

                // remove lingering 'complete' triggers...
                var triggersInState = await Delegate.SelectTriggersInState(conn, StoredTriggerState.Complete, cancellationToken).ConfigureAwait(false);
                foreach (var trigger in triggersInState)
                {
                    await DeleteTrigger(conn, trigger, cancellationToken).ConfigureAwait(false);
                }
                Logger.CompleteTriggersRemoved(triggersInState.Count);

                // clean up any fired trigger entries
                int deleted = await Delegate.DeleteFiredTriggers(conn, new FiredTriggerQuery(), cancellationToken).ConfigureAwait(false);
                Logger.StaleFiredJobEntriesRemoved(deleted);
            },
            "recover jobs");
    }

    private bool firstCheckIn = true;

    /// <summary>
    /// When this node last recorded that it is alive. Internal: it is bookkeeping the check-in loop
    /// owns, and a subclass writing it would move the moment every other node decides this one died.
    /// </summary>
    internal DateTimeOffset LastCheckin { get; set; }

    protected internal async ValueTask<bool> CheckIn(
        Guid requestorId,
        CancellationToken cancellationToken = default)
    {
        // Cluster check-in has to run in a transaction of its own to avoid deadlocking under recovery,
        // so it must never borrow a connection the application enlisted.
        using var suppression = AmbientConnection.Suppress();

        int maxRetries = MaxTransientRetries;
        int totalAttempts = maxRetries + 1;
        for (int attempt = 1; attempt <= totalAttempts; attempt++)
        {
            bool transOwner = false;
            bool transStateOwner = false;
            bool recovered = false;

            // Per attempt, not per call: an attempt is one round trip, and a failed one is worth seeing
            // as a failed check-in of its own rather than folded into the retry that succeeded after it.
            bool measureCheckin = Meters.ClusterCheckinEnabled;
            long checkinStarted = measureCheckin ? timeProvider.GetTimestamp() : 0;
            Exception? checkinFailure = null;

            ConnectionAndTransactionHolder conn = await GetLocalTransactionConnection(cancellationToken).ConfigureAwait(false);
            try
            {
                // Other than the first time, always checkin first to make sure there is
                // work to be done before we acquire the lock (since that is expensive,
                // and is almost never necessary).  This must be done in a separate
                // transaction to prevent a deadlock under recovery conditions.
                List<SchedulerStateRecord>? failedRecords = null;
                if (!firstCheckIn)
                {
                    failedRecords = await ClusterCheckIn(conn, cancellationToken).ConfigureAwait(false);
                    await CommitConnection(conn, true, cancellationToken).ConfigureAwait(false);
                }

                if (firstCheckIn || failedRecords is not null && failedRecords.Count > 0)
                {
                    transStateOwner = await LockHandler.ObtainLock(requestorId, conn, SchedulerLock.StateAccess, cancellationToken).ConfigureAwait(false);

                    // Now that we own the lock, make sure we still have work to do.
                    // The first time through, we also need to make sure we update/create our state record
                    if (firstCheckIn)
                    {
                        failedRecords = await ClusterCheckIn(conn, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        failedRecords = await FindFailedInstances(conn, cancellationToken).ConfigureAwait(false);
                    }

                    if (failedRecords.Count > 0)
                    {
                        transOwner = await LockHandler.ObtainLock(requestorId, conn, SchedulerLock.TriggerAccess, cancellationToken).ConfigureAwait(false);
                        //getLockHandler().obtainLock(conn, LockJobAccess);

                        await ClusterRecover(conn, failedRecords, cancellationToken).ConfigureAwait(false);
                        recovered = true;
                    }
                }

                await CommitConnection(conn, false, cancellationToken).ConfigureAwait(false);

                firstCheckIn = false;
                return recovered;
            }
            catch (JobPersistenceException jpe)
            {
                checkinFailure = jpe;
                await RollbackConnection(conn, jpe, cancellationToken).ConfigureAwait(false);
                if (attempt < totalAttempts && IsTransient(jpe))
                {
                    Logger.TransientFailureInCheckIn(attempt, totalAttempts, TransientRetryInterval, jpe);
                }
                else
                {
                    throw;
                }
            }
            finally
            {
                try
                {
                    try
                    {
                        await ReleaseLock(requestorId, SchedulerLock.TriggerAccess, transOwner, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        try
                        {
                            await ReleaseLock(requestorId, SchedulerLock.StateAccess, transStateOwner, cancellationToken).ConfigureAwait(false);
                        }
                        finally
                        {
                            await CleanupConnection(conn, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    // Outermost, so the measurement covers the locks and the connection as well as the
                    // work between them: a check-in that is slow because it waited on a lock is exactly
                    // the check-in an operator is looking for.
                    if (measureCheckin)
                    {
                        Meters.ClusterCheckinCompleted(
                            InstanceName,
                            InstanceId,
                            timeProvider.GetElapsedTime(checkinStarted),
                            checkinFailure);
                    }
                }
            }

            // Delay before the next attempt
            await Task.Delay(TransientRetryInterval, timeProvider, cancellationToken).ConfigureAwait(false);
        }

        Throw.InvalidOperationException("CheckIn retry loop exited unexpectedly");
        return default;
    }

    /// <summary>
    /// Get a list of all scheduler instances in the cluster that may have failed.
    /// This includes this scheduler if it is checking in for the first time.
    /// </summary>
    protected async ValueTask<List<SchedulerStateRecord>> FindFailedInstances(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        try
        {
            List<SchedulerStateRecord> failedInstances = [];
            bool foundThisScheduler = false;

            var states = await Delegate.SelectSchedulerStateRecords(conn, instanceId: null, cancellationToken).ConfigureAwait(false);

            foreach (SchedulerStateRecord record in states)
            {
                // find own record...
                if (record.SchedulerInstanceId == InstanceId)
                {
                    foundThisScheduler = true;
                    if (firstCheckIn)
                    {
                        failedInstances.Add(record);
                    }
                }
                else
                {
                    // find failed instances...
                    if (CalcFailedIfAfter(record) < timeProvider.GetUtcNow())
                    {
                        failedInstances.Add(record);
                    }
                }
            }

            // The first time through, also check for orphaned fired triggers.
            if (firstCheckIn)
            {
                failedInstances.AddRange(await FindOrphanedFailedInstances(conn, states, cancellationToken).ConfigureAwait(false));
            }

            // If not the first time but we didn't find our own instance, then
            // Someone must have done recovery for us.
            if (!foundThisScheduler && !firstCheckIn)
            {
                // TODO: revisit when handle self-failed-out impl'ed (see TODO in clusterCheckIn() below)
                Logger.RecoveredByAnotherInstance(InstanceId);
            }

            return failedInstances;
        }
        catch (Exception e)
        {
            LastCheckin = timeProvider.GetUtcNow();
            Throw.JobPersistenceException("Failure identifying failed instances when checking-in: " + e.Message, e);
            return default;
        }
    }

    /// <summary>
    /// Create dummy <see cref="SchedulerStateRecord" /> objects for fired triggers
    /// that have no scheduler state record.  Checkin timestamp and interval are
    /// left as zero on these dummy <see cref="SchedulerStateRecord" /> objects.
    /// </summary>
    /// <param name="conn"></param>
    /// <param name="schedulerStateRecords">List of all current <see cref="SchedulerStateRecord" />s</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    private async ValueTask<List<SchedulerStateRecord>> FindOrphanedFailedInstances(
        ConnectionAndTransactionHolder conn,
        List<SchedulerStateRecord> schedulerStateRecords,
        CancellationToken cancellationToken)
    {
        List<SchedulerStateRecord> orphanedInstances = [];

        var names = await Delegate.SelectFiredTriggerInstanceNames(conn, cancellationToken).ConfigureAwait(false);
        if (names.Count > 0)
        {
            var allFiredTriggerInstanceNames = new HashSet<string>(names);
            foreach (SchedulerStateRecord record in schedulerStateRecords)
            {
                allFiredTriggerInstanceNames.Remove(record.SchedulerInstanceId);
            }

            foreach (string name in allFiredTriggerInstanceNames)
            {
                SchedulerStateRecord orphanedInstance = new(name, CheckinTimestamp: default, CheckinInterval: default);
                orphanedInstances.Add(orphanedInstance);

                Logger.OrphanedFiredTriggersFound(orphanedInstance.SchedulerInstanceId);
            }
        }

        return orphanedInstances;
    }

    protected DateTimeOffset CalcFailedIfAfter(SchedulerStateRecord record)
    {
        TimeSpan passed = timeProvider.GetUtcNow() - LastCheckin;
        TimeSpan ts = record.CheckinInterval > passed ? record.CheckinInterval : passed;
        return record.CheckinTimestamp.Add(ts).Add(ClusterCheckinMisfireThreshold);
    }

    protected async ValueTask<List<SchedulerStateRecord>> ClusterCheckIn(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        var failedInstances = await FindFailedInstances(conn, cancellationToken).ConfigureAwait(false);
        try
        {
            // TODO: handle self-failed-out

            // check in...
            var checkinTime = timeProvider.GetUtcNow();
            if (await Delegate.UpdateSchedulerState(conn, InstanceId, checkinTime, cancellationToken).ConfigureAwait(false) == 0)
            {
                await Delegate.InsertSchedulerState(conn, InstanceId, checkinTime, ClusterCheckinInterval, cancellationToken).ConfigureAwait(false);
            }
            LastCheckin = checkinTime;
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Failure updating scheduler state when checking-in: " + e.Message, e);
        }

        return failedInstances;
    }

    protected async ValueTask ClusterRecover(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<SchedulerStateRecord> failedInstances,
        CancellationToken cancellationToken = default)
    {
        if (failedInstances.Count > 0)
        {
            long recoverIds = timeProvider.GetTimestamp();

            Logger.FailedInstancesDetected(failedInstances.Count);
            try
            {
                foreach (SchedulerStateRecord record in failedInstances)
                {
                    Logger.ScanningFailedInstance(record.SchedulerInstanceId);

                    var nodeFiredTriggers = await Delegate.SelectFiredTriggerRecords(conn, new FiredTriggerQuery { InstanceId = record.SchedulerInstanceId }, cancellationToken).ConfigureAwait(false);

                    int acquiredCount = 0;
                    int recoveredCount = 0;
                    int otherCount = 0;

                    var triggerKeys = new HashSet<TriggerKey>();

                    // Determine whether to preserve EXECUTING fired trigger records for
                    // DisallowConcurrentExecution jobs. On the first detection the node may
                    // still be alive, so we preserve the record and give it a grace period.
                    // Once the grace period expires (elapsed time exceeds two failure detection
                    // cycles), full cleanup is performed. This decision is derived entirely from
                    // DB state so all cluster nodes make the same choice (#2817).
                    bool isOrphanedInstance = record.CheckinInterval == default && record.CheckinTimestamp == default;
                    bool canDeferRecovery;
                    if (isOrphanedInstance)
                    {
                        canDeferRecovery = false;
                    }
                    else
                    {
                        TimeSpan elapsed = timeProvider.GetUtcNow() - record.CheckinTimestamp;
                        TimeSpan gracePeriod = record.CheckinInterval.Add(record.CheckinInterval).Add(ClusterCheckinMisfireThreshold);
                        canDeferRecovery = elapsed < gracePeriod;
                    }
                    HashSet<string>? preservedFireInstanceIds = null;
                    int deferredCount = 0;

                    foreach (FiredTriggerRecord firedTrigger in nodeFiredTriggers)
                    {
                        TriggerKey triggerKey = firedTrigger.TriggerKey;
                        JobKey? jobKey = firedTrigger.JobKey;

                        triggerKeys.Add(triggerKey);

                        // For timed-out (non-orphan) instances on first detection, preserve
                        // EXECUTING records for DisallowConcurrentExecution jobs. The node may
                        // still be alive and running the job. If it truly died, on the second
                        // detection (after the grace period) full cleanup will be performed.
                        if (canDeferRecovery
                            && firedTrigger.FireInstanceState == StoredTriggerState.Executing
                            && firedTrigger.JobDisallowsConcurrentExecution)
                        {
                            preservedFireInstanceIds ??= [];
                            preservedFireInstanceIds.Add(firedTrigger.FireInstanceId);
                            deferredCount++;
                            Logger.RecoveryDeferred(jobKey, firedTrigger.FireInstanceId, record.SchedulerInstanceId);
                            continue;
                        }

                        // release blocked triggers..
                        if (firedTrigger.FireInstanceState == StoredTriggerState.Blocked)
                        {
                            await Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jobKey!, StoredTriggerState.Waiting, StoredTriggerState.Blocked, cancellationToken).ConfigureAwait(false);
                        }
                        else if (firedTrigger.FireInstanceState == StoredTriggerState.PausedBlocked)
                        {
                            await Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jobKey!, StoredTriggerState.Paused, StoredTriggerState.PausedBlocked, cancellationToken).ConfigureAwait(false);
                        }

                        // release acquired triggers..
                        if (firedTrigger.FireInstanceState == StoredTriggerState.Acquired)
                        {
                            await Delegate.UpdateTriggerStateFromOtherState(conn, triggerKey, StoredTriggerState.Waiting, StoredTriggerState.Acquired, cancellationToken).ConfigureAwait(false);
                            acquiredCount++;
                        }
                        else if (firedTrigger.JobRequestsRecovery)
                        {
                            // handle jobs marked for recovery that were not fully
                            // executed..
                            if (await JobExists(conn, jobKey!, cancellationToken).ConfigureAwait(false))
                            {
                                SimpleTriggerImpl recoveryTrigger = new SimpleTriggerImpl(timeProvider)
                                {
                                    Key = new TriggerKey($"recover_{record.SchedulerInstanceId}_{recoverIds++}", SchedulerConstants.DefaultRecoveryGroup),
                                    StartTimeUtc = firedTrigger.FireTimestamp,
                                    JobKey = jobKey!,
                                    MisfireInstructionCode = MisfireInstruction.SimpleTrigger.FireNow,
                                    Priority = firedTrigger.Priority
                                };

                                JobDataMap jobDataMap = await Delegate.SelectTriggerJobDataMap(conn, triggerKey, cancellationToken).ConfigureAwait(false);
                                jobDataMap[SchedulerConstants.FailedJobOriginalTriggerName] = triggerKey.Name;
                                jobDataMap[SchedulerConstants.FailedJobOriginalTriggerGroup] = triggerKey.Group;
                                jobDataMap[SchedulerConstants.FailedJobOriginalTriggerFireTime] = Convert.ToString(firedTrigger.FireTimestamp, CultureInfo.InvariantCulture);
                                recoveryTrigger.JobDataMap = jobDataMap;

                                recoveryTrigger.ComputeFirstFireTimeUtc(null);
                                await AddTrigger(conn, recoveryTrigger, null, false, StoredTriggerState.Waiting, false, true, cancellationToken).ConfigureAwait(false);
                                recoveredCount++;
                            }
                            else
                            {
                                Logger.FailedJobNoLongerExists(jobKey);
                                otherCount++;
                            }
                        }
                        else
                        {
                            otherCount++;
                        }

                        // free up stateful job's triggers
                        if (firedTrigger.JobDisallowsConcurrentExecution)
                        {
                            await Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jobKey!, StoredTriggerState.Waiting, StoredTriggerState.Blocked, cancellationToken).ConfigureAwait(false);
                            await Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jobKey!, StoredTriggerState.Paused, StoredTriggerState.PausedBlocked, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    // Delete fired triggers, preserving EXECUTING records for
                    // DisallowConcurrentExecution jobs on timed-out (non-orphan) instances
                    if (preservedFireInstanceIds is { Count: > 0 })
                    {
                        foreach (FiredTriggerRecord firedTrigger in nodeFiredTriggers)
                        {
                            if (!preservedFireInstanceIds.Contains(firedTrigger.FireInstanceId))
                            {
                                await Delegate.DeleteFiredTrigger(conn, firedTrigger.FireInstanceId, cancellationToken).ConfigureAwait(false);
                            }
                        }
                    }
                    else
                    {
                        await Delegate.DeleteFiredTriggers(conn, new FiredTriggerQuery { InstanceId = record.SchedulerInstanceId }, cancellationToken).ConfigureAwait(false);
                    }

                    // Check if any of the fired triggers we just deleted were the last fired trigger
                    // records of a COMPLETE trigger.
                    int completeCount = 0;
                    foreach (TriggerKey triggerKey in triggerKeys)
                    {
                        if (await Delegate.SelectTriggerState(conn, triggerKey, cancellationToken).ConfigureAwait(false) == StoredTriggerState.Complete)
                        {
                            var firedTriggers = await Delegate.SelectFiredTriggerRecords(conn, new FiredTriggerQuery { Trigger = triggerKey }, cancellationToken).ConfigureAwait(false);
                            if (firedTriggers.Count == 0)
                            {
                                if (await DeleteTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false))
                                {
                                    completeCount++;
                                }
                            }
                        }
                    }

                    // Every fired-trigger row this pass acted on: the three counters are the three arms of
                    // one branch, so exactly one of them was raised per row that was not deferred, and a
                    // deferred row is one this node decided not to recover.
                    Meters.ClusterTriggersRecovered(
                        InstanceName,
                        InstanceId,
                        record.SchedulerInstanceId,
                        acquiredCount + recoveredCount + otherCount);

                    if (acquiredCount > 0)
                    {
                        Logger.AcquiredTriggersFreed(acquiredCount);
                    }

                    if (completeCount > 0)
                    {
                        Logger.CompleteTriggersDeleted(completeCount);
                    }

                    if (recoveredCount > 0)
                    {
                        Logger.RecoverableJobsScheduled(recoveredCount);
                    }

                    if (otherCount > 0)
                    {
                        Logger.OtherFailedJobsCleanedUp(otherCount);
                    }

                    if (deferredCount > 0)
                    {
                        Logger.ExecutingJobRecoveriesDeferred(deferredCount);
                    }

                    if (record.SchedulerInstanceId != InstanceId)
                    {
                        if (preservedFireInstanceIds is { Count: > 0 })
                        {
                            // Don't delete scheduler state — keep it with the stale timestamp so
                            // the instance continues to be detected as failed. As elapsed time
                            // grows past the grace period, the next recovery will do full cleanup.
                        }
                        else
                        {
                            // Sticky failover: release only AUTO-CLAIMED pins from the dead node
                            // (explicit pins are left untouched so the original node reclaims them
                            // when it returns). Resetting to the "*" sentinel rather than to another
                            // node lets any eligible node claim the trigger on its next fire, which
                            // correctly respects execution group limits. This must happen before the
                            // state row is deleted, and relies on the already-confirmed dead-node
                            // detection from FindFailedInstances.
                            int repinned = await Delegate.RepinTriggersFromDeadNode(
                                conn, record.SchedulerInstanceId, StdAdoConstants.AutoPinSentinel, cancellationToken).ConfigureAwait(false);
                            if (repinned > 0)
                            {
                                Logger.AutoPinnedTriggersReleased(repinned, record.SchedulerInstanceId);
                            }

                            await Delegate.DeleteSchedulerState(conn, record.SchedulerInstanceId, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Throw.JobPersistenceException("Failure recovering jobs: " + e.Message, e);
            }
        }
    }
}
