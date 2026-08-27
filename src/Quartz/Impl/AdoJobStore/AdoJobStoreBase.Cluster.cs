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

    /// <summary>
    /// Recovers everything the given failed instances left behind.
    /// </summary>
    protected async ValueTask ClusterRecover(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<SchedulerStateRecord> failedInstances,
        CancellationToken cancellationToken = default)
    {
        if (failedInstances.Count == 0)
        {
            return;
        }

        Logger.FailedInstancesDetected(failedInstances.Count);
        try
        {
            RecoveryTriggerNaming naming = new(timeProvider.GetTimestamp());
            foreach (SchedulerStateRecord record in failedInstances)
            {
                await RecoverFailedInstance(conn, record, naming, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            Throw.JobPersistenceException("Failure recovering jobs: " + e.Message, e);
        }
    }

    /// <summary>
    /// Everything one failed node's residue needs doing to it, in the order it needs doing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The steps are the sentence recovery is: work out what the rows mean, release what the node had
    /// acquired, unblock what its executions were holding, reschedule what asked to be recovered, delete
    /// its fired-trigger rows, delete the triggers those rows were the last of, and give up its
    /// registration.
    /// </para>
    /// <para>
    /// All of it runs in the caller's transaction under the trigger-access lock, so no other node sees
    /// the state between two steps. That is what lets the first three be grouped by what they do rather
    /// than issued row by row: they touch disjoint rows — a trigger's own state, its job's siblings, and
    /// a trigger that does not exist yet — and a recovery trigger is stored WAITING outright rather than
    /// through the blocked-state check, which is the only thing that would have made it depend on when
    /// the unblocking ran.
    /// </para>
    /// </remarks>
    private async ValueTask RecoverFailedInstance(
        ConnectionAndTransactionHolder conn,
        SchedulerStateRecord record,
        RecoveryTriggerNaming naming,
        CancellationToken cancellationToken)
    {
        Logger.ScanningFailedInstance(record.SchedulerInstanceId);

        List<FiredTriggerRecord> firedTriggers = await Delegate.SelectFiredTriggerRecords(
            conn,
            new FiredTriggerQuery { InstanceId = record.SchedulerInstanceId },
            cancellationToken).ConfigureAwait(false);

        FailedInstanceResidue residue = PlanRecovery(record, firedTriggers);

        await ReleaseAcquiredTriggers(conn, residue, cancellationToken).ConfigureAwait(false);
        await UnblockInterruptedJobs(conn, residue, cancellationToken).ConfigureAwait(false);
        RecoveryScheduling scheduling = await ScheduleRecoveryTriggers(conn, record, residue, naming, cancellationToken).ConfigureAwait(false);
        await DeleteFiredTriggerRows(conn, record, residue, cancellationToken).ConfigureAwait(false);
        int completeCount = await DeleteTriggersLeftComplete(conn, residue, cancellationToken).ConfigureAwait(false);

        ReportRecovery(record, residue, scheduling, completeCount);

        await ReleaseFailedInstanceRegistration(conn, record, residue, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads one failed node's fired-trigger rows and decides what each of them means, without writing
    /// anything: which triggers were merely reserved, which jobs were left blocked behind an execution,
    /// which executions asked to be recovered, and which are held back this pass.
    /// </summary>
    /// <remarks>
    /// A deferral is reported as it is decided, because the row it names is the only place the decision
    /// is visible — nothing downstream knows the row was ever there.
    /// </remarks>
    private FailedInstanceResidue PlanRecovery(SchedulerStateRecord record, List<FiredTriggerRecord> firedTriggers)
    {
        FailedInstanceResidue residue = new(firedTriggers);
        bool canDeferRecovery = CanDeferRecovery(record);

        foreach (FiredTriggerRecord firedTrigger in firedTriggers)
        {
            residue.TriggerKeys.Add(firedTrigger.TriggerKey);

            // For timed-out (non-orphan) instances on first detection, preserve EXECUTING records for
            // DisallowConcurrentExecution jobs. The node may still be alive and running the job. If it
            // truly died, on the second detection (after the grace period) full cleanup is performed.
            if (canDeferRecovery
                && firedTrigger.FireInstanceState == StoredTriggerState.Executing
                && firedTrigger.JobDisallowsConcurrentExecution)
            {
                residue.PreservedFireInstanceIds.Add(firedTrigger.FireInstanceId);
                Logger.RecoveryDeferred(firedTrigger.JobKey, firedTrigger.FireInstanceId, record.SchedulerInstanceId);
                continue;
            }

            // A row in one of the blocked states is a trigger that was waiting behind an execution which
            // is not going to finish, so its job's triggers go back to where they came from.
            if (firedTrigger.FireInstanceState == StoredTriggerState.Blocked)
            {
                residue.JobsToUnblock.Add(firedTrigger.JobKey!);
            }
            else if (firedTrigger.FireInstanceState == StoredTriggerState.PausedBlocked)
            {
                residue.JobsToUnpause.Add(firedTrigger.JobKey!);
            }

            if (firedTrigger.FireInstanceState == StoredTriggerState.Acquired)
            {
                residue.AcquiredTriggerKeys.Add(firedTrigger.TriggerKey);
                residue.AcquiredRowCount++;
            }
            else if (firedTrigger.JobRequestsRecovery)
            {
                residue.Recoverable.Add(firedTrigger);
            }
            else
            {
                residue.OtherRowCount++;
            }

            // And the siblings this execution itself blocked have to be let go, or the job never runs
            // again — paused ones back to paused rather than to waiting.
            if (firedTrigger.JobDisallowsConcurrentExecution)
            {
                residue.JobsToUnblock.Add(firedTrigger.JobKey!);
                residue.JobsToUnpause.Add(firedTrigger.JobKey!);
            }
        }

        return residue;
    }

    /// <summary>
    /// Whether an <see cref="StoredTriggerState.Executing" /> row of a <see cref="DisallowConcurrentExecutionAttribute" />
    /// job is held back rather than recovered this pass.
    /// </summary>
    /// <remarks>
    /// On the first detection the node may still be alive, so the record is preserved and given a grace
    /// period. Once that expires — elapsed time past two failure detection cycles — full cleanup is
    /// performed. The decision is derived entirely from database state, so every node of the cluster
    /// makes the same one (#2817).
    /// </remarks>
    private bool CanDeferRecovery(SchedulerStateRecord record)
    {
        bool isOrphanedInstance = record.CheckinInterval == default && record.CheckinTimestamp == default;
        if (isOrphanedInstance)
        {
            // An orphan has no check-in history to grant a grace period from.
            return false;
        }

        TimeSpan elapsed = timeProvider.GetUtcNow() - record.CheckinTimestamp;
        TimeSpan gracePeriod = record.CheckinInterval.Add(record.CheckinInterval).Add(ClusterCheckinMisfireThreshold);
        return elapsed < gracePeriod;
    }

    /// <summary>
    /// Puts back to WAITING every trigger the failed node had reserved and never fired.
    /// </summary>
    private ValueTask ReleaseAcquiredTriggers(
        ConnectionAndTransactionHolder conn,
        FailedInstanceResidue residue,
        CancellationToken cancellationToken)
    {
        if (residue.AcquiredTriggerKeys.Count == 0)
        {
            return default;
        }

        return Delegate.UpdateTriggerStatesFromOtherState(
            conn,
            residue.AcquiredTriggerKeys,
            StoredTriggerState.Waiting,
            StoredTriggerState.Acquired,
            cancellationToken);
    }

    /// <summary>
    /// Lets go of the triggers the failed node's executions were holding.
    /// </summary>
    /// <remarks>
    /// Two statements per job when they travel one row at a time, and the same job over and over when a
    /// node left several rows behind; the job keys are a set and each transition is one batch.
    /// </remarks>
    private async ValueTask UnblockInterruptedJobs(
        ConnectionAndTransactionHolder conn,
        FailedInstanceResidue residue,
        CancellationToken cancellationToken)
    {
        if (residue.JobsToUnblock.Count > 0)
        {
            await Delegate.UpdateTriggerStatesForJobsFromOtherState(
                conn,
                residue.JobsToUnblock,
                StoredTriggerState.Waiting,
                StoredTriggerState.Blocked,
                cancellationToken).ConfigureAwait(false);
        }

        if (residue.JobsToUnpause.Count > 0)
        {
            await Delegate.UpdateTriggerStatesForJobsFromOtherState(
                conn,
                residue.JobsToUnpause,
                StoredTriggerState.Paused,
                StoredTriggerState.PausedBlocked,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Schedules a replacement firing for each interrupted execution whose job asked for one.
    /// </summary>
    /// <remarks>
    /// Row by row, and it stays that way: each one reads whether its job still exists and what its
    /// trigger's data map holds, and both answers decide whether the next statement is issued at all.
    /// </remarks>
    private async ValueTask<RecoveryScheduling> ScheduleRecoveryTriggers(
        ConnectionAndTransactionHolder conn,
        SchedulerStateRecord record,
        FailedInstanceResidue residue,
        RecoveryTriggerNaming naming,
        CancellationToken cancellationToken)
    {
        int scheduled = 0;
        int jobsGone = 0;

        foreach (FiredTriggerRecord firedTrigger in residue.Recoverable)
        {
            JobKey jobKey = firedTrigger.JobKey!;
            if (!await JobExists(conn, jobKey, cancellationToken).ConfigureAwait(false))
            {
                Logger.FailedJobNoLongerExists(jobKey);
                jobsGone++;
                continue;
            }

            TriggerKey triggerKey = firedTrigger.TriggerKey;
            SimpleTriggerImpl recoveryTrigger = new SimpleTriggerImpl(timeProvider)
            {
                Key = naming.Next(record.SchedulerInstanceId),
                StartTimeUtc = firedTrigger.FireTimestamp,
                JobKey = jobKey,
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
            scheduled++;
        }

        return new RecoveryScheduling(scheduled, jobsGone);
    }

    /// <summary>
    /// Deletes the failed node's fired-trigger rows, keeping the ones recovery is holding back.
    /// </summary>
    private async ValueTask DeleteFiredTriggerRows(
        ConnectionAndTransactionHolder conn,
        SchedulerStateRecord record,
        FailedInstanceResidue residue,
        CancellationToken cancellationToken)
    {
        if (residue.PreservedFireInstanceIds.Count == 0)
        {
            // With nothing preserved the whole instance goes in one statement.
            await Delegate.DeleteFiredTriggers(
                conn,
                new FiredTriggerQuery { InstanceId = record.SchedulerInstanceId },
                cancellationToken).ConfigureAwait(false);
            return;
        }

        List<string> entryIds = new(residue.FiredTriggers.Count - residue.PreservedFireInstanceIds.Count);
        foreach (FiredTriggerRecord firedTrigger in residue.FiredTriggers)
        {
            if (!residue.PreservedFireInstanceIds.Contains(firedTrigger.FireInstanceId))
            {
                entryIds.Add(firedTrigger.FireInstanceId);
            }
        }

        if (entryIds.Count > 0)
        {
            await Delegate.DeleteFiredTriggers(conn, entryIds, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Deletes the triggers whose last fired-trigger row this pass has just removed and which had
    /// already run to COMPLETE — nothing else is ever going to clean them up.
    /// </summary>
    private async ValueTask<int> DeleteTriggersLeftComplete(
        ConnectionAndTransactionHolder conn,
        FailedInstanceResidue residue,
        CancellationToken cancellationToken)
    {
        int completeCount = 0;

        foreach (TriggerKey triggerKey in residue.TriggerKeys)
        {
            if (await Delegate.SelectTriggerState(conn, triggerKey, cancellationToken).ConfigureAwait(false) != StoredTriggerState.Complete)
            {
                continue;
            }

            List<FiredTriggerRecord> firedTriggers = await Delegate.SelectFiredTriggerRecords(
                conn,
                new FiredTriggerQuery { Trigger = triggerKey },
                cancellationToken).ConfigureAwait(false);

            if (firedTriggers.Count == 0
                && await DeleteTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false))
            {
                completeCount++;
            }
        }

        return completeCount;
    }

    /// <summary>
    /// Says what this pass did with one failed node's work, to the measurements and to the log.
    /// </summary>
    private void ReportRecovery(
        SchedulerStateRecord record,
        FailedInstanceResidue residue,
        RecoveryScheduling scheduling,
        int completeCount)
    {
        int otherCount = residue.OtherRowCount + scheduling.JobsGone;

        // Every fired-trigger row this pass acted on: the three counters are the three arms of one
        // branch, so exactly one of them was raised per row that was not deferred, and a deferred row is
        // one this node decided not to recover.
        Meters.ClusterTriggersRecovered(
            InstanceName,
            InstanceId,
            record.SchedulerInstanceId,
            residue.AcquiredRowCount + scheduling.Scheduled + otherCount);

        if (residue.AcquiredRowCount > 0)
        {
            Logger.AcquiredTriggersFreed(residue.AcquiredRowCount);
        }

        if (completeCount > 0)
        {
            Logger.CompleteTriggersDeleted(completeCount);
        }

        if (scheduling.Scheduled > 0)
        {
            Logger.RecoverableJobsScheduled(scheduling.Scheduled);
        }

        if (otherCount > 0)
        {
            Logger.OtherFailedJobsCleanedUp(otherCount);
        }

        if (residue.PreservedFireInstanceIds.Count > 0)
        {
            Logger.ExecutingJobRecoveriesDeferred(residue.PreservedFireInstanceIds.Count);
        }
    }

    /// <summary>
    /// Releases the pins the failed node claimed for itself and deletes its check-in row, which is what
    /// stops the cluster paying to rediscover the same corpse.
    /// </summary>
    private async ValueTask ReleaseFailedInstanceRegistration(
        ConnectionAndTransactionHolder conn,
        SchedulerStateRecord record,
        FailedInstanceResidue residue,
        CancellationToken cancellationToken)
    {
        if (record.SchedulerInstanceId == InstanceId)
        {
            // This node recovering its own previous run on its first check-in. That row is the live
            // registration it is about to check in against, and its pins are its own.
            return;
        }

        if (residue.PreservedFireInstanceIds.Count > 0)
        {
            // Don't delete scheduler state — keep it with the stale timestamp so the instance continues
            // to be detected as failed. As elapsed time grows past the grace period, the next recovery
            // will do full cleanup.
            return;
        }

        // Sticky failover: release only AUTO-CLAIMED pins from the dead node (explicit pins are left
        // untouched so the original node reclaims them when it returns). Resetting to the "*" sentinel
        // rather than to another node lets any eligible node claim the trigger on its next fire, which
        // correctly respects execution group limits. This must happen before the state row is deleted,
        // and relies on the already-confirmed dead-node detection from FindFailedInstances.
        int repinned = await Delegate.RepinTriggersFromDeadNode(
            conn, record.SchedulerInstanceId, StdAdoConstants.AutoPinSentinel, cancellationToken).ConfigureAwait(false);
        if (repinned > 0)
        {
            Logger.AutoPinnedTriggersReleased(repinned, record.SchedulerInstanceId);
        }

        await Delegate.DeleteSchedulerState(conn, record.SchedulerInstanceId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// What one failed node's fired-trigger rows turned out to mean, worked out before anything is
    /// written and then acted on a set at a time.
    /// </summary>
    private sealed class FailedInstanceResidue(List<FiredTriggerRecord> firedTriggers)
    {
        /// <summary>Every fired-trigger row the failed node left, in the order they were read.</summary>
        public List<FiredTriggerRecord> FiredTriggers { get; } = firedTriggers;

        /// <summary>Every trigger those rows name, which is the set the COMPLETE sweep looks at.</summary>
        public HashSet<TriggerKey> TriggerKeys { get; } = [];

        /// <summary>The triggers the node had reserved and never fired.</summary>
        public HashSet<TriggerKey> AcquiredTriggerKeys { get; } = [];

        /// <summary>
        /// How many rows those were. Counted separately from the key set, because the meter counts rows
        /// acted on and two rows naming one trigger are still two rows.
        /// </summary>
        public int AcquiredRowCount { get; set; }

        /// <summary>The interrupted executions whose job asks to be recovered.</summary>
        public List<FiredTriggerRecord> Recoverable { get; } = [];

        /// <summary>The jobs whose triggers go back from BLOCKED to WAITING.</summary>
        public HashSet<JobKey> JobsToUnblock { get; } = [];

        /// <summary>The jobs whose triggers go back from PAUSED_BLOCKED to PAUSED.</summary>
        public HashSet<JobKey> JobsToUnpause { get; } = [];

        /// <summary>
        /// The rows held back this pass: an execution of a serial job that may still be running on a node
        /// that has only missed a check-in.
        /// </summary>
        public HashSet<string> PreservedFireInstanceIds { get; } = [];

        /// <summary>
        /// Rows that were neither reserved nor recoverable — an interrupted execution of a job that does
        /// not ask to be replayed. Nothing is done with them beyond deleting the row.
        /// </summary>
        public int OtherRowCount { get; set; }
    }

    /// <summary>
    /// What <see cref="ScheduleRecoveryTriggers" /> found: how many replacement firings it scheduled, and
    /// how many it could not because the job had been deleted since.
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct RecoveryScheduling(int Scheduled, int JobsGone);

    /// <summary>
    /// Names the recovery triggers of one recovery pass.
    /// </summary>
    /// <remarks>
    /// The counter runs across the whole pass rather than per failed node, so two nodes recovered
    /// together cannot be given one name twice. It is seeded from the clock, which is what keeps the
    /// names of this pass clear of the pass before it.
    /// </remarks>
    private sealed class RecoveryTriggerNaming(long firstId)
    {
        private long nextId = firstId;

        public TriggerKey Next(string failedInstanceId)
        {
            return new TriggerKey($"recover_{failedInstanceId}_{nextId++}", SchedulerConstants.DefaultRecoveryGroup);
        }
    }
}
