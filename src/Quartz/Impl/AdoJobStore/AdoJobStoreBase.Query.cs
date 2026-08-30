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
    /// Check existence of a given job.
    /// </summary>
    protected ValueTask<bool> JobExists(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            () => Delegate.JobExists(conn, jobKey, cancellationToken),
            $"determine job existence ({jobKey})");
    }

    /// <summary>
    /// Check existence of a given trigger.
    /// </summary>
    protected ValueTask<bool> TriggerExists(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            () => Delegate.TriggerExists(conn, triggerKey, cancellationToken),
            $"determine trigger existence ({triggerKey})");
    }

    /// <summary>
    /// Retrieve the <see cref="IJobDetail" /> for the given
    /// <see cref="IJob" />.
    /// </summary>
    /// <param name="jobKey">The key identifying the job.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The desired <see cref="IJob" />, or null if there is no match.</returns>
    public ValueTask<IJobDetail?> GetJob(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        // no locks necessary for read...
        return ExecuteWithoutLock(conn => GetJob(conn, jobKey, cancellationToken), cancellationToken);
    }

    protected ValueTask<IJobDetail?> GetJob(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            () => Delegate.SelectJobDetail(conn, jobKey, TypeLoader, cancellationToken),
            "retrieve job",
            ReadFailureReason);
    }

    /// <summary>
    /// Retrieve the given <see cref="ITrigger" />.
    /// </summary>
    /// <param name="triggerKey">The key identifying the trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The desired <see cref="ITrigger" />, or null if there is no match.</returns>
    public ValueTask<IOperableTrigger?> GetTrigger(TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithoutLock( // no locks necessary for read...
            conn => GetTrigger(conn, triggerKey, cancellationToken),
            cancellationToken);
    }

    protected ValueTask<IOperableTrigger?> GetTrigger(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            () => Delegate.SelectTrigger(conn, triggerKey, cancellationToken),
            "retrieve trigger");
    }

    protected ValueTask<bool> CalendarExists(
        ConnectionAndTransactionHolder conn,
        string calendarName,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            () => Delegate.CalendarExists(conn, calendarName, cancellationToken),
            $"determine calendar existence ({calendarName})");
    }

    /// <summary>
    /// Retrieve the given <see cref="ITrigger" />.
    /// </summary>
    /// <param name="calendarName">The name of the <see cref="ICalendar" /> to be retrieved.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The desired <see cref="ICalendar" />, or null if there is no match.</returns>
    public ValueTask<ICalendar?> GetCalendar(string calendarName, CancellationToken cancellationToken = default)
    {
        return ExecuteWithoutLock( // no locks necessary for read...
            conn => GetCalendar(conn, calendarName, cancellationToken),
            cancellationToken);
    }

    protected async ValueTask<ICalendar?> GetCalendar(
        ConnectionAndTransactionHolder conn,
        string calendarName,
        CancellationToken cancellationToken = default)
    {
        // all calendars are persistent, but we lazy-cache them during run
        // time as long as we aren't running clustered.
        ICalendar? calendar = null;
        if (!Clustered)
        {
            calendarCache.TryGetValue(calendarName, out calendar);
        }
        if (calendar is not null)
        {
            return calendar;
        }

        return await Guarded(
            async () =>
            {
                ICalendar? loaded = await Delegate.SelectCalendar(conn, calendarName, cancellationToken).ConfigureAwait(false);
                if (!Clustered)
                {
                    calendarCache[calendarName] = loaded; // lazy-cache...
                }
                return loaded;
            },
            "retrieve calendar",
            ReadFailureReason).ConfigureAwait(false);
    }

    protected ValueTask<List<JobKey>> GetJobNames(ConnectionAndTransactionHolder conn, GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        return Guarded(
            () => Delegate.SelectJobKeysInGroup(conn, matcher, cancellationToken),
            "obtain job names");
    }

    /// <summary>
    /// Determine whether a <see cref="IJob"/> with the given identifier already
    /// exists within the scheduler.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="jobKey">the identifier to check for</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if a Job exists with the given identifier</returns>
    public ValueTask<bool> Exists(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithoutLock( // no locks necessary for read...
            conn => Exists(conn, jobKey, cancellationToken), cancellationToken);
    }

    protected ValueTask<bool> Exists(
        ConnectionAndTransactionHolder conn,
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            () => Delegate.JobExists(conn, jobKey, cancellationToken),
            "check for existence of job");
    }

    /// <summary>
    /// Determine whether a <see cref="ITrigger" /> with the given identifier already
    /// exists within the scheduler.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="triggerKey">the identifier to check for</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if a Trigger exists with the given identifier</returns>
    public ValueTask<bool> Exists(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithoutLock( // no locks necessary for read...
            conn => Exists(conn, triggerKey, cancellationToken), cancellationToken);
    }

    protected ValueTask<bool> Exists(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        // "of trigger", where this used to say "of job": the message was a copy of the overload above,
        // and named the wrong kind of thing to go looking for.
        return Guarded(
            () => Delegate.TriggerExists(conn, triggerKey, cancellationToken),
            "check for existence of trigger");
    }

    /// <summary>
    /// Determine whether an <see cref="ICalendar" /> with the given name already exists within the
    /// store.
    /// </summary>
    /// <remarks>
    /// Runs <c>CalendarExists</c>, which selects a constant rather than the calendar blob, so the answer
    /// costs an index probe instead of a read and a deserialization. The calendar cache is deliberately
    /// not consulted: it holds what <see cref="GetCalendar(string, CancellationToken)" /> loaded, so it can answer "yes" but never
    /// "no", and a member that had to fall through to the database half the time would be two code paths
    /// where one indexed statement does.
    /// </remarks>
    /// <param name="calendarName">the name to check for</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>true if a calendar is stored under the given name</returns>
    public ValueTask<bool> Exists(
        string calendarName,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithoutLock( // no locks necessary for read...
            conn => CalendarExists(conn, calendarName, cancellationToken), cancellationToken);
    }

    protected ValueTask<List<string>> GetTriggerGroupNames(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default)
    {
        return Guarded(
            () => Delegate.SelectTriggerGroupNames(conn, GroupMatcher<TriggerKey>.AnyGroup(), cancellationToken),
            "obtain trigger groups");
    }

    /// <inheritdoc />
    public ValueTask<PagedResult<JobHeader>> QueryJobs(JobQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // no locks necessary for read...
        return ExecuteWithoutLock(conn => QueryJobs(conn, query, cancellationToken), cancellationToken);
    }

    protected ValueTask<PagedResult<JobHeader>> QueryJobs(
        ConnectionAndTransactionHolder conn,
        JobQuery query,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            () => Delegate.SelectJobHeaders(conn, query, cancellationToken),
            "query jobs");
    }

    /// <inheritdoc />
    public ValueTask<PagedResult<TriggerHeader>> QueryTriggers(TriggerQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // no locks necessary for read...
        return ExecuteWithoutLock(conn => QueryTriggers(conn, query, cancellationToken), cancellationToken);
    }

    protected ValueTask<PagedResult<TriggerHeader>> QueryTriggers(
        ConnectionAndTransactionHolder conn,
        TriggerQuery query,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            () => Delegate.SelectTriggerHeaders(conn, query, cancellationToken),
            "query triggers");
    }

    /// <inheritdoc />
    public ValueTask<PagedResult<JobGroup>> QueryJobGroups(JobGroupQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // no locks necessary for read...
        return ExecuteWithoutLock(conn => QueryJobGroups(conn, query, cancellationToken), cancellationToken);
    }

    protected ValueTask<PagedResult<JobGroup>> QueryJobGroups(
        ConnectionAndTransactionHolder conn,
        JobGroupQuery query,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            () => Delegate.SelectJobGroups(conn, query, cancellationToken),
            "query job groups");
    }

    /// <inheritdoc />
    public ValueTask<PagedResult<TriggerGroup>> QueryTriggerGroups(TriggerGroupQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // no locks necessary for read...
        return ExecuteWithoutLock(conn => QueryTriggerGroups(conn, query, cancellationToken), cancellationToken);
    }

    protected ValueTask<PagedResult<TriggerGroup>> QueryTriggerGroups(
        ConnectionAndTransactionHolder conn,
        TriggerGroupQuery query,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            () => Delegate.SelectTriggerGroups(conn, query, cancellationToken),
            "query trigger groups");
    }

    /// <inheritdoc />
    public ValueTask<PagedResult<string>> QueryCalendarNames(CalendarQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // no locks necessary for read...
        return ExecuteWithoutLock(conn => QueryCalendarNames(conn, query, cancellationToken), cancellationToken);
    }

    protected ValueTask<PagedResult<string>> QueryCalendarNames(
        ConnectionAndTransactionHolder conn,
        CalendarQuery query,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            () => Delegate.SelectCalendarNames(conn, query, cancellationToken),
            "query calendar names");
    }

    /// <inheritdoc />
    public ValueTask<PagedResult<FireInstance>> QueryFireInstances(FireInstanceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // no locks necessary for read... and the rows of every node are equally visible, which is what
        // makes this listing cluster-wide
        return ExecuteWithoutLock(conn => QueryFireInstances(conn, query, cancellationToken), cancellationToken);
    }

    protected ValueTask<PagedResult<FireInstance>> QueryFireInstances(
        ConnectionAndTransactionHolder conn,
        FireInstanceQuery query,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            () => Delegate.SelectFireInstances(conn, query, cancellationToken),
            "query fire instances");
    }

    /// <inheritdoc />
    public ValueTask<List<ClusterNode>> QueryClusterNodes(CancellationToken cancellationToken = default)
    {
        if (!Clustered)
        {
            // A store that is not clustered never runs the check-in loop, so SCHEDULER_STATE holds
            // nothing of this scheduler's — reading it would answer with another cluster's rows or with
            // none. The one node there is, is this one.
            return new ValueTask<List<ClusterNode>>(new List<ClusterNode>
            {
                new ClusterNode(InstanceId, LastCheckInUtc: null, CheckInInterval: null, ClusterNodeState.Alive, IsCurrentNode: true)
            });
        }

        // no locks necessary for read... and the rows of every node are equally visible, which is what
        // makes this listing cluster-wide
        return ExecuteWithoutLock(conn => QueryClusterNodes(conn, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Reads SCHEDULER_STATE and classifies every row, current node first.
    /// </summary>
    /// <remarks>
    /// The verdict comes from <see cref="CalcFailedIfAfter" /> — the predicate
    /// <see cref="FindFailedInstances" /> decides recovery with — rather than from a formula of its own,
    /// so the listing and the recovery sweep can never disagree about which nodes are dead. The current
    /// node's own row is judged the same way as any other: a node that has stalled its own check-in
    /// reports itself honestly rather than flattering itself.
    /// </remarks>
    protected async ValueTask<List<ClusterNode>> QueryClusterNodes(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        List<SchedulerStateRecord> states = await Guarded(
            () => Delegate.SelectSchedulerStateRecords(conn, instanceId: null, cancellationToken),
            "query cluster nodes").ConfigureAwait(false);

        DateTimeOffset now = timeProvider.GetUtcNow();
        List<ClusterNode> nodes = new(states.Count);
        ClusterNode? currentNode = null;

        foreach (SchedulerStateRecord record in states)
        {
            bool isCurrentNode = string.Equals(record.SchedulerInstanceId, InstanceId, StringComparison.Ordinal);
            ClusterNode node = new(
                record.SchedulerInstanceId,
                record.CheckinTimestamp,
                record.CheckinInterval,
                ClassifyClusterNode(record, now),
                isCurrentNode);

            if (isCurrentNode)
            {
                currentNode = node;
            }
            else
            {
                nodes.Add(node);
            }
        }

        nodes.Sort(static (left, right) => string.CompareOrdinal(left.InstanceId, right.InstanceId));

        // The current node is listed whether or not its row exists yet: it has not written one before its
        // first check-in, and another node may have swept it away, but it is demonstrably running.
        currentNode ??= new ClusterNode(InstanceId, LastCheckInUtc: null, CheckInInterval: null, ClusterNodeState.Alive, IsCurrentNode: true);
        nodes.Insert(0, currentNode);

        return nodes;
    }

    private ClusterNodeState ClassifyClusterNode(SchedulerStateRecord record, DateTimeOffset now)
    {
        if (CalcFailedIfAfter(record) < now)
        {
            return ClusterNodeState.Failed;
        }

        return record.CheckinTimestamp + record.CheckinInterval < now ? ClusterNodeState.Overdue : ClusterNodeState.Alive;
    }

    /// <inheritdoc />
    public ValueTask<List<IJobDetail>> GetJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobKeys);

        // no locks necessary for read...
        return ExecuteWithoutLock(conn => GetJobs(conn, jobKeys, cancellationToken), cancellationToken);
    }

    protected ValueTask<List<IJobDetail>> GetJobs(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<JobKey> jobKeys,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            () => Delegate.SelectJobDetails(conn, jobKeys, TypeLoader, cancellationToken),
            "retrieve jobs",
            ReadFailureReason);
    }

    /// <inheritdoc />
    public ValueTask<List<IOperableTrigger>> GetTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triggerKeys);

        // no locks necessary for read...
        return ExecuteWithoutLock(conn => GetTriggers(conn, triggerKeys, cancellationToken), cancellationToken);
    }

    protected ValueTask<List<IOperableTrigger>> GetTriggers(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        return Guarded(
            () => Delegate.SelectTriggers(conn, triggerKeys, cancellationToken),
            "retrieve triggers");
    }

    /// <summary>
    /// Get all of the Triggers that are associated to the given Job.
    /// </summary>
    /// <remarks>
    /// If there are no matches, a zero-length array should be returned.
    /// </remarks>
    public ValueTask<List<IOperableTrigger>> GetTriggersForJob(
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        // no locks necessary for read...
        return ExecuteWithoutLock(conn => GetTriggersForJob(conn, jobKey, cancellationToken), cancellationToken);
    }

    protected ValueTask<List<IOperableTrigger>> GetTriggersForJob(ConnectionAndTransactionHolder conn, JobKey jobKey, CancellationToken cancellationToken = default)
    {
        return Guarded(
            () => Delegate.SelectTriggersForJob(conn, jobKey, cancellationToken),
            "obtain triggers for job");
    }
}
