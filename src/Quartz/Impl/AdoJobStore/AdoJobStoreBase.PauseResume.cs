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

internal abstract partial class AdoJobStoreBase
{
    /// <summary>
    /// Folds the paused state of the groups a trigger belongs to — its own, and its job's — into the
    /// state about to be stored for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three things can pause a trigger that is being stored, and all three are asked about here: the
    /// trigger's own group, the all-groups marker <c>PauseAll</c> writes, and the group the
    /// trigger's job is in. The last of those is what makes
    /// <see cref="IScheduler.PauseJobGroups" />' promise true — a pause recorded against a job group
    /// is imposed on the jobs added to it afterwards, which is the whole reason an equality matcher
    /// pauses a group that holds nothing yet.
    /// </para>
    /// <para>
    /// A group that is paused only because everything is paused has no row of its own until something
    /// stores a trigger into it, so the wildcard is materialized into an explicit row here — otherwise
    /// resuming that one group later would have nothing to remove. A job-group pause materializes
    /// nothing: it is recorded where it was made, and writing a trigger-group row for it would leave
    /// the trigger group paused after the job group was resumed.
    /// </para>
    /// </remarks>
    /// <param name="conn">The DB connection.</param>
    /// <param name="triggerGroup">The group the trigger belongs to.</param>
    /// <param name="jobGroup">The group the trigger's job belongs to.</param>
    /// <param name="state">The state the caller intends to store.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// <see cref="StoredTriggerState.Paused" /> when one of those groups is paused and the intended
    /// state is one a pause supersedes, and the intended state otherwise.
    /// </returns>
    private async ValueTask<StoredTriggerState> ApplyPausedGroupState(
        ConnectionAndTransactionHolder conn,
        string triggerGroup,
        string jobGroup,
        StoredTriggerState state,
        CancellationToken cancellationToken)
    {
        bool shouldBePaused = await Delegate.IsTriggerGroupPaused(conn, triggerGroup, cancellationToken).ConfigureAwait(false);

        if (!shouldBePaused)
        {
            shouldBePaused = await Delegate.IsTriggerGroupPaused(conn, AdoConstants.AllGroupsPaused, cancellationToken).ConfigureAwait(false);

            if (shouldBePaused)
            {
                // The row is written as it always was, with no record of its own: the pause-all's
                // record stays on the marker, and GetTriggerGroupPause answers it for a row like this.
                await Delegate.InsertPausedTriggerGroup(conn, triggerGroup, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                shouldBePaused = await Delegate.IsJobGroupPaused(conn, jobGroup, cancellationToken).ConfigureAwait(false);
            }
        }

        if (shouldBePaused && state is StoredTriggerState.Waiting or StoredTriggerState.Acquired)
        {
            return StoredTriggerState.Paused;
        }

        return state;
    }

    /// <summary>
    /// Get the current state of the identified <see cref="ITrigger" />.
    /// </summary>
    /// <seealso cref="TriggerState.Normal" />
    /// <seealso cref="TriggerState.Paused" />
    /// <seealso cref="TriggerState.Complete" />
    /// <seealso cref="TriggerState.Error" />
    /// <seealso cref="TriggerState.None" />
    /// <seealso cref="TriggerState.Blocked" />
    /// <seealso cref="TriggerState.Executing" />
    public ValueTask<TriggerState> GetTriggerState(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        // no locks necessary for read...
        return ExecuteWithoutLock(conn => GetTriggerState(conn, triggerKey, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Gets the state of the trigger.
    /// </summary>
    /// <param name="conn">The conn.</param>
    /// <param name="triggerKey">The key identifying the trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    protected ValueTask<TriggerState> GetTriggerState(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            async () =>
            {
                TriggerExecutionState stored = await Delegate
                    .SelectTriggerStateWithExecuting(conn, triggerKey, cancellationToken).ConfigureAwait(false);

                return TriggerStateMapping.ToTriggerState(stored.State, stored.IsExecuting);
            },
            $"determine state of trigger ({triggerKey})");
    }

    public async ValueTask<bool> ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return await ExecuteInLock(
            SchedulerLock.TriggerAccess,
            conn => ResetTriggerFromErrorState(conn, triggerKey, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resets the whole set inside one lock and one transaction rather than one per key.
    /// </summary>
    public ValueTask<List<TriggerKey>> ResetTriggersFromErrorState(
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInLock(SchedulerLock.TriggerAccess, async conn =>
        {
            List<TriggerKey> reset = new List<TriggerKey>(triggerKeys.Count);
            foreach (TriggerKey triggerKey in triggerKeys)
            {
                if (await ResetTriggerFromErrorState(conn, triggerKey, cancellationToken).ConfigureAwait(false))
                {
                    reset.Add(triggerKey);
                }
            }

            return reset;
        }, cancellationToken);
    }

    private ValueTask<bool> ResetTriggerFromErrorState(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            async () =>
            {
                StoredTriggerState newState = StoredTriggerState.Waiting;

                if (await Delegate.IsTriggerGroupPaused(conn, triggerKey.Group, cancellationToken).ConfigureAwait(false))
                {
                    newState = StoredTriggerState.Paused;
                }

                int updated = await Delegate.UpdateTriggerStateFromOtherState(conn, triggerKey, newState, StoredTriggerState.Error, cancellationToken).ConfigureAwait(false);
                if (updated == 0)
                {
                    // no trigger with the key, or it was not in the error state
                    return false;
                }

                // A row in error that still names a parent is a continuation parked because its parent
                // was deleted — a released one names none — so its fire time is the one it had while
                // it was waiting, which is to say none it can act on. Resetting it means running it now,
                // at the later of now and its own start time: the same instant a release would have
                // given it; and, like a release, it forgets the parent, so a later error and reset of
                // the same trigger keeps its schedule. The statement touches only a row that names a
                // parent, so an ordinary trigger's reset writes nothing extra.
                await Delegate.ResetContinuationFireTime(conn, triggerKey, timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);

                Logger.TriggerResetFromError(triggerKey, newState);
                return true;
            },
            $"reset from error state of trigger ({triggerKey})");
    }

    /// <summary>
    /// Pause the <see cref="ITrigger" /> with the given name.
    /// </summary>
    public async ValueTask<bool> PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return await ExecuteInLock(SchedulerLock.TriggerAccess, conn => PauseTrigger(conn, triggerKey, pause: null, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Details that <see cref="PauseDetails.SaysNothing">say nothing</see> make the reasonless pause,
    /// through the same driver-delegate members and statements; only a pause with something to record
    /// goes through the delegate's pause members.
    /// </remarks>
    public async ValueTask<bool> PauseTriggerWith(TriggerKey triggerKey, PauseDetails? details, CancellationToken cancellationToken = default)
    {
        PauseInfo? pause = PauseDetails.Record(details, timeProvider.GetUtcNow());
        return await ExecuteInLock(SchedulerLock.TriggerAccess, conn => PauseTrigger(conn, triggerKey, pause, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Pauses the whole set inside one lock and one transaction rather than one per key, and in a
    /// fixed number of statements rather than two per key.
    /// </summary>
    public ValueTask<List<TriggerKey>> PauseTriggers(
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInLock(
            SchedulerLock.TriggerAccess,
            conn => PauseTriggers(conn, triggerKeys, pause: null, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Pauses a set of triggers: one read of their stored states, then one statement per transition
    /// the set actually needs — at most two, whatever the size of the set.
    /// </summary>
    /// <remarks>
    /// The transitions are the ones <see cref="PauseTrigger(ConnectionAndTransactionHolder, TriggerKey, PauseInfo, CancellationToken)" />
    /// makes: a waiting or acquired trigger becomes paused, a blocked one becomes paused-blocked, and
    /// anything else is left alone. The updates name the old states as well, so a trigger the lock-free
    /// acquisition path moved from waiting to acquired between the read and the write is still paused,
    /// and one that left a pausable state entirely is not reported as paused. A
    /// <paramref name="pause" /> is written by the statement that moves each row; without one, the
    /// statements are the reasonless ones.
    /// </remarks>
    /// <returns>The keys that were paused, in the order they were given, each named once.</returns>
    protected ValueTask<List<TriggerKey>> PauseTriggers(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<TriggerKey> triggerKeys,
        PauseInfo? pause,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            async () =>
            {
                if (triggerKeys.Count == 0)
                {
                    return new List<TriggerKey>();
                }

                List<StoredTriggerHeader> headers = await Delegate.SelectStoredTriggerHeaders(conn, triggerKeys, cancellationToken).ConfigureAwait(false);

                List<TriggerKey> pausable = [];
                List<TriggerKey> blocked = [];
                foreach (StoredTriggerHeader header in headers)
                {
                    if (header.State is StoredTriggerState.Waiting or StoredTriggerState.Acquired)
                    {
                        pausable.Add(header.Key);
                    }
                    else if (header.State == StoredTriggerState.Blocked)
                    {
                        blocked.Add(header.Key);
                    }
                }

                HashSet<TriggerKey> paused = new(pausable.Count + blocked.Count);

                if (pausable.Count > 0)
                {
                    if (pause is null)
                    {
                        await Delegate.UpdateTriggerStatesFromOtherStates(conn, pausable, StoredTriggerState.Paused,
                            [StoredTriggerState.Waiting, StoredTriggerState.Acquired], cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await Delegate.PauseTriggerStates(conn, pausable, StoredTriggerState.Paused,
                            [StoredTriggerState.Waiting, StoredTriggerState.Acquired], pause, cancellationToken).ConfigureAwait(false);
                    }

                    paused.UnionWith(pausable);
                }

                if (blocked.Count > 0)
                {
                    if (pause is null)
                    {
                        await Delegate.UpdateTriggerStatesFromOtherStates(conn, blocked, StoredTriggerState.PausedBlocked,
                            [StoredTriggerState.Blocked], cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await Delegate.PauseTriggerStates(conn, blocked, StoredTriggerState.PausedBlocked,
                            [StoredTriggerState.Blocked], pause, cancellationToken).ConfigureAwait(false);
                    }

                    paused.UnionWith(blocked);
                }

                return InRequestedOrder(triggerKeys, paused);
            },
            "pause triggers");
    }

    /// <summary>
    /// The keys of <paramref name="requested" /> that are in <paramref name="selected" />, in the order
    /// they were requested and each named once — which is what the per-key loops these set operations
    /// replace returned.
    /// </summary>
    private static List<TriggerKey> InRequestedOrder(IReadOnlyCollection<TriggerKey> requested, HashSet<TriggerKey> selected)
    {
        List<TriggerKey> ordered = new(selected.Count);
        foreach (TriggerKey key in requested)
        {
            if (selected.Remove(key))
            {
                ordered.Add(key);
            }
        }

        return ordered;
    }

    /// <summary>
    /// Pause the <see cref="ITrigger" /> with the given name.
    /// </summary>
    /// <returns>
    /// <see langword="true" /> if the trigger existed in a pausable state and was moved into the
    /// paused state by this call, carrying <paramref name="pause" /> when there is one.
    /// </returns>
    /// <remarks>
    /// Without a <paramref name="pause" />, the statements are exactly the reasonless ones, so a delegate
    /// that overrides <see cref="IDriverDelegate.UpdateTriggerState" /> still decides how a trigger is
    /// paused.
    /// </remarks>
    protected ValueTask<bool> PauseTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        PauseInfo? pause,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            async () =>
            {
                StoredTriggerState oldState = await Delegate.SelectTriggerState(conn, triggerKey, cancellationToken).ConfigureAwait(false);

                if (oldState is StoredTriggerState.Waiting or StoredTriggerState.Acquired)
                {
                    return pause is null
                        ? await Delegate.UpdateTriggerState(conn, triggerKey, StoredTriggerState.Paused, cancellationToken).ConfigureAwait(false) > 0
                        : await Delegate.PauseTriggerStates(conn, [triggerKey], StoredTriggerState.Paused,
                            [StoredTriggerState.Waiting, StoredTriggerState.Acquired], pause, cancellationToken).ConfigureAwait(false) > 0;
                }

                if (oldState == StoredTriggerState.Blocked)
                {
                    return pause is null
                        ? await Delegate.UpdateTriggerState(conn, triggerKey, StoredTriggerState.PausedBlocked, cancellationToken).ConfigureAwait(false) > 0
                        : await Delegate.PauseTriggerStates(conn, [triggerKey], StoredTriggerState.PausedBlocked,
                            [StoredTriggerState.Blocked], pause, cancellationToken).ConfigureAwait(false) > 0;
                }

                // missing, already paused, or in a state that cannot be paused
                return false;
            },
            $"pause trigger '{triggerKey}'");
    }

    /// <summary>
    /// Pause the <see cref="IJob" /> with the given name - by
    /// pausing all of its current <see cref="ITrigger" />s.
    /// </summary>
    /// <seealso cref="ResumeJob(JobKey,CancellationToken)" />
    public async ValueTask<bool> PauseJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return await ExecuteInLock(SchedulerLock.TriggerAccess, conn => PauseJob(conn, jobKey, pause: null, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Details that <see cref="PauseDetails.SaysNothing">say nothing</see> make the reasonless pause.
    /// </remarks>
    public async ValueTask<bool> PauseJobWith(JobKey jobKey, PauseDetails? details, CancellationToken cancellationToken = default)
    {
        PauseInfo? pause = PauseDetails.Record(details, timeProvider.GetUtcNow());
        return await ExecuteInLock(SchedulerLock.TriggerAccess, conn => PauseJob(conn, jobKey, pause, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Pauses the whole set inside one lock and one transaction rather than one per key.
    /// </summary>
    public ValueTask<List<JobKey>> PauseJobs(
        IReadOnlyCollection<JobKey> jobKeys,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInLock(SchedulerLock.TriggerAccess, async conn =>
        {
            List<JobKey> paused = new List<JobKey>(jobKeys.Count);
            foreach (JobKey jobKey in jobKeys)
            {
                if (await PauseJob(conn, jobKey, pause: null, cancellationToken).ConfigureAwait(false))
                {
                    paused.Add(jobKey);
                }
            }

            return paused;
        }, cancellationToken);
    }

    /// <summary>
    /// Pause the <see cref="IJob" /> with the given key - by pausing all of its current
    /// <see cref="ITrigger" />s.
    /// </summary>
    /// <returns>
    /// <see langword="true" /> if the job exists — including a job that currently has no triggers.
    /// </returns>
    protected async ValueTask<bool> PauseJob(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        PauseInfo? pause,
        CancellationToken cancellationToken = default)
    {
        if (!await Exists(conn, jobKey, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        // The keys, not the triggers: pausing decides on the stored state, and building each trigger
        // would read its type table for a schedule nothing here looks at.
        List<TriggerKey> triggerKeys = await GetTriggerKeysForJob(conn, jobKey, cancellationToken).ConfigureAwait(false);
        await PauseTriggers(conn, triggerKeys, pause, cancellationToken).ConfigureAwait(false);

        return true;
    }

    private ValueTask<List<TriggerKey>> GetTriggerKeysForJob(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken)
    {
        return Guarded(
            () => Delegate.SelectTriggerKeysForJob(conn, jobKey, cancellationToken),
            "obtain trigger keys for job");
    }

    private ValueTask<List<TriggerKey>> GetTriggerKeysForJobs(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<JobKey> jobKeys,
        CancellationToken cancellationToken)
    {
        return Guarded(
            () => Delegate.SelectTriggerKeysForJobs(conn, jobKeys, cancellationToken),
            "obtain trigger keys for jobs");
    }

    /// <summary>
    /// Pause all of the <see cref="IJob" />s in the given
    /// group - by pausing all of their <see cref="ITrigger" />s.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each matched group is recorded in PAUSED_JOB_GRPS, so the pause outlives the process, reaches
    /// the other nodes of a cluster, can be listed — and binds what is added to the group next: a
    /// trigger stored for a job in a recorded group is born PAUSED, which
    /// <see cref="ApplyPausedGroupState" /> is what does. That is why an equality matcher records a
    /// group holding no job at all; a pattern is not a group, so only the groups it matched are.
    /// </para>
    /// <para>
    /// <c>RAMJobStore</c> keeps one refinement this store does not: a job resumed individually inside
    /// a still-paused group stays resumed there, where here resuming one job's triggers leaves the
    /// group's pause to be imposed on them again the next time they are stored.
    /// </para>
    /// </remarks>
    /// <seealso cref="ResumeJobGroups" />
    public ValueTask<List<string>> PauseJobGroups(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        return PauseMatchingJobGroups(matcher, pause: null, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Details that <see cref="PauseDetails.SaysNothing">say nothing</see> make the reasonless pause.
    /// </remarks>
    public ValueTask<List<string>> PauseJobGroupsWith(GroupMatcher<JobKey> matcher, PauseDetails? details, CancellationToken cancellationToken = default)
    {
        return PauseMatchingJobGroups(matcher, PauseDetails.Record(details, timeProvider.GetUtcNow()), cancellationToken);
    }

    private ValueTask<List<string>> PauseMatchingJobGroups(GroupMatcher<JobKey> matcher, PauseInfo? pause, CancellationToken cancellationToken)
    {
        return ExecuteInLock(SchedulerLock.TriggerAccess, async conn =>
        {
            List<JobKey> jobKeys = await GetJobNames(conn, matcher, cancellationToken).ConfigureAwait(false);

            // Every matched job's triggers in one read, and then one pause for the whole set — where
            // this used to walk the jobs and pause a trigger at a time.
            List<TriggerKey> triggerKeys = await GetTriggerKeysForJobs(conn, jobKeys, cancellationToken).ConfigureAwait(false);
            await PauseTriggers(conn, triggerKeys, pause, cancellationToken).ConfigureAwait(false);

            var groupNames = new HashSet<string>();
            foreach (JobKey jobKey in jobKeys)
            {
                groupNames.Add(jobKey.Group);
            }

            // An equality matcher names one group, so it pauses that group whether or not any job
            // is in it yet. Anything else is a pattern, and only the groups it matched are
            // recorded: a pattern is not a group, and a listing must never hand a caller back a
            // name no job can belong to.
            if (StringOperator.Equality.Equals(matcher.CompareWithOperator))
            {
                groupNames.Add(matcher.CompareToValue);
            }

            await RecordPausedJobGroups(conn, groupNames, pause, cancellationToken).ConfigureAwait(false);

            return new List<string>(groupNames);
        }, cancellationToken);
    }

    /// <summary>
    /// Writes a PAUSED_JOB_GRPS row for every group that does not already have one: one read of which
    /// of them are paused, then the missing rows together, rather than a check and an insert per group.
    /// </summary>
    /// <remarks>
    /// The check-then-insert is safe across a cluster because every caller holds the trigger-access
    /// lock for the whole operation, which is the same guarantee <see cref="PauseTriggerGroup" />
    /// relies on. The table's primary key is the backstop: were the lock ever bypassed, the second
    /// insert would fail rather than leave the group paused twice.
    /// </remarks>
    private ValueTask RecordPausedJobGroups(
        ConnectionAndTransactionHolder conn,
        HashSet<string> groupNames,
        PauseInfo? pause,
        CancellationToken cancellationToken)
    {
        return Guarded(
            async () =>
            {
                if (groupNames.Count == 0)
                {
                    return;
                }

                List<string> alreadyPaused = await Delegate.SelectPausedJobGroups(conn, groupNames, cancellationToken).ConfigureAwait(false);

                HashSet<string> paused = new(alreadyPaused, StringComparer.Ordinal);
                List<string> missing = [];
                foreach (string group in groupNames)
                {
                    if (!paused.Contains(group))
                    {
                        missing.Add(group);
                    }
                }

                if (missing.Count > 0)
                {
                    if (pause is null)
                    {
                        await Delegate.InsertPausedJobGroups(conn, missing, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await Delegate.InsertJobGroupPauses(conn, missing, pause, cancellationToken).ConfigureAwait(false);
                    }
                }
            },
            "pause job groups");
    }

    /// <summary>
    /// Determines if a Trigger for the given job should be blocked.
    /// State can only transition to StatePausedBlocked/StateBlocked from
    /// StatePaused/StateWaiting respectively.
    /// </summary>
    /// <returns>StatePausedBlocked, StateBlocked, or the currentState. </returns>
    protected async ValueTask<StoredTriggerState> CheckBlockedState(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        StoredTriggerState currentState,
        CancellationToken cancellationToken = default)
    {
        // State can only transition to BLOCKED from PAUSED or WAITING.
        if (currentState != StoredTriggerState.Waiting && currentState != StoredTriggerState.Paused)
        {
            return currentState;
        }

        return await Guarded(
            async () =>
            {
                var firedTriggers = await Delegate.SelectFiredTriggerRecords(conn, new FiredTriggerQuery { Job = jobKey }, cancellationToken).ConfigureAwait(false);

                if (firedTriggers.Count > 0)
                {
                    // The row's own state is deliberately not consulted. A fired-trigger row of a job
                    // that disallows concurrent execution means that job is reserved or running
                    // somewhere, whichever state the row is in, and that is the whole of what blocks a
                    // sibling. A row left behind by a node that died is not this predicate's business
                    // either: ClusterRecover deletes it and unblocks the job, and
                    // RecoverStaleAcquiredTriggers does the same for a stale reservation of this node's
                    // own. Reading the state here would only let a sibling through while a row that is
                    // about to be cleaned up says the job is still busy.
                    FiredTriggerRecord firedTrigger = firedTriggers[0];
                    if (firedTrigger.JobDisallowsConcurrentExecution)
                    {
                        return StoredTriggerState.Paused == currentState ? StoredTriggerState.PausedBlocked : StoredTriggerState.Blocked;
                    }
                }

                return currentState;
            },
            "determine if trigger should be in a blocked state '" + jobKey + "'").ConfigureAwait(false);
    }

    /// <summary>
    /// Keeps a trigger that is being resumed held back while a firing of its own still runs, when that is
    /// what held it back before it was paused.
    /// </summary>
    /// <remarks>
    /// A <c>PAUSED_BLOCKED</c> trigger whose job is not what blocks it was held back by an overlap policy
    /// — a <see cref="OverlapPolicy.BufferOne" /> firing of it, or a
    /// <see cref="OverlapPolicy.CancelPrevious" /> firing on another node — and that firing's completion
    /// is what lets go of it. Resuming it to <c>WAITING</c> while the firing runs would let the next one
    /// start beside it.
    /// </remarks>
    private async ValueTask<StoredTriggerState> CheckOverlapHeldState(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        StoredTriggerState pausedState,
        StoredTriggerState resumedState,
        CancellationToken cancellationToken)
    {
        if (pausedState != StoredTriggerState.PausedBlocked || resumedState != StoredTriggerState.Waiting)
        {
            return resumedState;
        }

        bool running = await Delegate.IsTriggerCurrentlyExecuting(conn, triggerKey, cancellationToken).ConfigureAwait(false);
        return running ? StoredTriggerState.Blocked : resumedState;
    }

    public async ValueTask<bool> ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return await ExecuteInLock(SchedulerLock.TriggerAccess, conn => ResumeTrigger(conn, triggerKey, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resumes the whole set inside one lock and one transaction rather than one per key, and in a
    /// number of statements that grows with the number of distinct jobs rather than with the number of
    /// triggers.
    /// </summary>
    public ValueTask<List<TriggerKey>> ResumeTriggers(
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInLock(
            SchedulerLock.TriggerAccess,
            conn => ResumeTriggers(conn, triggerKeys, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Resumes a set of triggers: one read of their stored headers, one blocked-state question per
    /// distinct job rather than per trigger, and one statement per state transition the set turns out
    /// to need — at most four, whatever the size of the set.
    /// </summary>
    /// <remarks>
    /// A trigger that missed fire times while paused still takes its own misfire-recovery write, which
    /// recomputes that trigger's schedule and so cannot be expressed as a set operation. Everything
    /// else about the resume is the same decision
    /// <see cref="ResumeTrigger(ConnectionAndTransactionHolder, TriggerKey, CancellationToken)" />
    /// makes.
    /// </remarks>
    /// <returns>The keys that were resumed, in the order they were given, each named once.</returns>
    protected ValueTask<List<TriggerKey>> ResumeTriggers(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            async () =>
            {
                if (triggerKeys.Count == 0)
                {
                    return new List<TriggerKey>();
                }

                List<StoredTriggerHeader> headers = await Delegate.SelectStoredTriggerHeaders(conn, triggerKeys, cancellationToken).ConfigureAwait(false);

                DateTimeOffset now = timeProvider.GetUtcNow();
                // The answer depends on the job and on nothing else this loop changes, so it is asked
                // once per job instead of once per trigger of that job.
                Dictionary<JobKey, StoredTriggerState> unpausedStateByJob = [];
                Dictionary<TriggerStateTransition, List<TriggerKey>> transitions = [];
                HashSet<TriggerKey> resumed = [];

                foreach (StoredTriggerHeader header in headers)
                {
                    if (header.NextFireTimeUtc is null || header.NextFireTimeUtc == DateTimeOffset.MinValue)
                    {
                        continue;
                    }

                    if (header.State is not StoredTriggerState.Paused and not StoredTriggerState.PausedBlocked)
                    {
                        // not paused, nothing to resume
                        continue;
                    }

                    if (!unpausedStateByJob.TryGetValue(header.JobKey, out StoredTriggerState newState))
                    {
                        newState = await CheckBlockedState(conn, header.JobKey, StoredTriggerState.Waiting, cancellationToken).ConfigureAwait(false);
                        unpausedStateByJob[header.JobKey] = newState;
                    }

                    // Per trigger rather than per job, and so not cached: what holds it back is a firing
                    // of its own.
                    newState = await CheckOverlapHeldState(conn, header.Key, header.State, newState, cancellationToken).ConfigureAwait(false);

                    if (schedulerRunning && header.NextFireTimeUtc.Value < now
                        && await UpdateMisfiredTrigger(conn, header.Key, newState, forceState: true, cancellationToken).ConfigureAwait(false))
                    {
                        resumed.Add(header.Key);
                        continue;
                    }

                    TriggerStateTransition transition = new(header.State, newState);
                    if (!transitions.TryGetValue(transition, out List<TriggerKey>? keys))
                    {
                        keys = [];
                        transitions[transition] = keys;
                    }

                    keys.Add(header.Key);
                }

                foreach (KeyValuePair<TriggerStateTransition, List<TriggerKey>> entry in transitions)
                {
                    await Delegate.UpdateTriggerStatesFromOtherStates(conn, entry.Value, entry.Key.To,
                        [entry.Key.From], cancellationToken).ConfigureAwait(false);
                    resumed.UnionWith(entry.Value);
                }

                List<TriggerKey> ordered = InRequestedOrder(triggerKeys, resumed);

                // One statement for the whole set, whatever each trigger's own transition was.
                await Delegate.ClearTriggerPauses(conn, ordered, cancellationToken).ConfigureAwait(false);

                return ordered;
            },
            "resume triggers");
    }

    /// <summary>
    /// Resume (un-pause) the <see cref="ITrigger" /> with the
    /// given name.
    /// </summary>
    /// <remarks>
    /// If the <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </remarks>
    /// <returns>
    /// <see langword="true" /> if the trigger existed in a paused state and was resumed by this
    /// call.
    /// </returns>
    protected ValueTask<bool> ResumeTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            async () =>
            {
                StoredTriggerHeader? status = await Delegate.SelectTriggerHeader(conn, triggerKey, cancellationToken).ConfigureAwait(false);

                if (status?.NextFireTimeUtc is null || status.NextFireTimeUtc == DateTimeOffset.MinValue)
                {
                    return false;
                }

                if (status.State is not StoredTriggerState.Paused and not StoredTriggerState.PausedBlocked)
                {
                    // not paused, nothing to resume
                    return false;
                }

                bool blocked = status.State == StoredTriggerState.PausedBlocked;

                StoredTriggerState newState = await CheckBlockedState(conn, status.JobKey, StoredTriggerState.Waiting, cancellationToken).ConfigureAwait(false);
                newState = await CheckOverlapHeldState(conn, triggerKey, status.State, newState, cancellationToken).ConfigureAwait(false);

                bool resumed = false;

                if (schedulerRunning && status.NextFireTimeUtc.Value < timeProvider.GetUtcNow())
                {
                    resumed = await UpdateMisfiredTrigger(conn, triggerKey, newState, forceState: true, cancellationToken).ConfigureAwait(false);
                }

                if (!resumed)
                {
                    StoredTriggerState pausedState = blocked ? StoredTriggerState.PausedBlocked : StoredTriggerState.Paused;
                    resumed = await Delegate.UpdateTriggerStateFromOtherState(conn, triggerKey, newState, pausedState, cancellationToken).ConfigureAwait(false) > 0;
                }

                if (resumed)
                {
                    // A resumed trigger forgets why it was paused, so the next pause starts from nothing.
                    await Delegate.ClearTriggerPauses(conn, [triggerKey], cancellationToken).ConfigureAwait(false);
                }

                return resumed;
            },
            "resume trigger '" + triggerKey + "'");
    }

    /// <summary>
    /// Resume (un-pause) the <see cref="IJob" /> with the
    /// given name.
    /// </summary>
    /// <remarks>
    /// If any of the <see cref="IJob"/>'s <see cref="ITrigger" /> s missed one
    /// or more fire-times, then the <see cref="ITrigger" />'s misfire
    /// instruction will be applied.
    /// </remarks>
    /// <seealso cref="PauseJob(JobKey,CancellationToken)" />
    public async ValueTask<bool> ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return await ExecuteInLock(SchedulerLock.TriggerAccess, conn => ResumeJob(conn, jobKey, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resumes the whole set inside one lock and one transaction rather than one per key.
    /// </summary>
    public ValueTask<List<JobKey>> ResumeJobs(
        IReadOnlyCollection<JobKey> jobKeys,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInLock(SchedulerLock.TriggerAccess, async conn =>
        {
            List<JobKey> resumed = new List<JobKey>(jobKeys.Count);
            foreach (JobKey jobKey in jobKeys)
            {
                if (await ResumeJob(conn, jobKey, cancellationToken).ConfigureAwait(false))
                {
                    resumed.Add(jobKey);
                }
            }

            return resumed;
        }, cancellationToken);
    }

    /// <summary>
    /// Resume (un-pause) the <see cref="IJob" /> with the given key.
    /// </summary>
    /// <returns>
    /// <see langword="true" /> if the job exists — including a job that currently has no triggers.
    /// </returns>
    protected async ValueTask<bool> ResumeJob(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        if (!await Exists(conn, jobKey, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        List<TriggerKey> triggerKeys = await GetTriggerKeysForJob(conn, jobKey, cancellationToken).ConfigureAwait(false);
        await ResumeTriggers(conn, triggerKeys, cancellationToken).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Resume (un-pause) all of the <see cref="IJob" />s in
    /// the given group.
    /// </summary>
    /// <remarks>
    /// If any of the <see cref="IJob" /> s had <see cref="ITrigger" /> s that
    /// missed one or more fire-times, then the <see cref="ITrigger" />'s
    /// misfire instruction will be applied.
    /// </remarks>
    /// <seealso cref="PauseJobGroups" />
    public ValueTask<List<string>> ResumeJobGroups(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        return ExecuteInLock(SchedulerLock.TriggerAccess, async conn =>
        {
            // Forget the pause of every group the matcher selects, whatever operator it carries —
            // a prefix pause recorded a row per matched group, so a resume that only understood
            // equality would leave them paused forever.
            await Guarded(
                () => Delegate.DeletePausedJobGroup(conn, matcher, cancellationToken),
                "resume job groups").ConfigureAwait(false);

            List<JobKey> jobKeys = await GetJobNames(conn, matcher, cancellationToken).ConfigureAwait(false);

            List<TriggerKey> triggerKeys = await GetTriggerKeysForJobs(conn, jobKeys, cancellationToken).ConfigureAwait(false);
            await ResumeTriggers(conn, triggerKeys, cancellationToken).ConfigureAwait(false);

            var groupNames = new HashSet<string>();
            foreach (JobKey jobKey in jobKeys)
            {
                groupNames.Add(jobKey.Group);
            }

            return groupNames.ToList();
        }, cancellationToken);
    }

    /// <summary>
    /// Pause all of the <see cref="ITrigger" />s in the given group.
    /// </summary>
    /// <seealso cref="ResumeTriggerGroups(Quartz.GroupMatcher{Quartz.TriggerKey}, CancellationToken)" />
    public ValueTask<List<string>> PauseTriggerGroups(
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInLock(
            SchedulerLock.TriggerAccess,
            conn => PauseTriggerGroup(conn, matcher, pause: null, cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Details that <see cref="PauseDetails.SaysNothing">say nothing</see> make the reasonless pause.
    /// </remarks>
    public ValueTask<List<string>> PauseTriggerGroupsWith(
        GroupMatcher<TriggerKey> matcher,
        PauseDetails? details,
        CancellationToken cancellationToken = default)
    {
        PauseInfo? pause = PauseDetails.Record(details, timeProvider.GetUtcNow());
        return ExecuteInLock(
            SchedulerLock.TriggerAccess,
            conn => PauseTriggerGroup(conn, matcher, pause, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Pause all of the <see cref="ITrigger" />s in the given group.
    /// </summary>
    /// <remarks>
    /// With a <paramref name="pause" />, the triggers it moves and the group rows it writes carry it, and
    /// a group that already has a row keeps the record it had. Without one, the statements are exactly
    /// the reasonless ones.
    /// </remarks>
    protected ValueTask<List<string>> PauseTriggerGroup(
        ConnectionAndTransactionHolder conn,
        GroupMatcher<TriggerKey> matcher,
        PauseInfo? pause,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            async () =>
            {
                if (pause is null)
                {
                    await Delegate.UpdateTriggerGroupStateFromOtherStates(conn, matcher, StoredTriggerState.Paused,
                        [StoredTriggerState.Acquired, StoredTriggerState.Waiting], cancellationToken).ConfigureAwait(false);

                    await Delegate.UpdateTriggerGroupStateFromOtherState(conn, matcher, StoredTriggerState.PausedBlocked,
                        StoredTriggerState.Blocked, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await Delegate.PauseTriggerGroupStates(conn, matcher, StoredTriggerState.Paused,
                        [StoredTriggerState.Acquired, StoredTriggerState.Waiting], pause, cancellationToken).ConfigureAwait(false);

                    await Delegate.PauseTriggerGroupStates(conn, matcher, StoredTriggerState.PausedBlocked,
                        [StoredTriggerState.Blocked], pause, cancellationToken).ConfigureAwait(false);
                }

                var groups = new List<string>(await Delegate.SelectTriggerGroupNames(conn, matcher, cancellationToken).ConfigureAwait(false));

                // make sure to account for an exact group match for a group that doesn't yet exist
                StringOperator op = matcher.CompareWithOperator;
                if (op.Equals(StringOperator.Equality) && !groups.Contains(matcher.CompareToValue))
                {
                    groups.Add(matcher.CompareToValue);
                }

                foreach (string group in groups)
                {
                    if (!await Delegate.IsTriggerGroupPaused(conn, group, cancellationToken).ConfigureAwait(false))
                    {
                        await InsertPausedTriggerGroup(conn, group, pause, cancellationToken).ConfigureAwait(false);
                    }
                }

                return groups;
            },
            "pause trigger group '" + matcher + "'");
    }

    public ValueTask<List<string>> ResumeTriggerGroups(
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInLock(
            SchedulerLock.TriggerAccess, conn => ResumeTriggerGroups(conn, matcher, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Resume (un-pause) all of the <see cref="ITrigger" />s
    /// in the given group.
    /// <para>
    /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </para>
    /// </summary>
    protected ValueTask<List<string>> ResumeTriggerGroups(
        ConnectionAndTransactionHolder conn,
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        // "resume trigger group", where this used to say "pause trigger group": the message was a copy
        // of PauseTriggerGroup's, and told an operator the opposite of what had just failed.
        return Guarded(
            async () =>
            {
                await Delegate.DeletePausedTriggerGroup(conn, matcher, cancellationToken).ConfigureAwait(false);

                List<TriggerKey> keys = await Delegate.SelectTriggerKeysInGroup(conn, matcher, cancellationToken).ConfigureAwait(false);
                await ResumeTriggers(conn, keys, cancellationToken).ConfigureAwait(false);

                var groups = new HashSet<string>();
                foreach (TriggerKey key in keys)
                {
                    groups.Add(key.Group);
                }

                return new List<string>(groups);
            },
            "resume trigger group '" + matcher + "'");
    }

    public async ValueTask PauseAll(CancellationToken cancellationToken = default)
    {
        await ExecuteInLock(SchedulerLock.TriggerAccess, conn => PauseAll(conn, pause: null, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Details that <see cref="PauseDetails.SaysNothing">say nothing</see> make the reasonless pause.
    /// </remarks>
    public async ValueTask PauseAllWith(PauseDetails? details, CancellationToken cancellationToken = default)
    {
        PauseInfo? pause = PauseDetails.Record(details, timeProvider.GetUtcNow());
        await ExecuteInLock(SchedulerLock.TriggerAccess, conn => PauseAll(conn, pause, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a paused-trigger-group row: the reasonless insert without a <paramref name="pause" />, and
    /// the one that records it with one.
    /// </summary>
    private ValueTask<int> InsertPausedTriggerGroup(
        ConnectionAndTransactionHolder conn,
        string groupName,
        PauseInfo? pause,
        CancellationToken cancellationToken)
    {
        return pause is null
            ? Delegate.InsertPausedTriggerGroup(conn, groupName, cancellationToken)
            : Delegate.InsertTriggerGroupPause(conn, groupName, pause, cancellationToken);
    }

    /// <summary>
    /// Pause all triggers - equivalent of calling <see cref="PauseTriggerGroups" />
    /// on every group.
    /// <para>
    /// When <see cref="ResumeAll(CancellationToken)" /> is called (to un-pause), trigger misfire
    /// instructions WILL be applied.
    /// </para>
    /// </summary>
    /// <seealso cref="ResumeAll(CancellationToken)" />
    protected async ValueTask PauseAll(
        ConnectionAndTransactionHolder conn,
        PauseInfo? pause,
        CancellationToken cancellationToken = default)
    {
        // Every group at once. Asking for the group names and then pausing each of them by name issued
        // the same statements a group at a time, and the any-group matcher already means all of them.
        await PauseTriggerGroup(conn, GroupMatcher<TriggerKey>.AnyGroup(), pause, cancellationToken).ConfigureAwait(false);

        await Guarded(
            async () =>
            {
                // The marker carries the record too, so a group it pauses later — by a trigger being
                // stored into it, whose row is written with no record of its own — answers it.
                if (!await Delegate.IsTriggerGroupPaused(conn, AdoConstants.AllGroupsPaused, cancellationToken).ConfigureAwait(false))
                {
                    await InsertPausedTriggerGroup(conn, AdoConstants.AllGroupsPaused, pause, cancellationToken).ConfigureAwait(false);
                }
            },
            "pause all trigger groups").ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask<PauseInfo?> GetTriggerPause(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triggerKey);

        // No lock for a read, as GetTriggerState takes none.
        return ExecuteWithoutLock(
            conn => Guarded(
                () => Delegate.SelectTriggerPause(conn, triggerKey, cancellationToken),
                $"read the pause of trigger '{triggerKey}'"),
            cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<PauseInfo?> GetTriggerGroupPause(string groupName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groupName);

        return ExecuteWithoutLock(
            conn => Guarded(
                async () =>
                {
                    PauseInfo? own = await Delegate.SelectTriggerGroupPause(conn, groupName, cancellationToken).ConfigureAwait(false);
                    if (own is not null || string.Equals(groupName, AdoConstants.AllGroupsPaused, StringComparison.Ordinal))
                    {
                        return own;
                    }

                    // A row with no record of its own is the one a trigger stored under the pause-all
                    // marker writes, which is written as it always was; the marker holds the record.
                    return await Delegate.IsTriggerGroupPaused(conn, groupName, cancellationToken).ConfigureAwait(false)
                        ? await Delegate.SelectTriggerGroupPause(conn, AdoConstants.AllGroupsPaused, cancellationToken).ConfigureAwait(false)
                        : null;
                },
                $"read the pause of trigger group '{groupName}'"),
            cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<PauseInfo?> GetJobGroupPause(string groupName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groupName);

        return ExecuteWithoutLock(
            conn => Guarded(
                () => Delegate.SelectJobGroupPause(conn, groupName, cancellationToken),
                $"read the pause of job group '{groupName}'"),
            cancellationToken);
    }

    /// <summary>
    /// Resume (un-pause) all triggers - equivalent of calling <see cref="ResumeTriggerGroups(Quartz.GroupMatcher{Quartz.TriggerKey}, CancellationToken)" />
    /// on every group.
    /// </summary>
    /// <remarks>
    /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </remarks>
    /// <seealso cref="PauseAll(CancellationToken)" />
    public async ValueTask ResumeAll(CancellationToken cancellationToken = default)
    {
        await ExecuteInLock(SchedulerLock.TriggerAccess, conn => ResumeAll(conn, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resume (un-pause) all triggers - equivalent of calling <see cref="ResumeTriggerGroups(Quartz.GroupMatcher{Quartz.TriggerKey}, CancellationToken)" />
    /// on every group.
    /// <para>
    /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </para>
    /// </summary>
    /// <seealso cref="PauseAll(CancellationToken)" />
    protected async ValueTask ResumeAll(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        // Every group at once, for the reason PauseAll takes the any-group matcher: naming each group
        // in turn issued the same statements a group at a time.
        await ResumeTriggerGroups(conn, GroupMatcher<TriggerKey>.AnyGroup(), cancellationToken).ConfigureAwait(false);

        await Guarded(
            async () =>
            {
                // Every paused group, not just the all-groups marker: the loop above only visits groups the
                // trigger table knows about, so a group that was paused while empty would keep its row and
                // go on pausing whatever was added to it after a resume-all resumed everything.
                await Delegate.DeletePausedTriggerGroup(conn, GroupMatcher<TriggerKey>.AnyGroup(), cancellationToken).ConfigureAwait(false);

                // Resume-all means everything, job groups included — otherwise a paused job group would
                // survive it and go on reporting itself paused with nothing left to resume it.
                await Delegate.DeletePausedJobGroup(conn, GroupMatcher<JobKey>.AnyGroup(), cancellationToken).ConfigureAwait(false);
            },
            "resume all trigger groups").ConfigureAwait(false);
    }
}
