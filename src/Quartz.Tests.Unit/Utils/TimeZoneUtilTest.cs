using FluentAssertions;

using Quartz.Impl.Calendar;
using Quartz.Util;

namespace Quartz.Tests.Unit.Utils;

[TestFixture]
public class TimeZoneUtilTest
{
    [Test]
    public void ShouldBeAbleToFindWithAlias()
    {
        var infoWithUtc = TimeZoneUtil.FindTimeZoneById("UTC");
        var infoWithUniversalCoordinatedTime = TimeZoneUtil.FindTimeZoneById("Coordinated Universal Time");

        Assert.That(infoWithUniversalCoordinatedTime, Is.EqualTo(infoWithUtc));
    }

    [Test]
    public void GetNextIncludedTimeUtc_CrashOriginal2270()
    {
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

        var weeklyCalendar = new WeeklyCalendar() { TimeZone = timeZone, };

        var dailyCalendar = new DailyCalendar(weeklyCalendar, "06:00", "22:00") { TimeZone = timeZone, InvertTimeRange = true, };

        var holidayCalendar = new HolidayCalendar(dailyCalendar) { TimeZone = timeZone, };
        holidayCalendar.AddExcludedDate(new DateTime(2024, 2, 19));
        holidayCalendar.AddExcludedDate(new DateTime(2024, 5, 27));
        holidayCalendar.AddExcludedDate(new DateTime(2024, 6, 19));
        holidayCalendar.AddExcludedDate(new DateTime(2024, 7, 4));
        holidayCalendar.AddExcludedDate(new DateTime(2024, 9, 2));
        holidayCalendar.AddExcludedDate(new DateTime(2024, 10, 14));
        holidayCalendar.AddExcludedDate(new DateTime(2024, 11, 11));
        holidayCalendar.AddExcludedDate(new DateTime(2024, 11, 28));
        holidayCalendar.AddExcludedDate(new DateTime(2024, 12, 25));

        var time = new DateTime(2024, 2, 5, 10, 6, 0, DateTimeKind.Utc);
        var expected = new DateTime(2024, 2, 5, 14, 0, 0, DateTimeKind.Utc);

        var d = holidayCalendar.GetNextIncludedTimeUtc(time);
        d.Should().Be(expected);
    }
}