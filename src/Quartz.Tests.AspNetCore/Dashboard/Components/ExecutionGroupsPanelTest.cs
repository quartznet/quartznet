using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Shared;
using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The execution-group panel: the join between what a scheduler allows and what it is running.
/// </summary>
/// <remarks>
/// Execution limits were configurable and invisible — <c>GetExecutionLimits</c> was implemented and no
/// page called it — so the thing these tests are most about is that the panel's arithmetic matches the
/// scheduler's: the same group key for a firing, the same catch-all rule, and the same two firing states
/// counted against a limit.
/// </remarks>
public class ExecutionGroupsPanelTest
{
    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
        context.WithScheduler();
        GivenLimits(new ExecutionLimitsDto([]));
        GivenFirings();
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    [Test]
    public void EachGroupIsARowSayingWhatItMayRunAndWhatItIsRunning()
    {
        GivenLimits(Limits(
            ("batch", 4, ExecutionLimitScope.Node),
            ("tenant-acme", 8, ExecutionLimitScope.Cluster)));
        GivenFirings(
            Firing("batch", FireInstanceState.Executing),
            Firing("batch", FireInstanceState.Executing),
            Firing("batch", FireInstanceState.Acquired),
            Firing("tenant-acme", FireInstanceState.Executing));

        IRenderedComponent<ExecutionGroups> panel = Render();

        RowCells(panel, 0).Should().Equal(["batch", "Node", "4", "3", "1"],
            "a reservation holds a slot exactly as a running execution does, so the acquired firing "
            + "counts against the limit the way the acquisition filter counts it");
        RowCells(panel, 1).Should().Equal(["tenant-acme", "Cluster", "8", "1", "7"],
            "a quota across the cluster and a ceiling per node are different promises, and the panel "
            + "has to say which one it is showing");
    }

    [Test]
    public void HeadroomStopsAtZeroRatherThanGoingNegative()
    {
        GivenLimits(Limits(("batch", 1, ExecutionLimitScope.Node)));
        GivenFirings(
            Firing("batch", FireInstanceState.Executing),
            Firing("batch", FireInstanceState.Executing),
            Firing("batch", FireInstanceState.Executing));

        IRenderedComponent<ExecutionGroups> panel = Render();

        RowCells(panel, 0).Should().Equal(["batch", "Node", "1", "3", "0"],
            "a limit lowered under work already in flight leaves nothing to spend, and '-2' would read "
            + "as an arithmetic fault rather than as a group that is over its ceiling");
    }

    [Test]
    public void AGroupWithNoLimitIsUnlimitedRatherThanZero()
    {
        GivenLimits(new ExecutionLimitsDto([]));
        GivenFirings(Firing("reports", FireInstanceState.Executing));

        IRenderedComponent<ExecutionGroups> panel = Render();

        RowCells(panel, 0).Should().Equal(["reports", "—", "Unlimited", "1", "Unlimited"],
            "no limit configured is no restriction, and a blank or a zero would read as a group that "
            + "cannot run");
    }

    [Test]
    public void AnExplicitlyUnlimitedGroupReadsTheSameAsAnUnconfiguredOne()
    {
        GivenLimits(new ExecutionLimitsDto(new Dictionary<string, DashboardExecutionLimit>
        {
            ["reports"] = new(null, ExecutionLimitScope.Node)
        }));
        GivenFirings(Firing("reports", FireInstanceState.Executing));

        IRenderedComponent<ExecutionGroups> panel = Render();

        RowCells(panel, 0).Should().Equal(["reports", "Node", "Unlimited", "1", "Unlimited"]);
    }

    [Test]
    public void AForbiddenGroupSaysSoRatherThanShowingABareZero()
    {
        GivenLimits(Limits(("batch", 0, ExecutionLimitScope.Node)));

        IRenderedComponent<ExecutionGroups> panel = Render();

        RowCells(panel, 0).Should().Equal(["batch", "Node", "0 (forbidden)", "0", "0"],
            "zero is the one limit that is a policy rather than a number, and it is the answer to 'why "
            + "is this group not running'");
    }

    [Test]
    public void TriggersWithNoExecutionGroupGetTheirOwnRowWhenAnyAreInFlight()
    {
        GivenFirings(Firing(executionGroup: null, FireInstanceState.Executing));

        IRenderedComponent<ExecutionGroups> panel = Render();

        RowCells(panel, 0).Should().Equal(["(no execution group)", "—", "Unlimited", "1", "Unlimited"],
            "work that belongs to no group is still work, and leaving it out would make the panel's "
            + "totals disagree with the executing tile");
    }

    /// <summary>
    /// The ungrouped bucket is never covered by the catch-all — the same rule the scheduler applies when
    /// it takes a slot.
    /// </summary>
    [Test]
    public void TheCatchAllNeverGovernsTheUngroupedBucket()
    {
        GivenLimits(Limits((ExecutionLimits.OtherGroups, 5, ExecutionLimitScope.Node)));
        GivenFirings(
            Firing(executionGroup: null, FireInstanceState.Executing),
            Firing("reports", FireInstanceState.Executing));

        IRenderedComponent<ExecutionGroups> panel = Render();

        RowCells(panel, 0).Should().Equal(["reports", "Node", "5 (other groups)", "1", "4"],
            "a named group with no limit of its own gets the catch-all's ceiling, and the panel says "
            + "where the number came from");
        RowCells(panel, 1).Should().Equal(["(no execution group)", "—", "Unlimited", "1", "Unlimited"],
            "the catch-all is for named groups only; charging the ungrouped bucket to it would show a "
            + "ceiling the scheduler does not enforce");
    }

    [Test]
    public void TheCatchAllIsShownAsARuleRatherThanAsABucketOfItsOwn()
    {
        GivenLimits(Limits((ExecutionLimits.OtherGroups, 5, ExecutionLimitScope.Cluster)));
        GivenFirings(Firing("reports", FireInstanceState.Executing));

        IRenderedComponent<ExecutionGroups> panel = Render();

        RowCells(panel, 1).Should().Equal(["other groups (catch-all)", "Cluster", "5", "—", "—"],
            "each unlisted group gets an allowance of its own rather than sharing one, so the catch-all "
            + "has nothing in flight against it and a count there would be a fiction");
        panel.Markup.Should().Contain("allowance each rather than one shared between them",
            "the footnote is what stops the dash reading as a rendering fault");
    }

    /// <summary>
    /// With <c>UseTriggerGroupWhenUnset</c> on, a trigger that carries no execution group is limited as
    /// though it belonged to a group named after its trigger group.
    /// </summary>
    /// <remarks>
    /// The panel resolves the key through the scheduler's own rule, so the count it shows and the count
    /// the acquisition filter makes cannot key the same firing differently. It labels the row, because a
    /// group nobody typed appearing in a listing of execution groups is otherwise a puzzle.
    /// </remarks>
    [Test]
    public void AnUngroupedFiringIsCountedUnderItsTriggerGroupWhenTheDerivationIsOn()
    {
        GivenLimits(new ExecutionLimitsDto(
            new Dictionary<string, DashboardExecutionLimit> { ["nightly"] = new(2, ExecutionLimitScope.Node) },
            UsesTriggerGroupWhenUnset: true));
        GivenFirings(Firing(executionGroup: null, FireInstanceState.Executing, triggerGroup: "nightly"));

        IRenderedComponent<ExecutionGroups> panel = Render();

        RowCells(panel, 0).Should().Equal(["nightly (from trigger group)", "Node", "2", "1", "1"],
            "the trigger carries no execution group, so the row's name came from the derivation and the "
            + "panel says so rather than inventing a group the schedule does not name");
    }

    [Test]
    public void WithTheDerivationOffAnUngroupedFiringStaysUngrouped()
    {
        GivenLimits(Limits(("nightly", 2, ExecutionLimitScope.Node)));
        GivenFirings(Firing(executionGroup: null, FireInstanceState.Executing, triggerGroup: "nightly"));

        IRenderedComponent<ExecutionGroups> panel = Render();

        RowCells(panel, 1).Should().Equal(["(no execution group)", "—", "Unlimited", "1", "Unlimited"],
            "the derivation is opt-in, and applying it unasked would show a ceiling the scheduler is "
            + "not enforcing");
        RowCells(panel, 0).Should().Equal(["nightly", "Node", "2", "0", "2"]);
    }

    /// <summary>
    /// A scheduler that cannot answer is not a scheduler with nothing limited.
    /// </summary>
    [Test]
    public void ASchedulerThatCannotReportLimitsSaysSoRatherThanShowingNone()
    {
        GivenLimits(ExecutionLimitsDto.CannotReport);
        GivenFirings(Firing("batch", FireInstanceState.Executing));

        IRenderedComponent<ExecutionGroups> panel = Render();

        panel.Markup.Should().Contain("cannot report execution limits",
            "rendering it as 'nothing is limited' would have the panel state a fact nobody established");
        panel.FindAll("tbody tr").Should().BeEmpty(
            "there are no ceilings to compare the firings against, so a table of headroom would be "
            + "guesswork");
    }

    [Test]
    public void NothingLimitedAndNothingRunningSaysSoRatherThanShowingAnEmptyTable()
    {
        IRenderedComponent<ExecutionGroups> panel = Render();

        panel.Markup.Should().Contain("every trigger is free to fire");
        panel.FindAll("table").Should().BeEmpty();
    }

    [TestCase(true, "Counted across the cluster")]
    [TestCase(false, "Counted on this node")]
    public void ThePanelSaysWhoseFiringsItIsCounting(bool persistent, string expected)
    {
        GivenFirings(Firing("batch", FireInstanceState.Executing));

        IRenderedComponent<ExecutionGroups> panel = Render(persistent: persistent);

        panel.Markup.Should().Contain(expected,
            "a persistent store answers the firing query for the whole cluster and an in-memory one for "
            + "this node, and the same number means different things in the two cases");
    }

    /// <summary>
    /// The one comparison the panel cannot make exactly: a node-scoped ceiling against a cluster-wide
    /// count.
    /// </summary>
    [Test]
    public void ANodeScopedLimitCountedAcrossAClusterIsCalledOut()
    {
        GivenLimits(Limits(("batch", 4, ExecutionLimitScope.Node)));
        GivenFirings(Firing("batch", FireInstanceState.Executing));

        IRenderedComponent<ExecutionGroups> panel = Render(persistent: true, clustered: true);

        panel.Markup.Should().Contain("A node-scoped limit is what one node may run",
            "every node enforces its own copy of a node-scoped limit, so subtracting the cluster's work "
            + "from it understates what this node can still start");
    }

    [Test]
    public void ANodeScopedLimitOnAnUnclusteredSchedulerNeedsNoCaveat()
    {
        GivenLimits(Limits(("batch", 4, ExecutionLimitScope.Node)));
        GivenFirings(Firing("batch", FireInstanceState.Executing));

        IRenderedComponent<ExecutionGroups> panel = Render();

        panel.Markup.Should().NotContain("A node-scoped limit is what one node may run",
            "there is one node, so the per-node ceiling and the count are the same measurement");
    }

    [Test]
    public void CountsTakenFromAPartialPageSayThatTheyAre()
    {
        GivenLimits(Limits(("batch", 4, ExecutionLimitScope.Node)));
        A.CallTo(() => context.Api.QueryFireInstances(A<string>._, A<DashboardFireInstanceQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<FireInstanceDto>(
                [Firing("batch", FireInstanceState.Executing)],
                HasMore: true,
                TotalCount: 900));

        IRenderedComponent<ExecutionGroups> panel = Render();

        panel.Markup.Should().Contain("there are more in flight",
            "headroom read off a page rather than off the whole listing is a number an operator would "
            + "otherwise act on");
    }

    [Test]
    public void AFailedReadIsReportedRatherThanLeavingAnEmptyPanel()
    {
        A.CallTo(() => context.Api.GetExecutionLimits(A<string>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("the scheduler is shutting down"));

        IRenderedComponent<ExecutionGroups> panel = Render();

        panel.Markup.Should().Contain("the scheduler is shutting down");
    }

    private IRenderedComponent<ExecutionGroups> Render(bool persistent = false, bool clustered = false)
    {
        return context.Render<ExecutionGroups>(parameters => parameters
            .Add(x => x.SchedulerName, TestData.SchedulerName)
            .Add(x => x.Persistent, persistent)
            .Add(x => x.Clustered, clustered));
    }

    private void GivenLimits(ExecutionLimitsDto limits)
    {
        A.CallTo(() => context.Api.GetExecutionLimits(A<string>._, A<CancellationToken>._)).Returns(limits);
    }

    private void GivenFirings(params FireInstanceDto[] firings)
    {
        A.CallTo(() => context.Api.QueryFireInstances(A<string>._, A<DashboardFireInstanceQuery>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.Page<FireInstanceDto>(firings.ToList()));
    }

    private static ExecutionLimitsDto Limits(params (string Group, int? MaxConcurrent, ExecutionLimitScope Scope)[] limits)
    {
        Dictionary<string, DashboardExecutionLimit> byGroup = new(StringComparer.Ordinal);
        foreach ((string group, int? maxConcurrent, ExecutionLimitScope scope) in limits)
        {
            byGroup[group] = new DashboardExecutionLimit(maxConcurrent, scope);
        }

        return new ExecutionLimitsDto(byGroup);
    }

    private static FireInstanceDto Firing(
        string? executionGroup,
        FireInstanceState state,
        string triggerGroup = "DEFAULT")
    {
        return new FireInstanceDto(
            FireInstanceId: Guid.NewGuid().ToString("N"),
            TriggerKey: new TriggerKeyDto(triggerGroup, "trigger-1"),
            JobKey: state == FireInstanceState.Acquired ? null : new JobKeyDto("jobs", "job-1"),
            SchedulerInstanceId: TestData.SchedulerInstanceId,
            State: state,
            FireTimeUtc: TestData.Dashboard.FiredAt,
            ScheduledFireTimeUtc: TestData.Dashboard.FiredAt,
            ExecutionGroup: executionGroup);
    }

    /// <summary>
    /// One row's cells, with runs of whitespace — the markup's indentation and the non-breaking spaces
    /// before each qualifier — collapsed to single spaces, so a test reads as the row does.
    /// </summary>
    private static List<string> RowCells(IRenderedComponent<ExecutionGroups> panel, int rowIndex)
    {
        List<string> cells = [];
        foreach (IElement cell in panel.FindAll("tbody tr")[rowIndex].QuerySelectorAll("td"))
        {
            cells.Add(string.Join(' ', cell.TextContent.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries)));
        }

        return cells;
    }
}
