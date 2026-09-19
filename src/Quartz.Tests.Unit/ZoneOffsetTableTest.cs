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

#nullable enable

namespace Quartz.Tests.Unit;

/// <summary>
/// <see cref="ZoneOffsetTable" /> held against the zone it claims to describe, by an hourly scan this
/// file performs itself.
/// </summary>
/// <remarks>
/// The table makes two promises, and this asserts both for sixty years of every zone in the list.
/// The offset it reports for an instant is the offset <see cref="TimeZoneInfo" /> reports - checked
/// at every hour of every segment, not at the daily and three-point samples the build verifies
/// itself with. And no point of any segment is within two days of any transition, where a transition
/// is one this file found by its own scan rather than one the table admits to knowing about. The
/// second promise is the one the fast path's correctness rests on: it is what makes a wall clock
/// inside a segment impossible to have been skipped or repeated.
/// </remarks>
/// <author>Marko Lahma (.NET)</author>
public class ZoneOffsetTableTest
{
    private const int FirstYear = 1990;

    private const int LastYear = 2050;

    /// <summary>The distance every segment must keep from every transition.</summary>
    private static readonly long safetyTicks = 48 * TimeSpan.TicksPerHour;

    public static IEnumerable<string> ZoneIds => CronExpressionFastPathDifferentialTest.ZoneIds;

    [TestCaseSource(nameof(ZoneIds))]
    public void SegmentsAgreeWithTheZoneAndStandTwoDaysClearOfEveryTransition(string zoneId)
    {
        TimeZoneInfo? zone = CronExpressionFastPathDifferentialTest.TryResolveZone(zoneId);
        if (zone is null)
        {
            Assert.Ignore($"time zone {zoneId} is not available on this system");
            return;
        }

        List<ZoneOffsetTable.Segment> segments = [];
        for (int year = FirstYear - FirstYear % ZoneOffsetTable.WindowYears; year <= LastYear; year += ZoneOffsetTable.WindowYears)
        {
            ZoneOffsetTable table = ZoneOffsetTable.Build(zone, year / ZoneOffsetTable.WindowYears);
            table.IsSupported.Should().BeTrue("the table for {0} starting {1} reproduced the zone's own answers", zoneId, year);
            segments.AddRange(table.Segments.ToArray());
        }

        segments.Should().NotBeEmpty("a zone with no safe stretch of time at all would leave the fast path unreachable");

        long start = new DateTime(FirstYear, 1, 1).Ticks;
        long end = new DateTime(LastYear + 1, 1, 1).Ticks;

        List<long> transitions = [];
        int index = 0;
        long previousOffset = OffsetAt(zone, start);

        for (long instant = start + TimeSpan.TicksPerHour; instant < end; instant += TimeSpan.TicksPerHour)
        {
            long offset = OffsetAt(zone, instant);
            if (offset != previousOffset)
            {
                transitions.Add(Bisect(zone, instant - TimeSpan.TicksPerHour, instant));
            }

            previousOffset = offset;

            // The segments are sorted and the scan runs forwards, so this walks them once.
            while (index < segments.Count && segments[index].EndUtcTicks <= instant)
            {
                index++;
            }

            if (index < segments.Count && segments[index].Contains(instant))
            {
                segments[index].OffsetTicks.Should().Be(
                    offset,
                    "the segment covering {0:O} in {1} claims an offset the zone does not have",
                    new DateTimeOffset(instant, TimeSpan.Zero), zoneId);
            }
        }

        if (zone.GetAdjustmentRules().Length > 0)
        {
            transitions.Should().NotBeEmpty("this scan is what the distance assertion below is measured against, and a zone carrying adjustment rules has something to find");
        }

        foreach (ZoneOffsetTable.Segment segment in segments)
        {
            foreach (long transition in transitions)
            {
                bool clear = transition <= segment.StartUtcTicks - safetyTicks || transition >= segment.EndUtcTicks + safetyTicks;

                clear.Should().BeTrue(
                    "the segment {0:O}-{1:O} in {2} comes within two days of the transition at {3:O}, so a wall clock inside it could be one the clocks skipped or repeated",
                    new DateTimeOffset(segment.StartUtcTicks, TimeSpan.Zero),
                    new DateTimeOffset(segment.EndUtcTicks, TimeSpan.Zero),
                    zoneId,
                    new DateTimeOffset(transition, TimeSpan.Zero));
            }
        }

        TestContext.Out.WriteLine($"{zoneId}: {segments.Count} segments, {transitions.Count} transitions, {Coverage(segments, start, end):P2} of {FirstYear}-{LastYear} covered");
    }

    /// <summary>
    /// A window outside the years a cron expression can name has no table, so nothing builds one for a
    /// probe that will never be read again.
    /// </summary>
    [Test]
    public void WindowsOutsideTheSchedulableYearsAreNeverBuilt()
    {
        ZoneOffsetTable.WindowIndexFor(new DateTime(2, 1, 1).Ticks).Should().BeNegative("the binary search in GetPreviousValidTimeBefore opens in year 2");
        ZoneOffsetTable.WindowIndexFor(new DateTime(1969, 12, 31).Ticks).Should().BeNegative("a cron year field cannot name a year before 1970");
        ZoneOffsetTable.WindowIndexFor(new DateTime(9000, 1, 1).Ticks).Should().BeNegative("past the year the scheduler gives up looking in");

        ZoneOffsetTable.WindowIndexFor(new DateTime(2026, 6, 1).Ticks)
            .Should().Be(ZoneOffsetTable.WindowIndexFor(new DateTime(2027, 6, 1).Ticks), "windows are eight years wide and aligned, so neighbours share one");
    }

    private static double Coverage(List<ZoneOffsetTable.Segment> segments, long start, long end)
    {
        long covered = 0;
        foreach (ZoneOffsetTable.Segment segment in segments)
        {
            covered += Math.Max(0, Math.Min(segment.EndUtcTicks, end) - Math.Max(segment.StartUtcTicks, start));
        }

        return covered / (double) (end - start);
    }

    private static long OffsetAt(TimeZoneInfo zone, long utcTicks)
    {
        return zone.GetUtcOffset(new DateTimeOffset(utcTicks, TimeSpan.Zero)).Ticks;
    }

    private static long Bisect(TimeZoneInfo zone, long low, long high)
    {
        long before = OffsetAt(zone, low);
        while (high - low > TimeSpan.TicksPerSecond)
        {
            long middle = low + (high - low) / 2;
            if (OffsetAt(zone, middle) == before)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return high;
    }
}
