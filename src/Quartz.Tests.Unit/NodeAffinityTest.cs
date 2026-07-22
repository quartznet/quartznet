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
    [TestCase("AUTO:nodeA")]
    [TestCase("Auto:nodeA")]
    [TestCase("prod-auto:region1")]
    public void WithPreferredNode_AutoPrefix_Throws(string value)
    {
        Assert.That(
            () => TriggerBuilder.Create().WithPreferredNode(value),
            Throws.ArgumentException);

        Assert.That(
            () => new TriggerDetailsUpdate().WithPreferredNode(value),
            Throws.ArgumentException);
    }

    [Test]
    public void GetTriggerBuilder_AutoPinnedTrigger_ProducesExplicitPin()
    {
        SimpleTriggerImpl trigger = new SimpleTriggerImpl("t1", "g1");
        trigger.JobName = "j1";
        // Use internal raw setter (simulates what TriggerFired/DB read does)
        ((Quartz.Spi.INextVersionTrigger) trigger).SetPreferredNodeRaw("auto:nodeA");

        ITrigger rebuilt = trigger.GetTriggerBuilder().Build();

        // Auto-pinned triggers strip "auto:" prefix and produce plain node name
        // as an explicit pin, preserving node affinity through clone/reschedule.
        AbstractTrigger at = (AbstractTrigger) rebuilt;
        Assert.That(at.PreferredNode, Is.EqualTo("nodeA"));
    }

    [Test]
    [TestCase("auto:nodeA")]
    [TestCase("AUTO:nodeA")]
    [TestCase("Auto:nodeA")]
    public void PreferredNode_Setter_RejectsAutoPrefix(string value)
    {
        SimpleTriggerImpl trigger = new SimpleTriggerImpl("t1", "g1");

        Assert.That(
            () => trigger.PreferredNode = value,
            Throws.ArgumentException);
    }

    [Test]
    public void SetPreferredNodeRaw_AcceptsAutoPrefix()
    {
        SimpleTriggerImpl trigger = new SimpleTriggerImpl("t1", "g1");
        Quartz.Spi.INextVersionTrigger nvt = (Quartz.Spi.INextVersionTrigger) trigger;
        nvt.SetPreferredNodeRaw("auto:nodeA");

        // Public getter normalizes (strips "auto:" prefix)
        Assert.That(trigger.PreferredNode, Is.EqualTo("nodeA"));
        // Internal interface getter returns raw value
        Assert.That(nvt.PreferredNode, Is.EqualTo("auto:nodeA"));
    }

    [Test]
    public void PreferredNode_PublicGetter_NormalizesAutoPrefix()
    {
        SimpleTriggerImpl trigger = new SimpleTriggerImpl("t1", "g1");
        ((Quartz.Spi.INextVersionTrigger) trigger).SetPreferredNodeRaw("auto:nodeA");

        // Public getter returns plain node name — safe for copy/assign
        Assert.That(trigger.PreferredNode, Is.EqualTo("nodeA"));

        // Assignment to another trigger works (no ArgumentException)
        SimpleTriggerImpl other = new SimpleTriggerImpl("t2", "g2");
        other.PreferredNode = trigger.PreferredNode;
        Assert.That(other.PreferredNode, Is.EqualTo("nodeA"));
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
