using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Impl.Triggers;
using Quartz.Simpl;
using Quartz.Spi;

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

        AbstractTrigger at = (AbstractTrigger) trigger;
        Assert.That(at.ExecutionGroup, Is.EqualTo("batch-jobs"));
    }

    [Test]
    public void TriggerBuilder_WithExecutionGroup_Null_ClearsProperty()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t1")
            .WithExecutionGroup("batch-jobs")
            .WithExecutionGroup(null)
            .Build();

        AbstractTrigger at = (AbstractTrigger) trigger;
        Assert.That(at.ExecutionGroup, Is.Null);
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

    public class NoOpJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }

    private sealed class NullSignaler : ISchedulerSignaler
    {
        public Task NotifyTriggerListenersMisfired(ITrigger trigger, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifySchedulerListenersFinalized(ITrigger trigger, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifySchedulerListenersJobDeleted(JobKey jobKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc, CancellationToken cancellationToken = default) { }
        public Task NotifySchedulerListenersError(string message, SchedulerException jpe, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
