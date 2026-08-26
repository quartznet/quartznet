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
