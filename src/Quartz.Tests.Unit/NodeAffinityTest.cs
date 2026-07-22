using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit;

public sealed class NodeAffinityTest
{
    [Test]
    public void TriggerBuilder_WithPreferredNode_SetsProperty()
    {
        var trigger = TriggerBuilder.Create()
            .WithIdentity("t1", "g1")
            .ForJob("j1")
            .WithPreferredNode("node-1")
            .Build();

        Assert.That(trigger.PreferredNode, Is.EqualTo("node-1"));
        Assert.That(trigger.IsPreferredNodeAuto, Is.False);
    }

    [Test]
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void TriggerBuilder_WithPreferredNode_Blank_ClearsProperty(string value)
    {
        var trigger = TriggerBuilder.Create()
            .WithIdentity("t1", "g1")
            .ForJob("j1")
            .WithPreferredNode(value)
            .Build();

        Assert.That(trigger.PreferredNode, Is.Null);
        Assert.That(trigger.IsPreferredNodeAuto, Is.False);
    }

    [Test]
    public void TriggerBuilder_WithPreferredNode_TrimsWhitespace()
    {
        var trigger = TriggerBuilder.Create()
            .WithIdentity("t1", "g1")
            .ForJob("j1")
            .WithPreferredNode("  node-1  ")
            .Build();

        Assert.That(trigger.PreferredNode, Is.EqualTo("node-1"));
    }

    [Test]
    public void TriggerBuilder_WithPreferredNode_AutoPinSentinel()
    {
        var trigger = TriggerBuilder.Create()
            .WithIdentity("t1", "g1")
            .ForJob("j1")
            .WithPreferredNode("*")
            .Build();

        // "*" requests auto-pin but is not itself an auto-claim until a node fires the trigger
        Assert.That(trigger.PreferredNode, Is.EqualTo("*"));
        Assert.That(trigger.IsPreferredNodeAuto, Is.False);
    }

    [Test]
    [TestCase("auto:nodeA")]
    [TestCase("prod-auto:region1")]
    public void TriggerBuilder_WithPreferredNode_AllowsAnyNodeName(string value)
    {
        // No substring is reserved: the auto-claim flag lives in its own column, so node names
        // are stored verbatim and can never collide with an internal marker.
        var trigger = TriggerBuilder.Create()
            .WithIdentity("t1", "g1")
            .ForJob("j1")
            .WithPreferredNode(value)
            .Build();

        Assert.That(trigger.PreferredNode, Is.EqualTo(value));
    }

    [Test]
    public void GetTriggerBuilder_ExplicitPin_StaysExplicit()
    {
        var trigger = TriggerBuilder.Create()
            .WithIdentity("t1", "g1")
            .ForJob("j1")
            .WithPreferredNode("nodeA")
            .Build();

        var rebuilt = trigger.GetTriggerBuilder().Build();

        Assert.That(rebuilt.PreferredNode, Is.EqualTo("nodeA"));
        Assert.That(rebuilt.IsPreferredNodeAuto, Is.False);
    }

    [Test]
    public void GetTriggerBuilder_AutoPinnedTrigger_RoundTripsAutoPin()
    {
        var trigger = new SimpleTriggerImpl("t1", "g1") { JobKey = new JobKey("j1") };
        // Simulates what the auto-pin claim in TriggerFired (and the database read) does
        trigger.SetPreferredNodeRaw("nodeA", auto: true);

        var rebuilt = trigger.GetTriggerBuilder().Build();

        // Rebuilding preserves the auto-claim, so the trigger still resets to the "*" sentinel
        // if nodeA dies rather than hardening into an explicit pin.
        Assert.That(rebuilt.PreferredNode, Is.EqualTo("nodeA"));
        Assert.That(rebuilt.IsPreferredNodeAuto, Is.True);
    }

    [Test]
    public void PreferredNode_PublicSetter_ClearsAutoClaim()
    {
        var trigger = new SimpleTriggerImpl("t1", "g1");
        trigger.SetPreferredNodeRaw("nodeA", auto: true);
        Assert.That(trigger.IsPreferredNodeAuto, Is.True);

        // An explicit assignment is always an explicit pin
        trigger.PreferredNode = "nodeB";

        Assert.That(trigger.PreferredNode, Is.EqualTo("nodeB"));
        Assert.That(trigger.IsPreferredNodeAuto, Is.False);
    }

    [Test]
    public void SetPreferredNodeRaw_StoresNodeNameVerbatim()
    {
        var trigger = new SimpleTriggerImpl("t1", "g1");
        trigger.SetPreferredNodeRaw("nodeA", auto: true);

        Assert.That(trigger.PreferredNode, Is.EqualTo("nodeA"));
        Assert.That(trigger.IsPreferredNodeAuto, Is.True);

        // Copy/assign to another trigger works and records an explicit pin
        var other = new SimpleTriggerImpl("t2", "g2");
        other.PreferredNode = trigger.PreferredNode;
        Assert.That(other.PreferredNode, Is.EqualTo("nodeA"));
        Assert.That(other.IsPreferredNodeAuto, Is.False);
    }

    [Test]
    public void SetPreferredNodeRaw_BlankValue_ClearsAutoClaim()
    {
        var trigger = new SimpleTriggerImpl("t1", "g1");

        trigger.SetPreferredNodeRaw(null, auto: true);

        Assert.That(trigger.PreferredNode, Is.Null);
        Assert.That(trigger.IsPreferredNodeAuto, Is.False);
    }

    [Test]
    public void AbstractTrigger_PreferredNode_CloneCopiesValue()
    {
        var trigger = new SimpleTriggerImpl("t1", "g1");
        trigger.SetPreferredNodeRaw("node-1", auto: true);

        var clone = (SimpleTriggerImpl) trigger.Clone();

        Assert.That(clone.PreferredNode, Is.EqualTo("node-1"));
        Assert.That(clone.IsPreferredNodeAuto, Is.True);
    }

    [Test]
    public void TriggerDetailsUpdate_WithPreferredNode_SetsValue()
    {
        var update = new TriggerDetailsUpdate().WithPreferredNode("  nodeB  ");

        Assert.That(update.HasPreferredNode, Is.True);
        Assert.That(update.PreferredNode, Is.EqualTo("nodeB"));
    }

    [Test]
    public void TriggerDetailsUpdate_WithPreferredNode_Null_Clears()
    {
        var update = new TriggerDetailsUpdate().WithPreferredNode(null);

        Assert.That(update.HasPreferredNode, Is.True);
        Assert.That(update.PreferredNode, Is.Null);
    }

    [Test]
    public void TriggerDetailsUpdate_WithoutPreferredNode_HasFlagIsFalse()
    {
        var update = new TriggerDetailsUpdate().WithDescription("x");

        Assert.That(update.HasPreferredNode, Is.False);
    }

    [Test]
    public async Task RamJobStore_PreferredNode_IsMetadataOnly_TriggerStillFires()
    {
        // RAMJobStore is single-node by definition, so a pin is carried as metadata and never
        // filters acquisition — a trigger pinned to another node must still fire.
        var store = new RAMJobStore();
        await store.Initialize(null!, new SampleSignaler());

        var job = JobBuilder.Create<TestJob>().WithIdentity("j1").Build();
        var trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("t1")
            .ForJob(job)
            .WithPreferredNode("some-other-node")
            .StartAt(DateTimeOffset.UtcNow.AddMilliseconds(-1000))
            .Build();
        trigger.ComputeFirstFireTimeUtc(null);

        await store.StoreJobAndTrigger(job, trigger);

        var retrieved = await store.RetrieveTrigger(trigger.Key);
        Assert.That(retrieved!.PreferredNode, Is.EqualTo("some-other-node"), "the pin round-trips as metadata");

        var acquired = await store.AcquireNextTriggers(DateTimeOffset.UtcNow.AddSeconds(10), 1, TimeSpan.Zero);
        Assert.That(acquired, Has.Count.EqualTo(1), "RAMJobStore must ignore the pin when acquiring");
    }

    private sealed class TestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context) => default;
    }

    private sealed class SampleSignaler : ISchedulerSignaler
    {
        public ValueTask NotifyTriggerListenersMisfired(ITrigger trigger, CancellationToken cancellationToken = default) => default;
        public ValueTask NotifySchedulerListenersFinalized(ITrigger trigger, CancellationToken cancellationToken = default) => default;
        public ValueTask NotifySchedulerListenersJobDeleted(JobKey jobKey, CancellationToken cancellationToken = default) => default;
        public ValueTask SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc, CancellationToken cancellationToken = default) => default;
        public ValueTask NotifySchedulerListenersError(string message, SchedulerException jpe, CancellationToken cancellationToken = default) => default;
    }
}
