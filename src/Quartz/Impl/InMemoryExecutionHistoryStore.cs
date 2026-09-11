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

using Microsoft.Extensions.Options;

using Quartz.Extensibility;

namespace Quartz.Impl;

/// <summary>
/// The per-process execution history Quartz keeps when nothing else is registered.
/// </summary>
/// <remarks>
/// Bounded twice: by <see cref="ExecutionHistoryOptions.MaxEntriesPerScheduler" />, so a busy scheduler
/// cannot grow it without limit, and by <see cref="ExecutionHistoryOptions.Retention" />, so a quiet one
/// stops showing executions from an arbitrary distance in the past. In memory and per process, so every
/// node of a cluster holds its own — which is why every row carries the node that produced it.
/// </remarks>
internal sealed class InMemoryExecutionHistoryStore : IExecutionHistoryStore
{
    private readonly ConcurrentDictionary<string, List<ExecutionHistoryEntry>> executionsByScheduler = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<MisfireHistoryEntry>> misfiresByScheduler = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider timeProvider;
    private readonly IOptions<ExecutionHistoryOptions> options;

    /// <param name="options">The bounds the history is kept under.</param>
    /// <param name="timeProvider">
    /// The clock the retention window is measured on — the scheduler's, so a test that moves it forward
    /// sees the store forget. Defaults to the system clock, for a container that holds none.
    /// </param>
    public InMemoryExecutionHistoryStore(IOptions<ExecutionHistoryOptions> options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        this.options = options;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ValueTask AddExecution(ExecutionHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Record(executionsByScheduler, entry.SchedulerName, entry, FiredAt);
        return default;
    }

    public ValueTask AddMisfire(MisfireHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Record(misfiresByScheduler, entry.SchedulerName, entry, MisfiredAt);
        return default;
    }

    public ValueTask<PagedResult<ExecutionHistoryEntry>> QueryExecutions(ExecutionHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IEnumerable<ExecutionHistoryEntry> filtered = OnNode(
            Snapshot(executionsByScheduler, query.SchedulerName, FiredAt),
            query.SchedulerInstanceId,
            static entry => entry.SchedulerInstanceId);

        if (!string.IsNullOrWhiteSpace(query.JobContains))
        {
            string normalizedJobFilter = query.JobContains.Trim();
            filtered = filtered.Where(x =>
                MatchesFilter(x.JobGroup, x.JobName, normalizedJobFilter));
        }

        if (!string.IsNullOrWhiteSpace(query.TriggerContains))
        {
            string normalizedTriggerFilter = query.TriggerContains.Trim();
            filtered = filtered.Where(x =>
                MatchesFilter(x.TriggerGroup, x.TriggerName, normalizedTriggerFilter));
        }

        return new ValueTask<PagedResult<ExecutionHistoryEntry>>(
            Page(filtered.OrderByDescending(static entry => entry.FiredAtUtc).ToList(), query));
    }

    public ValueTask<PagedResult<MisfireHistoryEntry>> QueryMisfires(MisfireHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IEnumerable<MisfireHistoryEntry> filtered = OnNode(
            Snapshot(misfiresByScheduler, query.SchedulerName, MisfiredAt),
            query.SchedulerInstanceId,
            static entry => entry.SchedulerInstanceId);

        if (!string.IsNullOrWhiteSpace(query.TriggerContains))
        {
            string normalizedTriggerFilter = query.TriggerContains.Trim();
            filtered = filtered.Where(x =>
                MatchesFilter(x.TriggerGroup, x.TriggerName, normalizedTriggerFilter));
        }

        return new ValueTask<PagedResult<MisfireHistoryEntry>>(
            Page(filtered.OrderByDescending(static entry => entry.MisfiredAtUtc).ToList(), query));
    }

    public ValueTask<int> CountMisfires(string schedulerName, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);

        int count = 0;
        foreach (MisfireHistoryEntry entry in Snapshot(misfiresByScheduler, schedulerName, MisfiredAt))
        {
            if (entry.MisfiredAtUtc >= since)
            {
                count++;
            }
        }

        return new ValueTask<int>(count);
    }

    private static DateTimeOffset FiredAt(ExecutionHistoryEntry entry) => entry.FiredAtUtc;

    private static DateTimeOffset MisfiredAt(MisfireHistoryEntry entry) => entry.MisfiredAtUtc;

    private static IEnumerable<T> OnNode<T>(List<T> entries, string? schedulerInstanceId, Func<T, string> nodeOf)
    {
        if (string.IsNullOrWhiteSpace(schedulerInstanceId))
        {
            return entries;
        }

        string normalized = schedulerInstanceId.Trim();
        return entries.Where(entry => string.Equals(nodeOf(entry), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static PagedResult<T> Page<T>(List<T> ordered, PagedQuery query)
    {
        // Skip past the end is an empty page rather than an error, the same answer a job store gives.
        int skip = Math.Min(query.Skip, ordered.Count);
        List<T> pageItems = ordered.Skip(skip).Take(query.Take).ToList();
        bool hasMore = skip + pageItems.Count < ordered.Count;
        return new PagedResult<T>(pageItems, hasMore, ordered.Count);
    }

    private void Record<T>(ConcurrentDictionary<string, List<T>> byScheduler, string schedulerName, T entry, Func<T, DateTimeOffset> timeOf)
    {
        List<T> list = byScheduler.GetOrAdd(schedulerName, static _ => []);
        lock (list)
        {
            list.Add(entry);
            Trim(list, timeOf);
        }
    }

    /// <summary>
    /// Takes a copy of what a scheduler holds, forgetting whatever has fallen out of bounds first.
    /// </summary>
    /// <remarks>
    /// Reading trims as writing does, because a scheduler that has stopped running jobs never writes
    /// again — and it is exactly that scheduler whose page would otherwise keep showing executions from
    /// an arbitrary distance in the past.
    /// </remarks>
    private List<T> Snapshot<T>(ConcurrentDictionary<string, List<T>> byScheduler, string schedulerName, Func<T, DateTimeOffset> timeOf)
    {
        List<T> snapshot = [];

        if (byScheduler.TryGetValue(schedulerName, out List<T>? list))
        {
            lock (list)
            {
                Trim(list, timeOf);
                snapshot.AddRange(list);
            }
        }

        return snapshot;
    }

    /// <summary>
    /// Applies both bounds, age before count.
    /// </summary>
    /// <remarks>
    /// The age pass is a full scan rather than a walk in from the oldest end: entries are appended in
    /// arrival order, which is only the same as timestamp order while one node is writing, and a store
    /// fed by a whole cluster would leave a late arrival stranded behind a fresher one forever.
    /// <para>
    /// The bounds are read per call rather than captured in the constructor, so a deployment that
    /// reconfigures them — the dashboard writes its own two settings onto these — is not held to
    /// whatever they were when the container was built.
    /// </para>
    /// </remarks>
    private void Trim<T>(List<T> list, Func<T, DateTimeOffset> timeOf)
    {
        ExecutionHistoryOptions bounds = options.Value;

        if (bounds.Retention > TimeSpan.Zero)
        {
            DateTimeOffset cutoff = timeProvider.GetUtcNow() - bounds.Retention;
            list.RemoveAll(entry => timeOf(entry) < cutoff);
        }

        if (list.Count > bounds.MaxEntriesPerScheduler)
        {
            list.RemoveRange(0, list.Count - bounds.MaxEntriesPerScheduler);
        }
    }

    private static bool MatchesFilter(string group, string name, string filter)
    {
        string key = group + "." + name;
        return key.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               group.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               name.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}
