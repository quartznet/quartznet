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
/// round is one: the triggers are claimed as acquisition claims them, their headers and jobs are read once
/// for the round, and each fire inserts its row as <c>EXECUTING</c>. On PostgreSQL that is a lock, a commit
/// and two reads per trigger off the path every firing waits on.
/// </para>
/// <para>
/// The lock is the one acquisition takes. A round that acquires without it — one trigger at a time,
/// without <see cref="AcquireTriggersWithinLock" /> — stays two transactions, so an idle or pending-only
/// round still never waits for the lock.
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

        if (!AcquireTriggersWithinLock && request.MaxCount <= 1)
        {
            // Acquired without the lock, as it always was; the fire takes the lock in a transaction of its
            // own, which is the scheduler's TriggersFired.
            AcquiredTriggers acquired = await AcquireNextTriggersWithoutFiring(request, cancellationToken).ConfigureAwait(false);
            return new TriggerAcquisitionResult
            {
                Pending = acquired.Triggers,
                Blocked = acquired.Blocked,
                LatestBlockingFiredUtc = acquired.LatestBlockingFiredUtc,
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

        while (true)
        {
            FireOnAcquireAttempt attempt;
            try
            {
                FireOnAcquireRound round = new(failed, oneByOne, Delegate.SupportsFireOnAcquire);
                attempt = await ExecuteInLocalTransactionLock(
                    SchedulerLock.TriggerAccess,
                    conn => AcquireAndFireDue(conn, request, round, cancellationToken),
                    (conn, result) => ValidateAcquiredAndFired(conn, result, cancellationToken),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
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
            };
        }
    }

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
        AcquiredTriggers acquired = await AcquireNextTrigger(conn, request, round, cancellationToken).ConfigureAwait(false);

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
            // The state and type discriminator of each, read in one statement rather than one per trigger.
            // The state is ACQUIRED — this transaction has just claimed them under the lock — and the
            // discriminator is what the fire's write compares the trigger's current type with.
            List<StoredTriggerHeader> headers = await Guarded(
                () => Delegate.SelectStoredTriggerHeaders(conn, triggerKeys, cancellationToken),
                "select trigger states").ConfigureAwait(false);

            prefetch = new FireOnAcquirePrefetch(headers, await ReadJobsToFire(conn, jobKeys, cancellationToken).ConfigureAwait(false));
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
    private sealed class FireOnAcquireRound(Dictionary<TriggerKey, Exception>? failed, bool oneByOne, bool throughRoundMembers)
    {
        /// <summary>
        /// The store's clock when the attempt claimed its first trigger. A trigger due at or before it is
        /// due in this attempt.
        /// </summary>
        private DateTimeOffset? now;

        public Dictionary<TriggerKey, Exception>? Failed => failed;

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
        /// Forgets the clock reading, for an attempt the transaction wrapper runs again.
        /// </summary>
        public void Reset() => now = null;

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
    /// The headers and jobs a fire-on-acquire round read for all of its due triggers.
    /// </summary>
    private sealed class FireOnAcquirePrefetch
    {
        private readonly Dictionary<TriggerKey, StoredTriggerHeader> headers;

        /// <summary>
        /// The job keys already handed to a fire in this round, whose next fire gets a copy.
        /// </summary>
        private HashSet<JobKey>? handedOut;

        /// <summary>
        /// The jobs that disallow concurrent execution which a fire of this round has started, with its
        /// writes still to go.
        /// </summary>
        private HashSet<JobKey>? firedInRound;

        public FireOnAcquirePrefetch(List<StoredTriggerHeader> headers, Dictionary<JobKey, IJobDetail>? jobs)
        {
            this.headers = new Dictionary<TriggerKey, StoredTriggerHeader>(headers.Count);
            foreach (StoredTriggerHeader header in headers)
            {
                this.headers[header.Key] = header;
            }

            Jobs = jobs;
        }

        /// <summary>
        /// The round's jobs by key, or <see langword="null" /> when each fire reads its own.
        /// </summary>
        public Dictionary<JobKey, IJobDetail>? Jobs { get; }

        public StoredTriggerHeader? Header(TriggerKey triggerKey) => headers.GetValueOrDefault(triggerKey);

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
