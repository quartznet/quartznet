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

using System.Reflection;

using Quartz.Impl.Calendar;

namespace Quartz.Tests.Unit.Impl.Calendar;

/// <summary>
/// A calendar excludes a time if its own rule excludes it <em>or</em> if the calendar it is based on
/// does, which is what makes "not on holidays, and not outside business hours" two calendars rather
/// than one — the semantic <c>tutorial/more-about-triggers.md</c> teaches.
/// </summary>
/// <remarks>
/// Every shipped calendar honours it, but not all of them the same way: <see cref="CronCalendar" />
/// and <see cref="DailyCalendar" /> reach <see cref="BaseCalendar.CalendarBase" /> directly instead of
/// calling <c>base.IsTimeIncluded</c>, so a refactor that centralised the chain in
/// <see cref="BaseCalendar" /> would leave those two behind with nothing else failing. The failure
/// mode is a job firing during an excluded period, which is silent and only visible in production, so
/// the chain is pinned once per calendar type rather than left to the calendars' own fixtures.
/// </remarks>
public sealed class CalendarChainingTest
{
    /// <summary>
    /// The day the base calendar excludes: a Monday, so no calendar under test excludes it for a
    /// reason of its own.
    /// </summary>
    private static readonly DateOnly holiday = new(2026, 7, 6);

    /// <summary>
    /// Noon on the excluded day. Every calendar under test includes this instant by its own rule, so
    /// only the base can be what excludes it.
    /// </summary>
    private static readonly DateTimeOffset noonOnTheHoliday = new(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Noon on the next day, a Tuesday, which nothing in the chain excludes.
    /// </summary>
    private static readonly DateTimeOffset noonOnTheDayAfter = new(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The first instant past the base's exclusion. Every calendar here answers a "when next?" with the
    /// start of the day the base lets go, because a <see cref="HolidayCalendar" /> excludes whole days.
    /// </summary>
    private static readonly DateTimeOffset startOfTheDayAfter = new(2026, 7, 7, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// One entry per shipped calendar, each configured so that its own rule includes both instants
    /// below — which is what leaves the base as the only thing that can exclude either of them.
    /// </summary>
    private static readonly (string Name, Func<BaseCalendar> Build)[] chains =
    [
        // A calendar with no rule of its own: what it excludes is exactly what its base excludes.
        (nameof(BaseCalendar), static () => new BaseCalendar()),

        // Christmas Day, which is neither day in this fixture.
        (nameof(AnnualCalendar), static () =>
        {
            AnnualCalendar calendar = new();
            calendar.AddExcludedDay(new MonthDay(12, 25));
            return calendar;
        }),

        // A holiday of its own, three weeks after the base's.
        (nameof(HolidayCalendar), static () =>
        {
            HolidayCalendar calendar = new();
            calendar.AddExcludedDay(new DateOnly(2026, 7, 27));
            return calendar;
        }),

        // The 20th of the month; the days here are the 6th and the 7th.
        (nameof(MonthlyCalendar), static () =>
        {
            MonthlyCalendar calendar = new();
            calendar.AddExcludedDay(20);
            return calendar;
        }),

        // Weekends, which is the default; both days here are weekdays.
        (nameof(WeeklyCalendar), static () => new WeeklyCalendar()),

        // Two afternoon hours, which noon is not inside.
        (nameof(CronCalendar), static () => new CronCalendar(null, "0 * 13-14 * * ?", TimeZoneInfo.Utc)),

        // An hour in the small hours, which noon is not inside.
        (nameof(DailyCalendar), static () => new DailyCalendar(new TimeOnly(1, 0), new TimeOnly(2, 0))),
    ];

    public static IEnumerable<TestCaseData> ShippedCalendars()
    {
        return chains.Select(x => new TestCaseData(x.Build).SetArgDisplayNames(x.Name));
    }

    [TestCaseSource(nameof(ShippedCalendars))]
    public void AnInstantOnlyTheBaseExcludes_IsExcluded(Func<BaseCalendar> build)
    {
        BaseCalendar calendar = Chain(build);

        calendar.CalendarBase!.IsTimeIncluded(noonOnTheHoliday).Should().BeFalse(
            "the base is what this case turns on, so a base that included the instant would prove nothing");
        calendar.IsTimeIncluded(noonOnTheHoliday).Should().BeFalse(
            "a calendar excludes what its base excludes, whatever its own rule says about the instant");
    }

    [TestCaseSource(nameof(ShippedCalendars))]
    public void AnInstantNeitherExcludes_IsIncluded(Func<BaseCalendar> build)
    {
        BaseCalendar calendar = Chain(build);

        calendar.IsTimeIncluded(noonOnTheDayAfter).Should().BeTrue(
            "chaining narrows what a calendar includes, so a calendar that excluded an instant both "
            + "halves of the chain include would be excluding it for no stated reason");
    }

    [TestCaseSource(nameof(ShippedCalendars))]
    public void GetNextIncludedTime_WalksPastTheBasesExclusion(Func<BaseCalendar> build)
    {
        BaseCalendar calendar = Chain(build);

        DateTimeOffset next = calendar.GetNextIncludedTimeUtc(noonOnTheHoliday);

        next.Should().Be(startOfTheDayAfter,
            "the base excludes the whole day, so the next included instant is the first one of the next "
            + "day — a walk that answered with a time the base still excludes would schedule a firing "
            + "into the exclusion it was asked to step over");
        calendar.IsTimeIncluded(next).Should().BeTrue(
            "the instant a calendar names as next included has to be one it includes");
    }

    /// <summary>
    /// Every public calendar the library ships has a case above, so a new one cannot be added without
    /// saying what it does with its base.
    /// </summary>
    [Test]
    public void EveryShippedCalendar_HasACase()
    {
        List<string> shipped = typeof(BaseCalendar).Assembly.GetTypes()
            .Where(x => x.IsPublic && !x.IsAbstract && typeof(ICalendar).IsAssignableFrom(x))
            .Select(x => x.Name)
            .ToList();

        chains.Select(x => x.Name).Should().BeEquivalentTo(shipped,
            "a calendar that is not in the case list is one whose chaining nothing checks");
    }

    /// <summary>
    /// Builds the calendar under test over a base that excludes one whole day, with both halves reading
    /// UTC so the day's edges are the same wherever the test runs.
    /// </summary>
    private static BaseCalendar Chain(Func<BaseCalendar> build)
    {
        HolidayCalendar bottom = new() { TimeZone = TimeZoneInfo.Utc };
        bottom.AddExcludedDay(holiday);

        BaseCalendar top = build();
        top.TimeZone = TimeZoneInfo.Utc;
        top.CalendarBase = bottom;
        return top;
    }
}
