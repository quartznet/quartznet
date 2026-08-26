using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

using DashboardPage = Quartz.Dashboard.Components.Pages.Dashboard;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The overview page: the four tiles, the scheduler's status, and what read-only mode takes away.
/// </summary>
public class DashboardPageTest
{
    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
        context.WithScheduler();
        GivenCounts(jobs: 0, triggers: 0, errorTriggers: 0, executing: 0);
        GivenNodes(Node(TestData.SchedulerInstanceId, ClusterNodeState.Alive));
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    [TestCase(SchedulerStatus.Running, "qz-state-running")]
    [TestCase(SchedulerStatus.Standby, "qz-state-standby")]
    [TestCase(SchedulerStatus.ShuttingDown, "qz-state-shutting-down")]
    [TestCase(SchedulerStatus.Shutdown, "qz-state-shutdown")]
    [TestCase(SchedulerStatus.Created, "qz-state-created")]
    public void TheSchedulersStatusIsShownWithTheOneMapping(SchedulerStatus status, string expectedModifier)
    {
        context.WithScheduler(status: status);

        IRenderedComponent<DashboardPage> page = context.Render<DashboardPage>();

        page.SchedulerStatusModifier().Should().Be(expectedModifier,
            "the overview and the header read the same scheduler, so they have to agree about its colour");
    }

    [Test]
    public void TheTilesShowTheTotalsRatherThanTheItemsOnAPage()
    {
        GivenCounts(jobs: 137, triggers: 250, errorTriggers: 3, executing: 2);

        IRenderedComponent<DashboardPage> page = context.Render<DashboardPage>();

        page.StatCardValue("Total Jobs").Should().Be("137");
        page.StatCardValue("Total Triggers").Should().Be("250");
        page.StatCardValue("Currently Executing").Should().Be("2");
        page.StatCardValue("Error Triggers").Should().Be("3");

        A.CallTo(() => context.Api.GetJobs(
                TestData.SchedulerName,
                A<DashboardJobQuery>.That.Matches(query => query.Take == 0),
                A<CancellationToken>._))
            .MustHaveHappened();
    }

    /// <summary>
    /// The Nodes tile counts what the cluster has, and says how much of it is not answering.
    /// </summary>
    /// <remarks>
    /// A bare count is no news — a four-node cluster has four nodes on its best day and on its worst.
    /// What an operator glances for is the second number, so it is in the tile's value rather than a
    /// click away, and the tile turns red to earn the glance.
    /// </remarks>
    [Test]
    public void TheNodesTileNamesTheNodesThatAreNotAnswering()
    {
        GivenNodes(
            Node("node-a", ClusterNodeState.Alive),
            Node("node-b", ClusterNodeState.Overdue),
            Node("node-c", ClusterNodeState.Failed));

        IRenderedComponent<DashboardPage> page = context.Render<DashboardPage>();

        page.StatCardValue("Nodes").Should().Be("3 (2 overdue/failed)");
        page.Find(".qz-stat-card-link .qz-stat-card").ClassList.Should().Contain("qz-stat-card-error",
            "a cluster with a node that stopped checking in is not an informational fact");
    }

    [Test]
    public void TheNodesTileIsACountAloneWhenEveryNodeIsAlive()
    {
        GivenNodes(Node("node-a", ClusterNodeState.Alive), Node("node-b", ClusterNodeState.Alive));

        IRenderedComponent<DashboardPage> page = context.Render<DashboardPage>();

        page.StatCardValue("Nodes").Should().Be("2", "a healthy cluster has nothing to qualify");
        page.Find(".qz-stat-card-link").GetAttribute("href").Should().Be("quartz/cluster",
            "the tile is the way to the page that explains it");
    }

    [Test]
    public void ReadOnlyModeHidesTheSchedulerControls()
    {
        context.Options.ReadOnly = true;

        IRenderedComponent<DashboardPage> page = context.Render<DashboardPage>();

        page.HasButton("Start").Should().BeFalse();
        page.HasButton("Standby").Should().BeFalse();
        page.HasButton("Pause all").Should().BeFalse();
        page.HasButton("Shutdown").Should().BeFalse();
        page.StatCardValue("Total Jobs").Should().Be("0", "a read-only dashboard still reports");
    }

    [Test]
    public void StandingTheSchedulerDownRecordsTheActionAndSaysSo()
    {
        IRenderedComponent<DashboardPage> page = context.Render<DashboardPage>();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Standby").Click();

        A.CallTo(() => context.Api.StandbyScheduler(TestData.SchedulerName, A<CancellationToken>._)).MustHaveHappened();
        context.Toasts.Messages.Should().ContainSingle().Which.Message.Should().Be("Scheduler is in standby.");
        context.ActionLog.GetLatest(1).Should().ContainSingle()
            .Which.Action.Should().Be("StandbyScheduler");
    }

    [Test]
    public void ShuttingDownAsksFirst()
    {
        IRenderedComponent<DashboardPage> page = context.Render<DashboardPage>();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Shutdown").Click();
        A.CallTo(() => context.Api.ShutdownScheduler(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
        page.Markup.Should().Contain("Running jobs may be interrupted",
            "shutting a scheduler down is not undone by clicking Start again");

        page.Find(".qz-confirm-dialog button.qz-button-danger").Click();

        A.CallTo(() => context.Api.ShutdownScheduler(TestData.SchedulerName, A<CancellationToken>._)).MustHaveHappened();
    }

    [Test]
    public void OnlyTheActiveSchedulersActionsAreListed()
    {
        context.ActionLog.Record(TestData.SchedulerName, "PauseJob", "reports.job-1", succeeded: true);
        context.ActionLog.Record("other", "PauseJob", "elsewhere.job-1", succeeded: true);

        IRenderedComponent<DashboardPage> page = context.Render<DashboardPage>();

        page.Markup.Should().Contain("reports.job-1");
        page.Markup.Should().NotContain("elsewhere.job-1",
            "the log is process-wide, and an action taken against another scheduler is not this page's history");
    }

    [Test]
    public void WithNoSchedulerSelectedThePageShowsZerosRatherThanFailing()
    {
        context.SchedulerState.ActiveSchedulerName = null;

        IRenderedComponent<DashboardPage> page = context.Render<DashboardPage>();

        page.StatCardValue("Total Jobs").Should().Be("0");
        page.SchedulerStatusModifier().Should().Be("qz-state-waiting",
            "no scheduler selected is not a scheduler in some state, which is what Unknown means");
    }

    /// <summary>
    /// The page an operator lands on when the scheduler they picked is a registration nothing has built.
    /// </summary>
    /// <remarks>
    /// The listing carries such a registration now, so it can be the active scheduler — after shutting
    /// the only one down, or by following it from the Schedulers page. Every read this page makes would
    /// answer "no such scheduler", which reads as a fault rather than as the state the listing already
    /// reported.
    /// </remarks>
    [Test]
    public void ASchedulerNothingHasBuiltSaysSoRatherThanReportingAFault()
    {
        context.SchedulerState.AvailableSchedulers = new List<SchedulerHeaderDto>
        {
            TestData.Dashboard.RegisteredSchedulerHeader("acme")
        };
        context.SchedulerState.ActiveSchedulerName = "acme";

        IRenderedComponent<DashboardPage> page = context.Render<DashboardPage>();

        page.Markup.Should().Contain("registered but has not been created");
        page.FindAll(".qz-stat-card").Should().BeEmpty("there is nothing to count");
        A.CallTo(() => context.Api.GetScheduler("acme", A<CancellationToken>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// The histogram breaks the triggers down by state, each count its own <c>Take = 0</c> query.
    /// </summary>
    /// <remarks>
    /// Four numbers used to be all the overview had, and none of them said how many triggers were
    /// paused, blocked or finished — the three answers to "why is nothing running" that the page was
    /// most often opened for.
    /// </remarks>
    [Test]
    public void TheHistogramCountsTheTriggersInEachState()
    {
        GivenTriggerStates(
            (TriggerState.Normal, 40),
            (TriggerState.Paused, 7),
            (TriggerState.Blocked, 2),
            (TriggerState.Error, 3),
            (TriggerState.Complete, 11));

        IRenderedComponent<DashboardPage> page = context.Render<DashboardPage>();

        HistogramCounts(page).Should().Equal(["40", "7", "2", "3", "11"]);
        page.StatCardValue("Error Triggers").Should().Be("3",
            "the tile and the histogram row are the same count read once, so they cannot disagree");

        foreach (TriggerState state in new[] { TriggerState.Normal, TriggerState.Paused, TriggerState.Blocked, TriggerState.Error, TriggerState.Complete })
        {
            A.CallTo(() => context.Api.GetTriggers(
                    TestData.SchedulerName,
                    A<DashboardTriggerQuery>.That.Matches(query => query.Take == 0 && query.State == state),
                    A<CancellationToken>._))
                .MustHaveHappened();
        }
    }

    [Test]
    public void EachHistogramCountLinksToTheTriggersBehindIt()
    {
        GivenTriggerStates((TriggerState.Paused, 7));

        IRenderedComponent<DashboardPage> page = context.Render<DashboardPage>();

        page.TextOfAll("a").Should().NotBeEmpty();
        page.FindAll("tbody a").Select(link => link.GetAttribute("href")).Should().Contain(
            "quartz/triggers?state=Paused",
            "a count nobody can follow is a count nobody can act on");
    }

    /// <summary>
    /// Pausing a trigger group and pausing a job group are different acts with the same consequence, and
    /// the tile reports both.
    /// </summary>
    [Test]
    public void ThePausedGroupsTileCountsBothKindsOfGroup()
    {
        GivenPausedGroups(triggerGroups: 2, jobGroups: 1);

        IRenderedComponent<DashboardPage> page = context.Render<DashboardPage>();

        page.StatCardValue("Paused Groups").Should().Be("2 trigger, 1 job");
        page.FindAll(".qz-stat-card-warning").Should().NotBeEmpty(
            "a paused group is why work is not being done, which is not an informational fact");

        A.CallTo(() => context.Api.GetTriggerGroups(
                TestData.SchedulerName,
                A<DashboardGroupQuery>.That.Matches(query => query.Take == 0 && query.Paused == true),
                A<CancellationToken>._))
            .MustHaveHappened();
        A.CallTo(() => context.Api.GetJobGroups(
                TestData.SchedulerName,
                A<DashboardGroupQuery>.That.Matches(query => query.Take == 0 && query.Paused == true),
                A<CancellationToken>._))
            .MustHaveHappened();
    }

    [Test]
    public void NoPausedGroupsIsAnInformationalTileRatherThanAWarning()
    {
        IRenderedComponent<DashboardPage> page = context.Render<DashboardPage>();

        page.StatCardValue("Paused Groups").Should().Be("0 trigger, 0 job");
        page.FindAll(".qz-stat-card-warning").Should().BeEmpty();
    }

    /// <summary>
    /// The misfire tile counts over the history store's own retention window, and says which window
    /// that is.
    /// </summary>
    /// <remarks>
    /// A fixed "last 24 h" would overstate a store configured to remember an hour: the count could never
    /// exceed what is retained, so the label would be promising a day it had already forgotten.
    /// </remarks>
    [Test]
    public void TheMisfireTileCountsOverTheWindowTheHistoryStoreKeeps()
    {
        A.CallTo(() => context.Api.CountMisfires(A<string>._, A<DateTimeOffset>._, A<CancellationToken>._)).Returns(4);

        IRenderedComponent<DashboardPage> page = context.Render<DashboardPage>();

        page.StatCardValue("Misfires (last 24 h)").Should().Be("4");
        page.FindAll(".qz-stat-card-warning").Should().NotBeEmpty(
            "a scheduler that has missed four firings is not an informational fact");
    }

    [Test]
    public void TheMisfireTileNamesTheRetentionWindowItWasConfiguredWith()
    {
        context.Options.HistoryRetention = TimeSpan.FromMinutes(30);
        A.CallTo(() => context.Api.CountMisfires(A<string>._, A<DateTimeOffset>._, A<CancellationToken>._)).Returns(0);

        IRenderedComponent<DashboardPage> page = context.Render<DashboardPage>();

        page.StatCardValue("Misfires (last 30 min)").Should().Be("0");
    }

    /// <summary>
    /// A data source with no misfire feed has not looked, which is not the same as having looked and
    /// found none.
    /// </summary>
    [Test]
    public void ADataSourceThatKeepsNoMisfiresSaysSoRatherThanReportingZero()
    {
        A.CallTo(() => context.Api.CountMisfires(A<string>._, A<DateTimeOffset>._, A<CancellationToken>._))
            .Returns((int?) null);

        IRenderedComponent<DashboardPage> page = context.Render<DashboardPage>();

        page.StatCardValue("Misfires (last 24 h)").Should().Be("—",
            "a dashboard that says '0 misfires' when it never counted is worse than one that says "
            + "nothing");
    }

    [Test]
    public void TheOverviewCarriesTheExecutionGroupPanel()
    {
        IRenderedComponent<DashboardPage> page = context.Render<DashboardPage>();

        page.TextOfAll("h2").Should().Contain("Execution groups",
            "the limits execution-groups.md teaches were configurable and invisible");
        A.CallTo(() => context.Api.GetExecutionLimits(TestData.SchedulerName, A<CancellationToken>._))
            .MustHaveHappened();
    }

    /// <summary>
    /// The counts each state row shows, in the order the histogram lists them.
    /// </summary>
    private static List<string> HistogramCounts(IRenderedComponent<DashboardPage> page)
    {
        List<string> counts = [];
        foreach (IElement row in page.FindAll("tbody tr"))
        {
            IElement? count = row.QuerySelector("a");
            if (count is not null)
            {
                counts.Add(count.TextContent.Trim());
            }
        }

        return counts;
    }

    private void GivenTriggerStates(params (TriggerState State, int Count)[] states)
    {
        Dictionary<TriggerState, int> byState = new();
        foreach ((TriggerState state, int count) in states)
        {
            byState[state] = count;
        }

        A.CallTo(() => context.Api.GetTriggers(A<string>._, A<DashboardTriggerQuery>._, A<CancellationToken>._))
            .ReturnsLazily((string _, DashboardTriggerQuery query, CancellationToken _) =>
                new PagedResult<TriggerHeaderDto>(
                    [],
                    HasMore: false,
                    TotalCount: query.State is { } state && byState.TryGetValue(state, out int count) ? count : 0));
    }

    private void GivenPausedGroups(int triggerGroups, int jobGroups)
    {
        A.CallTo(() => context.Api.GetTriggerGroups(A<string>._, A<DashboardGroupQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerGroupDto>([], HasMore: false, TotalCount: triggerGroups));
        A.CallTo(() => context.Api.GetJobGroups(A<string>._, A<DashboardGroupQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<JobGroupDto>([], HasMore: false, TotalCount: jobGroups));
    }

    private void GivenCounts(int jobs, int triggers, int errorTriggers, int executing)
    {
        A.CallTo(() => context.Api.GetJobs(A<string>._, A<DashboardJobQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<JobKeyDto>([], HasMore: false, TotalCount: jobs));
        A.CallTo(() => context.Api.GetTriggers(A<string>._, A<DashboardTriggerQuery>._, A<CancellationToken>._))
            .ReturnsLazily((string _, DashboardTriggerQuery query, CancellationToken _) =>
                new PagedResult<TriggerHeaderDto>(
                    [],
                    HasMore: false,
                    TotalCount: query.State == TriggerState.Error ? errorTriggers : triggers));
        A.CallTo(() => context.Api.GetFireInstances(A<string>._, A<DashboardFireInstanceQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<FireInstanceDto>([], HasMore: false, TotalCount: executing));
    }

    private void GivenNodes(params ClusterNodeDto[] nodes)
    {
        A.CallTo(() => context.Api.GetClusterNodes(A<string>._, A<CancellationToken>._))
            .Returns(nodes.ToList());
    }

    private static ClusterNodeDto Node(string instanceId, ClusterNodeState state)
    {
        return new ClusterNodeDto(
            instanceId,
            TestData.Dashboard.FiredAt,
            TimeSpan.FromSeconds(15),
            state,
            IsCurrentNode: instanceId == TestData.SchedulerInstanceId);
    }
}
