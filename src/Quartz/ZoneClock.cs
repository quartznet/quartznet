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
using System.Runtime.CompilerServices;

namespace Quartz;

/// <summary>
/// The <see cref="ZoneOffsetTable" />s built for one <see cref="TimeZoneInfo" />, and the policy for
/// when another one gets built.
/// </summary>
/// <remarks>
/// <para>
/// There is one of these per zone instance rather than per zone id, and it is held in a
/// <see cref="ConditionalWeakTable{TKey,TValue}" /> keyed by reference, so a zone the application
/// drops takes its tables with it - and <see cref="TimeZoneInfo.Local" />, which
/// <see cref="TimeZoneInfo.ClearCachedData" /> replaces with a new instance, gets new tables rather
/// than keeping the old machine's.
/// </para>
/// <para>
/// At most four windows are ever built, and an instant that falls in none of them once four exist is
/// simply answered "no". That is the whole of the eviction policy, and it is deliberate: a cache that
/// evicted could be made to rebuild the same window over and over by a caller that probes widely, and
/// <c>GetPreviousValidTimeBefore</c> is exactly such a caller. Four windows is thirty-two years, far
/// more than a running scheduler moves through, and the cost of being wrong is the search the slow
/// path always did.
/// </para>
/// </remarks>
internal sealed class ZoneClock
{
    private const int MaxWindows = 4;

    private static readonly ConditionalWeakTable<TimeZoneInfo, ZoneClock> clocks = new ConditionalWeakTable<TimeZoneInfo, ZoneClock>();

    private readonly TimeZoneInfo zone;
    private readonly Lock buildLock = new Lock();

    private volatile ZoneOffsetTable[] tables = [];

    /// <summary>The window the last read landed in; validated before use, so a stale one costs nothing.</summary>
    private volatile ZoneOffsetTable? lastTable;

    /// <summary>
    /// Set when a table failed to reproduce the zone's own answers. The zone is then left to the slow
    /// path for good: a zone whose offsets cannot be tabulated once will not start being tabulatable.
    /// </summary>
    private volatile bool unsupported;

    private ZoneClock(TimeZoneInfo zone)
    {
        this.zone = zone;
    }

    internal static ZoneClock For(TimeZoneInfo zone)
    {
        return clocks.GetValue(zone, static key => new ZoneClock(key));
    }

    /// <summary>
    /// The table covering an instant, building it when that is the policy, or <see langword="false" />
    /// when this instant is one the fast path must not answer for.
    /// </summary>
    internal bool TryGetTable(long utcTicks, [NotNullWhen(true)] out ZoneOffsetTable? table)
    {
        table = null;

        if (unsupported)
        {
            return false;
        }

        ZoneOffsetTable? candidate = lastTable;
        if (candidate is null || !candidate.Contains(utcTicks))
        {
            candidate = Find(utcTicks);
            if (candidate is null)
            {
                return false;
            }

            lastTable = candidate;
        }

        table = candidate;
        return true;
    }

    private ZoneOffsetTable? Find(long utcTicks)
    {
        ZoneOffsetTable[] snapshot = tables;
        foreach (ZoneOffsetTable candidate in snapshot)
        {
            if (candidate.Contains(utcTicks))
            {
                return candidate;
            }
        }

        if (snapshot.Length >= MaxWindows)
        {
            return null;
        }

        int windowIndex = ZoneOffsetTable.WindowIndexFor(utcTicks);
        if (windowIndex < 0)
        {
            return null;
        }

        lock (buildLock)
        {
            snapshot = tables;
            foreach (ZoneOffsetTable candidate in snapshot)
            {
                if (candidate.Contains(utcTicks))
                {
                    return candidate;
                }
            }

            if (snapshot.Length >= MaxWindows)
            {
                return null;
            }

            ZoneOffsetTable built = ZoneOffsetTable.Build(zone, windowIndex);
            if (!built.IsSupported)
            {
                unsupported = true;
                return null;
            }

            ZoneOffsetTable[] next = new ZoneOffsetTable[snapshot.Length + 1];
            Array.Copy(snapshot, next, snapshot.Length);
            next[snapshot.Length] = built;
            tables = next;

            return built;
        }
    }
}
