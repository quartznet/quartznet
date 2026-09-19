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

namespace Quartz.Impl.AdoJobStore;

internal abstract partial class AdoJobStoreBase
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
                await Delegate.DeleteFiredTrigger(conn, trigger.FireInstanceId!, cancellationToken).ConfigureAwait(false);
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
    public ValueTask TriggeredJobComplete(IOperableTrigger trigger, IJobDetail jobDetail, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default)
    {
        // The whole of the completion is on the context form below. This one settles no continuation,
        // which is right for the callers it has left: the scheduler thread's "could not dispatch"
        // paths, and anything outside Quartz completing a firing it has no outcome for.
        return FiringComplete(
            new TriggeredJobCompleteContext
            {
                Trigger = trigger,
                JobDetail = jobDetail,
                Instruction = triggerInstructionCode
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask FiringComplete(TriggeredJobCompleteContext context, CancellationToken cancellationToken = default)
    {
        IOperableTrigger trigger = context.Trigger;
        SchedulerInstruction triggerInstructionCode = context.Instruction;

        // Completion bookkeeping belongs to the scheduler, not to the job, and it retries a failing
        // JobPersistenceException until it succeeds. If a job body left an enlistment behind, this
        // would borrow a connection whose transaction is long gone and retry against it forever,
        // leaving the fired trigger uncleaned and its DisallowConcurrentExecution siblings blocked.
        using var suppression = AmbientConnection.Suppress();

        await RetryExecuteInLocalTransactionLock(
            SchedulerLock.TriggerAccess,
            conn => TriggeredJobComplete(conn, context, cancellationToken),
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

    protected ValueTask TriggeredJobComplete(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        IJobDetail jobDetail,
        SchedulerInstruction triggerInstructionCode,
        CancellationToken cancellationToken = default)
    {
        return TriggeredJobComplete(
            conn,
            new TriggeredJobCompleteContext
            {
                Trigger = trigger,
                JobDetail = jobDetail,
                Instruction = triggerInstructionCode
            },
            cancellationToken);
    }

    protected async ValueTask TriggeredJobComplete(
        ConnectionAndTransactionHolder conn,
        TriggeredJobCompleteContext context,
        CancellationToken cancellationToken = default)
    {
        IOperableTrigger trigger = context.Trigger;
        IJobDetail jobDetail = context.JobDetail;
        SchedulerInstruction triggerInstructionCode = context.Instruction;

        await Guarded(
            async () =>
            {
                // The continuations waiting on this trigger, settled inside the completion's own
                // transaction: a crash cannot leave one half-settled, and whichever node completed
                // the parent is the node that promotes them. Before the instruction is applied
                // below, so a completion that deletes the trigger settles by outcome first and the
                // deletion then finds nothing awaiting.
                //
                // A retry settles nothing: the occurrence has attempts left, so how it ends is not
                // known yet. The outcome the run shell reports says the same thing; this says it in
                // the store too, for a caller that reaches the completion another way.
                if (triggerInstructionCode != SchedulerInstruction.RetryTrigger)
                {
                    await SettleContinuations(conn, trigger.Key, context.Outcome, cancellationToken).ConfigureAwait(false);
                }

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
                else if (triggerInstructionCode == SchedulerInstruction.RetryTrigger)
                {
                    // The occurrence failed and the trigger has attempts left, so its next fire time is
                    // the retry instant ExecutionComplete put on it. The row goes back to waiting - or
                    // to paused, if the group is, because a retry waits with everything else in it.
                    //
                    // Written before the DisallowConcurrentExecution unblock below, which transitions
                    // from BLOCKED and PAUSED_BLOCKED and so leaves this row alone now that it holds
                    // the state it is going to wait in.
                    StoredTriggerState retryState = await ApplyPausedGroupState(
                        conn,
                        trigger.Key.Group,
                        trigger.JobKey.Group,
                        StoredTriggerState.Waiting,
                        cancellationToken).ConfigureAwait(false);

                    await Delegate.UpdateTriggerForRetry(conn, trigger, retryState, cancellationToken).ConfigureAwait(false);
                    conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
                }
                else if (!trigger.NextFireTimeUtc.HasValue)
                {
                    // Every instruction that settles the trigger is above, so what reaches here is a
                    // firing that never happened - one a job listener abandoned, or one the scheduler
                    // could not dispatch - completed with no instruction. That settles nothing about the
                    // schedule of a trigger that can fire again, and for those this branch is not taken.
                    // This one cannot fire again: TriggerFired stored it COMPLETE because firing left it
                    // with no fire time, and outside a cluster nothing ever sweeps a COMPLETE row up, so
                    // the trigger is one GetTrigger keeps handing back for good (#3507). The row is
                    // deleted the way the misfire path deletes a trigger it has just stored COMPLETE; the
                    // scheduler listeners have already been told the trigger is finalized, by the run
                    // shell (#3506), so nothing is announced here.
                    StoredTriggerHeader? header = await Delegate.SelectTriggerHeader(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
                    if (header is not null && !header.NextFireTimeUtc.HasValue)
                    {
                        // Read back rather than trusted, exactly as the DeleteTrigger branch above does:
                        // the trigger may have been rescheduled while the firing was in flight, and a
                        // trigger with a fire time ahead of it is nobody's leftover.
                        await DeleteTrigger(conn, trigger.Key, jobDetail, cancellationToken).ConfigureAwait(false);
                    }
                }

                // The occurrence is done with its retries — it succeeded, spent them, or was never going
                // to get one — so the row stops counting. Only when the count was actually non-zero,
                // which is what RetryAttemptCleared says, so an ordinary completion costs no statement.
                if (trigger is TriggerBase completed && completed.RetryAttemptCleared)
                {
                    await Delegate.ClearTriggerRetryAttempt(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
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
            () => Delegate.DeleteFiredTrigger(conn, trigger.FireInstanceId!, cancellationToken),
            "delete fired trigger").ConfigureAwait(false);
    }

    //---------------------------------------------------------------------------
    // Continuations
    //---------------------------------------------------------------------------

    /// <summary>
    /// Settles every trigger awaiting <paramref name="parent" /> against the outcome its firing
    /// reached: released when the outcome is one its condition names, deleted when it is not.
    /// </summary>
    /// <remarks>
    /// Runs on the completion's connection, inside its lock and its transaction. Both statements name
    /// AWAITING, so a row some other path has already moved on is left alone and settlement happens
    /// exactly once.
    /// </remarks>
    private async ValueTask SettleContinuations(
        ConnectionAndTransactionHolder conn,
        TriggerKey parent,
        ExecutionOutcome outcome,
        CancellationToken cancellationToken)
    {
        // NotExecuted satisfies nothing: the occurrence did not happen, so the triggers waiting on it
        // are waiting for a firing that still has to come. No statement is issued at all, which is
        // what keeps the ordinary "could not dispatch" completion as cheap as it was.
        if (Continuation.ConditionFor(outcome) is not { } satisfied)
        {
            return;
        }

        List<AwaitingContinuation> awaiting = await Delegate.SelectAwaitingContinuations(conn, parent, cancellationToken).ConfigureAwait(false);
        if (awaiting.Count == 0)
        {
            return;
        }

        foreach (AwaitingContinuation continuation in awaiting)
        {
            if (continuation.Condition.HasFlag(satisfied))
            {
                await ReleaseContinuation(conn, continuation.Key, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await DiscardContinuation(conn, continuation.Key, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Settles every trigger awaiting a parent that is being deleted.
    /// </summary>
    /// <remarks>
    /// The firing they are waiting for is never going to happen, and no outcome can be reported for
    /// it. A trigger that did not care how it ended is released anyway; anything narrower asked a
    /// question that now has no answer, so it is parked in <see cref="StoredTriggerState.Error" /> for
    /// an operator to see and reset rather than deleted behind their back.
    /// </remarks>
    private async ValueTask SettleContinuationsOfDeletedParent(
        ConnectionAndTransactionHolder conn,
        TriggerKey parent,
        CancellationToken cancellationToken)
    {
        List<AwaitingContinuation> awaiting = await Delegate.SelectAwaitingContinuations(conn, parent, cancellationToken).ConfigureAwait(false);

        foreach (AwaitingContinuation continuation in awaiting)
        {
            if (continuation.Condition == ContinuationCondition.OnAnyOutcome)
            {
                await ReleaseContinuation(conn, continuation.Key, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                Logger.TriggerSetToError(continuation.Key);
                await Delegate.UpdateTriggerState(conn, continuation.Key, StoredTriggerState.Error, cancellationToken).ConfigureAwait(false);
                await signaler.NotifySchedulerListenersTriggerInError(continuation.Key, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Moves one awaiting trigger into the ordinary schedule — or into the paused state, if its group
    /// or its job's group is paused, because the wait ending is not somebody resuming the group.
    /// </summary>
    private async ValueTask ReleaseContinuation(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken)
    {
        StoredTriggerHeader? header = await Delegate.SelectTriggerHeader(conn, triggerKey, cancellationToken).ConfigureAwait(false);
        if (header is null)
        {
            return;
        }

        StoredTriggerState released = await ApplyPausedGroupState(
            conn,
            triggerKey.Group,
            header.JobKey.Group,
            StoredTriggerState.Waiting,
            cancellationToken).ConfigureAwait(false);

        await Delegate.ReleaseContinuation(conn, triggerKey, released, timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
    }

    /// <summary>
    /// Deletes an awaiting trigger whose parent ended in a way its condition did not name, and tells
    /// the scheduler listeners it is finalized — which it is: there is no firing left for it.
    /// </summary>
    private async ValueTask DiscardContinuation(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken)
    {
        IOperableTrigger? discarded = await Delegate.SelectTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false);

        await DeleteTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false);

        if (discarded is not null)
        {
            await signaler.NotifySchedulerListenersFinalized(discarded, cancellationToken).ConfigureAwait(false);
        }
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
