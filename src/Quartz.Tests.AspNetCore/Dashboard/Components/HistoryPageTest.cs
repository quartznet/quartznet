using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// What the History page computes over the page it is showing, and how it writes it.
/// </summary>
/// <remarks>
/// The average and the p95 are the only arithmetic the dashboard does over a listing, and their
/// formatting is what a reader compares two runs by — a duration shown in the wrong unit reads as a
/// three-order-of-magnitude regression.
/// </remarks>
public class HistoryPageTest
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
    public void SubSecondDurationsAreShownInMilliseconds()
    {
        GivenHistory(
            Entry(100),
            Entry(200),
            Entry(300),
            Entry(400));

        IRenderedComponent<History> page = context.Render<History>();

        page.StatCardValue("Avg duration (page)").Should().Be("250 ms",
            "the average of 100, 200, 300 and 400 milliseconds is 250, and a sub-second duration is "
            + "unreadable in any larger unit");
        page.StatCardValue("P95 duration (page)").Should().Be("400 ms",
            "the p95 of four values is the fourth: ceil(4 * 0.95) - 1 indexes the last one");
    }

    [Test]
    public void ADurationOfZeroIsCountedAsARunButNotAsADuration()
    {
        GivenHistory(
            Entry(100),
            Entry(200),
            Entry(300),
            Entry(400),
            Entry(0));

        IRenderedComponent<History> page = context.Render<History>();

        page.StatCardValue("Avg duration (page)").Should().Be("250 ms",
            "a run the store recorded no duration for would otherwise drag every average toward zero");
        page.StatCardValue("Success rate (page)").Should().Be("100.0 %",
            "it is still a run that succeeded, so it counts toward the success rate");
    }

    [Test]
    public void DurationsCrossAUnitBoundaryOnePageAtATime()
    {
        GivenHistory(
            Entry(1_500),
            Entry(2_000),
            Entry(90_000));

        IRenderedComponent<History> page = context.Render<History>();

        page.StatCardValue("Avg duration (page)").Should().Be("31.17 s",
            "the average of 1.5, 2 and 90 seconds is 31.166…, and anything under a minute reads in seconds");
        page.StatCardValue("P95 duration (page)").Should().Be("0:01:30",
            "a minute or more is spelled out rather than shown as 90 s");
    }

    [Test]
    public void AFailedRunIsCountedAndShown()
    {
        GivenHistory(
            Entry(100),
            Entry(200, succeeded: false, exceptionMessage: "job blew up"),
            Entry(300),
            Entry(400));

        IRenderedComponent<History> page = context.Render<History>();

        page.StatCardValue("Failures (page)").Should().Be("1");
        page.StatCardValue("Success rate (page)").Should().Be("75.0 %",
            "three of four succeeded, and the rate is written as a percentage rather than a ratio");
        page.Markup.Should().Contain("job blew up",
            "the failure's message is the reason a reader opened the page");
    }

    [Test]
    public void APageWithoutADurationSaysSoRatherThanShowingZero()
    {
        GivenHistory(Entry(0), Entry(0));

        IRenderedComponent<History> page = context.Render<History>();

        page.StatCardValue("Avg duration (page)").Should().Be("n/a",
            "there is nothing to average, and 0 ms would read as an execution that took no time");
        page.StatCardValue("P95 duration (page)").Should().Be("n/a");
    }

    [Test]
    public void AStoreHoldingNothingShowsAnEmptyPageRatherThanAFault()
    {
        IRenderedComponent<History> page = context.Render<History>();

        page.Markup.Should().Contain("No execution history yet",
            "a store that has recorded nothing answers with an empty page, which is the only "
            + "'no history' answer there is now that the client cannot say it keeps none");
        page.Markup.Should().NotContain("qz-stat-card",
            "there is nothing to compute an average over");
    }

    [Test]
    public void TheAppliedFiltersAreSummarizedAboveTheTable()
    {
        GivenHistory(Entry(100));

        context.Navigate("/quartz/history?job=DummyGroup.DummyJob&trigger=%20CronTriggerGroup%20");

        IRenderedComponent<History> page = context.Render<History>();

        page.Markup.Should().Contain("Job: DummyGroup.DummyJob · Trigger: CronTriggerGroup",
            "the summary says what the listing is narrowed to, with the query values trimmed as they "
            + "were applied");

        A.CallTo(() => context.Api.QueryExecutions(
                A<DashboardHistoryQuery>.That.Matches(query =>
                    query.JobFilter == "DummyGroup.DummyJob"
                    && query.TriggerFilter == "CronTriggerGroup"
                    && query.SchedulerName == TestData.SchedulerName
                    && query.IncludeTotalCount),
                A<CancellationToken>._))
            .MustHaveHappened();
    }

    [Test]
    public void ThePagerTranslatesTheRequestedPageIntoSkipAndTake()
    {
        GivenHistory(60, Entry(100));

        context.Navigate("/quartz/history?page=3");

        context.Render<History>();

        A.CallTo(() => context.Api.QueryExecutions(
                A<DashboardHistoryQuery>.That.Matches(query => query.Skip == 50 && query.Take == 25),
                A<CancellationToken>._))
            .MustHaveHappened();
    }

    [Test]
    public void APageBeyondTheEndIsClampedToTheLastOne()
    {
        GivenHistory(30, Entry(100));

        context.Navigate("/quartz/history?page=99");

        IRenderedComponent<History> page = context.Render<History>();

        page.Markup.Should().Contain("Page 2 / 2",
            "asking for page 99 of 2 means the last one, which is what a job or trigger listing does too");
        A.CallTo(() => context.Api.QueryExecutions(
                A<DashboardHistoryQuery>.That.Matches(query => query.Skip == 25),
                A<CancellationToken>._))
            .MustHaveHappened();
    }

    [Test]
    public void EachRowSaysWhichNodeRanIt()
    {
        GivenHistory(
            Entry(100, node: "node-a"),
            Entry(200, node: "node-b"));

        IRenderedComponent<History> page = context.Render<History>();

        page.TextOfAll(".qz-history-node").Should().Equal(["node-a", "node-b"],
            "every node of a cluster keeps its own history, and a row that does not name one cannot be "
            + "attributed to a machine");
    }

    [Test]
    public void TheNodeFilterNarrowsTheListingAndTheStatCardsSaySo()
    {
        GivenHistory(Entry(100, node: "node-b"));

        context.Navigate("/quartz/history?node=node-b");

        IRenderedComponent<History> page = context.Render<History>();

        A.CallTo(() => context.Api.QueryExecutions(
                A<DashboardHistoryQuery>.That.Matches(query => query.SchedulerInstanceId == "node-b"),
                A<CancellationToken>._))
            .MustHaveHappened();

        page.StatCardValue("Success rate (page, node-b)").Should().Be("100.0 %",
            "the figures are over one node's rows now, so the card has to say which node they cover");
        page.Markup.Should().Contain("Node: node-b",
            "the summary above the table says what the listing is narrowed to");
    }

    [Test]
    public void AnUnfilteredListingSaysItCoversEveryNode()
    {
        GivenHistory(Entry(100));

        IRenderedComponent<History> page = context.Render<History>();

        page.Markup.Should().Contain("Node: all nodes",
            "a reader has to be able to tell 'every node' from 'the one node this page happens to show'");
        page.StatCardValue("Success rate (page)").Should().Be("100.0 %");
    }

    [Test]
    public void TheNodeFilterOffersTheClusterNodesEvenWhereNoRowNamesThem()
    {
        GivenHistory(Entry(100, node: "node-a"));
        A.CallTo(() => context.Api.QueryClusterNodes(TestData.SchedulerName, A<CancellationToken>._))
            .Returns(new List<ClusterNodeDto>
            {
                new("node-a", null, null, ClusterNodeState.Alive, IsCurrentNode: true),
                new("node-b", null, null, ClusterNodeState.Failed, IsCurrentNode: false)
            });

        IRenderedComponent<History> page = context.Render<History>();

        page.TextOfAll("#history-node-filter option").Should().Equal(["All nodes", "node-a", "node-b"],
            "a node that has produced nothing on this page is still a node worth asking about — most "
            + "of all the one that stopped");
    }

    [Test]
    public void ChoosingANodePutsItInTheUrlSoTheViewCanBeShared()
    {
        GivenHistory(Entry(100, node: "node-a"), Entry(200, node: "node-b"));

        IRenderedComponent<History> page = context.Render<History>();
        page.Find("#history-node-filter").Change("node-b");

        context.CurrentUri.Should().EndWith("/quartz/history?node=node-b",
            "the filters are query parameters so a narrowed listing is a link someone can send");
    }

    [Test]
    public void MisfiresAreListedBesideTheExecutions()
    {
        GivenHistory(Entry(100));
        GivenMisfires(
            TestData.Dashboard.MisfireEntry("nightly", jobKey: new JobKeyDto("reports", "rollup")),
            TestData.Dashboard.MisfireEntry("hourly", schedulerInstanceId: "node-b"));

        IRenderedComponent<History> page = context.Render<History>();

        page.TextOfAll(".qz-misfire-node").Should().Equal([TestData.SchedulerInstanceId, "node-b"]);
        page.Markup.Should().Contain("nightly").And.Contain("hourly");
        page.Markup.Should().Contain("reports.rollup",
            "the job a missed trigger points at is what a reader is looking for");
    }

    [Test]
    public void AFiringTheOverlapPolicySkippedIsToldApartFromAMisfire()
    {
        GivenHistory(Entry(100));
        GivenMisfires(
            TestData.Dashboard.MisfireEntry("skipped") with { Reason = MisfireReason.Overlap },
            TestData.Dashboard.MisfireEntry("missed"));

        IRenderedComponent<History> page = context.Render<History>();

        page.TextOfAll(".qz-misfire-reason").Should().Equal(["Overlap", "Misfire"],
            "a skip is the trigger doing what it was told, and a reader chasing a slow scheduler needs to tell it from one");
    }

    [Test]
    public void ASchedulerWithNoMisfiresSaysSoRatherThanShowingNothing()
    {
        GivenHistory(Entry(100));
        GivenMisfires();

        IRenderedComponent<History> page = context.Render<History>();

        page.Markup.Should().Contain("No misfires recorded",
            "an empty section says the scheduler is healthy; a missing one says nothing at all");
    }

    /// <summary>
    /// The other half of the overview's misfire tile: it links to <c>#misfires</c>, and a fragment
    /// that names nothing lands the reader at the top of the page with no sign anything went wrong.
    /// </summary>
    [Test]
    public void TheMisfiresSectionCarriesTheIdTheOverviewsTileLinksTo()
    {
        GivenHistory(Entry(100));
        GivenMisfires(TestData.Dashboard.MisfireEntry("nightly"));

        IRenderedComponent<History> page = context.Render<History>();

        page.Find("#misfires").ClassList.Should().Contain("qz-misfires",
            "the overview's tile links to quartz/history#misfires, so this id is what makes that link land");
    }

    /// <summary>
    /// A scheduler in another process whose API serves no history is told apart from one that has run
    /// nothing.
    /// </summary>
    /// <remarks>
    /// "No execution history yet. Run a job to populate history." would be a statement about a scheduler
    /// that may have been running jobs all day, and an error with a retry button would invite a reader
    /// to ask again for something that will not arrive.
    /// </remarks>
    [Test]
    public void ATargetThatServesNoHistorySaysSoRatherThanShowingAnEmptyPage()
    {
        A.CallTo(() => context.Api.QueryExecutions(A<DashboardHistoryQuery>._, A<CancellationToken>._))
            .Throws(new NotSupportedException("the target does not serve history"));

        IRenderedComponent<History> page = context.Render<History>();

        page.Markup.Should().Contain("history-unavailable");
        page.Markup.Should().Contain("runs in another process");
        page.Markup.Should().NotContain("No execution history yet",
            "a scheduler that keeps a history this page cannot read has not been shown to have run nothing");
        page.Markup.Should().NotContain("qz-alert-error",
            "nothing failed: the target answered, and what it said is that it has no history route");
    }

    [Test]
    public void AFailureThatIsGoingToBeRetriedIsNotTheSameRowAsOneThatGaveUp()
    {
        GivenHistory(
            Failed(retryAttempt: 0, retryScheduled: true),
            Failed(retryAttempt: 1, retryScheduled: false),
            Entry(100));

        IRenderedComponent<History> page = context.Render<History>();

        page.TextOfAll(".qz-state-label").Should().Equal(["Failed (retrying)", "Failed", "Succeeded"],
            "a page that called both failures the same thing made a job under a retry policy look "
            + "several times as broken as it was");
    }

    [Test]
    public void RunAgainIsOfferedOnlyWhereTheOccurrenceGaveUp()
    {
        GivenHistory(
            Failed(retryAttempt: 0, retryScheduled: true),
            Failed(retryAttempt: 1, retryScheduled: false),
            Entry(100));

        IRenderedComponent<History> page = context.Render<History>();

        page.FindAll("[data-testid=history-run-again]").Should().HaveCount(1,
            "a row the trigger is about to retry already has another attempt coming, and a success has "
            + "nothing to run again");
    }

    [Test]
    public void RunAgainFiresTheJobAndIsRecordedInTheActionLog()
    {
        GivenHistory(Failed(retryAttempt: 1, retryScheduled: false));

        IRenderedComponent<History> page = context.Render<History>();
        page.Find("[data-testid=history-run-again]").Click();

        A.CallTo(() => context.Api.TriggerJob(
                TestData.SchedulerName,
                A<JobKeyDto>.That.Matches(key => key.Group == "DummyGroup" && key.Name == "DummyJob"),
                null,
                A<CancellationToken>._))
            .MustHaveHappened();

        context.ActionLog.GetLatest().Should().ContainSingle(
                "every mutation the dashboard makes is recorded, and this one is a mutation like any other")
            .Which.Should().Match<DashboardActionLogEntry>(
                entry => entry.Action == "TriggerJob" && entry.Target == "DummyGroup.DummyJob" && entry.Succeeded);
    }

    /// <summary>
    /// Run again reads the row by its key, because a listing leaves the input out, and fires the job with
    /// the input the failed run had.
    /// </summary>
    [Test]
    public void RunAgainPassesTheRecordedInputBack()
    {
        const string input = "{\"invoiceId\":42}";
        DashboardHistoryEntry listed = Failed(retryAttempt: 1, retryScheduled: false) with { EntryId = "entry-1" };
        GivenHistory(listed);
        A.CallTo(() => context.Api.GetExecution(TestData.SchedulerName, "entry-1", A<CancellationToken>._))
            .Returns(listed with { Input = input });

        IRenderedComponent<History> page = context.Render<History>();
        page.Find("[data-testid=history-run-again]").Click();

        page.WaitForAssertion(() => A.CallTo(() => context.Api.TriggerJob(
                TestData.SchedulerName,
                A<JobKeyDto>.That.Matches(key => key.Group == "DummyGroup" && key.Name == "DummyJob"),
                A<JobDataMap?>.That.Matches(data => data != null && data.GetString(SchedulerConstants.JobInput) == input),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());

        context.ActionLog.GetLatest().Should().ContainSingle()
            .Which.Action.Should().Be("TriggerJobWithData", "the run carries data, as Job Detail's Trigger with data does");
        context.Toasts.Messages.Should().ContainSingle()
            .Which.Message.Should().Be("Triggered job DummyGroup.DummyJob with the original input.");
    }

    /// <summary>
    /// A row the history recorded no input for fires the job with no map, as Run again always did.
    /// </summary>
    [Test]
    public void RunAgainWithoutARecordedInputIsAPlainTriggerJob()
    {
        DashboardHistoryEntry listed = Failed(retryAttempt: 1, retryScheduled: false) with { EntryId = "entry-1" };
        GivenHistory(listed);
        A.CallTo(() => context.Api.GetExecution(TestData.SchedulerName, "entry-1", A<CancellationToken>._))
            .Returns(listed);

        IRenderedComponent<History> page = context.Render<History>();
        page.Find("[data-testid=history-run-again]").Click();

        page.WaitForAssertion(() => A.CallTo(() => context.Api.TriggerJob(
                TestData.SchedulerName, A<JobKeyDto>._, null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());

        context.ActionLog.GetLatest().Should().ContainSingle().Which.Action.Should().Be("TriggerJob");
        context.Toasts.Messages.Should().ContainSingle()
            .Which.Message.Should().Be("Triggered job DummyGroup.DummyJob without input.",
                "the operator is told the job ran without the input, rather than left to assume it had it");
    }

    /// <summary>
    /// A row whose input was too large to keep says so on the button and in the toast, and is fired
    /// without asking for an input it knows is not there.
    /// </summary>
    [Test]
    public void RunAgainOfARowWhoseInputWasTooLargeSaysSo()
    {
        GivenHistory(Failed(retryAttempt: 1, retryScheduled: false) with { EntryId = "entry-1", InputTooLarge = true });

        IRenderedComponent<History> page = context.Render<History>();
        IElement button = page.Find("[data-testid=history-run-again]");
        button.GetAttribute("title").Should().Contain("without input").And.Contain("too large");

        button.Click();

        page.WaitForAssertion(() => A.CallTo(() => context.Api.TriggerJob(
                TestData.SchedulerName, A<JobKeyDto>._, null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());

        A.CallTo(() => context.Api.GetExecution(A<string>._, A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
        context.Toasts.Messages.Should().ContainSingle()
            .Which.Message.Should().Be("Triggered job DummyGroup.DummyJob without input: the run's input was too large for the history to keep.");
    }

    /// <summary>
    /// A listing that carries the input, as the in-memory history's does, is not read again.
    /// </summary>
    [Test]
    public void RunAgainUsesAnInputTheListingAlreadyCarries()
    {
        GivenHistory(Failed(retryAttempt: 1, retryScheduled: false) with { EntryId = "entry-1", Input = "\"listed\"" });

        IRenderedComponent<History> page = context.Render<History>();
        IElement button = page.Find("[data-testid=history-run-again]");
        button.GetAttribute("title").Should().Be("Fires the job again with the original input");

        button.Click();

        page.WaitForAssertion(() => A.CallTo(() => context.Api.TriggerJob(
                TestData.SchedulerName,
                A<JobKeyDto>._,
                A<JobDataMap?>.That.Matches(data => data != null && data.GetString(SchedulerConstants.JobInput) == "\"listed\""),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());

        A.CallTo(() => context.Api.GetExecution(A<string>._, A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// A row trimmed from the history since the listing was read has no input left to give.
    /// </summary>
    [Test]
    public void RunAgainOfARowTrimmedSinceTheListingFiresWithoutInput()
    {
        GivenHistory(Failed(retryAttempt: 1, retryScheduled: false) with { EntryId = "entry-1" });
        A.CallTo(() => context.Api.GetExecution(A<string>._, A<string>._, A<CancellationToken>._))
            .Returns((DashboardHistoryEntry?) null);

        IRenderedComponent<History> page = context.Render<History>();
        page.Find("[data-testid=history-run-again]").GetAttribute("title").Should().Contain("when the history recorded one");
        page.Find("[data-testid=history-run-again]").Click();

        page.WaitForAssertion(() => A.CallTo(() => context.Api.TriggerJob(
                TestData.SchedulerName, A<JobKeyDto>._, null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());
    }

    /// <summary>
    /// A target that serves no single executions has no input to give, so Run again fires the job as before.
    /// </summary>
    [Test]
    public void RunAgainAgainstATargetWithoutSingleExecutionsFiresWithoutInput()
    {
        GivenHistory(Failed(retryAttempt: 1, retryScheduled: false) with { EntryId = "entry-1" });
        A.CallTo(() => context.Api.GetExecution(A<string>._, A<string>._, A<CancellationToken>._))
            .Throws(new NotSupportedException("the target does not serve single executions"));

        IRenderedComponent<History> page = context.Render<History>();
        page.Find("[data-testid=history-run-again]").Click();

        page.WaitForAssertion(() => A.CallTo(() => context.Api.TriggerJob(
                TestData.SchedulerName, A<JobKeyDto>._, null, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());

        context.Toasts.Messages.Should().ContainSingle().Which.Message.Should().EndWith("without input.");
    }

    [Test]
    public void ReadOnlyModeOffersNoWayToRunAnythingAgain()
    {
        context.Options.ReadOnly = true;
        GivenHistory(Failed(retryAttempt: 1, retryScheduled: false));

        IRenderedComponent<History> page = context.Render<History>();

        page.FindAll("[data-testid=history-run-again]").Should().BeEmpty();
        page.TextOfAll("th").Should().NotContain("Actions",
            "an empty column reads as a rendering fault rather than as a policy");
    }

    [Test]
    public void TheOutcomeFilterAsksTheStoreForTheOccurrencesThatGaveUp()
    {
        GivenHistory(Failed(retryAttempt: 1, retryScheduled: false));

        context.Navigate("/quartz/history?outcome=failed");

        IRenderedComponent<History> page = context.Render<History>();

        A.CallTo(() => context.Api.QueryExecutions(
                A<DashboardHistoryQuery>.That.Matches(query => query.FailedFinally == true),
                A<CancellationToken>._))
            .MustHaveHappened();

        page.Markup.Should().Contain("Failed after retries",
            "the summary above the table says what the listing is narrowed to");
    }

    [Test]
    public void AnUnfilteredListingAsksForEveryOutcome()
    {
        GivenHistory(Entry(100));

        IRenderedComponent<History> page = context.Render<History>();

        A.CallTo(() => context.Api.QueryExecutions(
                A<DashboardHistoryQuery>.That.Matches(query => query.FailedFinally == null),
                A<CancellationToken>._))
            .MustHaveHappened();

        page.Markup.Should().Contain("Any outcome");
    }

    [Test]
    public void ChoosingAnOutcomePutsItInTheUrlSoTheViewCanBeShared()
    {
        GivenHistory(Failed(retryAttempt: 1, retryScheduled: false));

        IRenderedComponent<History> page = context.Render<History>();
        page.Find("#history-outcome-filter").Change("failed");

        context.CurrentUri.Should().EndWith("/quartz/history?outcome=failed",
            "the filters are query parameters so a narrowed listing is a link someone can send");
    }

    [Test]
    public void AnOutcomeNobodyRecognisesNarrowsNothing()
    {
        GivenHistory(Entry(100));

        context.Navigate("/quartz/history?outcome=sideways");

        IRenderedComponent<History> page = context.Render<History>();

        // A hand-edited URL, or a link from a version that knows another value, reads as no filter
        // rather than as an empty page.
        A.CallTo(() => context.Api.QueryExecutions(
                A<DashboardHistoryQuery>.That.Matches(query => query.FailedFinally == null),
                A<CancellationToken>._))
            .MustHaveHappened();

        page.Markup.Should().Contain("Any outcome");
    }

    [Test]
    public void EachRowSaysWhatItsRunAchieved()
    {
        GivenHistory(
            Run(JobRunResult.Succeeded),
            Run(JobRunResult.Skipped),
            Run(JobRunResult.Failed) with { RetryScheduled = true },
            Run(JobRunResult.Failed),
            Run(JobRunResult.Cancelled),
            Entry(100, succeeded: false, exceptionMessage: "a 4.3 row"));

        IRenderedComponent<History> page = context.Render<History>();

        page.TextOfAll(".qz-state-label").Should().Equal(
            ["Succeeded", "Skipped", "Failed (retrying)", "Failed", "Cancelled", "Failed"],
            "a run that found nothing to do and one that was stopped are neither a success nor a fault, and a row written "
            + "before 4.4 still says what its success implies");
        page.FindAll(".qz-state-indicator").Single(indicator => indicator.TextContent.Contains("Cancelled", StringComparison.Ordinal))
            .ClassList.Should().Contain("qz-state-paused", "a stopped run wears the amber a paused thing does, not a fault's red");
    }

    [Test]
    public void ARowShowsItsSummaryItsMetricsAndWhetherItWasAskedForByHand()
    {
        GivenHistory(Run(JobRunResult.Skipped) with
        {
            Summary = "no stale reservations",
            MetricsJson = """{"scanned":1200,"note":"café","ok":true}""",
            Manual = true
        });

        IRenderedComponent<History> page = context.Render<History>();

        page.Find(".qz-history-summary-text").TextContent.Should().Be("no stale reservations");
        page.TextOfAll(".qz-metric-chip").Should().Equal(["scanned: 1200", "note: café", "ok: true"],
            "each metric reads as its name and its value, a string without its quotes");
        page.Find(".qz-metric-chip").GetAttribute("title").Should().Be("scanned: 1200",
            "a chip stays on one line and a long one is cut with an ellipsis, so its title carries the whole of it");
        page.FindAll("[data-testid=history-manual]").Should().ContainSingle("the run was asked for, not fired by a schedule");
    }

    [Test]
    public void TheResultFilterAsksForTheResultsTheUrlNames()
    {
        GivenHistory(Run(JobRunResult.Failed));

        context.Navigate("/quartz/history?result=failed,CANCELLED,sideways");

        IRenderedComponent<History> page = context.Render<History>();

        A.CallTo(() => context.Api.QueryExecutions(
                A<DashboardHistoryQuery>.That.Matches(query =>
                    query.Results != null && query.Results.SequenceEqual(new[] { JobRunResult.Failed, JobRunResult.Cancelled })),
                A<CancellationToken>._))
            .MustHaveHappened();

        page.Find("#history-result-failed").HasAttribute("checked").Should().BeTrue();
        page.Find("#history-result-succeeded").HasAttribute("checked").Should().BeFalse();
        page.Markup.Should().Contain("Results: Failed, Cancelled",
            "a result nobody recognises is dropped, as an unknown outcome is, rather than emptying the page");
    }

    [Test]
    public void TickingAResultPutsItInTheUrlSoTheViewCanBeShared()
    {
        GivenHistory(Run(JobRunResult.Skipped));

        IRenderedComponent<History> page = context.Render<History>();
        page.Find("#history-result-skipped").Change(true);

        page.WaitForAssertion(() => context.CurrentUri.Should().EndWith("/quartz/history?result=skipped",
            "the filters are query parameters so a narrowed listing is a link someone can send"));
    }

    [Test]
    public void TheResultsAreWrittenInTheOrderThePageOffersThem()
    {
        GivenHistory(Run(JobRunResult.Failed));
        context.Navigate("/quartz/history?result=failed");

        IRenderedComponent<History> page = context.Render<History>();
        page.Find("#history-result-skipped").Change(true);

        page.WaitForAssertion(() => context.CurrentUri.Should().EndWith("/quartz/history?result=skipped,failed",
            "one set has one spelling, whatever order it was ticked in, so two links to the same view are the same link"));
    }

    [Test]
    public void UntickingTheLastResultListsEveryResultAgain()
    {
        GivenHistory(Run(JobRunResult.Failed));
        context.Navigate("/quartz/history?result=failed");

        IRenderedComponent<History> page = context.Render<History>();
        page.Find("#history-result-failed").Change(false);

        page.WaitForAssertion(() => context.CurrentUri.Should().EndWith("/quartz/history",
            "no box ticked is every result, and not a page that matches nothing"));
    }

    [Test]
    public void TheExactJobFilterAsksForOneJobAndItsMisfires()
    {
        GivenHistory(Run(JobRunResult.Succeeded));

        context.Navigate("/quartz/history?jobGroup=billing&jobName=release-stale");

        IRenderedComponent<History> page = context.Render<History>();

        A.CallTo(() => context.Api.QueryExecutions(
                A<DashboardHistoryQuery>.That.Matches(query => query.Job == new JobKeyDto("billing", "release-stale")),
                A<CancellationToken>._))
            .MustHaveHappened();
        A.CallTo(() => context.Api.QueryMisfires(
                A<DashboardMisfireQuery>.That.Matches(query => query.Job == new JobKeyDto("billing", "release-stale")),
                A<CancellationToken>._))
            .MustHaveHappened();

        page.Find("[data-testid=history-exact-job]").TextContent.Should().Contain("billing.release-stale");
        page.Markup.Should().Contain("Job: billing.release-stale only");
    }

    [Test]
    public void TheExactJobFilterIsClearedFromTheUrl()
    {
        GivenHistory(Run(JobRunResult.Succeeded));
        context.Navigate("/quartz/history?outcome=failed&jobGroup=billing&jobName=release-stale");

        IRenderedComponent<History> page = context.Render<History>();
        page.Find("[data-testid=history-exact-job-clear]").Click();

        page.WaitForAssertion(() => context.CurrentUri.Should().EndWith("/quartz/history?outcome=failed",
            "clearing the job keeps every other filter the reader chose"));
    }

    [Test]
    public void AVetoIsToldApartFromAMisfireAndAnOverlap()
    {
        GivenHistory(Entry(100));
        GivenMisfires(
            TestData.Dashboard.MisfireEntry("vetoed") with { Reason = MisfireReason.Vetoed },
            TestData.Dashboard.MisfireEntry("skipped") with { Reason = MisfireReason.Overlap },
            TestData.Dashboard.MisfireEntry("missed"));

        IRenderedComponent<History> page = context.Render<History>();

        page.TextOfAll(".qz-misfire-reason").Should().Equal(["Vetoed", "Overlap", "Misfire"],
            "a listener that refused a firing is a decision someone wrote, not a scheduler that fell behind");
        A.CallTo(() => context.Api.QueryMisfires(
                A<DashboardMisfireQuery>.That.Matches(query => query.Reasons == null),
                A<CancellationToken>._))
            .MustHaveHappened();
    }

    /// <summary>
    /// A scheduler in another process whose host is older than 4.4 serves its history but not the 4.4
    /// filters, and the page says that rather than that it serves no history.
    /// </summary>
    [Test]
    public void ATargetThatCannotFilterSaysSoRatherThanThatItHasNoHistory()
    {
        A.CallTo(() => context.Api.QueryExecutions(
                A<DashboardHistoryQuery>.That.Matches(query => query.Results != null),
                A<CancellationToken>._))
            .Throws(new NotSupportedException("its host runs Quartz 4.3.0.0, whose history routes ignore the result filter"));

        context.Navigate("/quartz/history?result=failed");

        IRenderedComponent<History> page = context.Render<History>();

        page.Find("[data-testid=history-filter-unsupported]").TextContent.Should().Contain("4.3.0.0");
        page.FindAll("[data-testid=history-unavailable]").Should().BeEmpty(
            "the target serves history; what it cannot do is filter it, and clearing the filter shows it");
        page.Find("#history-result-failed").Should().NotBeNull("the filter stays there to be cleared");
    }

    private static DashboardHistoryEntry Run(JobRunResult result)
    {
        return Entry(100, succeeded: result is JobRunResult.Succeeded or JobRunResult.Skipped) with { Result = result };
    }

    private static DashboardHistoryEntry Entry(
        int durationMilliseconds,
        bool succeeded = true,
        string? exceptionMessage = null,
        string node = TestData.SchedulerInstanceId)
    {
        return TestData.Dashboard.HistoryEntry(
            TimeSpan.FromMilliseconds(durationMilliseconds),
            succeeded,
            exceptionMessage: exceptionMessage,
            schedulerInstanceId: node);
    }

    /// <summary>One failed attempt at an occurrence, which may or may not have another coming.</summary>
    private static DashboardHistoryEntry Failed(int retryAttempt, bool retryScheduled)
    {
        return Entry(100, succeeded: false, exceptionMessage: "the upstream system is down") with
        {
            RetryAttempt = retryAttempt,
            RetryScheduled = retryScheduled
        };
    }

    private void GivenHistory(params DashboardHistoryEntry[] entries)
    {
        GivenHistory(entries.Length, entries);
    }

    private void GivenHistory(int totalCount, params DashboardHistoryEntry[] entries)
    {
        A.CallTo(() => context.Api.QueryExecutions(A<DashboardHistoryQuery>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.Page<DashboardHistoryEntry>(entries, totalCount));
    }

    /// <remarks>
    /// Left unstubbed the misfire feed answers the empty page <see cref="DashboardComponentContext" />
    /// sets up, so a test that says nothing about misfires renders the section with nothing in it and
    /// the tests above are unaffected by one.
    /// </remarks>
    private void GivenMisfires(params DashboardMisfireEntry[] entries)
    {
        A.CallTo(() => context.Api.QueryMisfires(A<DashboardMisfireQuery>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.Page<DashboardMisfireEntry>(entries));
    }
}
