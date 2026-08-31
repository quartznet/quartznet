using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The Cluster page: which nodes it lists, what it says about each one's health, and what it counts
/// against them.
/// </summary>
/// <remarks>
/// This is the page an operator opens when a job has stopped running, so the two things it must get
/// right are the verdict — a failed node has to look different from a healthy one at a glance — and the
/// join to the firings, which is the only place the dashboard says which node is holding what.
/// </remarks>
public class ClusterPageTest
{
    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
        context.WithScheduler();
        GivenNodes(CurrentNode());
        GivenFirings();
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    [Test]
    public void EachNodesStateIsShownWithTheColourItsSeverityEarns()
    {
        GivenNodes(
            CurrentNode(ClusterNodeState.Alive),
            Node("node-b", ClusterNodeState.Overdue),
            Node("node-c", ClusterNodeState.Failed));

        IRenderedComponent<Cluster> page = context.Render<Cluster>();

        StateModifiers(page).Should().Equal(["qz-state-success", "qz-state-paused", "qz-state-error"],
            "a node that stopped checking in has to look different from a healthy one without reading "
            + "the label, which is the whole reason someone opens this page");
        page.TextOfAll("tbody td").Should().Contain(["Alive", "Overdue", "Failed"]);
    }

    [Test]
    public void TheNodeAnsweringIsMarkedAsThisOne()
    {
        GivenNodes(CurrentNode(), Node("node-b", ClusterNodeState.Alive));

        IRenderedComponent<Cluster> page = context.Render<Cluster>();

        IElement current = page.FindAll("tbody tr")[0];
        current.TextContent.Should().Contain(TestData.SchedulerInstanceId).And.Contain("(this node)",
            "the listing is answered by one node about the whole cluster, so which one it is decides "
            + "what an action taken here will reach");
        page.FindAll("tbody tr")[1].TextContent.Should().NotContain("(this node)");
    }

    [Test]
    public void TheCountsPerNodeComeFromTheFiringsThatNodeOwns()
    {
        GivenNodes(CurrentNode(), Node("node-b", ClusterNodeState.Failed));
        GivenFirings(
            Firing("node-b", FireInstanceState.Acquired),
            Firing("node-b", FireInstanceState.Acquired),
            Firing("node-b", FireInstanceState.Executing),
            Firing(TestData.SchedulerInstanceId, FireInstanceState.Executing));

        IRenderedComponent<Cluster> page = context.Render<Cluster>();

        RowCells(page, rowIndex: 0).Should().EndWith(["0", "1"],
            "this node holds nothing and is running one firing");
        RowCells(page, rowIndex: 1).Should().EndWith(["2", "1"],
            "the dead node's two reservations are exactly the residue recovery is about to clear, so "
            + "they belong beside its verdict rather than being folded into the running count");
    }

    [Test]
    public void ANodeWithNoFiringsCountsZeroRatherThanBlank()
    {
        GivenNodes(CurrentNode());
        GivenFirings(Firing("some-other-node", FireInstanceState.Executing));

        IRenderedComponent<Cluster> page = context.Render<Cluster>();

        RowCells(page, rowIndex: 0).Should().EndWith(["0", "0"],
            "a firing owned by a node that is no longer listed must not be counted against the one that is");
    }

    [Test]
    public void ASchedulerWithNoClusterStateSaysSoRatherThanShowingAnEmptyTable()
    {
        GivenNodes(new ClusterNodeDto(
            TestData.SchedulerInstanceId,
            LastCheckInUtc: null,
            CheckInInterval: null,
            ClusterNodeState.Alive,
            IsCurrentNode: true));

        IRenderedComponent<Cluster> page = context.Render<Cluster>();

        page.Markup.Should().Contain("This scheduler is not clustered",
            "the scheduler says its store is not clustered, and a single-row table with two dashes in it "
            + "explains nothing");
        RowCells(page, rowIndex: 0).Should().Contain("—",
            "an absent check-in time is shown as absent rather than as the epoch");
    }

    [Test]
    public void AClusterOfSeveralNodesIsNotDescribedAsUnclustered()
    {
        context.WithScheduler(clustered: true, persistent: true);
        GivenNodes(CurrentNode(), Node("node-b", ClusterNodeState.Alive));

        IRenderedComponent<Cluster> page = context.Render<Cluster>();

        page.Markup.Should().NotContain("This scheduler is not clustered");
    }

    /// <summary>
    /// The one case the node list cannot decide: a clustered store whose only node has not finished its
    /// first check-in looks exactly like a store that keeps no cluster state.
    /// </summary>
    /// <remarks>
    /// The page used to infer the verdict from that shape and so told an operator, for up to one
    /// check-in interval after every fresh start, that the cluster they had just configured was not one.
    /// <c>SchedulerDetailDto.Clustered</c> is what the scheduler itself reports, and it is never
    /// ambiguous.
    /// </remarks>
    [Test]
    public void AClusteredSchedulerWhoseOnlyNodeHasNotCheckedInYetIsNotCalledUnclustered()
    {
        context.WithScheduler(clustered: true, persistent: true);
        GivenNodes(new ClusterNodeDto(
            TestData.SchedulerInstanceId,
            LastCheckInUtc: null,
            CheckInInterval: null,
            ClusterNodeState.Alive,
            IsCurrentNode: true));

        IRenderedComponent<Cluster> page = context.Render<Cluster>();

        page.Markup.Should().NotContain("This scheduler is not clustered",
            "the store is clustered whatever its check-in table has had time to say, and a cluster of one "
            + "that has just started is the commonest way to see this page");
    }

    [Test]
    public void TheCheckInTimeIsShownInTheSelectedZoneWithHowLongAgoItWas()
    {
        GivenNodes(CurrentNode());

        IRenderedComponent<Cluster> page = context.Render<Cluster>();

        string cell = RowCells(page, rowIndex: 0)[2];
        cell.Should().Contain("2024-05-06 07:08:09",
            "the absolute time is what an operator correlates with a log, rendered in the zone they picked");
        cell.Should().Contain("ago",
            "and the relative one is what says whether the node is checking in now");
    }

    [Test]
    public void AFailedReadIsReportedRatherThanLeavingAnEmptyTable()
    {
        A.CallTo(() => context.Api.QueryClusterNodes(A<string>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("state table unavailable"));

        IRenderedComponent<Cluster> page = context.Render<Cluster>();

        page.Markup.Should().Contain("state table unavailable");
    }

    /// <summary>
    /// The severity modifier each row's state badge carries, in row order. <c>StateIndicator</c> puts it
    /// on the indicator and the stylesheet colours the dot through it.
    /// </summary>
    private static List<string> StateModifiers(IRenderedComponent<Cluster> page)
    {
        List<string> modifiers = [];
        foreach (IElement indicator in page.FindAll("tbody .qz-state-indicator"))
        {
            foreach (string token in indicator.ClassList)
            {
                if (token is not "qz-state-indicator")
                {
                    modifiers.Add(token);
                }
            }
        }

        return modifiers;
    }

    private static List<string> RowCells(IRenderedComponent<Cluster> page, int rowIndex)
    {
        List<string> cells = [];
        foreach (IElement cell in page.FindAll("tbody tr")[rowIndex].QuerySelectorAll("td"))
        {
            cells.Add(cell.TextContent.Trim());
        }

        return cells;
    }

    private static ClusterNodeDto CurrentNode(ClusterNodeState state = ClusterNodeState.Alive)
    {
        return new ClusterNodeDto(
            TestData.SchedulerInstanceId,
            TestData.Dashboard.FiredAt,
            TimeSpan.FromSeconds(15),
            state,
            IsCurrentNode: true);
    }

    private static ClusterNodeDto Node(string instanceId, ClusterNodeState state)
    {
        return new ClusterNodeDto(
            instanceId,
            TestData.Dashboard.FiredAt,
            TimeSpan.FromSeconds(15),
            state,
            IsCurrentNode: false);
    }

    private static FireInstanceDto Firing(string instanceId, FireInstanceState state)
    {
        return new FireInstanceDto(
            "fire-" + instanceId + "-" + state,
            new TriggerKeyDto("nightly", "trigger-1"),
            state == FireInstanceState.Acquired ? null : new JobKeyDto("reports", "job-1"),
            instanceId,
            state,
            TestData.Dashboard.FiredAt,
            TestData.Dashboard.FiredAt,
            ExecutionGroup: null);
    }

    private void GivenNodes(params ClusterNodeDto[] nodes)
    {
        A.CallTo(() => context.Api.QueryClusterNodes(A<string>._, A<CancellationToken>._))
            .Returns(nodes.ToList());
    }

    private void GivenFirings(params FireInstanceDto[] firings)
    {
        A.CallTo(() => context.Api.QueryFireInstances(A<string>._, A<DashboardFireInstanceQuery>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.Page<FireInstanceDto>(firings));
    }
}
