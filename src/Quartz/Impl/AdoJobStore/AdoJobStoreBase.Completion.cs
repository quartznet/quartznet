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
    /// <remarks>
    /// The trigger goes back to <c>WAITING</c> — unless its job disallows concurrent execution and is
    /// executing somewhere in the cluster, in which case it goes to, or stays, <c>BLOCKED</c>, and the
    /// completion of that execution is what lets go of it. A reservation another node's fire found
    /// <c>ACQUIRED</c> is one that fire has already moved to <c>BLOCKED</c>; releasing it to
    /// <c>WAITING</c> undid that, and the row then sat first in the acquisition order while every node
    /// skipped it as executing — a node acquiring one trigger at a time read nothing else and idled
    /// for its whole idle wait behind it (#3926).
    /// </remarks>
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
        return ReleaseAcquiredTrigger(conn, trigger.Key, trigger.JobKey, trigger.FireInstanceId!, cancellationToken);
    }

    /// <summary>
    /// Lets go of one reservation: deletes its fired-trigger row and puts the trigger back where the
    /// job's state says it belongs.
    /// </summary>
    /// <remarks>
    /// The fired row goes first, so that the question asked next — is this job executing? — is about
    /// other firings and never about the reservation being released. The row stays <c>BLOCKED</c>, or
    /// becomes it, only while a fired row of a job that disallows concurrent execution says the job is
    /// running: that execution's completion releases the job's triggers, and <c>ClusterRecover</c> does
    /// when the node running it dies. A <c>BLOCKED</c> row with no such execution behind it is one
    /// nothing else would ever let go of, so it goes back to <c>WAITING</c> as it always has.
    /// </remarks>
    private ValueTask ReleaseAcquiredTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        JobKey jobKey,
        string fireInstanceId,
        CancellationToken cancellationToken)
    {
        return Guarded(
            async () =>
            {
                await Delegate.DeleteFiredTrigger(conn, fireInstanceId, cancellationToken).ConfigureAwait(false);

                StoredTriggerState released = await CheckBlockedState(conn, jobKey, StoredTriggerState.Waiting, cancellationToken).ConfigureAwait(false);
                await Delegate.UpdateTriggerStateFromOtherState(conn, triggerKey, released, StoredTriggerState.Acquired, cancellationToken).ConfigureAwait(false);

                if (released == StoredTriggerState.Waiting)
                {
                    await Delegate.UpdateTriggerStateFromOtherState(conn, triggerKey, StoredTriggerState.Waiting, StoredTriggerState.Blocked, cancellationToken).ConfigureAwait(false);
                }
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

        // A firing that may run beside itself writes only its own rows when it ends, so it takes no
        // lock: its completion commits in parallel with every other worker's and with the loop's fire,
        // where TRIGGER_ACCESS used to queue them one behind the other for a whole fsync'd commit each
        // (#3863). CompletionTakesLock says which completions still need the lock, and the transaction
        // itself asks for it when it finds continuations to settle. Anything that stops the lock-free
        // attempt falls through to the locked path below, which is the retry-forever path it always was.
        bool completed = !CompletionTakesLock(context)
                         && await TryCompleteWithoutLock(context, cancellationToken).ConfigureAwait(false);

        if (!completed)
        {
            await RetryExecuteInLocalTransactionLock(
                SchedulerLock.TriggerAccess,
                conn => TriggeredJobComplete(conn, context, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }

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

    /// <summary>
    /// Whether completing this firing has to hold <see cref="SchedulerLock.TriggerAccess" />, because
    /// something it writes is decided from a read that only the lock keeps still, or is a row that is
    /// not this firing's own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What a completion that takes no lock is left with is a firing that may run beside itself, ending
    /// with <see cref="SchedulerInstruction.NoInstruction" /> or
    /// <see cref="SchedulerInstruction.DeleteTrigger" />: it deletes its fired row by entry id, deletes
    /// its trigger row by key when the trigger is spent, clears a retry count and writes its job's
    /// data. Each is one statement on one row that no other path decides from, and a concurrent editor
    /// of the same trigger key is answered by the row itself — a replace whose <c>UPDATE</c> finds the
    /// row gone inserts, a reschedule whose <c>DELETE</c> finds it gone says so.
    /// </para>
    /// <para>
    /// Everything else stays under the lock, each for a read it would otherwise race:
    /// </para>
    /// <list type="bullet">
    /// <item>SQLite serializes every operation (<see cref="LockAllOperations" />).</item>
    /// <item>A <see cref="DisallowConcurrentExecutionAttribute" /> job's completion unblocks its other
    /// triggers and applies their misfire policy, which reads their states.</item>
    /// <item>A retry, <c>SetTrigger*</c> and <c>SetAllJobTriggers*</c> consult paused-group state or
    /// write rows of other triggers.</item>
    /// <item>A <see cref="OverlapPolicy.BufferOne" /> or <see cref="OverlapPolicy.CancelPrevious" />
    /// firing may let go of its trigger, <c>BLOCKED</c> to <c>WAITING</c>, and then apply the misfire
    /// policy to it with an unconditional write. <c>PauseTrigger</c> reads <c>BLOCKED</c> and writes
    /// <c>PAUSED_BLOCKED</c> unconditionally too, so without the lock one of the two writes is lost.</item>
    /// <item>A <see cref="OverlapPolicy.Skip" /> firing whose next occurrence is the trigger's last owes
    /// the trigger a deletion if that occurrence was skipped while it ran, decided from a read of the
    /// row that a skip under the lock could overtake.</item>
    /// <item>A trigger of a job that is not durable, when this completion deletes it: the job goes with
    /// its last trigger, and "last" is a count of the job's triggers. Two lock-free completions of the
    /// job's last two triggers would each count the other's row and neither would delete the job.</item>
    /// </list>
    /// <para>
    /// A firing with continuations awaiting it is not on this list because the completion cannot know
    /// from what it holds: the lock-free transaction's first statement asks, and escalates when the
    /// answer is not empty.
    /// </para>
    /// </remarks>
    private bool CompletionTakesLock(TriggeredJobCompleteContext context)
    {
        // A hand-back stores a recovery trigger, and decides to from a read of this firing's row that a
        // peer's cluster recovery, which runs under the lock, could otherwise delete in between (#4014).
        if (LockAllOperations || context.JobDetail.ConcurrentExecutionDisallowed || HandsBack(context))
        {
            return true;
        }

        if (context.Instruction is not (SchedulerInstruction.NoInstruction or SchedulerInstruction.DeleteTrigger))
        {
            return true;
        }

        IOperableTrigger trigger = context.Trigger;
        if (trigger.OverlapPolicy is OverlapPolicy.BufferOne or OverlapPolicy.CancelPrevious)
        {
            return true;
        }

        if (MayDeleteTrigger(context))
        {
            return !context.JobDetail.Durable;
        }

        return trigger.OverlapPolicy == OverlapPolicy.Skip
               && trigger.NextFireTimeUtc is { } next
               && trigger.GetFireTimeAfter(next) is null;
    }

    /// <summary>
    /// Whether this completion hands its firing back for recovery rather than settling it: the scheduler
    /// marked it as one a shutdown cancelled, of a job that requests recovery, and this store is set to
    /// recover such firings.
    /// </summary>
    /// <remarks>
    /// The store's own setting is asked again, so a context marked by hand for a store that is not set to
    /// recover completes as the cancellation it is.
    /// </remarks>
    private bool HandsBack(TriggeredJobCompleteContext context)
    {
        return context.HandBackForRecovery
               && RecoverFiringsCancelledByShutdown
               && context.Outcome == ExecutionOutcome.Cancelled;
    }

    /// <summary>
    /// Whether the completion may delete the trigger's row: the instruction says so, or the trigger has
    /// no firing left and the row is the leftover the completion sweeps up.
    /// </summary>
    private static bool MayDeleteTrigger(TriggeredJobCompleteContext context)
    {
        return context.Instruction == SchedulerInstruction.DeleteTrigger || !context.Trigger.NextFireTimeUtc.HasValue;
    }

    /// <summary>
    /// Runs the completion in a transaction of its own with no lock, once.
    /// </summary>
    /// <returns>
    /// <see langword="true" /> when it committed. <see langword="false" /> when the completion has to run
    /// under the lock instead: it found continuations to settle, or it failed — a lost race with an
    /// editor of the same rows, or a database that is gone — and the locked path is where the retries
    /// live.
    /// </returns>
    private async ValueTask<bool> TryCompleteWithoutLock(TriggeredJobCompleteContext context, CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteInLocalTransactionLock(
                lockKind: null,
                conn => TriggeredJobComplete(conn, context, holdsLock: false, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (CompletionNeedsLockException)
        {
            Logger.CompletionEscalatedToLock(context.Trigger.Key);
            return false;
        }
        catch (JobPersistenceException e)
        {
            Logger.CompletionWithoutLockFailed(context.Trigger.Key, e);
            return false;
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

    /// <summary>
    /// The completion's statements, on a unit of work whose caller holds
    /// <see cref="SchedulerLock.TriggerAccess" />.
    /// </summary>
    protected ValueTask TriggeredJobComplete(
        ConnectionAndTransactionHolder conn,
        TriggeredJobCompleteContext context,
        CancellationToken cancellationToken = default)
    {
        return TriggeredJobComplete(conn, context, holdsLock: true, cancellationToken);
    }

    /// <summary>
    /// The completion's statements.
    /// </summary>
    /// <param name="conn">The unit of work.</param>
    /// <param name="context">What the scheduler is telling the store about the firing.</param>
    /// <param name="holdsLock">
    /// Whether the caller holds <see cref="SchedulerLock.TriggerAccess" />. Without it the triggers
    /// awaiting this one are looked for and not settled: any found is a
    /// <see cref="CompletionNeedsLockException" />, thrown before the first write.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    private async ValueTask TriggeredJobComplete(
        ConnectionAndTransactionHolder conn,
        TriggeredJobCompleteContext context,
        bool holdsLock,
        CancellationToken cancellationToken)
    {
        IOperableTrigger trigger = context.Trigger;
        IJobDetail jobDetail = context.JobDetail;
        SchedulerInstruction triggerInstructionCode = context.Instruction;
        bool handBack = HandsBack(context);

        // Whether the trigger's row went, which also says that its fired rows went with it:
        // DeleteTriggerAndChildren sweeps QRTZ_FIRED_TRIGGERS by trigger key, and this firing's row
        // is one of them. Read after the transaction body below, where the delete branches set it.
        bool triggerDeleted = false;

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
                // known yet. That is the instruction's claim, not the outcome's — a job that ran and
                // threw Failed whether or not the trigger asked for another attempt, so the gate
                // names the instruction.
                //
                // Whether the scan happened is kept, because it is what makes the second one — the
                // one the deletion below would otherwise make — a round trip whose answer is already
                // known to be empty. Settling leaves no row AWAITING for this parent: the ones its
                // outcome named have joined the schedule, the rest are gone.
                //
                // Without the lock the scan is the transaction's first statement, and settles nothing:
                // a row found escalates the whole completion to the locked path before anything here
                // has been written. It is skipped when there is nothing it could find that matters —
                // an occurrence that did not happen settles no continuation, and a trigger that stays
                // has no deletion to settle them for — so the count of statements is the locked path's.
                //
                // A firing handed back settles nothing either: it has not ended, it has been put back for
                // recovery to run again, and what awaits the trigger is moved onto the recovery trigger,
                // whose firing's outcome it then waits for. Both happen before an instruction below can
                // delete the trigger, so a job which is not durable still has a trigger when the deletion
                // counts them, and the deletion finds nothing awaiting to park.
                bool continuationsSettled = false;
                if (handBack)
                {
                    await HandBackForRecovery(conn, trigger, cancellationToken).ConfigureAwait(false);
                }
                else if (triggerInstructionCode != SchedulerInstruction.RetryTrigger)
                {
                    if (holdsLock)
                    {
                        continuationsSettled = await SettleContinuations(conn, trigger.Key, context.Outcome, cancellationToken).ConfigureAwait(false);
                    }
                    else if (Continuation.ConditionFor(context.Outcome) is not null || MayDeleteTrigger(context))
                    {
                        continuationsSettled = await RequireNothingAwaiting(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
                    }
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
                            triggerDeleted = await DeleteTrigger(conn, trigger.Key, jobDetail, !continuationsSettled, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        triggerDeleted = await DeleteTrigger(conn, trigger.Key, jobDetail, !continuationsSettled, cancellationToken).ConfigureAwait(false);
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
                        triggerDeleted = await DeleteTrigger(conn, trigger.Key, jobDetail, !continuationsSettled, cancellationToken).ConfigureAwait(false);
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
                else if (!triggerDeleted && trigger.OverlapPolicy is OverlapPolicy.BufferOne or OverlapPolicy.CancelPrevious)
                {
                    // This firing may have held its own trigger back: BufferOne from the moment it
                    // started, CancelPrevious when a firing of it came due on a node that could not
                    // interrupt this one. The firing's policy decides rather than the stored one, which
                    // may have changed since: it is this firing that did the holding.
                    await ReleaseOverlapHold(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
                }
                else if (!triggerDeleted
                         && trigger.OverlapPolicy == OverlapPolicy.Skip
                         && triggerInstructionCode == SchedulerInstruction.NoInstruction
                         && trigger.NextFireTimeUtc is { } next
                         && trigger.GetFireTimeAfter(next) is null)
                {
                    // A Skip trigger whose next firing is its last may have had that firing skipped
                    // while this one ran, which stores it COMPLETE with nothing left. This firing is the
                    // one that owes it the deletion a spent trigger gets; the header says whether it did.
                    StoredTriggerHeader? header = await Delegate.SelectTriggerHeader(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
                    if (header is { State: StoredTriggerState.Complete, NextFireTimeUtc: null })
                    {
                        triggerDeleted = await DeleteTrigger(conn, trigger.Key, jobDetail, !continuationsSettled, cancellationToken).ConfigureAwait(false);
                    }
                }
                // Not for a firing handed back: its replay starts from the data this run started with, as
                // a replay after a crash does, rather than from whatever the run wrote before it was
                // cancelled part-way.
                if (!handBack && jobDetail.PersistJobDataAfterExecution && jobDetail.JobDataMap.Dirty)
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

        // A completion that deleted the trigger has already deleted this row: the deletion sweeps
        // QRTZ_FIRED_TRIGGERS by trigger key, which is a superset of the one entry id, and it did it
        // in this very transaction. Asking again by entry id is a statement that matches nothing.
        if (triggerDeleted)
        {
            return;
        }

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
    /// <returns>
    /// <see langword="true" /> when the awaiting triggers were read and settled, which is also the
    /// answer to "does anything of this parent's still say AWAITING": nothing does. A completion that
    /// goes on to delete the trigger uses that to skip the scan
    /// <see cref="SettleContinuationsOfDeletedParent" /> would make.
    /// </returns>
    private async ValueTask<bool> SettleContinuations(
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
            return false;
        }

        List<AwaitingContinuation> awaiting = await Delegate.SelectAwaitingContinuations(conn, parent, cancellationToken).ConfigureAwait(false);
        if (awaiting.Count == 0)
        {
            return true;
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

        return true;
    }

    /// <summary>
    /// The lock-free completion's first statement: asks whether anything awaits
    /// <paramref name="parent" />, and escalates to the locked path when something does.
    /// </summary>
    /// <remarks>
    /// Settlement moves a trigger into the schedule in a state read from paused groups and running
    /// firings, which is what the lock keeps still; a completion that has not taken it does not settle.
    /// Nothing has been written when this throws, so the rollback that follows undoes nothing and the
    /// locked run starts from the same rows.
    /// </remarks>
    /// <returns>
    /// <see langword="true" />: nothing awaits the parent, so a deletion below need not ask again.
    /// </returns>
    /// <exception cref="CompletionNeedsLockException">A trigger awaits the parent.</exception>
    private async ValueTask<bool> RequireNothingAwaiting(
        ConnectionAndTransactionHolder conn,
        TriggerKey parent,
        CancellationToken cancellationToken)
    {
        List<AwaitingContinuation> awaiting = await Delegate.SelectAwaitingContinuations(conn, parent, cancellationToken).ConfigureAwait(false);
        if (awaiting.Count > 0)
        {
            throw new CompletionNeedsLockException();
        }

        return true;
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
            await SettleContinuationOfMissingParent(conn, continuation.Key, continuation.Condition, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Settles one trigger whose parent has no firing left to report: released when it waited on any
    /// outcome, parked in <see cref="StoredTriggerState.Error" /> otherwise.
    /// </summary>
    /// <returns><see langword="true" /> when the trigger was released, <see langword="false" /> when parked.</returns>
    private async ValueTask<bool> SettleContinuationOfMissingParent(
        ConnectionAndTransactionHolder conn,
        TriggerKey key,
        ContinuationCondition condition,
        CancellationToken cancellationToken)
    {
        if (condition == ContinuationCondition.OnAnyOutcome)
        {
            await ReleaseContinuation(conn, key, cancellationToken).ConfigureAwait(false);
            return true;
        }

        Logger.TriggerSetToError(key);
        await Delegate.UpdateTriggerState(conn, key, StoredTriggerState.Error, cancellationToken).ConfigureAwait(false);

        // Once the transaction has committed, as FiringComplete's own error notifications are: listener
        // code has no business running inside this transaction, or hearing of a parked trigger the
        // rollback of a failed deletion would put back.
        conn.NotifyAfterCommit((notifier, token) => notifier.NotifySchedulerListenersTriggerInError(key, token));
        return false;
    }

    /// <summary>
    /// Settles every <c>AWAITING</c> trigger whose parent no longer exists and is not running anywhere,
    /// as a deletion of that parent would have: released when it waited on any outcome, parked in
    /// <see cref="StoredTriggerState.Error" /> otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two things leave such a row. A continuation added to a running one-off parent by another
    /// thread, committing between the parent's lock-free completion's first statement and its commit
    /// (#3863): the completion did not see it and the parent's row went with the completion. And a
    /// parent that completed on a 4.1 node, which knows nothing of continuations. Under the lock a
    /// completion would have settled the first; nothing settled the second. This runs with the misfire
    /// pass, under <see cref="SchedulerLock.TriggerAccess" />, and asks the one question the settlement
    /// under the lock would have asked of a parent that is gone.
    /// </para>
    /// <para>
    /// The in-memory store needs no sweep: its completion holds the store's monitor from the scan to
    /// the end, so nothing is added to a parent between the two.
    /// </para>
    /// <para>
    /// Only a <see cref="StdAdoDelegate" /> carries the statement. A delegate written outside Quartz that
    /// does not derive from it has no sweep, and its completions settle under the lock as 4.2's did.
    /// </para>
    /// </remarks>
    /// <returns>How many triggers were settled.</returns>
    internal ValueTask<int> SweepStrandedContinuations(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            async () =>
            {
                if (Delegate is not StdAdoDelegate stdDelegate)
                {
                    return 0;
                }

                List<StrandedContinuation> stranded = await stdDelegate.SelectStrandedContinuations(conn, cancellationToken).ConfigureAwait(false);
                if (stranded.Count == 0)
                {
                    return 0;
                }

                int released = 0;
                foreach (StrandedContinuation continuation in stranded)
                {
                    if (await SettleContinuationOfMissingParent(conn, continuation.Key, continuation.Condition, cancellationToken).ConfigureAwait(false))
                    {
                        released++;
                    }
                }

                Logger.StrandedContinuationsSettled(stranded.Count, released, stranded.Count - released);
                return stranded.Count;
            },
            "settle continuations whose parent no longer exists");
    }

    /// <summary>
    /// Whether <see cref="SweepStrandedContinuations" /> would find anything: the same statement,
    /// asked without the lock so that a pass with nothing to settle takes none.
    /// </summary>
    private async ValueTask<bool> HasStrandedContinuations(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken)
    {
        if (Delegate is not StdAdoDelegate stdDelegate)
        {
            return false;
        }

        List<StrandedContinuation> stranded = await Guarded(
            () => stdDelegate.SelectStrandedContinuations(conn, cancellationToken),
            "look for continuations whose parent no longer exists").ConfigureAwait(false);

        return stranded.Count > 0;
    }

    /// <summary>
    /// Moves one awaiting trigger into the ordinary schedule, in the state a trigger stored at this
    /// moment would get: paused if its group or its job's group is, because the wait ending is not
    /// somebody resuming the group, and blocked if its job disallows concurrent execution and is
    /// running, for that execution's completion to let go like any other trigger of the job.
    /// </summary>
    /// <remarks>
    /// It fires at the later of now and its start time, moved on to its calendar's next included
    /// instant when the calendar excludes that one — <see cref="Continuation.ReleaseFireTime" />, the
    /// rule the in-memory store applies too. A trigger whose end time is behind that instant has no
    /// firing left, and is discarded instead.
    /// </remarks>
    private async ValueTask ReleaseContinuation(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken)
    {
        // The whole trigger rather than its header: a release needs the start time, the end time and
        // the calendar as well as the job, and the heavier read is paid only by a completion that has
        // something waiting on it.
        IOperableTrigger? trigger = await Delegate.SelectTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false);
        if (trigger is null)
        {
            return;
        }

        ICalendar? calendar = trigger.CalendarName is { } calendarName
            ? await GetCalendar(conn, calendarName, cancellationToken).ConfigureAwait(false)
            : null;

        if (Continuation.ReleaseFireTime(trigger, calendar, timeProvider.GetUtcNow()) is not { } fireTime)
        {
            await DiscardContinuation(conn, trigger, cancellationToken).ConfigureAwait(false);
            return;
        }

        StoredTriggerState released = await ApplyPausedGroupState(
            conn,
            triggerKey.Group,
            trigger.JobKey.Group,
            StoredTriggerState.Waiting,
            cancellationToken).ConfigureAwait(false);

        // What AddTrigger asks of a job that disallows concurrent execution, asked without loading the
        // job: the fired-trigger row of a running execution says whether its job is one of those, so a
        // job that allows concurrency answers "not blocked" from the same read. When the continuation's
        // job is the parent's own, the parent's row is still here and this answers BLOCKED — which the
        // unblock later in this same completion turns into WAITING, as it does for every trigger of the
        // job that has just finished.
        released = await CheckBlockedState(conn, trigger.JobKey, released, cancellationToken).ConfigureAwait(false);

        await Delegate.ReleaseContinuation(conn, triggerKey, released, fireTime, cancellationToken).ConfigureAwait(false);
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
        // No row is nothing to discard: the key came from the statement that found it awaiting, in
        // this transaction, so there is no path by which it went missing that left anything behind.
        IOperableTrigger? discarded = await Delegate.SelectTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false);
        if (discarded is not null)
        {
            await DiscardContinuation(conn, discarded, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Deletes an awaiting trigger that has no firing left — its parent ended in a way its condition
    /// did not name, or its end time is behind the instant a release would have fired it at — and
    /// tells the scheduler listeners it is finalized.
    /// </summary>
    /// <remarks>
    /// The triggers waiting on this one are discarded with it, and theirs with them: this trigger never
    /// runs, so no outcome any of them waits for can happen. That is what separates a discard from a
    /// deletion, whose dependants <see cref="SettleContinuationsOfDeletedParent" /> parks or releases —
    /// a parent somebody removed is a question an operator has to answer, and this is not. The row goes
    /// before its dependants are looked for, so a chain that loops back on itself ends rather than
    /// coming round again.
    /// </remarks>
    private async ValueTask DiscardContinuation(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger discarded,
        CancellationToken cancellationToken)
    {
        await DeleteTrigger(conn, discarded.Key, job: null, settleContinuations: false, cancellationToken).ConfigureAwait(false);

        // Once the completion has committed, not from inside its transaction and lock — the rule
        // FiringComplete's own notifications follow.
        conn.NotifyAfterCommit((notifier, token) => notifier.NotifySchedulerListenersFinalized(discarded, token));

        List<AwaitingContinuation> dependants = await Delegate.SelectAwaitingContinuations(conn, discarded.Key, cancellationToken).ConfigureAwait(false);
        foreach (AwaitingContinuation dependant in dependants)
        {
            await DiscardContinuation(conn, dependant.Key, cancellationToken).ConfigureAwait(false);
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

        await RecoverReleasedMisfires(conn, waiting, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies the misfire policy of every trigger in <paramref name="waiting" /> whose fire time went
    /// past the misfire threshold while it was held back, and deletes the ones left with nothing to fire.
    /// A policy that throws fails its own trigger and none of the rest.
    /// </summary>
    /// <param name="conn">The DB connection.</param>
    /// <param name="waiting">Triggers just put back to <c>WAITING</c>.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    private async ValueTask RecoverReleasedMisfires(
        ConnectionAndTransactionHolder conn,
        List<TriggerKey> waiting,
        CancellationToken cancellationToken)
    {
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

            // A policy that throws fails this trigger alone: it stays WAITING with its fire time, for the
            // misfire handler, or is stored ERROR at the limit, and the completion commits (#4006). Before,
            // the throw rolled the completion back, and the retry met it again for good.
            PreparedMisfire prepared = await PrepareMisfiredTriggerUpdate(conn, trigger, StoredTriggerState.Waiting, calendarCache: null, cancellationToken).ConfigureAwait(false);
            if (prepared.Update is not { } update)
            {
                continue;
            }

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
            // After the completion commits, with the continuation notifications above and for the
            // same reason: this runs inside the completion's transaction and lock.
            conn.NotifyAfterCommit((notifier, token) => notifier.NotifySchedulerListenersFinalized(trigger, token));

            // A trigger with nothing left to fire was just stored COMPLETE, and a COMPLETE row lingers
            // where callers expect the trigger to be gone — GetTrigger would keep handing it back.
            await DeleteTrigger(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
        }
    }

    //---------------------------------------------------------------------------
    // Management methods
    //---------------------------------------------------------------------------
}
