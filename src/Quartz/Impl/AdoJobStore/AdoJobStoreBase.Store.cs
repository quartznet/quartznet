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
            await AddJob(conn, job, false, cancellationToken).ConfigureAwait(false);
            await AddTrigger(conn, trigger, job, false, StoredTriggerState.Waiting, false, false, cancellationToken).ConfigureAwait(false);
            return null;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stores the given <see cref="IJobDetail" />.
    /// </summary>
    /// <param name="job">The <see cref="IJobDetail" /> to be stored.</param>
    /// <param name="replace">
    ///     If <see langword="true" />, any <see cref="IJob" /> existing in the
    ///     <see cref="IJobStore" /> with the same name &amp; group should be over-written.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public async ValueTask AddJob(IJobDetail job, bool replace, CancellationToken cancellationToken = default)
    {
        await ExecuteInLock(
            LockOnInsert || replace ? SchedulerLock.TriggerAccess : null,
            conn => AddJob(conn, job, replace, cancellationToken),
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
    /// <param name="replace">
    ///     If <see langword="true" />, any <see cref="ITrigger" /> existing in
    ///     the <see cref="IJobStore" /> with the same name &amp; group should
    ///     be over-written.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <exception cref="ObjectAlreadyExistsException">
    /// if a <see cref="ITrigger" /> with the same name/group already
    /// exists, and replace is set to false.
    /// </exception>
    public async ValueTask AddTrigger(IOperableTrigger trigger, bool replace, CancellationToken cancellationToken = default)
    {
        await ExecuteInLock(
            LockOnInsert || replace ? SchedulerLock.TriggerAccess : null,
            conn => AddTrigger(conn, trigger, null, replace, StoredTriggerState.Waiting, false, false, cancellationToken),
            cancellationToken).ConfigureAwait(false);
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
        bool existingTrigger = await TriggerExists(conn, newTrigger.Key, cancellationToken).ConfigureAwait(false);

        if (existingTrigger && !replace)
        {
            Throw.ObjectAlreadyExistsException(newTrigger);
        }

        await Guarded(
            async () =>
            {
                if (!forceState)
                {
                    state = await ApplyPausedTriggerGroupState(conn, newTrigger.Key.Group, state, cancellationToken).ConfigureAwait(false);
                }

                if (job is null)
                {
                    job = await GetJob(conn, newTrigger.JobKey, cancellationToken).ConfigureAwait(false);
                }
                if (job is null)
                {
                    Throw.JobPersistenceException($"The job ({newTrigger.JobKey}) referenced by the trigger does not exist.");
                }
                if (job.ConcurrentExecutionDisallowed && !recovering)
                {
                    state = await CheckBlockedState(conn, job.Key, state, cancellationToken).ConfigureAwait(false);
                }
                if (existingTrigger)
                {
                    // Preserve PreviousFireTimeUtc from the existing trigger when replacing,
                    // so that context.PreviousFireTimeUtc is not lost on application restart (#1834)
                    if (newTrigger.PreviousFireTimeUtc is null)
                    {
                        IOperableTrigger? existingTrig = await Delegate.SelectTrigger(conn, newTrigger.Key, cancellationToken).ConfigureAwait(false);
                        var prevFireTime = existingTrig?.PreviousFireTimeUtc;
                        if (prevFireTime is not null)
                        {
                            newTrigger.PreviousFireTimeUtc = prevFireTime;
                        }
                    }

                    await Delegate.UpdateTrigger(conn, newTrigger, state, job, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await Delegate.InsertTrigger(conn, newTrigger, state, job, cancellationToken).ConfigureAwait(false);
                }
            },
            $"store trigger '{newTrigger.Key}' for '{newTrigger.JobKey}' job").ConfigureAwait(false);
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
                    await DeleteTriggerAndChildren(conn, jobTrigger, cancellationToken).ConfigureAwait(false);
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
                    if (await DeleteJob(conn, jobKey, true, cancellationToken).ConfigureAwait(false))
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
    /// Per key for the same reason <see cref="DeleteJobs" /> is: removing a trigger also removes its
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

    public async ValueTask ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<IOperableTrigger>> triggersAndJobs, bool replace, CancellationToken cancellationToken = default)
    {
        await ExecuteInLock(
            LockOnInsert || replace ? SchedulerLock.TriggerAccess : null, async conn =>
            {
                // TODO: make this more efficient with a true bulk operation...
                foreach (var pair in triggersAndJobs)
                {
                    var job = pair.Key;
                    var triggers = pair.Value;
                    await AddJob(conn, job, replace, cancellationToken).ConfigureAwait(false);
                    foreach (var trigger in triggers)
                    {
                        await AddTrigger(conn, trigger, job, replace, StoredTriggerState.Waiting, false, false, cancellationToken).ConfigureAwait(false);
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
    /// Delete a trigger, its listeners, and its Simple/Cron/BLOB sub-table entry.
    /// </summary>
    /// <seealso cref="DeleteJob(ConnectionAndTransactionHolder, JobKey, bool, CancellationToken)" />
    /// <seealso cref="DeleteTrigger(ConnectionAndTransactionHolder, TriggerKey, IJobDetail, CancellationToken)" />
    /// <seealso cref="ReplaceTrigger(ConnectionAndTransactionHolder, TriggerKey, IOperableTrigger, CancellationToken)" />
    private async ValueTask<bool> DeleteTriggerAndChildren(
        ConnectionAndTransactionHolder conn,
        TriggerKey key,
        CancellationToken cancellationToken)
    {
        bool deleted = await Delegate.DeleteTrigger(conn, key, cancellationToken).ConfigureAwait(false) > 0;
        
        // Also clean up any fired trigger records to prevent recovery triggers from being created
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
        return Guarded(
            async () =>
            {
                // this must be called before we delete the trigger, obviously
                // we use fault tolerant type loading as we only want to delete things
                if (job is null)
                {
                    job = await Delegate.SelectJobForTrigger(conn, triggerKey, new NullJobTypeLoader(), loadJobType: false, cancellationToken).ConfigureAwait(false);
                }

                bool removedTrigger = await DeleteTriggerAndChildren(conn, triggerKey, cancellationToken).ConfigureAwait(false);

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
                var job = await Delegate.SelectJobForTrigger(conn, triggerKey, TypeLoader, loadJobType: true, cancellationToken).ConfigureAwait(false);

                if (job is null)
                {
                    return false;
                }

                if (!newTrigger.JobKey.Equals(job.Key))
                {
                    Throw.JobPersistenceException("New trigger is not related to the same job as the old trigger.");
                }

                bool removedTrigger = await DeleteTriggerAndChildren(conn, triggerKey, cancellationToken).ConfigureAwait(false);

                await AddTrigger(conn, newTrigger, job, false, StoredTriggerState.Waiting, false, false, cancellationToken).ConfigureAwait(false);

                return removedTrigger;
            },
            "replace trigger");
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

                if (!update.HasDescription && !update.HasPriority && !update.HasJobDataMap
                    && !update.HasCalendarName && !update.HasMisfireInstruction && !update.HasPreferredNode
                    && !update.HasExecutionGroup)
                {
                    return true;
                }

                update.EnsureMisfireInstructionMatchesFamily(existing, triggerKey);

                if (update.HasCalendarName && update.CalendarName is not null)
                {
                    bool calExists = await CalendarExists(conn, update.CalendarName, cancellationToken).ConfigureAwait(false);
                    if (!calExists)
                    {
                        Throw.JobPersistenceException($"Calendar '{update.CalendarName}' does not exist.");
                    }
                }

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

                StoredTriggerState state = await Delegate.SelectTriggerState(conn, triggerKey, cancellationToken).ConfigureAwait(false);
                IJobDetail? job = await Delegate.SelectJobForTrigger(conn, triggerKey, TypeLoader, loadJobType: true, cancellationToken).ConfigureAwait(false);

                if (job is null)
                {
                    Throw.JobPersistenceException($"The job referenced by trigger '{triggerKey}' does not exist.");
                }

                await Delegate.UpdateTrigger(conn, existing, state, job!, cancellationToken).ConfigureAwait(false);

                return true;
            },
            $"update trigger details for '{triggerKey}'");
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
                    Throw.ObjectAlreadyExistsException("Calendar with name '" + calendarName + "' already exists.");
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
                            trigger.UpdateWithNewCalendar(calendar, MisfireThreshold);
                            StoredTriggerState triggerState = await Delegate.SelectTriggerState(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
                            if (triggerState == StoredTriggerState.Deleted)
                            {
                                continue;
                            }
                            await AddTrigger(conn, trigger, null, true, triggerState, true, false, cancellationToken).ConfigureAwait(false);
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
    /// <remarks>
    /// </remarks>
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
