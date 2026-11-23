using Quartz.Dashboard.Components.Shared;
using Quartz.Dashboard.Services;
using Quartz.Impl.Calendar;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// The calendar editor's one translation (#4413): fields as text and ticks into a calendar of one of the six
/// built-in types, with its time zone said and the calendars under it kept.
/// </summary>
public sealed class CalendarFormTest
{
    private static readonly TimeZoneInfo Kathmandu = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kathmandu");

    private static IEnumerable<TestCaseData> EveryEditableType()
    {
        BaseCalendar under = new(Kathmandu) { Description = "Under" };

        HolidayCalendar holiday = new(under) { TimeZone = Kathmandu, Description = "Holidays" };
        holiday.AddExcludedDay(new DateOnly(2026, 12, 25));
        holiday.AddExcludedDay(new DateOnly(2027, 1, 1));
        yield return new TestCaseData(holiday).SetName("Holiday");

        AnnualCalendar annual = new(under) { TimeZone = Kathmandu, Description = "Annual" };
        annual.AddExcludedDay(new MonthDay(2, 29));
        annual.AddExcludedDay(new MonthDay(12, 25));
        yield return new TestCaseData(annual).SetName("Annual");

        WeeklyCalendar weekly = new(under) { TimeZone = Kathmandu, Description = "Weekly" };
        weekly.RemoveExcludedDay(DayOfWeek.Saturday);
        weekly.AddExcludedDay(DayOfWeek.Wednesday);
        yield return new TestCaseData(weekly).SetName("Weekly");

        MonthlyCalendar monthly = new(under) { TimeZone = Kathmandu, Description = "Monthly" };
        monthly.AddExcludedDay(1);
        monthly.AddExcludedDay(31);
        yield return new TestCaseData(monthly).SetName("Monthly");

        DailyCalendar daily = new(new TimeOnly(9, 30), new TimeOnly(17, 0, 0, 250), under) { TimeZone = Kathmandu, Description = "Daily", InvertTimeRange = true };
        yield return new TestCaseData(daily).SetName("Daily");

        CronCalendar cron = new(under, "* * 0-6 ? * MON-FRI", Kathmandu) { Description = "Cron" };
        yield return new TestCaseData(cron).SetName("Cron");
    }

    [TestCaseSource(nameof(EveryEditableType))]
    public void AFormLeftAloneBuildsTheCalendarItWasMadeFrom(ICalendar calendar)
    {
        CalendarFormResult result = CalendarForm.From(calendar).Build();

        result.Errors.Should().BeEmpty();
        result.Calendar.Should().NotBeNull();
        result.Calendar!.GetType().Should().Be(calendar.GetType());
        result.Calendar.Should().Be(calendar,
            "an edit that changes nothing stores the same rules, zone, description and base calendar it read");
        result.Calendar.CalendarBase.Should().NotBeSameAs(calendar.CalendarBase, "the base is a copy, as every calendar handed on is");
    }

    [Test]
    public void ANewCalendarStartsInUtcAndAWeeklyOneWithoutItsWeekend()
    {
        CalendarForm form = CalendarForm.ForNew(CalendarKind.Weekly);

        form.TimeZoneId.Should().Be(TimeZoneInfo.Utc.Id, "a new calendar's zone is said, never the browser's or the host's");
        form.WeeklyDays.Should().BeEquivalentTo([DayOfWeek.Saturday, DayOfWeek.Sunday], "the form starts where the type does");

        form.WeeklyDays.Remove(DayOfWeek.Sunday);
        WeeklyCalendar built = (WeeklyCalendar) form.Build().Calendar!;

        built.DaysExcluded.Should().BeEquivalentTo([DayOfWeek.Saturday],
            "a day taken off the form is taken off the calendar, though the type starts out excluding it");
        built.TimeZone.Should().Be(TimeZoneInfo.Utc);
        built.CalendarBase.Should().BeNull();
    }

    [Test]
    public void TheHolidayDatesAreReadOneALine()
    {
        CalendarForm form = CalendarForm.ForNew(CalendarKind.Holiday);
        form.HolidayDates = "2026-12-24\r\n\r\n  2026-12-25  \n2026-12-24";
        form.Description = "  Office  ";

        HolidayCalendar built = (HolidayCalendar) form.Build().Calendar!;

        built.DaysExcluded.Should().Equal([new DateOnly(2026, 12, 24), new DateOnly(2026, 12, 25)],
            "blank lines and surrounding space are not dates, and a date given twice is one");
        built.Description.Should().Be("Office");
    }

    [Test]
    public void EachMistakeIsNamedAndNothingIsBuilt()
    {
        CalendarForm holiday = CalendarForm.ForNew(CalendarKind.Holiday);
        holiday.HolidayDates = "2026-12-25\n25.12.2026\n2026-13-01";
        holiday.TimeZoneId = "Mars/Olympus_Mons";

        CalendarFormResult result = holiday.Build();

        result.Calendar.Should().BeNull();
        result.Errors.Should().Equal(
            "Time zone 'Mars/Olympus_Mons' is not one this dashboard knows.",
            "'25.12.2026' is not a date written as yyyy-MM-dd.",
            "'2026-13-01' is not a date written as yyyy-MM-dd.");
    }

    [TestCase("", "17:00", "A daily calendar needs a range start.")]
    [TestCase("9 o'clock", "17:00", "The range start '9 o'clock' is not a time of day written as HH:mm, HH:mm:ss or HH:mm:ss.fff.")]
    [TestCase("17:00", "09:00", "The range must start before it ends, and may not cross midnight.")]
    [TestCase("09:00", "09:00", "The range must start before it ends, and may not cross midnight.")]
    public void ADailyRangeMustBeTwoTimesOfOneDay(string start, string end, string error)
    {
        CalendarForm form = CalendarForm.ForNew(CalendarKind.Daily);
        form.DailyStart = start;
        form.DailyEnd = end;

        form.Build().Errors.Should().Equal(error);
    }

    [Test]
    public void ACronCalendarNeedsAnExpressionThatParses()
    {
        CalendarForm form = CalendarForm.ForNew(CalendarKind.Cron);

        form.Build().Errors.Should().Equal("A cron calendar needs an expression: it excludes every second the expression matches.");

        form.CronExpression = "every Monday";
        form.Build().Errors.Should().ContainSingle().Which.Should().StartWith("Invalid cron expression: ");

        form.CronExpression = "* * 0-6 ? * *";
        form.TimeZoneId = Kathmandu.Id;
        CronCalendar built = (CronCalendar) form.Build().Calendar!;
        built.TimeZone.Should().Be(Kathmandu, "the expression's hours are read in the zone the form names");
        built.CronExpression.TimeZone.Should().Be(Kathmandu);
    }

    [Test]
    public void ACalendarThatExcludesEveryDayIsRefused()
    {
        CalendarForm weekly = CalendarForm.ForNew(CalendarKind.Weekly);
        weekly.WeeklyDays.UnionWith(Enum.GetValues<DayOfWeek>());
        weekly.Build().Errors.Should().Equal("A weekly calendar that excludes every day of the week leaves a trigger nothing to fire at.");

        CalendarForm monthly = CalendarForm.ForNew(CalendarKind.Monthly);
        monthly.MonthlyDays.UnionWith(Enumerable.Range(1, 31));
        monthly.Build().Errors.Should().Equal("A monthly calendar that excludes every day of the month leaves a trigger nothing to fire at.");

        CalendarForm annual = CalendarForm.ForNew(CalendarKind.Annual);
        for (int month = 1; month <= 12; month++)
        {
            for (int day = 1; day <= CalendarReading.DaysIn(month); day++)
            {
                annual.AnnualDays.Add(new MonthDay(month, day));
            }
        }

        annual.Build().Errors.Should().Equal("An annual calendar that excludes every day of the year leaves a trigger nothing to fire at.");
    }

    [Test]
    public void AnEditKeepsTheTypeAndChangesOnlyTheCalendarsOwnRules()
    {
        HolidayCalendar under = new() { TimeZone = TimeZoneInfo.Utc };
        under.AddExcludedDay(new DateOnly(2026, 1, 1));
        HolidayCalendar holidays = new(under) { TimeZone = Kathmandu };
        holidays.AddExcludedDay(new DateOnly(2026, 12, 25));
        CalendarForm form = CalendarForm.From(holidays);

        form.IsNew.Should().BeFalse();
        form.HolidayDates.Should().Be("2026-12-25");
        form.HolidayDates += "\n2026-12-26";

        HolidayCalendar built = (HolidayCalendar) form.Build().Calendar!;

        built.DaysExcluded.Should().Equal([new DateOnly(2026, 12, 25), new DateOnly(2026, 12, 26)]);
        built.TimeZone.Should().Be(Kathmandu);
        built.CalendarBase.Should().Be(under, "the calendars under it go back as they were read");
    }

    [Test]
    public void ACalendarTheDashboardDoesNotEditHasNoForm()
    {
        Action custom = () => CalendarForm.From(new BaseCalendar());

        custom.Should().Throw<ArgumentException>().WithMessage("A BaseCalendar is not edited by the dashboard.*");

        Action baseKind = () => CalendarForm.ForNew(CalendarKind.Custom);
        baseKind.Should().Throw<ArgumentOutOfRangeException>();
    }
}
