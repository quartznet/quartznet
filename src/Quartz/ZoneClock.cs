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
/// There is a slot for every window a cron expression can reach - <see cref="ZoneOffsetTable.WindowCount" />,
/// about twenty - and each is built at most once and then kept. Nothing is evicted, so a caller that
/// probes widely, as <c>GetPreviousValidTimeBefore</c> does, cannot make a window be rebuilt; the most
/// a zone can ever cost is twenty builds of about a fifth of a millisecond and a few kilobytes.
/// </para>
/// <para>
/// A smaller cap is an order dependency. The clock is shared by every caller in the process, so the
/// first windows anybody asks about - a calendar checked against last decade, a test dated 2005 - would
/// fill it, and the window the scheduler runs in would be declined for the life of the process.
/// </para>
/// </remarks>
internal sealed class ZoneClock
{
    private static readonly ConditionalWeakTable<TimeZoneInfo, ZoneClock> clocks = new ConditionalWeakTable<TimeZoneInfo, ZoneClock>();

    private readonly TimeZoneInfo zone;
    private readonly Lock buildLock = new Lock();

    /// <summary>
    /// The table for each window, indexed from <see cref="ZoneOffsetTable.FirstWindowIndex" />. A slot
    /// is written once, under <see cref="buildLock" />, and read without it.
    /// </summary>
    private readonly ZoneOffsetTable?[] tables = new ZoneOffsetTable?[ZoneOffsetTable.WindowCount];

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
        int windowIndex = ZoneOffsetTable.WindowIndexFor(utcTicks);
        if (windowIndex < 0)
        {
            return null;
        }

        ref ZoneOffsetTable? slot = ref tables[windowIndex - ZoneOffsetTable.FirstWindowIndex];

        ZoneOffsetTable? existing = Volatile.Read(ref slot);
        if (existing is not null)
        {
            return existing;
        }

        lock (buildLock)
        {
            existing = slot;
            if (existing is not null || unsupported)
            {
                return existing;
            }

            ZoneOffsetTable built = ZoneOffsetTable.Build(zone, windowIndex);
            if (!built.IsSupported)
            {
                unsupported = true;
                return null;
            }

            Volatile.Write(ref slot, built);
            return built;
        }
    }
}
