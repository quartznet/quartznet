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
    public void AStoreThatKeepsNoHistorySaysSoInsteadOfShowingAnEmptyPage()
    {
        A.CallTo(() => context.Api.GetHistory(A<DashboardHistoryQuery>._, A<CancellationToken>._))
            .Returns((PagedResult<DashboardHistoryEntry>?) null);

        IRenderedComponent<History> page = context.Render<History>();

        page.Markup.Should().Contain("Execution history is unavailable",
            "a null page means the data source keeps no history, which is not the same as an empty one");
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

        A.CallTo(() => context.Api.GetHistory(
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

        A.CallTo(() => context.Api.GetHistory(
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
        A.CallTo(() => context.Api.GetHistory(
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

        A.CallTo(() => context.Api.GetHistory(
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
        A.CallTo(() => context.Api.GetClusterNodes(TestData.SchedulerName, A<CancellationToken>._))
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
    public void ASchedulerWithNoMisfiresSaysSoRatherThanShowingNothing()
    {
        GivenHistory(Entry(100));
        GivenMisfires();

        IRenderedComponent<History> page = context.Render<History>();

        page.Markup.Should().Contain("No misfires recorded",
            "an empty section says the scheduler is healthy; a missing one says nothing at all");
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

    private void GivenHistory(params DashboardHistoryEntry[] entries)
    {
        GivenHistory(entries.Length, entries);
    }

    private void GivenHistory(int totalCount, params DashboardHistoryEntry[] entries)
    {
        A.CallTo(() => context.Api.GetHistory(A<DashboardHistoryQuery>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.Page<DashboardHistoryEntry>(entries, totalCount));
    }

    /// <remarks>
    /// Left unstubbed the fake answers null, which is the "this data source keeps no history" answer —
    /// so a test that says nothing about misfires renders no misfire section, and the tests above are
    /// unaffected by one.
    /// </remarks>
    private void GivenMisfires(params DashboardMisfireEntry[] entries)
    {
        A.CallTo(() => context.Api.GetMisfires(A<DashboardMisfireQuery>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.Page<DashboardMisfireEntry>(entries));
    }
}
