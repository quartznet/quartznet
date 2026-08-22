using System.Collections.Specialized;

using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit;

public sealed class ExecutionGroupsTest
{
    /// <summary>
    /// The trigger group a slot request carries when the limits are not deriving anything from it, which
    /// is every case except the <see cref="ExecutionLimitsBuilder.UseTriggerGroupWhenUnset" /> ones.
    /// </summary>
    private const string AnyTriggerGroup = "trigger-group";

    [Test]
    public void TriggerBuilder_WithExecutionGroup_SetsProperty()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t1")
            .WithExecutionGroup("batch-jobs")
            .Build();

        Assert.That(trigger.ExecutionGroup, Is.EqualTo("batch-jobs"));
    }

    [Test]
    public void TriggerBuilder_WithExecutionGroup_Null_ClearsProperty()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t1")
            .WithExecutionGroup("batch-jobs")
            .WithExecutionGroup(null)
            .Build();

        Assert.That(trigger.ExecutionGroup, Is.Null);
    }

    [Test]
    public void TriggerBuilder_GetTriggerBuilder_RoundTrips()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t1", "g1")
            .ForJob("j1")
            .WithExecutionGroup("cpu-intensive")
            .Build();

        var builder = trigger.GetTriggerBuilder();
        ITrigger rebuilt = builder.Build();

        TriggerBase at = (TriggerBase) rebuilt;
        Assert.That(at.ExecutionGroup, Is.EqualTo("cpu-intensive"));
    }

    /// <summary>
    /// Reads the slots left for one group, asserting that the group is being tracked at all.
    /// </summary>
    private static int? Remaining(ExecutionSlots slots, string group)
    {
        slots.TryGetRemaining(group, out int? remaining)
            .Should().BeTrue($"execution group '{group ?? "(default)"}' should be tracked");
        return remaining;
    }

    /// <summary>
    /// Reads the limit configured for one scope, asserting that the scope has an entry of its own.
    /// </summary>
    private static int? LimitFor(ExecutionLimits limits, ExecutionGroupScope scope)
    {
        limits.TryGetLimit(scope, out int? limit)
            .Should().BeTrue($"execution scope '{scope}' should be configured");
        return limit;
    }

    [Test]
    public void ExecutionLimits_ForGroup_SetsLimit()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .ForGroup("batch-jobs", 2)
            .ForGroup("high-cpu", 5)
            .Build();

        LimitFor(limits, ExecutionGroupScope.Named("batch-jobs")).Should().Be(2);
        LimitFor(limits, ExecutionGroupScope.Named("high-cpu")).Should().Be(5);
        limits.Groups.Should().HaveCount(2);
    }

    [Test]
    public void ExecutionLimits_ForDefaultGroup_ReportsTheGroupAsNull()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .ForDefaultGroup(10)
            .Build();

        LimitFor(limits, ExecutionGroupScope.Default).Should().Be(10);
        limits.Groups.Should().ContainSingle().Which.Should().Be(new ExecutionGroupLimit(ExecutionGroupScope.Default, 10),
            "triggers without an execution group fall into the default group, which has no name");
    }

    [Test]
    public void ExecutionLimits_ForOtherGroups_SetsAsteriskKey()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .ForOtherGroups(3)
            .Build();

        LimitFor(limits, ExecutionGroupScope.OtherGroups).Should().Be(3);
    }

    [Test]
    public void ExecutionLimits_Unlimited_SetsNull()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .Unlimited("batch-jobs")
            .Build();

        LimitFor(limits, ExecutionGroupScope.Named("batch-jobs")).Should().BeNull();
    }

    [Test]
    public void ExecutionLimits_IsEmpty_WhenNothingWasConfigured()
    {
        ExecutionLimitsBuilder.Create().Build().IsEmpty.Should().BeTrue();
        ExecutionLimitsBuilder.Create().ForGroup("a", 1).Build().IsEmpty.Should().BeFalse();
    }

    [TestCase("*")]
    [TestCase("_")]
    [TestCase("null")]
    [TestCase("NULL")]
    [TestCase(" * ")]
    public void ExecutionLimits_ForGroup_RejectsReservedNames(string group)
    {
        Action forGroup = () => ExecutionLimitsBuilder.Create().ForGroup(group, 1);
        forGroup.Should().Throw<ArgumentException>().WithMessage("*reserved*");

        Action unlimited = () => ExecutionLimitsBuilder.Create().Unlimited(group);
        unlimited.Should().Throw<ArgumentException>().WithMessage("*reserved*");
    }

    [Test]
    public void ExecutionGroupScope_DistinguishesItsThreeCases()
    {
        ExecutionGroupScope.Default.IsDefault.Should().BeTrue();
        ExecutionGroupScope.Default.IsOtherGroups.Should().BeFalse();
        ExecutionGroupScope.Default.Name.Should().BeNull();

        ExecutionGroupScope.OtherGroups.IsDefault.Should().BeFalse();
        ExecutionGroupScope.OtherGroups.IsOtherGroups.Should().BeTrue();
        ExecutionGroupScope.OtherGroups.Name.Should().BeNull("the catch-all is not a group name a trigger can carry");

        ExecutionGroupScope named = ExecutionGroupScope.Named("batch-jobs");
        named.IsDefault.Should().BeFalse();
        named.IsOtherGroups.Should().BeFalse();
        named.Name.Should().Be("batch-jobs");

        default(ExecutionGroupScope).Should().Be(ExecutionGroupScope.Default, "an uninitialized scope must read as the default bucket");
    }

    [TestCase("*")]
    [TestCase("_")]
    [TestCase("null")]
    [TestCase("")]
    [TestCase("   ")]
    public void ExecutionGroupScope_Named_RejectsReservedAndBlankNames(string name)
    {
        Action act = () => ExecutionGroupScope.Named(name);

        act.Should().Throw<ArgumentException>(
            "the sentinels stay in configuration spelling; the typed scope exists so they can never be mistaken for group names");
    }

    [Test]
    public void ExecutionGroupScope_ReportsTheScopesTheBuilderWrote()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .ForGroup("batch-jobs", 2)
            .ForDefaultGroup(10)
            .ForOtherGroups(5)
            .Build();

        limits.Groups.Should().BeEquivalentTo(
        [
            new ExecutionGroupLimit(ExecutionGroupScope.Named("batch-jobs"), 2),
            new ExecutionGroupLimit(ExecutionGroupScope.Default, 10),
            new ExecutionGroupLimit(ExecutionGroupScope.OtherGroups, 5)
        ]);
    }

    [Test]
    public void TryTake_NoLimits_ReturnsTrue()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create().Build().CreateSlots();

        slots.TryTake("batch-jobs", AnyTriggerGroup).Should().BeTrue();
    }

    [Test]
    public void TryTake_Unlimited_ReturnsTrue()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create().Unlimited("batch-jobs").Build().CreateSlots();

        slots.TryTake("batch-jobs", AnyTriggerGroup).Should().BeTrue();
        slots.TryTake("batch-jobs", AnyTriggerGroup).Should().BeTrue();
    }

    [Test]
    public void TryTake_Forbidden_ReturnsFalse()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create().ForGroup("batch-jobs", 0).Build().CreateSlots();

        slots.TryTake("batch-jobs", AnyTriggerGroup).Should().BeFalse();
    }

    [Test]
    public void TryTake_Available_DecrementsAndReturnsTrue()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create().ForGroup("batch-jobs", 2).Build().CreateSlots();

        slots.TryTake("batch-jobs", AnyTriggerGroup).Should().BeTrue();
        Remaining(slots, "batch-jobs").Should().Be(1);

        slots.TryTake("batch-jobs", AnyTriggerGroup).Should().BeTrue();
        Remaining(slots, "batch-jobs").Should().Be(0);

        slots.TryTake("batch-jobs", AnyTriggerGroup).Should().BeFalse();
    }

    [Test]
    public void TryTake_FallsBackToOtherGroups()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create().ForOtherGroups(1).Build().CreateSlots();

        slots.TryTake("unknown-group", AnyTriggerGroup).Should().BeTrue();
        Remaining(slots, "unknown-group").Should().Be(0,
            "the catch-all allowance is counted down per group, not shared between them");

        slots.TryTake("unknown-group", AnyTriggerGroup).Should().BeFalse();
    }

    [Test]
    public void TryTake_NullGroup_DoesNotFallBackToOtherGroups()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create().ForOtherGroups(0).Build().CreateSlots();

        slots.TryTake(null, AnyTriggerGroup).Should().BeTrue("'*' is a catch-all for named groups, not for ungrouped triggers");
    }

    [Test]
    public void TryTake_NullGroup_UsesDefaultGroup()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create().ForDefaultGroup(1).Build().CreateSlots();

        slots.TryTake(null, AnyTriggerGroup).Should().BeTrue();
        slots.TryTake(null, AnyTriggerGroup).Should().BeFalse();
    }

    [Test]
    public void TryTake_GroupNotConfigured_NoDefault_ReturnsTrue()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create().ForGroup("batch-jobs", 0).Build().CreateSlots();

        slots.TryTake("other-group", AnyTriggerGroup).Should().BeTrue();
    }

    [Test]
    public void TryTake_ThreeTriggersLimitTwo()
    {
        // Simulate what a job store does: walk the candidates and ask for a slot for each
        ExecutionSlots slots = ExecutionLimitsBuilder.Create().ForGroup("batch-jobs", 2).Build().CreateSlots();

        int allowed = 0;
        for (int i = 0; i < 3; i++)
        {
            if (slots.TryTake("batch-jobs", AnyTriggerGroup))
            {
                allowed++;
            }
        }

        allowed.Should().Be(2);
    }

    [Test]
    public void UseTriggerGroupWhenUnset_IsOffUnlessAskedFor()
    {
        ExecutionLimitsBuilder.Create().Build().UsesTriggerGroupWhenUnset.Should().BeFalse();
        ExecutionLimitsBuilder.Create().UseTriggerGroupWhenUnset().Build().UsesTriggerGroupWhenUnset.Should().BeTrue();
    }

    [Test]
    public void UseTriggerGroupWhenUnset_CapsATriggerGroupThatNoTriggerNames()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create()
            .ForGroup("reports", 1)
            .UseTriggerGroupWhenUnset()
            .Build()
            .CreateSlots();

        // Neither trigger carries an execution group; both are in trigger group "reports".
        slots.TryTake(executionGroup: null, "reports").Should().BeTrue();
        slots.TryTake(executionGroup: null, "reports").Should().BeFalse("the trigger group stood in for the execution group");
        slots.TryTake(executionGroup: null, "ingest").Should().BeTrue("a different trigger group is a different bucket");
    }

    [Test]
    public void WithoutTheOption_ATriggerGroupIsNotAnExecutionGroup()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create().ForGroup("reports", 1).Build().CreateSlots();

        slots.TryTake(executionGroup: null, "reports").Should().BeTrue();
        slots.TryTake(executionGroup: null, "reports").Should().BeTrue(
            "an ungrouped trigger is ungrouped, whatever its trigger group is called");
    }

    [Test]
    public void UseTriggerGroupWhenUnset_LeavesAnExplicitExecutionGroupAlone()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create()
            .ForGroup("reports", 0)
            .ForGroup("cpu", 1)
            .UseTriggerGroupWhenUnset()
            .Build()
            .CreateSlots();

        slots.TryTake("cpu", "reports").Should().BeTrue(
            "a trigger that names its execution group is limited by that one, not by its trigger group");
        slots.TryTake("cpu", "reports").Should().BeFalse();
        slots.TryTake(executionGroup: null, "reports").Should().BeFalse("the derived group is forbidden");
    }

    [Test]
    public void UseTriggerGroupWhenUnset_MovesUngroupedTriggersUnderTheCatchAll()
    {
        ExecutionSlots derived = ExecutionLimitsBuilder.Create()
            .ForDefaultGroup(0)
            .ForOtherGroups(1)
            .UseTriggerGroupWhenUnset()
            .Build()
            .CreateSlots();

        // With the derivation on there is no ungrouped trigger left for ForDefaultGroup to forbid.
        derived.TryTake(executionGroup: null, "reports").Should().BeTrue();
        derived.TryTake(executionGroup: null, "reports").Should().BeFalse("the catch-all applies to it now");
    }

    [Test]
    public void UseTriggerGroupWhenUnset_DoesNotDeriveANameTheLimitsReserve()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create()
            .ForDefaultGroup(1)
            .ForOtherGroups(0)
            .UseTriggerGroupWhenUnset()
            .Build()
            .CreateSlots();

        // A trigger group may legitimately be called "*"; an execution group may not. Deriving one from
        // the other would drop the trigger into the catch-all, which is not what its name says.
        slots.TryTake(executionGroup: null, ExecutionLimits.OtherGroups).Should().BeTrue(
            "a trigger group named like a reserved limits key leaves the trigger ungrouped");
        slots.TryTake(executionGroup: null, ExecutionLimits.OtherGroups).Should().BeFalse(
            "...and so it counts against the default group's one slot");
    }

    [Test]
    public void CreateSlots_LeavesTheSnapshotAlone()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create().ForGroup("batch-jobs", 2).Build();

        ExecutionSlots first = limits.CreateSlots();
        first.TryTake("batch-jobs", AnyTriggerGroup).Should().BeTrue();
        first.TryTake("batch-jobs", AnyTriggerGroup).Should().BeTrue();
        first.TryTake("batch-jobs", AnyTriggerGroup).Should().BeFalse();

        ExecutionSlots second = limits.CreateSlots();
        second.TryTake("batch-jobs", AnyTriggerGroup).Should().BeTrue("a retried acquisition starts from the limits again");
        LimitFor(limits, ExecutionGroupScope.Named("batch-jobs")).Should().Be(2);
    }

    [Test]
    public void ExecutionLimits_FluentChaining()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .ForGroup("batch-jobs", 2)
            .ForDefaultGroup(10)
            .ForOtherGroups(5)
            .Build();

        limits.Groups.Should().HaveCount(3);
        LimitFor(limits, ExecutionGroupScope.Named("batch-jobs")).Should().Be(2);
        LimitFor(limits, ExecutionGroupScope.Default).Should().Be(10);
        LimitFor(limits, ExecutionGroupScope.OtherGroups).Should().Be(5);
    }

    [Test]
    public void ExecutionLimits_ForGroup_RejectsNegativeValue()
    {
        Action act = () => ExecutionLimitsBuilder.Create().ForGroup("x", -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void ExecutionLimits_ForDefaultGroup_RejectsNegativeValue()
    {
        Action act = () => ExecutionLimitsBuilder.Create().ForDefaultGroup(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void ExecutionLimits_ForOtherGroups_RejectsNegativeValue()
    {
        Action act = () => ExecutionLimitsBuilder.Create().ForOtherGroups(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void ExecutionLimits_Build_IsIndependentOfLaterBuilderChanges()
    {
        ExecutionLimitsBuilder builder = ExecutionLimitsBuilder.Create().ForGroup("a", 5);
        ExecutionLimits snapshot = builder.Build();

        // Keep configuring the builder after the snapshot was taken
        builder.ForGroup("a", 99).ForGroup("b", 10);

        LimitFor(snapshot, ExecutionGroupScope.Named("a")).Should().Be(5);
        snapshot.Groups.Should().ContainSingle();
        snapshot.TryGetLimit(ExecutionGroupScope.Named("b"), out _).Should().BeFalse();
    }

    [Test]
    public async Task ParseExecutionLimits_NumericValues()
    {
        NameValueCollection props = new()
        {
            ["quartz.executionLimit.batch-jobs"] = "2",
            ["quartz.executionLimit.high-cpu"] = "5",
            ["quartz.executionLimit._"] = "10",
            ["quartz.executionLimit.*"] = "3"
        };
        ISchedulerFactory factory = QuartzSchedulerBuilder.Create().UseProperties(props).Build();
        IScheduler scheduler = await factory.GetScheduler().ConfigureAwait(false);
        try
        {
            ExecutionLimits limits = await scheduler.GetExecutionLimits().ConfigureAwait(false);
            limits.Should().NotBeNull();
            LimitFor(limits, ExecutionGroupScope.Named("batch-jobs")).Should().Be(2);
            LimitFor(limits, ExecutionGroupScope.Named("high-cpu")).Should().Be(5);
            LimitFor(limits, ExecutionGroupScope.Default).Should().Be(10);
            LimitFor(limits, ExecutionGroupScope.OtherGroups).Should().Be(3);
        }
        finally
        {
            await scheduler.Shutdown(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task ParseExecutionLimits_UnlimitedValues()
    {
        NameValueCollection props = new()
        {
            ["quartz.executionLimit.a"] = "unlimited",
            ["quartz.executionLimit.b"] = "none",
            ["quartz.executionLimit.c"] = "null",   // value "null" means unlimited for group "c"
            ["quartz.executionLimit.d"] = "5",
            ["quartz.executionLimit._"] = "8"        // underscore key = default (null) group
        };
        ISchedulerFactory factory = QuartzSchedulerBuilder.Create().UseProperties(props).Build();
        IScheduler scheduler = await factory.GetScheduler().ConfigureAwait(false);
        try
        {
            ExecutionLimits limits = await scheduler.GetExecutionLimits().ConfigureAwait(false);
            limits.Should().NotBeNull();
            LimitFor(limits, ExecutionGroupScope.Named("a")).Should().BeNull();  // "unlimited" → null
            LimitFor(limits, ExecutionGroupScope.Named("b")).Should().BeNull();  // "none" → null
            LimitFor(limits, ExecutionGroupScope.Named("c")).Should().BeNull();  // "null" value → null (unlimited)
            LimitFor(limits, ExecutionGroupScope.Named("d")).Should().Be(5);
            LimitFor(limits, ExecutionGroupScope.Default).Should().Be(8); // "_" key → default group
        }
        finally
        {
            await scheduler.Shutdown(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task ParseExecutionLimits_NullKeyAlias()
    {
        NameValueCollection props = new()
        {
            ["quartz.executionLimit.null"] = "7"   // "null" key = default (null) group alias
        };
        ISchedulerFactory factory = QuartzSchedulerBuilder.Create().UseProperties(props).Build();
        IScheduler scheduler = await factory.GetScheduler().ConfigureAwait(false);
        try
        {
            ExecutionLimits limits = await scheduler.GetExecutionLimits().ConfigureAwait(false);
            limits.Should().NotBeNull();
            LimitFor(limits, ExecutionGroupScope.Default).Should().Be(7);
        }
        finally
        {
            await scheduler.Shutdown(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task ParseExecutionLimits_NoLimits_ReturnsNull()
    {
        NameValueCollection props = new();
        ISchedulerFactory factory = QuartzSchedulerBuilder.Create().UseProperties(props).Build();
        IScheduler scheduler = await factory.GetScheduler().ConfigureAwait(false);
        try
        {
            ExecutionLimits limits = await scheduler.GetExecutionLimits().ConfigureAwait(false);
            limits.Should().BeNull();
        }
        finally
        {
            await scheduler.Shutdown(false).ConfigureAwait(false);
        }
    }

    [Test]
    public void ParseExecutionLimits_InvalidValue_Throws()
    {
        NameValueCollection props = new()
        {
            ["quartz.executionLimit.batch-jobs"] = "notanumber"
        };
        QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create().UseProperties(props);

        // Reported while the container is being built rather than when the scheduler is first asked
        // for, because that is when the keys are turned into registrations.
        Action act = () => builder.Build();

        act.Should().Throw<SchedulerConfigException>().WithMessage("*batch-jobs*");
    }

    [Test]
    public void Slots_UnlistedGroupUsesTheCatchAll()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .ForGroup("batch", 5)
            .ForOtherGroups(3)
            .Build();

        ExecutionSlots slots = limits.CreateSlots();

        // "unknown" is not listed, so it is not tracked until it takes from the catch-all
        slots.TryGetRemaining("unknown", out _).Should().BeFalse();

        slots.TryTake("unknown", AnyTriggerGroup).Should().BeTrue();
        Remaining(slots, "unknown").Should().Be(2); // 3 - 1 = 2
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
