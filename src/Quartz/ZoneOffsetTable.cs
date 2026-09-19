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

namespace Quartz;

/// <summary>
/// One time zone's offsets over one eight-year window, as a sorted list of <i>safe segments</i>.
/// </summary>
/// <remarks>
/// <para>
/// A safe segment is a stretch of UTC time over which the zone's offset is constant and which no
/// transition comes within two days of. The second half is what makes the segment worth having:
/// inside one, a wall clock cannot be a reading the clocks skipped and cannot be a reading that
/// happens twice, because the nearest gap or overlap is at least forty-six hours of wall clock away -
/// two days, less the largest transition delta a zone has ever used. A caller holding a safe segment
/// therefore knows the answers to <see cref="TimeZoneInfo.IsInvalidTime(DateTime)" />,
/// <see cref="TimeZoneInfo.IsAmbiguousTime(DateTime)" /> and <see cref="TimeZoneInfo.GetUtcOffset(DateTime)" />
/// without asking.
/// </para>
/// <para>
/// The transitions the segments are cut around come from two instruments, because neither alone is
/// enough. A sweep of the zone's own offsets, one reading a day, finds every change that lasts a day
/// or more, whatever produced it and whatever platform's data describes it - and it brackets each one
/// between two readings a day apart, which two days of slack then covers from either side. The
/// adjustment rules add the ones a daily reading could step over: a rule's own start and end, and
/// both of its transitions in every year of the window. The two are then <i>verified</i> against
/// <see cref="TimeZoneInfo" /> - at every segment's ends and middle, in both the instant and the
/// wall-clock direction, and at every daily reading inside a segment - and a zone that disagrees
/// anywhere is marked unsupported rather than guessed at.
/// </para>
/// <para>
/// Instances are immutable once built, apart from <see cref="hint" />, which is a cached segment
/// index that every read validates before using.
/// </para>
/// </remarks>
internal sealed class ZoneOffsetTable
{
    /// <summary>How many calendar years one window spans.</summary>
    internal const int WindowYears = 8;

    /// <summary>
    /// How far either side of a transition found by the daily sweep is given up. The sweep brackets a
    /// transition between two readings a day apart, so two days from each reading leaves at least a
    /// day of slack past the transition itself - and forty-eight hours is well past the largest
    /// transition delta any zone has used, which is what the wall-clock argument needs.
    /// </summary>
    private const long SweepExclusionTicks = 48 * TimeSpan.TicksPerHour;

    /// <summary>
    /// How far either side of a transition derived from an adjustment rule is given up. A rule states
    /// its transition in wall clock, against a calendar this code re-derives, and the derived value is
    /// then read as though it were an instant - so it can be out by the fourteen hours a zone can be
    /// from UTC, and by a day if the re-derivation ever disagreed about which day of the month the
    /// rule names. Thirty-eight hours of worst case, given forty-eight.
    /// </summary>
    private const long RuleExclusionTicks = 48 * TimeSpan.TicksPerHour;

    /// <summary>How far outside the window transitions are looked for, so that one just past the edge still cuts it.</summary>
    private const long ScanMarginTicks = 8 * TimeSpan.TicksPerDay;

    private readonly Segment[] segments;

    /// <summary>
    /// The segment the last read landed in. A hint, not state: a reader checks the instant against the
    /// segment it names before using it, so a stale or concurrently overwritten value costs a binary
    /// search and nothing else. An <see cref="int" /> is written atomically, so it cannot tear.
    /// </summary>
    private int hint;

    /// <inheritdoc cref="hint" />
    private int wallClockHint;

    private ZoneOffsetTable(long windowStartUtcTicks, long windowEndUtcTicks, Segment[] segments, bool isSupported)
    {
        WindowStartUtcTicks = windowStartUtcTicks;
        WindowEndUtcTicks = windowEndUtcTicks;
        this.segments = segments;
        IsSupported = isSupported;
    }

    internal long WindowStartUtcTicks { get; }

    internal long WindowEndUtcTicks { get; }

    /// <summary>
    /// Whether the table agreed with <see cref="TimeZoneInfo" /> everywhere it was checked. A table
    /// that did not is kept only so the zone can be remembered as one to leave alone.
    /// </summary>
    internal bool IsSupported { get; }

    /// <summary>The segments, for the test that holds them against an independent scan of the zone.</summary>
    internal ReadOnlySpan<Segment> Segments => segments;

    internal bool Contains(long utcTicks)
    {
        return utcTicks >= WindowStartUtcTicks && utcTicks < WindowEndUtcTicks;
    }

    /// <summary>
    /// The window a given instant belongs to, or a negative number for an instant no window will ever
    /// be built for.
    /// </summary>
    /// <remarks>
    /// Windows are aligned on multiples of <see cref="WindowYears" /> so that two callers asking about
    /// the same decade ask for the same window. Instants outside the years a cron expression can name
    /// have none: the year field cannot go below <see cref="TriggerConstants.EarliestYear" /> or above
    /// <see cref="TriggerConstants.YearToGiveUpSchedulingAt" />, and refusing the rest is what keeps
    /// <c>GetPreviousValidTimeBefore</c>, whose binary search opens in year 2, from spending one of a
    /// zone's four windows on a century nothing will read again.
    /// </remarks>
    internal static int WindowIndexFor(long utcTicks)
    {
        int year = new DateTime(utcTicks).Year;
        if (year < TriggerConstants.EarliestYear || year > TriggerConstants.YearToGiveUpSchedulingAt)
        {
            return -1;
        }

        return year / WindowYears;
    }

    /// <summary>
    /// The zone's offset at an instant, or <see langword="false" /> when the instant falls in one of
    /// the stretches given up around a transition.
    /// </summary>
    internal bool TryGetOffsetAt(long utcTicks, out long offsetTicks)
    {
        Segment[] local = segments;

        int index = hint;
        if ((uint) index < (uint) local.Length && local[index].Contains(utcTicks))
        {
            offsetTicks = local[index].OffsetTicks;
            return true;
        }

        int low = 0;
        int high = local.Length - 1;
        while (low <= high)
        {
            int middle = (int) (((uint) low + (uint) high) >> 1);
            if (utcTicks < local[middle].StartUtcTicks)
            {
                high = middle - 1;
            }
            else if (utcTicks >= local[middle].EndUtcTicks)
            {
                low = middle + 1;
            }
            else
            {
                hint = middle;
                offsetTicks = local[middle].OffsetTicks;
                return true;
            }
        }

        offsetTicks = 0;
        return false;
    }

    /// <summary>
    /// The offset a wall-clock reading resolves with - the other direction, which is the one a search
    /// that walked in wall clock needs at the end of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is why the search may answer for an instant a season away from where it started. What the
    /// walk produces is a wall clock, and the slow path's own resolution of it consults the zone at
    /// <i>that</i> wall clock and nowhere in between: the offset the search started with never enters
    /// the answer. So one safe segment is needed for the floor, to read the wall clock the walk starts
    /// at, and another - not necessarily the same one - for the wall clock it ends at.
    /// </para>
    /// <para>
    /// Each segment covers a wall-clock stretch of <c>[Start + Offset, End + Offset)</c>, and those
    /// are in the same order and still disjoint, because consecutive segments are at least four days
    /// of UTC apart and no zone has ever moved its clocks by anything like that. So the search is the
    /// same binary search, over a different key.
    /// </para>
    /// </remarks>
    internal bool TryGetOffsetForWallClock(long wallClockTicks, out long offsetTicks)
    {
        Segment[] local = segments;

        int index = wallClockHint;
        if ((uint) index < (uint) local.Length && local[index].ContainsWallClock(wallClockTicks))
        {
            offsetTicks = local[index].OffsetTicks;
            return true;
        }

        int low = 0;
        int high = local.Length - 1;
        while (low <= high)
        {
            int middle = (int) (((uint) low + (uint) high) >> 1);
            if (wallClockTicks < local[middle].StartUtcTicks + local[middle].OffsetTicks)
            {
                high = middle - 1;
            }
            else if (wallClockTicks >= local[middle].EndUtcTicks + local[middle].OffsetTicks)
            {
                low = middle + 1;
            }
            else
            {
                wallClockHint = middle;
                offsetTicks = local[middle].OffsetTicks;
                return true;
            }
        }

        offsetTicks = 0;
        return false;
    }

    /// <summary>
    /// Builds the table for one zone and one window, or an unsupported one when the zone's own answers
    /// cannot be reproduced from it.
    /// </summary>
    internal static ZoneOffsetTable Build(TimeZoneInfo zone, int windowIndex)
    {
        long windowStart = new DateTime(windowIndex * WindowYears, 1, 1).Ticks;
        long windowEnd = new DateTime((windowIndex + 1) * WindowYears, 1, 1).Ticks;

        long scanStart = windowStart - ScanMarginTicks;
        long scanEnd = windowEnd + ScanMarginTicks;

        List<Interval> exclusions = [];

        try
        {
            AddRuleExclusions(zone, scanStart, scanEnd, windowStart, windowEnd, exclusions);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidTimeZoneException or NotSupportedException)
        {
            // A zone whose rules cannot be read or re-derived is one nothing can be proved about.
            return Unsupported(windowStart, windowEnd);
        }

        int sampleCount = (int) ((scanEnd - scanStart) / TimeSpan.TicksPerDay) + 1;
        long[] sampledOffsets = new long[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            sampledOffsets[i] = OffsetTicksAt(zone, scanStart + i * TimeSpan.TicksPerDay);
        }

        for (int i = 1; i < sampleCount; i++)
        {
            if (sampledOffsets[i] != sampledOffsets[i - 1])
            {
                // The transition is somewhere between the two readings, so both of them are excluded:
                // together they cover it with at least a day of slack on either side.
                AddExclusion(exclusions, scanStart + (i - 1) * TimeSpan.TicksPerDay, SweepExclusionTicks, windowStart, windowEnd);
                AddExclusion(exclusions, scanStart + i * TimeSpan.TicksPerDay, SweepExclusionTicks, windowStart, windowEnd);
            }
        }

        Segment[] segments = Complement(exclusions, windowStart, windowEnd, zone);

        if (!Verify(zone, segments, scanStart, sampledOffsets))
        {
            return Unsupported(windowStart, windowEnd);
        }

        return new ZoneOffsetTable(windowStart, windowEnd, segments, isSupported: true);
    }

    private static ZoneOffsetTable Unsupported(long windowStart, long windowEnd)
    {
        return new ZoneOffsetTable(windowStart, windowEnd, [], isSupported: false);
    }

    /// <summary>
    /// Adds an exclusion around every offset change the adjustment rules point at and the daily sweep
    /// could have stepped over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rule says where to look, not what is there. Each place it names - its own start and end, and
    /// both of its transitions in every year of the window - is scanned hour by hour for two days
    /// either way, and an exclusion is cut only where the offset is actually seen to move. That
    /// matters because a zone like Morocco carries one rule per year, so its rule boundaries alone
    /// would name four instants a year at which nothing whatever happens, and every one of them would
    /// cost four days of fast path.
    /// </para>
    /// <para>
    /// Two days either way is the width the derivation can be wrong by: a rule states its transition
    /// in wall clock, which is read here as though it were an instant and so can be fourteen hours
    /// out, and a day more if this file's reading of "the last Sunday in October" ever disagreed with
    /// the BCL's. What the hourly scan then leaves uncovered is an offset excursion shorter than an
    /// hour, which the daily sweep would also have missed and which no time zone has ever had.
    /// </para>
    /// </remarks>
    private static void AddRuleExclusions(
        TimeZoneInfo zone,
        long scanStart,
        long scanEnd,
        long windowStart,
        long windowEnd,
        List<Interval> exclusions)
    {
        TimeZoneInfo.AdjustmentRule[] rules = zone.GetAdjustmentRules();
        if (rules.Length == 0)
        {
            return;
        }

        int scanStartYear = new DateTime(scanStart).Year;
        int scanEndYear = new DateTime(scanEnd).Year;

        List<long> candidates = [];
        foreach (TimeZoneInfo.AdjustmentRule rule in rules)
        {
            AddCandidate(candidates, rule.DateStart.Ticks, windowStart, windowEnd);
            AddCandidate(candidates, rule.DateEnd.Ticks, windowStart, windowEnd);

            int firstYear = Math.Max(rule.DateStart.Year, scanStartYear);
            int lastYear = Math.Min(rule.DateEnd.Year, scanEndYear);

            for (int year = firstYear; year <= lastYear; year++)
            {
                AddCandidate(candidates, TransitionTicks(year, rule.DaylightTransitionStart), windowStart, windowEnd);
                AddCandidate(candidates, TransitionTicks(year, rule.DaylightTransitionEnd), windowStart, windowEnd);
            }
        }

        candidates.Sort();

        long probeStart = long.MinValue;
        long probeEnd = long.MinValue;

        foreach (long candidate in candidates)
        {
            long start = candidate - RuleExclusionTicks;
            long end = candidate + RuleExclusionTicks;

            if (probeEnd == long.MinValue)
            {
                probeStart = start;
                probeEnd = end;
                continue;
            }

            if (start <= probeEnd)
            {
                probeEnd = Math.Max(probeEnd, end);
                continue;
            }

            ProbeForOffsetChange(zone, probeStart, probeEnd, exclusions, windowStart, windowEnd);
            probeStart = start;
            probeEnd = end;
        }

        if (probeEnd != long.MinValue)
        {
            ProbeForOffsetChange(zone, probeStart, probeEnd, exclusions, windowStart, windowEnd);
        }
    }

    private static void AddCandidate(List<long> candidates, long ticks, long windowStart, long windowEnd)
    {
        if (ticks < windowStart - RuleExclusionTicks || ticks > windowEnd + RuleExclusionTicks)
        {
            return;
        }

        candidates.Add(ticks);
    }

    /// <summary>
    /// Reads the zone's offset hour by hour across a span and excludes the part of it the offset moved
    /// in, with two days of slack either side. A span the offset never moves in costs nothing.
    /// </summary>
    private static void ProbeForOffsetChange(
        TimeZoneInfo zone,
        long from,
        long to,
        List<Interval> exclusions,
        long windowStart,
        long windowEnd)
    {
        long previous = OffsetTicksAt(zone, from);
        long firstChange = long.MaxValue;
        long lastChange = long.MinValue;

        for (long probe = from + TimeSpan.TicksPerHour; probe <= to; probe += TimeSpan.TicksPerHour)
        {
            long offset = OffsetTicksAt(zone, probe);
            if (offset != previous)
            {
                firstChange = Math.Min(firstChange, probe - TimeSpan.TicksPerHour);
                lastChange = Math.Max(lastChange, probe);
            }

            previous = offset;
        }

        if (firstChange == long.MaxValue)
        {
            return;
        }

        AddInterval(exclusions, firstChange - SweepExclusionTicks, lastChange + SweepExclusionTicks, windowStart, windowEnd);
    }

    /// <summary>
    /// When in a given year a <see cref="TimeZoneInfo.TransitionTime" /> falls, as ticks. A replica of
    /// the BCL's own <c>TransitionTimeToDateTime</c>, which is not public.
    /// </summary>
    /// <remarks>
    /// The value is read as an instant although it is stated in wall clock. That is deliberately
    /// sloppy and deliberately safe: it is only ever the centre of a seventy-two hour exclusion, which
    /// is wider than any offset the misreading can be out by.
    /// </remarks>
    private static long TransitionTicks(int year, TimeZoneInfo.TransitionTime transition)
    {
        int daysInMonth = DateTime.DaysInMonth(year, transition.Month);

        int day;
        if (transition.IsFixedDateRule)
        {
            day = Math.Min(transition.Day, daysInMonth);
        }
        else if (transition.Week <= 4)
        {
            int firstDayOfWeek = (int) new DateTime(year, transition.Month, 1).DayOfWeek;
            day = 1 + ((int) transition.DayOfWeek - firstDayOfWeek + 7) % 7 + (transition.Week - 1) * 7;
            if (day > daysInMonth)
            {
                day -= 7;
            }
        }
        else
        {
            // Week 5 means "the last one in the month", however many the month has.
            int lastDayOfWeek = (int) new DateTime(year, transition.Month, daysInMonth).DayOfWeek;
            day = daysInMonth - (lastDayOfWeek - (int) transition.DayOfWeek + 7) % 7;
        }

        return new DateTime(year, transition.Month, day).Ticks + transition.TimeOfDay.TimeOfDay.Ticks;
    }

    private static void AddExclusion(List<Interval> exclusions, long centre, long radius, long windowStart, long windowEnd)
    {
        long start = centre < radius ? 0 : centre - radius;
        long end = centre > long.MaxValue - radius ? long.MaxValue : centre + radius;

        AddInterval(exclusions, start, end, windowStart, windowEnd);
    }

    private static void AddInterval(List<Interval> exclusions, long start, long end, long windowStart, long windowEnd)
    {
        if (end <= windowStart || start >= windowEnd)
        {
            return;
        }

        exclusions.Add(new Interval(Math.Max(start, windowStart), Math.Min(end, windowEnd)));
    }

    /// <summary>The window with the exclusions taken out of it, each remaining piece carrying the offset read at its middle.</summary>
    private static Segment[] Complement(List<Interval> exclusions, long windowStart, long windowEnd, TimeZoneInfo zone)
    {
        exclusions.Sort(static (left, right) => left.Start.CompareTo(right.Start));

        List<Segment> segments = [];
        long cursor = windowStart;

        foreach (Interval exclusion in exclusions)
        {
            if (exclusion.Start > cursor)
            {
                AddSegment(segments, cursor, exclusion.Start, zone);
            }

            cursor = Math.Max(cursor, exclusion.End);
        }

        if (cursor < windowEnd)
        {
            AddSegment(segments, cursor, windowEnd, zone);
        }

        return segments.ToArray();
    }

    private static void AddSegment(List<Segment> segments, long start, long end, TimeZoneInfo zone)
    {
        long middle = start + (end - start) / 2;
        segments.Add(new Segment(start, end, OffsetTicksAt(zone, middle)));
    }

    /// <summary>
    /// Holds the table against the zone it claims to describe: the offset at the ends and the middle of
    /// every segment, the wall clock at those three points being neither skipped nor repeated and
    /// carrying the same offset read the other way round, and every daily reading that falls inside a
    /// segment.
    /// </summary>
    private static bool Verify(TimeZoneInfo zone, Segment[] segments, long scanStart, long[] sampledOffsets)
    {
        foreach (Segment segment in segments)
        {
            if (segment.StartUtcTicks >= segment.EndUtcTicks)
            {
                return false;
            }

            long middle = segment.StartUtcTicks + (segment.EndUtcTicks - segment.StartUtcTicks) / 2;
            if (!Agrees(zone, segment, segment.StartUtcTicks)
                || !Agrees(zone, segment, middle)
                || !Agrees(zone, segment, segment.EndUtcTicks - 1))
            {
                return false;
            }
        }

        int index = 0;
        foreach (Segment segment in segments)
        {
            while (index < sampledOffsets.Length && scanStart + (long) index * TimeSpan.TicksPerDay < segment.StartUtcTicks)
            {
                index++;
            }

            while (index < sampledOffsets.Length && scanStart + (long) index * TimeSpan.TicksPerDay < segment.EndUtcTicks)
            {
                if (sampledOffsets[index] != segment.OffsetTicks)
                {
                    return false;
                }

                index++;
            }
        }

        return true;
    }

    private static bool Agrees(TimeZoneInfo zone, Segment segment, long utcTicks)
    {
        if (OffsetTicksAt(zone, utcTicks) != segment.OffsetTicks)
        {
            return false;
        }

        // And the other direction, which is the one the search actually walks in: the wall clock this
        // instant reads has to exist exactly once and resolve back to the same offset.
        DateTime wallClock = new DateTime(utcTicks + segment.OffsetTicks, DateTimeKind.Unspecified);

        return !zone.IsInvalidTime(wallClock)
               && !zone.IsAmbiguousTime(wallClock)
               && zone.GetUtcOffset(wallClock).Ticks == segment.OffsetTicks;
    }

    private static long OffsetTicksAt(TimeZoneInfo zone, long utcTicks)
    {
        return TimeZones.GetUtcOffset(new DateTimeOffset(utcTicks, TimeSpan.Zero), zone).Ticks;
    }

    /// <summary>A stretch of UTC time over which the zone's offset is constant and no transition is within two days.</summary>
    [StructLayout(LayoutKind.Auto)]
    internal readonly struct Segment(long startUtcTicks, long endUtcTicks, long offsetTicks)
    {
        internal long StartUtcTicks { get; } = startUtcTicks;

        internal long EndUtcTicks { get; } = endUtcTicks;

        internal long OffsetTicks { get; } = offsetTicks;

        internal bool Contains(long utcTicks) => utcTicks >= StartUtcTicks && utcTicks < EndUtcTicks;

        internal bool ContainsWallClock(long wallClockTicks)
        {
            return wallClockTicks >= StartUtcTicks + OffsetTicks && wallClockTicks < EndUtcTicks + OffsetTicks;
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct Interval(long start, long end)
    {
        internal long Start { get; } = start;

        internal long End { get; } = end;
    }
}
