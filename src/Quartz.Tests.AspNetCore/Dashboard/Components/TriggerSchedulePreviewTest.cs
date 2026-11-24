using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Impl.Calendar;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The Trigger Detail page's <em>Upcoming fires of the edit</em>: the next five fires of the schedule in the editor,
/// as the trigger would have them once rescheduled to it, shown only once that schedule is not the saved one.
/// </summary>
/// <remarks>
/// The context's clock reads 14:23 UTC on Monday 5 October 2026, and the dashboard shows times in UTC, so
/// every time written here is a UTC instant. Each trigger starts in August so that its start, which a
/// trigger built without one takes from the machine's clock, is behind the context's. Each trigger is saved
/// at 11:00 and edited to the expression whose fires the test reads.
/// </remarks>
public sealed class TriggerSchedulePreviewTest
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
                "2026-10-06 09:00:00 +05:45",
                "2026-10-07 09:00:00 +05:45",
                "2026-10-08 09:00:00 +05:45",
                "2026-10-09 09:00:00 +05:45",
                "2026-10-10 09:00:00 +05:45",
            ],
            "the trigger fires at 09:00 in its own zone, and the list says so in that zone, with its offset, whatever zone the dashboard shows"));
        page.Find("[data-testid=schedule-preview-fires] li .qz-muted").TextContent.Should().Be("(in 12h)",
            "an upcoming fire is ahead of now, so it is said as how long until it rather than how long ago");
        page.Find("[data-testid=schedule-preview-basis]").TextContent.Should().Be(
            $"Times in {kathmandu.Id}, the trigger's time zone.",
            "the list names the zone its times are in");
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
        page.Find("[data-testid=schedule-preview-basis]").TextContent.Should().Be(
            "Times in UTC, the trigger's time zone, less what calendar 'holidays' excludes.");
    }

    /// <summary>
    /// A calendar the page cannot read leaves the preview as the schedule alone, said under the list,
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
        page.Find("[data-testid=schedule-preview-calendar]").TextContent.Should().Be(
            "Calendar 'holidays' could not be read, so these times include any it excludes. "
            + "Calendar 'holidays' was not found in scheduler 'TestScheduler'.");
        page.FindAll(".qz-error-alert").Should().BeEmpty("the trigger was read, and the preview without its calendar still says something true");
    }

    /// <summary>
    /// The editor says what its schedule does from the start, beside its fields, and says whether that is the saved
    /// schedule or an edit: on a narrow screen <em>Upcoming fires</em> is far above it.
    /// </summary>
    [Test]
    public void TheSavedScheduleIsPreviewedAndSaidToBeTheSavedOne()
    {
        GivenTrigger(Cron(Saved, TimeZoneInfo.Utc));

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => page.Find("#trigger-cron-expression").GetAttribute("value").Should().Be(Saved));
        FireTimes(page).Should().StartWith("2026-10-06 11:00:00 +00:00", "the saved schedule's fires are previewed at open");
        page.FindAll("[data-testid=schedule-preview-saved]").Should().ContainSingle();
        page.FindAll("[data-testid=schedule-preview-unsaved]").Should().BeEmpty();

        Edit(page, "  " + Saved + " ");

        page.FindAll("[data-testid=schedule-preview-unsaved]").Should().BeEmpty(
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
        page.FindAll("[data-testid=schedule-preview]").Should().BeEmpty("with no editor there is no edited schedule to preview");
    }

    [Test]
    public void EditingTheExpressionChangesThePreviewBeforeItIsSaved()
    {
        GivenTrigger(Cron(Saved, TimeZoneInfo.Utc));
        IRenderedComponent<TriggerDetail> page = Render();
        page.WaitForAssertion(() => page.Find("#trigger-cron-expression").GetAttribute("value").Should().Be(Saved));
        page.FindAll("[data-testid=schedule-preview-unsaved]").Should().BeEmpty("nothing has been edited yet");

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
        page.Find("[data-testid=schedule-preview-unsaved]").TextContent.Should().Be("Of the edited schedule, which is not saved yet.");
        page.Find("[data-testid=schedule-preview-description]").TextContent.Should().Be("Every day at 06:30 (UTC)",
            "the edit is said in words, as the saved schedule is");
        A.CallTo(() => context.Api.RescheduleJob(A<string>._, A<TriggerKeyDto>._, A<RescheduleRequest>._, A<CancellationToken>._))
            .MustNotHaveHappened();

        page.Find("button.qz-schedule-reset").Click();

        page.WaitForAssertion(() => page.FindAll("[data-testid=schedule-preview-saved]").Should().ContainSingle(
            "resetting the editor puts the saved expression back"));
        page.FindAll("[data-testid=schedule-preview-unsaved]").Should().BeEmpty();
        FireTimes(page).Should().StartWith("2026-10-06 11:00:00 +00:00");
    }

    [Test]
    public void AnEditedExpressionThatDoesNotParseSaysWhyBesideTheFieldAndListsNothing()
    {
        GivenTrigger(Cron(Saved, TimeZoneInfo.Utc));
        IRenderedComponent<TriggerDetail> page = Render();

        Edit(page, "0 0 25 * * ?");

        page.WaitForAssertion(() =>
        {
            page.Find("[data-testid=schedule-cron-error]").TextContent.Should().StartWith("Invalid cron expression: ",
                "an expression that does not parse has no fire times, and the reader needs to know why");
            page.FindAll("[data-testid=schedule-preview-none]").Should().ContainSingle();
            FireTimes(page).Should().BeEmpty("the saved expression's fires would read as the edited one's");
        });
    }

    /// <summary>
    /// The save builds the schedule too, and posts nothing it could not build: the reason is already beside the field.
    /// </summary>
    [Test]
    public void SavingAnExpressionThatDoesNotParsePostsNothing()
    {
        GivenTrigger(Cron(Saved, TimeZoneInfo.Utc));
        IRenderedComponent<TriggerDetail> page = Render();

        Edit(page, "0 0 25 * * ?");
        page.Find("button.qz-schedule-save").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=schedule-cron-error]"));
        page.FindAll("[data-testid=reschedule-dialog]").Should().BeEmpty("nothing valid was built, so there is nothing to confirm");
        A.CallTo(() => context.Api.RescheduleJob(A<string>._, A<TriggerKeyDto>._, A<RescheduleRequest>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public void ATriggerThatHasEndedIsNotRescheduled()
    {
        GivenTrigger(Cron(Saved, TimeZoneInfo.Utc, endAt: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)));

        IRenderedComponent<TriggerDetail> page = Render();
        Edit(page, "0 0 12 * * ?");

        page.WaitForAssertion(() => page.Find("[data-testid=schedule-error]").TextContent.Should().Be(
            "This schedule does not fire again after now.",
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
    /// advance, so the page offers no editor and no preview rather than guess with a stand-in.
    /// </summary>
    [Test]
    public void ATriggerThePageCannotCloneIsNotPreviewed()
    {
        ICronTrigger foreignCronTrigger = A.Fake<ICronTrigger>();
        A.CallTo(() => foreignCronTrigger.CronExpressionString).Returns(Saved);
        A.CallTo(() => foreignCronTrigger.Key).Returns(new TriggerKey(TriggerName, TriggerGroup));
        GivenTrigger(foreignCronTrigger);

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => page.Find("[data-testid=schedule-editor-refused]"));
        page.FindAll("[data-testid=schedule-preview]").Should().BeEmpty();
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
        return [.. page.FindAll("[data-testid=schedule-preview-fires] li > span:first-child").Select(time => time.TextContent)];
    }

    /// <summary>Types <paramref name="expression" /> into the cron editor, once the page has rendered it.</summary>
    private static void Edit(IRenderedComponent<TriggerDetail> page, string expression)
    {
        page.WaitForElement("#trigger-cron-expression").Input(expression);
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
