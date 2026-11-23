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

using System.Diagnostics.CodeAnalysis;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Diagnostics;

/// <summary>
/// One ADO.NET store's last count of its due backlog (#4401), which the due-trigger gauges read.
/// </summary>
/// <remarks>
/// <para>
/// A gauge's callback runs on the exporter's collection thread, where a database round trip would hold
/// up the export, so the store's misfire pass takes the count and the callback only reads it.
/// </para>
/// <para>
/// Once the count is older than <see cref="maxAge" /> nothing is reported: a frozen last value would
/// hide the stalled store the gauge exists to show.
/// </para>
/// </remarks>
internal sealed class DueTriggerBacklog
{
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan maxAge;

    /// <summary>
    /// Every group the store has reported, so one whose backlog clears drops to zero rather than
    /// vanishing. Only the misfire handler's thread touches it.
    /// </summary>
    private readonly HashSet<string?> groupsSeen = [];

    private volatile Snapshot? current;

    public DueTriggerBacklog(string schedulerName, string schedulerId, TimeProvider timeProvider, TimeSpan maxAge)
    {
        SchedulerName = schedulerName;
        SchedulerId = schedulerId;
        this.timeProvider = timeProvider;
        this.maxAge = maxAge;
    }

    public string SchedulerName { get; }

    public string SchedulerId { get; }

    /// <summary>
    /// Replaces the count with what the store read at <paramref name="takenAt" />.
    /// </summary>
    internal void Publish(DateTimeOffset takenAt, List<DueTriggers> rows)
    {
        List<DueTriggerCount> counts = new(Math.Max(rows.Count, groupsSeen.Count));
        HashSet<string?> present = [];

        foreach (DueTriggers row in rows)
        {
            groupsSeen.Add(row.ExecutionGroup);
            present.Add(row.ExecutionGroup);

            // Measured at the read rather than at collection, so the count and the age describe one instant.
            double ageSeconds = Math.Max(0, (takenAt - row.EarliestDueUtc).TotalSeconds);
            counts.Add(new DueTriggerCount(row.ExecutionGroup, row.Count, ageSeconds));
        }

        foreach (string? group in groupsSeen)
        {
            if (!present.Contains(group))
            {
                counts.Add(new DueTriggerCount(group, 0, 0));
            }
        }

        current = new Snapshot(takenAt, counts);
    }

    /// <summary>
    /// The last count, unless there is none or it has gone stale.
    /// </summary>
    internal bool TryRead([NotNullWhen(true)] out List<DueTriggerCount>? counts)
    {
        Snapshot? snapshot = current;
        if (snapshot is null || timeProvider.GetUtcNow() - snapshot.TakenAt > maxAge)
        {
            counts = null;
            return false;
        }

        counts = snapshot.Counts;
        return true;
    }

    private sealed record Snapshot(DateTimeOffset TakenAt, List<DueTriggerCount> Counts);
}

/// <summary>
/// One execution group's due backlog as the gauges report it.
/// </summary>
/// <param name="ExecutionGroup">The group; <see langword="null" /> for triggers that name none.</param>
/// <param name="Count">How many triggers are due.</param>
/// <param name="AgeSeconds">How long the earliest of them had been due when they were counted; zero when none is.</param>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct DueTriggerCount(string? ExecutionGroup, long Count, double AgeSeconds);
