using System.Collections.Specialized;

using NUnit.Framework;

using Quartz.Impl;
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

        TriggerBuilder builder = trigger.GetTriggerBuilder();
        ITrigger rebuilt = builder.Build();

        AbstractTrigger at = (AbstractTrigger) rebuilt;
        Assert.That(at.ExecutionGroup, Is.EqualTo("cpu-intensive"));
    }

    [Test]
    public void ExecutionLimits_ForGroup_SetsLimit()
    {
        ExecutionLimits limits = new ExecutionLimits()
            .ForGroup("batch-jobs", 2)
            .ForGroup("high-cpu", 5);

        Assert.That(limits["batch-jobs"], Is.EqualTo(2));
        Assert.That(limits["high-cpu"], Is.EqualTo(5));
        Assert.That(limits.Count, Is.EqualTo(2));
    }

    [Test]
    public void ExecutionLimits_ForDefaultGroup_SetsEmptyKey()
    {
        ExecutionLimits limits = new ExecutionLimits()
            .ForDefaultGroup(10);

        Assert.That(limits[ExecutionLimits.DefaultGroupKey], Is.EqualTo(10));
    }

    [Test]
    public void ExecutionLimits_ForOtherGroups_SetsAsteriskKey()
    {
        ExecutionLimits limits = new ExecutionLimits()
            .ForOtherGroups(3);

        Assert.That(limits[ExecutionLimits.OtherGroups], Is.EqualTo(3));
    }

    [Test]
    public void ExecutionLimits_Unlimited_SetsNull()
    {
        ExecutionLimits limits = new ExecutionLimits()
            .Unlimited("batch-jobs");

        Assert.That(limits["batch-jobs"], Is.Null);
    }

    [Test]
    public void CheckExecutionLimits_NoLimits_ReturnsTrue()
    {
        Dictionary<string, int?> limits = new();

        Assert.That(ExecutionLimits.CheckExecutionLimits("batch-jobs", limits), Is.True);
    }

    [Test]
    public void CheckExecutionLimits_Unlimited_ReturnsTrue()
    {
        Dictionary<string, int?> limits = new()
        {
            ["batch-jobs"] = null
        };

        Assert.That(ExecutionLimits.CheckExecutionLimits("batch-jobs", limits), Is.True);
    }

    [Test]
    public void CheckExecutionLimits_Forbidden_ReturnsFalse()
    {
        Dictionary<string, int?> limits = new()
        {
            ["batch-jobs"] = 0
        };

        Assert.That(ExecutionLimits.CheckExecutionLimits("batch-jobs", limits), Is.False);
    }

    [Test]
    public void CheckExecutionLimits_Available_DecrementsAndReturnsTrue()
    {
        Dictionary<string, int?> limits = new()
        {
            ["batch-jobs"] = 2
        };

        Assert.That(ExecutionLimits.CheckExecutionLimits("batch-jobs", limits), Is.True);
        Assert.That(limits["batch-jobs"], Is.EqualTo(1));

        Assert.That(ExecutionLimits.CheckExecutionLimits("batch-jobs", limits), Is.True);
        Assert.That(limits["batch-jobs"], Is.EqualTo(0));

        Assert.That(ExecutionLimits.CheckExecutionLimits("batch-jobs", limits), Is.False);
    }

    [Test]
    public void CheckExecutionLimits_FallsBackToOtherGroups()
    {
        Dictionary<string, int?> limits = new()
        {
            [ExecutionLimits.OtherGroups] = 1
        };

        Assert.That(ExecutionLimits.CheckExecutionLimits("unknown-group", limits), Is.True);
        Assert.That(limits["unknown-group"], Is.EqualTo(0));

        Assert.That(ExecutionLimits.CheckExecutionLimits("unknown-group", limits), Is.False);
    }

    [Test]
    public void CheckExecutionLimits_NullGroup_UsesDefaultKey()
    {
        Dictionary<string, int?> limits = new()
        {
            [ExecutionLimits.DefaultGroupKey] = 1
        };

        Assert.That(ExecutionLimits.CheckExecutionLimits(null, limits), Is.True);
        Assert.That(ExecutionLimits.CheckExecutionLimits(null, limits), Is.False);
    }

    [Test]
    public void CheckExecutionLimits_GroupNotConfigured_NoDefault_ReturnsTrue()
    {
        Dictionary<string, int?> limits = new()
        {
            ["batch-jobs"] = 0
        };

        Assert.That(ExecutionLimits.CheckExecutionLimits("other-group", limits), Is.True);
    }

    [Test]
    public void CheckExecutionLimits_Integration_ThreeTriggersLimitTwo()
    {
        // Simulate what RAMJobStore does: iterate triggers and check limits
        Dictionary<string, int?> limits = new(StringComparer.Ordinal)
        {
            ["batch-jobs"] = 2
        };

        int allowed = 0;
        for (int i = 0; i < 3; i++)
        {
            if (ExecutionLimits.CheckExecutionLimits("batch-jobs", limits))
            {
                allowed++;
            }
        }

        Assert.That(allowed, Is.EqualTo(2));
    }

    [Test]
    public void CheckExecutionLimits_ForbiddenGroup_AllRejected()
    {
        Dictionary<string, int?> limits = new(StringComparer.Ordinal)
        {
            ["forbidden-group"] = 0
        };

        Assert.That(ExecutionLimits.CheckExecutionLimits("forbidden-group", limits), Is.False);
    }

    [Test]
    public void CheckExecutionLimits_NullLimits_AllAllowed()
    {
        // When no limits dictionary has no entries, everything is allowed
        Dictionary<string, int?> limits = new(StringComparer.Ordinal);
        Assert.That(ExecutionLimits.CheckExecutionLimits("batch-jobs", limits), Is.True);
        Assert.That(ExecutionLimits.CheckExecutionLimits("other", limits), Is.True);
        Assert.That(ExecutionLimits.CheckExecutionLimits(null, limits), Is.True);
    }

    [Test]
    public void ExecutionLimits_ToWorkingCopy_CreatesMutableCopy()
    {
        ExecutionLimits limits = new ExecutionLimits()
            .ForGroup("batch-jobs", 5);

        Dictionary<string, int?> copy = limits.ToWorkingCopy();
        copy["batch-jobs"] = 3;

        Assert.That(limits["batch-jobs"], Is.EqualTo(5));
        Assert.That(copy["batch-jobs"], Is.EqualTo(3));
    }

    [Test]
    public void ExecutionLimits_FluentChaining()
    {
        ExecutionLimits limits = new ExecutionLimits()
            .ForGroup("batch-jobs", 2)
            .ForDefaultGroup(10)
            .ForOtherGroups(5);

        Assert.That(limits.Count, Is.EqualTo(3));
        Assert.That(limits["batch-jobs"], Is.EqualTo(2));
        Assert.That(limits[ExecutionLimits.DefaultGroupKey], Is.EqualTo(10));
        Assert.That(limits[ExecutionLimits.OtherGroups], Is.EqualTo(5));
    }

    [Test]
    public void ExecutionLimits_ForGroup_RejectsNegativeValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutionLimits().ForGroup("x", -1));
    }

    [Test]
    public void ExecutionLimits_ForDefaultGroup_RejectsNegativeValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutionLimits().ForDefaultGroup(-1));
    }

    [Test]
    public void ExecutionLimits_ForOtherGroups_RejectsNegativeValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutionLimits().ForOtherGroups(-1));
    }

    [Test]
    public void ExecutionLimits_Snapshot_IsIndependent()
    {
        ExecutionLimits original = new ExecutionLimits().ForGroup("a", 5);
        ExecutionLimits snapshot = original.Snapshot();

        // Mutate original after snapshot
        original.ForGroup("a", 99);
        original.ForGroup("b", 10);

        Assert.That(snapshot["a"], Is.EqualTo(5));
        Assert.That(snapshot.Count, Is.EqualTo(1));
        Assert.That(snapshot.ContainsKey("b"), Is.False);
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
        StdSchedulerFactory factory = new(props);
        IScheduler scheduler = await factory.GetScheduler().ConfigureAwait(false);
        try
        {
            ExecutionLimits limits = await scheduler.GetExecutionLimits().ConfigureAwait(false);
            Assert.That(limits, Is.Not.Null);
            Assert.That(limits["batch-jobs"], Is.EqualTo(2));
            Assert.That(limits["high-cpu"], Is.EqualTo(5));
            Assert.That(limits[ExecutionLimits.DefaultGroupKey], Is.EqualTo(10));
            Assert.That(limits[ExecutionLimits.OtherGroups], Is.EqualTo(3));
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
        StdSchedulerFactory factory = new(props);
        IScheduler scheduler = await factory.GetScheduler().ConfigureAwait(false);
        try
        {
            ExecutionLimits limits = await scheduler.GetExecutionLimits().ConfigureAwait(false);
            Assert.That(limits, Is.Not.Null);
            Assert.That(limits["a"], Is.Null);  // "unlimited" → null
            Assert.That(limits["b"], Is.Null);  // "none" → null
            Assert.That(limits["c"], Is.Null);  // "null" value → null (unlimited)
            Assert.That(limits["d"], Is.EqualTo(5));
            Assert.That(limits[ExecutionLimits.DefaultGroupKey], Is.EqualTo(8)); // "_" key → default group
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
        StdSchedulerFactory factory = new(props);
        IScheduler scheduler = await factory.GetScheduler().ConfigureAwait(false);
        try
        {
            ExecutionLimits limits = await scheduler.GetExecutionLimits().ConfigureAwait(false);
            Assert.That(limits, Is.Not.Null);
            Assert.That(limits[ExecutionLimits.DefaultGroupKey], Is.EqualTo(7));
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
        StdSchedulerFactory factory = new(props);
        IScheduler scheduler = await factory.GetScheduler().ConfigureAwait(false);
        try
        {
            ExecutionLimits limits = await scheduler.GetExecutionLimits().ConfigureAwait(false);
            Assert.That(limits, Is.Null);
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
        StdSchedulerFactory factory = new(props);
        Assert.ThrowsAsync<SchedulerConfigException>(
            async () => await factory.GetScheduler().ConfigureAwait(false));
    }

    [Test]
    public void ComputeAvailableLimits_SubtractsRunningCounts()
    {
        // Simulate what ComputeAvailableExecutionGroupLimits does
        ExecutionLimits limits = new ExecutionLimits()
            .ForGroup("batch", 5)
            .ForGroup("cpu", 3)
            .ForOtherGroups(10);

        Dictionary<string, int?> available = limits.ToWorkingCopy();

        // Simulate 2 batch jobs and 1 cpu job running
        available["batch"] = Math.Max(available["batch"]!.Value - 2, 0); // 5 - 2 = 3
        available["cpu"] = Math.Max(available["cpu"]!.Value - 1, 0);     // 3 - 1 = 2

        Assert.That(available["batch"], Is.EqualTo(3));
        Assert.That(available["cpu"], Is.EqualTo(2));
        Assert.That(available[ExecutionLimits.OtherGroups], Is.EqualTo(10));
    }

    [Test]
    public void ComputeAvailableLimits_ClampsToZero()
    {
        ExecutionLimits limits = new ExecutionLimits().ForGroup("batch", 2);
        Dictionary<string, int?> available = limits.ToWorkingCopy();

        // Simulate 5 batch jobs running (more than the limit)
        available["batch"] = Math.Max(available["batch"]!.Value - 5, 0);
        Assert.That(available["batch"], Is.EqualTo(0));
    }

    [Test]
    public void ComputeAvailableLimits_UnlistedGroupUsesDefault()
    {
        ExecutionLimits limits = new ExecutionLimits()
            .ForGroup("batch", 5)
            .ForOtherGroups(3);

        Dictionary<string, int?> available = limits.ToWorkingCopy();

        // "unknown" group is not listed, should fall back to OtherGroups
        Assert.That(available.ContainsKey("unknown"), Is.False);
        // After CheckExecutionLimits, it should use and track the default
        Assert.That(ExecutionLimits.CheckExecutionLimits("unknown", available), Is.True);
        Assert.That(available["unknown"], Is.EqualTo(2)); // 3 - 1 = 2
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context) => default;
    }
}
