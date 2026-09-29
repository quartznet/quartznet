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

using System;
using System.Collections.Generic;
using System.Threading;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Counts, per trigger, the fires in a row that failed for a reason a retry will not cure, so that a
/// trigger whose every fire fails is stored <c>ERROR</c> instead of being released and acquired again
/// forever (#3963).
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
/// </remarks>
internal sealed class FireFailureLedger
{
    private readonly Dictionary<TriggerKey, Entry> entries = new();
    private readonly object gate = new();
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
            int failures = entries.TryGetValue(triggerKey, out Entry entry) && entry.PreviousFireTimeUtc == previousFireTimeUtc
                ? entry.Failures + 1
                : 1;

            entries[triggerKey] = new Entry(failures, previousFireTimeUtc);
            Volatile.Write(ref count, entries.Count);
            return failures;
        }
    }

    /// <summary>
    /// Forgets the trigger's count: it fired, or it has been stored <c>ERROR</c>.
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

    private readonly struct Entry
    {
        public Entry(int failures, DateTimeOffset? previousFireTimeUtc)
        {
            Failures = failures;
            PreviousFireTimeUtc = previousFireTimeUtc;
        }

        public int Failures { get; }

        public DateTimeOffset? PreviousFireTimeUtc { get; }
    }
}
