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

namespace Quartz.Dashboard.Services;

public sealed record DashboardHistoryEntry(
    string SchedulerName,
    string JobGroup,
    string JobName,
    string TriggerGroup,
    string TriggerName,
    DateTimeOffset FiredAtUtc,
    long DurationMs,
    bool Succeeded,
    string? ExceptionMessage);

public interface IDashboardHistoryStore
{
    void Add(DashboardHistoryEntry entry);

    DashboardHistoryPage GetPage(
        string schedulerName,
        int page,
        int pageSize,
        string? jobFilter = null,
        string? triggerFilter = null);
}

internal sealed class DashboardHistoryStore : IDashboardHistoryStore
{
    private readonly ConcurrentDictionary<string, List<DashboardHistoryEntry>> entriesByScheduler = new(StringComparer.OrdinalIgnoreCase);
    private readonly int maxEntriesPerScheduler = 2000;

    public void Add(DashboardHistoryEntry entry)
    {
        List<DashboardHistoryEntry> list = entriesByScheduler.GetOrAdd(entry.SchedulerName, _ => []);
        lock (list)
        {
            list.Add(entry);
            if (list.Count > maxEntriesPerScheduler)
            {
                list.RemoveRange(0, list.Count - maxEntriesPerScheduler);
            }
        }
    }

    public DashboardHistoryPage GetPage(
        string schedulerName,
        int page,
        int pageSize,
        string? jobFilter = null,
        string? triggerFilter = null)
    {
        int safePageSize = Math.Clamp(pageSize, 1, 100);
        List<DashboardHistoryEntry> snapshot = [];

        if (entriesByScheduler.TryGetValue(schedulerName, out List<DashboardHistoryEntry>? list))
        {
            lock (list)
            {
                snapshot.AddRange(list);
            }
        }

        IEnumerable<DashboardHistoryEntry> filtered = snapshot;
        if (!string.IsNullOrWhiteSpace(jobFilter))
        {
            string normalizedJobFilter = jobFilter.Trim();
            filtered = filtered.Where(x =>
                MatchesFilter(x.JobGroup, x.JobName, normalizedJobFilter));
        }

        if (!string.IsNullOrWhiteSpace(triggerFilter))
        {
            string normalizedTriggerFilter = triggerFilter.Trim();
            filtered = filtered.Where(x =>
                MatchesFilter(x.TriggerGroup, x.TriggerName, normalizedTriggerFilter));
        }

        List<DashboardHistoryEntry> ordered = filtered
            .OrderByDescending(x => x.FiredAtUtc)
            .ToList();

        int totalCount = ordered.Count;
        int totalPages = Math.Max(1, (int) Math.Ceiling((double) totalCount / safePageSize));
        int safePage = Math.Clamp(page, 1, totalPages);
        int skip = (safePage - 1) * safePageSize;
        List<DashboardHistoryEntry> pageItems = ordered.Skip(skip).Take(safePageSize).ToList();
        return new DashboardHistoryPage(safePage, safePageSize, totalCount, pageItems);
    }

    private static bool MatchesFilter(string group, string name, string filter)
    {
        string key = group + "." + name;
        return key.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               group.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               name.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record DashboardHistoryPage(int Page, int PageSize, int TotalCount, IReadOnlyList<DashboardHistoryEntry> Entries);
