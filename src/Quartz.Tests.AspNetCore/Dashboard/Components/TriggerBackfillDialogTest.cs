using System.Security.Claims;

using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Impl.Calendar;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The Trigger Detail page's <em>Backfill…</em> dialog: what it reads out of its fields, what it previews,
/// what it shows when the fields or the scheduler refuse, and what it records.
/// </summary>
/// <remarks>
/// The context pins the dashboard's time zone to UTC, so the wall-clock times typed here are UTC instants.
/// </remarks>
public sealed class TriggerBackfillDialogTest
{
    private const string TriggerGroup = "reports";
    private const string TriggerName = "hourly";

    private static readonly TriggerKeyDto TriggerKey = new(TriggerGroup, TriggerName);
    private static readonly DateTimeOffset from = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset until = new(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);

    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
        context.WithScheduler();
        context.AuthenticationState.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "operator@example.com")], authenticationType: "test"));
        A.CallTo(() => context.Api.GetTriggerState(TestData.SchedulerName, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(TriggerState.Normal);
        A.CallTo(() => context.Api.Backfill(A<string>._, A<TriggerKeyDto>._, A<DateTimeOffset>._, A<DateTimeOffset>._, A<BackfillOptions>._, A<CancellationToken>._))
            .Returns(new BackfillResult
            {
                SlotsFound = 24,
                AlreadyScheduled = 4,
                ScheduledTriggers = [.. Enumerable.Range(0, 20).Select(i => new TriggerKey($"hourly@{i}", "backfill:reports"))]
            });
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    [Test]
    public void SubmittingBackfillsTheRangeOnTheDashboardsClockAndRecordsIt()
    {
        GivenTrigger(Hourly());
        IRenderedComponent<TriggerDetail> page = OpenDialog();

        page.Find("#backfill-from").Change("2026-09-01T00:00");
        page.Find("#backfill-to").Change("2026-09-02T00:00:00");
        page.Find("#backfill-spacing").Change("00:01:00");
        page.Find("#backfill-max-slots").Change("50");
        page.Find(".qz-backfill-confirm").Click();

        page.WaitForAssertion(() =>
        {
            A.CallTo(() => context.Api.Backfill(
                    TestData.SchedulerName,
                    TriggerKey,
                    from,
                    until,
                    new BackfillOptions { MaxSlots = 50, Spacing = TimeSpan.FromMinutes(1) },
                    A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();

            context.ActionLog.GetLatest(1).Should().ContainSingle().Which.Should().Match<DashboardActionLogEntry>(entry =>
                entry.Action == "BackfillTrigger"
                && entry.Target == "reports.hourly"
                && entry.Succeeded
                && entry.User == "operator@example.com"
                && entry.Message == "2026-09-01T00:00:00Z to 2026-09-02T00:00:00Z: 24 slots, 20 scheduled, 4 already scheduled",
                "the record says who asked for which range, and what the scheduler made of it");
            context.Toasts.Messages[^1].Message.Should().Be("Backfilled trigger reports.hourly: 20 scheduled, 4 already scheduled.");
            page.FindAll("[data-testid=backfill-dialog]").Should().BeEmpty("a backfill that was made closes the dialog");
        });
    }

    [Test]
    public void ThePreviewCountsTheSlotsTheRangeHoldsLessWhatTheCalendarExcludes()
    {
        HolidayCalendar holidays = new() { TimeZone = TimeZoneInfo.Utc };
        holidays.AddExcludedDay(new DateOnly(2026, 9, 1));
        A.CallTo(() => context.Api.GetCalendar(TestData.SchedulerName, "holidays", A<CancellationToken>._)).Returns(holidays);
        GivenTrigger(Hourly(calendarName: "holidays"));
        IRenderedComponent<TriggerDetail> page = OpenDialog();

        page.Find("#backfill-from").Change("2026-08-31T18:00");
        page.Find("#backfill-to").Change("2026-09-01T06:00");

        page.WaitForAssertion(() => page.Find("[data-testid=backfill-preview]").TextContent.Should().Be("The range holds 6 slots.",
            "twelve hours of an hourly trigger, of which the six on the holiday are not slots"));
        A.CallTo(() => context.Api.Backfill(A<string>._, A<TriggerKeyDto>._, A<DateTimeOffset>._, A<DateTimeOffset>._, A<BackfillOptions>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public void LoweringMaxSlotsBelowTheCountPreviewsTheRefusal()
    {
        GivenTrigger(Hourly());
        IRenderedComponent<TriggerDetail> page = OpenDialog();

        page.Find("#backfill-from").Change("2026-09-01T00:00");
        page.Find("#backfill-to").Change("2026-09-02T00:00");
        page.Find("#backfill-max-slots").Change("10");

        page.WaitForAssertion(() => page.Find("[data-testid=backfill-preview]").TextContent.Should()
            .Be("The range holds 24 slots, more than Max slots (10); the backfill would be refused."));

        page.Find("#backfill-spacing").Change("00:01:00");

        page.WaitForAssertion(() => page.Find("[data-testid=backfill-preview]").TextContent.Should()
            .Be("The range holds 24 slots, more than Max slots (10); the backfill would be refused; with 00:01:00 spacing the last starts 00:23:00 after the first."));

        page.Find("#backfill-max-slots").Change("24");

        page.WaitForAssertion(() => page.Find("[data-testid=backfill-preview]").TextContent.Should()
            .Be("The range holds 24 slots; with 00:01:00 spacing the last starts 00:23:00 after the first.",
                "Max slots allows a range holding exactly that many slots"));
    }

    [Test]
    public void ChangingSpacingPreviewsWhenTheLastSlotStarts()
    {
        GivenTrigger(Hourly());
        IRenderedComponent<TriggerDetail> page = OpenDialog();

        page.Find("#backfill-from").Change("2026-09-01T00:00");
        page.Find("#backfill-to").Change("2026-09-02T00:00");
        page.Find("#backfill-spacing").Change("00:01:00");

        page.WaitForAssertion(() => page.Find("[data-testid=backfill-preview]").TextContent.Should()
            .Be("The range holds 24 slots; with 00:01:00 spacing the last starts 00:23:00 after the first."));

        page.Find("#backfill-spacing").Change("00:00:00");

        page.WaitForAssertion(() => page.Find("[data-testid=backfill-preview]").TextContent.Should()
            .Be("The range holds 24 slots."));
    }

    [TestCase("lots", "soon")]
    [TestCase("0", "-00:01:00")]
    [TestCase("-1", "10675199.02:48:05.4775807")]
    public void InvalidOptionsOrAnOverflowingSpreadLeaveTheSlotCountVisible(string maxSlots, string spacing)
    {
        GivenTrigger(Hourly());
        IRenderedComponent<TriggerDetail> page = OpenDialog();

        page.Find("#backfill-from").Change("2026-09-01T00:00");
        page.Find("#backfill-to").Change("2026-09-02T00:00");
        page.Find("#backfill-max-slots").Change(maxSlots);
        page.Find("#backfill-spacing").Change(spacing);

        page.WaitForAssertion(() => page.Find("[data-testid=backfill-preview]").TextContent.Should()
            .Be("The range holds 24 slots."));
    }

    [TestCase("100000", "The range holds more than 100000 slots, more than Max slots (100000); the backfill would be refused.")]
    [TestCase("100001", "The range holds more than 100000 slots.")]
    public void ACappedCountPreviewsRefusalOnlyWhenItIsKnownToExceedMaxSlots(string maxSlots, string preview)
    {
        GivenTrigger(TriggerBuilder.Create()
            .WithIdentity(TriggerName, TriggerGroup)
            .ForJob("export", "reports")
            .StartAt(from)
            .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromSeconds(1)).RepeatForever())
            .Build());
        IRenderedComponent<TriggerDetail> page = OpenDialog();

        page.Find("#backfill-from").Change("2026-09-01T00:00");
        page.Find("#backfill-to").Change("2026-09-03T00:00");
        page.Find("#backfill-max-slots").Change(maxSlots);
        page.Find("#backfill-spacing").Change("00:01:00");

        page.WaitForAssertion(() => page.Find("[data-testid=backfill-preview]").TextContent.Should().Be(preview,
            "a capped count proves only a lower bound and cannot give the last slot's start"));
    }

    /// <summary>
    /// A calendar the dialog cannot read leaves the count unsaid rather than wrong: the backfill reads the
    /// calendar again where it is, and counts there.
    /// </summary>
    [Test]
    public void APreviewWhoseCalendarCannotBeReadSaysSoInsteadOfCounting()
    {
        A.CallTo(() => context.Api.GetCalendar(TestData.SchedulerName, "holidays", A<CancellationToken>._))
            .Throws(new KeyNotFoundException("Calendar 'holidays' was not found in scheduler 'TestScheduler'."));
        GivenTrigger(Hourly(calendarName: "holidays"));
        IRenderedComponent<TriggerDetail> page = OpenDialog();

        page.Find("#backfill-from").Change("2026-08-31T18:00");
        page.Find("#backfill-to").Change("2026-09-01T06:00");

        page.WaitForAssertion(() => page.Find("[data-testid=backfill-preview]").TextContent.Should()
            .StartWith("The slots cannot be counted here: calendar 'holidays' could not be read."));
    }

    [Test]
    public void AFieldThatCannotBeReadIsRefusedBeforeAnythingIsAsked()
    {
        GivenTrigger(Hourly());
        IRenderedComponent<TriggerDetail> page = OpenDialog();

        page.Find("#backfill-from").Change("yesterday");
        page.Find("#backfill-to").Change("later");
        page.Find("#backfill-spacing").Change("soon");
        page.Find("#backfill-max-slots").Change("lots");
        page.Find(".qz-backfill-confirm").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=backfill-errors]").TextContent.Should()
            .Contain("From: 'yesterday' is not a date and time.")
            .And.Contain("To: 'later' is not a date and time.")
            .And.Contain("Spacing: 'soon' is not a duration; write hh:mm:ss.")
            .And.Contain("Max slots: 'lots' is not a whole number.",
                "every field that cannot be read is named at once, so the reader fixes them in one pass"));
        A.CallTo(() => context.Api.Backfill(A<string>._, A<TriggerKeyDto>._, A<DateTimeOffset>._, A<DateTimeOffset>._, A<BackfillOptions>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public void TheSchedulersRefusalIsShownBesideTheFieldsAndRecorded()
    {
        A.CallTo(() => context.Api.Backfill(A<string>._, A<TriggerKeyDto>._, A<DateTimeOffset>._, A<DateTimeOffset>._, A<BackfillOptions>._, A<CancellationToken>._))
            .Throws(new ArgumentException("The range holds 24 slots of trigger 'reports.hourly', more than BackfillOptions.MaxSlots (10) allows. Nothing was scheduled.", "options"));
        GivenTrigger(Hourly());
        IRenderedComponent<TriggerDetail> page = OpenDialog();

        page.Find("#backfill-from").Change("2026-09-01T00:00");
        page.Find("#backfill-to").Change("2026-09-02T00:00");
        page.Find(".qz-backfill-confirm").Click();

        page.WaitForAssertion(() =>
        {
            page.Find("[data-testid=backfill-errors]").TextContent.Should().Contain("The range holds 24 slots",
                "the reader corrects the range in the dialog it is still looking at");
            page.FindAll("[data-testid=backfill-dialog]").Should().ContainSingle("a refused backfill leaves the dialog open");
            context.ActionLog.GetLatest(1).Should().ContainSingle().Which.Succeeded.Should().BeFalse();
        });
    }

    [Test]
    public void ReadOnlyModeOffersNoBackfill()
    {
        context.Options.ReadOnly = true;
        GivenTrigger(Hourly());

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => page.Markup.Should().Contain("Trigger Detail"));
        page.HasButton("Backfill…").Should().BeFalse("a backfill schedules triggers, which a read-only dashboard does not do");
        page.FindAll("[data-testid=backfill-dialog]").Should().BeEmpty();
    }

    [Test]
    public void ADataSourceThatCannotBackfillDisablesTheButtonWithTheReason()
    {
        A.CallTo(() => context.Api.Backfill(A<string>._, A<TriggerKeyDto>._, A<DateTimeOffset>._, A<DateTimeOffset>._, A<BackfillOptions>._, A<CancellationToken>._))
            .CallsBaseMethod();
        GivenTrigger(Hourly());
        IRenderedComponent<TriggerDetail> page = OpenDialog();

        page.Find("#backfill-from").Change("2026-09-01T00:00");
        page.Find("#backfill-to").Change("2026-09-02T00:00");
        page.Find(".qz-backfill-confirm").Click();

        page.WaitForAssertion(() =>
        {
            page.Find("[data-testid=trigger-backfill-unavailable]").TextContent.Should().Contain("cannot backfill a trigger");
            page.FindAll("button").Single(button => button.TextContent.Trim() == "Backfill…")
                .HasAttribute("disabled").Should().BeTrue("the reason stays true for as long as the page is open");
            page.FindAll("[data-testid=backfill-dialog]").Should().BeEmpty();
            page.FindAll(".qz-error-alert").Should().BeEmpty("an operation a target cannot do is not an error page");
        });
    }

    [Test]
    public void CancellingClosesTheDialogAndAsksNothing()
    {
        GivenTrigger(Hourly());
        IRenderedComponent<TriggerDetail> page = OpenDialog();

        page.Find(".qz-backfill-cancel").Click();

        page.WaitForAssertion(() => page.FindAll("[data-testid=backfill-dialog]").Should().BeEmpty());
        A.CallTo(() => context.Api.Backfill(A<string>._, A<TriggerKeyDto>._, A<DateTimeOffset>._, A<DateTimeOffset>._, A<BackfillOptions>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    private static ITrigger Hourly(string? calendarName = null)
    {
        return TriggerBuilder.Create()
            .WithIdentity(TriggerName, TriggerGroup)
            .ForJob("export", "reports")
            .StartAt(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero))
            .WithCalendarName(calendarName)
            .WithCronSchedule("0 0 * * * ?", cron => cron.InTimeZone(TimeZoneInfo.Utc))
            .Build();
    }

    private void GivenTrigger(ITrigger trigger)
    {
        A.CallTo(() => context.Api.GetTrigger(TestData.SchedulerName, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(trigger);
    }

    private IRenderedComponent<TriggerDetail> OpenDialog()
    {
        IRenderedComponent<TriggerDetail> page = Render();
        page.WaitForAssertion(() => page.HasButton("Backfill…").Should().BeTrue());
        page.FindAll("button").Single(button => button.TextContent.Trim() == "Backfill…").Click();
        page.WaitForElement("[data-testid=backfill-dialog]");
        return page;
    }

    private IRenderedComponent<TriggerDetail> Render()
    {
        return context.Render<TriggerDetail>(parameters => parameters
            .Add(x => x.Group, TriggerGroup)
            .Add(x => x.Name, TriggerName));
    }
}
