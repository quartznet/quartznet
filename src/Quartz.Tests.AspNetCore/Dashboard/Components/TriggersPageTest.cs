using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The Triggers page's paging, group filtering and state filtering.
/// </summary>
public class TriggersPageTest
{
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
    public void TriggersAreListedUnderTheirGroupWithTheirState()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2, TriggerState.Paused));

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.TextOfAll("h2").Should().Equal(["nightly"]);
        page.TextOfAll("td.qz-col-state").Should().Equal(["Paused", "Paused"],
            "the state comes off the listing itself rather than from a call per trigger");
    }

    [Test]
    public void ThePagerAsksForTheSliceThePageNumberNames()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 60));

        IRenderedComponent<Triggers> page = context.Render<Triggers>();
        page.TextOfAll(".qz-key-badge-value").Should().HaveCount(25, "the page size is 25");

        page.FindAll(".qz-pagination button").First(button => button.TextContent.Trim() == "2").Click();

        A.CallTo(() => context.Api.QueryTriggers(
                TestData.SchedulerName,
                A<DashboardTriggerQuery>.That.Matches(query => query.Skip == 25 && query.Take == 25),
                A<CancellationToken>._))
            .MustHaveHappened();
    }

    [Test]
    public void TheStateFilterIsPassedToTheQueryAndShownAsSelected()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 1, TriggerState.Error));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        IElement errorOnly = page.FindAll("button").First(button => button.TextContent.Trim() == "Error only");
        errorOnly.ClassList.Should().NotContain("qz-button-primary", "no state filter is applied yet");

        errorOnly.Click();

        A.CallTo(() => context.Api.QueryTriggers(
                TestData.SchedulerName,
                A<DashboardTriggerQuery>.That.Matches(query => query.State == TriggerState.Error && query.Skip == 0),
                A<CancellationToken>._))
            .MustHaveHappened();
        page.FindAll("button").First(button => button.TextContent.Trim() == "Error only")
            .ClassList.Should().Contain("qz-button-primary",
                "which filter is applied has to be visible, or the listing looks like the whole truth");
    }

    [Test]
    public void ChangingTheStateFilterReturnsToTheFirstPage()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 60));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();
        page.FindAll(".qz-pagination button").First(button => button.TextContent.Trim() == "2").Click();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Executing only").Click();

        // A narrowed listing has fewer pages, so the page number the reader was on means nothing.
        A.CallTo(() => context.Api.QueryTriggers(
                TestData.SchedulerName,
                A<DashboardTriggerQuery>.That.Matches(query => query.State == TriggerState.Executing && query.Skip == 0),
                A<CancellationToken>._))
            .MustHaveHappened();
    }

    [Test]
    public void ReadOnlyModeHidesEveryMutatingAction()
    {
        context.Options.ReadOnly = true;
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.HasButton("Pause").Should().BeFalse();
        page.HasButton("Unschedule").Should().BeFalse();
        page.HasButton("Pause group").Should().BeFalse();
        page.HasButton("Error only").Should().BeTrue("filtering is reading, not writing");
    }

    [Test]
    public void AGroupActionAppliesToTheGroupItNamesAndNotToTheOnesThatMerelyContainIt()
    {
        GivenTriggers([
            .. TestData.Dashboard.TriggerHeaders("nightly", 2),
            .. TestData.Dashboard.TriggerHeaders("nightly-archive", 3)
        ]);
        A.CallTo(() => context.Api.PauseTrigger(A<string>._, A<TriggerKeyDto>._, A<CancellationToken>._)).Returns(true);
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Pause group").Click();
        page.ConfirmPause();

        A.CallTo(() => context.Api.PauseTrigger(
                TestData.SchedulerName,
                A<TriggerKeyDto>.That.Matches(key => key.Group == "nightly-archive"),
                A<CancellationToken>._))
            .MustNotHaveHappened();
        context.Toasts.Messages.Should().ContainSingle()
            .Which.Message.Should().Be("Paused 2 of 2 trigger(s) in group nightly.");
    }

    [Test]
    public void UnschedulingFromTheListingSaysWhetherTheTriggerWasStillThere()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 1));
        A.CallTo(() => context.Api.UnscheduleJob(A<string>._, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(true);
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Unschedule").Click();
        page.Find(".qz-confirm-dialog button.qz-button-danger").Click();

        context.Toasts.Messages.Should().ContainSingle()
            .Which.Message.Should().Be("Unscheduled trigger nightly.trigger-1.");

        A.CallTo(() => context.Api.UnscheduleJob(A<string>._, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(false);

        page.FindAll("button").First(button => button.TextContent.Trim() == "Unschedule").Click();
        page.Find(".qz-confirm-dialog button.qz-button-danger").Click();

        context.Toasts.Messages[^1].Message.Should().Be(
            "Trigger nightly.trigger-1 was not unscheduled - it no longer exists.",
            "a listing is a snapshot, so the answer to 'was it still there' is the one thing the page "
            + "cannot work out for itself");
    }

    [Test]
    public void TheAwaitingFilterAsksForTheTriggersThatAreWaiting()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 1, TriggerState.Awaiting));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Awaiting only").Click();

        A.CallTo(() => context.Api.QueryTriggers(
                TestData.SchedulerName,
                A<DashboardTriggerQuery>.That.Matches(query => query.State == TriggerState.Awaiting && query.Skip == 0),
                A<CancellationToken>._))
            .MustHaveHappened();
        page.FindAll("button").First(button => button.TextContent.Trim() == "Awaiting only")
            .ClassList.Should().Contain("qz-button-primary");
        page.Markup.Should().NotContain("Showing Awaiting triggers only",
            "the filter has a button of its own now, and the spelled-out line is for the states that "
            + "have none");
    }

    /// <summary>
    /// A listing narrowed to Awaiting is a page of triggers none of which will ever fire on their own,
    /// so each row has to say what it is waiting for.
    /// </summary>
    [Test]
    public void AWaitingTriggerNamesTheTriggerItIsWaitingFor()
    {
        GivenTriggers([
            new TriggerHeaderDto("nightly", "reconcile", "Cron", null, TriggerState.Awaiting, null)
            {
                ContinuesAfter = new TriggerKeyDto("nightly", "import"),
                ContinuationCondition = ContinuationCondition.OnFailure | ContinuationCondition.OnCancellation
            }
        ]);

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.Markup.Should().Contain("after nightly.import",
            "a row in Awaiting with no parent on it says only that the trigger is not running");
        page.Find(".qz-continues-after").GetAttribute("title").Should().Be("Released on: Failure or cancellation",
            "the condition is the other half of the answer, spelled out rather than shown as the flags "
            + "value the enum prints");
        page.TextOfAll("td.qz-col-state").Should().Equal(["Awaiting"]);
        page.Find("td.qz-col-state .qz-state-indicator").ClassList.Should().Contain("qz-state-awaiting",
            "waiting is its own colour: the fallback blue means 'a state the dashboard does not "
            + "recognise', which is what Awaiting would have looked like");
    }

    [Test]
    public void ATriggerThatWaitsForNothingShowsNoParent()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 1));

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.FindAll(".qz-continues-after").Should().BeEmpty(
            "an ordinary trigger fires on its own schedule, and a line saying it waits for nothing would "
            + "be one more thing to read on every row of every listing");
    }

    [Test]
    public void ATriggerWithNoExecutionGroupSaysSoRatherThanShowingNothing()
    {
        GivenTriggers([new TriggerHeaderDto("nightly", "trigger-1", "Cron", "0/5 * * * * ?", TriggerState.Normal, null)]);

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.Markup.Should().Contain("qz-muted",
            "an empty cell reads as a rendering fault; an em dash reads as 'no execution group'");
    }

    /// <summary>
    /// The overview's trigger-state histogram links here, so a state named in the query string has to
    /// open the listing already narrowed to it.
    /// </summary>
    [Test]
    public void AStateInTheQueryStringOpensTheListingNarrowedToIt()
    {
        GivenTriggers([
            .. TestData.Dashboard.TriggerHeaders("nightly", 2, TriggerState.Paused),
            .. TestData.Dashboard.TriggerHeaders("reports", 3, TriggerState.Normal, firstIndex: 10)
        ]);
        context.Navigate("/quartz/triggers?state=Paused");

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        A.CallTo(() => context.Api.QueryTriggers(
                TestData.SchedulerName,
                A<DashboardTriggerQuery>.That.Matches(query => query.State == TriggerState.Paused),
                A<CancellationToken>._))
            .MustHaveHappened();
        page.TextOfAll("h2").Should().Equal(["nightly"], "only the paused triggers were asked for");
        page.Markup.Should().Contain("Showing Paused triggers only",
            "the buttons offer three of the states and the histogram links to five, so a filter none of "
            + "them can show as selected has to be spelled out");
    }

    [Test]
    public void AQueryStringNamingNoStateOpensTheUnfilteredListing()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        context.Navigate("/quartz/triggers?state=not-a-state");

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        A.CallTo(() => context.Api.QueryTriggers(
                TestData.SchedulerName,
                A<DashboardTriggerQuery>.That.Matches(query => query.State == null),
                A<CancellationToken>._))
            .MustHaveHappened();
        page.TextOfAll("td.qz-col-state").Should().HaveCount(2,
            "a query string is whatever the address bar holds, and a spelling nothing recognises is not "
            + "a reason to show an error where a listing belongs");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Filters
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public void EveryFilterIsPassedToTheQueryAndWrittenToTheAddress()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.Find("#trigger-filter-group").Change("night");
        page.Find("#trigger-filter-name").Change("trigger");
        page.Find("#trigger-filter-job-group").Change("reports");
        page.Find("#trigger-filter-job-name").Change("export");
        page.Find("#trigger-filter-calendar").Change("holidays");
        page.Find("#trigger-filter-state").Change("Paused");
        page.Find("#trigger-filter-before").Change("2026-09-27T12:30");
        page.Find(".qz-filter-apply").Click();

        DateTimeOffset before = new(2026, 9, 27, 12, 30, 0, TimeSpan.Zero);
        page.WaitForAssertion(() => A.CallTo(() => context.Api.QueryTriggers(
                TestData.SchedulerName,
                A<DashboardTriggerQuery>.That.Matches(query =>
                    query.GroupContains == "night"
                    && query.NameContains == "trigger"
                    && query.Job == new JobKeyDto("reports", "export")
                    && query.CalendarName == "holidays"
                    && query.State == TriggerState.Paused
                    && query.NextFireTimeBefore == before
                    && query.Skip == 0),
                A<CancellationToken>._))
            .MustHaveHappened());
        context.CurrentUri.Should().EndWith(
            "/quartz/triggers?group=night&name=trigger&jobGroup=reports&jobName=export&calendar=holidays&state=Paused&before=2026-09-27T12%3A30%3A00.0000000%2B00%3A00",
            "every filter is a query parameter, so a narrowed listing is a link someone can send or bookmark");
    }

    [Test]
    public void AFilteredAddressOpensTheListingNarrowedByItWithTheFieldsFilledIn()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        context.Navigate("/quartz/triggers?name=trigger-2&jobGroup=reports&jobName=export&calendar=holidays&before=2026-09-27T12%3A30%3A00.0000000%2B00%3A00");

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        A.CallTo(() => context.Api.QueryTriggers(
                TestData.SchedulerName,
                A<DashboardTriggerQuery>.That.Matches(query =>
                    query.NameContains == "trigger-2"
                    && query.Job == new JobKeyDto("reports", "export")
                    && query.CalendarName == "holidays"
                    && query.NextFireTimeBefore == new DateTimeOffset(2026, 9, 27, 12, 30, 0, TimeSpan.Zero)),
                A<CancellationToken>._))
            .MustHaveHappened();
        page.Find("#trigger-filter-name").GetAttribute("value").Should().Be("trigger-2",
            "the fields show what the listing is narrowed by, or it looks like the whole truth");
        page.Find("#trigger-filter-before").GetAttribute("value").Should().Be("2026-09-27T12:30",
            "the bound is shown on the clock the dashboard shows, which the context pins to UTC");
    }

    [Test]
    public void HalfAJobKeyIsRefusedBeforeAnythingIsAsked()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 1));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();
        Fake.ClearRecordedCalls(context.Api);

        page.Find("#trigger-filter-job-group").Change("reports");
        page.Find(".qz-filter-apply").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=trigger-filter-error]").TextContent
            .Should().Contain("give both to filter by job", "a job is a key, and half of one names no job"));
        A.CallTo(() => context.Api.QueryTriggers(A<string>._, A<DashboardTriggerQuery>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public void ClearingTheFiltersListsEverythingAgain()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        context.Navigate("/quartz/triggers?name=trigger-2");
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.Find(".qz-filter-clear").Click();

        page.WaitForAssertion(() => A.CallTo(() => context.Api.QueryTriggers(
                TestData.SchedulerName,
                A<DashboardTriggerQuery>.That.Matches(query => query.NameContains == null && query.Job == null && query.State == null),
                A<CancellationToken>._))
            .MustHaveHappened());
        context.CurrentUri.Should().EndWith("/quartz/triggers");
        page.Find("#trigger-filter-name").GetAttribute("value").Should().BeEmpty();
    }

    [Test]
    public void TheStateSelectOffersEveryStateATriggerCanBeListedIn()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 1));

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.FindAll("#trigger-filter-state option").Select(option => option.TextContent)
            .Should().Equal(["All states", "Normal", "Paused", "Complete", "Error", "Blocked", "Executing", "Awaiting"],
                "None is what a key that resolves to nothing answers, and no listed trigger is in it");
    }

    /// <summary>
    /// A target whose HTTP API predates the next-fire filter ignores it. The client proves that and
    /// refuses; the page drops that one filter, says why beside its field, and lists the rest.
    /// </summary>
    [Test]
    public void ANextFireFilterTheTargetCannotApplyIsDisabledWithTheReason()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        A.CallTo(() => context.Api.QueryTriggers(
                A<string>._, A<DashboardTriggerQuery>.That.Matches(query => query.NextFireTimeBefore != null), A<CancellationToken>._))
            .Throws(new NotSupportedException("This scheduler does not filter by next fire time."));
        context.Navigate("/quartz/triggers?name=trigger&before=2026-09-27T12%3A30%3A00.0000000%2B00%3A00");

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.WaitForAssertion(() =>
        {
            page.Find("[data-testid=trigger-filter-before-unavailable]").TextContent.Should().Contain("does not filter by next fire time");
            page.Find("#trigger-filter-before").HasAttribute("disabled").Should().BeTrue();
            page.TextOfAll("td.qz-col-state").Should().HaveCount(2, "the listing is still shown, narrowed by what the target can apply");
            page.FindAll(".qz-error-alert").Should().BeEmpty("a filter a target cannot apply is not an error page");
        });
        context.CurrentUri.Should().EndWith("/quartz/triggers?name=trigger",
            "the address stops claiming a filter the listing is not narrowed by");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Acting on a selection
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public void PausingASelectionIsOneCallAndSaysWhichRowsItDidNotReach()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 3));
        TriggerKeyDto first = new("nightly", "trigger-1");
        TriggerKeyDto second = new("nightly", "trigger-2");
        A.CallTo(() => context.Api.PauseTriggers(TestData.SchedulerName, A<IReadOnlyCollection<TriggerKeyDto>>._, A<CancellationToken>._))
            .Returns(new List<TriggerKeyDto> { first });
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        Select(page, "nightly.trigger-1");
        Select(page, "nightly.trigger-2");
        page.WaitForAssertion(() => page.Find(".qz-bulk-count").TextContent.Should().Be("2 selected"));
        page.Find(".qz-bulk-pause").Click();
        page.WaitForAssertion(() => page.Markup.Should().Contain("Pause 2 selected trigger(s)?",
            "the prompt is asked once, for the whole batch"));
        page.ConfirmPause();

        page.WaitForAssertion(() => A.CallTo(() => context.Api.PauseTriggers(
                TestData.SchedulerName,
                A<IReadOnlyCollection<TriggerKeyDto>>.That.Matches(keys => keys.Count == 2 && keys.Contains(first) && keys.Contains(second)),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());
        page.WaitForAssertion(() =>
        {
            IElement result = page.Find("[data-testid=trigger-bulk-result]");
            result.TextContent.Should().Contain("Paused 1 of 2 selected trigger(s).");
            result.TextContent.Should().Contain("nightly.trigger-2",
                "the row the batch did not reach is the one somebody has to go and look at");
            result.TextContent.Should().Contain("no longer exists or was already paused");
        });
        A.CallTo(() => context.Api.PauseTriggerWith(A<string>._, A<TriggerKeyDto>._, A<PauseDetails>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        context.Toasts.Messages[^1].Message.Should().Be("Paused 1 of 2 selected trigger(s).");
        context.ActionLog.GetLatest(2).Should().BeEquivalentTo(
            [
                new { Action = "PauseTrigger", Target = "nightly.trigger-2", Succeeded = false },
                new { Action = "PauseTrigger", Target = "nightly.trigger-1", Succeeded = true }
            ],
            options => options.ExcludingMissingMembers(),
            "a bulk pause is recorded under each trigger's own key, where 'who paused this' looks");
        page.FindAll(".qz-bulk-count").Should().BeEmpty("the selection is spent once it has been acted on");
    }

    /// <summary>
    /// A pause that says something is one call for the whole selection, through the key-set member that
    /// records it, rather than a call per row (#3964).
    /// </summary>
    [Test]
    public void APauseWithAReasonIsOneCallForTheWholeSelection()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 3));
        TriggerKeyDto first = new("nightly", "trigger-1");
        TriggerKeyDto second = new("nightly", "trigger-2");
        A.CallTo(() => context.Api.PauseTriggersWith(
                TestData.SchedulerName, A<IReadOnlyCollection<TriggerKeyDto>>._, A<PauseDetails>._, A<CancellationToken>._))
            .ReturnsLazily((string _, IReadOnlyCollection<TriggerKeyDto> keys, PauseDetails? _, CancellationToken _) => keys.ToList());
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        Select(page, "nightly.trigger-1");
        Select(page, "nightly.trigger-2");
        page.WaitForAssertion(() => page.Find(".qz-bulk-count").TextContent.Should().Be("2 selected"));
        page.Find(".qz-bulk-pause").Click();
        page.ConfirmPause("disk full on the export host");

        page.WaitForAssertion(() => A.CallTo(() => context.Api.PauseTriggersWith(
                TestData.SchedulerName,
                A<IReadOnlyCollection<TriggerKeyDto>>.That.Matches(keys => keys.Count == 2 && keys.Contains(first) && keys.Contains(second)),
                A<PauseDetails>.That.Matches(details => details.Reason == "disk full on the export host"),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());
        // Over HTTP a call per row is a request and a store transaction per row.
        A.CallTo(() => context.Api.PauseTriggerWith(A<string>._, A<TriggerKeyDto>._, A<PauseDetails>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        page.WaitForAssertion(() => context.Toasts.Messages[^1].Message.Should().Be("Paused 2 of 2 selected trigger(s)."));
        context.ActionLog.GetLatest(2).Should().BeEquivalentTo(
            [
                new { Action = "PauseTrigger", Target = "nightly.trigger-2", Succeeded = true, Message = "one of 2 selected; reason: disk full on the export host" },
                new { Action = "PauseTrigger", Target = "nightly.trigger-1", Succeeded = true, Message = "one of 2 selected; reason: disk full on the export host" }
            ],
            options => options.ExcludingMissingMembers(),
            "one call still records each trigger under its own key, with the reason it was paused for");
    }

    [Test]
    public void ARefusedPauseWithAReasonLogsTheReasonBesideWhatTheSchedulerSaid()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 1));
        A.CallTo(() => context.Api.PauseTriggersWith(A<string>._, A<IReadOnlyCollection<TriggerKeyDto>>._, A<PauseDetails>._, A<CancellationToken>._))
            .Throws(new SchedulerException("the store refused"));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.Find("input.qz-select-group").Change(true);
        page.Find(".qz-bulk-pause").Click();
        page.ConfirmPause("maintenance");

        page.WaitForAssertion(() => context.ActionLog.GetLatest(1).Should().ContainSingle()
            .Which.Message.Should().Be("one of 1 selected; reason: maintenance; the store refused"));
    }

    /// <summary>
    /// A data source written against 4.3 has no key-set member that records a reason. The interface's
    /// default makes the pause a key at a time through the member that does, so the reason is still kept.
    /// </summary>
    [Test]
    public void APauseWithAReasonIsMadeThroughTheMemberThatRecordsItForEachSelectedRow()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        A.CallTo(() => context.Api.PauseTrigger(A<string>._, A<TriggerKeyDto>._, A<CancellationToken>._)).Returns(true);
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.Find("input.qz-select-group").Change(true);
        page.WaitForAssertion(() => page.Find(".qz-bulk-count").TextContent.Should().Be("2 selected",
            "the group's box selects every row of that group on the page"));
        page.Find(".qz-bulk-pause").Click();
        page.ConfirmPause("disk full on the export host");

        page.WaitForAssertion(() => A.CallTo(() => context.Api.PauseTriggerWith(
                TestData.SchedulerName,
                A<TriggerKeyDto>._,
                A<PauseDetails>.That.Matches(details => details.Reason == "disk full on the export host"),
                A<CancellationToken>._))
            .MustHaveHappenedTwiceExactly());
        A.CallTo(() => context.Api.PauseTriggers(A<string>._, A<IReadOnlyCollection<TriggerKeyDto>>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        page.WaitForAssertion(() => context.Toasts.Messages[^1].Message.Should().Be("Paused 2 of 2 selected trigger(s)."));
        page.FindAll("[data-testid=trigger-bulk-result]").Should().BeEmpty("every row was reached, so there is nothing to follow up");
    }

    /// <summary>
    /// The pause is one call and one store transaction, so a refusal is the whole selection's: every row
    /// is listed with what the scheduler said.
    /// </summary>
    [Test]
    public void ARefusedPauseListsEveryRowWithWhatTheSchedulerSaid()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        A.CallTo(() => context.Api.PauseTriggersWith(A<string>._, A<IReadOnlyCollection<TriggerKeyDto>>._, A<PauseDetails>._, A<CancellationToken>._))
            .Throws(new SchedulerException("the store refused"));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.Find("input.qz-select-group").Change(true);
        page.Find(".qz-bulk-pause").Click();
        page.ConfirmPause("maintenance");

        page.WaitForAssertion(() =>
        {
            IElement result = page.Find("[data-testid=trigger-bulk-result]");
            result.TextContent.Should().Contain("Paused 0 of 2 selected trigger(s).");
            result.TextContent.Should().Contain("nightly.trigger-1");
            result.TextContent.Should().Contain("nightly.trigger-2");
            result.TextContent.Should().Contain("the store refused", "a failure says what the scheduler said");
        });
        context.Toasts.Messages[^1].Message.Should().Be("Paused 0 of 2 selected trigger(s). 2 failed.");
    }

    [Test]
    public void ResumingASelectionIsOneCall()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2, TriggerState.Paused));
        A.CallTo(() => context.Api.ResumeTriggers(TestData.SchedulerName, A<IReadOnlyCollection<TriggerKeyDto>>._, A<CancellationToken>._))
            .ReturnsLazily((string _, IReadOnlyCollection<TriggerKeyDto> keys, CancellationToken _) => keys.ToList());
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.Find("input.qz-select-group").Change(true);
        page.Find(".qz-bulk-resume").Click();

        page.WaitForAssertion(() => A.CallTo(() => context.Api.ResumeTriggers(
                TestData.SchedulerName, A<IReadOnlyCollection<TriggerKeyDto>>.That.Matches(keys => keys.Count == 2), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());
        page.WaitForAssertion(() => context.Toasts.Messages[^1].Message.Should().Be("Resumed 2 of 2 selected trigger(s)."));
    }

    [Test]
    public void UnschedulingASelectionAsksFirst()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        A.CallTo(() => context.Api.UnscheduleJobs(TestData.SchedulerName, A<IReadOnlyCollection<TriggerKeyDto>>._, A<CancellationToken>._))
            .ReturnsLazily((string _, IReadOnlyCollection<TriggerKeyDto> keys, CancellationToken _) => keys.ToList());
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.Find("input.qz-select-group").Change(true);
        page.Find(".qz-bulk-unschedule").Click();

        page.WaitForAssertion(() => page.Markup.Should().Contain("Unschedule 2 selected trigger(s)?"));
        A.CallTo(() => context.Api.UnscheduleJobs(A<string>._, A<IReadOnlyCollection<TriggerKeyDto>>._, A<CancellationToken>._))
            .MustNotHaveHappened();

        page.Find(".qz-confirm-dialog button.qz-button-danger").Click();

        page.WaitForAssertion(() => A.CallTo(() => context.Api.UnscheduleJobs(
                TestData.SchedulerName, A<IReadOnlyCollection<TriggerKeyDto>>.That.Matches(keys => keys.Count == 2), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());
        page.WaitForAssertion(() => context.ActionLog.GetLatest(2).Should().OnlyContain(entry => entry.Action == "UnscheduleTrigger" && entry.Succeeded));
    }

    [Test]
    public void ABatchTheSchedulerRefusesReportsEveryRowAsNotReached()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        A.CallTo(() => context.Api.ResumeTriggers(A<string>._, A<IReadOnlyCollection<TriggerKeyDto>>._, A<CancellationToken>._))
            .Throws(new HttpClientException("Received response with status code Forbidden, error details: read-only"));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.Find("input.qz-select-group").Change(true);
        page.Find(".qz-bulk-resume").Click();

        page.WaitForAssertion(() =>
        {
            IElement result = page.Find("[data-testid=trigger-bulk-result]");
            result.TextContent.Should().Contain("Resumed 0 of 2 selected trigger(s).");
            result.QuerySelectorAll("li").Should().HaveCount(2, "one request, so a refusal is the whole batch's");
        });
        context.Toasts.Messages[^1].Message.Should().Be("Resumed 0 of 2 selected trigger(s). 2 failed.");
        context.ActionLog.GetLatest(2).Should().OnlyContain(entry => !entry.Succeeded && entry.Message!.Contains("read-only"));
    }

    [Test]
    public void ApplyingAFilterClearsTheSelection()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();
        Select(page, "nightly.trigger-1");
        page.WaitForElement(".qz-bulk-count");

        page.Find("#trigger-filter-name").Change("trigger-2");
        page.Find(".qz-filter-apply").Click();

        page.WaitForAssertion(() => page.FindAll(".qz-bulk-count").Should().BeEmpty(
            "a bulk action on rows the narrowed listing no longer shows would be a surprise"));
    }

    [Test]
    public void ReadOnlyModeOffersNoSelection()
    {
        context.Options.ReadOnly = true;
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.FindAll("input[type=checkbox]").Should().BeEmpty("a selection is only for acting on, and read-only acts on nothing");
        page.FindAll(".qz-filter-apply").Should().ContainSingle("filtering is reading, not writing");
    }

    private static void Select(IRenderedComponent<Triggers> page, string key)
    {
        page.Find("input.qz-select-trigger[aria-label='Select " + key + "']").Change(true);
    }

    private void GivenTriggers(IReadOnlyList<TriggerHeaderDto> triggers)
    {
        A.CallTo(() => context.Api.QueryTriggers(A<string>._, A<DashboardTriggerQuery>._, A<CancellationToken>._))
            .ReturnsLazily((string _, DashboardTriggerQuery query, CancellationToken _) =>
            {
                List<TriggerHeaderDto> matched = triggers
                    .Where(trigger => (query.GroupContains is null
                            || trigger.Group.Contains(query.GroupContains, StringComparison.OrdinalIgnoreCase))
                        && (query.State is null || trigger.State == query.State))
                    .ToList();

                return TestData.Dashboard.Page<TriggerHeaderDto>(
                    matched.Skip(query.Skip).Take(query.Take).ToList(),
                    matched.Count);
            });
    }
}
