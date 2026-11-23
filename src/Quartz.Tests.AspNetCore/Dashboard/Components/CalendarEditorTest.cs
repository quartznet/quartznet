using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Microsoft.AspNetCore.Components;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Impl.Calendar;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// Every built-in calendar type can be created from the Calendars page and edited on Calendar Detail (#4413),
/// and replacing a stored calendar asks whether the triggers that use it are updated.
/// </summary>
/// <remarks>
/// Before #4413 the Calendars page created a cron calendar and nothing else, in UTC, and no calendar could be
/// edited. The context's clock reads 14:23 UTC on Monday 5 October 2026.
/// </remarks>
public sealed class CalendarEditorTest
{
    private DashboardComponentContext context = null!;
    private readonly List<AddCalendarRequest> sent = [];

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
        context.WithScheduler();
        sent.Clear();
        A.CallTo(() => context.Api.AddCalendar(TestData.SchedulerName, A<AddCalendarRequest>._, A<CancellationToken>._))
            .Invokes((string _, AddCalendarRequest request, CancellationToken _) => sent.Add(request))
            .Returns(ValueTask.CompletedTask);
        GivenCalendarNames("existing");
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    [Test]
    public void AHolidayCalendarIsCreatedWithItsDatesInTheZoneNamed()
    {
        TimeZoneInfo kathmandu = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kathmandu");
        IRenderedComponent<Calendars> page = RenderListing();

        Fill(page, "office-holidays", "Holiday");
        page.Find("#calendar-time-zone").Change(kathmandu.Id);
        page.Find("#calendar-description").Change("Office holidays");
        page.Find("#calendar-holiday-dates").Change("2026-12-25\n2026-12-24");
        Save(page);

        AddCalendarRequest request = SentOnce(page);
        request.CalendarName.Should().Be("office-holidays");
        request.Replace.Should().BeFalse("a name the scheduler does not hold is a create, which may not replace anything");
        request.UpdateTriggers.Should().BeFalse();
        HolidayCalendar calendar = request.Calendar.Should().BeOfType<HolidayCalendar>().Subject;
        calendar.DaysExcluded.Should().Equal([new DateOnly(2026, 12, 24), new DateOnly(2026, 12, 25)]);
        calendar.TimeZone.Should().Be(kathmandu);
        calendar.Description.Should().Be("Office holidays");
    }

    [Test]
    public void AnAnnualCalendarIsCreatedFromTheDaysTicked()
    {
        IRenderedComponent<Calendars> page = RenderListing();

        Fill(page, "christmas", "Annual");
        page.Find("[data-testid=calendar-annual-grid] input[data-month-day='--12-25']").Change(true);
        page.Find("[data-testid=calendar-annual-grid] input[data-month-day='--02-29']").Change(true);
        Save(page);

        AnnualCalendar calendar = SentOnce(page).Calendar.Should().BeOfType<AnnualCalendar>().Subject;
        calendar.DaysExcluded.Should().BeEquivalentTo([new MonthDay(2, 29), new MonthDay(12, 25)]);
        calendar.TimeZone.Should().Be(TimeZoneInfo.Utc, "a new calendar is in UTC until the form says otherwise");
    }

    [Test]
    public void AWeeklyCalendarIsCreatedFromTheDaysTicked()
    {
        IRenderedComponent<Calendars> page = RenderListing();

        Fill(page, "no-fridays", "Weekly");
        page.Find("[data-testid=calendar-editor-weekly-days] input[data-day=Saturday]").Change(false);
        page.Find("[data-testid=calendar-editor-weekly-days] input[data-day=Sunday]").Change(false);
        page.Find("[data-testid=calendar-editor-weekly-days] input[data-day=Friday]").Change(true);
        Save(page);

        WeeklyCalendar calendar = SentOnce(page).Calendar.Should().BeOfType<WeeklyCalendar>().Subject;
        calendar.DaysExcluded.Should().BeEquivalentTo([DayOfWeek.Friday],
            "the weekend the form starts with was unticked, and a weekly calendar excludes it unless told not to");
    }

    [Test]
    public void AMonthlyCalendarIsCreatedFromTheDaysTicked()
    {
        IRenderedComponent<Calendars> page = RenderListing();

        Fill(page, "month-end", "Monthly");
        page.Find("[data-testid=calendar-editor-monthly-days] input[data-day='31']").Change(true);
        page.Find("[data-testid=calendar-editor-monthly-days] input[data-day='1']").Change(true);
        Save(page);

        MonthlyCalendar calendar = SentOnce(page).Calendar.Should().BeOfType<MonthlyCalendar>().Subject;
        calendar.DaysExcluded.Should().BeEquivalentTo([1, 31]);
    }

    [Test]
    public void ADailyCalendarIsCreatedFromItsRangeAndInversion()
    {
        IRenderedComponent<Calendars> page = RenderListing();

        Fill(page, "business-hours", "Daily");
        page.Find("#calendar-daily-start").Change("09:00");
        page.Find("#calendar-daily-end").Change("17:30:00");
        page.Find("#calendar-daily-invert").Change(true);
        Save(page);

        DailyCalendar calendar = SentOnce(page).Calendar.Should().BeOfType<DailyCalendar>().Subject;
        calendar.TimeRange.Should().Be(new TimeRange(new TimeOnly(9, 0), new TimeOnly(17, 30)));
        calendar.InvertTimeRange.Should().BeTrue();
    }

    /// <summary>
    /// 14:23 UTC is 20:08 in Kathmandu, so an expression excluding 02:00 to 04:00 there excludes next from
    /// 02:00 the next morning, which the editor previews as the expression is typed.
    /// </summary>
    [Test]
    public void ACronCalendarIsCreatedAndPreviewedAsItIsTyped()
    {
        TimeZoneInfo kathmandu = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kathmandu");
        IRenderedComponent<Calendars> page = RenderListing();

        Fill(page, "night", "Cron");
        page.Find("#calendar-time-zone").Change(kathmandu.Id);
        page.Find("#calendar-cron").Input("* * 2-3 ? * *");

        page.WaitForAssertion(() => Text(page.Find("[data-testid=calendar-cron-spans] li")).Should().Be(
            "2026-10-06 02:00:00 +05:45 until 2026-10-06 04:00:00 +05:45"));
        Save(page);

        CronCalendar calendar = SentOnce(page).Calendar.Should().BeOfType<CronCalendar>().Subject;
        calendar.CronExpression.CronExpressionString.Should().Be("* * 2-3 ? * *");
        calendar.TimeZone.Should().Be(kathmandu);
    }

    [Test]
    public void AMistakeIsShownBesideTheFormAndNothingIsSent()
    {
        IRenderedComponent<Calendars> page = RenderListing();

        Fill(page, "", "Daily");
        page.Find("#calendar-daily-start").Change("17:00");
        page.Find("#calendar-daily-end").Change("09:00");
        Save(page);

        page.WaitForAssertion(() => page.FindAll("[data-testid=calendar-editor-errors] li").Select(item => item.TextContent).Should().Equal(
            "A calendar needs a name: triggers refer to it by that name.",
            "The range must start before it ends, and may not cross midnight."));
        sent.Should().BeEmpty();
    }

    [Test]
    public void SavingUnderANameTheSchedulerHoldsAsksWhetherToUpdateTheTriggersThatUseIt()
    {
        GivenUsers("existing", 3);
        IRenderedComponent<Calendars> page = RenderListing();

        Fill(page, "existing", "Weekly");
        Save(page);

        page.WaitForAssertion(() => page.Find("[data-testid=calendar-replace-used-by]").TextContent.Should().Be("3 triggers use it."));
        sent.Should().BeEmpty("a replace waits for the answer");
        A.CallTo(() => context.Api.QueryTriggers(TestData.SchedulerName, A<DashboardTriggerQuery>.That.Matches(query => query.CalendarName == "existing" && query.Take == 0), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        page.Find("[data-testid=calendar-replace-update]").Click();

        AddCalendarRequest request = SentOnce(page);
        request.Replace.Should().BeTrue();
        request.UpdateTriggers.Should().BeTrue("the reader asked for the triggers that use it to be recomputed");
        page.FindAll("[data-testid=calendar-replace-dialog]").Should().BeEmpty();
        DashboardActionLogEntry entry = context.ActionLog.GetLatest(1).Should().ContainSingle().Which;
        entry.Action.Should().Be("ReplaceCalendar");
        entry.Target.Should().Be("existing");
        entry.Succeeded.Should().BeTrue();
        entry.Message.Should().Be("Weekly calendar; updateTriggers=true");
    }

    [Test]
    public void AReplaceCanBeCancelled()
    {
        GivenUsers("existing", 0);
        IRenderedComponent<Calendars> page = RenderListing();

        Fill(page, "existing", "Monthly");
        Save(page);

        page.WaitForAssertion(() => page.Find("[data-testid=calendar-replace-used-by]").TextContent.Should().Be("No trigger uses it."));
        page.FindAll("[data-testid=calendar-replace-update]").Should().BeEmpty("with no trigger using it there is nothing to update");
        page.Find("[data-testid=calendar-replace-cancel]").Click();

        page.WaitForAssertion(() => page.FindAll("[data-testid=calendar-replace-dialog]").Should().BeEmpty());
        sent.Should().BeEmpty();
    }

    [Test]
    public void ACalendarIsEditedInPlaceKeepingItsZoneAndTheCalendarsUnderIt()
    {
        TimeZoneInfo kathmandu = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kathmandu");
        WeeklyCalendar under = new() { TimeZone = TimeZoneInfo.Utc };
        HolidayCalendar holidays = new(under) { TimeZone = kathmandu, Description = "Office holidays" };
        holidays.AddExcludedDay(new DateOnly(2026, 12, 25));
        A.CallTo(() => context.Api.GetCalendar(TestData.SchedulerName, "office", A<CancellationToken>._)).Returns(holidays);
        GivenUsers("office", 2);

        IRenderedComponent<CalendarDetail> page = context.Render<CalendarDetail>(parameters => parameters.Add(x => x.CalendarName, "office"));
        page.WaitForAssertion(() => page.Find("[data-testid=calendar-edit]").Click());

        page.WaitForAssertion(() => page.Find("#calendar-holiday-dates").GetAttribute("value").Should().Be("2026-12-25"));
        page.Find("#calendar-time-zone").GetAttribute("value").Should().Be(kathmandu.Id, "the editor starts in the zone the calendar reports");
        page.FindAll("#calendar-name").Should().BeEmpty("an edit keeps the name it is stored under");
        page.FindAll("#calendar-kind").Should().BeEmpty("an edit keeps the type");
        page.Find("[data-testid=calendar-editor-base-kept]").TextContent.Should().Contain("1 calendar under it");

        page.Find("#calendar-holiday-dates").Change("2026-12-25\n2026-12-26");
        Save(page);

        page.WaitForAssertion(() => page.Find("[data-testid=calendar-replace-used-by]").TextContent.Should().Be("2 triggers use it.",
            "the count is the one the page read with the calendar"));
        Fake.ClearRecordedCalls(context.Api);
        page.Find("[data-testid=calendar-replace-only]").Click();

        AddCalendarRequest request = SentOnce(page);
        request.CalendarName.Should().Be("office");
        request.Replace.Should().BeTrue();
        request.UpdateTriggers.Should().BeFalse("the reader chose to replace only");
        HolidayCalendar replaced = request.Calendar.Should().BeOfType<HolidayCalendar>().Subject;
        replaced.DaysExcluded.Should().Equal([new DateOnly(2026, 12, 25), new DateOnly(2026, 12, 26)]);
        replaced.TimeZone.Should().Be(kathmandu);
        replaced.Description.Should().Be("Office holidays");
        replaced.CalendarBase.Should().Be(under);

        page.WaitForAssertion(() => page.FindAll("[data-testid=calendar-editor]").Should().BeEmpty("the editor closes once the save is applied"));
        A.CallTo(() => context.Api.GetCalendar(TestData.SchedulerName, "office", A<CancellationToken>._))
            .MustHaveHappenedOnceExactly(); // the page reads what is stored now
    }

    [Test]
    public void CalendarDetailSaysHowManyTriggersUseTheCalendarAndLinksToThem()
    {
        A.CallTo(() => context.Api.GetCalendar(TestData.SchedulerName, "office days", A<CancellationToken>._))
            .Returns(new WeeklyCalendar { TimeZone = TimeZoneInfo.Utc });
        GivenUsers("office days", 4);

        IRenderedComponent<CalendarDetail> page = context.Render<CalendarDetail>(parameters => parameters.Add(x => x.CalendarName, "office days"));

        page.WaitForAssertion(() => page.Find("[data-testid=calendar-used-by] a").TextContent.Should().Be("Used by 4 triggers"));
        page.Find("[data-testid=calendar-used-by] a").GetAttribute("href").Should().Be("quartz/triggers?calendar=office%20days",
            "the Triggers listing filters by calendar, so the count is a link to the triggers it counted");
        A.CallTo(() => context.Api.QueryTriggers(TestData.SchedulerName, A<DashboardTriggerQuery>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly(); // a page view counts once, with no trigger read
        A.CallTo(() => context.Api.QueryTriggers(TestData.SchedulerName, A<DashboardTriggerQuery>.That.Matches(query => query.CalendarName == "office days" && query.Take == 0), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        page.Find("[data-testid=refresh]").Click();

        page.WaitForAssertion(() => A.CallTo(() => context.Api.QueryTriggers(TestData.SchedulerName, A<DashboardTriggerQuery>._, A<CancellationToken>._))
            .MustHaveHappenedTwiceExactly());
        A.CallTo(() => context.Api.GetCalendar(TestData.SchedulerName, "office days", A<CancellationToken>._))
            .MustHaveHappenedTwiceExactly(); // Refresh reads the calendar and counts its triggers again, once each
    }

    [Test]
    public void ACountThatCannotBeReadLeavesTheLinkAndTheCalendar()
    {
        A.CallTo(() => context.Api.GetCalendar(TestData.SchedulerName, "office", A<CancellationToken>._))
            .Returns(new WeeklyCalendar { TimeZone = TimeZoneInfo.Utc });
        A.CallTo(() => context.Api.QueryTriggers(TestData.SchedulerName, A<DashboardTriggerQuery>._, A<CancellationToken>._))
            .ThrowsAsync(new InvalidOperationException("the listing is down"));

        IRenderedComponent<CalendarDetail> page = context.Render<CalendarDetail>(parameters => parameters.Add(x => x.CalendarName, "office"));

        page.WaitForAssertion(() => page.Find("[data-testid=calendar-used-by]").TextContent.Should().Contain("the listing is down"));
        page.Find("[data-testid=calendar-weekly-summary]").TextContent.Should().Be("Excludes Saturday and Sunday, every week.",
            "the calendar is what the page is for, and a count it could not read does not take it away");
    }

    [Test]
    public void ACustomCalendarIsNotOfferedForEditing()
    {
        A.CallTo(() => context.Api.GetCalendar(TestData.SchedulerName, "zone", A<CancellationToken>._))
            .Returns(new BaseCalendar(TimeZoneInfo.Utc));
        GivenUsers("zone", 0);

        IRenderedComponent<CalendarDetail> page = context.Render<CalendarDetail>(parameters => parameters.Add(x => x.CalendarName, "zone"));

        page.WaitForAssertion(() => page.Find("[data-testid=calendar-not-editable]").TextContent.Should().Contain("edited in code"));
        page.HasButton("Edit calendar").Should().BeFalse();
        page.HasButton("Delete calendar").Should().BeTrue("deleting a calendar does not need its type understood");
    }

    private IRenderedComponent<Calendars> RenderListing()
    {
        IRenderedComponent<Calendars> page = context.Render<Calendars>();
        page.WaitForAssertion(() => page.Find("[data-testid=calendar-editor]"));
        return page;
    }

    private static void Fill(IRenderedComponent<Calendars> page, string name, string kind)
    {
        page.Find("#calendar-name").Change(name);
        page.Find("#calendar-kind").Change(kind);
    }

    private static void Save<T>(IRenderedComponent<T> page) where T : IComponent => page.Find("[data-testid=calendar-save]").Click();

    private AddCalendarRequest SentOnce<T>(IRenderedComponent<T> page) where T : IComponent
    {
        page.WaitForAssertion(() => sent.Should().ContainSingle());
        return sent[0];
    }

    private void GivenCalendarNames(params string[] names)
    {
        A.CallTo(() => context.Api.GetCalendarNames(A<string>._, A<CancellationToken>._)).Returns(names.ToList());
    }

    private void GivenUsers(string calendarName, int count)
    {
        A.CallTo(() => context.Api.QueryTriggers(TestData.SchedulerName, A<DashboardTriggerQuery>.That.Matches(query => query.CalendarName == calendarName), A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeaderDto>([], HasMore: false, TotalCount: count));
    }

    private static string Text(IElement element) => string.Join(' ', element.TextContent.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries));
}
