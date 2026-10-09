using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The Schedulers page: the fleet, built or not, and what each one that exists is made of.
/// </summary>
/// <remarks>
/// This is the page that answers "what does this process run" without starting anything, so the two
/// things it must get right are that a registration nothing has built is <em>listed</em> rather than
/// silently missing, and that the reads it makes per scheduler are the ones that have an answer — a
/// node query against an in-memory store costs a round trip to be told there is one node.
/// </remarks>
public class SchedulersPageTest
{
    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    [Test]
    public void ARegistrationNothingHasBuiltIsListedWithNoMetadataRatherThanOmitted()
    {
        GivenSchedulers(TestData.Dashboard.RegisteredSchedulerHeader("acme"));

        IRenderedComponent<Schedulers> page = context.Render<Schedulers>();

        List<string> cells = RowCells(page, rowIndex: 0);
        cells[Scheduler].Should().Be("acme",
            "a tenant the container knows about is a tenant an operator came here to see, whether or not "
            + "anything has built it");
        cells[Status].Should().Contain("Not created");
        cells.Skip(Nodes).Should().AllBe("—",
            "there is no scheduler to read metadata from, and a blank cell reads as a rendering fault");

        A.CallTo(() => context.Api.GetScheduler("acme", A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public void ABuiltSchedulerShowsWhatItIsMadeOf()
    {
        GivenSchedulers(TestData.Dashboard.SchedulerHeader("core"));
        GivenDetail("core", TestData.Dashboard.SchedulerDetail(SchedulerStatus.Running, "core"));

        IRenderedComponent<Schedulers> page = context.Render<Schedulers>();

        List<string> cells = RowCells(page, rowIndex: 0);
        cells[Target].Should().Be("—", "a scheduler of this process is reached through no target");
        cells[Reached].Should().Be("this process");
        cells[Scheduler].Should().Contain("core").And.Contain(TestData.SchedulerInstanceId);
        cells[Status].Should().Contain("Running");
        cells[JobStore].Should().Contain("RAMJobStore").And.Contain("in-memory");
        cells[Threads].Should().Be("10", "the pool size is the number an operator sizes a scheduler by");
        cells[RunningSince].Should().Be("2024-05-06 07:08:09 +00:00", "times are rendered in the zone the user picked");
        cells[JobsExecuted].Should().Be("42");
        cells[Version].Should().Be("4.0.0.0");
    }

    [Test]
    public void OnlyAPersistentClusteredSchedulerIsAskedForItsNodes()
    {
        GivenSchedulers(
            TestData.Dashboard.SchedulerHeader("core"),
            TestData.Dashboard.SchedulerHeader("reporting"));
        GivenDetail("core", TestData.Dashboard.SchedulerDetail(SchedulerStatus.Running, "core", clustered: true, persistent: true));
        GivenDetail("reporting", TestData.Dashboard.SchedulerDetail(SchedulerStatus.Running, "reporting"));
        A.CallTo(() => context.Api.QueryClusterNodes("core", A<CancellationToken>._))
            .Returns(new List<ClusterNodeDto> { Node("node-a", isCurrent: true), Node("node-b", isCurrent: false) });

        IRenderedComponent<Schedulers> page = context.Render<Schedulers>();

        RowCells(page, rowIndex: 0)[Nodes].Should().Be("2");
        RowCells(page, rowIndex: 1)[Nodes].Should().Be("—",
            "a store with no node table answers with the one node it is, so asking is a round trip per "
            + "scheduler for a number that is always one");

        A.CallTo(() => context.Api.QueryClusterNodes("reporting", A<CancellationToken>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// A cluster's nodes are its members, which the row already carries, so the store is not asked.
    /// </summary>
    [Test]
    public void AClusterRowShowsItsTargetAndItsMembersAsNodesWithoutAskingTheStore()
    {
        GivenSchedulers(
            TestData.Dashboard.SchedulerHeader("QuartzScheduler", origin: SchedulerOrigin.Cluster, target: "a+b", members: ["a", "b"]),
            TestData.Dashboard.SchedulerHeader("QuartzScheduler", origin: SchedulerOrigin.Remote, target: "c"));
        GivenDetail("a+b/QuartzScheduler", TestData.Dashboard.SchedulerDetail(SchedulerStatus.Running, "QuartzScheduler", clustered: true, persistent: true));
        GivenDetail("c/QuartzScheduler", TestData.Dashboard.SchedulerDetail(SchedulerStatus.Running, "QuartzScheduler"));

        IRenderedComponent<Schedulers> page = context.Render<Schedulers>();

        List<string> cluster = RowCells(page, rowIndex: 0);
        cluster[Target].Should().Be("a+b");
        cluster[Reached].Should().Be("cluster");
        cluster[Scheduler].Should().Be("QuartzScheduler", "a cluster is every node at once, so no instance id is shown under it");
        cluster[Nodes].Should().Be("2", "the members are the nodes");

        List<string> http = RowCells(page, rowIndex: 1);
        http[Target].Should().Be("c");
        http[Reached].Should().Be("HTTP");

        A.CallTo(() => context.Api.QueryClusterNodes(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
        // The row is read through the cluster's key, which is what resolves it.
        A.CallTo(() => context.Api.GetScheduler("a+b/QuartzScheduler", A<CancellationToken>._)).MustHaveHappened();
    }

    [Test]
    public void RowsAreSortedByTargetWithThisProcessFirstThenByName()
    {
        GivenSchedulers(
            TestData.Dashboard.RegisteredSchedulerHeader("QuartzScheduler", SchedulerOrigin.Remote) with { Target = "w2" },
            TestData.Dashboard.RegisteredSchedulerHeader("zeta"),
            TestData.Dashboard.RegisteredSchedulerHeader("billing", SchedulerOrigin.Remote) with { Target = "w1" },
            TestData.Dashboard.RegisteredSchedulerHeader("alpha"));

        IRenderedComponent<Schedulers> page = context.Render<Schedulers>();

        page.FindAll("tbody tr").Select(row => RowCells(row)[Scheduler]).Should().Equal(["alpha", "zeta", "billing", "QuartzScheduler"],
            "the fleet reads as a list of places, each with what it holds");
    }

    [Test]
    public void FollowingATargetedSchedulersLinkMakesItsKeyTheActiveOne()
    {
        GivenSchedulers(TestData.Dashboard.SchedulerHeader("QuartzScheduler", origin: SchedulerOrigin.Remote, target: "w1"));
        GivenDetail("w1/QuartzScheduler", TestData.Dashboard.SchedulerDetail(SchedulerStatus.Running, "QuartzScheduler"));

        IRenderedComponent<Schedulers> page = context.Render<Schedulers>();
        page.FindAll("tbody tr")[0].QuerySelector("a")!.Click();

        context.SchedulerState.ActiveSchedulerName.Should().Be("w1/QuartzScheduler",
            "the key, not the name, is what the rest of the dashboard addresses the scheduler by");
    }

    private const int Target = 0;
    private const int Reached = 1;
    private const int Scheduler = 2;
    private const int Status = 3;
    private const int Nodes = 4;
    private const int JobStore = 5;
    private const int Threads = 6;
    private const int RunningSince = 7;
    private const int JobsExecuted = 8;
    private const int Version = 9;

    [Test]
    public void FollowingASchedulersLinkMakesItTheActiveOne()
    {
        GivenSchedulers(
            TestData.Dashboard.SchedulerHeader("core"),
            TestData.Dashboard.SchedulerHeader("reporting"));
        GivenDetail("core", TestData.Dashboard.SchedulerDetail(SchedulerStatus.Running, "core"));
        GivenDetail("reporting", TestData.Dashboard.SchedulerDetail(SchedulerStatus.Standby, "reporting"));

        IRenderedComponent<Schedulers> page = context.Render<Schedulers>();
        page.FindAll("tbody tr")[1].QuerySelector("a")!.Click();

        context.SchedulerState.ActiveSchedulerName.Should().Be("reporting",
            "the row is how an operator switches the dashboard to a scheduler, which is the only reason "
            + "to list them all in one place");
    }

    [Test]
    public void ARegistrationNothingHasBuiltIsNotALink()
    {
        GivenSchedulers(TestData.Dashboard.RegisteredSchedulerHeader("acme"));

        IRenderedComponent<Schedulers> page = context.Render<Schedulers>();

        page.FindAll("tbody tr")[0].QuerySelectorAll("a").Should().BeEmpty(
            "there is no scheduler behind the name, so every page the link leads to would report a "
            + "scheduler it could not find");
    }

    /// <summary>
    /// One scheduler that cannot answer must not blank the fleet.
    /// </summary>
    [Test]
    public void ASchedulerThatCannotBeReadIsReportedInItsOwnRow()
    {
        GivenSchedulers(
            TestData.Dashboard.SchedulerHeader("core"),
            TestData.Dashboard.SchedulerHeader("remote"));
        GivenDetail("core", TestData.Dashboard.SchedulerDetail(SchedulerStatus.Running, "core"));
        A.CallTo(() => context.Api.GetScheduler("remote", A<CancellationToken>._))
            .Throws(new InvalidOperationException("the other process is not answering"));

        IRenderedComponent<Schedulers> page = context.Render<Schedulers>();

        RowCells(page, rowIndex: 1)[Status].Should().Contain("the other process is not answering");
        RowCells(page, rowIndex: 0)[JobsExecuted].Should().Be("42",
            "the scheduler that did answer is still shown, which is the whole point of a fleet view");
    }

    [Test]
    public void AProcessWithNoSchedulersSaysSoRatherThanShowingAnEmptyTable()
    {
        GivenSchedulers();

        IRenderedComponent<Schedulers> page = context.Render<Schedulers>();

        page.Markup.Should().Contain("No schedulers registered.");
    }

    private static ClusterNodeDto Node(string instanceId, bool isCurrent)
    {
        return new ClusterNodeDto(
            instanceId,
            TestData.Dashboard.FiredAt,
            TimeSpan.FromSeconds(15),
            ClusterNodeState.Alive,
            isCurrent);
    }

    private static List<string> RowCells(IRenderedComponent<Schedulers> page, int rowIndex)
    {
        return RowCells(page.FindAll("tbody tr")[rowIndex]);
    }

    private static List<string> RowCells(IElement row)
    {
        List<string> cells = [];
        foreach (IElement cell in row.QuerySelectorAll("td"))
        {
            cells.Add(cell.TextContent.Trim());
        }

        return cells;
    }

    private void GivenSchedulers(params SchedulerHeaderDto[] schedulers)
    {
        A.CallTo(() => context.Api.GetSchedulers(A<CancellationToken>._)).Returns(schedulers.ToList());
    }

    private void GivenDetail(string schedulerName, SchedulerDetailDto detail)
    {
        A.CallTo(() => context.Api.GetScheduler(schedulerName, A<CancellationToken>._)).Returns(detail);
    }
}
