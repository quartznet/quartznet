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

using System.Runtime.InteropServices;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Counts, per trigger, the fires in a row that failed for a reason a retry will not cure, so that a
/// trigger whose every fire fails is stored <c>ERROR</c> instead of being released and acquired again
/// forever (#3963). A misfire whose policy throws — the trigger's calendar, or a trigger type of the
/// application's own — counts as one of them (#3985, #4006).
/// </summary>
/// <remarks>
/// <para>
/// One per store, in memory. Keyed by the trigger alone: a fire that is rolled back leaves the trigger's
/// fire time where it was, and the misfire handler may move it, so neither says whether two failures are
/// of the same firing.
/// </para>
/// <para>
/// What <em>does</em> say that a fire committed in between is the trigger's previous fire time, which only
/// a committed fire moves. The count starts again when it differs from the one the last failure saw, so a
/// fire another node committed breaks the run as a fire on this node does, and an entry left behind by a
/// trigger this node stopped seeing cannot add up to a limit long afterwards.
/// </para>
/// <para>
/// A misfire failure counts at most once per interval, the misfire threshold. A misfire is handled again
/// as soon as the next pass reaches it, and with a backlog of misfires that is every few milliseconds, so
/// a calendar that is briefly unreachable would otherwise park its trigger before it could recover. A
/// failed fire counts every time: the trigger is released and acquired again at its own pace.
/// </para>
/// </remarks>
internal sealed class FireFailureLedger
{
    private readonly Dictionary<TriggerKey, Entry> entries = [];
    private readonly Lock gate = new();
    private int count;

    /// <summary>
    /// Whether no trigger has a failure counted, read without the lock: the ordinary batch, in which
    /// nothing has failed, asks this and nothing else.
    /// </summary>
    public bool IsEmpty => Volatile.Read(ref count) == 0;

    /// <summary>
    /// Counts one more failed fire of the trigger and answers how many there have been in a row.
    /// </summary>
    /// <param name="triggerKey">The trigger whose fire failed.</param>
    /// <param name="previousFireTimeUtc">The trigger's previous fire time as it was acquired.</param>
    public int RecordFailure(TriggerKey triggerKey, DateTimeOffset? previousFireTimeUtc)
    {
        lock (gate)
        {
            bool continuing = entries.TryGetValue(triggerKey, out Entry entry) && entry.PreviousFireTimeUtc == previousFireTimeUtc;
            int failures = continuing ? entry.Failures + 1 : 1;

            entries[triggerKey] = new Entry(failures, previousFireTimeUtc, continuing ? entry.MisfireCountedAtUtc : null);
            Volatile.Write(ref count, entries.Count);
            return failures;
        }
    }

    /// <summary>
    /// Answers how many failures in a row the trigger would have with one more misfire failure counted,
    /// without counting it; or <see langword="null" /> when a misfire failure of the same run was counted
    /// less than <paramref name="interval" /> before <paramref name="nowUtc" />, so this one does not count.
    /// </summary>
    /// <param name="triggerKey">The trigger whose misfire policy failed.</param>
    /// <param name="previousFireTimeUtc">The trigger's previous fire time as the failure found it.</param>
    /// <param name="nowUtc">When the failure happened, by the store's clock.</param>
    /// <param name="interval">How long after one counted misfire failure the next one counts.</param>
    public int? MisfireFailuresWithOneMore(TriggerKey triggerKey, DateTimeOffset? previousFireTimeUtc, DateTimeOffset nowUtc, TimeSpan interval)
    {
        if (IsEmpty)
        {
            return 1;
        }

        lock (gate)
        {
            if (!entries.TryGetValue(triggerKey, out Entry entry) || entry.PreviousFireTimeUtc != previousFireTimeUtc)
            {
                return 1;
            }

            if (entry.MisfireCountedAtUtc is { } countedAt && nowUtc - countedAt < interval)
            {
                return null;
            }

            return entry.Failures + 1;
        }
    }

    /// <summary>
    /// Counts one more failed misfire of the trigger, stamped with when it was counted, and answers how
    /// many failures there have been in a row. The caller asks
    /// <see cref="MisfireFailuresWithOneMore" /> first, whether this one counts at all.
    /// </summary>
    /// <param name="triggerKey">The trigger whose misfire policy failed.</param>
    /// <param name="previousFireTimeUtc">The trigger's previous fire time as the failure found it.</param>
    /// <param name="countedAtUtc">When the failure happened, by the store's clock.</param>
    public int RecordMisfireFailure(TriggerKey triggerKey, DateTimeOffset? previousFireTimeUtc, DateTimeOffset countedAtUtc)
    {
        lock (gate)
        {
            int failures = entries.TryGetValue(triggerKey, out Entry entry) && entry.PreviousFireTimeUtc == previousFireTimeUtc
                ? entry.Failures + 1
                : 1;

            entries[triggerKey] = new Entry(failures, previousFireTimeUtc, countedAtUtc);
            Volatile.Write(ref count, entries.Count);
            return failures;
        }
    }

    /// <summary>
    /// Forgets the trigger's count: it fired, its misfire was handled, or it has been stored <c>ERROR</c>.
    /// </summary>
    public void Clear(TriggerKey triggerKey)
    {
        if (IsEmpty)
        {
            return;
        }

        lock (gate)
        {
            if (entries.Remove(triggerKey))
            {
                Volatile.Write(ref count, entries.Count);
            }
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Entry(int Failures, DateTimeOffset? PreviousFireTimeUtc, DateTimeOffset? MisfireCountedAtUtc);
}
