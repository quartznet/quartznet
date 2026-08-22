using System.Collections.Specialized;

using Quartz.Configuration;

namespace Quartz.Tests.Unit;

/// <summary>
/// What an execution limit is counted against, and the arithmetic that follows from it.
/// </summary>
/// <remarks>
/// <para>
/// The scope is the whole of the cluster-wide ceiling as far as the limits themselves are concerned:
/// a node-scoped limit is lowered by what the scheduler thread has dispatched here, a cluster-scoped
/// one by what the store says the cluster holds, and each ignores the other's subtraction. That
/// division is what keeps a firing from being counted twice — as this node's running work and as its
/// own reservation in the store — and it is invisible in a single-node deployment, where the two
/// counts are the same firings.
/// </para>
/// </remarks>
public sealed class ExecutionLimitScopeTest
{
    /// <summary>
    /// The trigger group a slot request carries when the limits are not deriving anything from it.
    /// </summary>
    private const string AnyTriggerGroup = "trigger-group";

    [Test]
    public void ALimitIsCountedOnThisNodeUnlessItSaysOtherwise()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .ForGroup("batch", 2)
            .ForDefaultGroup(1)
            .ForOtherGroups(3)
            .Unlimited("free")
            .Build();

        limits.Groups.Should().OnlyContain(x => x.Scope == ExecutionLimitScope.Node,
            "execution limits were per node before scopes existed, and configuration that says nothing must go on meaning that");
        limits.HasClusterScopedLimits.Should().BeFalse();
    }

    [Test]
    public void ALimitRemembersTheScopeItWasDeclaredIn()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .ForGroup("batch", 2)
            .ForGroup("tenant", 8, ExecutionLimitScope.Cluster)
            .Build();

        limits.Groups.Should().BeEquivalentTo(new[]
        {
            new ExecutionGroupLimit(ExecutionGroupScope.Named("batch"), 2),
            new ExecutionGroupLimit(ExecutionGroupScope.Named("tenant"), 8, ExecutionLimitScope.Cluster),
        });

        limits.HasClusterScopedLimits.Should().BeTrue(
            "a store reads this to decide whether the cluster-wide count is worth a round trip");
    }

    [Test]
    public void AnExplicitlyUnlimitedGroupIsNotAClusterScopedLimit()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create().Unlimited("free").Build();

        limits.HasClusterScopedLimits.Should().BeFalse(
            "there is no number to count, so no reason to ask the store for one");
    }

    [Test]
    public void AForbiddenGroupIsNotAReasonToCountTheCluster()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .ForGroup("batch", 0, ExecutionLimitScope.Cluster)
            .Build();

        limits.HasClusterScopedLimits.Should().BeFalse(
            "nothing in flight can turn a zero into anything else, so the round trip would buy nothing");
    }

    [Test]
    public void AScopeThatIsNeitherOfTheTwoIsRejected()
    {
        Action act = () => ExecutionLimitsBuilder.Create().ForGroup("batch", 1, (ExecutionLimitScope) 42);

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*Node or Cluster*");
    }

    [Test]
    public void ClusterInFlightWorkLowersAClusterScopedLimit()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .ForGroup("tenant", 3, ExecutionLimitScope.Cluster)
            .Build();

        ExecutionSlots slots = limits.CreateSlots([new ExecutionGroupInFlight("tenant", AnyTriggerGroup, 2)]);

        slots.TryTake("tenant", AnyTriggerGroup).Should().BeTrue("one of the three slots is still free");
        slots.TryTake("tenant", AnyTriggerGroup).Should().BeFalse("the other two are held elsewhere in the cluster");
    }

    [Test]
    public void ClusterInFlightWorkLeavesANodeScopedLimitAlone()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create().ForGroup("batch", 2).Build();

        ExecutionSlots slots = limits.CreateSlots([new ExecutionGroupInFlight("batch", AnyTriggerGroup, 2)]);

        slots.TryTake("batch", AnyTriggerGroup).Should().BeTrue(
            "a node-scoped limit reaches a store already lowered by what runs here, and lowering it again by a count that includes those same firings would charge them twice");
        slots.TryTake("batch", AnyTriggerGroup).Should().BeTrue();
        slots.TryTake("batch", AnyTriggerGroup).Should().BeFalse("two is still two");
    }

    [Test]
    public void AnInFlightCountOfNothingLowersNothing()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .ForGroup("tenant", 1, ExecutionLimitScope.Cluster)
            .Build();

        // A store is free to report a pair it is tracking but currently holds nothing for, and a row
        // like that must not spend a slot.
        ExecutionSlots slots = limits.CreateSlots([new ExecutionGroupInFlight("tenant", AnyTriggerGroup, 0)]);

        slots.TryTake("tenant", AnyTriggerGroup).Should().BeTrue("nothing is in flight, so the whole quota is free");
    }

    [Test]
    public void ClusterInFlightWorkNeverPushesALimitBelowZero()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .ForGroup("tenant", 1, ExecutionLimitScope.Cluster)
            .Build();

        ExecutionSlots slots = limits.CreateSlots([new ExecutionGroupInFlight("tenant", AnyTriggerGroup, 5)]);

        slots.TryTake("tenant", AnyTriggerGroup).Should().BeFalse(
            "a quota that has been overshot is exhausted, not owed slots back");
    }

    [Test]
    public void ClusterInFlightCountsForOneGroupAddUp()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .ForGroup("tenant", 3, ExecutionLimitScope.Cluster)
            .Build();

        // Two rows, because the store's aggregate groups by trigger group as well; both fold into
        // the one execution group.
        ExecutionSlots slots = limits.CreateSlots(
        [
            new ExecutionGroupInFlight("tenant", "nightly", 1),
            new ExecutionGroupInFlight("tenant", "hourly", 2),
        ]);

        slots.TryTake("tenant", "nightly").Should().BeFalse(
            "three are already in flight between the two trigger groups, which is the whole quota");
    }

    [Test]
    public void TheCatchAllGivesEachUnlistedGroupItsOwnClusterQuota()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .ForOtherGroups(1, ExecutionLimitScope.Cluster)
            .Build();

        ExecutionSlots slots = limits.CreateSlots([new ExecutionGroupInFlight("acme", AnyTriggerGroup, 1)]);

        slots.TryTake("acme", AnyTriggerGroup).Should().BeFalse("acme's one cluster-wide slot is taken");
        slots.TryTake("initech", AnyTriggerGroup).Should().BeTrue(
            "the catch-all hands each unlisted group an allowance of its own rather than one they share");
    }

    [Test]
    public void ClusterInFlightWorkIsFoldedThroughTheSameDerivationTheFilterUses()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .ForGroup("acme", 1, ExecutionLimitScope.Cluster)
            .UseTriggerGroupWhenUnset()
            .Build();

        // The in-flight row carries no execution group, exactly as the store persisted it; its trigger
        // group is what the limits stand in with.
        ExecutionSlots slots = limits.CreateSlots([new ExecutionGroupInFlight(null, "acme", 1)]);

        slots.TryTake(executionGroup: null, "acme").Should().BeFalse(
            "the count and the filter have to key work the same way, or a derived group would be counted in one bucket and spent from another");
    }

    [Test]
    public void TheDefaultBucketCanBeCappedAcrossTheCluster()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .ForDefaultGroup(2, ExecutionLimitScope.Cluster)
            .Build();

        ExecutionSlots slots = limits.CreateSlots([new ExecutionGroupInFlight(null, AnyTriggerGroup, 2)]);

        slots.TryTake(executionGroup: null, AnyTriggerGroup).Should().BeFalse();
    }

    [Test]
    public void ClusterInFlightWorkForAnUnlimitedGroupChangesNothing()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .ForGroup("tenant", 1, ExecutionLimitScope.Cluster)
            .Build();

        ExecutionSlots slots = limits.CreateSlots([new ExecutionGroupInFlight("something-else", AnyTriggerGroup, 9)]);

        slots.TryTake("tenant", AnyTriggerGroup).Should().BeTrue("the count was for a group nothing limits");
        slots.TryTake("something-else", AnyTriggerGroup).Should().BeTrue();
    }

    [Test]
    public void ClusterExecutionLimitPropertiesAreReadAsClusterScoped()
    {
        NameValueCollection properties = new()
        {
            ["quartz.executionLimit.batch"] = "2",
            ["quartz.clusterExecutionLimit.tenant"] = "8",
            ["quartz.clusterExecutionLimit.*"] = "1",
        };

        ExecutionLimits limits = ExecutionLimitsParser.Parse(properties);

        limits.Should().NotBeNull();
        limits.Groups.Should().BeEquivalentTo(new[]
        {
            new ExecutionGroupLimit(ExecutionGroupScope.Named("batch"), 2),
            new ExecutionGroupLimit(ExecutionGroupScope.Named("tenant"), 8, ExecutionLimitScope.Cluster),
            new ExecutionGroupLimit(ExecutionGroupScope.OtherGroups, 1, ExecutionLimitScope.Cluster),
        }, "the two prefixes configure the same groups in different scopes, and neither spelling can be mistaken for the other");
    }
}
