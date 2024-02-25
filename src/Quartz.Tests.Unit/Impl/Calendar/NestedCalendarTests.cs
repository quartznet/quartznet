using System;

using FluentAssertions;

using NUnit.Framework;

using Quartz.Impl.Calendar;

namespace Quartz.Tests.Unit.Impl.Calendar;

public class NestedCalendarTests
{
    [Test]
    public void NestedCalendarGetNextIncludedTimeUtc_AndCompletes_WithinExpectedTime()
    {
        // Perf Test - Original but was similar setup would take > 10secs. We need to provide enough space for slow CI.
        const int ExpectToFinishInSeconds = 5;
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

        var holidayCalendar = new HolidayCalendar
        {
            TimeZone = timeZone,
        };
        holidayCalendar.AddExcludedDate(new DateTime(2024, 2, 19));
        holidayCalendar.AddExcludedDate(new DateTime(2024, 5, 27));
        holidayCalendar.AddExcludedDate(new DateTime(2024, 6, 19));
        holidayCalendar.AddExcludedDate(new DateTime(2024, 7, 4));
        holidayCalendar.AddExcludedDate(new DateTime(2024, 9, 2));
        holidayCalendar.AddExcludedDate(new DateTime(2024, 10, 14));
        holidayCalendar.AddExcludedDate(new DateTime(2024, 11, 11));
        holidayCalendar.AddExcludedDate(new DateTime(2024, 11, 28));
        holidayCalendar.AddExcludedDate(new DateTime(2024, 12, 25));

        var weeklyCalendar = new WeeklyCalendar(holidayCalendar)
        {
            TimeZone = timeZone,
        };
        
        weeklyCalendar.SetDayExcluded(DayOfWeek.Tuesday, true);
        
        var calendar = new DailyCalendar(weeklyCalendar, "06:00", "22:00")
        {
            TimeZone = timeZone,
            InvertTimeRange = true,
        };
        var time = new DateTime(2024, 2, 5, 10, 6, 0, DateTimeKind.Utc);
        var expected = new DateTime(2024, 2, 5, 14, 0, 0, DateTimeKind.Utc);

        var d = calendar.GetNextIncludedTimeUtc(time); 
        d.Should().Be(expected);
        
        calendar.ExecutionTimeOf(cal => cal.GetNextIncludedTimeUtc(time).Should().BeLessThan(TimeSpan.FromSeconds(ExpectToFinishInSeconds)));
    }
}