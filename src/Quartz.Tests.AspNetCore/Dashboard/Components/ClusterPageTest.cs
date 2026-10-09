using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Quartz.Configuration;
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
    /// For a cluster fronted by several targets, each node a member fronts offers the verbs that belong
    /// to one process, sent through that member's own key.
    /// </summary>
    [Test]
    public void ANodeOfAClusterIsActedOnThroughTheMemberThatFrontsIt()
    {
        GivenCluster();

        IRenderedComponent<Cluster> page = context.Render<Cluster>();

        page.WaitForAssertion(() => RowCells(page, rowIndex: 0).Should().Contain("a",
            "the member that fronts the node is named, which is what the actions go through"));
        RowCells(page, rowIndex: 2).Should().Contain("—", "no target fronts node-c, so it has no actions");

        page.FindAll("tbody tr")[1].QuerySelectorAll("button").First(button => button.TextContent.Trim() == "Standby").Click();

        // Stand-by acts on one node, so it goes through the member's key rather than the cluster's.
        page.WaitForAssertion(() => A.CallTo(() => context.Api.Standby("b/QuartzScheduler", A<CancellationToken>._)).MustHaveHappened());
        A.CallTo(() => context.Api.Standby("a+b/QuartzScheduler", A<CancellationToken>._)).MustNotHaveHappened();
        page.FindAll("tbody tr")[2].QuerySelectorAll("button").Should().BeEmpty();
    }

    [Test]
    public void InterruptingANodeInterruptsWhatItIsRunningThroughItsMember()
    {
        GivenCluster();
        GivenFirings(
            Firing("node-a", FireInstanceState.Executing),
            Firing("node-b", FireInstanceState.Executing));

        IRenderedComponent<Cluster> page = context.Render<Cluster>();

        page.WaitForAssertion(() => page.FindAll("tbody tr")[0].QuerySelectorAll("button").Should().NotBeEmpty());
        page.FindAll("tbody tr")[0].QuerySelectorAll("button").First(button => button.TextContent.Trim() == "Interrupt").Click();

        // The node's own firings are interrupted through the member that fronts it.
        page.WaitForAssertion(() => A.CallTo(() => context.Api.InterruptFireInstance("a/QuartzScheduler", "fire-node-a-Executing", A<CancellationToken>._)).MustHaveHappened());
        A.CallTo(() => context.Api.InterruptFireInstance(A<string>._, "fire-node-b-Executing", A<CancellationToken>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// A node that restarts under a new instance id is the same member fronting a different row, so the
    /// member is asked again which node it fronts.
    /// </summary>
    [Test]
    public void AMemberWhoseNodeRestartedUnderANewIdIsMappedAgain()
    {
        GivenCluster();

        IRenderedComponent<Cluster> page = context.Render<Cluster>();
        page.WaitForAssertion(() => RowCells(page, rowIndex: 0).Should().Contain("a"));

        // Node a comes back as node-a2: its member answers with the new id, and the store lists the new row.
        A.CallTo(() => context.Api.GetScheduler("a/QuartzScheduler", A<CancellationToken>._))
            .Returns(TestData.Dashboard.SchedulerDetail(SchedulerStatus.Running, "QuartzScheduler", clustered: true, persistent: true) with { SchedulerInstanceId = "node-a2" });
        GivenNodes(Node("node-a2", ClusterNodeState.Alive), Node("node-b", ClusterNodeState.Alive));

        context.SchedulerState.NotifyChanged();

        page.WaitForAssertion(() =>
        {
            page.FindAll("tbody tr")[0].TextContent.Should().Contain("node-a2");
            RowCells(page, rowIndex: 0).Should().Contain("a",
                "the member's node is no longer listed, so the member was asked again and its new node is mapped to it");
        });
    }

    /// <summary>
    /// A member that never answers which node it fronts costs the page the listing's deadline and no
    /// more: the other member's node is still mapped, and the page still renders.
    /// </summary>
    [Test]
    public void AMemberThatNeverAnswersCostsThePageTheDeadlineAndNoMore()
    {
        GivenCluster();
        A.CallTo(() => context.Api.GetScheduler("b/QuartzScheduler", A<CancellationToken>._))
            .ReturnsLazily(call => new ValueTask<SchedulerDetailDto>(Stall(call.GetArgument<CancellationToken>(1))));

        IRenderedComponent<Cluster> page = context.Render<Cluster>();

        // The member is asked under the listing's deadline, not under the client's timeout: the rows are
        // there within it and a margin, which the fake's never-completing answer would otherwise hold.
        page.WaitForAssertion(
            () =>
            {
                RowCells(page, rowIndex: 0).Should().Contain("a", "the member that answered is mapped to its node");
                RowCells(page, rowIndex: 1).Should().Contain("—", "the member that did not answer is asked again next refresh");
            },
            ContainerSchedulerRegistry.StatusTimeout + TimeSpan.FromSeconds(5));
    }

    [Test]
    public void ReadOnlyHidesTheNodeActions()
    {
        context.Dispose();
        context = new DashboardComponentContext(options => options.ReadOnly = true);
        GivenCluster();

        IRenderedComponent<Cluster> page = context.Render<Cluster>();

        page.WaitForAssertion(() => RowCells(page, rowIndex: 0).Should().Contain("a"));
        page.FindAll("tbody button").Should().BeEmpty("a read-only dashboard offers no way to drive a node");
    }

    private void GivenCluster()
    {
        context.WithScheduler(
            "QuartzScheduler",
            clustered: true,
            persistent: true,
            origin: SchedulerOrigin.Cluster,
            target: "a+b",
            members: ["a", "b"]);

        // What the picker last listed, which is where the page learns that the key is a cluster's.
        context.SchedulerState.AvailableSchedulers =
            [TestData.Dashboard.SchedulerHeader("QuartzScheduler", origin: SchedulerOrigin.Cluster, target: "a+b", members: ["a", "b"])];

        A.CallTo(() => context.Api.GetScheduler("a/QuartzScheduler", A<CancellationToken>._))
            .Returns(TestData.Dashboard.SchedulerDetail(SchedulerStatus.Running, "QuartzScheduler", clustered: true, persistent: true) with { SchedulerInstanceId = "node-a" });
        A.CallTo(() => context.Api.GetScheduler("b/QuartzScheduler", A<CancellationToken>._))
            .Returns(TestData.Dashboard.SchedulerDetail(SchedulerStatus.Running, "QuartzScheduler", clustered: true, persistent: true) with { SchedulerInstanceId = "node-b" });

        GivenNodes(Node("node-a", ClusterNodeState.Alive), Node("node-b", ClusterNodeState.Alive), Node("node-c", ClusterNodeState.Alive));
        GivenFirings();
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

    /// <summary>
    /// An answer that never comes: completes only by the caller's own cancellation.
    /// </summary>
    private static async Task<SchedulerDetailDto> Stall(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("never reached");
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
