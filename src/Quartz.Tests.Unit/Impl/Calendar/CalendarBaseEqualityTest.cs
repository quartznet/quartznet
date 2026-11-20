using System;

using Quartz.Impl.Calendar;

namespace Quartz.Tests.Unit.Impl.Calendar;

public class CalendarBaseEqualityTest
{
    [Test]
    public void CalendarWithoutBase_DoesNotEqualSameCalendarOverBase()
    {
        var holidays = new HolidayCalendar();
        holidays.AddExcludedDate(new DateTime(2024, 12, 25));

        var plain = new WeeklyCalendar { TimeZone = TimeZoneInfo.Utc };
        var layered = new WeeklyCalendar(holidays) { TimeZone = TimeZoneInfo.Utc };

        plain.Equals(layered).Should().BeFalse(
            "a calendar with no base excludes less than the same calendar over a base");
        layered.Equals(plain).Should().BeFalse(
            "equality is symmetric");
    }
}
