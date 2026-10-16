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
using System.Diagnostics;

using Quartz.Extensibility;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Acquiring the next triggers and firing the ones already due in one transaction (#3864).
/// </summary>
/// <remarks>
/// <para>
/// A round of already-due triggers used to be two transactions under <c>TRIGGER_ACCESS</c>: the
/// acquisition, which reserved each trigger with an <c>ACQUIRED</c> fired-trigger row, and the fire, which
/// read each trigger's header and job again and updated the reservation to <c>EXECUTING</c>. Here the
/// round is one: the triggers are claimed as acquisition claims them, their jobs are read once for the
/// round, and each fire inserts its row as <c>EXECUTING</c>. On PostgreSQL that is a lock, a commit and
/// two reads per trigger off the path every firing waits on. Through a delegate Quartz ships the round
/// reads nothing per trigger at all (#4066): the candidate read keeps each row's discriminator, one read
/// says which jobs of the round are executing, and one read holds the paused groups for every fire.
/// </para>
/// <para>
/// The lock is <see cref="SchedulerLock.TriggerAccess" />, and since 4.6 a round takes it only when
/// something in it needs it (#4067). A round decides from its candidate read: a job that disallows
/// concurrent execution, an overlap policy the fire decides from the trigger's running firings, a
/// cluster-scoped execution limit, an isolation level above read committed, a live peer older than 4.6, or
/// the option being off all send it to the locked path, which is the round as it was. Otherwise the round
/// runs in a transaction with no lock: the claim is a compare-and-swap on each row's fire time, which also
/// leaves the row locked to this transaction until it commits, so every write the fire makes lands on a
/// row nothing else can move in between. Such a round claims first and reads the claimed rows after, so
/// what it fires is the row as the claim holds it; a job or trigger found changed by that read rolls the
/// round back and runs it again under the lock. <see cref="RoundLockReason" /> is the list.
/// </para>
/// <para>
/// What the lock used to keep still for a concurrent-allowed fire — other writers re-arming a row the
/// round had just fired — the writers themselves now keep off: the misfire handler and the trigger
/// editors hold their writes to the row as they read it. With <see cref="LockFreeFiringRounds" /> off,
/// a round that acquires one trigger at a time without <see cref="AcquireTriggersWithinLock" /> is two
/// transactions, as it was in 4.5, and every other round is locked throughout.
/// </para>
/// <para>
/// The round reads and inserts through the delegate's round members, which stand in for its single-trigger
/// ones, only for a delegate that says it supports them (<see cref="IDriverDelegate.SupportsFireOnAcquire" />):
/// the ones Quartz ships. Any other delegate's round is still one transaction, but it claims, reserves, reads
/// and fires each trigger through the members acquiring and then firing it called, so that no override of
/// them is bypassed.
/// </para>
/// </remarks>
internal abstract partial class AdoJobStoreBase
{
    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// A fire that fails for a reason a retry will not cure rolls the round back, acquisition and all, and
    /// the round runs again, as a <see cref="TriggersFired" /> batch does (#3931). In the runs after it the
    /// failed trigger is claimed and reserved as acquisition reserves any trigger, but not fired: it is
    /// answered <see cref="TriggerFiredResult.Failed" />, which the scheduler releases, and it is counted
    /// against <see cref="MaxConsecutiveFireFailures" /> and stored <c>ERROR</c> at the limit exactly as a
    /// failed fire of an acquired trigger is (#3963). Nothing the round claimed is left <c>ACQUIRED</c>
    /// without a fired-trigger row: a due trigger that does not fire after all is reserved like the rest.
    /// </para>
    /// </remarks>
    public async ValueTask<TriggerAcquisitionResult> AcquireNextTriggersAndFireDue(
        TriggerAcquisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!LockFreeFiringRounds && !AcquireTriggersWithinLock && request.MaxCount <= 1)
        {
            // With lock-free rounds off, 4.5's shape exactly: acquired without the lock, as it always was,
            // and fired by the scheduler's TriggersFired under the lock in a transaction of its own. With
            // them on, a round of one is a round like any other below, and takes no lock when nothing in
            // it needs one (#4067).
            AcquiredTriggers acquired = await AcquireNextTriggersWithoutFiring(request, cancellationToken).ConfigureAwait(false);
            return new TriggerAcquisitionResult
            {
                Pending = acquired.Triggers,
                Blocked = acquired.Blocked,
                LatestBlockingFiredUtc = acquired.LatestBlockingFiredUtc,
                GroupsAtLimit = acquired.GroupsAtLimit,

                // The round — this acquisition and the fire the scheduler makes of it — runs under the
                // lock, which TriggersFired takes; the tag describes the round, as it does below.
                LockName = SchedulerLock.TriggerAccess.ToLockName(),
                LockReason = RoundLockReason.OptionOff.ToTag(),
            };
        }

        // The triggers whose fire failed in an attempt of this call, with why. Each is claimed but not
        // fired by the attempts after it. Nothing is allocated until a fire fails.
        Dictionary<TriggerKey, Exception>? failed = null;

        // The failed triggers whose failure was the last one in a row the store allows, with how many.
        List<(TriggerKey TriggerKey, int Failures)>? failing = null;

        // Whether the round claims and writes one trigger at a time rather than together: once a batch has
        // failed without saying which trigger it failed on, which the one-at-a-time run says.
        bool oneByOne = false;

        // Why this round takes TRIGGER_ACCESS, or null for a round that takes none (#4067). Decided here
        // from what is known before the first read; an attempt that finds a candidate only the lock
        // decides rolls back and the round runs again with the reason it found.
        RoundLockReason? lockReason = RoundLockReasonBeforeRead(request);

        while (true)
        {
            FireOnAcquireAttempt attempt;
            FireOnAcquireRound round = new(failed, oneByOne, Delegate.SupportsFireOnAcquire, lockReason);
            try
            {
                attempt = await ExecuteInLocalTransactionLock(
                    lockReason is null ? null : SchedulerLock.TriggerAccess,
                    conn => AcquireAndFireDue(conn, request, round, cancellationToken),
                    (conn, result) => ValidateAcquiredAndFired(conn, result, cancellationToken),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (RoundNeedsLockException needsLock)
            {
                // Nothing of the attempt stands. The round runs again under the lock, and so does the
                // next one: a schedule whose serial job is first in the order would otherwise escalate
                // every round, and a locked round that finds nothing needing the lock lets the one after
                // it start without.
                if (needsLock.Reason == RoundLockReason.DefinitionChanged)
                {
                    Logger.RoundRolledBackForLock(needsLock.TriggerKey);
                }
                else
                {
                    Logger.RoundEscalatedToLock(needsLock.Reason.Describe(), needsLock.TriggerKey);
                }

                lockReason = needsLock.Reason;
                Volatile.Write(ref nextRoundStartsLocked, true);
                continue;
            }
            catch (TriggerWriteFailedException batch)
            {
                Logger.RoundBatchWriteFailed(batch);
                oneByOne = true;
                continue;
            }
            catch (ClaimOutcomeUnknownException unknown)
            {
                Logger.RoundBatchClaimUnknown(unknown);
                oneByOne = true;
                continue;
            }
            catch (TriggerFireFailedException failure)
            {
                Exception cause = failure.InnerException!;
                if (cause is JobPersistenceException jpe)
                {
                    Logger.JobPersistenceExceptionCaught(jpe.Message, jpe);
                }
                else
                {
                    Logger.ExceptionCaught(cause.Message, cause);
                }

                (failed ??= [])[failure.TriggerKey] = cause;

                // Counted with the previous fire time it was acquired with, which only a fire that
                // committed moves.
                if (MaxConsecutiveFireFailures > 0)
                {
                    int failures = fireFailures.RecordFailure(failure.TriggerKey, failure.PreviousFireTimeUtc);
                    if (failures >= MaxConsecutiveFireFailures)
                    {
                        (failing ??= []).Add((failure.TriggerKey, failures));
                    }
                }

                Logger.FireBatchRolledBack(failure.TriggerKey, failure.BatchSize, failure.BatchSize - 1);
                continue;
            }

            if (lockReason == RoundLockReason.Hysteresis && !round.CandidateNeededLock)
            {
                // Locked for the previous round's sake, and nothing in this one needed it.
                Volatile.Write(ref nextRoundStartsLocked, false);
            }

            List<TriggerFiredResult> fired = new(attempt.Due.Count);
            for (int i = 0; i < attempt.Due.Count; i++)
            {
                fired.Add(attempt.Fired[i] ?? TriggerFiredResult.Failed(failed![attempt.Due[i].Key]));
            }

            ForgetFireFailures(attempt.Due, attempt.Fired);

            if (failing is not null)
            {
                // Only a trigger the committed attempt holds ACQUIRED can be stored ERROR from it. One that
                // attempt did not take again — another node claimed it meanwhile — keeps its count, and its
                // next failure here parks it.
                failing.RemoveAll(entry => !attempt.Due.Exists(trigger => trigger.Key.Equals(entry.TriggerKey)));
                if (failing.Count > 0)
                {
                    await ParkFailingTriggers(failing, cancellationToken).ConfigureAwait(false);
                }
            }

            return new TriggerAcquisitionResult
            {
                Due = attempt.Due,
                Fired = fired,
                Pending = attempt.Pending,
                Blocked = attempt.Acquired.Blocked,
                LatestBlockingFiredUtc = attempt.Acquired.LatestBlockingFiredUtc,
                GroupsAtLimit = attempt.Acquired.GroupsAtLimit,
                LockName = lockReason is null ? "none" : SchedulerLock.TriggerAccess.ToLockName(),
                LockReason = lockReason?.ToTag(),
            };
        }
    }

    /// <summary>
    /// Whether the next firing round starts under <see cref="SchedulerLock.TriggerAccess" /> because the
    /// one before it had to take it (#4067). Set by an escalation, cleared by a round that held the lock
    /// for this reason alone and found nothing in its candidates that needed it.
    /// </summary>
    private bool nextRoundStartsLocked;

    /// <summary>
    /// Why a round takes <see cref="SchedulerLock.TriggerAccess" /> before anything has been read, or
    /// <see langword="null" /> for a round that starts without it and decides the rest from its candidates
    /// (#4067). In the order the rule asks: the store's configuration, the cluster's peers, the request's
    /// cluster-scoped limit, and the round before.
    /// </summary>
    private RoundLockReason? RoundLockReasonBeforeRead(TriggerAcquisitionRequest request)
    {
        if (StaticRoundLockReason is { } configured)
        {
            return configured;
        }

        if (!PeersAllowLockFreeRounds)
        {
            return RoundLockReason.OlderPeer;
        }

        // The quota's count is read under the lock, and the docs promise each node adds at most one.
        if (request.ExecutionLimits?.HasClusterScopedLimits == true)
        {
            return RoundLockReason.ClusterLimit;
        }

        if (Volatile.Read(ref nextRoundStartsLocked))
        {
            return RoundLockReason.Hysteresis;
        }

        return null;
    }

    /// <summary>
    /// The first candidate of a read that only the lock decides, with why, or <see langword="null" /> when
    /// every candidate may be claimed and fired without it (#4067).
    /// </summary>
    /// <remarks>
    /// A job that disallows concurrent execution is decided against the fired-trigger table, a
    /// <see cref="OverlapPolicy.Skip" />, <see cref="OverlapPolicy.BufferOne" /> or
    /// <see cref="OverlapPolicy.CancelPrevious" /> trigger against its running firings, and both reads
    /// are kept still only by the lock. A candidate whose delegate did not read the flag, the policy or
    /// the fire time gives no answer, and no answer is "lock".
    /// </remarks>
    private static (RoundLockReason Reason, TriggerKey TriggerKey)? FirstCandidateNeedingLock(List<TriggerAcquireResult> results)
    {
        foreach (TriggerAcquireResult candidate in results)
        {
            if (candidate.ConcurrentExecutionDisallowed is null || candidate.OverlapPolicy is null || candidate.NextFireTimeUtc is null)
            {
                return (RoundLockReason.Delegate, candidate.TriggerKey);
            }

            if (candidate.ConcurrentExecutionDisallowed == true)
            {
                return (RoundLockReason.SerialJob, candidate.TriggerKey);
            }

            if (candidate.OverlapPolicy is not (OverlapPolicy.Default or OverlapPolicy.AllowAll))
            {
                return (RoundLockReason.OverlapPolicy, candidate.TriggerKey);
            }
        }

        return null;
    }

    /// <summary>
    /// The half of an acquisition pass a round without the lock makes once its candidates are known to
    /// need none (#4067): claim first, read the claimed rows after.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The locked pass reads each candidate's row in full, decides, and claims. Without the lock that
    /// order has a hole: a trigger replaced between the read and the claim by one that happens to be due
    /// at the same instant is claimed by the compare-and-swap and fired as the old definition, and the
    /// fire's write then overwrites the new one. Claiming first closes it — the claim holds the row until
    /// the round commits, so the read after it is the row the fire writes over — and costs no statement
    /// the locked pass does not make: the candidate read carries the fire time the claim needs.
    /// </para>
    /// <para>
    /// A claim that moved no row is a race lost to another node, as in the locked pass. A job type that
    /// will not load is stored <c>ERROR</c> from <c>WAITING</c>, before the claim and only while the row is
    /// still the candidate's. Nothing here asks the fired-trigger table or the paused groups: no candidate
    /// of this pass is a serial job's or has a policy the fire decides, and a concurrent-allowed fire
    /// writes <c>WAITING</c> or <c>COMPLETE</c> unconditionally — a pause that commits after the candidate
    /// read lands on the row the fire leaves, which is the serialised order, round then pause.
    /// </para>
    /// </remarks>
    /// <param name="conn">The round's transaction.</param>
    /// <param name="request">What the scheduler asked for.</param>
    /// <param name="round">The round, which the reads record their findings on.</param>
    /// <param name="results">The candidates, in fire-time order, with the exclusions and the refused rows dropped.</param>
    /// <param name="maxCount">The most the pass may claim.</param>
    /// <param name="acquiredTriggers">Where the claimed triggers go, in candidate order.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    private async ValueTask<LockFreePass> ClaimAndReadWithoutLock(
        ConnectionAndTransactionHolder conn,
        TriggerAcquisitionRequest request,
        FireOnAcquireRound round,
        List<TriggerAcquireResult> results,
        int maxCount,
        List<IOperableTrigger> acquiredTriggers,
        CancellationToken cancellationToken)
    {
        int raced = 0;
        bool reachedBatchEnd = false;
        DateTimeOffset batchEnd = request.NoLaterThan;
        List<TriggerClaim> planned = new(Math.Min(results.Count, maxCount));

        foreach (TriggerAcquireResult candidate in results)
        {
            if (planned.Count >= maxCount)
            {
                break;
            }

            // Resolved for the reason the locked pass resolves it: a type that will not load is settled
            // here, before the claim, rather than met by every round for good.
            if (!(await ResolveCandidateJobType(conn, candidate, cancellationToken).ConfigureAwait(false)).Loaded)
            {
                raced++;
                continue;
            }

            // The rule refused a read that did not say, so this is the row's fire time as the candidate
            // read found it, and what the claim holds the row to.
            DateTimeOffset nextFireTimeUtc = candidate.NextFireTimeUtc!.Value;
            if (nextFireTimeUtc > batchEnd)
            {
                // Everything behind this row is due later still, so there is nothing to read past to.
                reachedBatchEnd = true;
                break;
            }

            if (planned.Count == 0)
            {
                DateTimeOffset now = timeProvider.GetUtcNow();
                batchEnd = (now > nextFireTimeUtc ? now : nextFireTimeUtc) + request.TimeWindow;
            }

            planned.Add(new TriggerClaim { TriggerKey = candidate.TriggerKey, NextFireTimeUtc = nextFireTimeUtc });
        }

        if (planned.Count == 0)
        {
            return new LockFreePass(raced, reachedBatchEnd);
        }

        // The claim: WAITING at the fire time read, to ACQUIRED. The second of two nodes reaching for a
        // row waits on the first's row lock, re-evaluates against the committed row — WAITING again, at
        // the next fire time — and moves nothing, which every dialect Quartz ships does at read committed.
        List<TriggerKey> claimed = round.ClaimsTogether
            ? await Delegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(conn, planned, StoredTriggerState.Acquired, StoredTriggerState.Waiting, cancellationToken).ConfigureAwait(false)
            : await ClaimOneAtATime(conn, planned, cancellationToken).ConfigureAwait(false);

        raced += planned.Count - claimed.Count;
        if (claimed.Count == 0)
        {
            return new LockFreePass(raced, reachedBatchEnd);
        }

        // The claimed rows, read now that this transaction holds them: the trigger the fire advances, and
        // the discriminator its write compares the type with, current by construction.
        HashSet<TriggerKey> took = [.. claimed];
        Dictionary<TriggerKey, IOperableTrigger> rows = await ReadAcquisitionCandidates(
            conn,
            results.FindAll(candidate => took.Contains(candidate.TriggerKey)),
            round,
            cancellationToken).ConfigureAwait(false);

        List<IOperableTrigger>? firedTriggerRows = null;
        foreach (TriggerClaim claim in planned)
        {
            if (!took.Contains(claim.TriggerKey))
            {
                continue;
            }

            if (!rows.TryGetValue(claim.TriggerKey, out IOperableTrigger? trigger))
            {
                // The claim moved the row and this transaction holds it until it commits, so it is there
                // to read. The attempt rolls back rather than leave a claim with no row behind it.
                throw new JobPersistenceException($"Trigger '{claim.TriggerKey}' was claimed but could not be read back in the same transaction.");
            }

            trigger.FireInstanceId = GetFiredTriggerRecordId();

            // A trigger this transaction fires has its row written by the fire; one due later in the
            // window is reserved, as acquisition has always reserved it.
            if (!round.FiresWithoutReservation(trigger.Key, claim.NextFireTimeUtc, timeProvider))
            {
                (firedTriggerRows ??= []).Add(trigger);
            }

            acquiredTriggers.Add(trigger);
        }

        if (firedTriggerRows is not null)
        {
            await Delegate.InsertFiredTriggers(conn, firedTriggerRows, StoredTriggerState.Acquired, null, cancellationToken).ConfigureAwait(false);
        }

        return new LockFreePass(raced, reachedBatchEnd);
    }

    /// <summary>
    /// The claims of a lock-free pass one statement at a time, for a round that has stopped batching.
    /// </summary>
    private async ValueTask<List<TriggerKey>> ClaimOneAtATime(
        ConnectionAndTransactionHolder conn,
        List<TriggerClaim> planned,
        CancellationToken cancellationToken)
    {
        List<TriggerKey> claimed = new(planned.Count);
        foreach (TriggerClaim claim in planned)
        {
            int rowsUpdated = await Delegate.UpdateTriggerStateFromOtherStateWithNextFireTime(
                conn,
                claim.TriggerKey,
                StoredTriggerState.Acquired,
                StoredTriggerState.Waiting,
                claim.NextFireTimeUtc,
                cancellationToken).ConfigureAwait(false);

            if (rowsUpdated > 0)
            {
                claimed.Add(claim.TriggerKey);
            }
        }

        return claimed;
    }

    /// <summary>
    /// What a lock-free pass found beside what it claimed: how many candidates another node took first,
    /// and whether it stopped at the end of the batch window rather than at the end of the read.
    /// </summary>
    private readonly record struct LockFreePass(int Raced, bool ReachedBatchEnd);

    /// <summary>
    /// One attempt at a round: the acquisition, then the fire of every due trigger it claimed that has not
    /// failed in an earlier attempt, in the attempt's transaction.
    /// </summary>
    /// <exception cref="TriggerFireFailedException">
    /// A fire failed for a reason a retry will not cure. The transaction wrapper rolls the attempt back,
    /// and <see cref="AcquireNextTriggersAndFireDue" /> runs the round again with the trigger claimed but
    /// not fired.
    /// </exception>
    private async ValueTask<FireOnAcquireAttempt> AcquireAndFireDue(
        ConnectionAndTransactionHolder conn,
        TriggerAcquisitionRequest request,
        FireOnAcquireRound round,
        CancellationToken cancellationToken)
    {
        // Every attempt decides for itself, from the clock when it claims its first trigger.
        round.Reset();
        AcquiredTriggers acquired = await AcquireNextTrigger(conn, request, round, round.HoldsLock, cancellationToken).ConfigureAwait(false);

        List<IOperableTrigger> due = [];
        List<IOperableTrigger> pending = [];
        foreach (IOperableTrigger trigger in acquired.Triggers)
        {
            // Acquisition read the clock when it claimed the first of them, so every one was claimed with
            // the same answer to "is it due".
            if (round.IsDue(trigger.NextFireTimeUtc!.Value))
            {
                due.Add(trigger);
            }
            else
            {
                pending.Add(trigger);
            }
        }

        TriggerFiredResult?[] fired = due.Count > 0
            ? await FireDue(conn, due, round, cancellationToken).ConfigureAwait(false)
            : [];

        return new FireOnAcquireAttempt(due, fired, pending, acquired);
    }

    /// <summary>
    /// Fires the due triggers of a round in its transaction, having read their headers and jobs once for
    /// all of them, and writes every fire's rows together once all of them are decided.
    /// </summary>
    /// <returns>
    /// A result for each trigger, index-aligned with <paramref name="due" />; <see langword="null" /> for
    /// one that failed in an earlier attempt, which acquisition reserved and this does not fire.
    /// </returns>
    /// <exception cref="TriggerFireFailedException">
    /// One fire's writes failed for a reason a retry will not cure.
    /// </exception>
    /// <exception cref="TriggerWriteFailedException">
    /// The fires' writes failed as one batch, which does not say whose; the round runs again one trigger at
    /// a time.
    /// </exception>
    private async ValueTask<TriggerFiredResult?[]> FireDue(
        ConnectionAndTransactionHolder conn,
        List<IOperableTrigger> due,
        FireOnAcquireRound round,
        CancellationToken cancellationToken)
    {
        Dictionary<TriggerKey, Exception>? failed = round.Failed;
        List<TriggerKey> triggerKeys = new(due.Count);
        List<JobKey> jobKeys = new(due.Count);
        foreach (IOperableTrigger trigger in due)
        {
            if (failed is null || !failed.ContainsKey(trigger.Key))
            {
                triggerKeys.Add(trigger.Key);
                jobKeys.Add(trigger.JobKey);
            }
        }

        TriggerFiredResult?[] results = new TriggerFiredResult?[due.Count];
        if (triggerKeys.Count == 0)
        {
            return results;
        }

        // Null for a round through the single-trigger members, each of whose fires reads its own header and
        // job, and updates the reservation acquisition wrote for it.
        FireOnAcquirePrefetch? prefetch = null;
        if (round.ReadsTogether)
        {
            // The state and type discriminator of each. The state is ACQUIRED — this transaction has just
            // claimed them — and the discriminator is what the fire's write compares the trigger's
            // current type with. A shipped delegate's read of the candidates kept the discriminator beside
            // each trigger it built, so there is nothing to go back for (#4066); any other delegate's round
            // reads the headers in one statement, as it did. Both stand in either form of the round
            // (#4067): under the lock, because every reschedule takes it and nothing can replace a row
            // between the read and the claim; without it, because the read came after the claim, on a
            // row this transaction holds.
            Dictionary<TriggerKey, StoredTriggerIdentity> stored = new(triggerKeys.Count);
            if (round.StoredTypes is { } storedTypes)
            {
                foreach (TriggerKey triggerKey in triggerKeys)
                {
                    if (storedTypes.TryGetValue(triggerKey, out string? storedType))
                    {
                        stored[triggerKey] = new StoredTriggerIdentity(StoredTriggerState.Acquired, storedType);
                    }
                }
            }
            else
            {
                List<StoredTriggerHeader> headers = await Guarded(
                    () => Delegate.SelectStoredTriggerHeaders(conn, triggerKeys, cancellationToken),
                    "select trigger states").ConfigureAwait(false);

                foreach (StoredTriggerHeader header in headers)
                {
                    stored[header.Key] = new StoredTriggerIdentity(header.State, header.TriggerType);
                }
            }

            prefetch = new FireOnAcquirePrefetch(
                stored,
                await ReadJobsToFire(conn, jobKeys, cancellationToken).ConfigureAwait(false),
                round.IdleJobs,
                ShippedDelegate,
                round.HoldsLock);
        }

        // The due triggers that did not fire after all, left ACQUIRED with no row: reserved below, as
        // acquisition reserves the triggers it does not fire, for the scheduler to release. A round that
        // reserved every trigger as it claimed it leaves none.
        List<IOperableTrigger>? unfired = null;

        // The writes of every fire, applied together once all of them are decided, with the position in
        // the round of the trigger each belongs to. Null when the round writes each fire as it decides it.
        List<TriggerFiredUpdate>? writes = round.WritesTogether ? new(triggerKeys.Count) : null;
        List<int>? writers = round.WritesTogether ? new(triggerKeys.Count) : null;

        for (int i = 0; i < due.Count; i++)
        {
            IOperableTrigger trigger = due[i];
            if (failed is not null && failed.ContainsKey(trigger.Key))
            {
                continue;
            }

            TriggerFiredResult result;
            try
            {
                // A copy, so that Triggered() does not move the acquired trigger on: a rolled-back attempt
                // leaves it as it was, and the scheduler releases the acquired instance.
                IOperableTrigger triggerCopy = (IOperableTrigger) trigger.Clone();
                int written = writes?.Count ?? 0;
                result = await FireTrigger(conn, triggerCopy, prefetch, writes, cancellationToken).ConfigureAwait(false);
                if (writes is not null && writes.Count > written)
                {
                    writers!.Add(i);
                }
            }
            catch (RoundNeedsLockException)
            {
                // Not a failed fire: the round runs again under the lock, and no trigger is blamed.
                throw;
            }
            catch (JobPersistenceException jpe)
            {
                if (IsTransient(jpe))
                {
                    throw; // the transaction wrapper retries the whole attempt
                }

                throw FireFailed(i, trigger, triggerKeys.Count, jpe);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A cancellation is not caught: it is the caller asking to stop, which the transaction
                // wrapper reports as itself rather than as a trigger that failed to fire.
                if (IsTransient(ex))
                {
                    throw new JobPersistenceException("Transient error firing trigger: " + ex.Message, ex);
                }

                throw FireFailed(i, trigger, triggerKeys.Count, ex);
            }

            if (result.TriggerFiredBundle is null && !result.IsDeclined && result.Exception is null && !round.ReservesEveryTrigger)
            {
                (unfired ??= []).Add(trigger);
            }

            results[i] = result;
        }

        if (writes is { Count: > 0 })
        {
            await ApplyWrites(conn, due, writes, writers!, triggerKeys.Count, cancellationToken).ConfigureAwait(false);
        }

        if (unfired is not null)
        {
            await Guarded(
                () => Delegate.InsertFiredTriggers(conn, unfired, StoredTriggerState.Acquired, null, cancellationToken),
                "reserve the triggers that did not fire").ConfigureAwait(false);
        }

        return results;
    }

    private static TriggerFireFailedException FireFailed(int index, IOperableTrigger trigger, int batchSize, Exception failure)
    {
        return new TriggerFireFailedException(index, trigger.Key, failure)
        {
            PreviousFireTimeUtc = trigger.PreviousFireTimeUtc,
            BatchSize = batchSize,
        };
    }

    /// <summary>
    /// Applies the writes of a round's fires together, and says whose failed when that is known.
    /// </summary>
    /// <param name="conn">The round's transaction.</param>
    /// <param name="due">The round's due triggers.</param>
    /// <param name="writes">The writes of each fire, in the order the fires were decided.</param>
    /// <param name="writers">The position in <paramref name="due" /> of the trigger each write belongs to.</param>
    /// <param name="batchSize">How many triggers the round is firing, for the log line of a failure.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    private async ValueTask ApplyWrites(
        ConnectionAndTransactionHolder conn,
        List<IOperableTrigger> due,
        List<TriggerFiredUpdate> writes,
        List<int> writers,
        int batchSize,
        CancellationToken cancellationToken)
    {
        try
        {
            await Delegate.ApplyTriggersFired(conn, writes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Exception cause = e is TriggerWriteFailedException { InnerException: { } inner } ? inner : e;
            if (IsTransient(cause))
            {
                throw new JobPersistenceException("Transient error firing triggers: " + cause.Message, cause);
            }

            // One fire's writes failed, and which one is known: answered as the fire of an acquired trigger
            // is (#3931), by running the round again without firing that trigger.
            if (e is TriggerWriteFailedException { Index: >= 0 } one && one.Index < writers.Count)
            {
                // Described as the fire of an acquired trigger describes its failed write, so that what
                // the scheduler is handed — and releases the trigger on — is the same either way.
                int position = writers[one.Index];
                IOperableTrigger trigger = due[position];
                JobPersistenceException recorded = cause as JobPersistenceException
                    ?? new JobPersistenceException($"Couldn't record the fire of trigger '{trigger.Key}' for '{trigger.JobKey}' job: {cause.Message}", cause);
                throw FireFailed(position, trigger, batchSize, recorded);
            }

            // Written as one batch, or by a delegate of its own that did not say whose failed: the round runs
            // again one trigger at a time, which finds it.
            throw e as TriggerWriteFailedException ?? new TriggerWriteFailedException(-1, cause);
        }
    }

    /// <summary>
    /// Claims the triggers one acquisition pass of a round took, together, and takes back out of what the
    /// pass acquired and reserves each whose row another node moved first.
    /// </summary>
    /// <remarks>
    /// The pass counted each as acquired while it went, so its batch window is the first trigger's whether
    /// that trigger's claim took or not: a window no wider than it would have been, never wider.
    /// </remarks>
    /// <param name="conn">The round's transaction.</param>
    /// <param name="planned">The claims the pass made, in the order it made them.</param>
    /// <param name="acquired">What the pass acquired, the claimed triggers exactly.</param>
    /// <param name="reservations">The triggers the pass reserves.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    private async ValueTask ClaimPlanned(
        ConnectionAndTransactionHolder conn,
        List<TriggerClaim> planned,
        List<IOperableTrigger> acquired,
        List<IOperableTrigger> reservations,
        CancellationToken cancellationToken)
    {
        List<TriggerKey> claimed = await Delegate.UpdateTriggerStatesFromOtherStateWithNextFireTime(
            conn,
            planned,
            StoredTriggerState.Acquired,
            StoredTriggerState.Waiting,
            cancellationToken).ConfigureAwait(false);

        if (claimed.Count == planned.Count)
        {
            return;
        }

        // Not worth a warning, for the reason a lost claim of one trigger is not: losing a row to another
        // node is the ordinary outcome of two nodes reaching for the same batch.
        HashSet<TriggerKey> took = [.. claimed];
        acquired.RemoveAll(trigger => !took.Contains(trigger.Key));
        reservations.RemoveAll(trigger => !took.Contains(trigger.Key));
    }

    /// <summary>
    /// Reads the jobs a round's triggers fire in one statement, or answers <see langword="null" /> for
    /// each fire to read its own when the batch did not read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A job whose stored data will not read — a type that will not load, data that will not deserialize —
    /// fails the read of every job beside it. Read on its own, as the fire of an acquired trigger reads it,
    /// it fails that trigger alone, which is stored <c>ERROR</c> as it always has been, so such a failure
    /// falls back to that rather than failing the round.
    /// </para>
    /// <para>
    /// A failure the database itself raised does not. On PostgreSQL it has aborted the transaction, so every
    /// read after it would fail as well, and each trigger of the round would be blamed — and counted towards
    /// parking — for a failure that is not its own. It fails the round instead, which the scheduler retries
    /// as it retries any failed acquisition. A transient failure is the transaction wrapper's to retry, and a
    /// cancellation is the caller's.
    /// </para>
    /// </remarks>
    private async ValueTask<Dictionary<JobKey, IJobDetail>?> ReadJobsToFire(
        ConnectionAndTransactionHolder conn,
        List<JobKey> jobKeys,
        CancellationToken cancellationToken)
    {
        List<IJobDetail> jobs;
        try
        {
            jobs = await Delegate.SelectJobDetails(conn, jobKeys, TypeLoader, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException && !IsTransient(e) && !RaisedByTheDatabase(e))
        {
            return null;
        }

        Dictionary<JobKey, IJobDetail> byKey = new(jobs.Count);
        foreach (IJobDetail job in jobs)
        {
            byKey[job.Key] = job;
        }

        return byKey;
    }

    /// <summary>
    /// Whether the database raised the failure, or one it wraps: a <see cref="DbException" />, rather than
    /// something that went wrong in this process with what the database returned.
    /// </summary>
    private static bool RaisedByTheDatabase(Exception failure)
    {
        for (Exception? cause = failure; cause is not null; cause = cause.InnerException)
        {
            if (cause is DbException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Asked when an attempt's commit reported a failure: whether the attempt landed anyway, which it did
    /// when any row it wrote for the triggers it acquired is there.
    /// </summary>
    private ValueTask<bool> ValidateAcquiredAndFired(
        ConnectionAndTransactionHolder conn,
        FireOnAcquireAttempt attempt,
        CancellationToken cancellationToken)
    {
        return Guarded(
            async () =>
            {
                List<FiredTriggerRecord> records = await Delegate
                    .SelectFiredTriggerRecords(conn, new FiredTriggerQuery { InstanceId = InstanceId }, cancellationToken)
                    .ConfigureAwait(false);

                HashSet<string> written = new(StringComparer.Ordinal);
                foreach (FiredTriggerRecord record in records)
                {
                    written.Add(record.FireInstanceId);
                }

                return attempt.Due.Exists(trigger => written.Contains(trigger.FireInstanceId!))
                       || attempt.Pending.Exists(trigger => written.Contains(trigger.FireInstanceId!));
            },
            "validate trigger acquisition");
    }

    /// <summary>
    /// What an attempt at a fire-on-acquire round decides its triggers by.
    /// </summary>
    /// <param name="failed">The triggers whose fire failed in an earlier attempt, claimed but not fired.</param>
    /// <param name="oneByOne">
    /// Whether the round claims and writes one trigger at a time, as it does once a batch has failed without
    /// saying which trigger it failed on.
    /// </param>
    /// <param name="throughRoundMembers">
    /// Whether the delegate's round members stand in for its single-trigger ones
    /// (<see cref="IDriverDelegate.SupportsFireOnAcquire" />). Without them the round claims, reserves, reads
    /// and fires each trigger through the members acquiring and then firing it called.
    /// </param>
    /// <param name="lockReason">
    /// Why the attempt runs under <see cref="SchedulerLock.TriggerAccess" />, or <see langword="null" />
    /// for an attempt that takes no lock (#4067).
    /// </param>
    private sealed class FireOnAcquireRound(Dictionary<TriggerKey, Exception>? failed, bool oneByOne, bool throughRoundMembers, RoundLockReason? lockReason)
    {
        /// <summary>
        /// The store's clock when the attempt claimed its first trigger. A trigger due at or before it is
        /// due in this attempt.
        /// </summary>
        private DateTimeOffset? now;

        /// <summary>
        /// The discriminator each candidate's row was stored under, as the attempt's read of the candidates
        /// returned it; <see langword="null" /> for a delegate whose read does not say.
        /// </summary>
        private Dictionary<TriggerKey, string>? storedTypes;

        /// <summary>
        /// The jobs this attempt's acquisition asked after and found not executing, whose fires in the
        /// attempt need not ask again.
        /// </summary>
        private HashSet<JobKey>? idleJobs;

        public Dictionary<TriggerKey, Exception>? Failed => failed;

        /// <inheritdoc cref="storedTypes" />
        public Dictionary<TriggerKey, string>? StoredTypes => storedTypes;

        /// <inheritdoc cref="idleJobs" />
        public HashSet<JobKey>? IdleJobs => idleJobs;

        /// <summary>
        /// Whether the attempt runs under <see cref="SchedulerLock.TriggerAccess" />. Without it the
        /// acquisition claims before it reads, never asks the fired-trigger table, and rolls the attempt
        /// back when it finds a candidate only the lock decides (#4067).
        /// </summary>
        public bool HoldsLock => lockReason is not null;

        /// <summary>
        /// Whether a locked attempt's candidates included one that needed the lock: what the round after
        /// this one starts from, when this one was locked only because the round before it had to be.
        /// </summary>
        public bool CandidateNeededLock { get; set; }

        /// <summary>
        /// Whether the round's claims are made together, at the end of each acquisition pass.
        /// </summary>
        public bool ClaimsTogether => throughRoundMembers && !oneByOne;

        /// <summary>
        /// Whether the round's fire writes are applied together, once every fire is decided.
        /// </summary>
        public bool WritesTogether => throughRoundMembers && !oneByOne;

        /// <summary>
        /// Whether the round reads the headers and jobs of its due triggers once for all of them, and their
        /// fires insert their rows rather than update a reservation.
        /// </summary>
        public bool ReadsTogether => throughRoundMembers;

        /// <summary>
        /// Whether every trigger the round claims is reserved as acquisition reserves it, due or not, so that
        /// its fire updates the reservation as the fire of an acquired trigger does.
        /// </summary>
        public bool ReservesEveryTrigger => !throughRoundMembers;

        /// <summary>
        /// Forgets the clock reading and what the acquisition read, for an attempt the transaction wrapper
        /// runs again.
        /// </summary>
        public void Reset()
        {
            now = null;
            storedTypes = null;
            idleJobs = null;
            CandidateNeededLock = false;
        }

        /// <summary>
        /// Records the discriminator a candidate's row was stored under, as the acquisition's read of the
        /// rows returned it, for the fire's write to compare the trigger's type with.
        /// </summary>
        /// <remarks>
        /// Valid while nothing can replace the row between that read and the fire. A round that holds
        /// <see cref="SchedulerLock.TriggerAccess" /> reads before it claims, and every reschedule takes
        /// that lock; a round that holds no lock reads after it claims, and the claim holds the row until
        /// the round commits (#4067). Either way the discriminator is the row's at the fire.
        /// </remarks>
        public void RecordStoredType(TriggerKey triggerKey, string storedType) => (storedTypes ??= [])[triggerKey] = storedType;

        /// <summary>
        /// Records that the acquisition asked whether the job is executing, in this transaction and under
        /// the lock, and found that it is not. Nothing can start a firing of it before the round's own fire
        /// does, so that fire has its answer.
        /// </summary>
        /// <remarks>
        /// Valid only because the round holds <see cref="SchedulerLock.TriggerAccess" /> from the ask to
        /// the fire's write, and every write of an <c>EXECUTING</c> row takes it. A round that holds no
        /// lock never marks a job idle, and never needs to: a serial candidate sends the round to the
        /// locked path before any is asked about (#4067).
        /// </remarks>
        public void MarkJobIdle(JobKey jobKey)
        {
            Debug.Assert(HoldsLock, "a job is marked idle from a read only the lock keeps still");
            (idleJobs ??= []).Add(jobKey);
        }

        /// <summary>
        /// Whether the trigger just claimed gets no reservation row, because its fire in this attempt inserts
        /// its row: the round fires through its round members, the trigger is due, and its fire has not
        /// failed in an earlier attempt.
        /// </summary>
        public bool FiresWithoutReservation(TriggerKey triggerKey, DateTimeOffset nextFireTimeUtc, TimeProvider clock)
        {
            // Read once, when there is a trigger to decide about: a reading taken before the candidates were
            // read would leave the triggers that came due while they were read waiting for a second
            // transaction.
            now ??= clock.GetUtcNow();
            return throughRoundMembers && nextFireTimeUtc <= now.Value && (failed is null || !failed.ContainsKey(triggerKey));
        }

        /// <summary>
        /// Whether a trigger this attempt claimed is due in it.
        /// </summary>
        public bool IsDue(DateTimeOffset nextFireTimeUtc) => now is { } reading && nextFireTimeUtc <= reading;
    }

    /// <summary>
    /// What a fire-on-acquire round read once for all of its due triggers: the state and discriminator of
    /// each, their jobs, the jobs its acquisition found idle, and — read when the first fire asks — the
    /// paused groups.
    /// </summary>
    private sealed class FireOnAcquirePrefetch
    {
        private readonly Dictionary<TriggerKey, StoredTriggerIdentity> stored;
        private readonly HashSet<JobKey>? idleJobs;
        private readonly StdAdoDelegate? shipped;

        /// <summary>
        /// The job keys already handed to a fire in this round, whose next fire gets a copy.
        /// </summary>
        private HashSet<JobKey>? handedOut;

        /// <summary>
        /// The jobs that disallow concurrent execution which a fire of this round has started, with its
        /// writes still to go.
        /// </summary>
        private HashSet<JobKey>? firedInRound;

        /// <summary>
        /// The paused groups, read by the first fire of the round whose stored state asks after them.
        /// </summary>
        private PausedGroups? pausedGroups;

        /// <param name="stored">The state and discriminator of each due trigger, by key.</param>
        /// <param name="jobs">The round's jobs, or <see langword="null" /> when each fire reads its own.</param>
        /// <param name="idleJobs">The jobs the round's acquisition found not executing, or <see langword="null" />.</param>
        /// <param name="shipped">
        /// The delegate, when it is one Quartz ships: the one kind whose paused-group reads the round may
        /// make once for all of its fires, since no override of the per-trigger ones exists to bypass.
        /// </param>
        /// <param name="holdsLock">
        /// Whether the round holds <see cref="SchedulerLock.TriggerAccess" />. A fire in a round that does
        /// not re-checks, from the rows read after the claim, that nothing it fires needs it (#4067).
        /// </param>
        public FireOnAcquirePrefetch(
            Dictionary<TriggerKey, StoredTriggerIdentity> stored,
            Dictionary<JobKey, IJobDetail>? jobs,
            HashSet<JobKey>? idleJobs,
            StdAdoDelegate? shipped,
            bool holdsLock)
        {
            this.stored = stored;
            this.idleJobs = idleJobs;
            this.shipped = shipped;
            Jobs = jobs;
            HoldsLock = holdsLock;
        }

        /// <summary>
        /// The round's jobs by key, or <see langword="null" /> when each fire reads its own.
        /// </summary>
        public Dictionary<JobKey, IJobDetail>? Jobs { get; }

        /// <summary>
        /// Whether the round holds <see cref="SchedulerLock.TriggerAccess" />; see the constructor.
        /// </summary>
        public bool HoldsLock { get; }

        /// <summary>
        /// The stored state and discriminator of a due trigger, or <see langword="null" /> for one whose
        /// row the round did not read — which is to say, one that is not there.
        /// </summary>
        public StoredTriggerIdentity? Stored(TriggerKey triggerKey) => stored.TryGetValue(triggerKey, out StoredTriggerIdentity identity) ? identity : null;

        /// <summary>
        /// Whether the round's acquisition asked whether the job is executing and found that it is not,
        /// in this transaction and under the lock, so that the fire need not ask again.
        /// </summary>
        /// <remarks>
        /// An answer only because the round holds <see cref="SchedulerLock.TriggerAccess" /> from the
        /// acquisition's ask to this fire's write, which every write of an <c>EXECUTING</c> row takes;
        /// see <see cref="FireOnAcquireRound.MarkJobIdle" />. A round without the lock has no idle jobs
        /// and no serial fires to ask for them (#4067).
        /// </remarks>
        public bool KnownIdle(JobKey jobKey) => idleJobs is not null && idleJobs.Contains(jobKey);

        /// <summary>
        /// The paused groups as one read finds them, read on the first ask and kept for the round; or
        /// <see langword="null" /> for a delegate whose per-trigger reads the round has to make.
        /// </summary>
        /// <remarks>
        /// Asked only by a fire that stores <c>BLOCKED</c> — a serial job's, or a <c>BufferOne</c>
        /// trigger's — which is always a fire under the lock (#4067): a concurrent-allowed fire writes
        /// <c>WAITING</c> or <c>COMPLETE</c> and never reads the paused groups, so a pause committed after
        /// the candidate read lands on the row the fire leaves.
        /// </remarks>
        public async ValueTask<PausedGroups?> ReadPausedGroups(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken)
        {
            Debug.Assert(HoldsLock, "the paused groups are read once for a round only under the lock, which keeps them still");
            if (shipped is null)
            {
                return null;
            }

            return pausedGroups ??= await shipped.SelectPausedGroups(conn, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// The job a fire runs, or <see langword="null" /> when there is none. Two fires of one job in a
        /// round each get an instance of their own, as reading it once per fire gave them: the job's data
        /// map belongs to the firing that runs it.
        /// </summary>
        public IJobDetail? TakeJob(JobKey jobKey)
        {
            if (Jobs is null || !Jobs.TryGetValue(jobKey, out IJobDetail? job))
            {
                return null;
            }

            handedOut ??= [];
            return handedOut.Add(jobKey) ? job : job.Clone();
        }

        /// <summary>
        /// Whether a fire of this round has already started the job, which disallows concurrent execution.
        /// </summary>
        /// <remarks>
        /// Acquisition takes one trigger of such a job a round, by the flag the delegate read with each
        /// candidate. Where a delegate does not read it, acquisition asks the job's type instead, and a job
        /// made serial by its builder rather than an attribute gets two triggers into the round. The
        /// fired-trigger table cannot hold the second back while the first one's row is still to be
        /// written, so this does: the second is not fired, as when the table says the job is executing.
        /// </remarks>
        public bool FiredInRound(JobKey jobKey) => firedInRound is not null && firedInRound.Contains(jobKey);

        /// <summary>
        /// Records that a fire of this round started the job, whose row the round writes later.
        /// </summary>
        public void MarkFiredInRound(JobKey jobKey) => (firedInRound ??= []).Add(jobKey);
    }

    /// <summary>
    /// The two things a fire reads off a trigger's row before it writes: the state it may fire from, and
    /// the discriminator its write compares the trigger's current type with.
    /// </summary>
    private readonly record struct StoredTriggerIdentity(StoredTriggerState State, string TriggerType);

    /// <summary>
    /// What one attempt at a fire-on-acquire round did: the due triggers with a result for each —
    /// <see langword="null" /> for one reserved but not fired — the triggers left pending, and what its
    /// acquisition took, with the due triggers a running firing holds back from it.
    /// </summary>
    private sealed record FireOnAcquireAttempt(
        List<IOperableTrigger> Due,
        TriggerFiredResult?[] Fired,
        List<IOperableTrigger> Pending,
        AcquiredTriggers Acquired);
}
