using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Microsoft.AspNetCore.Components;

using Quartz.Dashboard.Components.Layout;
using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The Agenda page (#4438): every fire of every trigger in a window, grouped by the hours of the header's zone, with
/// <em>Pause selected</em> and <em>Resume selected</em> over the fires' triggers.
/// </summary>
/// <remarks>
/// The context's clock reads 14:23 UTC on 5 October 2026, and its zone is UTC unless a case names another.
/// </remarks>
public sealed class AgendaPageTest
{
    private static readonly DateTimeOffset now = new(2026, 10, 5, 14, 23, 0, TimeSpan.Zero);
    private static readonly TriggerKeyDto nightly = new("reports", "nightly");
    private static readonly TriggerKeyDto hourly = new("reports", "hourly");

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

    [Test]
    public void ItOpensOnTheNext24HoursAndNamesTheZoneAndItsOffset()
    {
        context.SchedulerState.SelectedTimeZoneId = "Europe/Helsinki";
        GivenFires([]);

        IRenderedComponent<Agenda> page = context.Render<Agenda>();

        page.WaitForAssertion(() => A.CallTo(() => context.Api.QueryUpcomingFires(
                TestData.SchedulerName,
                A<DashboardUpcomingFiresQuery>.That.Matches(query => query.From == now && query.To == now.AddHours(24) && query.GroupContains == null),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());
        page.Find("[data-testid=agenda-zone]").TextContent.Trim().Should().Be("Times in Europe/Helsinki (UTC+03:00)");
        page.Find("[data-preset=next-24-hours]").GetAttribute("aria-pressed").Should().Be("true");
        page.FindAll("[aria-pressed=true]").Should().ContainSingle();
    }

    [Test]
    public void FiresAreGroupedByTheHourTheyFireInWithACount()
    {
        GivenFires([Fire(nightly, now.AddMinutes(7)), Fire(hourly, now.AddMinutes(22)), Fire(nightly, now.AddMinutes(47))]);

        IRenderedComponent<Agenda> page = context.Render<Agenda>();

        page.WaitForAssertion(() => page.TextOfAll("[data-testid=agenda-hour-label]").Should().Equal(["Mon 5 Oct, 14:00", "Mon 5 Oct, 15:00"]));
        page.TextOfAll(".qz-agenda-hour-count").Should().Equal(["2 fires", "1 fire"]);
        page.TextOfAll("td.qz-agenda-col-time").Should().Equal(["14:30:00", "14:45:00", "15:10:00"]);
    }

    /// <summary>
    /// The autumn change in Helsinki repeats 03:00 to 04:00: once at UTC+03:00 and once at UTC+02:00. Each is an hour
    /// of its own, keyed by its start in UTC and labelled with its offset, so neither is merged into the other.
    /// </summary>
    [Test]
    public void ARepeatedAutumnHourIsTwoHoursEachWithItsOffset()
    {
        context.SchedulerState.SelectedTimeZoneId = "Europe/Helsinki";
        DateTimeOffset first = new(2026, 10, 25, 0, 15, 0, TimeSpan.Zero);
        GivenFires([Fire(nightly, first), Fire(nightly, first.AddHours(1)), Fire(nightly, first.AddHours(2))]);
        context.Navigate("/quartz/agenda?from=2026-10-25T00:00:00Z&to=2026-10-25T03:00:00Z");

        IRenderedComponent<Agenda> page = context.Render<Agenda>();

        page.WaitForAssertion(() => page.TextOfAll("[data-testid=agenda-hour-label]").Should().Equal(
            ["Sun 25 Oct, 03:00 (UTC+03:00)", "Sun 25 Oct, 03:00 (UTC+02:00)", "Sun 25 Oct, 04:00 (UTC+02:00)"]));
        page.TextOfAll("td.qz-agenda-col-time").Should().Equal(["03:15:00", "03:15:00", "04:15:00"]);
    }

    [Test]
    public void RowsLinkTheTriggerAndTheJobAndSayTheirStateInWords()
    {
        GivenFires([
            Fire(nightly, now.AddMinutes(10), TriggerState.Paused) with { ScheduleDescription = "Every day at 02:15 (UTC)", ExecutionGroup = "batch" },
            Fire(hourly, now.AddMinutes(20)) with { OnlyTheNext = true },
        ]);

        IRenderedComponent<Agenda> page = context.Render<Agenda>();

        page.WaitForAssertion(() => page.FindAll("[data-testid=agenda-fire]").Should().HaveCount(2));
        IElement paused = page.FindAll("[data-testid=agenda-fire]")[0];
        paused.QuerySelectorAll("td.qz-agenda-col-key > a").Select(link => link.GetAttribute("href")).Should().Equal(
            ["quartz/triggers/reports/nightly", "quartz/jobs/reports/export"]);
        paused.QuerySelector("td.qz-agenda-col-state")!.TextContent.Trim().Should().Be("Paused", "a state is said in words, not by colour alone");
        paused.QuerySelector("td.qz-agenda-col-schedule")!.TextContent.Trim().Should().Be("Every day at 02:15 (UTC)");
        paused.QuerySelector("td.qz-agenda-col-execution-group")!.TextContent.Trim().Should().Be("batch");
        page.FindAll("[data-testid=agenda-fire]")[1].QuerySelector(".qz-agenda-only-next")!.TextContent
            .Should().Contain("Only the next fire is known");
    }

    [Test]
    public void ACutListSaysWhereItWasCutAndUncomputedTriggersAreNamed()
    {
        GivenFires([Fire(nightly, now.AddMinutes(10)), Fire(hourly, now.AddMinutes(47))], cutAt: now.AddMinutes(47), notComputed: 2);

        IRenderedComponent<Agenda> page = context.Render<Agenda>();

        page.WaitForAssertion(() => Normalized(page.Find("[data-testid=agenda-cut]")).Should().Be(
            "Showing the first 2 fires, up to Mon 5 Oct 15:10. Later fires in the window are not listed: narrow the window or filter by group."));
        Normalized(page.Find("[data-testid=agenda-not-computed]")).Should().StartWith("2 triggers due in the window could not be computed");
    }

    [Test]
    public void AWindowWithNothingInItSaysSo()
    {
        GivenFires([]);

        IRenderedComponent<Agenda> page = context.Render<Agenda>();

        page.WaitForAssertion(() => Normalized(page.Find("[data-testid=agenda-empty]")).Should().Be("Nothing fires between Mon 5 Oct 14:23 and Tue 6 Oct 14:23."));
        page.FindAll("table").Should().BeEmpty("an empty window is a sentence, not an empty table");
    }

    /// <summary>
    /// The selection is fires and the action takes their distinct triggers: two fires of one trigger and one of
    /// another pause two triggers, in one call, with the reason asked for once, and the prompt says they stay paused.
    /// </summary>
    [Test]
    public void PauseSelectedPausesTheSelectedFiresTriggersOnceEachAndSaysTheyStayPaused()
    {
        GivenFires([Fire(nightly, now.AddMinutes(10)), Fire(hourly, now.AddMinutes(20)), Fire(nightly, now.AddMinutes(40))]);
        A.CallTo(() => context.Api.PauseTriggersWith(TestData.SchedulerName, A<IReadOnlyCollection<TriggerKeyDto>>._, A<PauseDetails>._, A<CancellationToken>._))
            .ReturnsLazily((string _, IReadOnlyCollection<TriggerKeyDto> keys, PauseDetails? _, CancellationToken _) => keys.ToList());
        IRenderedComponent<Agenda> page = context.Render<Agenda>();
        page.WaitForAssertion(() => page.FindAll("input.qz-agenda-select-fire").Should().HaveCount(3));

        for (int i = 0; i < 3; i++)
        {
            // Found again each time: a change re-renders the rows.
            page.FindAll("input.qz-agenda-select-fire")[i].Change(true);
        }

        page.WaitForAssertion(() => page.Find("[data-testid=agenda-selection-count]").TextContent.Should().Be("2 triggers selected (3 fires)"));
        page.Find(".qz-bulk-pause").Click();
        page.WaitForElement(".qz-pause-dialog");
        page.Find(".qz-pause-dialog-note").TextContent.Should().Be(
            "They stay paused until resumed: nothing resumes them when the window ends. On resume, each trigger's misfire instruction decides what happens to the fires it missed.");
        page.Find(".qz-pause-dialog p").TextContent.Should().Be("Pause 2 triggers (3 fires)?");
        page.ConfirmPause("database maintenance");

        page.WaitForAssertion(() => A.CallTo(() => context.Api.PauseTriggersWith(
                TestData.SchedulerName,
                A<IReadOnlyCollection<TriggerKeyDto>>.That.Matches(keys => keys.SequenceEqual(new[] { hourly, nightly })),
                A<PauseDetails>.That.Matches(details => details.Reason == "database maintenance"),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());
        page.WaitForAssertion(() => context.Toasts.Messages[^1].Message.Should().Be("Paused 2 of 2 selected trigger(s)."));
        context.ActionLog.GetLatest(2).Should().BeEquivalentTo(
            [
                new { Action = "PauseTrigger", Target = "reports.nightly", Succeeded = true, Message = "one of 2 selected; reason: database maintenance" },
                new { Action = "PauseTrigger", Target = "reports.hourly", Succeeded = true, Message = "one of 2 selected; reason: database maintenance" }
            ],
            options => options.ExcludingMissingMembers(),
            "each trigger is recorded under its own key, as the Triggers listing records it");
        // Read again, so the rows show what the pause did.
        page.WaitForAssertion(() => A.CallTo(() => context.Api.QueryUpcomingFires(A<string>._, A<DashboardUpcomingFiresQuery>._, A<CancellationToken>._))
            .MustHaveHappenedTwiceExactly());
        page.FindAll("[data-testid=agenda-bulk-actions]").Should().BeEmpty("the selection is spent once it has been acted on");
    }

    [Test]
    public void SelectThisHourSelectsItsFiresAndResumeSelectedResumesTheirTriggers()
    {
        GivenFires([Fire(nightly, now.AddMinutes(10)), Fire(hourly, now.AddMinutes(20)), Fire(nightly, now.AddMinutes(47))]);
        A.CallTo(() => context.Api.ResumeTriggers(TestData.SchedulerName, A<IReadOnlyCollection<TriggerKeyDto>>._, A<CancellationToken>._))
            .ReturnsLazily((string _, IReadOnlyCollection<TriggerKeyDto> keys, CancellationToken _) => keys.ToList());
        IRenderedComponent<Agenda> page = context.Render<Agenda>();
        page.WaitForAssertion(() => page.FindAll("input.qz-agenda-select-hour").Should().HaveCount(2));

        page.FindAll("input.qz-agenda-select-hour")[0].Change(true);

        page.WaitForAssertion(() => page.Find("[data-testid=agenda-selection-count]").TextContent.Should().Be("2 triggers selected (2 fires)",
            "the first hour holds a fire of each trigger"));
        page.Find(".qz-bulk-resume").Click();

        page.WaitForAssertion(() => A.CallTo(() => context.Api.ResumeTriggers(
                TestData.SchedulerName,
                A<IReadOnlyCollection<TriggerKeyDto>>.That.Matches(keys => keys.Count == 2),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());
    }

    /// <summary>
    /// <em>Clear selection</em> removes the bar it is on, so focus moves to the fires rather than fall to the body.
    /// </summary>
    [Test]
    public void ClearingTheSelectionMovesFocusToTheFires()
    {
        GivenFires([Fire(nightly, now.AddMinutes(10))]);
        IRenderedComponent<Agenda> page = context.Render<Agenda>();
        string results = ReferenceOf(page.WaitForElement("[data-testid=agenda-results]"));
        page.WaitForElement("input.qz-agenda-select-fire").Change(true);

        page.WaitForElement(".qz-bulk-clear").Click();

        page.WaitForAssertion(() => page.FindAll("[data-testid=agenda-bulk-actions]").Should().BeEmpty());
        page.WaitForAssertion(() => LastFocused().Should().Be(results));
    }

    /// <summary>
    /// A pause empties the selection, which removes the bar its button was on: focus moves to the fires, which stay on
    /// screen while they are read again, rather than give way to a spinner.
    /// </summary>
    [Test]
    public void APauseMovesFocusToTheFiresWhichStayOnScreenWhileTheyAreReadAgain()
    {
        TaskCompletionSource<UpcomingFiresDto> reread = new();
        List<UpcomingFireDto> fires = [Fire(nightly, now.AddMinutes(10)), Fire(hourly, now.AddMinutes(20))];
        int reads = 0;
        A.CallTo(() => context.Api.QueryUpcomingFires(A<string>._, A<DashboardUpcomingFiresQuery>._, A<CancellationToken>._))
            .ReturnsLazily(() => ++reads == 1
                ? ValueTask.FromResult(new UpcomingFiresDto(now, now.AddHours(24), fires))
                : new ValueTask<UpcomingFiresDto>(reread.Task));
        A.CallTo(() => context.Api.PauseTriggersWith(TestData.SchedulerName, A<IReadOnlyCollection<TriggerKeyDto>>._, A<PauseDetails>._, A<CancellationToken>._))
            .ReturnsLazily((string _, IReadOnlyCollection<TriggerKeyDto> keys, PauseDetails? _, CancellationToken _) => keys.ToList());
        IRenderedComponent<Agenda> page = context.Render<Agenda>();
        string results = ReferenceOf(page.WaitForElement("[data-testid=agenda-results]"));
        page.WaitForElement("input.qz-agenda-select-hour").Change(true);

        page.WaitForElement(".qz-bulk-pause").Click();
        page.ConfirmPause();

        page.WaitForAssertion(() => LastFocused().Should().Be(results));
        page.FindAll("[data-testid=agenda-fire]").Should().HaveCount(2, "the fires stay on screen while the agenda is read again");
        page.FindAll(".qz-loading-spinner").Should().BeEmpty();

        reread.SetResult(new UpcomingFiresDto(now, now.AddHours(24), [.. fires.Select(fire => fire with { State = TriggerState.Paused })]));
        page.WaitForAssertion(() => page.TextOfAll("td.qz-agenda-col-state").Should().Equal(["Paused", "Paused"]));
    }

    [Test]
    public void DismissingTheResultMovesFocusToTheFires()
    {
        GivenFires([Fire(nightly, now.AddMinutes(10))]);
        A.CallTo(() => context.Api.ResumeTriggers(TestData.SchedulerName, A<IReadOnlyCollection<TriggerKeyDto>>._, A<CancellationToken>._))
            .Returns(new List<TriggerKeyDto>());
        IRenderedComponent<Agenda> page = context.Render<Agenda>();
        string results = ReferenceOf(page.WaitForElement("[data-testid=agenda-results]"));
        page.WaitForElement("input.qz-agenda-select-fire").Change(true);
        page.WaitForElement(".qz-bulk-resume").Click();
        page.WaitForElement("[data-testid=agenda-bulk-result]");
        int focusedSoFar = Focused().Count;

        page.Find(".qz-bulk-result-dismiss").Click();

        page.WaitForAssertion(() => page.FindAll("[data-testid=agenda-bulk-result]").Should().BeEmpty());
        page.WaitForAssertion(() => Focused().Should().HaveCount(focusedSoFar + 1));
        LastFocused().Should().Be(results);
    }

    [Test]
    public void AReadOnlyDashboardOffersNoSelection()
    {
        context.Options.ReadOnly = true;
        GivenFires([Fire(nightly, now.AddMinutes(10))]);

        IRenderedComponent<Agenda> page = context.Render<Agenda>();

        page.WaitForAssertion(() => page.FindAll("[data-testid=agenda-fire]").Should().ContainSingle());
        page.FindAll("input[type=checkbox]").Should().BeEmpty();
    }

    [Test]
    public void AViewerSeesTheAgendaWithoutSelection()
    {
        context.WithOperatorRole(SchedulerAccess.View);
        GivenFires([Fire(nightly, now.AddMinutes(10))]);

        IRenderedComponent<Agenda> page = context.Render<Agenda>();

        page.WaitForAssertion(() => page.FindAll("[data-testid=agenda-fire]").Should().ContainSingle("the agenda is a read, which View allows"));
        page.FindAll("input[type=checkbox]").Should().BeEmpty("pausing and resuming need Operate");
    }

    [Test]
    public void ASourceThatCannotListTheAgendaSaysSo()
    {
        A.CallTo(() => context.Api.QueryUpcomingFires(A<string>._, A<DashboardUpcomingFiresQuery>._, A<CancellationToken>._)).CallsBaseMethod();

        IRenderedComponent<Agenda> page = context.Render<Agenda>();

        page.WaitForAssertion(() => page.Find("[data-testid=agenda-unavailable]").TextContent.Should().Contain("cannot list what fires between two times"));
        page.FindAll("table").Should().BeEmpty();
    }

    [Test]
    public void ARefusalIsShownInItsOwnWords()
    {
        A.CallTo(() => context.Api.QueryUpcomingFires(A<string>._, A<DashboardUpcomingFiresQuery>._, A<CancellationToken>._))
            .Throws(new ArgumentException("More than 10,000 triggers are due before 2026-10-06T14:23:00.0000000+00:00: narrow the window or filter by trigger group.", "query"));

        IRenderedComponent<Agenda> page = context.Render<Agenda>();

        page.WaitForAssertion(() => page.Find("[data-testid=agenda-refusal]").TextContent.Should().Be(
            "More than 10,000 triggers are due before 2026-10-06T14:23:00.0000000+00:00: narrow the window or filter by trigger group.",
            "the scheduler's words, without the parameter the exception names"));
    }

    [Test]
    public void APresetReadsItsWindowFromNow()
    {
        GivenFires([]);
        IRenderedComponent<Agenda> page = context.Render<Agenda>();

        page.Find("[data-preset=next-hour]").Click();
        page.Find("[data-preset=rest-of-today]").Click();
        page.Find("[data-preset=next-7-days]").Click();

        page.WaitForAssertion(() => Queried().Select(query => query.To - query.From!.Value).Should().Equal(
            [TimeSpan.FromHours(24), TimeSpan.FromHours(1), TimeSpan.FromHours(9) + TimeSpan.FromMinutes(37), TimeSpan.FromDays(7)]));
        page.Find("[data-preset=next-7-days]").GetAttribute("aria-pressed").Should().Be("true");
    }

    [Test]
    public void ACustomWindowIsReadInTheHeadersZone()
    {
        context.SchedulerState.SelectedTimeZoneId = "Europe/Helsinki";
        GivenFires([]);
        IRenderedComponent<Agenda> page = context.Render<Agenda>();

        page.Find("#agenda-from").Change("2026-10-06T02:00");
        page.Find("#agenda-to").Change("2026-10-06T04:00");
        page.Find(".qz-agenda-apply").Click();

        page.WaitForAssertion(() => Queried()[^1].Should().Match<DashboardUpcomingFiresQuery>(query =>
            query.From == new DateTimeOffset(2026, 10, 5, 23, 0, 0, TimeSpan.Zero) && query.To == new DateTimeOffset(2026, 10, 6, 1, 0, 0, TimeSpan.Zero)));
        page.FindAll("[aria-pressed=true]").Should().BeEmpty("a window typed in is no preset");
    }

    [TestCase("2026-10-06T04:00", "2026-10-06T02:00", "To must be after From.")]
    [TestCase("2026-10-06T02:00", "2026-10-14T02:00", "The window may be at most 7 days long.")]
    [TestCase("tomorrow", "2026-10-06T02:00", "From and To must each be a date and time.")]
    public void AWindowThePageCanRefuseIsRefusedInlineWithoutARead(string from, string to, string words)
    {
        GivenFires([]);
        IRenderedComponent<Agenda> page = context.Render<Agenda>();
        page.WaitForAssertion(() => Queried().Should().ContainSingle());

        page.Find("#agenda-from").Change(from);
        page.Find("#agenda-to").Change(to);
        page.Find(".qz-agenda-apply").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=agenda-input-error]").TextContent.Should().Be(words));
        Queried().Should().ContainSingle("nothing was asked of the scheduler for a window the page could refuse itself");
    }

    [Test]
    public void TheWindowAndTheGroupRoundTripThroughTheAddress()
    {
        GivenFires([]);
        context.Navigate("/quartz/agenda?from=2026-10-06T00:00:00Z&to=2026-10-06T06:00:00Z&group=reports");

        IRenderedComponent<Agenda> page = context.Render<Agenda>();

        page.WaitForAssertion(() => Queried().Should().ContainSingle().Which.Should().Match<DashboardUpcomingFiresQuery>(query =>
            query.From == new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero)
            && query.To == new DateTimeOffset(2026, 10, 6, 6, 0, 0, TimeSpan.Zero)
            && query.GroupContains == "reports"));
        page.Find("#agenda-from").GetAttribute("value").Should().Be("2026-10-06T00:00");
        page.Find("#agenda-group").GetAttribute("value").Should().Be("reports");

        page.Find("#agenda-group").Change("billing");
        page.Find(".qz-agenda-apply").Click();

        page.WaitForAssertion(() => context.CurrentUri.Should().EndWith("agenda?from=2026-10-06T00%3A00%3A00Z&to=2026-10-06T06%3A00%3A00Z&group=billing"));
        Queried()[^1].GroupContains.Should().Be("billing");
    }

    [Test]
    public void TheDefaultWindowIsWrittenIntoTheAddress()
    {
        GivenFires([]);

        IRenderedComponent<Agenda> page = context.Render<Agenda>();

        page.WaitForAssertion(() => context.CurrentUri.Should().EndWith("agenda?from=2026-10-05T14%3A23%3A00Z&to=2026-10-06T14%3A23%3A00Z",
            "a reload restores the window on screen"));
        Queried().Should().ContainSingle("writing the address reads nothing again");
    }

    /// <summary>
    /// One read can compute over thousands of triggers, so the page reads on load, on a change and on Refresh only.
    /// </summary>
    [Test]
    public void ThePageReadsNothingOnATimer()
    {
        GivenFires([Fire(nightly, now.AddMinutes(10))]);
        IRenderedComponent<Agenda> page = context.Render<Agenda>();
        page.WaitForAssertion(() => Queried().Should().ContainSingle());

        context.Clock.Advance(TimeSpan.FromMinutes(5));

        Queried().Should().ContainSingle();
        page.Find("[data-testid=refresh]").Click();
        page.WaitForAssertion(() => Queried().Should().HaveCount(2, "Refresh reads the same window again"));
    }

    [Test]
    public void TheNavigationHasTheAgendaAfterTheTimelineAndHighlightsItOnThePage()
    {
        context.Navigate("/quartz/agenda");

        IRenderedComponent<NavMenu> menu = context.Render<NavMenu>();

        List<string> labels = menu.TextOfAll(".qz-nav-link-text");
        labels.IndexOf("Agenda").Should().Be(labels.IndexOf("Timeline") + 1);
        menu.FindAll("a.qz-nav-active").Select(link => link.TextContent.Trim()).Should().Equal(["Agenda"]);
    }

    /// <summary>The ids of the elements <c>FocusAsync</c> was asked to focus, in order.</summary>
    private List<string> Focused()
    {
        return context.JSInterop.Invocations
            .Where(invocation => invocation.Identifier == "Blazor._internal.domWrapper.focus")
            .Select(invocation => ((ElementReference) invocation.Arguments[0]!).Id)
            .ToList();
    }

    private string LastFocused() => Focused().Should().NotBeEmpty().And.Subject.Last();

    /// <summary>
    /// The id an element's <c>@ref</c> is focused by, read off its first render: a later render of the element does not
    /// carry it again.
    /// </summary>
    private static string ReferenceOf(IElement element)
    {
        string? id = element.GetAttribute("blazor:elementreference");
        id.Should().NotBeNullOrEmpty();
        return id!;
    }

    private List<DashboardUpcomingFiresQuery> Queried()
    {
        return Fake.GetCalls(context.Api)
            .Where(call => call.Method.Name == nameof(IQuartzApiClient.QueryUpcomingFires))
            .Select(call => call.GetArgument<DashboardUpcomingFiresQuery>(1)!)
            .ToList();
    }

    private void GivenFires(List<UpcomingFireDto> fires, DateTimeOffset? cutAt = null, int notComputed = 0)
    {
        A.CallTo(() => context.Api.QueryUpcomingFires(A<string>._, A<DashboardUpcomingFiresQuery>._, A<CancellationToken>._))
            .ReturnsLazily((string _, DashboardUpcomingFiresQuery query, CancellationToken _) =>
                new UpcomingFiresDto(query.From ?? now, query.To, fires) { Cut = cutAt is not null, CutAtUtc = cutAt, TriggersNotComputed = notComputed });
    }

    private static UpcomingFireDto Fire(TriggerKeyDto trigger, DateTimeOffset at, TriggerState state = TriggerState.Normal)
    {
        return new UpcomingFireDto(at, trigger, new JobKeyDto("reports", "export"), state);
    }

    private static string Normalized(IElement element)
    {
        return string.Join(' ', element.TextContent.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries));
    }
}