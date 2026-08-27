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

public abstract partial class AdoJobStoreBase
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
    public virtual ValueTask<List<IOperableTrigger>> AcquireNextTriggers(
        TriggerAcquisitionRequest request,
        CancellationToken cancellationToken = default)
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
            conn => AcquireNextTrigger(conn, request, cancellationToken),
            (conn, result) => Guarded(
                async () =>
                {
                    var acquired = await Delegate.SelectFiredTriggerRecords(conn, new FiredTriggerQuery { InstanceId = InstanceId }, cancellationToken).ConfigureAwait(false);
                    var fireInstanceIds = new HashSet<string>();
                    foreach (FiredTriggerRecord ft in acquired)
                    {
                        fireInstanceIds.Add(ft.FireInstanceId!);
                    }
                    foreach (IOperableTrigger tr in result)
                    {
                        if (fireInstanceIds.Contains(tr.FireInstanceId))
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
    /// silent one.
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
    protected ValueTask<List<IOperableTrigger>> AcquireNextTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerAcquisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            async () =>
            {
                List<IOperableTrigger> acquiredTriggers = [];
                HashSet<JobKey> acquiredJobKeysForNoConcurrentExec = [];
                const int MaxDoLoopRetry = 3;
                int currentLoopCount = 0;

                do
                {
                    currentLoopCount++;
                    // Built inside the loop, so each retry asks again and sees the time it retried at.
                    TriggerAcquisitionCriteria criteria = CreateAcquisitionCriteria(request);
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
                        return acquiredTriggers;
                    }

                    DateTimeOffset batchEnd = request.NoLaterThan;

                    foreach (var result in results)
                    {
                        // The delegate was told which job types this node will not run, and did not say
                        // it enforces that itself. Dropping the candidate here — on the name the
                        // acquisition read already returned — is what keeps the promise; doing it before
                        // the read below is what keeps it from costing a round trip and a type
                        // resolution per candidate (#3443).
                        if (excludedJobTypeNames is not null && excludedJobTypeNames.Contains(result.JobTypeName))
                        {
                            continue; // next trigger
                        }

                        TriggerKey triggerKey = result.TriggerKey;

                        // If our trigger is no longer available, try a new one.
                        var nextTrigger = await GetTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false);
                        if (nextTrigger is null)
                        {
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
                            continue;
                        }

                        // The same question JobDetailImpl answers, answered the same way: the attribute is
                        // inherited from an interface as readily as from a base class, and this loop used to
                        // consult the non-walking check and so let an interface-inherited one fire twice.
                        if (JobTypeInformation.GetOrCreate(jobType).ConcurrentExecutionDisallowed)
                        {
                            if (!acquiredJobKeysForNoConcurrentExec.Add(nextTrigger.JobKey))
                            {
                                continue; // next trigger
                            }

                            // Cluster-safe check: skip if job is already executing on another node
                            if (await Delegate.IsJobCurrentlyExecuting(conn, nextTrigger.JobKey, cancellationToken).ConfigureAwait(false))
                            {
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
                            Logger.TriggerHasNoNextFireTime(nextTrigger.Key);
                            continue;
                        }

                        if (nextFireTimeUtc > batchEnd)
                        {
                            break;
                        }

                        // We now have a acquired trigger, let's add to return list.
                        // If our trigger was no longer in the expected state, try a new one.
                        int rowsUpdated = await Delegate.UpdateTriggerStateFromOtherStateWithNextFireTime(conn, triggerKey, StoredTriggerState.Acquired, StoredTriggerState.Waiting, nextFireTimeUtc.Value, cancellationToken).ConfigureAwait(false);
                        if (rowsUpdated <= 0)
                        {
                            // Not worth a warning: the row was no longer WAITING, which is what losing
                            // the race to another node looks like, and in a cluster that is the ordinary
                            // outcome of two nodes reaching for the same batch. Logging it would produce
                            // noise proportional to how well the cluster is sharing its work.
                            continue; // next trigger
                        }
                        nextTrigger.FireInstanceId = GetFiredTriggerRecordId();
                        await Delegate.InsertFiredTrigger(conn, nextTrigger, StoredTriggerState.Acquired, null, cancellationToken).ConfigureAwait(false);

                        if (acquiredTriggers.Count == 0)
                        {
                            var now = timeProvider.GetUtcNow();
                            var nextFireTime = nextFireTimeUtc.Value;
                            var max = now > nextFireTime ? now : nextFireTime;

                            batchEnd = max + request.TimeWindow;
                        }

                        acquiredTriggers.Add(nextTrigger);
                    }

                    // if we didn't end up with any trigger to fire from that first
                    // batch, try again for another batch. We allow with a max retry count.
                    if (acquiredTriggers.Count == 0 && currentLoopCount < MaxDoLoopRetry)
                    {
                        continue;
                    }

                    // We are done with the while loop.
                    break;
                } while (true);

                // Return the acquired trigger list
                return acquiredTriggers;
            },
            "acquire next trigger");
    }

    public ValueTask<List<TriggerFiredResult>> TriggersFired(IReadOnlyCollection<IOperableTrigger> triggers, CancellationToken cancellationToken = default)
    {
        return ExecuteInLocalTransactionLock(
            SchedulerLock.TriggerAccess,
            async conn =>
            {
                List<TriggerFiredResult> results = new(triggers.Count);

                foreach (IOperableTrigger trigger in triggers)
                {
                    TriggerFiredResult result;
                    try
                    {
                        // Clone so that trigger.Triggered() mutation doesn't affect retries
                        var triggerCopy = (IOperableTrigger) trigger.Clone();
                        var bundle = await TriggerFired(conn, triggerCopy, cancellationToken).ConfigureAwait(false);
                        result = bundle is null ? TriggerFiredResult.NotFired : TriggerFiredResult.Fired(bundle);
                    }
                    catch (JobPersistenceException jpe)
                    {
                        if (IsTransient(jpe))
                        {
                            throw; // Let ExecuteInLocalTransactionLock retry the whole transaction
                        }
                        Logger.JobPersistenceExceptionCaught(jpe.Message, jpe);
                        result = TriggerFiredResult.Failed(jpe);
                    }
                    catch (Exception ex)
                    {
                        if (IsTransient(ex))
                        {
                            // Wrap as JobPersistenceException so outer retry mechanism can handle it
                            throw new JobPersistenceException("Transient error firing trigger: " + ex.Message, ex);
                        }
                        Logger.ExceptionCaught(ex.Message, ex);
                        result = TriggerFiredResult.Failed(ex);
                    }

                    results.Add(result);
                }

                return results;
            },
            (conn, result) => Guarded(
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
                            executingTriggers.Contains(tr.TriggerFiredBundle.Trigger.FireInstanceId))
                        {
                            return true;
                        }
                    }

                    return false;
                },
                "validate trigger acquisition"),
            cancellationToken: cancellationToken);
    }

    protected async ValueTask<TriggerFiredBundle?> TriggerFired(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        IJobDetail? job;
        ICalendar? calendar = null;

        // Make sure trigger wasn't deleted, paused, or completed... No row at all means the trigger was
        // deleted, which is not a state it may fire from either. The header also carries the type
        // discriminator, which is what the write below would otherwise have gone back for, and its very
        // existence is the answer to "does this row exist" that the write used to ask separately.
        StoredTriggerHeader? header = await Guarded(
            () => Delegate.SelectTriggerHeader(conn, trigger.Key, cancellationToken),
            "select trigger state").ConfigureAwait(false);

        if (header is null || header.State != StoredTriggerState.Acquired)
        {
            return null;
        }

        try
        {
            job = await GetJob(conn, trigger.JobKey, cancellationToken).ConfigureAwait(false);
            if (job is null)
            {
                return null;
            }
        }
        catch (JobPersistenceException jpe)
        {
            try
            {
                Logger.JobRetrievalFailed(jpe);
                await Delegate.UpdateTriggerState(conn, trigger.Key, StoredTriggerState.Error, cancellationToken).ConfigureAwait(false);

                // Same as above: the trigger stops here and nothing else says so.
                await signaler.NotifySchedulerListenersTriggerInError(trigger.Key, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception sqle)
            {
                Logger.TriggerErrorStateUpdateFailed(sqle);
            }
            throw;
        }

        // Cluster-safe check: prevent concurrent execution across nodes for
        // [DisallowConcurrentExecution] jobs by checking the FIRED_TRIGGERS table.
        // This runs under the TRIGGER_ACCESS lock, providing serialized access.
        // The current trigger's own fired record has JOB_NAME=null (set during
        // AcquireNextTrigger) so it won't appear in the query results.
        if (job.ConcurrentExecutionDisallowed)
        {
            bool alreadyExecuting = await Guarded(
                () => Delegate.IsJobCurrentlyExecuting(conn, trigger.JobKey, cancellationToken),
                $"check concurrent execution for job '{trigger.JobKey}'").ConfigureAwait(false);

            if (alreadyExecuting)
            {
                Logger.ConcurrentExecutionDeclined(trigger.Key, trigger.JobKey);
                return null;
            }
        }

        if (trigger.CalendarName is not null)
        {
            calendar = await GetCalendar(conn, trigger.CalendarName, cancellationToken).ConfigureAwait(false);
            if (calendar is null)
            {
                Logger.TriggerReferencesMissingCalendar(trigger.Key, trigger.CalendarName);
                return null;
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

        // call triggered - to update the trigger's next-fire-time state...
        trigger.Triggered(calendar);

        StoredTriggerState state2 = StoredTriggerState.Waiting;
        bool force = true;

        if (job.ConcurrentExecutionDisallowed)
        {
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
            state2 = await ApplyPausedTriggerGroupState(conn, trigger.Key.Group, state2, cancellationToken).ConfigureAwait(false);
        }

        await Guarded(
            () => Delegate.ApplyTriggerFired(conn, new TriggerFiredUpdate
            {
                Trigger = trigger,
                JobDetail = job,
                NewState = state2,
                StoredTriggerType = header.TriggerType,
                ScheduledFireTimeUtc = scheduledFireTimeUtc,
                ClearMisfireOriginalFireTime = scheduledFireTime.HasValue,
                BlockJobTriggers = job.ConcurrentExecutionDisallowed,
            }, cancellationToken),
            $"record the fire of trigger '{trigger.Key}' for '{trigger.JobKey}' job").ConfigureAwait(false);

        job.JobDataMap.ClearDirtyFlag();

        return new TriggerFiredBundle
        {
            JobDetail = job,
            Trigger = trigger,
            Calendar = calendar,
            Recovering = trigger.Key.Group == SchedulerConstants.DefaultRecoveryGroup,
            FireTimeUtc = timeProvider.GetUtcNow(),
            ScheduledFireTimeUtc = scheduledFireTime ?? trigger.PreviousFireTimeUtc,
            PreviousFireTimeUtc = prevFireTime,
            NextFireTimeUtc = trigger.NextFireTimeUtc,
        };
    }
}
