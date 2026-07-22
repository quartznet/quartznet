using System;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit;

public sealed class NodeAffinityTest
{
    [Test]
    public void TriggerBuilder_WithPreferredNode_SetsProperty()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t1")
            .WithPreferredNode("node-1")
            .Build();

        AbstractTrigger at = (AbstractTrigger) trigger;
        Assert.That(at.PreferredNode, Is.EqualTo("node-1"));
    }

    [Test]
    public void TriggerBuilder_WithPreferredNode_Null_ClearsProperty()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t1")
            .WithPreferredNode("node-1")
            .WithPreferredNode(null)
            .Build();

        AbstractTrigger at = (AbstractTrigger) trigger;
        Assert.That(at.PreferredNode, Is.Null);
    }

    [Test]
    public void TriggerBuilder_WithPreferredNode_EmptyString_ClearsProperty()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t1")
            .WithPreferredNode("node-1")
            .WithPreferredNode("")
            .Build();

        AbstractTrigger at = (AbstractTrigger) trigger;
        Assert.That(at.PreferredNode, Is.Null);
    }

    [Test]
    public void TriggerBuilder_WithPreferredNode_Whitespace_ClearsProperty()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t1")
            .WithPreferredNode("node-1")
            .WithPreferredNode("  ")
            .Build();

        AbstractTrigger at = (AbstractTrigger) trigger;
        Assert.That(at.PreferredNode, Is.Null);
    }

    [Test]
    public void TriggerBuilder_WithPreferredNode_TrimsWhitespace()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t1")
            .WithPreferredNode("  node-1  ")
            .Build();

        AbstractTrigger at = (AbstractTrigger) trigger;
        Assert.That(at.PreferredNode, Is.EqualTo("node-1"));
    }

    [Test]
    public void TriggerBuilder_WithPreferredNode_AutoPinSentinel()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t1")
            .WithPreferredNode("*")
            .Build();

        AbstractTrigger at = (AbstractTrigger) trigger;
        Assert.That(at.PreferredNode, Is.EqualTo("*"));
    }

    [Test]
    public void TriggerBuilder_GetTriggerBuilder_RoundTrips()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t1", "g1")
            .ForJob("j1")
            .WithPreferredNode("node-1")
            .Build();

        TriggerBuilder builder = trigger.GetTriggerBuilder();
        ITrigger rebuilt = builder.Build();

        AbstractTrigger at = (AbstractTrigger) rebuilt;
        Assert.That(at.PreferredNode, Is.EqualTo("node-1"));
    }

    [Test]
    public void TriggerBuilder_WithPreferredNode_AndExecutionGroup_BothSet()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t1")
            .WithExecutionGroup("batch-jobs")
            .WithPreferredNode("node-1")
            .Build();

        AbstractTrigger at = (AbstractTrigger) trigger;
        Assert.That(at.ExecutionGroup, Is.EqualTo("batch-jobs"));
        Assert.That(at.PreferredNode, Is.EqualTo("node-1"));
    }

    [Test]
    public void TriggerDetailsUpdate_WithPreferredNode_SetsHasFlag()
    {
        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithPreferredNode("node-1");

        Assert.That(update.HasPreferredNode, Is.True);
    }

    [Test]
    public void TriggerDetailsUpdate_WithPreferredNode_Null_SetsHasFlag()
    {
        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithPreferredNode(null);

        Assert.That(update.HasPreferredNode, Is.True);
        Assert.That(update.PreferredNode, Is.Null);
    }

    [Test]
    public void TriggerDetailsUpdate_WithoutPreferredNode_HasFlagIsFalse()
    {
        TriggerDetailsUpdate update = new TriggerDetailsUpdate()
            .WithDescription("desc");

        Assert.That(update.HasPreferredNode, Is.False);
    }

    [Test]
    public void AbstractTrigger_PreferredNode_SetAndGet()
    {
        SimpleTriggerImpl trigger = new SimpleTriggerImpl("t1", "g1");
        trigger.PreferredNode = "node-1";
        Assert.That(trigger.PreferredNode, Is.EqualTo("node-1"));
    }

    [Test]
    public void AbstractTrigger_PreferredNode_CloneCopiesValue()
    {
        SimpleTriggerImpl trigger = new SimpleTriggerImpl("t1", "g1");
        trigger.JobName = "j1";
        trigger.PreferredNode = "node-1";

        SimpleTriggerImpl clone = (SimpleTriggerImpl) trigger.Clone();
        Assert.That(clone.PreferredNode, Is.EqualTo("node-1"));
    }

    [Test]
    [TestCase("auto:nodeA")]
    [TestCase("prod-auto:region1")]
    public void WithPreferredNode_AllowsAnyNodeName(string value)
    {
        // No substring is reserved: the auto-claim flag lives in its own column, so node
        // names are stored verbatim and can never collide with an internal marker.
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t1", "g1")
            .ForJob("j1")
            .WithPreferredNode(value)
            .Build();

        Assert.That(((AbstractTrigger) trigger).PreferredNode, Is.EqualTo(value));
        Assert.That(((AbstractTrigger) trigger).IsPreferredNodeAuto, Is.False);

        Assert.That(new TriggerDetailsUpdate().WithPreferredNode(value).PreferredNode, Is.EqualTo(value));
    }

    [Test]
    public void GetTriggerBuilder_AutoPinnedTrigger_RoundTripsAutoPin()
    {
        SimpleTriggerImpl trigger = new SimpleTriggerImpl("t1", "g1");
        trigger.JobName = "j1";
        // Simulates what the auto-pin claim in TriggerFired (and the DB read) does
        ((Quartz.Spi.INextVersionTrigger) trigger).SetPreferredNodeRaw("nodeA", auto: true);

        ITrigger rebuilt = trigger.GetTriggerBuilder().Build();

        // Rebuilding preserves the auto-claim, so the trigger still resets to the "*"
        // sentinel if nodeA dies rather than hardening into an explicit pin.
        AbstractTrigger at = (AbstractTrigger) rebuilt;
        Assert.That(at.PreferredNode, Is.EqualTo("nodeA"));
        Assert.That(at.IsPreferredNodeAuto, Is.True);
    }

    [Test]
    public void GetTriggerBuilder_ExplicitPin_StaysExplicit()
    {
        SimpleTriggerImpl trigger = new SimpleTriggerImpl("t1", "g1");
        trigger.JobName = "j1";
        trigger.PreferredNode = "nodeA";

        AbstractTrigger rebuilt = (AbstractTrigger) trigger.GetTriggerBuilder().Build();

        Assert.That(rebuilt.PreferredNode, Is.EqualTo("nodeA"));
        Assert.That(rebuilt.IsPreferredNodeAuto, Is.False);
    }

    [Test]
    public void PreferredNode_PublicSetter_ClearsAutoClaim()
    {
        SimpleTriggerImpl trigger = new SimpleTriggerImpl("t1", "g1");
        Quartz.Spi.INextVersionTrigger nvt = (Quartz.Spi.INextVersionTrigger) trigger;
        nvt.SetPreferredNodeRaw("nodeA", auto: true);
        Assert.That(trigger.IsPreferredNodeAuto, Is.True);

        // An explicit assignment is always an explicit pin
        trigger.PreferredNode = "nodeB";

        Assert.That(trigger.PreferredNode, Is.EqualTo("nodeB"));
        Assert.That(trigger.IsPreferredNodeAuto, Is.False);
    }

    [Test]
    public void SetPreferredNodeRaw_StoresNodeNameVerbatim()
    {
        SimpleTriggerImpl trigger = new SimpleTriggerImpl("t1", "g1");
        Quartz.Spi.INextVersionTrigger nvt = (Quartz.Spi.INextVersionTrigger) trigger;
        nvt.SetPreferredNodeRaw("nodeA", auto: true);

        // Public and internal getters agree — there is no prefix to strip
        Assert.That(trigger.PreferredNode, Is.EqualTo("nodeA"));
        Assert.That(nvt.PreferredNode, Is.EqualTo("nodeA"));
        Assert.That(nvt.IsPreferredNodeAuto, Is.True);

        // Copy/assign to another trigger works and records an explicit pin
        SimpleTriggerImpl other = new SimpleTriggerImpl("t2", "g2");
        other.PreferredNode = trigger.PreferredNode;
        Assert.That(other.PreferredNode, Is.EqualTo("nodeA"));
        Assert.That(other.IsPreferredNodeAuto, Is.False);
    }

    [Test]
    public void SetPreferredNodeRaw_BlankValue_ClearsAutoClaim()
    {
        SimpleTriggerImpl trigger = new SimpleTriggerImpl("t1", "g1");
        Quartz.Spi.INextVersionTrigger nvt = (Quartz.Spi.INextVersionTrigger) trigger;

        nvt.SetPreferredNodeRaw(null, auto: true);

        Assert.That(trigger.PreferredNode, Is.Null);
        Assert.That(trigger.IsPreferredNodeAuto, Is.False);
    }

    [Test]
    public void TriggerDetailsUpdate_WithPreferredNode_AutoPinSentinel()
    {
        TriggerDetailsUpdate update = new TriggerDetailsUpdate().WithPreferredNode("*");

        Assert.That(update.HasPreferredNode, Is.True);
        Assert.That(update.PreferredNode, Is.EqualTo("*"));
    }

    [Test]
    public async Task RamJobStore_PreferredNode_IsMetadataOnly_TriggerStillFires()
    {
        // RAMJobStore is single-node: the pin is preserved as metadata but has no
        // effect on acquisition — a trigger pinned to some other node still fires.
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "NodeAffinityRamTest",
            ["quartz.scheduler.instanceId"] = "ramNode",
            ["quartz.threadPool.threadCount"] = "2",
        };
        IScheduler scheduler = await new StdSchedulerFactory(properties).GetScheduler();
        try
        {
            CountingJob.Reset();
            await scheduler.Start();

            IJobDetail job = JobBuilder.Create<CountingJob>()
                .WithIdentity("ramPinJob")
                .StoreDurably()
                .Build();
            await scheduler.AddJob(job, true);

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("ramPinTrigger")
                .ForJob(job)
                .WithPreferredNode("some-other-node")
                .WithSimpleSchedule(s => s
                    .WithInterval(TimeSpan.FromMilliseconds(250))
                    .RepeatForever())
                .StartNow()
                .Build();
            await scheduler.ScheduleJob(trigger);

            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (CountingJob.ExecutionCount < 1 && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(100);
            }
            Assert.That(CountingJob.ExecutionCount, Is.GreaterThanOrEqualTo(1),
                "RAMJobStore must ignore the pin and fire the trigger");

            // Round-trip: the pin is preserved as metadata
            ITrigger retrieved = await scheduler.GetTrigger(trigger.Key);
            Assert.That(((AbstractTrigger) retrieved).PreferredNode, Is.EqualTo("some-other-node"));

            // Runtime update and clear via TriggerDetailsUpdate
            await scheduler.UpdateTriggerDetails(
                trigger.Key, new TriggerDetailsUpdate().WithPreferredNode("another-node"));
            retrieved = await scheduler.GetTrigger(trigger.Key);
            Assert.That(((AbstractTrigger) retrieved).PreferredNode, Is.EqualTo("another-node"));

            await scheduler.UpdateTriggerDetails(
                trigger.Key, new TriggerDetailsUpdate().WithPreferredNode(null));
            retrieved = await scheduler.GetTrigger(trigger.Key);
            Assert.That(((AbstractTrigger) retrieved).PreferredNode, Is.Null);
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    public sealed class CountingJob : IJob
    {
        private static int executionCount;

        public static int ExecutionCount => Volatile.Read(ref executionCount);

        public static void Reset() => Interlocked.Exchange(ref executionCount, 0);

        public Task Execute(IJobExecutionContext context)
        {
            Interlocked.Increment(ref executionCount);
            return Task.CompletedTask;
        }
    }
}
