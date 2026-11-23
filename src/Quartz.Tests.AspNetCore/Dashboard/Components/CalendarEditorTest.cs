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
        AddHoliday(page, "2026-12-25");
        AddHoliday(page, "2026-12-24");
        page.FindAll("[data-testid=calendar-editor-holiday-dates] li .qz-mono").Select(item => item.TextContent).Should().Equal(
            ["2026-12-24", "2026-12-25"], "the list is in date order, whatever order the dates were added in");
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

        page.WaitForAssertion(() => page.Find("#calendar-name-error").TextContent.Should().Be("A calendar needs a name: triggers refer to it by that name."));
        page.Find("#calendar-dailyrange-error").TextContent.Should().Be("The range must start before it ends, and may not cross midnight.");
        page.FindAll("[data-testid=calendar-editor-errors]").Should().BeEmpty("the banner is for what the scheduler refused; a field's mistake is said beside it");
        sent.Should().BeEmpty();
    }

    /// <summary>
    /// A field the save found wrong is marked invalid and names its message, which is announced: a screen reader
    /// hears what is wrong and where, and the field's border says it to the eye.
    /// </summary>
    [Test]
    public void AFieldASaveFoundWrongIsMarkedInvalidAndNamesItsMessage()
    {
        IRenderedComponent<Calendars> page = RenderListing();

        Fill(page, "", "Daily");
        page.Find("#calendar-time-zone").Change("Mars/Olympus_Mons");
        page.Find("#calendar-daily-start").Change("17:00");
        page.Find("#calendar-daily-end").Change("09:00");
        Save(page);

        page.WaitForAssertion(() => page.Find("#calendar-name").GetAttribute("aria-invalid").Should().Be("true"));
        AssertDescribedBy(page, "#calendar-name", "calendar-name-error", "A calendar needs a name");
        AssertDescribedBy(page, "#calendar-time-zone", "calendar-timezone-error", "Time zone 'Mars/Olympus_Mons' is not one");
        AssertDescribedBy(page, "#calendar-daily-start", "calendar-dailyrange-error", "The range must start before it ends");
        AssertDescribedBy(page, "#calendar-daily-end", "calendar-dailyrange-error", "The range must start before it ends");
        page.Find("#calendar-description").HasAttribute("aria-invalid").Should().BeFalse("nothing is wrong with the description");

        page.Find("#calendar-kind").Change("Cron");
        page.Find("#calendar-cron").Input("not a cron");
        page.WaitForAssertion(() => AssertDescribedBy(page, "#calendar-cron", "calendar-cron-error", "Not a valid cron expression"));
    }

    /// <summary>
    /// What a save found wrong is about the form as it was: a change of type, or any edit, takes it away, so a
    /// holiday calendar does not say its daily range is missing.
    /// </summary>
    [Test]
    public void ASaveErrorGoesWhenTheTypeChangesOrTheFormIsEdited()
    {
        IRenderedComponent<Calendars> page = RenderListing();

        Fill(page, "business-hours", "Daily");
        Save(page);
        page.WaitForAssertion(() => page.FindAll(".qz-field-error").Select(error => error.TextContent).Should().Equal(
            "A daily calendar needs a range start.", "A daily calendar needs a range end."));

        page.Find("#calendar-kind").Change("Holiday");

        page.WaitForAssertion(() => page.FindAll(".qz-field-error").Should().BeEmpty("the daily range is not part of a holiday calendar"));

        page.Find("#calendar-kind").Change("Daily");
        Save(page);
        page.WaitForAssertion(() => page.FindAll(".qz-field-error").Should().NotBeEmpty());
        page.Find("#calendar-description").Change("Office hours");

        page.WaitForAssertion(() => page.FindAll(".qz-field-error").Should().BeEmpty("the form the save was about has been edited"));
        page.FindAll("[aria-invalid]").Should().BeEmpty();
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

        page.WaitForAssertion(() => page.FindAll("[data-testid=calendar-editor-holiday-dates] li .qz-mono").Select(item => item.TextContent).Should().Equal(["2026-12-25"]));
        page.Find("#calendar-time-zone").GetAttribute("value").Should().Be(kathmandu.Id, "the editor starts in the zone the calendar reports");
        page.FindAll("#calendar-name").Should().BeEmpty("an edit keeps the name it is stored under");
        page.FindAll("#calendar-kind").Should().BeEmpty("an edit keeps the type");
        page.Find("[data-testid=calendar-editor-base-kept]").TextContent.Should().Be("The calendar under it is kept as it is.");

        AddHoliday(page, "2026-12-26");
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

    /// <summary>
    /// A wrong expression is said once, beside it, as it is typed, and goes as soon as the expression parses; it is
    /// not also put in the form's error banner, where a save used to leave it after the expression was fixed.
    /// </summary>
    [Test]
    public void AWrongCronExpressionIsSaidOnceAndGoesWhenItParses()
    {
        IRenderedComponent<Calendars> page = RenderListing();

        Fill(page, "night", "Cron");
        page.Find("#calendar-cron").Input("not a cron");
        Save(page);

        page.WaitForAssertion(() => CountOf(page.Find("[data-testid=calendar-editor]").TextContent, "Not a valid cron expression").Should().Be(1,
            "the message is said beside the expression and nowhere else"));
        page.Find("[data-testid=calendar-cron-error]").TextContent.Should().StartWith("Not a valid cron expression: ");
        page.FindAll("[data-testid=calendar-editor-errors]").Should().BeEmpty();
        sent.Should().BeEmpty();

        page.Find("#calendar-cron").Input("* * 2-3 ? * *");

        page.WaitForAssertion(() => page.Find("[data-testid=calendar-editor]").TextContent.Should().NotContain("Not a valid cron expression",
            "the expression parses now, so nothing is wrong with it"));
        page.FindAll("[data-testid=calendar-cron-spans] li").Should().NotBeEmpty("a valid expression is previewed");
    }

    [Test]
    public void AMissingCronExpressionIsSaidBesideItOnSaveAndGoesWhenOneIsTyped()
    {
        IRenderedComponent<Calendars> page = RenderListing();

        Fill(page, "night", "Cron");
        page.FindAll("[data-testid=calendar-cron-error]").Should().BeEmpty("a blank field is not wrong until it is saved");
        Save(page);

        page.WaitForAssertion(() => page.Find("[data-testid=calendar-cron-error]").TextContent.Should().StartWith("A cron calendar needs an expression"));
        page.Find("#calendar-cron").Input("* * 2");

        page.WaitForAssertion(() => page.Find("[data-testid=calendar-cron-error]").TextContent.Should().StartWith("Not a valid cron expression"));
        page.Find("#calendar-cron").Input("* * 2-3 ? * *");
        page.WaitForAssertion(() => page.FindAll("[data-testid=calendar-cron-error]").Should().BeEmpty());
    }

    [Test]
    public void AMonthlyCalendarWithNoDayTickedIsWarnedAboutBeforeItIsSaved()
    {
        IRenderedComponent<Calendars> page = RenderListing();

        Fill(page, "month-days", "Monthly");

        page.WaitForAssertion(() => page.Find("[data-testid=calendar-editor-excludes-nothing]").TextContent.Should().Be(
            "No day of the month is ticked: saved like this, the calendar excludes nothing."));
        page.Find("[data-testid=calendar-editor-monthly-days] input[data-day='15']").Change(true);

        page.WaitForAssertion(() => page.FindAll("[data-testid=calendar-editor-excludes-nothing]").Should().BeEmpty());
    }

    [Test]
    public void TheDailyRangeIsTwoTimeFields()
    {
        IRenderedComponent<Calendars> page = RenderListing();

        Fill(page, "business-hours", "Daily");

        foreach (string id in new[] { "#calendar-daily-start", "#calendar-daily-end" })
        {
            page.Find(id).GetAttribute("type").Should().Be("time");
            page.Find(id).GetAttribute("step").Should().Be("1", "a range may start or end on a second");
        }
    }

    [Test]
    public void AHolidayIsTakenOffTheListAndSeveralArePastedAtOnce()
    {
        IRenderedComponent<Calendars> page = RenderListing();

        Fill(page, "office-holidays", "Holiday");
        page.Find("#calendar-holiday-paste").Change("2026-12-24\n2026-12-25\nBoxing Day\n2026-12-26");
        page.Find("[data-testid=calendar-holiday-paste-add]").Click();

        page.WaitForAssertion(() => page.FindAll("[data-testid=calendar-editor-holiday-dates] li .qz-mono").Select(item => item.TextContent).Should().Equal(
            ["2026-12-24", "2026-12-25", "2026-12-26"]));
        page.Find("[data-testid=calendar-holiday-rejected]").TextContent.Should().Contain("Boxing Day");
        page.Find("#calendar-holiday-paste").GetAttribute("value").Should().Be("Boxing Day", "the lines that were not added stay in the box to be fixed");

        page.Find("[data-testid=calendar-editor-holiday-dates] button[data-date='2026-12-25']").Click();
        Save(page);

        HolidayCalendar calendar = SentOnce(page).Calendar.Should().BeOfType<HolidayCalendar>().Subject;
        calendar.DaysExcluded.Should().Equal([new DateOnly(2026, 12, 24), new DateOnly(2026, 12, 26)]);
    }

    [Test]
    public void EscapeClosesTheReplaceQuestionAndSendsNothing()
    {
        GivenUsers("existing", 2);
        IRenderedComponent<Calendars> page = RenderListing();

        Fill(page, "existing", "Weekly");
        Save(page);
        page.WaitForElement("[data-testid=calendar-replace-dialog]");

        page.Find("[data-testid=modal-overlay]").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        page.WaitForAssertion(() => page.FindAll("[data-testid=calendar-replace-dialog]").Should().BeEmpty());
        sent.Should().BeEmpty();
    }

    [Test]
    public void ACalendarNoTriggerUsesSaysSoWithoutALink()
    {
        A.CallTo(() => context.Api.GetCalendar(TestData.SchedulerName, "unused", A<CancellationToken>._))
            .Returns(new WeeklyCalendar { TimeZone = TimeZoneInfo.Utc });
        GivenUsers("unused", 0);

        IRenderedComponent<CalendarDetail> page = context.Render<CalendarDetail>(parameters => parameters.Add(x => x.CalendarName, "unused"));

        page.WaitForAssertion(() => page.Find("[data-testid=calendar-used-by]").TextContent.Trim().Should().Be("Not used by any trigger"));
        page.FindAll("[data-testid=calendar-used-by] a").Should().BeEmpty("a listing of no triggers is not worth a link");
    }

    [Test]
    public void WhileEditingTheCalendarIsNotAlsoShownBelowTheEditor()
    {
        A.CallTo(() => context.Api.GetCalendar(TestData.SchedulerName, "office", A<CancellationToken>._))
            .Returns(new WeeklyCalendar { TimeZone = TimeZoneInfo.Utc });
        GivenUsers("office", 0);
        IRenderedComponent<CalendarDetail> page = context.Render<CalendarDetail>(parameters => parameters.Add(x => x.CalendarName, "office"));
        page.WaitForAssertion(() => page.FindAll("[data-testid=calendar-facts]").Should().ContainSingle());

        page.Find("[data-testid=calendar-edit]").Click();

        page.WaitForAssertion(() => page.FindAll("[data-testid=calendar-editor]").Should().ContainSingle());
        page.FindAll("[data-testid=calendar-facts]").Should().BeEmpty("the editor holds the type, the zone and the days");

        page.Find("[data-testid=calendar-cancel]").Click();
        page.WaitForAssertion(() => page.FindAll("[data-testid=calendar-facts]").Should().ContainSingle());
    }

    [Test]
    public void TheListingSaysEachCalendarsType()
    {
        GivenCalendarNames("bank", "weekends", "zone", "broken");
        A.CallTo(() => context.Api.GetCalendar(TestData.SchedulerName, "bank", A<CancellationToken>._)).Returns(new HolidayCalendar());
        A.CallTo(() => context.Api.GetCalendar(TestData.SchedulerName, "weekends", A<CancellationToken>._)).Returns(new WeeklyCalendar());
        A.CallTo(() => context.Api.GetCalendar(TestData.SchedulerName, "zone", A<CancellationToken>._)).Returns(new BaseCalendar());
        A.CallTo(() => context.Api.GetCalendar(TestData.SchedulerName, "broken", A<CancellationToken>._))
            .ThrowsAsync(new InvalidOperationException("no serializer for OfficeClosures"));

        IRenderedComponent<Calendars> page = context.Render<Calendars>();

        page.WaitForAssertion(() => page.FindAll("[data-testid=calendar-type]").Select(cell => cell.TextContent).Should().Equal(
            ["Holiday", "Weekly", "Base", "unreadable"]));
        page.FindAll("[data-testid=calendar-type]")[3].GetAttribute("title").Should().Be("no serializer for OfficeClosures",
            "a calendar that cannot be read says why, and the others are still read");
        page.FindAll("thead th").Select(cell => cell.TextContent.Trim()).Take(2).Should().Equal("Name", "Type");
    }

    /// <summary>
    /// The listing comes first: the form to create a calendar opens on asking, closes once it has saved, and the new
    /// calendar's row is picked out and scrolled to, so a phone does not scroll three screens to find either.
    /// </summary>
    [Test]
    public void TheListingComesFirstAndTheFormOpensOnAskingAndClosesAfterASave()
    {
        IRenderedComponent<Calendars> page = context.Render<Calendars>();
        page.WaitForAssertion(() => page.Find("[data-testid=calendar-new]").TextContent.Trim().Should().Be("New calendar"));
        page.FindAll("[data-testid=calendar-editor]").Should().BeEmpty("the form is closed until asked for");

        page.Find("[data-testid=calendar-new]").Click();
        Fill(page, "office-holidays", "Holiday");
        AddHoliday(page, "2026-12-25");
        GivenCalendarNames("existing", "office-holidays");
        Save(page);

        page.WaitForAssertion(() => page.FindAll("[data-testid=calendar-editor]").Should().BeEmpty("the form closes once it has saved"));
        IElement row = page.FindAll("[data-testid=calendar-row]").Single(candidate => candidate.TextContent.Contains("office-holidays", StringComparison.Ordinal));
        row.ClassList.Should().Contain("qz-row-new");
        page.WaitForAssertion(() => context.JSInterop.Invocations.Should().Contain(invocation =>
            invocation.Identifier == "quartzDashboardDom.revealById" && Equals(invocation.Arguments[0], row.Id)));
        context.Toasts.Messages.Should().Contain(toast => toast.Message == "Created calendar office-holidays.");
        page.Find("[data-testid=calendar-new]");
    }

    [Test]
    public void TheFormCanBeClosedWithoutSaving()
    {
        IRenderedComponent<Calendars> page = RenderListing();

        page.Find("[data-testid=calendar-cancel]").Click();

        page.WaitForAssertion(() => page.FindAll("[data-testid=calendar-editor]").Should().BeEmpty());
        sent.Should().BeEmpty();
    }

    [Test]
    public void TheDeleteQuestionsButtonSaysWhatItDoes()
    {
        IRenderedComponent<Calendars> page = context.Render<Calendars>();
        page.WaitForAssertion(() => page.FindAll("[data-testid=calendar-row] button").First(button => button.TextContent.Trim() == "Delete").Click());

        page.WaitForAssertion(() => page.Find(".qz-confirm-dialog button.qz-button-danger").TextContent.Trim().Should().Be("Delete",
            "the button says the action it takes, not Confirm"));
    }

    /// <summary>
    /// "Every day excluded" is about the days ticked as a whole: the group of day checks is the field that is marked
    /// invalid and names the message, as any other field the save found wrong is.
    /// </summary>
    [TestCase("Weekly", "[data-testid=calendar-editor-weekly-days] input", "A weekly calendar that excludes every day")]
    [TestCase("Monthly", "[data-testid=calendar-editor-monthly-days] input", "A monthly calendar that excludes every day")]
    public void TickingEveryDayMarksTheDayGroupInvalid(string kind, string checks, string message)
    {
        IRenderedComponent<Calendars> page = RenderListing();

        Fill(page, "every-day", kind);
        foreach (AngleSharp.Dom.IElement check in page.FindAll(checks).Where(check => !check.HasAttribute("checked")).ToList())
        {
            page.Find(checks + "[data-day='" + check.GetAttribute("data-day") + "']").Change(true);
        }

        Save(page);

        page.WaitForAssertion(() => page.Find("[data-testid=calendar-editor-days]").GetAttribute("aria-invalid").Should().Be("true"));
        IElement group = page.Find("[data-testid=calendar-editor-days]");
        group.GetAttribute("role").Should().Be("group");
        group.GetAttribute("aria-describedby").Should().Be("calendar-rules-error");
        IElement said = page.Find("#calendar-rules-error");
        said.GetAttribute("role").Should().Be("alert");
        said.TextContent.Should().StartWith(message);
        sent.Should().BeEmpty();
    }

    [Test]
    public void AWeeklyCalendarWithNoDayTickedIsWarnedAboutBeforeItIsSaved()
    {
        IRenderedComponent<Calendars> page = RenderListing();

        Fill(page, "no-days", "Weekly");
        page.FindAll("[data-testid=calendar-editor-excludes-nothing]").Should().BeEmpty("a new weekly calendar starts with the weekend ticked");
        page.Find("[data-testid=calendar-editor-weekly-days] input[data-day=Saturday]").Change(false);
        page.Find("[data-testid=calendar-editor-weekly-days] input[data-day=Sunday]").Change(false);

        page.WaitForAssertion(() => page.Find("[data-testid=calendar-editor-excludes-nothing]").TextContent.Should().Be(
            "No day of the week is ticked: saved like this, the calendar excludes nothing."));
    }

    private IRenderedComponent<Calendars> RenderListing()
    {
        IRenderedComponent<Calendars> page = context.Render<Calendars>();
        page.WaitForAssertion(() => page.Find("[data-testid=calendar-new]").Click());
        page.WaitForAssertion(() => page.Find("[data-testid=calendar-editor]"));
        return page;
    }

    private static void AssertDescribedBy(IRenderedComponent<Calendars> page, string input, string messageId, string message)
    {
        IElement field = page.Find(input);
        field.GetAttribute("aria-invalid").Should().Be("true", input + " is the field the message is about");
        field.GetAttribute("aria-describedby").Should().Be(messageId);
        IElement said = page.Find("#" + messageId);
        said.GetAttribute("role").Should().Be("alert", "the message is announced as it appears");
        said.TextContent.Should().StartWith(message);
    }

    private static void AddHoliday<T>(IRenderedComponent<T> page, string date) where T : IComponent
    {
        page.Find("#calendar-holiday-add").Change(date);
        page.Find("[data-testid=calendar-holiday-add]").Click();
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

    private static int CountOf(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string Text(IElement element) => string.Join(' ', element.TextContent.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries));
}
