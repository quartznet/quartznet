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
    /// Store the given <see cref="IJobDetail" /> and <see cref="IOperableTrigger" />.
    /// </summary>
    /// <param name="job">Job to be stored.</param>
    /// <param name="trigger">Trigger to be stored.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public async ValueTask ScheduleJob(
        IJobDetail job,
        IOperableTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        await ExecuteInLock<object?>(LockOnInsert ? SchedulerLock.TriggerAccess : null, async conn =>
        {
            // Before the job is written, not only inside AddTrigger: a store running in a transaction
            // the application owns — or in none — cannot count on a rollback to take the job back out.
            await EnsureContinuationParentExists(conn, trigger, alsoStored: null, cancellationToken).ConfigureAwait(false);

            await AddJob(conn, job, false, cancellationToken).ConfigureAwait(false);
            await AddTrigger(conn, trigger, job, false, StoredTriggerState.Waiting, false, false, continuationParentChecked: true, cancellationToken).ConfigureAwait(false);
            return null;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stores the given <see cref="IJobDetail" />.
    /// </summary>
    /// <param name="job">The <see cref="IJobDetail" /> to be stored.</param>
    /// <param name="options">How to store it; see <see cref="IJobStore.AddJob" />.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public async ValueTask AddJob(IJobDetail job, AddJobOptions options = default, CancellationToken cancellationToken = default)
    {
        await ExecuteInLock(
            LockOnInsert || options.Replace ? SchedulerLock.TriggerAccess : null,
            conn => AddJob(conn, job, options.Replace, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary> <para>
    /// Insert or update a job.
    /// </para>
    /// </summary>
    protected async ValueTask AddJob(
        ConnectionAndTransactionHolder conn,
        IJobDetail newJob,
        bool replace,
        CancellationToken cancellationToken = default)
    {
        // Outside the guard below, so that a failure to read the row is reported as the read it was
        // rather than as a failure to store.
        bool existingJob = await JobExists(conn, newJob.Key, cancellationToken).ConfigureAwait(false);

        // ObjectAlreadyExistsException is the one failure this method raises on purpose, and the one a
        // caller catches by type to tell "already there" from "the store broke"; Guarded leaves it as
        // itself.
        await Guarded(
            async () =>
            {
                if (existingJob)
                {
                    if (!replace)
                    {
                        Throw.ObjectAlreadyExistsException(newJob);
                    }
                    if (await Delegate.UpdateJobDetail(conn, newJob, cancellationToken).ConfigureAwait(false) > 0)
                    {
                        return;
                    }
                }
                if (await Delegate.InsertJobDetail(conn, newJob, cancellationToken).ConfigureAwait(false) < 1)
                {
                    throw new JobPersistenceException("Couldn't store job. Insert failed.");
                }
            },
            "store job").ConfigureAwait(false);
    }

    /// <summary>
    /// Store the given <see cref="ITrigger" />.
    /// </summary>
    /// <param name="trigger">The <see cref="ITrigger" /> to be stored.</param>
    /// <param name="options">How to store it; see <see cref="IJobStore.AddTrigger" />.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <exception cref="ObjectAlreadyExistsException">
    /// if a <see cref="ITrigger" /> with the same name/group already exists, and
    /// <see cref="AddTriggerOptions.Replace" /> was not asked for.
    /// </exception>
    public async ValueTask AddTrigger(IOperableTrigger trigger, AddTriggerOptions options = default, CancellationToken cancellationToken = default)
    {
        if (!options.Paused)
        {
            await ExecuteInLock(
                LockOnInsert || options.Replace ? SchedulerLock.TriggerAccess : null,
                conn => AddTrigger(conn, trigger, null, options.Replace, StoredTriggerState.Waiting, false, false, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        PauseInfo? pause = PauseDetails.Record(options.Pause, timeProvider.GetUtcNow());
        await ExecuteInLock(
            LockOnInsert || options.Replace ? SchedulerLock.TriggerAccess : null,
            conn => AddTrigger(conn, trigger, null, options.Replace, StoredTriggerState.Waiting, false, false, continuationParentChecked: false, cancellationToken, storePaused: true, pause: pause),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Store the given trigger, deciding under <c>TRIGGER_ACCESS</c> what becomes of one already stored
    /// under its key.
    /// </summary>
    /// <remarks>
    /// Every mode but <see cref="TriggerConflict.Throw" /> takes the lock whatever <c>LockOnInsert</c>
    /// says: the read that decides and the write that follows must be one step, or two nodes keeping the
    /// same key would both find it absent and one of them would fail on the primary key instead of being
    /// told the other's trigger was kept.
    /// </remarks>
    /// <param name="trigger">The trigger to store.</param>
    /// <param name="onConflict">What to do when a trigger is stored under the same key.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public ValueTask<ScheduleTriggerResult> StoreTrigger(IOperableTrigger trigger, TriggerConflict onConflict, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        TriggerConflictResolution.RequireDefined(onConflict, nameof(onConflict));

        return ExecuteInLock(
            LockOnInsert || onConflict != TriggerConflict.Throw ? SchedulerLock.TriggerAccess : null,
            async conn =>
            {
                StoredTriggerHeader? existing = await Delegate.SelectTriggerHeader(conn, trigger.Key, cancellationToken).ConfigureAwait(false);

                if (existing is not null)
                {
                    if (onConflict == TriggerConflict.Throw)
                    {
                        Throw.ObjectAlreadyExistsException(trigger);
                    }

                    if (TriggerConflictResolution.KeepsExisting(onConflict, existing.NextFireTimeUtc, trigger.NextFireTimeUtc))
                    {
                        return new ScheduleTriggerResult(existing.NextFireTimeUtc!.Value, ScheduleOutcome.Kept);
                    }
                }

                await AddTrigger(
                    conn,
                    trigger,
                    job: null,
                    replace: existing is not null,
                    StoredTriggerState.Waiting,
                    forceState: false,
                    recovering: false,
                    continuationParentChecked: false,
                    cancellationToken,
                    knownToExist: existing is not null).ConfigureAwait(false);

                return new ScheduleTriggerResult(
                    TriggerConflictResolution.FireTimeOf(trigger),
                    existing is null ? ScheduleOutcome.Created : ScheduleOutcome.Replaced);
            },
            cancellationToken);
    }

    /// <summary>
    /// Insert or update a trigger.
    /// </summary>
    protected async ValueTask AddTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger newTrigger,
        IJobDetail? job,
        bool replace,
        StoredTriggerState state,
        bool forceState,
        bool recovering,
        CancellationToken cancellationToken = default)
    {
        await AddTrigger(conn, newTrigger, job, replace, state, forceState, recovering, continuationParentChecked: false, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="AddTrigger(ConnectionAndTransactionHolder, IOperableTrigger, IJobDetail, bool, StoredTriggerState, bool, bool, CancellationToken)" />
    /// <param name="conn">The unit of work.</param>
    /// <param name="newTrigger">The trigger to store.</param>
    /// <param name="job">The trigger's job, when the caller already holds it.</param>
    /// <param name="replace">Whether an existing trigger of the same key may be overwritten.</param>
    /// <param name="state">The state to store it in, before a pause, a blocked job or a continuation says otherwise.</param>
    /// <param name="forceState">Whether <paramref name="state" /> is stored as given.</param>
    /// <param name="recovering">Whether this is a recovery trigger, which is never blocked.</param>
    /// <param name="continuationParentChecked">
    /// Whether the caller has already made sure a continuation's parent exists — the batch add, which
    /// looks across the batch as well as the store, so that a continuation stored before its parent in
    /// the same call is not refused.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <param name="knownToExist">
    /// Whether the caller has just read the key in this transaction, so the row need not be looked for
    /// again; <see langword="null" /> when it has not.
    /// </param>
    /// <param name="storePaused">
    /// Whether the trigger is stored paused rather than waiting — <see cref="AddTriggerOptions.Paused" />
    /// — so that it is never acquirable between being stored and being paused.
    /// </param>
    /// <param name="pause">The record of that pause, or <see langword="null" /> for the reasonless one.</param>
    /// <param name="asRead">
    /// For a replace that writes back a row the caller read and changed: the row as it was read, which
    /// the write is held to. A row a firing round moved in between is left alone and this answers
    /// <see langword="false" />, for the caller to read it again (#4067). <see langword="null" /> for a
    /// replace that is meant to overwrite whatever the row holds, which is every other caller.
    /// </param>
    /// <returns>
    /// Whether the trigger was written: inserted, or updated over the row it was meant to replace.
    /// </returns>
    private async ValueTask<bool> AddTrigger(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger newTrigger,
        IJobDetail? job,
        bool replace,
        StoredTriggerState state,
        bool forceState,
        bool recovering,
        bool continuationParentChecked,
        CancellationToken cancellationToken,
        bool? knownToExist = null,
        bool storePaused = false,
        PauseInfo? pause = null,
        RowAsRead? asRead = null)
    {
        bool existingTrigger = knownToExist ?? await TriggerExists(conn, newTrigger.Key, cancellationToken).ConfigureAwait(false);

        if (existingTrigger && !replace)
        {
            Throw.ObjectAlreadyExistsException(newTrigger);
        }

        // A trigger that waits for another one's firing is stored awaiting, whatever else is true of
        // it: a paused group and a blocked job are decided again when its parent releases it, and
        // neither is a reason to make it schedulable now. Skipped when the caller forces a state,
        // which is the calendar-update path writing back the state the row already holds.
        bool awaiting = !forceState && !newTrigger.Continuation.IsNone;
        if (awaiting)
        {
            state = StoredTriggerState.Awaiting;

            // In this transaction and before anything is written, so a refused continuation leaves the
            // store as it was — the job stored beside it and the row it would have replaced included.
            if (!continuationParentChecked)
            {
                await EnsureContinuationParentExists(conn, newTrigger, alsoStored: null, cancellationToken).ConfigureAwait(false);
            }
        }

        // Inserted paused rather than paused afterwards, so no acquisition ever sees it waiting. The
        // blocked check below still makes it paused-blocked while its job runs.
        bool paused = storePaused && !forceState && !awaiting;
        if (paused)
        {
            state = StoredTriggerState.Paused;
        }

        bool written = await Guarded(
            async () =>
            {
                if (!forceState && !awaiting)
                {
                    state = await ApplyPausedGroupState(conn, newTrigger.Key.Group, newTrigger.JobKey.Group, state, cancellationToken).ConfigureAwait(false);
                }

                if (job is null)
                {
                    job = await GetJob(conn, newTrigger.JobKey, cancellationToken).ConfigureAwait(false);
                }
                if (job is null)
                {
                    Throw.JobPersistenceException($"The job ({newTrigger.JobKey}) referenced by the trigger does not exist.");
                }
                if (job.ConcurrentExecutionDisallowed && !recovering && !awaiting)
                {
                    state = await CheckBlockedState(conn, job.Key, state, cancellationToken).ConfigureAwait(false);
                }
                if (existingTrigger)
                {
                    // Read back only when something below needs the row as it stands.
                    bool overlapUnsettledCheck = newTrigger is TriggerBase { OverlapPolicy: OverlapPolicy.CancelPrevious };
                    IOperableTrigger? existingTrig = newTrigger.PreviousFireTimeUtc is null || overlapUnsettledCheck
                        ? await Delegate.SelectTrigger(conn, newTrigger.Key, cancellationToken).ConfigureAwait(false)
                        : null;

                    // Preserve PreviousFireTimeUtc from the existing trigger when replacing,
                    // so that context.PreviousFireTimeUtc is not lost on application restart (#1834)
                    if (newTrigger.PreviousFireTimeUtc is null)
                    {
                        var prevFireTime = existingTrig?.PreviousFireTimeUtc;
                        if (prevFireTime is not null)
                        {
                            newTrigger.PreviousFireTimeUtc = prevFireTime;
                        }
                    }

                    // A trigger given CancelPrevious while a firing of it that started under another
                    // policy runs is unsettled until none does; see TriggerBase.OverlapPolicyUnsettled.
                    if (newTrigger is TriggerBase replacing)
                    {
                        replacing.OverlapPolicyUnsettled = overlapUnsettledCheck
                            && existingTrig is TriggerBase stored
                            && await IsOverlapPolicyUnsettled(conn, stored, OverlapPolicy.CancelPrevious, cancellationToken).ConfigureAwait(false);
                    }

                    int updated = asRead is { } read
                        ? await Delegate.UpdateTriggerIfUnchanged(conn, newTrigger, state, job, read.NextFireTimeUtc, cancellationToken).ConfigureAwait(false)
                        : await Delegate.UpdateTrigger(conn, newTrigger, state, job, cancellationToken).ConfigureAwait(false);

                    if (updated > 0)
                    {
                        return true;
                    }

                    if (asRead is not null)
                    {
                        // The row is not the one the caller read: a firing round that took no lock fired
                        // it in between (#4067), or its last firing's completion deleted it (#3863). Either
                        // way the edit was made to a row that is gone, and the caller decides on the one
                        // that is there now.
                        return false;
                    }

                    // The row went between the read that found it and this write: the firing it was
                    // running completed without the lock and deleted it, which is the one way a row
                    // disappears under a holder of TRIGGER_ACCESS (#3863). The trigger is stored as the
                    // new one it now is, as AddJob stores a job whose row went the same way.
                }

                if (newTrigger is TriggerBase inserting)
                {
                    // A trigger that was not there has no firing running.
                    inserting.OverlapPolicyUnsettled = false;
                }

                await Delegate.InsertTrigger(conn, newTrigger, state, job, cancellationToken).ConfigureAwait(false);
                return true;
            },
            $"store trigger '{newTrigger.Key}' for '{newTrigger.JobKey}' job").ConfigureAwait(false);

        if (paused && written)
        {
            await RecordStoredPause(conn, newTrigger.Key, state, pause, existingTrigger, cancellationToken).ConfigureAwait(false);
        }

        return written;
    }

    /// <summary>
    /// A trigger's row as an editor read it, which the editor's write back is held to: its fire time,
    /// which every fire, misfire, release and replace of the row moves (#4067).
    /// </summary>
    private readonly record struct RowAsRead(DateTimeOffset? NextFireTimeUtc);

    /// <summary>
    /// How many times an editor applies its edit again to a row a firing moved under it before it gives
    /// up. A trigger firing continuously could otherwise hold an edit off for good; one that fires this
    /// often while being edited is a configuration to report, not to wait out.
    /// </summary>
    private const int MaxEditAttempts = 8;

    /// <summary>
    /// Writes the record of a trigger just stored paused, in the transaction that stored it.
    /// </summary>
    /// <remarks>
    /// The insert and update statements name no pause column, so the record is the statement a pause
    /// with a reason makes, moving the row from the state it was stored in to that same state. A reasonless
    /// pause records nothing, so it writes nothing — except over a row it replaced, whose columns may
    /// still hold an earlier pause's record, which would otherwise be read as this one's.
    /// </remarks>
    private ValueTask RecordStoredPause(
        ConnectionAndTransactionHolder conn,
        TriggerKey key,
        StoredTriggerState state,
        PauseInfo? pause,
        bool replaced,
        CancellationToken cancellationToken)
    {
        return Guarded(
            async () =>
            {
                if (pause is not null)
                {
                    await Delegate.PauseTriggerStates(conn, [key], state, [state], pause, cancellationToken).ConfigureAwait(false);
                }
                else if (replaced)
                {
                    await Delegate.ClearTriggerPauses(conn, [key], cancellationToken).ConfigureAwait(false);
                }
            },
            $"record the pause of trigger '{key}'");
    }

    /// <summary>
    /// Refuses a trigger whose continuation waits for a trigger this store does not hold.
    /// </summary>
    /// <remarks>
    /// Such a trigger would wait for a firing that can never happen — a misspelled parent, or a
    /// one-shot parent that has already fired and been deleted — and a row held in
    /// <c>AWAITING</c> with nothing to release it is one nobody notices.
    /// </remarks>
    /// <param name="conn">The unit of work the trigger is being stored in.</param>
    /// <param name="trigger">The trigger about to be stored.</param>
    /// <param name="alsoStored">
    /// Keys being stored in the same operation, which count as present; <see langword="null" /> when
    /// there are none.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    private async ValueTask EnsureContinuationParentExists(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        HashSet<TriggerKey>? alsoStored,
        CancellationToken cancellationToken)
    {
        if (trigger.Continuation.Parent is { } parent
            && alsoStored?.Contains(parent) != true
            && !await TriggerExists(conn, parent, cancellationToken).ConfigureAwait(false))
        {
            Throw.ObjectDoesNotExistException(trigger, parent);
        }
    }

    /// <summary>
    /// Remove (delete) the <see cref="IJob" /> with the given
    /// name, and any <see cref="ITrigger" /> s that reference
    /// it.
    /// </summary>
    ///
    /// <remarks>
    /// If removal of the <see cref="IJob" /> results in an empty group, the
    /// group should be removed from the <see cref="IJobStore" />'s list of
    /// known group names.
    /// </remarks>
    /// <returns>
    /// <see langword="true" /> if a <see cref="IJob" /> with the given name &amp;
    /// group was found and removed from the store.
    /// </returns>
    public ValueTask<bool> DeleteJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return ExecuteInLock(SchedulerLock.TriggerAccess, conn => DeleteJob(conn, jobKey, true, cancellationToken), cancellationToken);
    }

    protected ValueTask<bool> DeleteJob(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        bool activeDeleteSafe,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            async () =>
            {
                var jobTriggers = await Delegate.SelectTriggerKeysForJob(conn, jobKey, cancellationToken).ConfigureAwait(false);

                foreach (TriggerKey jobTrigger in jobTriggers)
                {
                    await DeleteTriggerAndChildren(conn, jobTrigger, settleContinuations: true, storedTriggerType: null, cancellationToken).ConfigureAwait(false);
                }

                return await DeleteJobAndChildren(conn, jobKey, cancellationToken).ConfigureAwait(false);
            },
            "remove job");
    }

    /// <summary>
    /// Delete the identified jobs, and the triggers that reference them, in one lock and one
    /// transaction.
    /// </summary>
    /// <remarks>
    /// The walk is per key, and deliberately so: deleting a job is not one statement but a cascade —
    /// its triggers and their sub-table rows, the fired-trigger rows that would otherwise resurrect
    /// it (#1696), and finally the job detail row. A set-based <c>DELETE … WHERE … IN (…)</c> would
    /// report a row count rather than which keys it hit, which is precisely the answer this member
    /// owes its caller. Naming the deleted keys therefore costs nothing beyond what the cascade
    /// already spends: each iteration's result was previously folded into a boolean and thrown away.
    /// </remarks>
    public ValueTask<List<JobKey>> DeleteJobs(
        IReadOnlyCollection<JobKey> jobKeys,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInLock(
            SchedulerLock.TriggerAccess, async conn =>
            {
                List<JobKey> deleted = new List<JobKey>(jobKeys.Count);
                foreach (JobKey jobKey in jobKeys)
                {
                    if (await DeleteJob(conn, jobKey, activeDeleteSafe: true, cancellationToken).ConfigureAwait(false))
                    {
                        deleted.Add(jobKey);
                    }
                }

                return deleted;
            }, cancellationToken);
    }

    /// <summary>
    /// Delete the jobs in the matching groups, and the triggers that reference them, in one lock and
    /// one transaction.
    /// </summary>
    /// <remarks>
    /// The matching keys are resolved inside the lock rather than by the caller, which is the whole
    /// point of the group form: a job stored between a listing and a deletion would survive a call
    /// the caller believes emptied the group. Once the keys are in hand the walk is the one
    /// <see cref="DeleteJobs(IReadOnlyCollection{JobKey}, CancellationToken)" /> does, for the reason
    /// given there — the answer has to name the keys it hit, and a set-based delete reports a row
    /// count instead.
    /// </remarks>
    public ValueTask<List<JobKey>> DeleteJobs(
        GroupMatcher<JobKey> matcher,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(matcher);

        return ExecuteInLock(
            SchedulerLock.TriggerAccess, async conn =>
            {
                List<JobKey> matching = await Delegate.SelectJobKeysInGroup(conn, matcher, cancellationToken).ConfigureAwait(false);
                List<JobKey> deleted = new List<JobKey>(matching.Count);
                foreach (JobKey jobKey in matching)
                {
                    if (await DeleteJob(conn, jobKey, activeDeleteSafe: true, cancellationToken).ConfigureAwait(false))
                    {
                        deleted.Add(jobKey);
                    }
                }

                return deleted;
            }, cancellationToken);
    }

    /// <summary>
    /// Delete the identified triggers in one lock and one transaction.
    /// </summary>
    /// <remarks>
    /// Per key for the same reason <see cref="DeleteJobs(IReadOnlyCollection{JobKey}, CancellationToken)" /> is: removing a trigger also removes its
    /// sub-table row, its fired-trigger rows, and the job it orphans when that job is not durable.
    /// </remarks>
    public ValueTask<List<TriggerKey>> DeleteTriggers(
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInLock(
            SchedulerLock.TriggerAccess,
            async conn =>
            {
                List<TriggerKey> deleted = new List<TriggerKey>(triggerKeys.Count);
                foreach (TriggerKey triggerKey in triggerKeys)
                {
                    if (await DeleteTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false))
                    {
                        deleted.Add(triggerKey);
                    }
                }

                return deleted;
            }, cancellationToken);
    }

    /// <inheritdoc cref="DeleteJobs(GroupMatcher{JobKey}, CancellationToken)" />
    public ValueTask<List<TriggerKey>> DeleteTriggers(
        GroupMatcher<TriggerKey> matcher,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(matcher);

        return ExecuteInLock(
            SchedulerLock.TriggerAccess, async conn =>
            {
                List<TriggerKey> matching = await Delegate.SelectTriggerKeysInGroup(conn, matcher, cancellationToken).ConfigureAwait(false);
                List<TriggerKey> deleted = new List<TriggerKey>(matching.Count);
                foreach (TriggerKey triggerKey in matching)
                {
                    if (await DeleteTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false))
                    {
                        deleted.Add(triggerKey);
                    }
                }

                return deleted;
            }, cancellationToken);
    }

    public async ValueTask ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<IOperableTrigger>> triggersAndJobs, ScheduleJobOptions options = default, CancellationToken cancellationToken = default)
    {
        // One instant for the batch, as the set pauses stamp one.
        PauseInfo? pause = options.Paused ? PauseDetails.Record(options.Pause, timeProvider.GetUtcNow()) : null;

        await ExecuteInLock(
            LockOnInsert || options.Replace ? SchedulerLock.TriggerAccess : null, async conn =>
            {
                // A continuation may wait for a trigger stored beside it in this same call, whichever
                // order the two come in, so its parent is looked for in the batch as well as in the
                // store — once, here, rather than by each AddTrigger below, which would refuse one that
                // came before its parent.
                HashSet<TriggerKey> batch = [];
                foreach (var pair in triggersAndJobs)
                {
                    foreach (var trigger in pair.Value)
                    {
                        batch.Add(trigger.Key);
                    }
                }

                foreach (var pair in triggersAndJobs)
                {
                    foreach (var trigger in pair.Value)
                    {
                        await EnsureContinuationParentExists(conn, trigger, batch, cancellationToken).ConfigureAwait(false);
                    }
                }

                // A job and its triggers at a time, on purpose rather than for want of a bulk insert:
                // AddTrigger is a read-decide-write — does the row exist, is its group paused, is its
                // job blocked — so a batch would have to gather every read first, and the reads are
                // exactly what a bulk insert cannot skip. What this path costs is what AddJob and
                // AddTrigger cost; there is nothing here to fold that is not folded there.
                foreach (var pair in triggersAndJobs)
                {
                    var job = pair.Key;
                    var triggers = pair.Value;
                    await AddJob(conn, job, options.Replace, cancellationToken).ConfigureAwait(false);
                    foreach (var trigger in triggers)
                    {
                        await AddTrigger(
                            conn,
                            trigger,
                            job,
                            options.Replace,
                            StoredTriggerState.Waiting,
                            forceState: false,
                            recovering: false,
                            continuationParentChecked: true,
                            cancellationToken,
                            storePaused: options.Paused,
                            pause: pause).ConfigureAwait(false);
                    }
                }
            }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Delete a job and its listeners.
    /// </summary>
    /// <seealso cref="AdoJobStoreBase.DeleteJob(ConnectionAndTransactionHolder, JobKey, bool, CancellationToken)" />
    /// <seealso cref="DeleteTrigger(ConnectionAndTransactionHolder, TriggerKey, IJobDetail, CancellationToken)" />
    private async ValueTask<bool> DeleteJobAndChildren(
        ConnectionAndTransactionHolder conn,
        JobKey key,
        CancellationToken cancellationToken)
    {
        // Clean up any fired trigger records referencing this job to prevent
        // orphaned EXECUTING rows that block re-creation of the same job (#1696)
        await Delegate.DeleteFiredTriggers(conn, new FiredTriggerQuery { Job = key }, cancellationToken).ConfigureAwait(false);

        return await Delegate.DeleteJobDetail(conn, key, cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <summary>
    /// Delete a trigger, its listeners, its Simple/Cron/BLOB sub-table entry, and the fired-trigger rows
    /// of its executions.
    /// </summary>
    /// <remarks>
    /// The fired rows go because this is a removal: an execution whose trigger was deliberately
    /// unscheduled is not one to bring back as a recovery run (#744), and a recovery trigger built from a
    /// row whose trigger is gone would have no job data to copy (#2083). A <em>replacement</em> is the
    /// other case and does not come through here — see
    /// <see cref="ReplaceTrigger(ConnectionAndTransactionHolder, TriggerKey, IOperableTrigger, CancellationToken)" />.
    /// </remarks>
    /// <seealso cref="DeleteJob(ConnectionAndTransactionHolder, JobKey, bool, CancellationToken)" />
    /// <seealso cref="DeleteTrigger(ConnectionAndTransactionHolder, TriggerKey, IJobDetail, CancellationToken)" />
    /// <param name="conn">The unit of work.</param>
    /// <param name="key">The trigger being deleted.</param>
    /// <param name="settleContinuations">
    /// Whether the triggers awaiting this one still have to be settled. A completion that deletes its
    /// trigger has already settled them against the outcome its firing reached, and that settlement
    /// leaves none of them AWAITING, so asking again is a round trip whose answer is known to be empty.
    /// Every other caller deletes a trigger that has not fired and passes <see langword="true" />.
    /// </param>
    /// <param name="storedTriggerType">
    /// The discriminator the trigger's row holds, when the caller has just read its header; a shipped
    /// delegate then deletes the trigger, its type-table row and its fired rows as one batch (#4066).
    /// <see langword="null" /> when the caller has not, which deletes them as before.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    private async ValueTask<bool> DeleteTriggerAndChildren(
        ConnectionAndTransactionHolder conn,
        TriggerKey key,
        bool settleContinuations,
        string? storedTriggerType,
        CancellationToken cancellationToken)
    {
        // The triggers waiting for this one's firing, before the row goes: it is never going to fire,
        // so they are settled here rather than left waiting for ever. A replacement does not come
        // through this method — ReplaceTrigger deletes the row itself, precisely because the trigger
        // still exists afterwards — so a rescheduled parent keeps its dependants.
        if (settleContinuations)
        {
            await SettleContinuationsOfDeletedParent(conn, key, cancellationToken).ConfigureAwait(false);
        }

        if (storedTriggerType is not null && ShippedDelegate is { } shipped)
        {
            return await shipped.DeleteTriggerAndFiredRows(conn, key, storedTriggerType, cancellationToken).ConfigureAwait(false);
        }

        bool deleted = await Delegate.DeleteTrigger(conn, key, cancellationToken).ConfigureAwait(false) > 0;

        if (deleted)
        {
            await Delegate.DeleteFiredTriggers(conn, new FiredTriggerQuery { Trigger = key }, cancellationToken).ConfigureAwait(false);
        }

        return deleted;
    }

    /// <summary>
    /// Remove (delete) the <see cref="ITrigger" /> with the
    /// given name.
    /// </summary>
    ///
    /// <remarks>
    /// <para>
    /// If removal of the <see cref="ITrigger" /> results in an empty group, the
    /// group should be removed from the <see cref="IJobStore" />'s list of
    /// known group names.
    /// </para>
    ///
    /// <para>
    /// If removal of the <see cref="ITrigger" /> results in an 'orphaned' <see cref="IJob" />
    /// that is not 'durable', then the <see cref="IJob" /> should be deleted
    /// also.
    /// </para>
    /// </remarks>
    /// <param name="triggerKey">The key identifying the trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// <see langword="true" /> if a <see cref="ITrigger" /> with the given
    /// name &amp; group was found and removed from the store.
    ///</returns>
    public ValueTask<bool> DeleteTrigger(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInLock(
            SchedulerLock.TriggerAccess,
            conn => DeleteTrigger(conn, triggerKey, cancellationToken),
            cancellationToken);
    }

    protected ValueTask<bool> DeleteTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return DeleteTrigger(conn, triggerKey, null, cancellationToken);
    }

    protected ValueTask<bool> DeleteTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        IJobDetail? job,
        CancellationToken cancellationToken = default)
    {
        return DeleteTrigger(conn, triggerKey, job, settleContinuations: true, cancellationToken);
    }

    /// <inheritdoc cref="DeleteTrigger(ConnectionAndTransactionHolder, TriggerKey, IJobDetail, CancellationToken)" />
    /// <param name="conn">The unit of work.</param>
    /// <param name="triggerKey">The key identifying the trigger.</param>
    /// <param name="job">The trigger's job, when the caller already holds it.</param>
    /// <param name="settleContinuations">
    /// <inheritdoc cref="DeleteTriggerAndChildren" path="/param[@name='settleContinuations']" />
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    protected ValueTask<bool> DeleteTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        IJobDetail? job,
        bool settleContinuations,
        CancellationToken cancellationToken = default)
    {
        return DeleteTrigger(conn, triggerKey, job, settleContinuations, storedTriggerType: null, cancellationToken);
    }

    /// <inheritdoc cref="DeleteTrigger(ConnectionAndTransactionHolder, TriggerKey, IJobDetail, bool, CancellationToken)" />
    /// <param name="conn">The unit of work.</param>
    /// <param name="triggerKey">The key identifying the trigger.</param>
    /// <param name="job">The trigger's job, when the caller already holds it.</param>
    /// <param name="settleContinuations">
    /// <inheritdoc cref="DeleteTriggerAndChildren" path="/param[@name='settleContinuations']" />
    /// </param>
    /// <param name="storedTriggerType">
    /// <inheritdoc cref="DeleteTriggerAndChildren" path="/param[@name='storedTriggerType']" />
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    private ValueTask<bool> DeleteTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        IJobDetail? job,
        bool settleContinuations,
        string? storedTriggerType,
        CancellationToken cancellationToken)
    {
        return Guarded(
            async () =>
            {
                // this must be called before we delete the trigger, obviously
                // we use fault tolerant type loading as we only want to delete things
                if (job is null)
                {
                    job = await Delegate.SelectJobForTrigger(conn, triggerKey, new NullJobTypeLoader(), loadJobType: false, cancellationToken).ConfigureAwait(false);
                }

                bool removedTrigger = await DeleteTriggerAndChildren(conn, triggerKey, settleContinuations, storedTriggerType, cancellationToken).ConfigureAwait(false);

                if (null != job && !job.Durable)
                {
                    int numTriggers = await Delegate.CountTriggersForJob(conn, job.Key, cancellationToken).ConfigureAwait(false);
                    if (numTriggers == 0)
                    {
                        // Don't call DeleteJob() because we don't want to check for
                        // triggers again.
                        if (await DeleteJobAndChildren(conn, job.Key, cancellationToken).ConfigureAwait(false))
                        {
                            await signaler.NotifySchedulerListenersJobDeleted(job.Key, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }

                return removedTrigger;
            },
            "remove trigger");
    }

    private sealed class NullJobTypeLoader : ITypeLoader
    {
        public Type? LoadType(string name)
        {
            return null;
        }
    }

    /// <see cref="IJobStore.ReplaceTrigger(TriggerKey, IOperableTrigger, CancellationToken)" />
    public ValueTask<bool> ReplaceTrigger(
        TriggerKey triggerKey,
        IOperableTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInLock(SchedulerLock.TriggerAccess,
            conn => ReplaceTrigger(conn, triggerKey, trigger, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Deletes the trigger row and stores the replacement, leaving the fired-trigger rows of the old
    /// trigger's executions alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A replacement is not a removal. The trigger keeps its identity and its job, so an execution the
    /// row records is still one to finish — its completion deletes the row by fire instance id — or, if
    /// the node running it died, one to recover. Deleting the rows here is what #3759 was: an application
    /// that declares its triggers through <c>AddTrigger</c> has them re-applied as a reschedule every time
    /// it starts, before recovery has read a row of the run the kill interrupted, so a job that asked to
    /// be recovered never was. Only <see cref="DeleteTriggerAndChildren" /> deletes them.
    /// </para>
    /// <para>
    /// The kept row is a row the store reads: the replacement of a trigger whose job disallows concurrent
    /// execution is stored <c>BLOCKED</c> behind an execution still in flight, as any trigger of that job
    /// is, and is released by the execution's completion or by recovery — where deleting the row would
    /// have let it fire alongside the execution it could not see.
    /// </para>
    /// </remarks>
    protected ValueTask<bool> ReplaceTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        IOperableTrigger newTrigger,
        CancellationToken cancellationToken = default)
    {
        // "replace trigger", where this used to say "remove trigger": the message was a copy of
        // DeleteTrigger's, and named an operation the caller never asked for.
        return Guarded(
            async () =>
            {
                // this must be called before we delete the trigger, obviously
                //
                // The class is not resolved: rescheduling is an editing operation, and an administration
                // node that cannot load the job's assembly must be able to do it (#3705). What the
                // replacement needs from the job is its key, its durability and its two attribute flags,
                // and all four come from the row.
                var job = await Delegate.SelectJobForTrigger(conn, triggerKey, TypeLoader, loadJobType: false, cancellationToken).ConfigureAwait(false);

                if (job is null)
                {
                    return false;
                }

                if (!newTrigger.JobKey.Equals(job.Key))
                {
                    Throw.JobPersistenceException("New trigger is not related to the same job as the old trigger.");
                }

                // Likewise a replacement that would wait for a trigger the store does not hold — a
                // rescheduled continuation whose parent has gone — and before the old row is deleted, for
                // a store whose transaction is not its own to roll back. AddTrigger asks again once the
                // row has gone, which is what refuses a trigger replaced by one that waits for itself.
                await EnsureContinuationParentExists(conn, newTrigger, alsoStored: null, cancellationToken).ConfigureAwait(false);

                // Read before the row goes: a declared trigger re-applied at a start keeps the pause the row
                // has, with its record (#4128). Any other replace decides the state afresh.
                (bool keepPause, PauseInfo? keptPause) = await PauseKeptByReplace(conn, triggerKey, newTrigger, cancellationToken).ConfigureAwait(false);

                bool removedTrigger = await Delegate.DeleteTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false) > 0;
                if (!removedTrigger)
                {
                    // The row was there for the read above and is gone for the delete: its last firing
                    // completed without the lock in between (#3863). There is no trigger to replace, which
                    // is the answer a caller that arrived after that completion gets.
                    return false;
                }

                await AddTrigger(
                    conn,
                    newTrigger,
                    job,
                    replace: false,
                    StoredTriggerState.Waiting,
                    forceState: false,
                    recovering: false,
                    continuationParentChecked: false,
                    cancellationToken,
                    storePaused: keepPause,
                    pause: keptPause).ConfigureAwait(false);

                return true;
            },
            "replace trigger");
    }

    /// <summary>
    /// What a replace keeps of the pause on the row it replaces: whether to store the replacement paused,
    /// and the record to store with it.
    /// </summary>
    /// <remarks>
    /// Kept only for a declared trigger the scheduling-data processor re-applies at a start, so a redeploy
    /// does not resume a trigger an operator paused (#4128); read in the replace's own transaction, under
    /// <c>TRIGGER_ACCESS</c>. The record is the one the trigger answers while paused: its own, or its
    /// group's when it has none.
    /// </remarks>
    private async ValueTask<(bool Paused, PauseInfo? Pause)> PauseKeptByReplace(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        IOperableTrigger newTrigger,
        CancellationToken cancellationToken)
    {
        if (newTrigger is not TriggerBase { KeepsPauseOnReplace: true })
        {
            return (false, null);
        }

        StoredTriggerState state = await Delegate.SelectTriggerState(conn, triggerKey, cancellationToken).ConfigureAwait(false);
        if (state is not (StoredTriggerState.Paused or StoredTriggerState.PausedBlocked))
        {
            return (false, null);
        }

        return (true, await Delegate.SelectTriggerPause(conn, triggerKey, cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public ValueTask<bool> UpdateTriggerDetails(
        TriggerKey triggerKey,
        TriggerDetailsUpdate update,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInLock(
            SchedulerLock.TriggerAccess,
            conn => UpdateTriggerDetails(conn, triggerKey, update, cancellationToken),
            cancellationToken);
    }

    protected ValueTask<bool> UpdateTriggerDetails(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        TriggerDetailsUpdate update,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            async () =>
            {
                IOperableTrigger? existing = await Delegate.SelectTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false);
                if (existing is null)
                {
                    return false;
                }

                if (update.ChangesNothing)
                {
                    return true;
                }

                update.EnsureMisfireInstructionMatchesFamily(existing, triggerKey);
                update.EnsureOverlapPolicyCanBeCarried(existing, triggerKey);
                update.EnsureMisfireThresholdCanBeCarried(existing, triggerKey);
                update.EnsureMeasureIntervalFromCompletionCanBeCarried(existing, triggerKey);

                if (update.HasCalendarName && update.CalendarName is not null)
                {
                    bool calExists = await CalendarExists(conn, update.CalendarName, cancellationToken).ConfigureAwait(false);
                    if (!calExists)
                    {
                        Throw.JobPersistenceException($"Calendar '{update.CalendarName}' does not exist.");
                    }
                }

                // Not resolved, for the reason ReplaceTrigger does not resolve it: editing a trigger is
                // something a process without the job's assembly is entitled to do, and the flags the
                // write needs are columns rather than attributes.
                IJobDetail? job = await Delegate.SelectJobForTrigger(conn, triggerKey, TypeLoader, loadJobType: false, cancellationToken).ConfigureAwait(false);

                if (job is null)
                {
                    Throw.JobPersistenceException($"The job referenced by trigger '{triggerKey}' does not exist.");
                }

                // The edit is a read, a change and a write back of the whole row, under TRIGGER_ACCESS. A
                // firing round of work that allows concurrent execution takes no lock (#4067), so it can
                // fire the row between the read and the write, and a write by key would then put the
                // fire time the edit read back over the one the fire wrote. The write is held to the row
                // as read instead, and a miss reads the row again and applies the edit to the row the
                // fire left — the serialised order, round then edit.
                for (int attempt = 1; ; attempt++)
                {
                    RowAsRead asRead = new(existing.NextFireTimeUtc);
                    await ApplyTriggerDetails(conn, existing, update, cancellationToken).ConfigureAwait(false);

                    StoredTriggerState state = await Delegate.SelectTriggerState(conn, triggerKey, cancellationToken).ConfigureAwait(false);
                    if (await Delegate.UpdateTriggerIfUnchanged(conn, existing, state, job!, asRead.NextFireTimeUtc, cancellationToken).ConfigureAwait(false) > 0)
                    {
                        return true;
                    }

                    // The row's count rather than true: the trigger read above may have completed its
                    // last firing without the lock since, and an edit of a trigger that is gone is not an
                    // edit.
                    IOperableTrigger? moved = await ReadRowMovedUnderEdit(conn, triggerKey, attempt, "it was being edited, and the edit", cancellationToken).ConfigureAwait(false);
                    if (moved is null)
                    {
                        return false;
                    }

                    existing = moved;
                }
            },
            $"update trigger details for '{triggerKey}'");
    }

    /// <summary>
    /// Reads again a row a firing moved between an editor's read and its write, for the edit to be
    /// applied to the row the firing left (#4067).
    /// </summary>
    /// <param name="conn">The unit of work.</param>
    /// <param name="triggerKey">The trigger being edited.</param>
    /// <param name="attempt">How many times the edit has been applied, this one included.</param>
    /// <param name="edit">What the editor was doing, and what it failed to apply, as the failure says it.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The row as it is now, or <see langword="null" /> when it is gone.</returns>
    /// <exception cref="JobPersistenceException">The edit has missed <see cref="MaxEditAttempts" /> times.</exception>
    private async ValueTask<IOperableTrigger?> ReadRowMovedUnderEdit(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        int attempt,
        string edit,
        CancellationToken cancellationToken)
    {
        IOperableTrigger? moved = await Delegate.SelectTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false);
        if (moved is null)
        {
            return null;
        }

        if (attempt >= MaxEditAttempts)
        {
            Throw.JobPersistenceException($"Trigger '{triggerKey}' fired {attempt} times while {edit} was not applied.");
        }

        Logger.TriggerEditReapplied(triggerKey);
        return moved;
    }

    /// <summary>
    /// Writes what a <see cref="TriggerDetailsUpdate" /> asks for onto the trigger as its row was read.
    /// </summary>
    private async ValueTask ApplyTriggerDetails(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger existing,
        TriggerDetailsUpdate update,
        CancellationToken cancellationToken)
    {
        if (update.HasDescription)
        {
            existing.Description = update.Description;
        }

        if (update.HasPriority)
        {
            existing.Priority = update.Priority;
        }

        if (update.HasJobDataMap)
        {
            JobDataMap newMap = update.JobDataMap is { Count: > 0 }
                ? new JobDataMap((IDictionary<string, object?>) update.JobDataMap)
                : new JobDataMap();

            // Force dirty flag so Delegate.UpdateTrigger writes the BLOB
            newMap[SchedulerConstants.ForceJobDataMapDirty] = true;
            newMap.Remove(SchedulerConstants.ForceJobDataMapDirty);

            existing.JobDataMap = newMap;
        }

        if (update.HasCalendarName)
        {
            existing.CalendarName = update.CalendarName;
        }

        if (update.HasMisfireInstruction)
        {
            existing.MisfireInstructionCode = update.MisfireInstructionCode;
        }

        if (update.HasPreferredNode)
        {
            // Setting the property marks the pin dirty, so the subsequent store writes the
            // preferred node columns.
            existing.PreferredNode = update.PreferredNode;
        }

        if (update.HasExecutionGroup)
        {
            // EXECUTION_GROUP is part of the generic trigger UPDATE below, so nothing more is
            // needed to persist it.
            existing.ExecutionGroup = update.ExecutionGroup;
        }

        if (update.HasRetryPolicy)
        {
            // RETRY_POLICY is part of the same generic UPDATE. RETRY_ATTEMPT is written by it
            // too, with the value just read back from the row - so a re-policy leaves an
            // occurrence that is mid-retry counting from where it was.
            existing.RetryPolicy = update.RetryPolicy;
        }

        if (update.HasOverlapPolicy)
        {
            // OVERLAP_POLICY is part of the same generic UPDATE. Whether the new policy is
            // unsettled is read from the row as it stands, before the write replaces it.
            TriggerBase overlapping = (TriggerBase) existing;
            bool unsettled = await IsOverlapPolicyUnsettled(conn, overlapping, update.OverlapPolicy, cancellationToken).ConfigureAwait(false);
            overlapping.OverlapPolicy = update.OverlapPolicy;
            overlapping.OverlapPolicyUnsettled = unsettled;
        }

        if (update.HasMisfireThreshold)
        {
            // MISFIRE_THRESHOLD is part of the same generic UPDATE.
            ((TriggerBase) existing).MisfireThreshold = update.MisfireThreshold;
        }

        // FIXED_DELAY is part of the same generic UPDATE (#4140).
        update.ApplyMeasureIntervalFromCompletion(existing);
    }

    /// <summary>
    /// Recomputes one trigger's fire times from a calendar that changed, and writes the row back held to
    /// the row as read: a firing round that took no lock and fired the trigger in between (#4067) has the
    /// recomputation done again from the row it left, rather than overwritten.
    /// </summary>
    /// <param name="conn">The unit of work, which holds <see cref="SchedulerLock.TriggerAccess" />.</param>
    /// <param name="trigger">The trigger as <see cref="IDriverDelegate.SelectTriggersForCalendar" /> read it.</param>
    /// <param name="calendar">The calendar as it is now.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    private async ValueTask UpdateTriggerWithNewCalendar(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        ICalendar calendar,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            RowAsRead asRead = new(trigger.NextFireTimeUtc);
            trigger.UpdateWithNewCalendar(calendar, trigger.MisfireThreshold ?? MisfireThreshold);

            StoredTriggerState triggerState = await Delegate.SelectTriggerState(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
            if (triggerState == StoredTriggerState.Deleted)
            {
                return;
            }

            if (await AddTrigger(conn, trigger, null, true, triggerState, true, false, continuationParentChecked: false, cancellationToken, knownToExist: true, asRead: asRead).ConfigureAwait(false))
            {
                return;
            }

            IOperableTrigger? moved = await ReadRowMovedUnderEdit(
                conn,
                trigger.Key,
                attempt,
                $"calendar '{trigger.CalendarName}' was being applied to it, and the calendar",
                cancellationToken).ConfigureAwait(false);

            if (moved is null)
            {
                return;
            }

            trigger = moved;
        }
    }

    /// <summary>
    /// Store the given <see cref="ICalendar" />.
    /// </summary>
    /// <param name="calendarName">The name of the calendar.</param>
    /// <param name="calendar">The <see cref="ICalendar" /> to be stored.</param>
    /// <param name="options">
    /// Whether an existing calendar of the same name may be over-written, and whether the triggers
    /// referencing it have their next fire time re-computed.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <exception cref="ObjectAlreadyExistsException">
    ///           if a <see cref="ICalendar" /> with the same name already
    ///           exists, and replace is set to false.
    /// </exception>
    public async ValueTask AddCalendar(
        string calendarName,
        ICalendar calendar,
        AddCalendarOptions options = default,
        CancellationToken cancellationToken = default)
    {
        await ExecuteInLock(
            LockOnInsert || options.UpdateTriggers ? SchedulerLock.TriggerAccess : null,
            conn => AddCalendar(conn, calendarName, calendar, options, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    protected ValueTask AddCalendar(
        ConnectionAndTransactionHolder conn,
        string calendarName,
        ICalendar calendar,
        AddCalendarOptions options,
        CancellationToken cancellationToken = default)
    {
        // The ObjectAlreadyExistsException raised below is documented on the member and leaves as
        // itself; Guarded is what keeps it out of an InnerException.
        return Guarded(
            async () =>
            {
                bool existingCal = await CalendarExists(conn, calendarName, cancellationToken).ConfigureAwait(false);
                if (existingCal && !options.Replace)
                {
                    Throw.CalendarAlreadyExists(calendarName);
                }

                if (existingCal)
                {
                    if (await Delegate.UpdateCalendar(conn, calendarName, calendar, cancellationToken).ConfigureAwait(false) < 1)
                    {
                        Throw.JobPersistenceException("Couldn't store calendar.  Update failed.");
                    }

                    if (options.UpdateTriggers)
                    {
                        var triggers = await Delegate.SelectTriggersForCalendar(conn, calendarName, cancellationToken).ConfigureAwait(false);

                        foreach (IOperableTrigger trigger in triggers)
                        {
                            await UpdateTriggerWithNewCalendar(conn, trigger, calendar, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
                else
                {
                    if (await Delegate.InsertCalendar(conn, calendarName, calendar, cancellationToken).ConfigureAwait(false) < 1)
                    {
                        Throw.JobPersistenceException("Couldn't store calendar.  Insert failed.");
                    }
                }

                if (!Clustered)
                {
                    calendarCache[calendarName] = calendar; // lazy-cache
                }
            },
            "store calendar",
            WriteFailureReason);
    }

    /// <summary>
    /// Remove (delete) the <see cref="ICalendar" /> with the given name.
    /// </summary>
    /// <remarks>
    /// If removal of the <see cref="ICalendar" /> would result in
    /// <see cref="ITrigger" />s pointing to non-existent calendars, then a
    /// <see cref="JobPersistenceException" /> will be thrown.
    /// </remarks>
    /// <param name="calendarName">The name of the <see cref="ICalendar" /> to be removed.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// <see langword="true" /> if a <see cref="ICalendar" /> with the given name
    /// was found and removed from the store.
    ///</returns>
    public ValueTask<bool> DeleteCalendar(
        string calendarName,
        CancellationToken cancellationToken = default)
    {
        return ExecuteInLock(SchedulerLock.TriggerAccess, conn => DeleteCalendar(conn, calendarName, cancellationToken), cancellationToken);
    }

    protected ValueTask<bool> DeleteCalendar(
        ConnectionAndTransactionHolder conn,
        string calendarName,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            async () =>
            {
                if (await Delegate.CalendarIsReferenced(conn, calendarName, cancellationToken).ConfigureAwait(false))
                {
                    Throw.JobPersistenceException("Calendar cannot be removed if it is referenced by a trigger!");
                }

                if (!Clustered)
                {
                    calendarCache.Remove(calendarName);
                }

                return await Delegate.DeleteCalendar(conn, calendarName, cancellationToken).ConfigureAwait(false) > 0;
            },
            "remove calendar");
    }

    /// <summary>
    /// Clear (delete!) all scheduling data - all <see cref="IJob"/>s, <see cref="ITrigger" />s
    /// <see cref="ICalendar" />s.
    /// </summary>
    public async ValueTask Clear(CancellationToken cancellationToken = default)
    {
        await ExecuteInLock(SchedulerLock.TriggerAccess, conn => Clear(conn, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    protected ValueTask Clear(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            () => Delegate.ClearData(conn, cancellationToken),
            "clear scheduling data");
    }
}
