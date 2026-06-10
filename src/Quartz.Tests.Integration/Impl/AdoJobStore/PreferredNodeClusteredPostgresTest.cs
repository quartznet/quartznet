using System;
using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Impl.Triggers;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Two-node clustered tests for preferred-node behavior using PostgreSQL.
/// Covers pin exclusion while the preferred node is alive, auto-pin on first
/// fire, and sticky failover re-pin via ClusterRecover.
/// Requires Docker (Testcontainers).
/// </summary>
public sealed class PreferredNodeClusteredPostgresTest : ClusteredPostgresTestBase
{
    protected override string SchedulerName => "PreferredNodeClusterTest";

    [Test]
    public async Task AutoPin_PinsToFirstFiringNode()
    {
        IScheduler scheduler = await CreateScheduler("autopin-node").ConfigureAwait(false);
        try
        {
            await scheduler.Start().ConfigureAwait(false);
            await Task.Delay(500).ConfigureAwait(false);

            IJobDetail job = JobBuilder.Create<RecordingJob>()
                .WithIdentity("autoPinJob", "clusteredTest")
                .StoreDurably()
                .Build();
            await scheduler.AddJob(job, true).ConfigureAwait(false);

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("autoPinTrigger", "clusteredTest")
                .ForJob(job)
                .WithPreferredNode("*")
                .WithSimpleSchedule(s => s
                    .WithIntervalInSeconds(1)
                    .RepeatForever())
                .StartNow()
                .Build();
            await scheduler.ScheduleJob(trigger).ConfigureAwait(false);

            // Wait for at least one fire, then verify immediately (trigger persists because RepeatForever)
            await WaitForExecutionCount(1, 10_000).ConfigureAwait(false);

            Assert.That(RecordingJob.Executions, Does.Contain("autopin-node"),
                "The job should have executed on the autopin-node scheduler");

            ITrigger retrieved = await scheduler.GetTrigger(trigger.Key).ConfigureAwait(false);
            Assert.That(retrieved, Is.Not.Null, "RepeatForever trigger should still exist after firing");
            // Public getter normalizes (strips "auto:" prefix)
            Assert.That(((AbstractTrigger) retrieved).PreferredNode, Is.EqualTo("autopin-node"),
                "Auto-pin should resolve to the firing node's instance id");
        }
        finally
        {
            await scheduler.Clear().ConfigureAwait(false);
            await scheduler.Shutdown(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task LiveNodePin_ExecutesOnPinnedNode()
    {
        IScheduler nodeA = await CreateScheduler("nodeA").ConfigureAwait(false);
        IScheduler nodeB = await CreateScheduler("nodeB").ConfigureAwait(false);
        try
        {
            await nodeA.Start().ConfigureAwait(false);
            await nodeB.Start().ConfigureAwait(false);
            await Task.Delay(2000).ConfigureAwait(false);

            IJobDetail job = JobBuilder.Create<RecordingJob>()
                .WithIdentity("exclusionJob", "clusteredTest")
                .StoreDurably()
                .Build();
            await nodeA.AddJob(job, true).ConfigureAwait(false);

            // Pin to nodeA, repeating so we can observe multiple fires
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("exclusionTrigger", "clusteredTest")
                .ForJob(job)
                .WithPreferredNode("nodeA")
                .WithSimpleSchedule(s => s
                    .WithIntervalInSeconds(1)
                    .WithRepeatCount(3))
                .StartNow()
                .Build();
            await nodeA.ScheduleJob(trigger).ConfigureAwait(false);

            // Wait for several fires
            await Task.Delay(6000).ConfigureAwait(false);

            // All recorded executions should be on nodeA, none on nodeB
            Assert.That(RecordingJob.Executions, Is.Not.Empty, "Job should have executed at least once");
            Assert.That(RecordingJob.Executions, Has.All.EqualTo("nodeA"),
                "While nodeA is alive, pinned trigger should only execute on nodeA");

            ITrigger retrieved = await nodeA.GetTrigger(trigger.Key).ConfigureAwait(false);
            if (retrieved != null)
            {
                Assert.That(((AbstractTrigger) retrieved).PreferredNode, Is.EqualTo("nodeA"));
            }
        }
        finally
        {
            await nodeA.Clear().ConfigureAwait(false);
            await nodeA.Shutdown(false).ConfigureAwait(false);
            await nodeB.Shutdown(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task Failover_DeadNodeTriggerRepinnedToSurvivor()
    {
        IScheduler nodeA = await CreateScheduler("nodeA").ConfigureAwait(false);
        try
        {
            await nodeA.Start().ConfigureAwait(false);
            await Task.Delay(2000).ConfigureAwait(false);

            IJobDetail job = JobBuilder.Create<RecordingJob>()
                .WithIdentity("failoverJob", "clusteredTest")
                .StoreDurably()
                .Build();
            await nodeA.AddJob(job, true).ConfigureAwait(false);

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("failoverTrigger", "clusteredTest")
                .ForJob(job)
                .WithPreferredNode("*")
                .WithSimpleSchedule(s => s
                    .WithIntervalInSeconds(1)
                    .RepeatForever())
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(3))
                .Build();
            await nodeA.ScheduleJob(trigger).ConfigureAwait(false);

            await nodeA.Shutdown(false).ConfigureAwait(false);
        }
        catch
        {
            await nodeA.Shutdown(false).ConfigureAwait(false);
            throw;
        }

        // Reset recordings so any nodeA executions don't satisfy nodeB's assertions
        RecordingJob.Reset();

        // Wait for nodeA's checkin to become stale
        await Task.Delay(10_000).ConfigureAwait(false);

        IScheduler nodeB = await CreateScheduler("nodeB").ConfigureAwait(false);
        try
        {
            await nodeB.Start().ConfigureAwait(false);

            // Wait for nodeB to execute the trigger (proves failover worked)
            await WaitForExecutionCount(1, 15_000).ConfigureAwait(false);

            Assert.That(RecordingJob.Executions, Does.Contain("nodeB"),
                "The job should have executed on nodeB after failover");

            // Poll until auto-pin settles (sentinel "*" resolved to "nodeB" after first fire)
            TriggerKey failoverKey = new TriggerKey("failoverTrigger", "clusteredTest");
            await WaitForCondition(async () =>
            {
                ITrigger t = await nodeB.GetTrigger(failoverKey).ConfigureAwait(false);
                return t != null && ((AbstractTrigger) t).PreferredNode == "nodeB";
            }, 10_000, "auto-pin to settle on nodeB").ConfigureAwait(false);
        }
        finally
        {
            await nodeB.Clear().ConfigureAwait(false);
            await nodeB.Shutdown(false).ConfigureAwait(false);
        }
    }
}
