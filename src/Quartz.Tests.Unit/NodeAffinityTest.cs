using Quartz.Impl.Triggers;
using Quartz.Impl;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit;

public sealed class NodeAffinityTest
{
    [Test]
    public void PreferredNode_None_IsTheDefault()
    {
        default(PreferredNode).Should().Be(PreferredNode.None);
        PreferredNode.None.IsNone.Should().BeTrue();
        PreferredNode.None.Node.Should().BeNull();
        PreferredNode.None.IsAutomatic.Should().BeFalse();
    }

    [Test]
    public void PreferredNode_Auto_IsAutomaticButUnclaimed()
    {
        PreferredNode.Auto.IsNone.Should().BeFalse();
        PreferredNode.Auto.IsAutomatic.Should().BeTrue();
        PreferredNode.Auto.Node.Should().BeNull("no node has claimed the pin yet");
    }

    [Test]
    public void PreferredNode_For_NamesANode()
    {
        PreferredNode pin = PreferredNode.For("  node-1  ");

        pin.Node.Should().Be("node-1");
        pin.IsAutomatic.Should().BeFalse();
        pin.IsNone.Should().BeFalse();
    }

    [Test]
    [TestCase("*")]
    [TestCase("_")]
    [TestCase("null")]
    [TestCase("NULL")]
    [TestCase("")]
    [TestCase("   ")]
    public void PreferredNode_For_RejectsReservedNames(string value)
    {
        Action act = () => PreferredNode.For(value);

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    [TestCase("auto:nodeA")]
    [TestCase("prod-auto:region1")]
    public void PreferredNode_For_AllowsAnyOtherNodeName(string value)
    {
        // No substring is reserved: the auto-claim flag lives in its own column, so node names
        // are stored verbatim and can never collide with an internal marker.
        PreferredNode.For(value).Node.Should().Be(value);
    }

    [Test]
    public void TriggerBuilder_WithPreferredNode_SetsProperty()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t1", "g1")
            .ForJob("j1")
            .WithPreferredNode(PreferredNode.For("node-1"))
            .Build();

        trigger.PreferredNode.Should().Be(PreferredNode.For("node-1"));
        trigger.PreferredNode.IsAutomatic.Should().BeFalse();
    }

    [Test]
    public void TriggerBuilder_WithPreferredNode_None_ClearsProperty()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t1", "g1")
            .ForJob("j1")
            .WithPreferredNode(PreferredNode.None)
            .Build();

        trigger.PreferredNode.Should().Be(PreferredNode.None);
        trigger.PreferredNode.IsAutomatic.Should().BeFalse();
    }

    [Test]
    public void TriggerBuilder_WithPreferredNode_Auto()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t1", "g1")
            .ForJob("j1")
            .WithPreferredNode(PreferredNode.Auto)
            .Build();

        // Auto requests a pin but is not itself claimed until a node fires the trigger
        trigger.PreferredNode.Should().Be(PreferredNode.Auto);
        trigger.PreferredNode.Node.Should().BeNull();
        trigger.PreferredNode.IsAutomatic.Should().BeTrue();
    }

    [Test]
    public void GetTriggerBuilder_ExplicitPin_StaysExplicit()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t1", "g1")
            .ForJob("j1")
            .WithPreferredNode(PreferredNode.For("nodeA"))
            .Build();

        ITrigger rebuilt = trigger.GetTriggerBuilder().Build();

        rebuilt.PreferredNode.Should().Be(PreferredNode.For("nodeA"));
        rebuilt.PreferredNode.IsAutomatic.Should().BeFalse();
    }

    [Test]
    public void GetTriggerBuilder_AutoPinnedTrigger_RoundTripsAutoPin()
    {
        SimpleTriggerImpl trigger = new SimpleTriggerImpl { Key = new TriggerKey("t1", "g1"), StartTimeUtc = TimeProvider.System.GetUtcNow(), JobKey = new JobKey("j1") };
        // Simulates what the auto-pin claim in TriggerFired (and the database read) does
        trigger.SetPreferredNode(PreferredNode.ClaimedBy("nodeA"), markDirty: true);

        ITrigger rebuilt = trigger.GetTriggerBuilder().Build();

        // Rebuilding preserves the auto-claim, so the trigger is still released if nodeA dies
        // rather than hardening into a pin the user named.
        rebuilt.PreferredNode.Node.Should().Be("nodeA");
        rebuilt.PreferredNode.IsAutomatic.Should().BeTrue();
    }

    [Test]
    public void PreferredNode_Setter_RecordsTheValueAsGiven()
    {
        SimpleTriggerImpl trigger = new SimpleTriggerImpl { Key = new TriggerKey("t1", "g1"), StartTimeUtc = TimeProvider.System.GetUtcNow() };
        trigger.SetPreferredNode(PreferredNode.ClaimedBy("nodeA"), markDirty: true);
        trigger.PreferredNode.IsAutomatic.Should().BeTrue();

        trigger.PreferredNode = PreferredNode.For("nodeB");

        trigger.PreferredNode.Node.Should().Be("nodeB");
        trigger.PreferredNode.IsAutomatic.Should().BeFalse("PreferredNode.For names a pin explicitly");
    }

    [Test]
    public void PreferredNode_CopiedBetweenTriggers_KeepsItsAutoClaim()
    {
        SimpleTriggerImpl trigger = new SimpleTriggerImpl { Key = new TriggerKey("t1", "g1"), StartTimeUtc = TimeProvider.System.GetUtcNow() };
        trigger.SetPreferredNode(PreferredNode.ClaimedBy("nodeA"), markDirty: true);

        trigger.PreferredNode.Node.Should().Be("nodeA");
        trigger.PreferredNode.IsAutomatic.Should().BeTrue();

        // The value carries the auto-claim flag, so copying it is lossless
        SimpleTriggerImpl other = new SimpleTriggerImpl { Key = new TriggerKey("t2", "g2"), StartTimeUtc = TimeProvider.System.GetUtcNow() };
        other.PreferredNode = trigger.PreferredNode;

        other.PreferredNode.Should().Be(trigger.PreferredNode);
        other.PreferredNode.IsAutomatic.Should().BeTrue();
    }

    [Test]
    public void PreferredNode_None_ClearsAutoClaim()
    {
        SimpleTriggerImpl trigger = new SimpleTriggerImpl { Key = new TriggerKey("t1", "g1"), StartTimeUtc = TimeProvider.System.GetUtcNow() };
        trigger.SetPreferredNode(PreferredNode.ClaimedBy("nodeA"), markDirty: true);

        trigger.PreferredNode = PreferredNode.None;

        trigger.PreferredNode.Should().Be(PreferredNode.None);
        trigger.PreferredNode.IsAutomatic.Should().BeFalse();
    }

    [Test]
    public void TriggerBase_PreferredNode_CloneCopiesValue()
    {
        SimpleTriggerImpl trigger = new SimpleTriggerImpl { Key = new TriggerKey("t1", "g1"), StartTimeUtc = TimeProvider.System.GetUtcNow() };
        trigger.SetPreferredNode(PreferredNode.ClaimedBy("node-1"), markDirty: true);

        SimpleTriggerImpl clone = (SimpleTriggerImpl) trigger.Clone();

        clone.PreferredNode.Node.Should().Be("node-1");
        clone.PreferredNode.IsAutomatic.Should().BeTrue();
    }

    [Test]
    public void TriggerDetailsUpdate_WithPreferredNode_SetsValue()
    {
        TriggerDetailsUpdate update = new TriggerDetailsUpdate().WithPreferredNode(PreferredNode.For("  nodeB  "));

        update.HasPreferredNode.Should().BeTrue();
        update.PreferredNode.Node.Should().Be("nodeB");
    }

    [Test]
    public void TriggerDetailsUpdate_WithPreferredNode_None_Clears()
    {
        TriggerDetailsUpdate update = new TriggerDetailsUpdate().WithPreferredNode(PreferredNode.None);

        update.HasPreferredNode.Should().BeTrue();
        update.PreferredNode.Should().Be(PreferredNode.None);
    }

    [Test]
    public void TriggerDetailsUpdate_WithoutPreferredNode_HasFlagIsFalse()
    {
        TriggerDetailsUpdate update = new TriggerDetailsUpdate().WithDescription("x");

        update.HasPreferredNode.Should().BeFalse();
    }

    [Test]
    public async Task RamJobStore_PreferredNode_IsMetadataOnly_TriggerStillFires()
    {
        // RAMJobStore is single-node by definition, so a pin is carried as metadata and never
        // filters acquisition — a trigger pinned to another node must still fire.
        RAMJobStore store = TestJobStores.Ram();
        await store.Initialize();

        IJobDetail job = JobBuilder.Create<TestJob>().WithIdentity("j1").Build();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("t1")
            .ForJob(job)
            .WithPreferredNode(PreferredNode.For("some-other-node"))
            .StartAt(DateTimeOffset.UtcNow.AddMilliseconds(-1000))
            .Build();
        trigger.ComputeFirstFireTimeUtc(null);

        await store.ScheduleJob(job, trigger);

        IOperableTrigger retrieved = await store.GetTrigger(trigger.Key);
        retrieved!.PreferredNode.Node.Should().Be("some-other-node", "the pin round-trips as metadata");

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest { NoLaterThan = DateTimeOffset.UtcNow.AddSeconds(10), MaxCount = 1, TimeWindow = TimeSpan.Zero });
        acquired.Should().HaveCount(1, "RAMJobStore must ignore the pin when acquiring");
    }

    private sealed class TestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
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
