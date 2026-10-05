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
    protected virtual string GetFiredTriggerRecordId()
    {
        Interlocked.Increment(ref firedTriggerCounter);
        return InstanceId + firedTriggerCounter;
    }

    private static long firedTriggerCounter = TimeProvider.System.GetTimestamp();

    /// <summary>
    /// Get a handle to the next N triggers to be fired, and mark them as 'reserved'
    /// by the calling scheduler.
    /// </summary>
    /// <seealso cref="ReleaseAcquiredTrigger(IOperableTrigger, CancellationToken)" />
    /// <inheritdoc />
    public virtual async ValueTask<List<IOperableTrigger>> AcquireNextTriggers(
        TriggerAcquisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        AcquiredTriggers acquired = await AcquireNextTriggersWithoutFiring(request, cancellationToken).ConfigureAwait(false);
        return acquired.Triggers;
    }

    /// <summary>
    /// <see cref="AcquireNextTriggers" />, saying as well how many due triggers it passed over behind an
    /// executing job, which <see cref="AcquireNextTriggersAndFireDue" /> hands the scheduler.
    /// </summary>
    private ValueTask<AcquiredTriggers> AcquireNextTriggersWithoutFiring(
        TriggerAcquisitionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        SchedulerLock? lockKind;
        if (AcquireTriggersWithinLock || request.MaxCount > 1)
        {
            lockKind = SchedulerLock.TriggerAccess;
        }
        else
        {
            lockKind = null;
        }

        return ExecuteInLocalTransactionLock(
            lockKind,
            conn => AcquireNextTrigger(conn, request, round: null, cancellationToken),
            (conn, result) => Guarded(
                async () =>
                {
                    var acquired = await Delegate.SelectFiredTriggerRecords(conn, new FiredTriggerQuery { InstanceId = InstanceId }, cancellationToken).ConfigureAwait(false);
                    var fireInstanceIds = new HashSet<string>();
                    foreach (FiredTriggerRecord ft in acquired)
                    {
                        fireInstanceIds.Add(ft.FireInstanceId!);
                    }
                    foreach (IOperableTrigger tr in result.Triggers)
                    {
                        if (fireInstanceIds.Contains(tr.FireInstanceId!))
                        {
                            return true;
                        }
                    }
                    return false;
                },
                "validate trigger acquisition"),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Builds the criteria <see cref="IDriverDelegate.SelectTriggersToAcquire" /> is called with when
    /// this node looks for the next triggers to fire.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the override seam for acquisition filtering (see issue #2238). A derived store narrows
    /// what its own node picks up by starting from <c>base.CreateAcquisitionCriteria(request)</c> and
    /// returning a copy with the additional filters set — the criteria are a record, so <c>with</c>
    /// leaves everything the base decided in place.
    /// </para>
    /// <para>
    /// Called once per acquisition attempt, inside the store's internal retry loop, so an override
    /// runs again for every retry rather than once per <see cref="AcquireNextTriggers" /> call.
    /// </para>
    /// <para>
    /// An override may lower <see cref="TriggerAcquisitionCriteria.MaxCount" /> but must never raise it
    /// above the request's: the choice between lock-free and locked acquisition was already made from the
    /// request before this factory runs, so a raised count is only caught by post-acquisition validation
    /// and the surplus is released and retried — a performance hazard rather than corruption, but a
    /// silent one. The count this returns is the most a round acquires. The store reads <em>above</em>
    /// it on its own when a round skipped every row it read — a trigger of a job executing on another
    /// node, say — so that the rows due behind the skipped ones are seen, and still takes no more than
    /// this count.
    /// </para>
    /// <para>
    /// One property is filled in after this returns:
    /// <see cref="TriggerAcquisitionCriteria.ClusterInFlight" /> is read from the delegate when the
    /// limits contain a cluster-scoped one and the override left it <see langword="null" />. An
    /// override that sets it keeps its own answer.
    /// </para>
    /// <para>
    /// <see cref="TriggerAcquisitionCriteria" />'s remarks state the contract a new filter has to
    /// keep: it is another optional property on that record, defaulting to "no additional filtering".
    /// </para>
    /// <para>
    /// The other half of the acquisition contract is on the far side: the list
    /// <see cref="IJobStore.AcquireNextTriggers" /> returns stays the store's, because the scheduler
    /// thread copies it before working with it. A store overriding acquisition does not have to build a
    /// fresh list to be safe.
    /// </para>
    /// </remarks>
    /// <param name="request">What the scheduler asked this store to acquire.</param>
    protected virtual TriggerAcquisitionCriteria CreateAcquisitionCriteria(TriggerAcquisitionRequest request)
    {
        // The liveness cutoff determines when a preferred node is considered dead, releasing
        // its pinned triggers to other nodes. SQL check: a node is live if
        // (now - lastCheckin) <= checkinInterval + misfireThreshold. This is equivalent to
        // CalcFailedIfAfter for healthy acquiring nodes; the formulas only diverge when the
        // acquiring node itself is unhealthy (its own checkins are late), in which case
        // CalcFailedIfAfter becomes MORE lenient while this stays fixed. Being more
        // aggressive in that edge case is the safer direction — it prevents triggers pinned
        // to a dead node from being stuck when the surviving nodes are under load.
        DateTimeOffset liveNodeCutoff = timeProvider.GetUtcNow() - ClusterCheckinMisfireThreshold;

        return new TriggerAcquisitionCriteria
        {
            NoLaterThan = request.NoLaterThan + request.TimeWindow,
            NoEarlierThan = MisfireTime,
            MaxCount = request.MaxCount,
            ExecutionLimits = request.ExecutionLimits,
            ExcludedJobTypeNames = request.ExcludedJobTypeNames,
            LiveNodeCutoff = liveNodeCutoff,
        };
    }

    // The acquired triggers carry their fire instance id on themselves rather than in a bundle beside
    // them, because IOperableTrigger.FireInstanceId is the contract the scheduling loop, the fired-
    // trigger row and TriggerFiredBundle all read it through; a second shape here would be a fourth
    // spelling of the same field rather than a way of removing one.
    //
    // A round that fires what is due as it acquires it passes its FireOnAcquireRound: a trigger the round
    // fires gets no reservation row here, because its fire writes the row as EXECUTING in the same
    // transaction.
    private ValueTask<AcquiredTriggers> AcquireNextTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerAcquisitionRequest request,
        FireOnAcquireRound? round,
        CancellationToken cancellationToken)
    {
        return Guarded(
            async () =>
            {
                List<IOperableTrigger> acquiredTriggers = [];
                HashSet<JobKey> acquiredJobKeysForNoConcurrentExec = [];
                const int MaxDoLoopRetry = 3;
                int retries = 0;

                // Rows the previous round read and could not use, and which the next read will return
                // again ahead of everything else: still WAITING, and still first in the fire-time order.
                // Zero on the first round and on every acquisition that skips nothing, so the ordinary
                // acquisition reads exactly the count it was asked for.
                int readAhead = 0;

                // The rows the last read skipped because their job was executing, and the jobs found
                // executing. Each read past skipped rows returns those rows again, so the last read's
                // count is the acquisition's; the jobs are remembered from read to read, since a job
                // found executing is also one whose key the set above already holds, and its rows read
                // again are skipped by that check before they reach this one.
                int blocked = 0;
                HashSet<JobKey>? executingJobs = null;

                do
                {
                    // Built inside the loop, so each retry asks again and sees the time it retried at.
                    TriggerAcquisitionCriteria criteria = CreateAcquisitionCriteria(request);

                    // The most this round may acquire: the request's count, or what an override lowered
                    // it to. A round reading past skipped rows reads more than this, and stops taking
                    // triggers at it.
                    int maxCount = criteria.MaxCount;
                    if (readAhead > 0)
                    {
                        criteria = criteria with { MaxCount = maxCount + readAhead };
                    }

                    // The backstop for a delegate that does not keep the excluded job types out itself.
                    // Not built at all for one that says it does — which is every dialect Quartz ships —
                    // so the shipped path pays nothing for it. It deliberately compares ordinally; SQL
                    // filtering follows the job-class column's collation and is not guaranteed to agree.
                    HashSet<string>? excludedJobTypeNames = !Delegate.FiltersAcquisitionJobTypeExclusions
                                                            && criteria.ExcludedJobTypeNames is { Count: > 0 } names
                        ? new HashSet<string>(names, StringComparer.Ordinal)
                        : null;

                    // A cluster-scoped limit is counted against the fired-triggers table, so the count is
                    // read here rather than derived from anything this node remembers. One aggregate per
                    // attempt, and none at all unless a cluster-scoped limit is configured - which is also
                    // why an override that already answered the question is left alone.
                    if (criteria.ClusterInFlight is null && criteria.ExecutionLimits?.HasClusterScopedLimits == true)
                    {
                        criteria = criteria with
                        {
                            ClusterInFlight = await Delegate.SelectExecutionGroupsInFlight(conn, cancellationToken).ConfigureAwait(false),
                        };
                    }

                    List<TriggerAcquireResult> results = await Delegate.SelectTriggersToAcquire(conn, criteria, cancellationToken).ConfigureAwait(false);

                    // No trigger is ready to fire yet.
                    if (results.Count == 0)
                    {
                        blocked = 0;
                        break;
                    }

                    // Whether the read stopped at its own limit rather than at the end of what is due,
                    // which is the only case in which rows this round skips can be hiding rows behind them.
                    bool readWasLimited = results.Count >= criteria.MaxCount;

                    // Candidates this round read, could not use, and left WAITING - so a read of the same
                    // length would return the same rows again - against candidates that left WAITING while
                    // this round ran, which a read of the same length will not see and may find something
                    // behind.
                    int skipped = 0;
                    int raced = 0;
                    bool reachedBatchEnd = false;
                    blocked = 0;

                    // The delegate was told which job types this node will not run, and did not say it
                    // enforces that itself. Dropping them here — on the name the acquisition read already
                    // returned — is what keeps the promise; doing it before the read below is what keeps
                    // it from costing a round trip and a type resolution per candidate (#3443).
                    if (excludedJobTypeNames is not null)
                    {
                        int before = results.Count;
                        results = results.FindAll(candidate => !excludedJobTypeNames.Contains(candidate.JobTypeName));
                        skipped += before - results.Count;
                    }

                    // Rows the delegate read and refused for their execution group's limit, returned
                    // flagged rather than dropped so that this round knows it read them. They stay
                    // WAITING, first in the order, and hide what is due behind them from a read of the same
                    // length exactly as a row of an executing job does (#3928) - so they are skipped rows,
                    // and never read back.
                    int atLimit = results.RemoveAll(static candidate => candidate.ExecutionGroupAtLimit);
                    skipped += atLimit;

                    // One read for the whole round's candidates. The acquisition statement just named
                    // them; going back per candidate cost a round trip each before a single one was
                    // marked acquired (#3424).
                    Dictionary<TriggerKey, IOperableTrigger> candidates = await ReadAcquisitionCandidates(Delegate, conn, results, cancellationToken).ConfigureAwait(false);

                    DateTimeOffset batchEnd = request.NoLaterThan;

                    // The fired-trigger rows of this pass, written together at the end of it rather than
                    // one statement at a time. They name no job until the trigger fires, so nothing this
                    // loop asks the database can see them.
                    List<IOperableTrigger> firedTriggerRows = [];

                    // The claims of a round that fires what is due as it acquires it, made together at the
                    // end of the pass: one batch, where the delegate sends one. Null for every other
                    // acquisition, which claims each trigger as it comes to it.
                    List<TriggerClaim>? planned = round is { ClaimsTogether: true } ? [] : null;

                    foreach (var result in results)
                    {
                        // Only a round reading past skipped rows can have more rows than it may take.
                        if (acquiredTriggers.Count >= maxCount)
                        {
                            break;
                        }

                        TriggerKey triggerKey = result.TriggerKey;

                        // If our trigger is no longer available, try a new one.
                        if (!candidates.TryGetValue(triggerKey, out IOperableTrigger? nextTrigger))
                        {
                            raced++;
                            continue; // next trigger
                        }

                        // If trigger's job is set as @DisallowConcurrentExecution, and it has already been added to result, then
                        // put it back into the timeTriggers set and continue to search for next trigger.
                        Type jobType;
                        try
                        {
                            jobType = JobType.Resolve(result.JobTypeName, typeLoader)!;
                        }
                        catch (Exception e)
                        {
                            try
                            {
                                Logger.JobRetrievalFailed(e);
                                await Delegate.UpdateTriggerState(conn, triggerKey, StoredTriggerState.Error, cancellationToken).ConfigureAwait(false);

                                // A trigger whose job type will not load stops firing here and is reported
                                // nowhere else - not even through SchedulerError. Inline, as the misfire
                                // notification in this store already is.
                                await signaler.NotifySchedulerListenersTriggerInError(triggerKey, cancellationToken).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                Logger.TriggerErrorStateUpdateFailed(ex);
                            }

                            // The row is ERROR now, or about to be looked at again for the same reason;
                            // either way the next read tells the difference, so it counts as a race.
                            raced++;
                            continue;
                        }

                        // The stored flag, which is what the fire path obeys: a job configured with
                        // DisallowConcurrentExecution() says so without any attribute on its type, and asking
                        // the type alone let such a job into one batch twice. A delegate that did not read the
                        // flag is answered from the type the way JobDetailImpl answers it: the attribute is
                        // inherited from an interface as readily as from a base class.
                        if (result.ConcurrentExecutionDisallowed ?? JobTypeInformation.GetOrCreate(jobType).ConcurrentExecutionDisallowed)
                        {
                            if (!acquiredJobKeysForNoConcurrentExec.Add(nextTrigger.JobKey))
                            {
                                skipped++;
                                if (executingJobs is not null && executingJobs.Contains(nextTrigger.JobKey))
                                {
                                    blocked++;
                                }

                                continue; // next trigger
                            }

                            // Cluster-safe check: skip if job is already executing on another node. The
                            // row stays WAITING, first in the order, and a read of the same length would
                            // return it again ahead of everything due behind it (#3926). Counted apart as
                            // well: the execution's end is what frees it, and that end may be another
                            // node's, which nothing tells this one of (#3988).
                            if (await Delegate.IsJobCurrentlyExecuting(conn, nextTrigger.JobKey, cancellationToken).ConfigureAwait(false))
                            {
                                (executingJobs ??= []).Add(nextTrigger.JobKey);
                                skipped++;
                                blocked++;
                                continue;
                            }
                        }

                        var nextFireTimeUtc = nextTrigger.NextFireTimeUtc;

                        // A trigger should not return NULL on nextFireTime when fetched from DB.
                        // But for whatever reason if we do have this (BAD trigger implementation or
                        // data?), we then should log a warning and continue to next trigger.
                        // User would need to manually fix these triggers from DB as they will not
                        // able to be clean up by Quartz since we are not returning it to be processed.
                        if (nextFireTimeUtc is null)
                        {
                            // The candidates were read by their fire time, so a row read back without one
                            // has changed since. A row no longer waiting is a race lost — another node fired
                            // it, and a spent one-off waits for its completion to delete it — which a node
                            // firing what it acquires makes common. Only a waiting row is bad data.
                            if (await Delegate.SelectTriggerState(conn, triggerKey, cancellationToken).ConfigureAwait(false) != StoredTriggerState.Waiting)
                            {
                                raced++;
                                continue;
                            }

                            Logger.TriggerHasNoNextFireTime(nextTrigger.Key);
                            skipped++;
                            continue;
                        }

                        if (nextFireTimeUtc > batchEnd)
                        {
                            // Everything behind this row is due later still, so there is nothing to
                            // read past to.
                            reachedBatchEnd = true;
                            break;
                        }

                        // We now have a acquired trigger, let's add to return list. A round claiming its
                        // triggers together counts this one as acquired until its claim says otherwise.
                        if (planned is not null)
                        {
                            planned.Add(new TriggerClaim { TriggerKey = triggerKey, NextFireTimeUtc = nextFireTimeUtc.Value });
                        }
                        else
                        {
                            // If our trigger was no longer in the expected state, try a new one.
                            int rowsUpdated = await Delegate.UpdateTriggerStateFromOtherStateWithNextFireTime(conn, triggerKey, StoredTriggerState.Acquired, StoredTriggerState.Waiting, nextFireTimeUtc.Value, cancellationToken).ConfigureAwait(false);
                            if (rowsUpdated <= 0)
                            {
                                // Not worth a warning: the row was no longer WAITING, which is what losing
                                // the race to another node looks like, and in a cluster that is the ordinary
                                // outcome of two nodes reaching for the same batch. Logging it would produce
                                // noise proportional to how well the cluster is sharing its work.
                                raced++;
                                continue; // next trigger
                            }
                        }

                        nextTrigger.FireInstanceId = GetFiredTriggerRecordId();

                        // A trigger this transaction fires has its row written by the fire; every other
                        // one is reserved as acquisition has always reserved it.
                        if (round is null || !round.FiresWithoutReservation(nextTrigger.Key, nextFireTimeUtc.Value, timeProvider))
                        {
                            firedTriggerRows.Add(nextTrigger);
                        }

                        if (acquiredTriggers.Count == 0)
                        {
                            var now = timeProvider.GetUtcNow();
                            var nextFireTime = nextFireTimeUtc.Value;
                            var max = now > nextFireTime ? now : nextFireTime;

                            batchEnd = max + request.TimeWindow;
                        }

                        acquiredTriggers.Add(nextTrigger);
                    }

                    if (planned is { Count: > 0 })
                    {
                        await ClaimPlanned(conn, planned, acquiredTriggers, firedTriggerRows, cancellationToken).ConfigureAwait(false);
                        raced += planned.Count - acquiredTriggers.Count;
                    }

                    if (firedTriggerRows.Count > 0)
                    {
                        await Delegate.InsertFiredTriggers(conn, firedTriggerRows, StoredTriggerState.Acquired, null, cancellationToken).ConfigureAwait(false);
                    }

                    if (acquiredTriggers.Count > 0)
                    {
                        break;
                    }

                    // Every row this round read was one it could not use, and the read stopped at its
                    // limit, so what is due behind them has not been looked at. Read past them: the next
                    // round asks for at least twice as many rows beyond the count as this one skipped,
                    // so the rounds are few however many such rows there are, and it ends as soon as a
                    // read comes back short of its limit. Not a retry - nothing raced - so it is not
                    // charged as one.
                    if (skipped > 0 && readWasLimited && !reachedBatchEnd)
                    {
                        readAhead = 2 * Math.Max(skipped, readAhead);
                        continue;
                    }

                    // Rows that stopped being WAITING while this round ran, which is what losing them to
                    // another node looks like; a read of the same length now finds whatever was due behind
                    // them. Bounded, as a cluster under contention can lose every round. A round that only
                    // skipped is not retried: the same read would return the same rows, and skip them for
                    // the same reasons.
                    if (raced > 0 && ++retries < MaxDoLoopRetry)
                    {
                        continue;
                    }

                    // We are done with the while loop.
                    break;
                } while (true);

                // A round that took nothing says as well how many of this node's pinned triggers a firing on
                // another node holds BLOCKED. Its read never sees them, and that firing's end wakes only its own
                // node, so without this the round's node would find them an idle wait later (#3988). Asked only
                // of a cluster, and only by a round that is about to leave its node idle.
                PinnedTriggersBlocked pinned = acquiredTriggers.Count == 0 && Clustered
                    ? await SelectPinnedTriggersBlockedElsewhere(conn, request.NoLaterThan + request.TimeWindow, cancellationToken).ConfigureAwait(false)
                    : PinnedTriggersBlocked.None;

                return new AcquiredTriggers(acquiredTriggers, blocked + pinned.Count, pinned.LatestBlockingFiredUtc);
            },
            "acquire next trigger");
    }

    /// <summary>
    /// The triggers pinned to this node and due by <paramref name="noLaterThan" /> that a firing on another
    /// node holds <c>BLOCKED</c>, and when the latest such firing was fired (#3988).
    /// </summary>
    /// <remarks>
    /// Asked of the delegates Quartz ships, through a member of their own rather than of
    /// <see cref="IDriverDelegate" />: the answer only says how soon to look again, and a delegate of
    /// somebody's own that cannot give it leaves its node looking after its idle wait, as before.
    /// </remarks>
    /// <param name="conn">The acquisition's connection.</param>
    /// <param name="noLaterThan">The end of the acquisition's window.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal virtual ValueTask<PinnedTriggersBlocked> SelectPinnedTriggersBlockedElsewhere(
        ConnectionAndTransactionHolder conn,
        DateTimeOffset noLaterThan,
        CancellationToken cancellationToken)
    {
        return Delegate is StdAdoDelegate shipped
            ? shipped.SelectPinnedTriggersBlockedElsewhere(conn, noLaterThan, cancellationToken)
            : new ValueTask<PinnedTriggersBlocked>(PinnedTriggersBlocked.None);
    }

    /// <summary>
    /// What one acquisition took, and how many due triggers a running firing holds back from it: ones it
    /// passed over because their job, which disallows concurrent execution, was executing, and — when it
    /// took nothing — this node's pinned triggers a firing on another node holds <c>BLOCKED</c>, with when
    /// the latest of those firings was fired.
    /// </summary>
    private readonly record struct AcquiredTriggers(List<IOperableTrigger> Triggers, int Blocked, DateTimeOffset? LatestBlockingFiredUtc = null);

    /// <summary>
    /// Reads back the triggers an acquisition round has just named, in one statement, keyed by their
    /// trigger key.
    /// </summary>
    /// <remarks>
    /// A candidate with no entry in the result is one whose row is no longer there — deleted between
    /// the acquisition read and this one — which is exactly what the per-candidate read said by
    /// answering <see langword="null" />. The delegate chunks the key set to the provider's parameter
    /// ceiling, so "one statement" means one for a batch of any size a scheduler asks for.
    /// </remarks>
    private static async ValueTask<Dictionary<TriggerKey, IOperableTrigger>> ReadAcquisitionCandidates(
        IDriverDelegate driverDelegate,
        ConnectionAndTransactionHolder conn,
        List<TriggerAcquireResult> results,
        CancellationToken cancellationToken)
    {
        if (results.Count == 0)
        {
            return new Dictionary<TriggerKey, IOperableTrigger>();
        }

        List<TriggerKey> keys = new(results.Count);
        foreach (TriggerAcquireResult result in results)
        {
            keys.Add(result.TriggerKey);
        }

        List<IOperableTrigger> triggers = await Guarded(
            () => driverDelegate.SelectTriggers(conn, keys, cancellationToken),
            "retrieve triggers").ConfigureAwait(false);

        Dictionary<TriggerKey, IOperableTrigger> byKey = new(triggers.Count);
        foreach (IOperableTrigger trigger in triggers)
        {
            byKey[trigger.Key] = trigger;
        }

        return byKey;
    }

    /// <summary>
    /// Fires a batch of acquired triggers in one transaction under
    /// <see cref="SchedulerLock.TriggerAccess" />, and says for each what became of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A fire that fails for a reason a retry will not cure is not left half-written. A statement that
    /// fails partway leaves the writes before it in the transaction — the fired row updated, the job's
    /// other triggers <c>BLOCKED</c> — and a commit keeps them, with nothing executing to let go of
    /// them; on PostgreSQL the failure also dooms every statement after it, so the rest of the batch
    /// fails too and the fires reported before it are rolled back underneath the jobs about to run
    /// (#3931). So the attempt throws, the transaction wrapper rolls it back and releases the lock, and
    /// the batch runs again without that trigger — once per trigger that fails, so the attempts are
    /// bounded by the batch. A batch in which every fire succeeds is one attempt, exactly as before, and
    /// pays nothing for this.
    /// </para>
    /// <para>
    /// A failure the store settles inside the transaction — a job that will not load, whose trigger is
    /// stored <c>ERROR</c> so that it is not acquired again — is not one of those: <see cref="FireTrigger" />
    /// answers it as a result, and the attempt goes on and commits it.
    /// </para>
    /// <para>
    /// A trigger whose fire fails is released by the scheduler and acquired again, and its fire time has
    /// not moved, so one that fails every time would be first in every round for good. Each such failure
    /// is counted against it, and once it has failed <see cref="MaxConsecutiveFireFailures" /> times in a
    /// row it is stored <c>ERROR</c> after the batch has settled (#3963).
    /// </para>
    /// </remarks>
    public async ValueTask<List<TriggerFiredResult>> TriggersFired(IReadOnlyCollection<IOperableTrigger> triggers, CancellationToken cancellationToken = default)
    {
        // The batch as the scheduler handed it over, fired whole on the first attempt. Only a failure
        // copies it, to take the failed trigger out.
        IReadOnlyList<IOperableTrigger> attempt = triggers as IReadOnlyList<IOperableTrigger> ?? [.. triggers];

        // What has been settled outside an attempt, by the position the scheduler knows the trigger by,
        // and which position each trigger still in the attempt has there. Neither exists until a fire
        // has failed.
        TriggerFiredResult?[]? settled = null;
        List<int>? positions = null;

        // The triggers whose failure in this batch was the last one in a row the store allows, with how
        // many that was. Stored ERROR once the batch has settled.
        List<(TriggerKey TriggerKey, int Failures)>? failing = null;

        while (true)
        {
            List<TriggerFiredResult> fired;
            try
            {
                fired = await ExecuteInLocalTransactionLock(
                    SchedulerLock.TriggerAccess,
                    conn => FireBatch(conn, attempt, cancellationToken),
                    (conn, result) => ValidateFired(conn, result, cancellationToken),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
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

                settled ??= new TriggerFiredResult?[triggers.Count];
                positions ??= [.. Enumerable.Range(0, triggers.Count)];
                settled[positions[failure.Index]] = TriggerFiredResult.Failed(cause);
                positions.RemoveAt(failure.Index);

                // Counted with the previous fire time it was acquired with, which only a fire that
                // committed moves.
                if (MaxConsecutiveFireFailures > 0)
                {
                    int failures = fireFailures.RecordFailure(failure.TriggerKey, attempt[failure.Index].PreviousFireTimeUtc);
                    if (failures >= MaxConsecutiveFireFailures)
                    {
                        (failing ??= []).Add((failure.TriggerKey, failures));
                    }
                }

                List<IOperableTrigger> remaining = new(attempt.Count - 1);
                for (int i = 0; i < attempt.Count; i++)
                {
                    if (i != failure.Index)
                    {
                        remaining.Add(attempt[i]);
                    }
                }

                attempt = remaining;
                Logger.FireBatchRolledBack(failure.TriggerKey, triggers.Count, remaining.Count);

                if (remaining.Count > 0)
                {
                    continue;
                }

                fired = [];
            }

            ForgetFireFailures(attempt, fired);

            if (settled is null)
            {
                return fired;
            }

            // The attempt kept the scheduler's order for the triggers it still had, so its results slot
            // into the gaps between the settled ones in order.
            List<TriggerFiredResult> results = new(settled.Length);
            int next = 0;
            foreach (TriggerFiredResult? result in settled)
            {
                results.Add(result ?? fired[next++]);
            }

            if (failing is not null)
            {
                await ParkFailingTriggers(failing, cancellationToken).ConfigureAwait(false);
            }

            return results;
        }
    }

    /// <summary>
    /// Ends the run of failed fires of each trigger the committed attempt settled: fired, declined by its
    /// overlap policy, or stored <c>ERROR</c> because its job would not load.
    /// </summary>
    /// <remarks>
    /// A trigger the attempt did not fire at all — paused, deleted, held back, or failed in an earlier
    /// attempt and so not fired in this one — keeps its count, since nothing about it was written.
    /// Nothing to do while no trigger has a failure counted, which is the ordinary batch.
    /// </remarks>
    private void ForgetFireFailures(IReadOnlyList<IOperableTrigger> attempt, IReadOnlyList<TriggerFiredResult?> fired)
    {
        if (fireFailures.IsEmpty)
        {
            return;
        }

        for (int i = 0; i < fired.Count; i++)
        {
            TriggerFiredResult? result = fired[i];
            if (result is null)
            {
                continue;
            }

            if (result.TriggerFiredBundle is not null || result.IsDeclined || result.Exception is not null)
            {
                fireFailures.Clear(attempt[i].Key);
            }
        }
    }

    /// <summary>
    /// Stores <c>ERROR</c> each trigger whose fire has failed as many times in a row as
    /// <see cref="MaxConsecutiveFireFailures" /> allows, so that it is not acquired again (#3963).
    /// </summary>
    /// <remarks>
    /// <para>
    /// One short transaction per trigger under <see cref="SchedulerLock.TriggerAccess" />, once the batch
    /// has settled: the attempt the failure happened in was rolled back, so the park cannot be part of it.
    /// The row moves only from <c>ACQUIRED</c>, the reservation this batch still holds, so a trigger
    /// paused or deleted in the meantime is left as it is. The scheduler thread then releases the failed
    /// result as it releases any other, which deletes the reservation's fired row and leaves an
    /// <c>ERROR</c> row alone.
    /// </para>
    /// <para>
    /// A park that fails is logged, and the batch's results are returned all the same: the fires beside
    /// the failed one have committed, and their jobs are the scheduler's to run. The count is kept, so the
    /// trigger's next failure tries again.
    /// </para>
    /// </remarks>
    private async ValueTask ParkFailingTriggers(List<(TriggerKey TriggerKey, int Failures)> failing, CancellationToken cancellationToken)
    {
        foreach ((TriggerKey triggerKey, int failures) in failing)
        {
            bool parked;
            try
            {
                parked = await ExecuteInLocalTransactionLock(
                    SchedulerLock.TriggerAccess,
                    async conn =>
                    {
                        int updated = await Guarded(
                            () => Delegate.UpdateTriggerStateFromOtherState(conn, triggerKey, StoredTriggerState.Error, StoredTriggerState.Acquired, cancellationToken),
                            $"store trigger '{triggerKey}' ERROR").ConfigureAwait(false);

                        if (updated == 0)
                        {
                            return false;
                        }

                        // As for a job that will not load: raised once the ERROR has committed.
                        conn.NotifyAfterCommit((notifier, token) => notifier.NotifySchedulerListenersTriggerInError(triggerKey, token));
                        return true;
                    },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Logger.TriggerErrorStateUpdateFailed(e);
                continue;
            }

            fireFailures.Clear(triggerKey);
            if (parked)
            {
                Logger.FailingTriggerParkedInError(triggerKey, failures);
            }
        }
    }

    /// <summary>
    /// One attempt at a batch: every trigger fired in turn, in the attempt's transaction.
    /// </summary>
    /// <exception cref="TriggerFireFailedException">
    /// A fire failed for a reason a retry will not cure. The transaction wrapper rolls the attempt back,
    /// and <see cref="TriggersFired" /> runs the batch again without the trigger.
    /// </exception>
    /// <exception cref="JobPersistenceException">
    /// A fire failed transiently. The transaction wrapper retries the whole attempt.
    /// </exception>
    private async ValueTask<List<TriggerFiredResult>> FireBatch(
        ConnectionAndTransactionHolder conn,
        IReadOnlyList<IOperableTrigger> triggers,
        CancellationToken cancellationToken)
    {
        List<TriggerFiredResult> results = new(triggers.Count);

        for (int i = 0; i < triggers.Count; i++)
        {
            IOperableTrigger trigger = triggers[i];
            TriggerFiredResult result;
            try
            {
                // Clone so that trigger.Triggered() mutation doesn't affect retries
                var triggerCopy = (IOperableTrigger) trigger.Clone();
                result = await FireTrigger(conn, triggerCopy, prefetch: null, deferredWrites: null, cancellationToken).ConfigureAwait(false);
            }
            catch (JobPersistenceException jpe)
            {
                if (IsTransient(jpe))
                {
                    throw; // Let ExecuteInLocalTransactionLock retry the whole transaction
                }

                throw new TriggerFireFailedException(i, trigger.Key, jpe);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A cancellation is not caught: it is the caller asking to stop, which the transaction
                // wrapper reports as itself rather than as a trigger that failed to fire.
                if (IsTransient(ex))
                {
                    // Wrap as JobPersistenceException so outer retry mechanism can handle it
                    throw new JobPersistenceException("Transient error firing trigger: " + ex.Message, ex);
                }

                throw new TriggerFireFailedException(i, trigger.Key, ex);
            }

            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Asked when an attempt's commit reported a failure: whether the fires landed anyway, which is the
    /// case when any fired row this attempt reported fired is <c>EXECUTING</c>.
    /// </summary>
    private ValueTask<bool> ValidateFired(
        ConnectionAndTransactionHolder conn,
        List<TriggerFiredResult> result,
        CancellationToken cancellationToken)
    {
        return Guarded(
            async () =>
            {
                var acquired = await Delegate
                    .SelectFiredTriggerRecords(conn, new FiredTriggerQuery { InstanceId = InstanceId }, cancellationToken)
                    .ConfigureAwait(false);
                var executingTriggers = new HashSet<string>();
                foreach (FiredTriggerRecord ft in acquired)
                {
                    if (ft.FireInstanceState == StoredTriggerState.Executing)
                    {
                        executingTriggers.Add(ft.FireInstanceId);
                    }
                }

                foreach (TriggerFiredResult tr in result)
                {
                    if (tr.TriggerFiredBundle is not null &&
                        executingTriggers.Contains(tr.TriggerFiredBundle.Trigger.FireInstanceId!))
                    {
                        return true;
                    }
                }

                return false;
            },
            "validate trigger acquisition");
    }

    protected async ValueTask<TriggerFiredBundle?> TriggerFired(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        TriggerFiredResult result = await FireTrigger(conn, trigger, prefetch: null, deferredWrites: null, cancellationToken).ConfigureAwait(false);
        return result.TriggerFiredBundle;
    }

    /// <summary>
    /// Fires one acquired trigger inside the batch's transaction: <see cref="TriggerFiredResult.Fired" />
    /// with what to run, <see cref="TriggerFiredResult.NotFired" /> when it may not fire after all —
    /// <see cref="TriggerFiredResult.Blocked" /> when a running firing of its job holds it back — or
    /// <see cref="TriggerFiredResult.Declined" /> when its overlap policy settled it instead.
    /// </summary>
    /// <param name="conn">The batch's transaction.</param>
    /// <param name="trigger">A copy of the acquired trigger, which the fire advances.</param>
    /// <param name="prefetch">
    /// The headers and jobs a fire-on-acquire round read for all of its triggers at once, whose fires
    /// write their rows rather than update a reservation; <see langword="null" /> for the fire of a
    /// trigger acquired earlier, which reads its own.
    /// </param>
    /// <param name="deferredWrites">
    /// Where a round that writes its fires together collects this fire's writes instead of applying them;
    /// <see langword="null" /> to apply them here.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    private async ValueTask<TriggerFiredResult> FireTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        FireOnAcquirePrefetch? prefetch,
        List<TriggerFiredUpdate>? deferredWrites,
        CancellationToken cancellationToken)
    {
        IJobDetail? job;
        ICalendar? calendar = null;

        // Make sure trigger wasn't deleted, paused, or completed... No row at all means the trigger was
        // deleted, which is not a state it may fire from either. The header also carries the type
        // discriminator, which is what the write below would otherwise have gone back for, and its very
        // existence is the answer to "does this row exist" that the write used to ask separately.
        StoredTriggerHeader? header = prefetch is not null
            ? prefetch.Header(trigger.Key)
            : await Guarded(
                () => Delegate.SelectTriggerHeader(conn, trigger.Key, cancellationToken),
                "select trigger state").ConfigureAwait(false);

        if (header is null || header.State != StoredTriggerState.Acquired)
        {
            // BLOCKED is another fire of a job that disallows concurrent execution, which moved this
            // reservation out of reach when it started (#3926): that firing's end is what lets go of it.
            return header?.State == StoredTriggerState.Blocked ? TriggerFiredResult.Blocked : TriggerFiredResult.NotFired;
        }

        try
        {
            job = prefetch?.Jobs is not null
                ? prefetch.TakeJob(trigger.JobKey)
                : await GetJob(conn, trigger.JobKey, cancellationToken).ConfigureAwait(false);
            if (job is null)
            {
                return TriggerFiredResult.NotFired;
            }
        }
        catch (JobPersistenceException jpe)
        {
            // Settled here rather than by rolling the fire back: the trigger is stored ERROR so that it
            // is not acquired again, and the batch goes on and commits that. Answered as a failed result
            // rather than thrown, because a thrown failure is one the batch undoes (#3931); the
            // scheduler releases the trigger on the result, which leaves an ERROR row alone.
            Logger.JobRetrievalFailed(jpe);
            try
            {
                await Delegate.UpdateTriggerState(conn, trigger.Key, StoredTriggerState.Error, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception sqle)
            {
                // Nothing is settled then, and the transaction may be doomed: the batch treats the write
                // failure as it treats any other failed fire, retrying a transient one.
                Logger.TriggerErrorStateUpdateFailed(sqle);
                throw;
            }

            // Same as above: the trigger stops here and nothing else says so. Raised once the ERROR
            // state has committed, not from inside the transaction that may yet roll it back.
            TriggerKey key = trigger.Key;
            conn.NotifyAfterCommit((notifier, token) => notifier.NotifySchedulerListenersTriggerInError(key, token));
            return TriggerFiredResult.Failed(jpe);
        }

        // Cluster-safe check: prevent concurrent execution across nodes for
        // [DisallowConcurrentExecution] jobs by checking the FIRED_TRIGGERS table.
        // This runs under the TRIGGER_ACCESS lock, providing serialized access.
        // The current trigger's own fired record has JOB_NAME=null (set during
        // AcquireNextTrigger), or does not exist yet when it fires as it is acquired,
        // so it won't appear in the query results. Nor does the row of a fire earlier in
        // the same round whose writes are deferred, so the round answers for those itself.
        if (job.ConcurrentExecutionDisallowed)
        {
            bool alreadyExecuting = prefetch is not null && prefetch.FiredInRound(trigger.JobKey)
                || await Guarded(
                    () => Delegate.IsJobCurrentlyExecuting(conn, trigger.JobKey, cancellationToken),
                    $"check concurrent execution for job '{trigger.JobKey}'").ConfigureAwait(false);

            if (alreadyExecuting)
            {
                Logger.ConcurrentExecutionDeclined(trigger.Key, trigger.JobKey);
                return TriggerFiredResult.Blocked;
            }
        }

        if (trigger.CalendarName is not null)
        {
            calendar = await GetCalendar(conn, trigger.CalendarName, cancellationToken).ConfigureAwait(false);
            if (calendar is null)
            {
                Logger.TriggerReferencesMissingCalendar(trigger.Key, trigger.CalendarName);
                return TriggerFiredResult.NotFired;
            }
        }

        // The trigger's overlap policy, asked only when it can say anything: Skip and CancelPrevious,
        // for a job that lets concurrent firings through at all — one that does not is held back above —
        // and for a scheduled occurrence rather than a retry, which continues one already started.
        // Default, AllowAll and BufferOne ask nothing here, so the ordinary fire pays nothing for it.
        List<string>? superseded = null;
        if (trigger is TriggerBase { OverlapPolicy: OverlapPolicy.Skip or OverlapPolicy.CancelPrevious } overlapping
            && trigger.RetryAttempt == 0
            && !job.ConcurrentExecutionDisallowed)
        {
            superseded = [];
            if (await ApplyOverlapPolicy(conn, overlapping, job, calendar, superseded, cancellationToken).ConfigureAwait(false) is { } declined)
            {
                return declined;
            }
        }

        // The time this fire was scheduled for, captured before Triggered() moves the trigger on to the
        // one after it. The fired-trigger row records it, and it used to be read straight off the
        // trigger — which is why that row had to be written before Triggered() ran. Every write this
        // method makes now goes out together at the end, so what the writes need is taken here instead
        // of being expressed as an ordering constraint a batch could not honour.
        DateTimeOffset? scheduledFireTimeUtc = trigger.NextFireTimeUtc;

        // Auto-pin: when the preferred node is the "*" sentinel, claim the trigger by assigning this
        // node's instance id and flagging it as auto-claimed. When it is some OTHER node's id and
        // already auto-claimed, that node was stale or dead at acquisition time (the acquisition SQL
        // only releases another node's pin via the liveness fallback), so steal the pin — sticky
        // failover converges to a live node.
        // The write is a compare-and-swap against the values observed at acquire time, so a
        // concurrent change (an UpdateTriggerDetails re-pin or clear between acquisition and firing,
        // or ClusterRecover's reset to "*") wins over the claim instead of being clobbered by it.
        // Explicit pins (AUTO = false) are never re-pinned here.
        if (trigger is TriggerBase pinTrigger)
        {
            PreferredNode pin = pinTrigger.PreferredNode;
            string? rawPreferredNode = pin.StoredNode;
            bool rawPreferredNodeAuto = pin.StoredAutomatic;
            bool claimUnpinned = rawPreferredNode == StdAdoConstants.AutoPinSentinel;
            bool stealFromStaleNode = rawPreferredNode is not null
                && rawPreferredNodeAuto
                && rawPreferredNode != InstanceId;

            if (claimUnpinned || stealFromStaleNode)
            {
                PreferredNode claim = PreferredNode.ClaimedBy(InstanceId);
                int claimed = await Delegate.UpdateTriggerPreferredNodeConditional(
                    conn,
                    trigger.Key,
                    new PreferredNodeTransition { Expected = pin, New = claim },
                    cancellationToken).ConfigureAwait(false);
                if (claimed > 0)
                {
                    // Mirror the persisted value; not dirty — the row already holds it
                    pinTrigger.SetPreferredNode(claim, markDirty: false);
                }
                // else the pin changed concurrently: leave the concurrent value in place. The
                // in-memory value is stale but not dirty, so the store below will not write it
                // back; the next acquisition reloads the current value.
            }
        }

        // Read saved original fire time from trigger (populated by SelectTrigger from DB column). The
        // column is cleared as part of the write below, so that the recorded time does not survive the
        // firing that reports it.
        DateTimeOffset? scheduledFireTime = (trigger as TriggerBase)?.MisfiredFromFireTimeUtc;

        DateTimeOffset? prevFireTime = trigger.PreviousFireTimeUtc;

        // call triggered - to update the trigger's next-fire-time state. A trigger carrying a retry
        // attempt is being fired for a retry rather than for a scheduled occurrence, so it advances
        // past the retry instant without burning a count or moving its previous fire time - the same
        // dispatch on TriggerBase the misfire original fire time above uses.
        if (trigger.RetryAttempt > 0 && trigger is TriggerBase retryingTrigger)
        {
            retryingTrigger.RetryFired(calendar);
        }
        else
        {
            trigger.Triggered(calendar);
        }

        StoredTriggerState state2 = StoredTriggerState.Waiting;
        bool force = true;

        if (job.ConcurrentExecutionDisallowed)
        {
            state2 = StoredTriggerState.Blocked;
            force = false;
        }
        else if (trigger.OverlapPolicy == OverlapPolicy.BufferOne)
        {
            // Held back while this firing runs, as a [DisallowConcurrentExecution] job's triggers are,
            // but this trigger alone: nothing of it is acquired until the completion lets go of it.
            state2 = StoredTriggerState.Blocked;
            force = false;
        }

        if (!trigger.NextFireTimeUtc.HasValue)
        {
            state2 = StoredTriggerState.Complete;
            force = true;
        }

        // What AddTrigger would still do for this trigger, and no more. The rest of it is answered
        // already: the row exists and its type is known from the header read above, the job was read
        // above, and CheckBlockedState is a no-op for every state this path can reach — it only speaks
        // for WAITING and PAUSED, and a job that disallows concurrent execution is storing BLOCKED or
        // COMPLETE by now.
        if (!force)
        {
            state2 = await ApplyPausedGroupState(conn, trigger.Key.Group, trigger.JobKey.Group, state2, cancellationToken).ConfigureAwait(false);
        }

        TriggerFiredUpdate update = new()
        {
            Trigger = trigger,
            JobDetail = job,
            NewState = state2,
            StoredTriggerType = header.TriggerType,
            ScheduledFireTimeUtc = scheduledFireTimeUtc,
            ClearMisfireOriginalFireTime = scheduledFireTime.HasValue,
            BlockJobTriggers = job.ConcurrentExecutionDisallowed,
            FiredOnAcquire = prefetch is not null,
        };

        if (deferredWrites is not null)
        {
            // Written with the rest of the round's fires once every one of them is decided. The one thing
            // a later fire of the round decides on that this fire writes is whether the job is executing,
            // and the round remembers that itself. Every other write is to the fire's own rows.
            if (job.ConcurrentExecutionDisallowed)
            {
                prefetch!.MarkFiredInRound(trigger.JobKey);
            }

            deferredWrites.Add(update);
        }
        else
        {
            await Guarded(
                () => Delegate.ApplyTriggerFired(conn, update, cancellationToken),
                $"record the fire of trigger '{trigger.Key}' for '{trigger.JobKey}' job").ConfigureAwait(false);
        }

        job.JobDataMap.ClearDirtyFlag();

        return TriggerFiredResult.Fired(new TriggerFiredBundle
        {
            JobDetail = job,
            Trigger = trigger,
            Calendar = calendar,
            Recovering = trigger.Key.Group == SchedulerConstants.DefaultRecoveryGroup,
            FireTimeUtc = timeProvider.GetUtcNow(),
            ScheduledFireTimeUtc = scheduledFireTime ?? trigger.PreviousFireTimeUtc,
            PreviousFireTimeUtc = prevFireTime,
            NextFireTimeUtc = trigger.NextFireTimeUtc,
            SupersededFireInstanceIds = superseded is { Count: > 0 } ? superseded : null,
        });
    }
}
