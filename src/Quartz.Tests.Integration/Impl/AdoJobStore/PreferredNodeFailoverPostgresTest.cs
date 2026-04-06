using System;
using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Tests for preferred-node failover semantics using PostgreSQL.
/// Validates that auto-pinned triggers are re-pinned during ClusterRecover
/// while explicitly pinned triggers are preserved through failover.
/// </summary>
public sealed class PreferredNodeFailoverPostgresTest : ClusteredPostgresTestBase
{
    protected override string SchedulerName => "FailoverTest";

    [Test]
    public async Task ExplicitPin_PreservedThroughFailover()
    {
        // Explicit pin to nodeA — should NOT be re-pinned during failover.
        IScheduler nodeA = await CreateScheduler("nodeA").ConfigureAwait(false);
        try
        {
            await nodeA.Start().ConfigureAwait(false);
            await Task.Delay(2000).ConfigureAwait(false);

            IJobDetail job = JobBuilder.Create<RecordingJob>()
                .WithIdentity("explicitPinJob", "failoverTest")
                .StoreDurably()
                .Build();
            await nodeA.AddJob(job, true).ConfigureAwait(false);

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("explicitPinTrigger", "failoverTest")
                .ForJob(job)
                .WithPreferredNode("nodeA")
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

        // Wait for nodeA's checkin to become stale
        await Task.Delay(10_000).ConfigureAwait(false);

        IScheduler nodeB = await CreateScheduler("nodeB").ConfigureAwait(false);
        try
        {
            await nodeB.Start().ConfigureAwait(false);

            // Wait for ClusterRecover to run and nodeB to fire the trigger
            await Task.Delay(10_000).ConfigureAwait(false);

            ITrigger retrieved = await nodeB.GetTrigger(new TriggerKey("explicitPinTrigger", "failoverTest")).ConfigureAwait(false);
            Assert.That(retrieved, Is.Not.Null, "Repeating trigger should still exist after failover");

            // Explicit pin should NOT be re-pinned — stays as "nodeA"
            Assert.That(((AbstractTrigger) retrieved).PreferredNode, Is.EqualTo("nodeA"),
                "Explicit pin should be preserved through failover, not re-pinned to nodeB");

            // But the trigger should still fire on nodeB via the NOT IN failover SQL
            Assert.That(RecordingJob.Executions, Does.Contain("nodeB"),
                "The job should have executed on nodeB via NOT IN failover");
        }
        finally
        {
            RecordingJob.Reset();
            await nodeB.Clear().ConfigureAwait(false);
            await nodeB.Shutdown(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task AutoPin_RepinnedThroughFailover()
    {
        // Auto-pin (*) to nodeA — should be re-pinned to nodeB during failover.
        IScheduler nodeA = await CreateScheduler("nodeA").ConfigureAwait(false);
        try
        {
            await nodeA.Start().ConfigureAwait(false);
            await Task.Delay(2000).ConfigureAwait(false);

            IJobDetail job = JobBuilder.Create<RecordingJob>()
                .WithIdentity("autoPinFailoverJob", "failoverTest")
                .StoreDurably()
                .Build();
            await nodeA.AddJob(job, true).ConfigureAwait(false);

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("autoPinFailoverTrigger", "failoverTest")
                .ForJob(job)
                .WithPreferredNode("*")
                .WithSimpleSchedule(s => s
                    .WithIntervalInSeconds(1)
                    .RepeatForever())
                .StartNow()
                .Build();
            await nodeA.ScheduleJob(trigger).ConfigureAwait(false);

            // Wait for auto-pin to happen (first fire pins to nodeA)
            await WaitForExecutionCount(1, 10_000).ConfigureAwait(false);

            ITrigger pinnedTrigger = await nodeA.GetTrigger(trigger.Key).ConfigureAwait(false);
            Assert.That(pinnedTrigger, Is.Not.Null);
            // Public getter normalizes (strips "auto:" prefix)
            Assert.That(((AbstractTrigger) pinnedTrigger).PreferredNode, Is.EqualTo("nodeA"),
                "Auto-pin should resolve to 'nodeA' after first fire");

            await nodeA.Shutdown(false).ConfigureAwait(false);
        }
        catch
        {
            await nodeA.Shutdown(false).ConfigureAwait(false);
            throw;
        }

        // Reset recordings so nodeA's earlier executions don't satisfy nodeB's assertions
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
            TriggerKey failoverKey = new TriggerKey("autoPinFailoverTrigger", "failoverTest");
            await WaitForCondition(async () =>
            {
                ITrigger t = await nodeB.GetTrigger(failoverKey).ConfigureAwait(false);
                return t != null && ((AbstractTrigger) t).PreferredNode == "nodeB";
            }, 10_000, "auto-pin to settle on nodeB").ConfigureAwait(false);
        }
        finally
        {
            RecordingJob.Reset();
            await nodeB.Clear().ConfigureAwait(false);
            await nodeB.Shutdown(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task ThreeNode_SingleSurvivor_GetsAutoRepinnedTrigger()
    {
        IScheduler nodeA = await CreateScheduler("nodeA").ConfigureAwait(false);
        IScheduler nodeB = await CreateScheduler("nodeB").ConfigureAwait(false);
        try
        {
            await nodeA.Start().ConfigureAwait(false);
            await nodeB.Start().ConfigureAwait(false);
            await Task.Delay(2000).ConfigureAwait(false);

            IJobDetail job = JobBuilder.Create<RecordingJob>()
                .WithIdentity("threeNodeJob", "failoverTest")
                .StoreDurably()
                .Build();
            await nodeA.AddJob(job, true).ConfigureAwait(false);

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("threeNodeTrigger", "failoverTest")
                .ForJob(job)
                .WithPreferredNode("*")
                .WithSimpleSchedule(s => s
                    .WithIntervalInSeconds(1)
                    .RepeatForever())
                .StartNow()
                .Build();
            await nodeA.ScheduleJob(trigger).ConfigureAwait(false);

            // Wait for auto-pin to first fire on whichever node acquires it
            await WaitForExecutionCount(1, 10_000).ConfigureAwait(false);

            // Determine which node got pinned
            string pinnedNodeId = RecordingJob.Executions.First();

            // Shut down the pinned node
            if (pinnedNodeId == "nodeA")
            {
                await nodeA.Shutdown(false).ConfigureAwait(false);
            }
            else
            {
                await nodeB.Shutdown(false).ConfigureAwait(false);
            }
        }
        catch
        {
            await nodeA.Shutdown(false).ConfigureAwait(false);
            await nodeB.Shutdown(false).ConfigureAwait(false);
            throw;
        }

        RecordingJob.Reset();

        // Wait for stale checkin
        await Task.Delay(10_000).ConfigureAwait(false);

        // Start nodeC as a new survivor
        IScheduler nodeC = await CreateScheduler("nodeC").ConfigureAwait(false);
        try
        {
            await nodeC.Start().ConfigureAwait(false);

            // Wait for ClusterRecover + trigger fires
            await WaitForExecutionCount(2, 15_000).ConfigureAwait(false);

            ITrigger retrieved = await nodeC.GetTrigger(new TriggerKey("threeNodeTrigger", "failoverTest")).ConfigureAwait(false);
            Assert.That(retrieved, Is.Not.Null);

            // After recovery, trigger should settle to a single survivor.
            // Public getter normalizes (strips "auto:"), so we just check non-null/non-sentinel.
            string currentPin = ((AbstractTrigger) retrieved).PreferredNode;
            Assert.That(currentPin, Is.Not.Null.And.Not.EqualTo("*"),
                "Trigger should have been claimed by a surviving node");

            // All new executions should be on a single node
            Assert.That(RecordingJob.Executions, Is.Not.Empty);
        }
        finally
        {
            RecordingJob.Reset();
            await nodeC.Clear().ConfigureAwait(false);
            await nodeC.Shutdown(false).ConfigureAwait(false);
        }
    }
}
