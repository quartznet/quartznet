using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Impl.Calendar;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The Trigger Detail page's <em>Upcoming cron fires</em>: the next five fires of the expression in the
/// editor, as the trigger would have them once rescheduled to it, shown only once that expression is not the
/// saved one.
/// </summary>
/// <remarks>
/// The context's clock reads 14:23 UTC on Monday 5 October 2026, and the dashboard shows times in UTC, so
/// every time written here is a UTC instant. Each trigger starts in August so that its start, which a
/// trigger built without one takes from the machine's clock, is behind the context's. Each trigger is saved
/// at 11:00 and edited to the expression whose fires the test reads.
/// </remarks>
public sealed class TriggerCronPreviewTest
{
    private const string TriggerGroup = "reports";
    private const string TriggerName = "nightly";

    /// <summary>The expression every trigger here is saved with; each test edits it to another.</summary>
    private const string Saved = "0 0 11 * * ?";

    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
        context.WithScheduler();
        A.CallTo(() => context.Api.GetTriggerState(TestData.SchedulerName, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(TriggerState.Normal);
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    /// <summary>
    /// Kathmandu is UTC+05:45 all year, which is neither a CI host's zone nor any zone whose offset is a
    /// whole hour, so the dashboard host's zone cannot produce these times by accident.
    /// </summary>
    [Test]
    public void ATriggerInAnotherZonePreviewsItsFiresInItsOwnZone()
    {
        TimeZoneInfo kathmandu = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kathmandu");
        GivenTrigger(Cron(Saved, kathmandu));

        IRenderedComponent<TriggerDetail> page = Render();
        Edit(page, "0 0 9 * * ?");

        page.WaitForAssertion(() => FireTimes(page).Should().Equal(
            [
                "2026-10-06 03:15:00 +00:00",
                "2026-10-07 03:15:00 +00:00",
                "2026-10-08 03:15:00 +00:00",
                "2026-10-09 03:15:00 +00:00",
                "2026-10-10 03:15:00 +00:00",
            ],
            "09:00 in Kathmandu is 03:15 UTC, and the trigger fires at 09:00 in its own zone, not the dashboard host's"));
        page.Find(".qz-cron-next-fires li .qz-muted").TextContent.Should().Be("(in 12h)",
            "an upcoming fire is ahead of now, so it is said as how long until it rather than how long ago");
        page.Find("[data-testid=cron-next-fires-basis]").TextContent.Should().Be(
            $"Read in {kathmandu.Id}, the trigger's time zone.",
            "the expression's hours are the trigger's zone's, which the times in the list are not");
    }

    [Test]
    public void ACalendarExcludingTheNextFireSkipsItInThePreview()
    {
        HolidayCalendar holidays = new() { TimeZone = TimeZoneInfo.Utc };
        holidays.AddExcludedDay(new DateOnly(2026, 10, 6));
        A.CallTo(() => context.Api.GetCalendar(TestData.SchedulerName, "holidays", A<CancellationToken>._)).Returns(holidays);
        GivenTrigger(Cron(Saved, TimeZoneInfo.Utc, calendarName: "holidays"));

        IRenderedComponent<TriggerDetail> page = Render();
        Edit(page, "0 0 12 * * ?");

        page.WaitForAssertion(() => FireTimes(page).Should().Equal(
            [
                "2026-10-07 12:00:00 +00:00",
                "2026-10-08 12:00:00 +00:00",
                "2026-10-09 12:00:00 +00:00",
                "2026-10-10 12:00:00 +00:00",
                "2026-10-11 12:00:00 +00:00",
            ],
            "the trigger's calendar excludes 6 October, so the scheduler will not fire it then"));
        page.Find("[data-testid=cron-next-fires-basis]").TextContent.Should().Be(
            "Read in UTC, the trigger's time zone, less what calendar 'holidays' excludes.");
    }

    /// <summary>
    /// A calendar the page cannot read leaves the preview as the expression alone, said under the list,
    /// rather than failing a page whose every other part was read.
    /// </summary>
    [Test]
    public void ACalendarThatCannotBeReadIsLeftOutAndTheNoteSaysSo()
    {
        A.CallTo(() => context.Api.GetCalendar(TestData.SchedulerName, "holidays", A<CancellationToken>._))
            .Throws(new KeyNotFoundException("Calendar 'holidays' was not found in scheduler 'TestScheduler'."));
        GivenTrigger(Cron(Saved, TimeZoneInfo.Utc, calendarName: "holidays"));

        IRenderedComponent<TriggerDetail> page = Render();
        Edit(page, "0 0 12 * * ?");

        page.WaitForAssertion(() => FireTimes(page).Should().HaveCount(5)
            .And.StartWith("2026-10-06 12:00:00 +00:00"));
        page.Find("[data-testid=cron-next-fires-calendar]").TextContent.Should().Be(
            "Calendar 'holidays' could not be read, so these times include any it excludes. "
            + "Calendar 'holidays' was not found in scheduler 'TestScheduler'.");
        page.FindAll(".qz-error-alert").Should().BeEmpty("the trigger was read, and the preview without its calendar still says something true");
    }

    /// <summary>
    /// The saved expression's fires are <em>Upcoming fires</em>, so a preview of it would be the same list
    /// twice, under a heading that says something else.
    /// </summary>
    [Test]
    public void TheSavedExpressionIsNotPreviewed()
    {
        GivenTrigger(Cron(Saved, TimeZoneInfo.Utc));

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => page.Find("#trigger-cron-expression").GetAttribute("value").Should().Be(Saved));
        page.Markup.Should().Contain("Upcoming fires", "the saved schedule's fires are listed there");
        page.Markup.Should().NotContain("Upcoming cron fires", "nothing has been edited, so a preview would repeat Upcoming fires");
        page.FindAll(".qz-cron-next-fires").Should().BeEmpty();

        Edit(page, "  " + Saved + " ");

        page.FindAll(".qz-cron-next-fires").Should().BeEmpty(
            "the editor's expression, trimmed, is still the saved one");
    }

    [Test]
    public void ReadOnlyModeHasNoPreview()
    {
        context.Options.ReadOnly = true;
        GivenTrigger(Cron(Saved, TimeZoneInfo.Utc));

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => page.Markup.Should().Contain("Upcoming fires",
            "a read-only dashboard still lists the saved schedule's fires"));
        page.FindAll("#trigger-cron-expression").Should().BeEmpty("a read-only dashboard has no editor");
        page.Markup.Should().NotContain("Upcoming cron fires", "with no editor there is no edited expression to preview");
        page.FindAll(".qz-cron-next-fires").Should().BeEmpty();
    }

    [Test]
    public void EditingTheExpressionChangesThePreviewBeforeItIsSaved()
    {
        GivenTrigger(Cron(Saved, TimeZoneInfo.Utc));
        IRenderedComponent<TriggerDetail> page = Render();
        page.WaitForAssertion(() => page.Find("#trigger-cron-expression").GetAttribute("value").Should().Be(Saved));
        page.FindAll(".qz-cron-next-fires").Should().BeEmpty("nothing has been edited yet");

        Edit(page, "0 30 6 * * ?");

        page.WaitForAssertion(() => FireTimes(page).Should().Equal(
            [
                "2026-10-06 06:30:00 +00:00",
                "2026-10-07 06:30:00 +00:00",
                "2026-10-08 06:30:00 +00:00",
                "2026-10-09 06:30:00 +00:00",
                "2026-10-10 06:30:00 +00:00",
            ],
            "the preview is there to show what the edited expression means before anybody saves it"));
        page.Find("[data-testid=cron-preview-unsaved]").TextContent.Should().Be("Of the edited expression, which is not saved yet.");
        A.CallTo(() => context.Api.RescheduleJob(A<string>._, A<TriggerKeyDto>._, A<RescheduleRequest>._, A<CancellationToken>._))
            .MustNotHaveHappened();

        page.FindAll("button").Single(button => button.TextContent.Trim() == "Reset").Click();

        page.WaitForAssertion(() => page.FindAll(".qz-cron-next-fires").Should().BeEmpty(
            "resetting the editor puts the saved expression back, whose fires are Upcoming fires"));
        page.FindAll("[data-testid=cron-preview-unsaved]").Should().BeEmpty();
    }

    [Test]
    public void AnEditedExpressionThatDoesNotParseShowsTheErrorInPlaceOfTheList()
    {
        GivenTrigger(Cron(Saved, TimeZoneInfo.Utc));
        IRenderedComponent<TriggerDetail> page = Render();

        Edit(page, "0 0 25 * * ?");

        page.WaitForAssertion(() =>
        {
            page.Find(".qz-cron-next-fires .qz-error-alert").TextContent.Should().StartWith("Invalid cron expression: ",
                "an expression that does not parse has no fire times, and the reader needs to know why");
            FireTimes(page).Should().BeEmpty("the saved expression's fires would read as the edited one's");
        });
    }

    /// <summary>
    /// The save parses the expression too, and an exception out of a click handler ends the circuit.
    /// </summary>
    [Test]
    public void SavingAnExpressionThatDoesNotParseSaysWhyAndPostsNothing()
    {
        GivenTrigger(Cron(Saved, TimeZoneInfo.Utc));
        IRenderedComponent<TriggerDetail> page = Render();

        Edit(page, "0 0 25 * * ?");
        page.Find("button.qz-button-primary").Click();

        page.WaitForAssertion(() => context.Toasts.Messages.Should().ContainSingle()
            .Which.Message.Should().StartWith("Invalid cron expression: ",
                "the reader is told why nothing was saved, as the preview already told them why it shows nothing"));
        A.CallTo(() => context.Api.RescheduleJob(A<string>._, A<TriggerKeyDto>._, A<RescheduleRequest>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public void ATriggerThatHasEndedHasNoUpcomingFires()
    {
        GivenTrigger(Cron(Saved, TimeZoneInfo.Utc, endAt: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)));

        IRenderedComponent<TriggerDetail> page = Render();
        Edit(page, "0 0 12 * * ?");

        page.WaitForAssertion(() => page.Find(".qz-cron-next-fires").TextContent.Should().Contain("No upcoming fire times.",
            "the trigger's end time is part of its schedule, and it has passed"));
        FireTimes(page).Should().BeEmpty();
    }

    [Test]
    public void ATriggerThatStartsLaterPreviewsFromItsStart()
    {
        GivenTrigger(Cron(Saved, TimeZoneInfo.Utc, startAt: new DateTimeOffset(2026, 12, 24, 0, 0, 0, TimeSpan.Zero)));

        IRenderedComponent<TriggerDetail> page = Render();
        Edit(page, "0 0 12 * * ?");

        page.WaitForAssertion(() => FireTimes(page).Should().StartWith("2026-12-24 12:00:00 +00:00",
            "a trigger does not fire before its start time"));
    }

    /// <summary>
    /// A cron trigger of a type the page cannot clone has a schedule only its own type knows how to
    /// advance, so the page says it cannot preview it rather than guess with a stand-in.
    /// </summary>
    [Test]
    public void ATriggerThePageCannotCloneIsNotPreviewed()
    {
        ICronTrigger foreignCronTrigger = A.Fake<ICronTrigger>();
        A.CallTo(() => foreignCronTrigger.CronExpressionString).Returns(Saved);
        A.CallTo(() => foreignCronTrigger.Key).Returns(new TriggerKey(TriggerName, TriggerGroup));
        GivenTrigger(foreignCronTrigger);

        IRenderedComponent<TriggerDetail> page = Render();
        Edit(page, "0 0 12 * * ?");

        page.WaitForAssertion(() => page.Find(".qz-cron-next-fires").TextContent.Trim().Should()
            .Be("The dashboard cannot preview this trigger's fire times."));
    }

    private static ITrigger Cron(
        string cronExpression,
        TimeZoneInfo timeZone,
        string? calendarName = null,
        DateTimeOffset? startAt = null,
        DateTimeOffset? endAt = null)
    {
        return TriggerBuilder.Create()
            .WithIdentity(TriggerName, TriggerGroup)
            .ForJob("export", "reports")
            .StartAt(startAt ?? new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero))
            .EndAt(endAt)
            .WithCalendarName(calendarName)
            .WithCronSchedule(cronExpression, cron => cron.InTimeZone(timeZone))
            .Build();
    }

    private static List<string> FireTimes(IRenderedComponent<TriggerDetail> page)
    {
        return [.. page.FindAll(".qz-cron-next-fires li > span:first-child").Select(time => time.TextContent)];
    }

    /// <summary>Types <paramref name="expression" /> into the cron editor, once the page has rendered it.</summary>
    private static void Edit(IRenderedComponent<TriggerDetail> page, string expression)
    {
        page.WaitForElement("#trigger-cron-expression").Change(expression);
    }

    private void GivenTrigger(ITrigger trigger)
    {
        A.CallTo(() => context.Api.GetTrigger(TestData.SchedulerName, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(trigger);
    }

    private IRenderedComponent<TriggerDetail> Render()
    {
        return context.Render<TriggerDetail>(parameters => parameters
            .Add(x => x.Group, TriggerGroup)
            .Add(x => x.Name, TriggerName));
    }
}
