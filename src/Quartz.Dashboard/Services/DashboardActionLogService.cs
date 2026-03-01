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

namespace Quartz.Dashboard.Services;

internal sealed record DashboardActionLogEntry(
    DateTimeOffset Timestamp,
    string SchedulerName,
    string Action,
    string Target,
    bool Succeeded,
    string? Message);

internal sealed class DashboardActionLogService
{
    private readonly List<DashboardActionLogEntry> entries = [];
    private readonly Lock syncRoot = new();
    private readonly int maxEntries = 250;

    public void Record(
        string schedulerName,
        string action,
        string target,
        bool succeeded,
        string? message = null)
    {
        DashboardActionLogEntry entry = new(
            Timestamp: DateTimeOffset.UtcNow,
            SchedulerName: schedulerName,
            Action: action,
            Target: target,
            Succeeded: succeeded,
            Message: message);

        lock (syncRoot)
        {
            entries.Insert(0, entry);
            if (entries.Count > maxEntries)
            {
                entries.RemoveRange(maxEntries, entries.Count - maxEntries);
            }
        }
    }

    public IReadOnlyList<DashboardActionLogEntry> GetLatest(int maxCount = 25)
    {
        int safeMaxCount = Math.Clamp(maxCount, 1, maxEntries);
        lock (syncRoot)
        {
            return entries.Take(safeMaxCount).ToList();
        }
    }
}
