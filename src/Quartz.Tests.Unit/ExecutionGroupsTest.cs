using System.Collections.Specialized;

using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit;

public sealed class ExecutionGroupsTest
{
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

        AbstractTrigger at = (AbstractTrigger) rebuilt;
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
    /// Reads the limit configured for one group, asserting that the group has an entry of its own.
    /// </summary>
    private static int? LimitFor(ExecutionLimits limits, string group)
    {
        limits.TryGetLimit(group, out int? limit)
            .Should().BeTrue($"execution group '{group ?? "(default)"}' should be configured");
        return limit;
    }

    [Test]
    public void ExecutionLimits_ForGroup_SetsLimit()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .ForGroup("batch-jobs", 2)
            .ForGroup("high-cpu", 5)
            .Build();

        LimitFor(limits, "batch-jobs").Should().Be(2);
        LimitFor(limits, "high-cpu").Should().Be(5);
        limits.Groups.Should().HaveCount(2);
    }

    [Test]
    public void ExecutionLimits_ForDefaultGroup_ReportsTheGroupAsNull()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .ForDefaultGroup(10)
            .Build();

        LimitFor(limits, null).Should().Be(10);
        limits.Groups.Should().ContainSingle().Which.Should().Be(new ExecutionGroupLimit(null, 10),
            "triggers without an execution group fall into the default group, which has no name");
    }

    [Test]
    public void ExecutionLimits_ForOtherGroups_SetsAsteriskKey()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .ForOtherGroups(3)
            .Build();

        LimitFor(limits, ExecutionLimits.OtherGroups).Should().Be(3);
    }

    [Test]
    public void ExecutionLimits_Unlimited_SetsNull()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create()
            .Unlimited("batch-jobs")
            .Build();

        LimitFor(limits, "batch-jobs").Should().BeNull();
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
    public void TryTake_NoLimits_ReturnsTrue()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create().Build().CreateSlots();

        slots.TryTake("batch-jobs").Should().BeTrue();
    }

    [Test]
    public void TryTake_Unlimited_ReturnsTrue()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create().Unlimited("batch-jobs").Build().CreateSlots();

        slots.TryTake("batch-jobs").Should().BeTrue();
        slots.TryTake("batch-jobs").Should().BeTrue();
    }

    [Test]
    public void TryTake_Forbidden_ReturnsFalse()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create().ForGroup("batch-jobs", 0).Build().CreateSlots();

        slots.TryTake("batch-jobs").Should().BeFalse();
    }

    [Test]
    public void TryTake_Available_DecrementsAndReturnsTrue()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create().ForGroup("batch-jobs", 2).Build().CreateSlots();

        slots.TryTake("batch-jobs").Should().BeTrue();
        Remaining(slots, "batch-jobs").Should().Be(1);

        slots.TryTake("batch-jobs").Should().BeTrue();
        Remaining(slots, "batch-jobs").Should().Be(0);

        slots.TryTake("batch-jobs").Should().BeFalse();
    }

    [Test]
    public void TryTake_FallsBackToOtherGroups()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create().ForOtherGroups(1).Build().CreateSlots();

        slots.TryTake("unknown-group").Should().BeTrue();
        Remaining(slots, "unknown-group").Should().Be(0,
            "the catch-all allowance is counted down per group, not shared between them");

        slots.TryTake("unknown-group").Should().BeFalse();
    }

    [Test]
    public void TryTake_NullGroup_DoesNotFallBackToOtherGroups()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create().ForOtherGroups(0).Build().CreateSlots();

        slots.TryTake(null).Should().BeTrue("'*' is a catch-all for named groups, not for ungrouped triggers");
    }

    [Test]
    public void TryTake_NullGroup_UsesDefaultGroup()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create().ForDefaultGroup(1).Build().CreateSlots();

        slots.TryTake(null).Should().BeTrue();
        slots.TryTake(null).Should().BeFalse();
    }

    [Test]
    public void TryTake_GroupNotConfigured_NoDefault_ReturnsTrue()
    {
        ExecutionSlots slots = ExecutionLimitsBuilder.Create().ForGroup("batch-jobs", 0).Build().CreateSlots();

        slots.TryTake("other-group").Should().BeTrue();
    }

    [Test]
    public void TryTake_ThreeTriggersLimitTwo()
    {
        // Simulate what a job store does: walk the candidates and ask for a slot for each
        ExecutionSlots slots = ExecutionLimitsBuilder.Create().ForGroup("batch-jobs", 2).Build().CreateSlots();

        int allowed = 0;
        for (int i = 0; i < 3; i++)
        {
            if (slots.TryTake("batch-jobs"))
            {
                allowed++;
            }
        }

        allowed.Should().Be(2);
    }

    [Test]
    public void CreateSlots_LeavesTheSnapshotAlone()
    {
        ExecutionLimits limits = ExecutionLimitsBuilder.Create().ForGroup("batch-jobs", 2).Build();

        ExecutionSlots first = limits.CreateSlots();
        first.TryTake("batch-jobs").Should().BeTrue();
        first.TryTake("batch-jobs").Should().BeTrue();
        first.TryTake("batch-jobs").Should().BeFalse();

        ExecutionSlots second = limits.CreateSlots();
        second.TryTake("batch-jobs").Should().BeTrue("a retried acquisition starts from the limits again");
        LimitFor(limits, "batch-jobs").Should().Be(2);
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
        LimitFor(limits, "batch-jobs").Should().Be(2);
        LimitFor(limits, null).Should().Be(10);
        LimitFor(limits, ExecutionLimits.OtherGroups).Should().Be(5);
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

        LimitFor(snapshot, "a").Should().Be(5);
        snapshot.Groups.Should().ContainSingle();
        snapshot.TryGetLimit("b", out _).Should().BeFalse();
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
            LimitFor(limits, "batch-jobs").Should().Be(2);
            LimitFor(limits, "high-cpu").Should().Be(5);
            LimitFor(limits, null).Should().Be(10);
            LimitFor(limits, ExecutionLimits.OtherGroups).Should().Be(3);
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
            LimitFor(limits, "a").Should().BeNull();  // "unlimited" → null
            LimitFor(limits, "b").Should().BeNull();  // "none" → null
            LimitFor(limits, "c").Should().BeNull();  // "null" value → null (unlimited)
            LimitFor(limits, "d").Should().Be(5);
            LimitFor(limits, null).Should().Be(8); // "_" key → default group
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
            LimitFor(limits, null).Should().Be(7);
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

        slots.TryTake("unknown").Should().BeTrue();
        Remaining(slots, "unknown").Should().Be(2); // 3 - 1 = 2
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
