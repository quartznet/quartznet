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

using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Quartz.Diagnostics;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.Triggers;
using Quartz.Extensibility;

namespace Quartz.Impl;

/// <summary>
/// This class implements a <see cref="IJobStore" /> that
/// utilizes RAM as its storage device.
/// <para>
/// As you should know, the ramification of this is that access is extremely
/// fast, but the data is completely volatile - therefore this <see cref="IJobStore" />
/// should not be used if true persistence between program shutdowns is
/// required.
/// </para>
/// </summary>
/// <author>James House</author>
/// <author>Sharada Jambula</author>
/// <author>Marko Lahma (.NET)</author>
public sealed class RAMJobStore : IJobStore
{
    private readonly SemaphoreSlim lockObject = new(initialCount: 1, maxCount: 1);

    private readonly ConcurrentDictionary<JobKey, JobWrapper> jobsByKey = [];
    private readonly ConcurrentDictionary<TriggerKey, TriggerWrapper> triggersByKey = new();
    private readonly Dictionary<string, Dictionary<JobKey, JobWrapper>> jobsByGroup = [];
    private readonly Dictionary<string, Dictionary<TriggerKey, TriggerWrapper>> triggersByGroup = [];
    private readonly SortedSet<TriggerWrapper> timeTriggers = new(new TriggerWrapperComparator());
    private readonly ConcurrentDictionary<string, ICalendar> calendarsByName = [];
    private readonly Dictionary<JobKey, List<TriggerWrapper>> triggersByJob = [];
    private readonly HashSet<string> pausedTriggerGroups = [];
    private readonly HashSet<string> pausedJobGroups = [];
    private readonly HashSet<JobKey> blockedJobs = [];
    private readonly HashSet<JobKey> resumedJobsInPausedGroups = new HashSet<JobKey>();

    /// <summary>
    /// The executions each trigger has started that are still running, by fire instance id.
    /// </summary>
    /// <remarks>
    /// Deliberately keyed by <see cref="TriggerKey" /> rather than held on the wrapper: rescheduling a
    /// trigger replaces its wrapper, and an execution already in flight has to survive that. Keying by
    /// fire instance makes a late or duplicated completion a no-op instead of a miscount, and keeps
    /// several concurrent executions of one trigger distinct rather than collapsing them into a count.
    /// This mirrors the ADO store, where the answer comes from FIRED_TRIGGERS rows that likewise outlive
    /// a trigger update.
    /// </remarks>
    private readonly Dictionary<TriggerKey, Dictionary<string, FireInstanceEntry>> executingFireInstances = [];

    /// <summary>
    /// What one running execution is, beyond its id. The in-memory counterpart of an EXECUTING row of
    /// the ADO store's FIRED_TRIGGERS table, and the source of the <see cref="FireInstance" />s
    /// <see cref="QueryFireInstances" /> reports.
    /// </summary>
    private readonly record struct FireInstanceEntry(
        JobKey JobKey,
        DateTimeOffset FireTimeUtc,
        DateTimeOffset? ScheduledFireTimeUtc,
        string? ExecutionGroup);

    /// <summary>
    /// The instance id of the scheduler this store belongs to, from <see cref="Initialize" />. Reported
    /// on every <see cref="FireInstance" /> the store hands out, so that a listing reads the same way
    /// whichever store answered it — for this store the answer is always this one process.
    /// </summary>
    private string schedulerInstanceId = "";
    private TimeSpan misfireThreshold = TimeSpan.FromSeconds(5);
    private readonly ISchedulerSignaler signaler;
    private readonly TimeProvider timeProvider;
    private readonly ILogger logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RAMJobStore"/> class.
    /// </summary>
    public RAMJobStore(ILoggerFactory loggerFactory, ISchedulerSignaler signaler, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        logger = loggerFactory.CreateLogger<RAMJobStore>();
        this.signaler = signaler;
        this.timeProvider = timeProvider;
    }

    /// <summary>
    /// Gets or sets the time by which a trigger must have missed its next-fire-time, in order for it to
    /// be considered "misfired" and thus have its misfire instruction applied.
    /// </summary>
    /// <value>
    /// The time by which a trigger must have missed its next-fire-time, in order for it to be considered
    /// "misfired" and thus have its misfire instruction applied. The default is <c>5</c> seconds.
    /// </value>
    /// <exception cref="ArgumentException"><paramref name="value"/> represents less than one millisecond.</exception>
    [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
    public TimeSpan MisfireThreshold
    {
        get => misfireThreshold;
        internal set
        {
            if (value.TotalMilliseconds < 1)
            {
                Throw.ArgumentException("MisfireThreshold must be larger than 0");
            }
            misfireThreshold = value;
        }
    }

    private static long ftrCtr = TimeProvider.System.GetTimestamp();

    /// <summary>
    /// Gets the fired trigger record id.
    /// </summary>
    /// <returns>The fired trigger record id.</returns>
    private static string GetFiredTriggerRecordId()
    {
        long value = Interlocked.Increment(ref ftrCtr);
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Called by the QuartzScheduler before the <see cref="IJobStore" /> is
    /// used, in order to give it a chance to Initialize.
    /// </summary>
    public ValueTask Initialize(SchedulerIdentity identity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        schedulerInstanceId = identity.InstanceId;
        logger.StoreInitialized();
        return default;
    }

    /// <summary>
    /// Called by the QuartzScheduler to inform the <see cref="IJobStore" /> that
    /// the scheduler has started.
    /// </summary>
    public ValueTask SchedulerStarted(CancellationToken cancellationToken = default)
    {
        // nothing to do
        return default;
    }

    /// <summary>
    /// Called by the QuartzScheduler to inform the JobStore that
    /// the scheduler has been paused.
    /// </summary>
    public ValueTask SchedulerPaused(CancellationToken cancellationToken = default)
    {
        // nothing to do
        return default;
    }

    /// <summary>
    /// Called by the QuartzScheduler to inform the JobStore that
    /// the scheduler has resumed after being paused.
    /// </summary>
    public ValueTask SchedulerResumed(CancellationToken cancellationToken = default)
    {
        // nothing to do
        return default;
    }

    /// <summary>
    /// Called by the QuartzScheduler to inform the <see cref="IJobStore" /> that
    /// it should free up all of its resources because the scheduler is
    /// shutting down.
    /// </summary>
    public ValueTask Shutdown(CancellationToken cancellationToken = default)
    {
        return default;
    }

    /// <summary>
    /// Returns whether this instance supports persistence.
    /// </summary>
    /// <value></value>
    /// <returns></returns>
    public bool SupportsPersistence => false;

    /// <summary>
    /// Clears (deletes!) all scheduling data - all <see cref="IJob"/>s, <see cref="ITrigger" />s
    /// <see cref="ICalendar"/>s.
    /// </summary>
    public async ValueTask Clear(CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // unschedule jobs (delete triggers)
            foreach (string group in new List<string>(triggersByGroup.Keys))
            {
                var keys = GetTriggerKeysNoLock(GroupMatcher<TriggerKey>.GroupEquals(group));
                foreach (TriggerKey key in keys)
                {
                    await RemoveTriggerNoLock(key, removeOrphanedJob: true, keepExecutions: false, cancellationToken).ConfigureAwait(false);
                }
            }

            // delete jobs
            foreach (string group in new List<string>(jobsByGroup.Keys))
            {
                var keys = GetJobKeysNoLock(GroupMatcher<JobKey>.GroupEquals(group));
                foreach (JobKey key in keys)
                {
                    await RemoveJobNoLock(key, cancellationToken).ConfigureAwait(false);
                }
            }

            // delete calendars
            foreach (string name in new List<string>(calendarsByName.Keys))
            {
                RemoveCalendarNoLock(name);
            }

            resumedJobsInPausedGroups.Clear();
            executingFireInstances.Clear();
        }
        finally
        {
            lockObject.Release();
        }
    }

    /// <summary>
    /// Store the given <see cref="IJobDetail" /> and <see cref="ITrigger" />.
    /// </summary>
    /// <param name="job">The <see cref="IJobDetail" /> to be stored.</param>
    /// <param name="trigger">The <see cref="ITrigger" /> to be stored.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public async ValueTask ScheduleJob(IJobDetail job, IOperableTrigger trigger, CancellationToken cancellationToken = default)
    {
        await AddJob(job, replace: false, cancellationToken).ConfigureAwait(false);
        await AddTrigger(trigger, replace: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Store the given <see cref="IJob" />.
    /// </summary>
    /// <param name="job">The <see cref="IJob" /> to be stored.</param>
    /// <param name="replace">If <see langword="true" />, any <see cref="IJob" /> existing in the
    ///     <see cref="IJobStore" /> with the same name and group should be
    ///     over-written.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public async ValueTask AddJob(IJobDetail job, bool replace, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AddJobNoLock(job, replace);
        }
        finally
        {
            lockObject.Release();
        }
    }

    private void AddJobNoLock(IJobDetail job, bool replace)
    {
        var jobKey = job.Key;

        if (jobsByKey.TryGetValue(jobKey, out var originalJob))
        {
            if (!replace)
            {
                Throw.ObjectAlreadyExistsException(job);
            }

            // update job detail
            originalJob.JobDetail = job.Clone();
        }
        else
        {
            // get job group
            if (!jobsByGroup.TryGetValue(jobKey.Group, out var grpMap))
            {
                grpMap = new Dictionary<JobKey, JobWrapper>();
                jobsByGroup[jobKey.Group] = grpMap;
            }

            JobWrapper jw = new JobWrapper(job.Clone());

            // add to jobs by group
            grpMap[jobKey] = jw;
            // add to jobs by FQN map
            jobsByKey[jobKey] = jw;
        }
    }

    /// <summary>
    /// Remove (delete) the <see cref="IJob" /> with the given
    /// name, and any <see cref="ITrigger" /> s that reference
    /// it.
    /// </summary>
    /// <returns>
    /// 	<see langword="true" /> if a <see cref="IJob" /> with the given name and
    /// group was found and removed from the store.
    /// </returns>
    public async ValueTask<bool> DeleteJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RemoveJobNoLock(jobKey, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lockObject.Release();
        }
    }

    private async ValueTask<bool> RemoveJobNoLock(JobKey jobKey, CancellationToken cancellationToken)
    {
        bool found = false;
        var triggersForJob = GetTriggerKeysForJobNoLock(jobKey);
        foreach (var key in triggersForJob)
        {
            await RemoveTriggerNoLock(key, removeOrphanedJob: true, keepExecutions: false, cancellationToken).ConfigureAwait(false);
            found = true;
        }

        found = jobsByKey.TryRemove(jobKey, out _) || found;
        resumedJobsInPausedGroups.Remove(jobKey);

        if (found)
        {
            if (jobsByGroup.TryGetValue(jobKey.Group, out var grpMap))
            {
                if (grpMap.Remove(jobKey) && grpMap.Count == 0)
                {
                    jobsByGroup.Remove(jobKey.Group);
                }
            }
        }

        return found;
    }

    public async ValueTask<List<JobKey>> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<JobKey> deleted = new List<JobKey>(jobKeys.Count);
            foreach (JobKey key in jobKeys)
            {
                if (await RemoveJobNoLock(key, cancellationToken).ConfigureAwait(false))
                {
                    deleted.Add(key);
                }
            }

            return deleted;
        }
        finally
        {
            lockObject.Release();
        }
    }

    public async ValueTask<List<TriggerKey>> DeleteTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<TriggerKey> deleted = new List<TriggerKey>(triggerKeys.Count);
            foreach (TriggerKey key in triggerKeys)
            {
                if (await RemoveTriggerNoLock(key, removeOrphanedJob: true, keepExecutions: false, cancellationToken).ConfigureAwait(false))
                {
                    deleted.Add(key);
                }
            }

            return deleted;
        }
        finally
        {
            lockObject.Release();
        }
    }

    public async ValueTask ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<IOperableTrigger>> triggersAndJobs, bool replace, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // make sure there are no collisions...
            if (!replace)
            {
                foreach (var triggersByJob in triggersAndJobs)
                {
                    var job = triggersByJob.Key;

                    if (jobsByKey.ContainsKey(job.Key))
                    {
                        Throw.ObjectAlreadyExistsException(job);
                    }

                    foreach (IOperableTrigger trigger in triggersByJob.Value)
                    {
                        if (triggersByKey.ContainsKey(trigger.Key))
                        {
                            Throw.ObjectAlreadyExistsException(trigger);
                        }
                    }
                }
            }

            // do bulk add...
            foreach (var triggersByJob in triggersAndJobs)
            {
                AddJobNoLock(triggersByJob.Key, replace: true);
                foreach (IOperableTrigger trigger in triggersByJob.Value)
                {
                    await AddTriggerNoLock(trigger, replace: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            lockObject.Release();
        }
    }

    /// <summary>
    /// Remove (delete) the <see cref="ITrigger" /> with the
    /// given name.
    /// </summary>
    /// <returns>
    /// 	<see langword="true" /> if a <see cref="ITrigger" /> with the given
    /// name and group was found and removed from the store.
    /// </returns>
    public ValueTask<bool> DeleteTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        return DeleteTrigger(triggerKey, removeOrphanedJob: true, cancellationToken);
    }

    /// <summary>
    /// Store the given <see cref="ITrigger" />.
    /// </summary>
    /// <param name="trigger">The <see cref="ITrigger" /> to be stored.</param>
    /// <param name="replace">If <see langword="true" />, any <see cref="ITrigger" /> existing in
    ///     the <see cref="IJobStore" /> with the same name and group should
    ///     be over-written.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public async ValueTask AddTrigger(IOperableTrigger trigger, bool replace, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AddTriggerNoLock(trigger, replace, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lockObject.Release();
        }
    }

    private async Task AddTriggerNoLock(IOperableTrigger trigger, bool replace, CancellationToken cancellationToken)
    {
        TriggerWrapper tw = new((IOperableTrigger) trigger.Clone());
        if (triggersByKey.ContainsKey(tw.TriggerKey))
        {
            if (!replace)
            {
                Throw.ObjectAlreadyExistsException(trigger);
            }

            // don't delete orphaned job, this trigger has the job anyways
            await RemoveTriggerNoLock(tw.TriggerKey, removeOrphanedJob: false, keepExecutions: true, cancellationToken).ConfigureAwait(false);
        }

        if (!jobsByKey.ContainsKey(tw.JobKey))
        {
            Throw.JobPersistenceException($"The job ({tw.JobKey}) referenced by the trigger does not exist.");
        }

        // add to triggers by job
        if (!triggersByJob.TryGetValue(tw.JobKey, out var jobList))
        {
            jobList = new List<TriggerWrapper>(1);
            triggersByJob.Add(tw.JobKey, jobList);
        }

        jobList.Add(tw);

        // add to triggers by group
        if (!triggersByGroup.TryGetValue(tw.TriggerKey.Group, out var grpMap))
        {
            grpMap = new Dictionary<TriggerKey, TriggerWrapper>();
            triggersByGroup[tw.TriggerKey.Group] = grpMap;
        }

        grpMap[tw.TriggerKey] = tw;
        // add to triggers by FQN map
        triggersByKey[tw.TriggerKey] = tw;

        if (pausedTriggerGroups.Contains(tw.TriggerKey.Group) ||
            (pausedJobGroups.Contains(tw.JobKey.Group) && !resumedJobsInPausedGroups.Contains(tw.JobKey)))
        {
            tw.state = StoredTriggerState.Paused;
            if (blockedJobs.Contains(tw.JobKey))
            {
                tw.state = StoredTriggerState.PausedBlocked;
            }
        }
        else if (blockedJobs.Contains(tw.JobKey))
        {
            tw.state = StoredTriggerState.Blocked;
        }
        else
        {
            timeTriggers.Add(tw);
        }
    }

    /// <summary>
    /// Remove (delete) the <see cref="ITrigger" /> with the
    /// given name.
    ///
    /// </summary>
    /// <returns>
    /// 	<see langword="true" /> if a <see cref="ITrigger" /> with the given
    /// name and group was found and removed from the store.
    /// </returns>
    /// <param name="key">The <see cref="ITrigger" /> to be removed.</param>
    /// <param name="removeOrphanedJob">Whether to delete orphaned job details from scheduler if job becomes orphaned from removing the trigger.</param>
    /// <param name="cancellationToken"></param>
    private async ValueTask<bool> DeleteTrigger(TriggerKey key, bool removeOrphanedJob, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RemoveTriggerNoLock(key, removeOrphanedJob, keepExecutions: false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lockObject.Release();
        }
    }

    // keepExecutions: whether executions already started under this key survive. Only a trigger being
    // replaced in place keeps them, matching the ADO store, where updating a trigger leaves its
    // fired-trigger rows alone but deleting one removes them. There is no default: every caller says which.
    private async Task<bool> RemoveTriggerNoLock(TriggerKey key, bool removeOrphanedJob, bool keepExecutions, CancellationToken cancellationToken)
    {
        if (!keepExecutions)
        {
            executingFireInstances.Remove(key);
        }

        // remove from triggers by FQN map
        var found = triggersByKey.TryRemove(key, out var tw);
        if (tw is not null)
        {
            // remove from triggers by group
            if (triggersByGroup.TryGetValue(key.Group, out var grpMap))
            {
                if (grpMap.Remove(key) && grpMap.Count == 0)
                {
                    triggersByGroup.Remove(key.Group);
                }
            }

            //remove from triggers by job
            if (triggersByJob.TryGetValue(tw.JobKey, out var jobList))
            {
                if (jobList.Remove(tw) && jobList.Count == 0)
                {
                    triggersByJob.Remove(tw.JobKey);
                }
            }

            timeTriggers.Remove(tw);

            if (removeOrphanedJob)
            {
                JobWrapper jw = jobsByKey[tw.JobKey];
                var triggerKeys = GetTriggerKeysForJobNoLock(tw.JobKey);
                if (triggerKeys.Length == 0 && !jw.JobDetail.Durable && await RemoveJobNoLock(jw.Key, cancellationToken).ConfigureAwait(false))
                {
                    await signaler.NotifySchedulerListenersJobDeleted(jw.Key, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Replaces the trigger.
    /// </summary>
    /// <param name="triggerKey">The <see cref="TriggerKey"/> of the <see cref="ITrigger" /> to be replaced.</param>
    /// <param name="trigger">The new trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public async ValueTask<bool> ReplaceTrigger(TriggerKey triggerKey, IOperableTrigger trigger, CancellationToken cancellationToken = default)
    {
        bool found;

        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            found = triggersByKey.TryGetValue(triggerKey, out var tw);

            if (found)
            {
                // Validated before anything is removed, so a rejected replacement leaves the store exactly
                // as it was rather than half-deleting the trigger it refused to replace.
                if (!tw!.JobKey.Equals(trigger.JobKey))
                {
                    Throw.JobPersistenceException("New trigger is not related to the same job as the old trigger.");
                }

                // Kept so the rollback below can put them back: the old trigger is still the one running
                // them until the replacement actually succeeds.
                executingFireInstances.TryGetValue(triggerKey, out var fireInstances);

                // The old trigger is deleted rather than updated, so its executions go with it, as they do
                // in the ADO store where ReplaceTrigger deletes the fired-trigger rows. Removing through
                // the shared path means anything kept per trigger is cleaned up here too.
                await RemoveTriggerNoLock(triggerKey, removeOrphanedJob: false, keepExecutions: false, cancellationToken).ConfigureAwait(false);

                try
                {
                    await AddTriggerNoLock(trigger, replace: false, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (JobPersistenceException)
                {
                    // put previous trigger back...
                    await AddTriggerNoLock(tw.Trigger, replace: false, cancellationToken: cancellationToken).ConfigureAwait(false);

                    // ...along with the executions it never stopped running.
                    if (fireInstances is not null)
                    {
                        executingFireInstances[triggerKey] = fireInstances;
                    }

                    throw;
                }
            }
        }
        finally
        {
            lockObject.Release();
        }
        return found;
    }

    /// <inheritdoc />
    public async ValueTask<bool> UpdateTriggerDetails(TriggerKey triggerKey, TriggerDetailsUpdate update, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!triggersByKey.TryGetValue(triggerKey, out TriggerWrapper? tw))
            {
                return false;
            }

            if (!update.HasDescription && !update.HasPriority && !update.HasJobDataMap
                && !update.HasCalendarName && !update.HasMisfireInstruction && !update.HasPreferredNode
                && !update.HasExecutionGroup)
            {
                return true;
            }

            IOperableTrigger trigger = tw.Trigger;

            update.EnsureMisfireInstructionMatchesFamily(trigger, triggerKey);

            if (update.HasCalendarName && update.CalendarName is not null)
            {
                if (!calendarsByName.ContainsKey(update.CalendarName))
                {
                    Throw.JobPersistenceException($"Calendar '{update.CalendarName}' does not exist.");
                }
            }

            if (update.HasDescription)
            {
                trigger.Description = update.Description;
            }

            if (update.HasPriority)
            {
                // Priority affects SortedSet ordering, must remove/re-add
                bool wasInTimeTriggers = timeTriggers.Remove(tw);
                trigger.Priority = update.Priority;
                if (wasInTimeTriggers)
                {
                    timeTriggers.Add(tw);
                }
            }

            if (update.HasJobDataMap)
            {
                JobDataMap newMap = update.JobDataMap is { Count: > 0 }
                    ? new JobDataMap((IDictionary<string, object?>) update.JobDataMap)
                    : new JobDataMap();

                trigger.JobDataMap = newMap;
            }

            if (update.HasCalendarName)
            {
                trigger.CalendarName = update.CalendarName;
            }

            if (update.HasMisfireInstruction)
            {
                trigger.MisfireInstructionCode = update.MisfireInstructionCode;
            }

            if (update.HasPreferredNode)
            {
                // The stored instance is the one mutated here - reads hand out clones of it - so the
                // new pin is what every later reader sees, exactly as the ADO.NET store's update does.
                trigger.PreferredNode = update.PreferredNode;
            }

            if (update.HasExecutionGroup)
            {
                trigger.ExecutionGroup = update.ExecutionGroup;
            }

            return true;
        }
        finally
        {
            lockObject.Release();
        }
    }

    /// <summary>
    /// Retrieve the <see cref="IJobDetail" /> for the given
    /// <see cref="IJob" />.
    /// </summary>
    /// <returns>
    /// The desired <see cref="IJob" />, or null if there is no match.
    /// </returns>
    public async ValueTask<IJobDetail?> GetJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            jobsByKey.TryGetValue(jobKey, out JobWrapper? jw);
            return jw?.JobDetail.Clone();
        }
        finally
        {
            lockObject.Release();
        }
    }

    /// <summary>
    /// Retrieve the given <see cref="ITrigger" />.
    /// </summary>
    /// <returns>
    /// The desired <see cref="ITrigger" />, or null if there is no match.
    /// </returns>
    public async ValueTask<IOperableTrigger?> GetTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            triggersByKey.TryGetValue(triggerKey, out var tw);
            return (IOperableTrigger?) tw?.Trigger.Clone();
        }
        finally
        {
            lockObject.Release();
        }
    }

    /// <summary>
    /// Determine whether a <see cref="IJob"/> with the given identifier already
    /// exists within the scheduler.
    /// </summary>
    /// <param name="jobKey">the identifier to check for</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if a Job exists with the given identifier</returns>
    public async ValueTask<bool> Exists(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return jobsByKey.ContainsKey(jobKey);
        }
        finally
        {
            lockObject.Release();
        }
    }

    /// <summary>
    /// Determine whether a <see cref="ITrigger" /> with the given identifier already
    /// exists within the scheduler.
    /// </summary>
    /// <param name="triggerKey">triggerKey the identifier to check for</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if a Trigger exists with the given identifier</returns>
    public async ValueTask<bool> Exists(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return triggersByKey.ContainsKey(triggerKey);
        }
        finally
        {
            lockObject.Release();
        }
    }

    /// <summary>
    /// Get the current state of the identified <see cref="ITrigger" />.
    /// </summary>
    /// <seealso cref="TriggerState.Normal" />
    /// <seealso cref="TriggerState.Paused" />
    /// <seealso cref="TriggerState.Complete" />
    /// <seealso cref="TriggerState.Error" />
    /// <seealso cref="TriggerState.Blocked" />
    /// <seealso cref="TriggerState.None"/>
    /// <seealso cref="TriggerState.Executing" />
    public async ValueTask<TriggerState> GetTriggerState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Both facts have to be read under the same lock, or a fire landing between the two reads
            // would produce a state that was never actually true.
            return triggersByKey.TryGetValue(triggerKey, out var tw) ? ToTriggerStateNoLock(tw) : TriggerState.None;
        }
        finally
        {
            lockObject.Release();
        }
    }

    /// <summary>
    /// Maps a wrapper to the state callers see. The precedence lives in <see cref="TriggerStateResolver" />,
    /// shared with the ADO store.
    /// </summary>
    private TriggerState ToTriggerStateNoLock(TriggerWrapper tw)
    {
        return TriggerStateResolver.Resolve(tw.state, executingFireInstances.ContainsKey(tw.TriggerKey));
    }

    /// <summary>
    /// Records that an execution of the trigger has finished, forgetting the trigger entirely once its
    /// last one has. An unknown fire instance is a no-op, so a repeated completion cannot miscount.
    /// </summary>
    private void ReleaseExecutionNoLock(TriggerKey triggerKey, string fireInstanceId)
    {
        if (executingFireInstances.TryGetValue(triggerKey, out var fireInstances)
            && fireInstances.Remove(fireInstanceId)
            && fireInstances.Count == 0)
        {
            executingFireInstances.Remove(triggerKey);
        }
    }

    public async ValueTask<bool> ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return ResetTriggerFromErrorStateNoLock(triggerKey);
        }
        finally
        {
            lockObject.Release();
        }
    }

    /// <summary>
    /// Resets the whole set inside one lock pass rather than taking the lock per key.
    /// </summary>
    public async ValueTask<List<TriggerKey>> ResetTriggersFromErrorState(
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<TriggerKey> reset = new List<TriggerKey>(triggerKeys.Count);
            foreach (TriggerKey triggerKey in triggerKeys)
            {
                if (ResetTriggerFromErrorStateNoLock(triggerKey))
                {
                    reset.Add(triggerKey);
                }
            }

            return reset;
        }
        finally
        {
            lockObject.Release();
        }
    }

    private bool ResetTriggerFromErrorStateNoLock(TriggerKey triggerKey)
    {
        // does the trigger exist?
        if (!triggersByKey.TryGetValue(triggerKey, out var tw) || tw.Trigger is null)
        {
            return false;
        }

        // is the trigger in error state?
        if (tw.state != StoredTriggerState.Error)
        {
            return false;
        }

        if (pausedTriggerGroups.Contains(triggerKey.Group))
        {
            tw.state = StoredTriggerState.Paused;
        }
        else
        {
            tw.state = StoredTriggerState.Waiting;
            timeTriggers.Add(tw);
        }

        return true;
    }

    /// <summary>
    /// Store the given <see cref="ICalendar" />.
    /// </summary>
    /// <param name="calendarName">The name.</param>
    /// <param name="calendar">The <see cref="ICalendar" /> to be stored.</param>
    /// <param name="options">Whether an existing calendar of the same name may be over-written,
    /// and whether the triggers referencing it have their next fire time re-computed.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public async ValueTask AddCalendar(
        string calendarName,
        ICalendar calendar,
        AddCalendarOptions options = default,
        CancellationToken cancellationToken = default)
    {
        calendar = calendar.Clone();

        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            calendarsByName.TryGetValue(calendarName, out var obj);

            if (obj is not null && !options.Replace)
            {
                Throw.ObjectAlreadyExistsException($"Calendar with name '{calendarName}' already exists.");
            }

            if (obj is not null)
            {
                calendarsByName.TryRemove(calendarName, out _);
            }

            calendarsByName[calendarName] = calendar;

            if (obj is not null && options.UpdateTriggers)
            {
                foreach (TriggerWrapper tw in GetTriggerWrappersForCalendarNoLock(calendarName))
                {
                    bool removed = timeTriggers.Remove(tw);

                    tw.Trigger.UpdateWithNewCalendar(calendar, MisfireThreshold);

                    if (removed)
                    {
                        timeTriggers.Add(tw);
                    }
                }
            }
        }
        finally
        {
            lockObject.Release();
        }
    }

    /// <summary>
    /// Remove (delete) the <see cref="ICalendar" /> with the
    /// given name.
    /// <para>
    /// If removal of the <see cref="ICalendar" /> would result in
    /// <see cref="ITrigger" />s pointing to non-existent calendars, then a
    /// <see cref="JobPersistenceException" /> will be thrown.</para>
    /// </summary>
    /// <param name="calendarName">The name of the <see cref="ICalendar" /> to be removed.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// 	<see langword="true" /> if a <see cref="ICalendar" /> with the given name
    /// was found and removed from the store.
    /// </returns>
    public async ValueTask<bool> DeleteCalendar(string calendarName, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return RemoveCalendarNoLock(calendarName);
        }
        finally
        {
            lockObject.Release();
        }
    }

    private bool RemoveCalendarNoLock(string name)
    {
        int numRefs = 0;
        foreach (TriggerWrapper triggerWrapper in triggersByKey.Values)
        {
            IOperableTrigger trigg = triggerWrapper.Trigger;
            if (trigg.CalendarName is not null && trigg.CalendarName == name)
            {
                numRefs++;
            }
        }

        if (numRefs > 0)
        {
            Throw.JobPersistenceException("Calendar cannot be removed if it is referenced by a Trigger!");
        }

        return calendarsByName.TryRemove(name, out _);
    }

    /// <summary>
    /// Retrieve the given <see cref="ITrigger" />.
    /// </summary>
    /// <param name="calendarName">The name of the <see cref="ICalendar" /> to be retrieved.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// The desired <see cref="ICalendar" />, or null if there is no match.
    /// </returns>
    public async ValueTask<ICalendar?> GetCalendar(string calendarName, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            calendarsByName.TryGetValue(calendarName, out var calendar);
            return calendar?.Clone();
        }
        finally
        {
            lockObject.Release();
        }
    }

    private List<JobKey> GetJobKeysNoLock(GroupMatcher<JobKey> matcher)
    {
        HashSet<JobKey> outList = [];
        StringOperator op = matcher.CompareWithOperator;
        string compareToValue = matcher.CompareToValue;

        if (StringOperator.Equality.Equals(op))
        {
            if (jobsByGroup.TryGetValue(compareToValue, out var grpMap))
            {
                foreach (JobWrapper jw in grpMap.Values)
                {
                    outList.Add(jw.JobDetail.Key);
                }
            }
        }
        else
        {
            foreach (KeyValuePair<string, Dictionary<JobKey, JobWrapper>> entry in jobsByGroup)
            {
                if (op.Evaluate(entry.Key, compareToValue))
                {
                    foreach (JobWrapper jobWrapper in entry.Value.Values)
                    {
                        outList.Add(jobWrapper.JobDetail.Key);
                    }
                }
            }
        }

        return [.. outList];
    }

    private List<TriggerKey> GetTriggerKeysNoLock(GroupMatcher<TriggerKey> matcher)
    {
        List<TriggerKey> outList;
        StringOperator op = matcher.CompareWithOperator;
        string compareToValue = matcher.CompareToValue;

        if (StringOperator.Equality.Equals(op))
        {
            if (triggersByGroup.TryGetValue(compareToValue, out var grpMap))
            {
                outList = new List<TriggerKey>(grpMap.Count);
                foreach (KeyValuePair<TriggerKey, TriggerWrapper> entry in grpMap)
                {
                    outList.Add(entry.Value.TriggerKey);
                }
            }
            else
            {
                outList = [];
            }
        }
        else
        {
            outList = [];
            foreach (KeyValuePair<string, Dictionary<TriggerKey, TriggerWrapper>> candidatePair in triggersByGroup)
            {
                if (op.Evaluate(candidatePair.Key, compareToValue))
                {
                    foreach (KeyValuePair<TriggerKey, TriggerWrapper> entry in candidatePair.Value)
                    {
                        outList.Add(entry.Value.TriggerKey);
                    }
                }
            }
        }

        return outList;
    }

    /// <inheritdoc />
    public async ValueTask<PagedResult<JobHeader>> QueryJobs(JobQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        List<IJobDetail> matches = [];

        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            GroupMatcher<JobKey>? matcher = query.Group;
            NameMatcher<JobKey>? nameMatcher = query.Name;
            if (matcher is not null && StringOperator.Equality.Equals(matcher.CompareWithOperator))
            {
                if (jobsByGroup.TryGetValue(matcher.CompareToValue, out Dictionary<JobKey, JobWrapper>? groupMap))
                {
                    foreach (JobWrapper jobWrapper in groupMap.Values)
                    {
                        if (nameMatcher is null || nameMatcher.IsMatch(jobWrapper.JobDetail.Key))
                        {
                            matches.Add(jobWrapper.JobDetail);
                        }
                    }
                }
            }
            else
            {
                foreach (KeyValuePair<string, Dictionary<JobKey, JobWrapper>> entry in jobsByGroup)
                {
                    if (matcher is not null && !matcher.CompareWithOperator.Evaluate(entry.Key, matcher.CompareToValue))
                    {
                        continue;
                    }

                    foreach (JobWrapper jobWrapper in entry.Value.Values)
                    {
                        if (nameMatcher is null || nameMatcher.IsMatch(jobWrapper.JobDetail.Key))
                        {
                            matches.Add(jobWrapper.JobDetail);
                        }
                    }
                }
            }
        }
        finally
        {
            lockObject.Release();
        }

        if (query.Take > 0)
        {
            // the count idiom (Take = 0) reads no page, so the ordering work is skipped
            matches.Sort(static (left, right) => CompareByGroupThenName(left.Key.Group, left.Key.Name, right.Key.Group, right.Key.Name));
        }

        return Page(matches, query, static job => new JobHeader(
            job.Key,
            job.Description,
            // the ADO store persists JobType.FullName, so a listing has to report the same string
            job.JobType.FullName,
            job.Durable,
            job.ConcurrentExecutionDisallowed,
            job.PersistJobDataAfterExecution,
            job.RequestsRecovery));
    }

    /// <inheritdoc />
    public async ValueTask<PagedResult<TriggerHeader>> QueryTriggers(TriggerQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        List<TriggerMatch> matches = [];

        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            GroupMatcher<TriggerKey>? matcher = query.Group;
            if (matcher is not null && StringOperator.Equality.Equals(matcher.CompareWithOperator))
            {
                if (triggersByGroup.TryGetValue(matcher.CompareToValue, out Dictionary<TriggerKey, TriggerWrapper>? groupMap))
                {
                    CollectMatchingTriggersNoLock(groupMap, query, matches);
                }
            }
            else
            {
                foreach (KeyValuePair<string, Dictionary<TriggerKey, TriggerWrapper>> entry in triggersByGroup)
                {
                    if (matcher is not null && !matcher.CompareWithOperator.Evaluate(entry.Key, matcher.CompareToValue))
                    {
                        continue;
                    }

                    CollectMatchingTriggersNoLock(entry.Value, query, matches);
                }
            }
        }
        finally
        {
            lockObject.Release();
        }

        if (query.Take > 0)
        {
            // the count idiom (Take = 0) reads no page, so the ordering work is skipped
            matches.Sort(static (left, right) => CompareByGroupThenName(
                left.Trigger.Key.Group,
                left.Trigger.Key.Name,
                right.Trigger.Key.Group,
                right.Trigger.Key.Name));
        }

        return Page(matches, query, static match => new TriggerHeader(
            match.Trigger.Key,
            match.Trigger.JobKey,
            match.Trigger.Description,
            GetTriggerTypeDiscriminator(match.Trigger),
            match.State,
            match.Trigger.StartTimeUtc,
            match.Trigger.EndTimeUtc,
            match.Trigger.NextFireTimeUtc,
            match.Trigger.PreviousFireTimeUtc,
            match.Trigger.CalendarName,
            match.Trigger.Priority,
            match.Trigger.ExecutionGroup));
    }

    private void CollectMatchingTriggersNoLock(
        Dictionary<TriggerKey, TriggerWrapper> groupMap,
        TriggerQuery query,
        List<TriggerMatch> matches)
    {
        foreach (TriggerWrapper triggerWrapper in groupMap.Values)
        {
            if (query.Name is not null && !query.Name.IsMatch(triggerWrapper.TriggerKey))
            {
                continue;
            }

            if (query.Job is not null && !query.Job.Equals(triggerWrapper.JobKey))
            {
                continue;
            }

            if (query.CalendarName is not null
                && !string.Equals(triggerWrapper.Trigger.CalendarName, query.CalendarName, StringComparison.Ordinal))
            {
                continue;
            }

            TriggerState state = ToTriggerStateNoLock(triggerWrapper);
            if (query.State is not null && state != query.State.Value)
            {
                continue;
            }

            matches.Add(new TriggerMatch(triggerWrapper.Trigger, state));
        }
    }

    /// <inheritdoc />
    public async ValueTask<PagedResult<JobGroup>> QueryJobGroups(JobGroupQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        List<JobGroup> groups = [];

        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (query.Paused == true)
            {
                // a group can be paused while holding no jobs, and a listing of paused groups has to report it
                foreach (string group in pausedJobGroups)
                {
                    if (MatchesName(query.Name, group))
                    {
                        groups.Add(new JobGroup(group, Paused: true));
                    }
                }
            }
            else
            {
                foreach (string group in jobsByGroup.Keys)
                {
                    if (!MatchesName(query.Name, group))
                    {
                        continue;
                    }

                    bool paused = pausedJobGroups.Contains(group);
                    if (query.Paused is null || !paused)
                    {
                        groups.Add(new JobGroup(group, paused));
                    }
                }
            }
        }
        finally
        {
            lockObject.Release();
        }

        groups.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));

        return Page(groups, query, static group => group);
    }

    /// <inheritdoc />
    public async ValueTask<PagedResult<TriggerGroup>> QueryTriggerGroups(TriggerGroupQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        List<TriggerGroup> groups = [];

        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (query.Paused == true)
            {
                // a group can be paused while holding no triggers, and a listing of paused groups has to report it
                foreach (string group in pausedTriggerGroups)
                {
                    if (MatchesName(query.Name, group))
                    {
                        groups.Add(new TriggerGroup(group, Paused: true));
                    }
                }
            }
            else
            {
                foreach (string group in triggersByGroup.Keys)
                {
                    if (!MatchesName(query.Name, group))
                    {
                        continue;
                    }

                    bool paused = pausedTriggerGroups.Contains(group);
                    if (query.Paused is null || !paused)
                    {
                        groups.Add(new TriggerGroup(group, paused));
                    }
                }
            }
        }
        finally
        {
            lockObject.Release();
        }

        groups.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));

        return Page(groups, query, static group => group);
    }

    /// <summary>
    /// Whether a group name passes a group query's exact-name filter; a null filter passes everything.
    /// </summary>
    private static bool MatchesName(string? filter, string group)
    {
        return filter is null || string.Equals(filter, group, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public async ValueTask<PagedResult<string>> QueryCalendarNames(CalendarQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        CalendarNameMatcher? nameMatcher = query.Name;
        List<string> names = [];

        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (string calendarName in calendarsByName.Keys)
            {
                if (nameMatcher is null || nameMatcher.IsMatch(calendarName))
                {
                    names.Add(calendarName);
                }
            }
        }
        finally
        {
            lockObject.Release();
        }

        names.Sort(StringComparer.Ordinal);

        return Page(names, query, static name => name);
    }

    /// <inheritdoc />
    public async ValueTask<PagedResult<FireInstance>> QueryFireInstances(FireInstanceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        List<FireInstance> matches = [];

        // This store's world is one process, so a query naming another node matches nothing rather than
        // silently answering for this one.
        bool thisNode = query.SchedulerInstanceId is null
            || string.Equals(query.SchedulerInstanceId, schedulerInstanceId, StringComparison.Ordinal);

        if (thisNode)
        {
            await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Everything is projected into records under the lock and nothing that could still change
                // escapes it, so the page is a snapshot rather than a view.
                if (query.State is null or FireInstanceState.Executing)
                {
                    CollectExecutingFireInstancesNoLock(query, matches);
                }

                if (query.State is null or FireInstanceState.Acquired)
                {
                    CollectAcquiredFireInstancesNoLock(query, matches);
                }
            }
            finally
            {
                lockObject.Release();
            }
        }

        if (query.Take > 0)
        {
            // the count idiom (Take = 0) reads no page, so the ordering work is skipped
            matches.Sort(static (left, right) =>
            {
                int byKey = CompareByGroupThenName(
                    left.TriggerKey.Group,
                    left.TriggerKey.Name,
                    right.TriggerKey.Group,
                    right.TriggerKey.Name);

                // One trigger can have many firings in flight, so the key alone does not order them:
                // without the fire instance id two pages could show the same firing twice and miss another.
                return byKey != 0 ? byKey : StringComparer.Ordinal.Compare(left.FireInstanceId, right.FireInstanceId);
            });
        }

        return Page(matches, query, static instance => instance);
    }

    private void CollectExecutingFireInstancesNoLock(FireInstanceQuery query, List<FireInstance> matches)
    {
        foreach (KeyValuePair<TriggerKey, Dictionary<string, FireInstanceEntry>> byTrigger in executingFireInstances)
        {
            TriggerKey triggerKey = byTrigger.Key;
            if (!MatchesTriggerKey(query, triggerKey))
            {
                continue;
            }

            foreach (KeyValuePair<string, FireInstanceEntry> execution in byTrigger.Value)
            {
                FireInstanceEntry entry = execution.Value;
                if (query.Job is not null && !query.Job.Equals(entry.JobKey))
                {
                    continue;
                }

                matches.Add(new FireInstance(
                    execution.Key,
                    triggerKey,
                    entry.JobKey,
                    schedulerInstanceId,
                    FireInstanceState.Executing,
                    entry.FireTimeUtc,
                    entry.ScheduledFireTimeUtc,
                    entry.ExecutionGroup));
            }
        }
    }

    private void CollectAcquiredFireInstancesNoLock(FireInstanceQuery query, List<FireInstance> matches)
    {
        // A reservation is not in executingFireInstances — nothing has started yet — so it is read from
        // the wrapper the acquisition marked, which is this store's whole record of one.
        if (query.Job is not null)
        {
            // ...and it names no job, so a job filter excludes every reservation, exactly as the job
            // columns of an unstarted FIRED_TRIGGERS row do.
            return;
        }

        foreach (TriggerWrapper tw in triggersByKey.Values)
        {
            if (tw.state != StoredTriggerState.Acquired || !MatchesTriggerKey(query, tw.TriggerKey))
            {
                continue;
            }

            matches.Add(new FireInstance(
                tw.Trigger.FireInstanceId,
                tw.TriggerKey,
                JobKey: null,
                schedulerInstanceId,
                FireInstanceState.Acquired,
                tw.acquiredAtUtc,
                tw.Trigger.NextFireTimeUtc,
                tw.Trigger.ExecutionGroup));
        }
    }

    private static bool MatchesTriggerKey(FireInstanceQuery query, TriggerKey triggerKey)
    {
        return (query.TriggerGroup is null || query.TriggerGroup.IsMatch(triggerKey))
               && (query.TriggerName is null || query.TriggerName.IsMatch(triggerKey));
    }

    /// <inheritdoc />
    public ValueTask<List<ClusterNode>> QueryClusterNodes(CancellationToken cancellationToken = default)
    {
        // This store's world is one process, so the cluster is this node and nothing else. It keeps no
        // check-in history because it has nobody to keep one for: the times are absent rather than
        // invented, and the state is Alive because the node answering is by definition running.
        return new ValueTask<List<ClusterNode>>(new List<ClusterNode>
        {
            new ClusterNode(schedulerInstanceId, LastCheckInUtc: null, CheckInInterval: null, ClusterNodeState.Alive, IsCurrentNode: true)
        });
    }

    /// <summary>
    /// What this store holds in flight per (execution group, trigger group) pair, which is what a
    /// <see cref="ExecutionLimitScope.Cluster" /> execution limit is counted against.
    /// </summary>
    /// <remarks>
    /// The in-memory counterpart of the ADO store's aggregate over FIRED_TRIGGERS, and deliberately over
    /// the same set: a wrapper in the acquired state is a reservation, an entry in
    /// <see cref="executingFireInstances" /> is a running execution. This store is never clustered, so
    /// its cluster is this one process and a cluster-scoped limit comes out as the same number a
    /// node-scoped one would — which is why the store honours the scope rather than declining it.
    /// </remarks>
    private List<ExecutionGroupInFlight> CollectInFlightExecutionGroupsNoLock()
    {
        Dictionary<(string? ExecutionGroup, string TriggerGroup), int> counts = new();

        foreach (TriggerWrapper tw in triggersByKey.Values)
        {
            if (tw.state == StoredTriggerState.Acquired)
            {
                CountOne(counts, tw.Trigger.ExecutionGroup, tw.TriggerKey.Group);
            }
        }

        foreach (KeyValuePair<TriggerKey, Dictionary<string, FireInstanceEntry>> byTrigger in executingFireInstances)
        {
            foreach (FireInstanceEntry entry in byTrigger.Value.Values)
            {
                CountOne(counts, entry.ExecutionGroup, byTrigger.Key.Group);
            }
        }

        List<ExecutionGroupInFlight> result = new(counts.Count);
        foreach (KeyValuePair<(string? ExecutionGroup, string TriggerGroup), int> pair in counts)
        {
            result.Add(new ExecutionGroupInFlight(pair.Key.ExecutionGroup, pair.Key.TriggerGroup, pair.Value));
        }

        return result;
    }

    /// <summary>
    /// Adds one firing to the tally for its (execution group, trigger group) pair.
    /// </summary>
    /// <remarks>
    /// One hash probe rather than the two a lookup-then-assign costs, and a method taking the
    /// dictionary rather than a local function closing over it — a captured collection that only grows
    /// inside a local function reads to symbolic analysis as one nothing ever writes to, which makes
    /// the caller's enumeration of it look dead.
    /// </remarks>
    private static void CountOne(
        Dictionary<(string? ExecutionGroup, string TriggerGroup), int> counts,
        string? executionGroup,
        string triggerGroup)
    {
        ref int tally = ref CollectionsMarshal.GetValueRefOrAddDefault(counts, (executionGroup, triggerGroup), out _);
        tally++;
    }

    /// <inheritdoc />
    public async ValueTask<List<IJobDetail>> GetJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobKeys);

        List<IJobDetail> jobs = new(jobKeys.Count);
        HashSet<JobKey> seen = new(jobKeys.Count);

        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (JobKey jobKey in jobKeys)
            {
                if (seen.Add(jobKey) && jobsByKey.TryGetValue(jobKey, out JobWrapper? jobWrapper))
                {
                    jobs.Add(jobWrapper.JobDetail.Clone());
                }
            }
        }
        finally
        {
            lockObject.Release();
        }

        return jobs;
    }

    /// <inheritdoc />
    public async ValueTask<List<IOperableTrigger>> GetTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triggerKeys);

        List<IOperableTrigger> triggers = new(triggerKeys.Count);
        HashSet<TriggerKey> seen = new(triggerKeys.Count);

        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (TriggerKey triggerKey in triggerKeys)
            {
                if (seen.Add(triggerKey) && triggersByKey.TryGetValue(triggerKey, out TriggerWrapper? triggerWrapper))
                {
                    triggers.Add((IOperableTrigger) triggerWrapper.Trigger.Clone());
                }
            }
        }
        finally
        {
            lockObject.Release();
        }

        return triggers;
    }

    /// <summary>
    /// Maps a trigger to the type discriminator the ADO job store would persist for it.
    /// </summary>
    /// <remarks>
    /// This has to agree with the persistence delegate <c>StdAdoDelegate.FindTriggerPersistenceDelegate</c>
    /// would pick, in the order the delegates are registered; a trigger no delegate handles is stored
    /// as a blob there.
    /// </remarks>
    private static string GetTriggerTypeDiscriminator(IOperableTrigger trigger)
    {
        return trigger switch
        {
            SimpleTriggerImpl { HasAdditionalProperties: false } => AdoConstants.TriggerTypeSimple,
            CronTriggerImpl { HasAdditionalProperties: false } => AdoConstants.TriggerTypeCron,
            CalendarIntervalTriggerImpl { HasAdditionalProperties: false } => AdoConstants.TriggerTypeCalendarInterval,
            DailyTimeIntervalTriggerImpl { HasAdditionalProperties: false } => AdoConstants.TriggerTypeDailyTimeInterval,
            RecurrenceTriggerImpl => AdoConstants.TriggerTypeRecurrence,
            _ => AdoConstants.TriggerTypeBlob
        };
    }

    private static int CompareByGroupThenName(string leftGroup, string leftName, string rightGroup, string rightName)
    {
        int byGroup = StringComparer.Ordinal.Compare(leftGroup, rightGroup);
        return byGroup != 0 ? byGroup : StringComparer.Ordinal.Compare(leftName, rightName);
    }

    /// <summary>
    /// Returns the requested page of an already ordered set of matches.
    /// </summary>
    /// <remarks>
    /// The whole match set is known here, so <see cref="PagedResult{T}.HasMore" /> is computed exactly
    /// rather than by reading one item past the page.
    /// </remarks>
    private static PagedResult<TResult> Page<TSource, TResult>(List<TSource> ordered, PagedQuery query, Func<TSource, TResult> selector)
    {
        int total = ordered.Count;
        List<TResult> items = [];

        if (query.Take > 0 && query.Skip < total)
        {
            int end = (int) Math.Min((long) query.Skip + query.Take, total);
            items.Capacity = end - query.Skip;
            for (int i = query.Skip; i < end; i++)
            {
                items.Add(selector(ordered[i]));
            }
        }

        bool hasMore = query.Skip + (long) items.Count < total;
        return new PagedResult<TResult>(items, hasMore, query.IncludeTotalCount ? total : null);
    }

    /// <summary>
    /// A trigger that matched a query, with the state it had when it matched.
    /// </summary>
    private readonly record struct TriggerMatch(IOperableTrigger Trigger, TriggerState State);

    /// <summary>
    /// Get all the Triggers that are associated to the given Job.
    /// <para>
    /// If there are no matches, a zero-length array should be returned.
    /// </para>
    /// </summary>
    public async ValueTask<List<IOperableTrigger>> GetTriggersForJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (triggersByJob.TryGetValue(jobKey, out List<TriggerWrapper>? jobList))
            {
                var trigList = new List<IOperableTrigger>(jobList.Count);
                for (var i = 0; i < jobList.Count; i++)
                {
                    trigList.Add((IOperableTrigger) jobList[i].Trigger.Clone());
                }
                return trigList;
            }

            return [];
        }
        finally
        {
            lockObject.Release();
        }
    }

    private TriggerKey[] GetTriggerKeysForJobNoLock(JobKey jobKey)
    {
        if (triggersByJob.TryGetValue(jobKey, out List<TriggerWrapper>? jobList))
        {
            var trigList = new TriggerKey[jobList.Count];
            for (var i = 0; i < jobList.Count; i++)
            {
                trigList[i] = jobList[i].Trigger.Key;
            }
            return trigList;
        }

        return [];
    }

    /// <summary>
    /// Gets the trigger wrappers for job.
    /// </summary>
    /// <returns></returns>
    /// <remarks>
    /// This method should only be executed while holding the instance level lock.
    /// </remarks>
    private List<TriggerWrapper> GetTriggerWrappersForJobNoLock(JobKey jobKey)
    {
        return triggersByJob.TryGetValue(jobKey, out var jobList) ? jobList : [];
    }

    /// <summary>
    /// Gets the trigger wrappers for calendar.
    /// </summary>
    /// <param name="name">Name of the calendar.</param>
    /// <returns></returns>
    private IEnumerable<TriggerWrapper> GetTriggerWrappersForCalendarNoLock(string name)
    {
        foreach (var tw in triggersByKey.Values)
        {
            var tcalName = tw.Trigger.CalendarName;
            if (tcalName is not null && tcalName == name)
            {
                yield return tw;
            }
        }
    }

    /// <summary>
    /// Pause the <see cref="ITrigger" /> with the given name.
    /// </summary>
    public async ValueTask<bool> PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return PauseTriggerNoLock(triggerKey);
        }
        finally
        {
            lockObject.Release();
        }
    }

    /// <summary>
    /// Pauses the whole set inside one lock pass rather than taking the lock per key.
    /// </summary>
    public async ValueTask<List<TriggerKey>> PauseTriggers(
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<TriggerKey> paused = new List<TriggerKey>(triggerKeys.Count);
            foreach (TriggerKey triggerKey in triggerKeys)
            {
                if (PauseTriggerNoLock(triggerKey))
                {
                    paused.Add(triggerKey);
                }
            }

            return paused;
        }
        finally
        {
            lockObject.Release();
        }
    }

    /// <summary>
    /// Moves one trigger into the paused state, and reports whether it moved.
    /// </summary>
    /// <remarks>
    /// Only a trigger that is waiting, acquired or blocked is pausable, which is the set the ADO
    /// store writes PAUSED over. Anything else keeps the state it is in: a completed trigger has
    /// nothing left to pause, a paused one is already there, and a trigger in error is a failure
    /// somebody has to see — pausing its group must not quietly clear it, or
    /// <see cref="ResetTriggerFromErrorState" /> finds nothing left to reset.
    /// </remarks>
    private bool PauseTriggerNoLock(TriggerKey triggerKey)
    {
        // does the trigger exist?
        if (!triggersByKey.TryGetValue(triggerKey, out var tw))
        {
            return false;
        }

        if (tw.state == StoredTriggerState.Blocked)
        {
            tw.state = StoredTriggerState.PausedBlocked;
        }
        else if (tw.state is StoredTriggerState.Waiting or StoredTriggerState.Acquired)
        {
            tw.state = StoredTriggerState.Paused;
        }
        else
        {
            return false;
        }

        timeTriggers.Remove(tw);
        return true;
    }

    /// <summary>
    /// Pause all of the <see cref="ITrigger" />s in the given group.
    /// <para>
    /// The JobStore should "remember" that the group is paused, and impose the
    /// pause on any new triggers that are added to the group while the group is
    /// paused.
    /// </para>
    /// </summary>
    public async ValueTask<List<string>> PauseTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return PauseTriggersNoLock(matcher);
        }
        finally
        {
            lockObject.Release();
        }
    }

    private List<string> PauseTriggersNoLock(GroupMatcher<TriggerKey> matcher)
    {
        var pausedGroups = new HashSet<string>();

        StringOperator op = matcher.CompareWithOperator;
        if (StringOperator.Equality.Equals(op))
        {
            if (pausedTriggerGroups.Add(matcher.CompareToValue))
            {
                pausedGroups.Add(matcher.CompareToValue);
            }
        }
        else
        {
            // The group that matched is what gets remembered, not the matcher's own text: a pattern is
            // not a group, and keying the set on it would let the first matching group swallow the
            // pause for every later one.
            foreach (string group in triggersByGroup.Keys)
            {
                if (op.Evaluate(group, matcher.CompareToValue) && pausedTriggerGroups.Add(group))
                {
                    pausedGroups.Add(group);
                }
            }
        }

        foreach (string pausedGroup in pausedGroups)
        {
            var keys = GetTriggerKeysNoLock(GroupMatcher<TriggerKey>.GroupEquals(pausedGroup));

            foreach (TriggerKey key in keys)
            {
                PauseTriggerNoLock(key);
            }
        }

        return [..pausedGroups];
    }

    /// <summary>
    /// Pause the <see cref="IJobDetail" /> with the given
    /// name - by pausing all of its current <see cref="ITrigger" />s.
    /// </summary>
    public async ValueTask<bool> PauseJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return PauseJobNoLock(jobKey);
        }
        finally
        {
            lockObject.Release();
        }
    }

    /// <summary>
    /// Pauses the whole set inside one lock pass rather than taking the lock per key.
    /// </summary>
    public async ValueTask<List<JobKey>> PauseJobs(
        IReadOnlyCollection<JobKey> jobKeys,
        CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<JobKey> paused = new List<JobKey>(jobKeys.Count);
            foreach (JobKey jobKey in jobKeys)
            {
                if (PauseJobNoLock(jobKey))
                {
                    paused.Add(jobKey);
                }
            }

            return paused;
        }
        finally
        {
            lockObject.Release();
        }
    }

    private bool PauseJobNoLock(JobKey jobKey)
    {
        if (!jobsByKey.ContainsKey(jobKey))
        {
            return false;
        }

        resumedJobsInPausedGroups.Remove(jobKey);
        var triggerKeysForJob = GetTriggerKeysForJobNoLock(jobKey);
        foreach (TriggerKey key in triggerKeysForJob)
        {
            PauseTriggerNoLock(key);
        }

        return true;
    }

    /// <summary>
    /// Pause all of the <see cref="IJobDetail" />s in the
    /// given group - by pausing all of their <see cref="ITrigger" />s.
    /// <para>
    /// The JobStore should "remember" that the group is paused, and impose the
    /// pause on any new jobs that are added to the group while the group is
    /// paused.
    /// </para>
    /// </summary>
    public async ValueTask<List<string>> PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<string> pausedGroups = [];
            StringOperator op = matcher.CompareWithOperator;
            if (StringOperator.Equality.Equals(op))
            {
                resumedJobsInPausedGroups.RemoveWhere(k => k.Group == matcher.CompareToValue);
                if (pausedJobGroups.Add(matcher.CompareToValue))
                {
                    pausedGroups.Add(matcher.CompareToValue);
                }
            }
            else
            {
                foreach (string group in jobsByGroup.Keys)
                {
                    if (op.Evaluate(group, matcher.CompareToValue))
                    {
                        resumedJobsInPausedGroups.RemoveWhere(k => k.Group == group);
                        if (pausedJobGroups.Add(group))
                        {
                            pausedGroups.Add(group);
                        }
                    }
                }
            }

            foreach (string groupName in pausedGroups)
            {
                foreach (JobKey jobKey in GetJobKeysNoLock(GroupMatcher<JobKey>.GroupEquals(groupName)))
                {
                    var triggerKeys = GetTriggerKeysForJobNoLock(jobKey);
                    foreach (TriggerKey key in triggerKeys)
                    {
                        PauseTriggerNoLock(key);
                    }
                }
            }

            return pausedGroups;
        }
        finally
        {
            lockObject.Release();
        }
    }

    /// <summary>
    /// Resume (un-pause) the <see cref="ITrigger" /> with the given key.
    /// </summary>
    /// <remarks>
    /// If the <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </remarks>
    public async ValueTask<bool> ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ResumeTriggerNoLock(triggerKey).ConfigureAwait(false);
        }
        finally
        {
            lockObject.Release();
        }
    }

    /// <summary>
    /// Resumes the whole set inside one lock pass rather than taking the lock per key.
    /// </summary>
    public async ValueTask<List<TriggerKey>> ResumeTriggers(
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<TriggerKey> resumed = new List<TriggerKey>(triggerKeys.Count);
            foreach (TriggerKey triggerKey in triggerKeys)
            {
                if (await ResumeTriggerNoLock(triggerKey).ConfigureAwait(false))
                {
                    resumed.Add(triggerKey);
                }
            }

            return resumed;
        }
        finally
        {
            lockObject.Release();
        }
    }

    private async ValueTask<bool> ResumeTriggerNoLock(TriggerKey triggerKey)
    {
        // does the trigger exist?
        if (!triggersByKey.TryGetValue(triggerKey, out var tw))
        {
            return false;
        }

        // if the trigger is not paused resuming it does not make sense...
        if (tw.state != StoredTriggerState.Paused &&
            tw.state != StoredTriggerState.PausedBlocked)
        {
            return false;
        }

        if (blockedJobs.Contains(tw.JobKey))
        {
            tw.state = StoredTriggerState.Blocked;
        }
        else
        {
            tw.state = StoredTriggerState.Waiting;
        }

        await ApplyMisfireNoLock(tw).ConfigureAwait(false);

        if (tw.state == StoredTriggerState.Waiting)
        {
            timeTriggers.Add(tw);
        }

        return true;
    }

    /// <summary>
    /// Resume (un-pause) all of the <see cref="ITrigger" />s in the
    /// given group.
    /// <para>
    /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </para>
    /// </summary>
    public async ValueTask<List<string>> ResumeTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ResumeTriggersNoLock(matcher).ConfigureAwait(false);
        }
        finally
        {
            lockObject.Release();
        }
    }

    private async ValueTask<List<string>> ResumeTriggersNoLock(GroupMatcher<TriggerKey> matcher)
    {
        var groups = new HashSet<string>();
        var keys = GetTriggerKeysNoLock(matcher);

        foreach (TriggerKey triggerKey in keys)
        {
            groups.Add(triggerKey.Group);
            if (triggersByKey.TryGetValue(triggerKey, out var tw))
            {
                string jobGroup = tw.JobKey.Group;
                if (pausedJobGroups.Contains(jobGroup))
                {
                    continue;
                }
            }

            await ResumeTriggerNoLock(triggerKey).ConfigureAwait(false);
        }

        // Forget the pause of every group the matcher selects, whichever operator it carries — the
        // pause is recorded per matched group, so a resume that only understood equality would leave
        // the groups a prefix pause recorded paused forever.
        StringOperator op = matcher.CompareWithOperator;
        string matcherGroup = matcher.CompareToValue;
        if (StringOperator.Equality.Equals(op))
        {
            pausedTriggerGroups.Remove(matcherGroup);
        }
        else
        {
            pausedTriggerGroups.RemoveWhere(group => op.Evaluate(group, matcherGroup));
        }

        return [..groups];
    }

    /// <summary>
    /// Resume (un-pause) the <see cref="IJobDetail" /> with
    /// the given name.
    /// <para>
    /// If any of the <see cref="IJob" />'s<see cref="ITrigger" /> s missed one
    /// or more fire-times, then the <see cref="ITrigger" />'s misfire
    /// instruction will be applied.
    /// </para>
    /// </summary>
    public async ValueTask<bool> ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ResumeJobNoLock(jobKey).ConfigureAwait(false);
        }
        finally
        {
            lockObject.Release();
        }
    }

    /// <summary>
    /// Resumes the whole set inside one lock pass rather than taking the lock per key.
    /// </summary>
    public async ValueTask<List<JobKey>> ResumeJobs(
        IReadOnlyCollection<JobKey> jobKeys,
        CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<JobKey> resumed = new List<JobKey>(jobKeys.Count);
            foreach (JobKey jobKey in jobKeys)
            {
                if (await ResumeJobNoLock(jobKey).ConfigureAwait(false))
                {
                    resumed.Add(jobKey);
                }
            }

            return resumed;
        }
        finally
        {
            lockObject.Release();
        }
    }

    private async ValueTask<bool> ResumeJobNoLock(JobKey jobKey)
    {
        if (!jobsByKey.ContainsKey(jobKey))
        {
            return false;
        }

        if (pausedJobGroups.Contains(jobKey.Group))
        {
            resumedJobsInPausedGroups.Add(jobKey);
        }

        var triggerKeysForJob = GetTriggerKeysForJobNoLock(jobKey);
        foreach (TriggerKey key in triggerKeysForJob)
        {
            await ResumeTriggerNoLock(key).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Resume (un-pause) all of the <see cref="IJobDetail" />s
    /// in the given group.
    /// <para>
    /// If any of the <see cref="IJob" /> s had <see cref="ITrigger" /> s that
    /// missed one or more fire-times, then the <see cref="ITrigger" />'s
    /// misfire instruction will be applied.
    /// </para>
    /// </summary>
    public async ValueTask<List<string>> ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var resumedGroups = new List<string>();
            var keys = GetJobKeysNoLock(matcher);

            foreach (string pausedJobGroup in pausedJobGroups)
            {
                if (matcher.CompareWithOperator.Evaluate(pausedJobGroup, matcher.CompareToValue))
                {
                    resumedGroups.Add(pausedJobGroup);
                }
            }

            foreach (string resumedGroup in resumedGroups)
            {
                pausedJobGroups.Remove(resumedGroup);
                resumedJobsInPausedGroups.RemoveWhere(k => k.Group == resumedGroup);
            }

            foreach (JobKey key in keys)
            {
                var triggerKeys = GetTriggerKeysForJobNoLock(key);
                foreach (TriggerKey triggerKey in triggerKeys)
                {
                    await ResumeTriggerNoLock(triggerKey).ConfigureAwait(false);
                }
            }
            return resumedGroups;
        }
        finally
        {
            lockObject.Release();
        }
    }

    /// <summary>
    /// Pause all triggers - equivalent of calling <see cref="PauseTriggers(GroupMatcher{TriggerKey}, CancellationToken)" />
    /// on every group.
    /// <para>
    /// When <see cref="ResumeAll" /> is called (to un-pause), trigger misfire
    /// instructions WILL be applied.
    /// </para>
    /// </summary>
    /// <seealso cref="ResumeAll(CancellationToken)" />
    public async ValueTask PauseAll(CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (string groupName in triggersByGroup.Keys)
            {
                PauseTriggersNoLock(GroupMatcher<TriggerKey>.GroupEquals(groupName));
            }
        }
        finally
        {
            lockObject.Release();
        }
    }

    /// <summary>
    /// Resume (un-pause) all triggers - equivalent of calling <see cref="ResumeTriggers(GroupMatcher{TriggerKey}, CancellationToken)" />
    /// on every trigger group and setting all job groups unpaused />.
    /// <para>
    /// If any <see cref="ITrigger" /> missed one or more fire-times, then the
    /// <see cref="ITrigger" />'s misfire instruction will be applied.
    /// </para>
    /// </summary>
    /// <seealso cref="PauseAll(CancellationToken)" />
    public async ValueTask ResumeAll(CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // TODO need a match all here!
            pausedJobGroups.Clear();
            resumedJobsInPausedGroups.Clear();

            foreach (string groupName in triggersByGroup.Keys)
            {
                await ResumeTriggersNoLock(GroupMatcher<TriggerKey>.GroupEquals(groupName)).ConfigureAwait(false);
            }

            // make sure we don't have anything left in groups
            pausedTriggerGroups.Clear();
        }
        finally
        {
            lockObject.Release();
        }
    }

    /// <summary>
    /// Applies the misfire.
    /// </summary>
    /// <param name="tw">The trigger wrapper.</param>
    /// <returns>
    /// <see langword="true"/> if the next fire time of the trigger was updated from either
    /// one value to another, or from a given value to <see langword="null"/>; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    private async ValueTask<bool> ApplyMisfireNoLock(TriggerWrapper tw)
    {
        if (tw.Trigger.MisfireInstructionCode == MisfireInstruction.IgnoreMisfirePolicy)
        {
            return false;
        }

        DateTimeOffset misfireTime = timeProvider.GetUtcNow();
        if (MisfireThreshold > TimeSpan.Zero)
        {
            misfireTime = misfireTime.AddTicks(-1 * MisfireThreshold.Ticks);
        }

        DateTimeOffset? tnft = tw.Trigger.NextFireTimeUtc;
        if (!tnft.HasValue || tnft.GetValueOrDefault() > misfireTime)
        {
            return false;
        }

        ICalendar? calendar = null;
        if (tw.Trigger.CalendarName is not null)
        {
            calendarsByName.TryGetValue(tw.Trigger.CalendarName, out calendar);
        }

        await signaler.NotifyTriggerListenersMisfired(tw.Trigger.Clone()).ConfigureAwait(false);

        // Save the original scheduled fire time before misfire handling changes it.
        var originalFireTime = tnft;
        var now = timeProvider.GetUtcNow();

        tw.Trigger.UpdateAfterMisfire(calendar);

        // Only save for "fire now" misfire policies (FireOnceNow, FireNow, RescheduleNowWith*).
        // These set nextFireTimeUtc to ~now. "Reschedule next" policies (DoNothing,
        // RescheduleNextWith*) set it to a future schedule time where the existing code
        // already produces the correct ScheduledFireTimeUtc.
        var updatedTnft = tw.Trigger.NextFireTimeUtc;
        if (tw.Trigger is TriggerBase abstractTrigger
            && originalFireTime.HasValue && updatedTnft.HasValue
            && originalFireTime.Value != updatedTnft.Value
            && Math.Abs((updatedTnft.Value - now).TotalMilliseconds) < TriggerBase.FireNowMisfireDetectionThresholdMs)
        {
            abstractTrigger.MisfiredFromFireTimeUtc = originalFireTime;
        }

        if (!updatedTnft.HasValue)
        {
            tw.state = StoredTriggerState.Complete;
            await signaler.NotifySchedulerListenersFinalized(tw.Trigger).ConfigureAwait(false);

            // We do not remove the trigger that we applied the misfire for (since its next fire time has been
            // updated). Instead we remove a trigger with the same trigger key, but with no next fire time set.
            timeTriggers.Remove(tw);
        }
        else if (tnft.GetValueOrDefault() == updatedTnft.GetValueOrDefault())
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Get a handle to the next trigger to be fired, and mark it as 'reserved'
    /// by the calling scheduler.
    /// </summary>
    /// <seealso cref="ITrigger" />
    /// <inheritdoc />
    public async ValueTask<List<IOperableTrigger>> AcquireNextTriggers(
        TriggerAcquisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // return empty list if store has no triggers.
            if (timeTriggers.Count == 0)
            {
                return [];
            }

            List<IOperableTrigger> result = [];

            // Both sets stay null until something needs them. Only a job that disallows concurrent
            // execution fills the first, and only a trigger that is turned away fills the second, so on
            // the attempts that dominate a running scheduler neither is ever created.
            HashSet<JobKey>? acquiredJobKeysForNoConcurrentExec = null;
            HashSet<TriggerWrapper>? excludedTriggers = null;
            DateTimeOffset batchEnd = request.NoLaterThan;

            // execution limits will be modified during processing
            ExecutionSlots? executionSlots = request.ExecutionLimits?.CreateSlots(
                request.ExecutionLimits.HasClusterScopedLimits ? CollectInFlightExecutionGroupsNoLock() : null);

            // The names are compared against JobType.FullName, which is the same string the ADO store
            // writes into JOB_CLASS_NAME and compares its NOT IN clause against, so one exclusion set
            // means the same thing to both stores. Ordinal, because a type name is not prose.
            HashSet<string>? excludedJobTypeNames = request.ExcludedJobTypeNames is { Count: > 0 } names
                ? new HashSet<string>(names, StringComparer.Ordinal)
                : null;

            while (true)
            {
                var tw = timeTriggers.Min;
                if (tw is null)
                {
                    break;
                }

                // It would've been more efficient to only remove the trigger if we're really acquiring it, but
                // we need to remove it before we apply the misfire. It not, after having updated the trigger,
                // we'd attempt to remove the trigger with the new next fire time which would no longer match
                // the trigger in the 'timeTriggers' set.
                timeTriggers.Remove(tw);

                // Use a local for the next fire time to reduce number of interface calls.
                var tnft = tw.Trigger.NextFireTimeUtc;

                // When the trigger is not scheduled to fire, continue with the next trigger.
                if (!tnft.HasValue)
                {
                    continue;
                }

                if (await ApplyMisfireNoLock(tw).ConfigureAwait(false))
                {
                    // If - after applying the misfire policy - the trigger is still scheduled to fire, we'll
                    // add it back to the set of triggers. We cannot use the "cached" next fire time here as
                    // it has been updated in ApplyMisfire(TriggerWrapper tw).
                    if (tw.Trigger.NextFireTimeUtc is not null)
                    {
                        timeTriggers.Add(tw);
                    }

                    continue;
                }

                // The first trigger that is scheduled to fire after the window for the current batch completes
                // the current batch.
                if (tnft.GetValueOrDefault() > batchEnd)
                {
                    // Since we removed the trigger from 'timeTriggers' earlier, we now need to add it back.
                    timeTriggers.Add(tw);
                    break;
                }

                JobKey jobKey = tw.JobKey;

                // A trigger whose job is gone cannot be fired. Skipping it leaves it out of timeTriggers,
                // where it stays until something stores or resumes it again; throwing here would instead
                // take down the acquisition loop and stop every other trigger from firing.
                if (!jobsByKey.TryGetValue(jobKey, out var jobWrapper))
                {
                    logger.TriggerSkippedJobMissing(tw.TriggerKey, jobKey);
                    continue;
                }

                IJobDetail job = jobWrapper.JobDetail;

                // An excluded job type is declined for this attempt only, so the trigger goes back into
                // timeTriggers with the rest of the turned-away ones rather than being dropped: the next
                // request may carry a different exclusion set, and a trigger left out of timeTriggers
                // stays out until something stores or resumes it again.
                if (excludedJobTypeNames is not null && excludedJobTypeNames.Contains(job.JobType.FullName))
                {
                    excludedTriggers ??= [];
                    excludedTriggers.Add(tw);
                    continue;
                }

                // If trigger's job disallows concurrent execution and the job was already added to the result,
                // then we'll add the trigger to the list of excluded triggers (which we'll add back to the set
                // of time triggers after we've completed the current batch) and skip the trigger.
                if (job.ConcurrentExecutionDisallowed)
                {
                    acquiredJobKeysForNoConcurrentExec ??= [];
                    if (!acquiredJobKeysForNoConcurrentExec.Add(jobKey))
                    {
                        excludedTriggers ??= [];
                        excludedTriggers.Add(tw);
                        continue; // go to next trigger in store.
                    }
                }

                // Check execution group limits
                if (executionSlots is not null)
                {
                    // The trigger group goes along because the limits may be configured to stand in for
                    // an execution group the trigger does not carry.
                    if (!executionSlots.TryTake(tw.Trigger.ExecutionGroup, tw.TriggerKey.Group))
                    {
                        excludedTriggers ??= [];
                        excludedTriggers.Add(tw);
                        continue;
                    }
                }

                tw.state = StoredTriggerState.Acquired;
                tw.Trigger.FireInstanceId = GetFiredTriggerRecordId();

                // The reservation's own timestamp, which is what the ADO store writes into FIRED_TIME
                // when it inserts the ACQUIRED row; the execution listing reports it until the firing
                // starts and overwrites it with the execution's start.
                tw.acquiredAtUtc = timeProvider.GetUtcNow();

                IOperableTrigger trig = (IOperableTrigger) tw.Trigger.Clone();

                result.Add(trig);

                if (result.Count == request.MaxCount)
                {
                    break;
                }

                // Use the next fire time of the first acquired trigger to update the maximum next fire
                // time that we accept for this batch. We only perform this update if we want to acquire
                // more than one trigger.
                if (result.Count == 1)
                {
                    var now = timeProvider.GetUtcNow();
                    var nextFireTime = tnft.GetValueOrDefault();
                    var max = now > nextFireTime ? now : nextFireTime;

                    batchEnd = max + request.TimeWindow;
                }
            }

            // If we did excluded triggers to prevent ACQUIRE state due to DisallowConcurrentExecution, we need to add them back to store.
            if (excludedTriggers is not null)
            {
                foreach (var excludedTrigger in excludedTriggers)
                {
                    timeTriggers.Add(excludedTrigger);
                }
            }

            return result;
        }
        finally
        {
            lockObject.Release();
        }
    }

    /// <summary>
    /// Inform the <see cref="IJobStore" /> that the scheduler no longer plans to
    /// fire the given <see cref="ITrigger" />, that it had previously acquired
    /// (reserved).
    /// </summary>
    public async ValueTask ReleaseAcquiredTrigger(IOperableTrigger trigger, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Releasing means the scheduler is not going to run this fire after all, so anything recorded
            // for it has to go: the scheduler releases the whole batch when TriggersFired fails part-way,
            // and no completion will ever arrive for the fires it had already recorded. Note this does not
            // undo the blocking fan-out TriggersFired applies for a non-concurrent job — only
            // TriggeredJobComplete does that, which is why the scheduler uses it on every path where a job
            // actually started.
            ReleaseExecutionNoLock(trigger.Key, trigger.FireInstanceId);

            if (triggersByKey.TryGetValue(trigger.Key, out var tw) && tw.state == StoredTriggerState.Acquired)
            {
                tw.state = StoredTriggerState.Waiting;
                timeTriggers.Add(tw);
            }
        }
        finally
        {
            lockObject.Release();
        }
    }

    /// <summary>
    /// Inform the <see cref="IJobStore" /> that the scheduler is now firing the
    /// given <see cref="ITrigger" /> (executing its associated <see cref="IJob" />),
    /// that it had previously acquired (reserved).
    /// </summary>
    public async ValueTask<List<TriggerFiredResult>> TriggersFired(IReadOnlyCollection<IOperableTrigger> triggers, CancellationToken cancellationToken = default)
    {
        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<TriggerFiredResult> results = new(triggers.Count);

            foreach (IOperableTrigger trigger in triggers)
            {
                // was the trigger deleted since being acquired?
                if (!triggersByKey.TryGetValue(trigger.Key, out var tw))
                {
                    results.Add(TriggerFiredResult.NotFired);
                    continue;
                }

                // was the trigger completed, paused, blocked, etc. since being acquired?
                if (tw.state != StoredTriggerState.Acquired)
                {
                    results.Add(TriggerFiredResult.NotFired);
                    continue;
                }

                ICalendar? calendar = null;
                if (tw.Trigger.CalendarName is not null)
                {
                    calendarsByName.TryGetValue(tw.Trigger.CalendarName, out calendar);
                    if (calendar is null)
                    {
                        logger.TriggerReferencesMissingCalendar(tw.Trigger.Key, tw.Trigger.CalendarName);
                        results.Add(TriggerFiredResult.NotFired);
                        continue;
                    }
                }

                // Was the job deleted since the trigger was acquired? Checked here, with the other
                // bail-outs, because everything below mutates the trigger: once it has left timeTriggers
                // and been moved off Acquired, ReleaseAcquiredTrigger can no longer re-arm it and the
                // trigger would stop firing altogether.
                if (!jobsByKey.TryGetValue(trigger.JobKey, out var jobWrapper))
                {
                    results.Add(TriggerFiredResult.NotFired);
                    continue;
                }

                DateTimeOffset? prevFireTime = trigger.PreviousFireTimeUtc;

                // Read saved original fire time (set during ApplyMisfireNoLock if a misfire occurred)
                DateTimeOffset? scheduledFireTime = null;
                if (trigger is TriggerBase at)
                {
                    scheduledFireTime = at.MisfiredFromFireTimeUtc;
                    at.MisfiredFromFireTimeUtc = null;
                }
                if (tw.Trigger is TriggerBase twAt)
                {
                    twAt.MisfiredFromFireTimeUtc = null;
                }

                // in case trigger was replaced between acquiring and firing
                timeTriggers.Remove(tw);

                // The fire time this firing is for, read while it is still the trigger's next one — the
                // execution listing reports it, and Triggered() is about to move it on.
                DateTimeOffset? firingScheduledTime = trigger.NextFireTimeUtc;

                // call triggered on our copy, and the scheduler's copy
                tw.Trigger.Triggered(calendar);
                trigger.Triggered(calendar);
                // Deliberately not an "executing" state: this field decides whether the trigger can be
                // acquired and fired again, and TriggersFired/ReleaseAcquiredTrigger/the blocking fan-out
                // below all depend on it being Waiting or Blocked here. Executions are tracked separately,
                // in executingFireInstances.
                tw.state = StoredTriggerState.Waiting;

                var jobDetail = jobWrapper.JobDetail.Clone();
                TriggerFiredBundle bndle = new TriggerFiredBundle
                {
                    JobDetail = jobDetail,
                    Trigger = trigger,
                    Calendar = calendar,
                    Recovering = false,
                    FireTimeUtc = timeProvider.GetUtcNow(),
                    ScheduledFireTimeUtc = scheduledFireTime ?? trigger.PreviousFireTimeUtc,
                    PreviousFireTimeUtc = prevFireTime,
                    NextFireTimeUtc = trigger.NextFireTimeUtc,
                };

                IJobDetail job = bndle.JobDetail;

                if (job.ConcurrentExecutionDisallowed)
                {
                    var triggerWrappersForJob = GetTriggerWrappersForJobNoLock(job.Key);

                    for (var i = 0; i < triggerWrappersForJob.Count; i++)
                    {
                        var ttw = triggerWrappersForJob[i];

                        if (ttw.state == StoredTriggerState.Waiting)
                        {
                            ttw.state = StoredTriggerState.Blocked;
                        }

                        if (ttw.state == StoredTriggerState.Paused)
                        {
                            ttw.state = StoredTriggerState.PausedBlocked;
                        }

                        timeTriggers.Remove(ttw);
                    }

                    blockedJobs.Add(job.Key);
                }
                else if (tw.Trigger.NextFireTimeUtc is not null)
                {
                    timeTriggers.Add(tw);
                }

                // Recorded only once the bundle is guaranteed, so nothing above can leave an execution
                // behind that no completion will ever clear. Released in TriggeredJobComplete.
                if (!executingFireInstances.TryGetValue(tw.TriggerKey, out var fireInstances))
                {
                    fireInstances = [];
                    executingFireInstances[tw.TriggerKey] = fireInstances;
                }

                // The scheduled time recorded here is the fire time the schedule called for, read before
                // Triggered() advanced the trigger — which is what the ADO store writes into SCHED_TIME
                // at the same point, misfires included, and so is deliberately not the misfire's original
                // fire time that the bundle carries.
                fireInstances[trigger.FireInstanceId] = new FireInstanceEntry(
                    job.Key,
                    bndle.FireTimeUtc,
                    firingScheduledTime,
                    trigger.ExecutionGroup);

                results.Add(TriggerFiredResult.Fired(bndle));
            }

            return results;
        }
        finally
        {
            lockObject.Release();
        }
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
        // Which error notification the state changes below earned, raised once the lock is gone.
        // Listener code runs on this thread and may well call back into the store, which would
        // deadlock on the semaphore we are holding.
        ErrorNotification errorNotification = ErrorNotification.None;

        await lockObject.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // It's possible that the job is null if:
            //   1- it was deleted during execution
            //   2- RAMJobStore is being used only for volatile jobs / triggers
            //      from the JDBC job store

            if (jobsByKey.TryGetValue(jobDetail.Key, out var jw))
            {
                IJobDetail jd = jw.JobDetail;

                if (jobDetail.PersistJobDataAfterExecution)
                {
                    JobDataMap newData = jobDetail.JobDataMap;
                    newData = newData.Clone();
                    newData.ClearDirtyFlag();

                    // Asking the detail for a copy of itself, rather than rebuilding one through the
                    // builder, is what lets an implementation of the interface other than our own
                    // survive its first completion of a job that persists its data.
                    jd = jd.WithJobData(newData);
                    jw.JobDetail = jd;
                }

                if (jd.ConcurrentExecutionDisallowed)
                {
                    blockedJobs.Remove(jd.Key);

                    var triggerWrappersForJob = GetTriggerWrappersForJobNoLock(jd.Key);

                    for (var i = 0; i < triggerWrappersForJob.Count; i++)
                    {
                        var ttw = triggerWrappersForJob[i];

                        if (ttw.state == StoredTriggerState.Blocked)
                        {
                            ttw.state = StoredTriggerState.Waiting;
                            timeTriggers.Add(ttw);
                        }

                        if (ttw.state == StoredTriggerState.PausedBlocked)
                        {
                            ttw.state = StoredTriggerState.Paused;
                        }
                    }

                    await signaler.SignalSchedulingChange(candidateNewNextFireTimeUtc: null, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                // even if it was deleted, there may be cleanup to do
                blockedJobs.Remove(jobDetail.Key);
            }

            // Releases what TriggersFired recorded. Done before the trigger-deleted check below, and
            // unconditionally, so that an execution outliving its trigger still clears its entry.
            ReleaseExecutionNoLock(trigger.Key, trigger.FireInstanceId);

            // check for trigger deleted during execution...
            if (triggersByKey.TryGetValue(trigger.Key, out var tw))
            {
                if (triggerInstructionCode == SchedulerInstruction.DeleteTrigger)
                {
                    logger.TriggerDeleting();
                    DateTimeOffset? d = trigger.NextFireTimeUtc;
                    if (!d.HasValue)
                    {
                        // double check for possible reschedule within job
                        // execution, which would cancel the need to delete...
                        d = tw.Trigger.NextFireTimeUtc;
                        if (!d.HasValue)
                        {
                            await RemoveTriggerNoLock(trigger.Key, removeOrphanedJob: true, keepExecutions: false, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            logger.TriggerDeletionCancelled();
                        }
                    }
                    else
                    {
                        await RemoveTriggerNoLock(trigger.Key, removeOrphanedJob: true, keepExecutions: false, cancellationToken).ConfigureAwait(false);
                        await signaler.SignalSchedulingChange(candidateNewNextFireTimeUtc: null, cancellationToken).ConfigureAwait(false);
                    }
                }
                else if (triggerInstructionCode == SchedulerInstruction.SetTriggerComplete)
                {
                    tw.state = StoredTriggerState.Complete;
                    timeTriggers.Remove(tw);
                    await signaler.SignalSchedulingChange(candidateNewNextFireTimeUtc: null, cancellationToken).ConfigureAwait(false);
                }
                else if (triggerInstructionCode == SchedulerInstruction.SetTriggerError)
                {
                    logger.TriggerSetToError(trigger.Key);
                    tw.state = StoredTriggerState.Error;
                    errorNotification = ErrorNotification.Trigger;
                    await signaler.SignalSchedulingChange(candidateNewNextFireTimeUtc: null, cancellationToken).ConfigureAwait(false);
                }
                else if (triggerInstructionCode == SchedulerInstruction.SetAllJobTriggersError)
                {
                    logger.JobTriggersSetToError(trigger.JobKey);
                    SetAllTriggersOfJobToState(trigger.JobKey, StoredTriggerState.Error);
                    errorNotification = ErrorNotification.JobTriggers;
                    await signaler.SignalSchedulingChange(candidateNewNextFireTimeUtc: null, cancellationToken).ConfigureAwait(false);
                }
                else if (triggerInstructionCode == SchedulerInstruction.SetAllJobTriggersComplete)
                {
                    SetAllTriggersOfJobToState(trigger.JobKey, StoredTriggerState.Complete);
                    await signaler.SignalSchedulingChange(candidateNewNextFireTimeUtc: null, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            lockObject.Release();
        }

        if (errorNotification == ErrorNotification.Trigger)
        {
            await signaler.NotifySchedulerListenersTriggerInError(trigger.Key, cancellationToken).ConfigureAwait(false);
        }
        else if (errorNotification == ErrorNotification.JobTriggers)
        {
            await signaler.NotifySchedulerListenersTriggersInError(trigger.JobKey, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Which error notification a completed firing earned, deferred until the store's lock is released.
    /// </summary>
    private enum ErrorNotification
    {
        None,
        Trigger,
        JobTriggers
    }

    public TimeSpan EstimatedTimeToReleaseAndAcquireTrigger => TimeSpan.FromMilliseconds(5);

    public bool Clustered => false;

    public TimeSpan GetAcquireRetryDelay(int failureCount) => TimeSpan.FromMilliseconds(20);

    /// <summary>
    /// Sets the state of all triggers of job to specified state.
    /// </summary>
    /// <remarks>
    /// This method should only be executed while holding the instance level lock.
    /// </remarks>
    internal void SetAllTriggersOfJobToState(JobKey jobKey, StoredTriggerState state)
    {
        var triggerWrappersForJob = GetTriggerWrappersForJobNoLock(jobKey);

        for (var i = 0; i < triggerWrappersForJob.Count; i++)
        {
            var tw = triggerWrappersForJob[i];

            tw.state = state;
            if (state != StoredTriggerState.Waiting)
            {
                timeTriggers.Remove(tw);
            }
        }
    }

    /// <summary>
    /// Peeks the triggers.
    /// </summary>
    /// <returns></returns>
    internal async ValueTask<string> PeekTriggers()
    {
        StringBuilder str = new StringBuilder();

        await lockObject.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (TriggerWrapper tw in triggersByKey.Values)
            {
                str.Append(tw.Trigger.Key.Name);
                str.Append('/');
            }

            str.Append(" | ");

            foreach (TriggerWrapper tw in timeTriggers)
            {
                str.Append(tw.Trigger.Key.Name);
                str.Append("->");
            }
        }
        finally
        {
            lockObject.Release();
        }

        return str.ToString();
    }
}