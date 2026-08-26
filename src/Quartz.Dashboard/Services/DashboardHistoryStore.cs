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

namespace Quartz.Dashboard.Services;

/// <remarks>
/// <see cref="Duration" /> is a <see cref="TimeSpan" />, as every other duration the dashboard shows
/// is; it used to be a count of whole milliseconds, which lost every execution shorter than one.
/// </remarks>
/// <param name="SchedulerName">The scheduler the execution belongs to.</param>
/// <param name="SchedulerInstanceId">
/// The node that ran it. Every node of a cluster keeps its own history of its own executions, so
/// without this a row cannot say which machine it came from — and a store shared across the cluster
/// cannot say it either.
/// </param>
/// <param name="JobGroup">The group of the job that ran.</param>
/// <param name="JobName">The name of the job that ran.</param>
/// <param name="TriggerGroup">The group of the trigger that fired it.</param>
/// <param name="TriggerName">The name of the trigger that fired it.</param>
/// <param name="FiredAtUtc">When the execution fired.</param>
/// <param name="Duration">How long the job took.</param>
/// <param name="Succeeded">Whether the job completed without throwing.</param>
/// <param name="ExceptionMessage">What it threw, or <see langword="null" /> when it succeeded.</param>
public sealed record DashboardHistoryEntry(
    string SchedulerName,
    string SchedulerInstanceId,
    string JobGroup,
    string JobName,
    string TriggerGroup,
    string TriggerName,
    DateTimeOffset FiredAtUtc,
    TimeSpan Duration,
    bool Succeeded,
    string? ExceptionMessage);

/// <summary>
/// One trigger that missed its scheduled firing, as the scheduler reported it.
/// </summary>
/// <remarks>
/// A misfire is not an execution — nothing ran — so it is recorded beside the executions rather than
/// among them, under the same bounds.
/// </remarks>
/// <param name="SchedulerName">The scheduler the trigger belongs to.</param>
/// <param name="SchedulerInstanceId">The node that noticed the misfire.</param>
/// <param name="TriggerGroup">The group of the trigger that missed its firing.</param>
/// <param name="TriggerName">The name of the trigger that missed its firing.</param>
/// <param name="JobKey">
/// The job the trigger points at, or <see langword="null" /> when the trigger names none.
/// </param>
/// <param name="MisfiredAtUtc">When the misfire was noticed, on the scheduler's clock.</param>
/// <param name="ScheduledFireTimeUtc">
/// The firing that was missed, or <see langword="null" /> when the trigger had no next firing left to
/// name. The scheduler reports a misfire before it applies the trigger's misfire instruction, so this
/// is the time the trigger was still due at.
/// </param>
public sealed record DashboardMisfireEntry(
    string SchedulerName,
    string SchedulerInstanceId,
    string TriggerGroup,
    string TriggerName,
    JobKeyDto? JobKey,
    DateTimeOffset MisfiredAtUtc,
    DateTimeOffset? ScheduledFireTimeUtc);

/// <summary>
/// Where the dashboard's execution history and misfire feed live.
/// </summary>
/// <remarks>
/// The seam an application replaces to keep history somewhere that survives a restart — the shipped
/// implementation is per-process and in-memory, so every node of a cluster holds its own. Both feeds
/// carry the node that produced each row, which is what makes a shared implementation readable.
/// </remarks>
public interface IDashboardHistoryStore
{
    ValueTask Add(DashboardHistoryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of the recorded executions, newest first.
    /// </summary>
    ValueTask<PagedResult<DashboardHistoryEntry>> GetPage(DashboardHistoryQuery query, CancellationToken cancellationToken = default);

    ValueTask AddMisfire(DashboardMisfireEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of the recorded misfires, newest first.
    /// </summary>
    ValueTask<PagedResult<DashboardMisfireEntry>> GetMisfires(DashboardMisfireQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many misfires the scheduler has recorded since <paramref name="since" />.
    /// </summary>
    /// <remarks>
    /// A count rather than a page, because a summary asks "how bad is it right now" and a store that
    /// keeps history in a database can answer that with one <c>COUNT(*)</c> instead of loading rows it
    /// would throw away.
    /// </remarks>
    ValueTask<int> CountMisfires(string schedulerName, DateTimeOffset since, CancellationToken cancellationToken = default);
}

/// <summary>
/// The per-process history the dashboard keeps when nothing else is registered.
/// </summary>
/// <remarks>
/// Bounded twice: by <see cref="QuartzDashboardOptions.HistoryMaxEntriesPerScheduler" />, so a busy
/// scheduler cannot grow it without limit, and by <see cref="QuartzDashboardOptions.HistoryRetention" />,
/// so a quiet one stops showing executions from an arbitrary distance in the past. The count bound alone
/// left the second case unanswered, which is what <see href="https://github.com/quartznet/quartznet/issues/3422" />
/// reported.
/// </remarks>
internal sealed class DashboardHistoryStore : IDashboardHistoryStore
{
    private readonly ConcurrentDictionary<string, List<DashboardHistoryEntry>> executionsByScheduler = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<DashboardMisfireEntry>> misfiresByScheduler = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider timeProvider;
    private readonly int maxEntriesPerScheduler;
    private readonly TimeSpan retention;

    /// <param name="options">The bounds the history is kept under.</param>
    /// <param name="timeProvider">
    /// The clock the retention window is measured on — the scheduler's, so a test that moves it forward
    /// sees the store forget.
    /// </param>
    public DashboardHistoryStore(IOptions<QuartzDashboardOptions> options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;
        maxEntriesPerScheduler = options.Value.HistoryMaxEntriesPerScheduler;
        retention = options.Value.HistoryRetention;
    }

    public ValueTask Add(DashboardHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Record(executionsByScheduler, entry.SchedulerName, entry, FiredAt);
        return default;
    }

    public ValueTask AddMisfire(DashboardMisfireEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Record(misfiresByScheduler, entry.SchedulerName, entry, MisfiredAt);
        return default;
    }

    public ValueTask<PagedResult<DashboardHistoryEntry>> GetPage(DashboardHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IEnumerable<DashboardHistoryEntry> filtered = OnNode(
            Snapshot(executionsByScheduler, query.SchedulerName, FiredAt),
            query.SchedulerInstanceId,
            static entry => entry.SchedulerInstanceId);

        if (!string.IsNullOrWhiteSpace(query.JobFilter))
        {
            string normalizedJobFilter = query.JobFilter.Trim();
            filtered = filtered.Where(x =>
                MatchesFilter(x.JobGroup, x.JobName, normalizedJobFilter));
        }

        if (!string.IsNullOrWhiteSpace(query.TriggerFilter))
        {
            string normalizedTriggerFilter = query.TriggerFilter.Trim();
            filtered = filtered.Where(x =>
                MatchesFilter(x.TriggerGroup, x.TriggerName, normalizedTriggerFilter));
        }

        return ValueTask.FromResult(Page(filtered.OrderByDescending(static entry => entry.FiredAtUtc).ToList(), query));
    }

    public ValueTask<PagedResult<DashboardMisfireEntry>> GetMisfires(DashboardMisfireQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IEnumerable<DashboardMisfireEntry> filtered = OnNode(
            Snapshot(misfiresByScheduler, query.SchedulerName, MisfiredAt),
            query.SchedulerInstanceId,
            static entry => entry.SchedulerInstanceId);

        if (!string.IsNullOrWhiteSpace(query.TriggerFilter))
        {
            string normalizedTriggerFilter = query.TriggerFilter.Trim();
            filtered = filtered.Where(x =>
                MatchesFilter(x.TriggerGroup, x.TriggerName, normalizedTriggerFilter));
        }

        return ValueTask.FromResult(Page(filtered.OrderByDescending(static entry => entry.MisfiredAtUtc).ToList(), query));
    }

    public ValueTask<int> CountMisfires(string schedulerName, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);

        int count = 0;
        foreach (DashboardMisfireEntry entry in Snapshot(misfiresByScheduler, schedulerName, MisfiredAt))
        {
            if (entry.MisfiredAtUtc >= since)
            {
                count++;
            }
        }

        return ValueTask.FromResult(count);
    }

    private static DateTimeOffset FiredAt(DashboardHistoryEntry entry) => entry.FiredAtUtc;

    private static DateTimeOffset MisfiredAt(DashboardMisfireEntry entry) => entry.MisfiredAtUtc;

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
    /// </remarks>
    private void Trim<T>(List<T> list, Func<T, DateTimeOffset> timeOf)
    {
        if (retention > TimeSpan.Zero)
        {
            DateTimeOffset cutoff = timeProvider.GetUtcNow() - retention;
            list.RemoveAll(entry => timeOf(entry) < cutoff);
        }

        if (list.Count > maxEntriesPerScheduler)
        {
            list.RemoveRange(0, list.Count - maxEntriesPerScheduler);
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
