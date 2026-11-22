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

using Quartz.Impl.Calendar;

namespace Quartz.Tests.Unit.Impl.Calendar;

/// <summary>
/// A calendar's rule is read on the wall clock of its <see cref="BaseCalendar.TimeZone" />, so the same
/// rule in two zones excludes different instants. Two such calendars are not equal.
/// </summary>
public sealed class CalendarTimeZoneEqualityTest
{
    /// <summary>
    /// A fixed offset with no transitions, so the case reads the same on every machine.
    /// </summary>
    private static readonly TimeZoneInfo plusNine =
        TimeZoneInfo.CreateCustomTimeZone("Test+09", TimeSpan.FromHours(9), "Test+09", "Test+09");

    private static readonly (string Name, Func<BaseCalendar> Build)[] calendars =
    [
        (nameof(AnnualCalendar), static () =>
        {
            AnnualCalendar calendar = new();
            calendar.AddExcludedDay(new MonthDay(12, 25));
            return calendar;
        }),

        (nameof(HolidayCalendar), static () =>
        {
            HolidayCalendar calendar = new();
            calendar.AddExcludedDay(new DateOnly(2026, 7, 27));
            return calendar;
        }),

        (nameof(MonthlyCalendar), static () =>
        {
            MonthlyCalendar calendar = new();
            calendar.AddExcludedDay(20);
            return calendar;
        }),

        (nameof(WeeklyCalendar), static () => new WeeklyCalendar()),

        (nameof(CronCalendar), static () => new CronCalendar(null, "0 * 13-14 * * ?", TimeZoneInfo.Utc)),

        (nameof(DailyCalendar), static () => new DailyCalendar(new TimeOnly(1, 0), new TimeOnly(2, 0))),
    ];

    public static IEnumerable<TestCaseData> Calendars()
    {
        return calendars.Select(x => new TestCaseData(x.Build).SetArgDisplayNames(x.Name));
    }

    [TestCaseSource(nameof(Calendars))]
    public void TheSameRuleInAnotherZone_IsNotEqual(Func<BaseCalendar> build)
    {
        BaseCalendar utc = build();
        utc.TimeZone = TimeZoneInfo.Utc;
        BaseCalendar shifted = build();
        shifted.TimeZone = plusNine;

        FirstInstantTheyDisagreeOn(utc, shifted).Should().NotBeNull(
            "the rule is read on each calendar's own wall clock, so the two exclude different instants");
        Equals(utc, shifted).Should().BeFalse(
            "calendars that exclude different instants are not equal");
        Equals(shifted, utc).Should().BeFalse(
            "equality is symmetric");
    }

    [TestCaseSource(nameof(Calendars))]
    public void TheSameRuleInTheSameZone_IsEqual(Func<BaseCalendar> build)
    {
        BaseCalendar first = build();
        first.TimeZone = plusNine;
        BaseCalendar second = build();
        second.TimeZone = plusNine;

        Equals(first, second).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    private static DateTimeOffset? FirstInstantTheyDisagreeOn(ICalendar first, ICalendar second)
    {
        DateTimeOffset start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (DateTimeOffset time = start; time < start.AddYears(1); time = time.AddMinutes(30))
        {
            if (first.IsTimeIncluded(time) != second.IsTimeIncluded(time))
            {
                return time;
            }
        }

        return null;
    }
}
