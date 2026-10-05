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
/// <para>
/// Bounded by <see cref="ExecutionHistoryOptions" />: an age per result, a cap per job, and
/// <see cref="ExecutionHistoryOptions.MaxEntriesPerScheduler" /> as the backstop, so a busy scheduler
/// cannot grow it without limit and a quiet one stops showing executions from an arbitrary distance in
/// the past. In memory and per process, so every node of a cluster holds its own — which is why every
/// row carries the node that produced it.
/// </para>
/// <para>
/// Beside the rows it keeps a <see cref="JobRunStatus" /> per job, folded from every execution it
/// records and kept however the rows are trimmed.
/// </para>
/// </remarks>
internal sealed class InMemoryExecutionHistoryStore : IExecutionHistoryStore
{
    private readonly ConcurrentDictionary<string, ExecutionFeed> executionsByScheduler = new(StringComparer.OrdinalIgnoreCase);
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

        ExecutionHistoryOptions bounds = options.Value;

        // Named when it arrives without, so every row this store returns is one GetExecution can find;
        // and held to the input cap whoever recorded it, because that cap is what bounds this store's
        // memory for inputs.
        ExecutionHistoryEntry named = ExecutionHistoryPlugin.WithinInputCap(
            entry.EntryId is null ? entry with { EntryId = ExecutionHistoryPlugin.NewEntryId() } : entry,
            bounds.MaxInputBytes);

        ExecutionFeed feed = executionsByScheduler.GetOrAdd(named.SchedulerName, static _ => new ExecutionFeed());

        lock (feed)
        {
            feed.Entries.Add(named);
            feed.Fold(named, bounds.MaxEntriesPerScheduler);
            CapJob(feed.Entries, named, bounds.MaxEntriesPerJob);
            TrimExecutions(feed.Entries, bounds);
        }

        return default;
    }

    /// <remarks>
    /// The row as it was recorded, its log included: this store keeps the whole entry, so its listing
    /// carries the log too.
    /// </remarks>
    public ValueTask<ExecutionHistoryEntry?> GetExecution(string schedulerName, string entryId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);

        foreach (ExecutionHistoryEntry entry in ExecutionSnapshot(schedulerName))
        {
            if (string.Equals(entry.EntryId, entryId, StringComparison.Ordinal))
            {
                return new ValueTask<ExecutionHistoryEntry?>(entry);
            }
        }

        return new ValueTask<ExecutionHistoryEntry?>((ExecutionHistoryEntry?) null);
    }

    public ValueTask AddMisfire(MisfireHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        List<MisfireHistoryEntry> list = misfiresByScheduler.GetOrAdd(entry.SchedulerName, static _ => []);
        lock (list)
        {
            list.Add(entry);
            TrimMisfires(list, options.Value);
        }

        return default;
    }

    public ValueTask<PagedResult<ExecutionHistoryEntry>> QueryExecutions(ExecutionHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IEnumerable<ExecutionHistoryEntry> filtered = OnNode(
            ExecutionSnapshot(query.SchedulerName),
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

        if (query.FailedFinally is { } failedFinally)
        {
            // A failure the trigger answered with another attempt is not a final one, so it belongs
            // with the rest rather than with the occurrences that gave up.
            filtered = filtered.Where(x => (!x.Succeeded && !x.RetryScheduled) == failedFinally);
        }

        if (query.Job is { } job)
        {
            filtered = filtered.Where(x => IsJob(x, job));
        }

        if (query.FiredFrom is { } firedFrom)
        {
            filtered = filtered.Where(x => x.FiredAtUtc >= firedFrom);
        }

        if (query.FiredBefore is { } firedBefore)
        {
            filtered = filtered.Where(x => x.FiredAtUtc < firedBefore);
        }

        if (query.Results is { } results)
        {
            HashSet<JobRunResult> wanted = [.. results];
            filtered = filtered.Where(x => wanted.Contains(x.EffectiveResult));
        }

        return new ValueTask<PagedResult<ExecutionHistoryEntry>>(
            Page(filtered.OrderByDescending(static entry => entry.FiredAtUtc).ToList(), query));
    }

    public ValueTask<PagedResult<MisfireHistoryEntry>> QueryMisfires(MisfireHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IEnumerable<MisfireHistoryEntry> filtered = OnNode(
            MisfireSnapshot(query.SchedulerName),
            query.SchedulerInstanceId,
            static entry => entry.SchedulerInstanceId);

        if (!string.IsNullOrWhiteSpace(query.TriggerContains))
        {
            string normalizedTriggerFilter = query.TriggerContains.Trim();
            filtered = filtered.Where(x =>
                MatchesFilter(x.TriggerGroup, x.TriggerName, normalizedTriggerFilter));
        }

        if (query.Job is { } job)
        {
            filtered = filtered.Where(x => job.Equals(x.JobKey));
        }

        if (query.Reasons is { } reasons)
        {
            HashSet<MisfireReason> wanted = [.. reasons];
            filtered = filtered.Where(x => wanted.Contains(x.Reason));
        }

        return new ValueTask<PagedResult<MisfireHistoryEntry>>(
            Page(filtered.OrderByDescending(static entry => entry.MisfiredAtUtc).ToList(), query));
    }

    public ValueTask<int> CountMisfires(string schedulerName, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);

        int count = 0;
        foreach (MisfireHistoryEntry entry in MisfireSnapshot(schedulerName))
        {
            // A firing the overlap policy skipped, or a listener vetoed, is recorded beside the misfires,
            // and is not one.
            if (entry.MisfiredAtUtc >= since && entry.Reason == MisfireReason.Missed)
            {
                count++;
            }
        }

        return new ValueTask<int>(count);
    }

    public ValueTask<PagedResult<JobRunStatus>> QueryJobRunStatuses(JobRunStatusQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        List<JobRunStatus> statuses = [];
        if (executionsByScheduler.TryGetValue(query.SchedulerName, out ExecutionFeed? feed))
        {
            lock (feed)
            {
                if (query.Jobs is null)
                {
                    statuses.AddRange(feed.Statuses.Values);
                }
                else
                {
                    // Looked up rather than scanned, and each named once however often the caller named it.
                    foreach (JobKey job in new HashSet<JobKey>(query.Jobs))
                    {
                        if (feed.Statuses.TryGetValue(job, out JobRunStatus? status))
                        {
                            statuses.Add(status);
                        }
                    }
                }
            }
        }

        IEnumerable<JobRunStatus> filtered = statuses;
        if (query.Failing is { } failing)
        {
            filtered = filtered.Where(status => (status.ConsecutiveFailures > 0) == failing);
        }

        List<JobRunStatus> ordered = filtered
            .OrderBy(static status => status.Job.Group, StringComparer.Ordinal)
            .ThenBy(static status => status.Job.Name, StringComparer.Ordinal)
            .ToList();

        return new ValueTask<PagedResult<JobRunStatus>>(Page(ordered, query));
    }

    public ValueTask<JobRunStatus?> GetJobRunStatus(string schedulerName, JobKey jobKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);
        ArgumentNullException.ThrowIfNull(jobKey);

        if (executionsByScheduler.TryGetValue(schedulerName, out ExecutionFeed? feed))
        {
            lock (feed)
            {
                if (feed.Statuses.TryGetValue(jobKey, out JobRunStatus? status))
                {
                    return new ValueTask<JobRunStatus?>(status);
                }
            }
        }

        return new ValueTask<JobRunStatus?>((JobRunStatus?) null);
    }

    private static bool IsJob(ExecutionHistoryEntry entry, JobKey job)
    {
        return string.Equals(entry.JobName, job.Name, StringComparison.Ordinal)
               && string.Equals(entry.JobGroup, job.Group, StringComparison.Ordinal);
    }

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

    /// <summary>
    /// Takes a copy of what a scheduler holds, forgetting whatever has fallen out of bounds first.
    /// </summary>
    /// <remarks>
    /// Reading trims as writing does, because a scheduler that has stopped running jobs never writes
    /// again — and it is exactly that scheduler whose page would otherwise keep showing executions from
    /// an arbitrary distance in the past.
    /// </remarks>
    private List<ExecutionHistoryEntry> ExecutionSnapshot(string schedulerName)
    {
        List<ExecutionHistoryEntry> snapshot = [];

        if (executionsByScheduler.TryGetValue(schedulerName, out ExecutionFeed? feed))
        {
            lock (feed)
            {
                TrimExecutions(feed.Entries, options.Value);
                snapshot.AddRange(feed.Entries);
            }
        }

        return snapshot;
    }

    /// <inheritdoc cref="ExecutionSnapshot" />
    private List<MisfireHistoryEntry> MisfireSnapshot(string schedulerName)
    {
        List<MisfireHistoryEntry> snapshot = [];

        if (misfiresByScheduler.TryGetValue(schedulerName, out List<MisfireHistoryEntry>? list))
        {
            lock (list)
            {
                TrimMisfires(list, options.Value);
                snapshot.AddRange(list);
            }
        }

        return snapshot;
    }

    /// <summary>
    /// Applies the age bound for each row's result, then the scheduler-wide count.
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
    private void TrimExecutions(List<ExecutionHistoryEntry> list, ExecutionHistoryOptions bounds)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset? defaultCutoff = Cutoff(now, bounds.Retention);

        if (bounds.RetentionByResult.Count == 0)
        {
            if (defaultCutoff is { } cutoff)
            {
                list.RemoveAll(entry => entry.FiredAtUtc < cutoff);
            }
        }
        else
        {
            Dictionary<JobRunResult, DateTimeOffset?> cutoffs = new(bounds.RetentionByResult.Count);
            foreach (KeyValuePair<JobRunResult, TimeSpan> tier in bounds.RetentionByResult)
            {
                cutoffs[tier.Key] = Cutoff(now, tier.Value);
            }

            list.RemoveAll(entry =>
            {
                DateTimeOffset? cutoff = cutoffs.TryGetValue(entry.EffectiveResult, out DateTimeOffset? tierCutoff)
                    ? tierCutoff
                    : defaultCutoff;

                return entry.FiredAtUtc < cutoff;
            });
        }

        if (list.Count > bounds.MaxEntriesPerScheduler)
        {
            list.RemoveRange(0, list.Count - bounds.MaxEntriesPerScheduler);
        }
    }

    /// <summary>
    /// Applies the misfire feed's age bound, then its count.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="TrimExecutions" path="/remarks" />
    /// </remarks>
    private void TrimMisfires(List<MisfireHistoryEntry> list, ExecutionHistoryOptions bounds)
    {
        if (Cutoff(timeProvider.GetUtcNow(), bounds.MisfireRetention ?? bounds.Retention) is { } cutoff)
        {
            list.RemoveAll(entry => entry.MisfiredAtUtc < cutoff);
        }

        if (list.Count > bounds.MaxEntriesPerScheduler)
        {
            list.RemoveRange(0, list.Count - bounds.MaxEntriesPerScheduler);
        }
    }

    /// <summary>
    /// The oldest instant an age keeps, or <see langword="null" /> for an age that bounds nothing.
    /// </summary>
    /// <remarks>
    /// An age reaching past <see cref="DateTimeOffset.MinValue" /> — <see cref="TimeSpan.MaxValue" />, to
    /// keep failures for good — keeps everything rather than overflowing.
    /// </remarks>
    private static DateTimeOffset? Cutoff(DateTimeOffset now, TimeSpan age)
    {
        if (age <= TimeSpan.Zero)
        {
            return null;
        }

        return age >= now - DateTimeOffset.MinValue ? DateTimeOffset.MinValue : now - age;
    }

    /// <summary>
    /// Keeps at most <paramref name="maxPerJob" /> rows of the job <paramref name="added" /> belongs to,
    /// its failures not counted, dropping the earliest-fired first.
    /// </summary>
    /// <remarks>
    /// Only the job just written to can have gone over, so only it is counted: a write costs a scan of
    /// the feed, not a sort of it.
    /// </remarks>
    private static void CapJob(List<ExecutionHistoryEntry> list, ExecutionHistoryEntry added, int maxPerJob)
    {
        if (maxPerJob <= 0 || added.EffectiveResult == JobRunResult.Failed)
        {
            return;
        }

        int counted = 0;
        foreach (ExecutionHistoryEntry entry in list)
        {
            if (CountsTowardsCap(entry, added))
            {
                counted++;
            }
        }

        while (counted > maxPerJob)
        {
            int oldest = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (CountsTowardsCap(list[i], added) && (oldest < 0 || list[i].FiredAtUtc < list[oldest].FiredAtUtc))
                {
                    oldest = i;
                }
            }

            list.RemoveAt(oldest);
            counted--;
        }
    }

    private static bool CountsTowardsCap(ExecutionHistoryEntry entry, ExecutionHistoryEntry added)
    {
        return entry.EffectiveResult != JobRunResult.Failed
               && string.Equals(entry.JobName, added.JobName, StringComparison.Ordinal)
               && string.Equals(entry.JobGroup, added.JobGroup, StringComparison.Ordinal);
    }

    private static bool MatchesFilter(string group, string name, string filter)
    {
        string key = group + "." + name;
        return key.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               group.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               name.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One scheduler's executions and the per-job statuses folded from them, locked as one.
    /// </summary>
    /// <remarks>
    /// One lock for both, so a status never counts a row the feed has not received yet, and a reader of
    /// either sees the same moment.
    /// </remarks>
    private sealed class ExecutionFeed
    {
        public List<ExecutionHistoryEntry> Entries { get; } = [];

        public Dictionary<JobKey, JobRunStatus> Statuses { get; } = [];

        /// <summary>
        /// Folds one row into its job's status, then drops the statuses of the jobs that ran longest ago
        /// until at most <paramref name="maxStatuses" /> remain.
        /// </summary>
        public void Fold(ExecutionHistoryEntry entry, int maxStatuses)
        {
            JobKey job = new(entry.JobName, entry.JobGroup);
            Statuses.TryGetValue(job, out JobRunStatus? current);
            Statuses[job] = JobRunStatusFold.Apply(current, entry);

            while (Statuses.Count > Math.Max(maxStatuses, 0))
            {
                JobRunStatus? stalest = null;
                foreach (JobRunStatus status in Statuses.Values)
                {
                    if (stalest is null || status.LastFiredAtUtc < stalest.LastFiredAtUtc)
                    {
                        stalest = status;
                    }
                }

                Statuses.Remove(stalest!.Job);
            }
        }
    }
}
