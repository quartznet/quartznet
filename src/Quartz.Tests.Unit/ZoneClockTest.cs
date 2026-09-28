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
/// <see cref="ZoneClock" />'s policy: which windows get a table, and how often one is built.
/// </summary>
/// <remarks>
/// Every test here builds a zone instance of its own. A clock is keyed by instance and shared by the
/// whole process, so a system zone would arrive carrying whatever windows earlier tests asked about -
/// which is the very dependency the first test pins.
/// </remarks>
/// <author>Marko Lahma (.NET)</author>
public class ZoneClockTest
{
    /// <summary>
    /// Four windows asked about first do not keep a fifth out (#3918). With a cap of four, the cron
    /// suite's dates in 2005-2024 left the machine's own zone unable to build 2032.
    /// </summary>
    [Test]
    public void AWindowGetsATableWhateverWasAskedAboutFirst()
    {
        ZoneClock clock = ZoneClock.For(CreatePrivateZone());

        foreach (int year in new[] { 2001, 2009, 2017, 2041 })
        {
            clock.TryGetTable(MidYear(year), out _).Should().BeTrue("{0} is a year a cron expression can name", year);
        }

        clock.TryGetTable(MidYear(2033), out ZoneOffsetTable? table).Should().BeTrue(
            "the clock is shared by every caller in the process, so four windows somebody else asked about first must not keep the fast path out of a fifth");
        table!.Contains(MidYear(2033)).Should().BeTrue();
    }

    [Test]
    public void EveryYearACronExpressionCanNameHasATable()
    {
        ZoneClock clock = ZoneClock.For(CreatePrivateZone());

        for (int year = TriggerConstants.EarliestYear; year <= TriggerConstants.YearToGiveUpSchedulingAt; year++)
        {
            clock.TryGetTable(MidYear(year), out ZoneOffsetTable? table).Should().BeTrue("{0} is a year a cron expression can name", year);
            table!.Contains(MidYear(year)).Should().BeTrue();
        }

        clock.TryGetTable(MidYear(TriggerConstants.EarliestYear - ZoneOffsetTable.WindowYears), out _)
            .Should().BeFalse("no year in that window is one a cron year field can name");
        clock.TryGetTable(MidYear(TriggerConstants.YearToGiveUpSchedulingAt + ZoneOffsetTable.WindowYears), out _)
            .Should().BeFalse("that window is wholly past the year the scheduler gives up looking in");
    }

    [Test]
    public void AWindowIsBuiltOnceAndKept()
    {
        ZoneClock clock = ZoneClock.For(CreatePrivateZone());

        clock.TryGetTable(MidYear(2009), out ZoneOffsetTable? first).Should().BeTrue();
        clock.TryGetTable(MidYear(2033), out _).Should().BeTrue();
        clock.TryGetTable(MidYear(2010), out ZoneOffsetTable? again).Should().BeTrue();

        again.Should().BeSameAs(first, "nothing is evicted, so a caller probing back and forth across windows cannot make one be rebuilt");
    }

    /// <summary>
    /// A zone no other test can have touched, with a daylight-saving rule of its own so that its tables
    /// have transitions to cut around, and no dependence on this machine's time zone database.
    /// </summary>
    internal static TimeZoneInfo CreatePrivateZone()
    {
        TimeZoneInfo.TransitionTime start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 3, 5, DayOfWeek.Sunday);
        TimeZoneInfo.TransitionTime end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 4, 0, 0), 10, 5, DayOfWeek.Sunday);
        TimeZoneInfo.AdjustmentRule rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1), start, end);

        return TimeZoneInfo.CreateCustomTimeZone("Quartz/ZoneClockTest", TimeSpan.FromHours(2), "Quartz zone clock test", "Quartz standard", "Quartz daylight", [rule]);
    }

    private static long MidYear(int year)
    {
        return new DateTime(year, 6, 15, 12, 0, 0).Ticks;
    }
}
