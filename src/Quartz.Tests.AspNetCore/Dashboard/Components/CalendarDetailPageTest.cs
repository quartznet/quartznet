using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Pages;
using Quartz.Impl.Calendar;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// Calendar Detail shows each calendar type's own rules (#4413): a holiday calendar's dates, an annual
/// calendar's days of the year, a weekly or monthly calendar's days, a daily calendar's range, a cron
/// calendar's expression and what it excludes next, each with its time zone and the calendars under it.
/// </summary>
/// <remarks>
/// The page used to print every public property's <c>ToString()</c>, through reflection: a holiday
/// calendar's dates read <c>System.Collections.Generic.SortedSet`1[System.DateOnly]</c>. The context's clock
/// reads 14:23 UTC on Monday 5 October 2026.
/// </remarks>
public sealed class CalendarDetailPageTest
{
    private const string Name = "office";

    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
        context.WithScheduler();
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    /// <summary>
    /// Kathmandu is UTC+05:45, which is neither a CI host's zone nor a whole-hour offset, so the page cannot
    /// show it by accident.
    /// </summary>
    [Test]
    public void AHolidayCalendarListsItsDatesAndTheZoneTheyAreReadIn()
    {
        TimeZoneInfo kathmandu = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kathmandu");
        HolidayCalendar holidays = new() { TimeZone = kathmandu, Description = "Office holidays" };
        holidays.AddExcludedDay(new DateOnly(2026, 12, 25));
        holidays.AddExcludedDay(new DateOnly(2026, 12, 24));

        IRenderedComponent<CalendarDetail> page = Render(holidays);

        page.WaitForAssertion(() => page.FindAll("[data-testid=calendar-holiday-dates] li").Select(Text).Should().Equal(
            ["2026-12-24 Thursday", "2026-12-25 Friday"],
            "the dates are the calendar's rules, in order, not the name of the set holding them"));
        page.Markup.Should().NotContain("SortedSet", "the page reads the calendar's members rather than printing their types");
        page.Find("[data-testid=calendar-kind]").TextContent.Should().Contain("Holiday calendar");
        page.Find("[data-testid=calendar-time-zone]").TextContent.Should().Contain(kathmandu.Id,
            "a date excludes a whole day in the calendar's own zone, so the zone is said beside the dates");
        page.Markup.Should().Contain("Office holidays");
    }

    [Test]
    public void AnAnnualCalendarShowsItsDaysOfTheYearInAGrid()
    {
        AnnualCalendar annual = new() { TimeZone = TimeZoneInfo.Utc };
        annual.AddExcludedDay(new MonthDay(12, 25));
        annual.AddExcludedDay(new MonthDay(1, 1));

        IRenderedComponent<CalendarDetail> page = Render(annual);

        page.WaitForAssertion(() => page.Find("[data-testid=calendar-annual-summary]").TextContent.Should().Be(
            "Excludes January 1 and December 25, every year."));
        IReadOnlyList<IElement> boxes = page.FindAll("[data-testid=calendar-annual-grid] input[type=checkbox]");
        boxes.Should().HaveCount(366, "a day of the year is any day of a leap year");
        boxes.Where(box => box.HasAttribute("checked")).Select(box => box.GetAttribute("data-month-day")).Should().Equal(["--01-01", "--12-25"]);
        boxes.Should().OnlyContain(box => box.HasAttribute("disabled"), "the detail page reads; the editor is where days change");
        page.Markup.Should().NotContain("HashSet");
    }

    [Test]
    public void AWeeklyCalendarTicksTheDaysItExcludes()
    {
        WeeklyCalendar weekly = new() { TimeZone = TimeZoneInfo.Utc };
        weekly.AddExcludedDay(DayOfWeek.Friday);

        IRenderedComponent<CalendarDetail> page = Render(weekly);

        page.WaitForAssertion(() => page.Find("[data-testid=calendar-weekly-summary]").TextContent.Should().Be(
            "Excludes Friday, Saturday and Sunday, every week.",
            "a weekly calendar starts out excluding the weekend, and Friday was added to it"));
        page.FindAll("[data-testid=calendar-weekly-days] input").Select(box => box.GetAttribute("data-day") + "=" + box.HasAttribute("checked")).Should().Equal(
            "Monday=False", "Tuesday=False", "Wednesday=False", "Thursday=False", "Friday=True", "Saturday=True", "Sunday=True");
    }

    [Test]
    public void AMonthlyCalendarTicksTheDaysItExcludes()
    {
        MonthlyCalendar monthly = new() { TimeZone = TimeZoneInfo.Utc };
        monthly.AddExcludedDay(15);
        monthly.AddExcludedDay(1);

        IRenderedComponent<CalendarDetail> page = Render(monthly);

        page.WaitForAssertion(() => page.Find("[data-testid=calendar-monthly-summary]").TextContent.Should().StartWith(
            "Excludes days 1 and 15 of every month."));
        IReadOnlyList<IElement> boxes = page.FindAll("[data-testid=calendar-monthly-days] input");
        boxes.Should().HaveCount(31);
        boxes.Where(box => box.HasAttribute("checked")).Select(box => box.GetAttribute("data-day")).Should().Equal(["1", "15"]);
    }

    [Test]
    public void ADailyCalendarSaysItsRangeAndWhetherItIsInverted()
    {
        DailyCalendar daily = new(new TimeOnly(9, 0), new TimeOnly(17, 30)) { TimeZone = TimeZoneInfo.Utc };

        IRenderedComponent<CalendarDetail> page = Render(daily);

        page.WaitForAssertion(() => Text(page.Find("[data-testid=calendar-daily-range]")).Should().Be(
            "Excludes 09:00:00 through 17:30:00 every day."));
        page.Find("[data-testid=calendar-daily-inverted]").TextContent.Should().StartWith("Inverted: no");

        daily.InvertTimeRange = true;
        IRenderedComponent<CalendarDetail> inverted = Render(daily);

        inverted.WaitForAssertion(() => Text(inverted.Find("[data-testid=calendar-daily-range]")).Should().Be(
            "Excludes every time of day except 09:00:00 through 17:30:00.",
            "an inverted range is the part of the day the calendar includes"));
        inverted.Find("[data-testid=calendar-daily-inverted]").TextContent.Should().StartWith("Inverted: yes");
    }

    /// <summary>
    /// 14:23 UTC is 20:08 in Kathmandu, so the first span the expression excludes is 02:00 to 04:00 there the
    /// next morning, which is 20:15 to 22:15 UTC: shown in the calendar's zone, the zone its hours are read in.
    /// </summary>
    [Test]
    public void ACronCalendarShowsItsExpressionAndWhatItExcludesNextInItsOwnZone()
    {
        TimeZoneInfo kathmandu = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kathmandu");
        CronCalendar cron = new(baseCalendar: null, "* * 2-3 ? * *", kathmandu);

        IRenderedComponent<CalendarDetail> page = Render(cron);

        page.WaitForAssertion(() => page.Find("[data-testid=calendar-cron-expression]").TextContent.Should().Be("* * 2-3 ? * *"));
        page.FindAll("[data-testid=calendar-cron-spans] li").Select(Text).Take(2).Should().Equal(
            "2026-10-06 02:00:00 +05:45 until 2026-10-06 04:00:00 +05:45",
            "2026-10-07 02:00:00 +05:45 until 2026-10-07 04:00:00 +05:45");
        page.FindAll("[data-testid=calendar-cron-spans] li").Should().HaveCount(5);
        page.Find("[data-testid=calendar-cron-preview-basis]").TextContent.Should().StartWith(
            $"Read in {kathmandu.Id}, the calendar's time zone.");
    }

    [Test]
    public void TheCalendarsUnderACalendarAreShownLevelByLevel()
    {
        BaseCalendar zoneOnly = new(TimeZoneInfo.Utc) { Description = "Zone only" };
        HolidayCalendar holidays = new(zoneOnly) { TimeZone = TimeZoneInfo.Utc };
        holidays.AddExcludedDay(new DateOnly(2026, 12, 25));
        WeeklyCalendar weekdays = new(holidays) { TimeZone = TimeZoneInfo.Utc };

        IRenderedComponent<CalendarDetail> page = Render(weekdays);

        page.WaitForAssertion(() => page.FindAll("[data-testid=calendar-base] h3").Select(Text).Should().Equal(
            "1. Holiday calendar", "2. Base calendar"));
        IReadOnlyList<IElement> levels = page.FindAll("[data-testid=calendar-base]");
        levels[0].QuerySelectorAll("[data-testid=calendar-holiday-dates] li").Select(Text).Should().Equal(["2026-12-25 Friday"],
            "each level is drawn with its own rules");
        levels[1].QuerySelector("[data-testid=calendar-base-only]").Should().NotBeNull();
        levels[1].TextContent.Should().Contain("Zone only");
        Text(page.Find("[data-testid=calendar-base-chain]")).Should().Contain("a copy stored inside this one",
            "replacing a calendar stored under another name does not change the copy here, and the page says so");
    }

    [Test]
    public void ACustomCalendarIsNamedAndNotRead()
    {
        OfficeClosuresCalendar custom = new() { Description = "Office closures" };

        IRenderedComponent<CalendarDetail> page = Render(custom);

        page.WaitForAssertion(() => page.Find("[data-testid=calendar-custom]").TextContent.Should().Contain(typeof(OfficeClosuresCalendar).FullName!));
        page.Find("[data-testid=calendar-kind]").TextContent.Should().Contain("Custom calendar");
        page.Find("[data-testid=calendar-time-zone]").TextContent.Should().Contain("Not reported");
        page.Markup.Should().Contain("Office closures");
        page.Markup.Should().NotContain("do-not-print", "the page no longer reflects over a calendar's properties");
    }

    private IRenderedComponent<CalendarDetail> Render(ICalendar calendar)
    {
        A.CallTo(() => context.Api.GetCalendar(TestData.SchedulerName, Name, A<CancellationToken>._)).Returns(calendar);
        return context.Render<CalendarDetail>(parameters => parameters.Add(x => x.CalendarName, Name));
    }

    /// <summary>An element's text with its whitespace runs folded, as a reader sees it.</summary>
    private static string Text(IElement element) => string.Join(' ', element.TextContent.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>A calendar of an application's own, with a property the page must not print.</summary>
    private sealed class OfficeClosuresCalendar : ICalendar
    {
        public string? Description { get; set; }

        public ICalendar? CalendarBase { get; set; }

        public string Secret { get; } = "do-not-print";

        public bool IsTimeIncluded(DateTimeOffset timeUtc) => true;

        public DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc) => timeUtc;

        public ICalendar Clone() => new OfficeClosuresCalendar { Description = Description, CalendarBase = CalendarBase?.Clone() };
    }
}
