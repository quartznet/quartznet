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

namespace Quartz.Impl.AdoJobStore;

public abstract partial class AdoJobStoreBase
{
    /// <summary>
    /// Inform the <see cref="IJobStore" /> that the scheduler no longer plans to
    /// fire the given <see cref="ITrigger" />, that it had previously acquired
    /// (reserved).
    /// </summary>
    public async ValueTask ReleaseAcquiredTrigger(IOperableTrigger trigger, CancellationToken cancellationToken = default)
    {
        await RetryExecuteInLocalTransactionLock(
            SchedulerLock.TriggerAccess,
            conn => ReleaseAcquiredTrigger(conn, trigger, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    protected ValueTask ReleaseAcquiredTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            async () =>
            {
                await Delegate.UpdateTriggerStateFromOtherState(conn, trigger.Key, StoredTriggerState.Waiting, StoredTriggerState.Acquired, cancellationToken).ConfigureAwait(false);
                await Delegate.UpdateTriggerStateFromOtherState(conn, trigger.Key, StoredTriggerState.Waiting, StoredTriggerState.Blocked, cancellationToken).ConfigureAwait(false);
                await Delegate.DeleteFiredTrigger(conn, trigger.FireInstanceId, cancellationToken).ConfigureAwait(false);
            },
            "release acquired trigger");
    }

    /// <summary>
    /// Inform the <see cref="IJobStore" /> that the scheduler has completed the
    /// firing of the given <see cref="ITrigger" /> (and the execution its
    /// associated <see cref="IJob" />), and that the <see cref="JobDataMap" />
    /// in the given <see cref="IJobDetail" /> should be updated if the <see cref="IJob" />
    /// is stateful.
    /// </summary>
    public async ValueTask TriggeredJobComplete(IOperableTrigger trigger, IJobDetail jobDetail, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default)
    {
        // Completion bookkeeping belongs to the scheduler, not to the job, and it retries a failing
        // JobPersistenceException until it succeeds. If a job body left an enlistment behind, this
        // would borrow a connection whose transaction is long gone and retry against it forever,
        // leaving the fired trigger uncleaned and its DisallowConcurrentExecution siblings blocked.
        using var suppression = AmbientConnection.Suppress();

        await RetryExecuteInLocalTransactionLock(
            SchedulerLock.TriggerAccess,
            conn => TriggeredJobComplete(conn, trigger, jobDetail, triggerInstructionCode, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        // Deliberately after the transaction, and only if it committed: these run listener code, which
        // has no business executing inside the store's transaction or seeing a state that may roll back.
        if (triggerInstructionCode == SchedulerInstruction.SetTriggerError)
        {
            await signaler.NotifySchedulerListenersTriggerInError(trigger.Key, cancellationToken).ConfigureAwait(false);
        }
        else if (triggerInstructionCode == SchedulerInstruction.SetAllJobTriggersError)
        {
            await signaler.NotifySchedulerListenersTriggersInError(trigger.JobKey, cancellationToken).ConfigureAwait(false);
        }
    }

    protected async ValueTask TriggeredJobComplete(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        IJobDetail jobDetail,
        SchedulerInstruction triggerInstructionCode,
        CancellationToken cancellationToken = default)
    {
        await Guarded(
            async () =>
            {
                if (triggerInstructionCode == SchedulerInstruction.DeleteTrigger)
                {
                    if (!trigger.NextFireTimeUtc.HasValue)
                    {
                        // double check for possible reschedule within job
                        // execution, which would cancel the need to delete...
                        var header = await Delegate.SelectTriggerHeader(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
                        if (header is not null && !header.NextFireTimeUtc.HasValue)
                        {
                            await DeleteTrigger(conn, trigger.Key, jobDetail, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        await DeleteTrigger(conn, trigger.Key, jobDetail, cancellationToken).ConfigureAwait(false);
                        conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
                    }
                }
                else if (triggerInstructionCode == SchedulerInstruction.SetTriggerComplete)
                {
                    await Delegate.UpdateTriggerState(conn, trigger.Key, StoredTriggerState.Complete, cancellationToken).ConfigureAwait(false);
                    conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
                }
                else if (triggerInstructionCode == SchedulerInstruction.SetTriggerError)
                {
                    Logger.TriggerSetToError(trigger.Key);
                    await Delegate.UpdateTriggerState(conn, trigger.Key, StoredTriggerState.Error, cancellationToken).ConfigureAwait(false);
                    conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
                }
                else if (triggerInstructionCode == SchedulerInstruction.SetAllJobTriggersComplete)
                {
                    await Delegate.UpdateTriggerStatesForJob(conn, trigger.JobKey, StoredTriggerState.Complete, cancellationToken).ConfigureAwait(false);
                    conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
                }
                else if (triggerInstructionCode == SchedulerInstruction.SetAllJobTriggersError)
                {
                    Logger.JobTriggersSetToError(trigger.JobKey);
                    await Delegate.UpdateTriggerStatesForJob(conn, trigger.JobKey, StoredTriggerState.Error, cancellationToken).ConfigureAwait(false);
                    conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
                }

                if (jobDetail.ConcurrentExecutionDisallowed)
                {
                    await Delegate.UpdateTriggerStatesForJobFromOtherState(conn, jobDetail.Key, unblockJobTriggersTransitions, cancellationToken).ConfigureAwait(false);
                    conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;

                    await RecoverUnblockedMisfires(conn, jobDetail.Key, cancellationToken).ConfigureAwait(false);
                }
                if (jobDetail.PersistJobDataAfterExecution && jobDetail.JobDataMap.Dirty)
                {
                    // Its own catch rather than a Guarded call: the two failures name different
                    // operations - one of them the serialization inside the write - where Guarded
                    // reports one operation with an optional reason.
                    try
                    {
                        await Delegate.UpdateJobData(conn, jobDetail, cancellationToken).ConfigureAwait(false);
                    }
                    catch (IOException e)
                    {
                        Throw.JobPersistenceException("Couldn't serialize job data: " + e.Message, e);
                    }
                    catch (Exception e)
                    {
                        Throw.JobPersistenceException("Couldn't update job data: " + e.Message, e);
                    }
                }
            },
            "update trigger state(s)").ConfigureAwait(false);

        await Guarded(
            () => Delegate.DeleteFiredTrigger(conn, trigger.FireInstanceId, cancellationToken),
            "delete fired trigger").ConfigureAwait(false);
    }

    /// <summary>
    /// The transitions that free a job's triggers once it has finished executing — the mirror image of
    /// the blocking its fire applied.
    /// </summary>
    private static readonly TriggerStateTransition[] unblockJobTriggersTransitions =
    [
        new(StoredTriggerState.Blocked, StoredTriggerState.Waiting),
        new(StoredTriggerState.PausedBlocked, StoredTriggerState.Paused)
    ];

    /// <summary>
    /// Applies the misfire policy of every trigger the completion above has just unblocked.
    /// </summary>
    /// <remarks>
    /// A trigger that sat BLOCKED while its job ran may well have passed a fire time meanwhile, and
    /// nothing else would notice: misfire recovery does not look at BLOCKED triggers, and by the time
    /// this runs they are WAITING with a fire time in the past. There is no way to ask the database
    /// which triggers just changed state, but asking for the job's triggers that are WAITING <em>now</em>
    /// describes the same set — in one read, where the previous shape read the job's triggers, then each
    /// trigger's state one statement at a time, then loaded again every trigger that turned out to be
    /// waiting.
    /// </remarks>
    /// <param name="conn">The DB connection.</param>
    /// <param name="jobKey">The job whose triggers were just unblocked.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    private async ValueTask RecoverUnblockedMisfires(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken)
    {
        List<TriggerKey> waiting = await Delegate.SelectTriggerKeysForJob(conn, jobKey, StoredTriggerState.Waiting, cancellationToken).ConfigureAwait(false);
        if (waiting.Count == 0)
        {
            return;
        }

        List<IOperableTrigger> triggers = await Delegate.SelectTriggers(conn, waiting, cancellationToken).ConfigureAwait(false);

        DateTimeOffset misfireTime = timeProvider.GetUtcNow();
        if (MisfireThreshold > TimeSpan.Zero)
        {
            misfireTime = misfireTime.AddMilliseconds(-1 * MisfireThreshold.TotalMilliseconds);
        }

        List<MisfiredTriggerUpdate>? updates = null;
        List<IOperableTrigger>? finalized = null;

        foreach (IOperableTrigger trigger in triggers)
        {
            if (trigger.NextFireTimeUtc.GetValueOrDefault() > misfireTime)
            {
                continue;
            }

            MisfiredTriggerUpdate update = await PrepareMisfiredTriggerUpdate(conn, trigger, StoredTriggerState.Waiting, calendarCache: null, cancellationToken).ConfigureAwait(false);
            (updates ??= []).Add(update);

            if (update.NewState == StoredTriggerState.Complete)
            {
                (finalized ??= []).Add(trigger);
            }
        }

        if (updates is null)
        {
            return;
        }

        await Delegate.UpdateMisfiredTriggers(conn, updates, cancellationToken).ConfigureAwait(false);

        foreach (IOperableTrigger trigger in finalized ?? [])
        {
            await signaler.NotifySchedulerListenersFinalized(trigger, cancellationToken).ConfigureAwait(false);

            // A trigger with nothing left to fire was just stored COMPLETE, and a COMPLETE row lingers
            // where callers expect the trigger to be gone — GetTrigger would keep handing it back.
            await DeleteTrigger(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
        }
    }

    //---------------------------------------------------------------------------
    // Management methods
    //---------------------------------------------------------------------------
}
