using System.Collections.Specialized;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;

namespace Quartz.Tests.Unit;

/// <summary>
/// A limit for a family of execution groups: every group starting with a prefix gets the limit on its
/// own, which is the catch-all's per-group allowance narrowed to the groups that match.
/// </summary>
/// <remarks>
/// The resolution order is the contract: a group's own limit, then the longest prefix it starts with,
/// then the catch-all, and the ungrouped bucket never inherits. Every place a limit is evaluated —
/// the slots a store takes from, the node-scoped lowering the scheduler thread does, the cluster-scoped
/// lowering a store does — goes through the same resolution, so each is asserted here.
/// </remarks>
public sealed class ExecutionGroupPrefixTest
{
    private const string AnyTriggerGroup = "trigger-group";

    [Test]
    public void EachGroupUnderAPrefixGetsTheLimitOnItsOwn()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create()
            .ForGroupsWithPrefix("tenant:", 2)
            .Build()
            .CreateSlots();

        slots.TryTake("tenant:acme", AnyTriggerGroup).Should().BeTrue();
        slots.TryTake("tenant:acme", AnyTriggerGroup).Should().BeTrue();
        slots.TryTake("tenant:acme", AnyTriggerGroup).Should().BeFalse("acme has used both of its own slots");

        slots.TryTake("tenant:initech", AnyTriggerGroup).Should().BeTrue(
            "a prefix is a family of allowances, not one bucket the family shares");
        slots.TryTake("other", AnyTriggerGroup).Should().BeTrue("a group outside the prefix is not limited by it");
    }

    [Test]
    public void TheLongestMatchingPrefixGoverns()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create()
            .ForGroupsWithPrefix("tenant:", 5)
            .ForGroupsWithPrefix("tenant:free:", 1)
            .Build()
            .CreateSlots();

        slots.TryTake("tenant:free:bob", AnyTriggerGroup).Should().BeTrue();
        slots.TryTake("tenant:free:bob", AnyTriggerGroup).Should().BeFalse(
            "the more specific prefix is the one that describes the group, whatever order they were declared in");

        slots.TryTake("tenant:paid:alice", AnyTriggerGroup).Should().BeTrue();
        slots.TryTake("tenant:paid:alice", AnyTriggerGroup).Should().BeTrue("the shorter prefix still governs the rest of the family");
    }

    [Test]
    public void ANamedGroupWinsOverAPrefixEqualToItsName()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create()
            .ForGroup("tenant:", 0)
            .ForGroupsWithPrefix("tenant:", 1)
            .Build()
            .CreateSlots();

        slots.TryTake("tenant:", AnyTriggerGroup).Should().BeFalse(
            "a group's own limit is the most specific thing that can be said about it, even when a prefix spells the same text");
        slots.TryTake("tenant:acme", AnyTriggerGroup).Should().BeTrue("the prefix still governs the groups that only start with it");
    }

    [Test]
    public void APrefixWinsOverTheCatchAll()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create()
            .ForGroupsWithPrefix("tenant:", 1)
            .ForOtherGroups(0)
            .Build()
            .CreateSlots();

        slots.TryTake("tenant:acme", AnyTriggerGroup).Should().BeTrue("the prefix is checked before the catch-all");
        slots.TryTake("reports", AnyTriggerGroup).Should().BeFalse("the catch-all still governs what no prefix matches");
    }

    [Test]
    public void TheUngroupedBucketIsNeverMatchedByAPrefix()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create()
            .ForGroupsWithPrefix("t", 0)
            .Build()
            .CreateSlots();

        slots.TryTake(executionGroup: null, "tenant-trigger-group").Should().BeTrue(
            "a trigger with no execution group is in the default bucket, which only ForDefaultGroup limits");
    }

    [Test]
    public void TheDerivedTriggerGroupIsMatchedLikeAnyOtherGroup()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create()
            .ForGroupsWithPrefix("tenant:", 1)
            .UseTriggerGroupWhenUnset()
            .Build()
            .CreateSlots();

        slots.TryTake(executionGroup: null, "tenant:acme").Should().BeTrue();
        slots.TryTake(executionGroup: null, "tenant:acme").Should().BeFalse(
            "the trigger group stands in for the execution group before the prefix is looked for");
    }

    [Test]
    public void AClusterScopedPrefixIsLoweredByWhatTheClusterHoldsForEachGroup()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .ForGroupsWithPrefix("tenant:", 2, ExecutionLimitScope.Cluster)
            .Build();

        limits.HasClusterScopedLimits.Should().BeTrue("a store decides from this whether the cluster count is worth reading");

        ExecutionSlots slots = limits.CreateSlots(
        [
            new ExecutionGroupInFlight("tenant:acme", "nightly", 1),
            new ExecutionGroupInFlight("tenant:acme", "hourly", 1),
            new ExecutionGroupInFlight("tenant:initech", "nightly", 1),
        ]);

        slots.TryTake("tenant:acme", AnyTriggerGroup).Should().BeFalse("acme's two cluster-wide slots are both in flight");
        slots.TryTake("tenant:initech", AnyTriggerGroup).Should().BeTrue("initech has one of its own two left");
        slots.TryTake("tenant:initech", AnyTriggerGroup).Should().BeFalse();
        slots.TryTake("tenant:umbrella", AnyTriggerGroup).Should().BeTrue("a tenant with nothing in flight starts from the whole limit");
    }

    [Test]
    public void ANodeScopedPrefixIsLoweredByWhatThisNodeRuns()
    {
        ExecutionLimits configured = ExecutionLimitsBuilder.Create()
            .ForGroupsWithPrefix("tenant:", 2)
            .Build();

        ExecutionLimits available = configured.LowerByNodeInFlight(
        [
            new KeyValuePair<string, int>("tenant:acme", 2),
            new KeyValuePair<string, int>("tenant:initech", 1),
        ]);

        ExecutionSlots slots = available.CreateSlots(
        [
            // Cluster in-flight never lowers a node-scoped allowance a second time.
            new ExecutionGroupInFlight("tenant:initech", AnyTriggerGroup, 5),
        ]);

        slots.TryTake("tenant:acme", AnyTriggerGroup).Should().BeFalse("this node already runs acme's two");
        slots.TryTake("tenant:initech", AnyTriggerGroup).Should().BeTrue("initech has one left on this node");
        slots.TryTake("tenant:initech", AnyTriggerGroup).Should().BeFalse();
        slots.TryTake("tenant:umbrella", AnyTriggerGroup).Should().BeTrue(
            "the prefix travels with the lowered limits, so a group not yet running still finds it");
        slots.TryTake("tenant:umbrella", AnyTriggerGroup).Should().BeTrue();
        slots.TryTake("tenant:umbrella", AnyTriggerGroup).Should().BeFalse();
    }

    [Test]
    public void APrefixCountedInTheOtherScopeStillGovernsTheGroupsItMatches()
    {
        ExecutionLimits configured = ExecutionLimitsBuilder.Create()
            .ForGroupsWithPrefix("tenant:", 1, ExecutionLimitScope.Cluster)
            .ForOtherGroups(5)
            .Build();

        // The scheduler thread lowers node-scoped limits only; the cluster-scoped prefix is not its to
        // lower, and the node-scoped catch-all must not step in behind it.
        ExecutionLimits available = configured.LowerByNodeInFlight([new KeyValuePair<string, int>("tenant:acme", 1)]);
        ExecutionSlots slots = available.CreateSlots([new ExecutionGroupInFlight("tenant:acme", AnyTriggerGroup, 1)]);

        slots.TryTake("tenant:acme", AnyTriggerGroup).Should().BeFalse(
            "one allowance governs a group, and here it is the prefix's single cluster-wide slot");
    }

    [Test]
    public void APrefixIsReadBackAsAPrefix()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .ForGroup("batch", 2)
            .ForGroupsWithPrefix("tenant:", 3, ExecutionLimitScope.Cluster)
            .Build();

        limits.IsEmpty.Should().BeFalse();
        limits.Groups.Should().BeEquivalentTo(new[]
        {
            new ExecutionGroupLimit(ExecutionGroupScope.Named("batch"), 2),
            new ExecutionGroupLimit(ExecutionGroupScope.GroupsWithPrefix("tenant:"), 3, ExecutionLimitScope.Cluster),
        });

        limits.TryGetLimit(ExecutionGroupScope.GroupsWithPrefix("tenant:"), out int? limit).Should().BeTrue();
        limit.Should().Be(3);
        limits.TryGetLimit(ExecutionGroupScope.GroupsWithPrefix("tenant"), out _).Should().BeFalse(
            "a prefix is read back by the prefix it was declared with, not by one that would match more");
        limits.TryGetLimit(ExecutionGroupScope.Named("tenant:acme"), out _).Should().BeFalse(
            "false does not mean unlimited: the group has no limit of its own, and the prefix governs it");
    }

    [Test]
    public void APrefixLimitOnItsOwnIsSomethingToEnforce()
    {
        ExecutionLimitsBuilder.Create().ForGroupsWithPrefix("tenant:", 1).Build().IsEmpty.Should().BeFalse(
            "the scheduler thread skips limits that are empty, and a prefix alone limits something");
        ExecutionLimitsBuilder.Create().ForGroupsWithPrefix("tenant:", 1).Build().HasClusterScopedLimits.Should().BeFalse();
    }

    [Test]
    public void ThePrefixScopeSaysWhatItIs()
    {
        ExecutionGroupScope scope = ExecutionGroupScope.GroupsWithPrefix("  tenant:");

        scope.IsPrefix.Should().BeTrue();
        scope.Prefix.Should().Be("tenant:", "leading white space never starts a group name, so it is dropped");
        scope.Name.Should().BeNull("a prefix is not a group");
        scope.IsDefault.Should().BeFalse();
        scope.IsOtherGroups.Should().BeFalse();
        scope.ToString().Should().Be("tenant:*", "the spelling configuration uses for it");
        scope.Should().NotBe(ExecutionGroupScope.Named("tenant:"), "a prefix and a group of the same text are different buckets");

        ExecutionGroupScope.Named("tenant:").IsPrefix.Should().BeFalse();
        ExecutionGroupScope.Named("tenant:").Prefix.Should().BeNull();
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("tenant:*")]
    public void APrefixThatNamesNothingIsRefused(string prefix)
    {
        Action build = () => ExecutionLimitsBuilder.Create().ForGroupsWithPrefix(prefix, 1);
        build.Should().Throw<ArgumentException>();

        Action scope = () => ExecutionGroupScope.GroupsWithPrefix(prefix);
        scope.Should().Throw<ArgumentException>("the read side accepts what the write side does, no more");
    }

    [Test]
    public void APrefixWithTheStarOfAConfigurationKeySaysWhatToWriteInstead()
    {
        Action build = () => ExecutionLimitsBuilder.Create().ForGroupsWithPrefix("tenant:*", 1);

        build.Should().Throw<ArgumentException>().WithMessage("*'tenant:'*",
            "the star is configuration's spelling, and the message names the prefix that was meant");
    }

    [Test]
    public void ANamedGroupSpelledLikeAPrefixKeyIsRefused()
    {
        Action named = () => ExecutionLimitsBuilder.Create().ForGroup("tenant:*", 1);
        Action unlimited = () => ExecutionLimitsBuilder.Create().Unlimited("tenant:*");

        named.Should().Throw<ArgumentException>().WithMessage("*ForGroupsWithPrefix*",
            "a named group ending in '*' would come back over the HTTP API, or out of properties, as a prefix");
        unlimited.Should().Throw<ArgumentException>();
    }

    [Test]
    public void APrefixDeclaredTwiceKeepsTheLastLimit()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .ForGroupsWithPrefix("tenant:", 1)
            .ForGroupsWithPrefix("tenant:", 4, ExecutionLimitScope.Cluster)
            .Build();

        limits.Groups.Should().ContainSingle().Which.Should().Be(
            new ExecutionGroupLimit(ExecutionGroupScope.GroupsWithPrefix("tenant:"), 4, ExecutionLimitScope.Cluster),
            "a builder method sets a limit, as ForGroup does, rather than adding a second one");
    }

    [Test]
    public void PrefixPropertiesAreReadInBothScopes()
    {
        NameValueCollection properties = new()
        {
            ["quartz.executionLimit.tenant:*"] = "2",
            ["quartz.clusterExecutionLimit.region-*"] = "8",
            ["quartz.executionLimit.*"] = "1",
        };

        ExecutionLimits limits = ExecutionLimitsParser.Parse(properties);

        limits.Should().NotBeNull();
        limits.Groups.Should().BeEquivalentTo(new[]
        {
            new ExecutionGroupLimit(ExecutionGroupScope.GroupsWithPrefix("tenant:"), 2),
            new ExecutionGroupLimit(ExecutionGroupScope.GroupsWithPrefix("region-"), 8, ExecutionLimitScope.Cluster),
            new ExecutionGroupLimit(ExecutionGroupScope.OtherGroups, 1),
        }, "a key ending in '*' after a prefix names the family, and a lone '*' is still the catch-all");
    }

    [Test]
    public async Task APrefixPropertyReachesTheSchedulerThroughTheBridge()
    {
        ServiceCollection services = new();
        services.AddQuartz(new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = $"prefix-bridge-{Guid.NewGuid():N}",
            ["quartz.clusterExecutionLimit.tenant:*"] = "2",
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            ExecutionLimits limits = await scheduler.GetExecutionLimits();

            limits.Should().NotBeNull("the key is under a supported prefix, so the bridge reads it rather than refusing it");
            limits.Groups.Should().ContainSingle().Which.Should().Be(
                new ExecutionGroupLimit(ExecutionGroupScope.GroupsWithPrefix("tenant:"), 2, ExecutionLimitScope.Cluster));
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [TestCase("unlimited", "*cannot be unlimited*")]
    [TestCase("", "*cannot be unlimited*")]
    public void APrefixPropertyWithoutACountIsRefused(string value, string message)
    {
        NameValueCollection properties = new() { ["quartz.executionLimit.tenant:*"] = value };

        Action parse = () => ExecutionLimitsParser.Parse(properties);

        parse.Should().Throw<SchedulerConfigException>().WithMessage(message,
            "an unlimited family would be a rule nothing enforces; leaving the key out already means unlimited")
            .And.Message.Should().Contain("quartz.executionLimit.tenant:*", "the error names the property that said it");
    }

    [Test]
    public void APrefixPropertyWithTwoStarsIsRefused()
    {
        NameValueCollection properties = new() { ["quartz.executionLimit.tenant:**"] = "1" };

        Action parse = () => ExecutionLimitsParser.Parse(properties);

        parse.Should().Throw<SchedulerConfigException>().WithMessage("*quartz.executionLimit.tenant:*",
            "a prefix ending in '*' would match only the groups that literally start with the star");
    }
}
