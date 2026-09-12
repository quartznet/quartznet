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
    string? Message)
{
    /// <summary>
    /// Where the scheduler the action was aimed at is, or <see langword="null" /> when the listing this
    /// circuit last read said nothing about it.
    /// </summary>
    /// <remarks>
    /// Nullable rather than defaulted to <see cref="SchedulerOrigin.Container" />: a page that acted
    /// before anything listed the schedulers knows the name and nothing else, and an entry claiming the
    /// scheduler is this container's would be inventing that.
    /// </remarks>
    public SchedulerOrigin? Origin { get; init; }

    /// <summary>
    /// Which node the action reached, or <see langword="null" /> when nothing has built the scheduler or
    /// the listing could not ask it.
    /// </summary>
    public string? SchedulerInstanceId { get; init; }

    /// <summary>
    /// Whether the action landed on that one node rather than on the scheduling data every node shares.
    /// </summary>
    /// <remarks>
    /// Said by the page that took the action, the way a mutating route says it mutates: interrupting a
    /// firing has to reach the node running it, and starting, standing by and shutting down act on the
    /// node that answered. Pausing a trigger does not — it writes the store, and every node in the
    /// cluster is bound by it — so naming a node beside it would suggest the other nodes were unaffected.
    /// </remarks>
    public bool NodeLocal { get; init; }
}

internal sealed class DashboardActionLogService
{
    private readonly List<DashboardActionLogEntry> entries = [];
    private readonly Lock syncRoot = new();
    private readonly int maxEntries = 250;

    /// <summary>
    /// Keeps one action, dropping the oldest once the bound is reached.
    /// </summary>
    /// <remarks>
    /// The finished entry rather than its fields: who took the action and what the last listing said about
    /// the scheduler are a circuit's, and this store is the process's. <see cref="DashboardActionLog" /> is
    /// the scoped thing that knows both and builds one.
    /// </remarks>
    public void Record(DashboardActionLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

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
