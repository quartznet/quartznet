using System.Linq;

using NUnit.Framework;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Two-node clustered tests for preferred-node behavior using PostgreSQL.
/// Covers pin exclusion while the preferred node is alive, auto-pin on first
/// fire, and sticky failover re-pin via ClusterRecover.
/// Runs against the assembly-wide PostgreSQL database (see ClusteredPostgresTestBase).
/// </summary>
[Category("db-postgres")]
[NonParallelizable]
public sealed class PreferredNodeClusteredPostgresTest : ClusteredPostgresTestBase
{
    protected override string SchedulerName => "PreferredNodeClusterTest";

    [Test]
    public async Task AutoPin_PinsToFirstFiringNode()
    {
        IScheduler scheduler = await CreateScheduler("autopin-node");
        try
        {
            await scheduler.Start();
            await Task.Delay(500);

            IJobDetail job = JobBuilder.Create<RecordingJob>()
                .WithIdentity("autoPinJob", "clusteredTest")
                .StoreDurably()
                .Build();
            await scheduler.AddJob(job, true);

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("autoPinTrigger", "clusteredTest")
                .ForJob(job)
                .WithPreferredNode("*")
                .WithSimpleSchedule(s => s
                    .WithIntervalInSeconds(1)
                    .RepeatForever())
                .StartNow()
                .Build();
            await scheduler.ScheduleJob(trigger);

            // Wait for at least one fire (trigger persists because RepeatForever)
            await WaitForExecutionCount(1, 10_000);

            Assert.That(RecordingJob.Executions, Does.Contain("autopin-node"),
                "The job should have executed on the autopin-node scheduler");

            // Poll until the auto-pin claim is observable (node name stored verbatim,
            // with the auto-claim recorded in its own column)
            await WaitForCondition(async () =>
            {
                ITrigger t = await scheduler.GetTrigger(trigger.Key);
                return t != null
                    && t.PreferredNode == "autopin-node"
                    && t.IsPreferredNodeAuto;
            }, 10_000, "auto-pin to resolve to the firing node's instance id");
        }
        finally
        {
            await scheduler.Clear();
            await scheduler.Shutdown(false);
        }
    }

    [Test]
    public async Task LiveNodePin_ExecutesOnPinnedNode()
    {
        IScheduler nodeA = await CreateScheduler("nodeA");
        IScheduler nodeB = await CreateScheduler("nodeB");
        try
        {
            await nodeA.Start();
            await nodeB.Start();
            await Task.Delay(2000);

            IJobDetail job = JobBuilder.Create<RecordingJob>()
                .WithIdentity("exclusionJob", "clusteredTest")
                .StoreDurably()
                .Build();
            await nodeA.AddJob(job, true);

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
            await nodeA.ScheduleJob(trigger);

            // Wait for several fires (RepeatCount(3) yields up to 4 executions)
            await WaitForExecutionCount(3, 15_000);

            // All recorded executions should be on nodeA, none on nodeB
            Assert.That(RecordingJob.Executions, Has.All.EqualTo("nodeA"),
                "While nodeA is alive, pinned trigger should only execute on nodeA");

            ITrigger retrieved = await nodeA.GetTrigger(trigger.Key);
            if (retrieved != null)
            {
                Assert.That(retrieved.PreferredNode, Is.EqualTo("nodeA"));
            }
        }
        finally
        {
            await nodeA.Clear();
            await nodeA.Shutdown(false);
            await nodeB.Shutdown(false);
        }
    }

    [Test]
    public async Task Failover_DeadNodeTriggerRepinnedToSurvivor()
    {
        IScheduler nodeA = await CreateScheduler("nodeA");
        try
        {
            await nodeA.Start();
            await Task.Delay(2000);

            IJobDetail job = JobBuilder.Create<RecordingJob>()
                .WithIdentity("failoverJob", "clusteredTest")
                .StoreDurably()
                .Build();
            await nodeA.AddJob(job, true);

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("failoverTrigger", "clusteredTest")
                .ForJob(job)
                .WithPreferredNode("*")
                .WithSimpleSchedule(s => s
                    .WithIntervalInSeconds(1)
                    .RepeatForever())
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(3))
                .Build();
            await nodeA.ScheduleJob(trigger);

            await nodeA.Shutdown(false);
        }
        catch
        {
            await nodeA.Shutdown(false);
            throw;
        }

        // Reset recordings so any nodeA executions don't satisfy nodeB's assertions
        RecordingJob.Reset();

        // Wait for nodeA's checkin to become stale
        await Task.Delay(10_000);

        IScheduler nodeB = await CreateScheduler("nodeB");
        try
        {
            await nodeB.Start();

            // Wait for nodeB to execute the trigger (proves failover worked)
            await WaitForExecutionCount(1, 15_000);

            Assert.That(RecordingJob.Executions, Does.Contain("nodeB"),
                "The job should have executed on nodeB after failover");

            // Poll until auto-pin settles (sentinel "*" resolved to "nodeB" after first fire)
            TriggerKey failoverKey = new TriggerKey("failoverTrigger", "clusteredTest");
            await WaitForCondition(async () =>
            {
                ITrigger t = await nodeB.GetTrigger(failoverKey);
                return t != null && t.PreferredNode == "nodeB";
            }, 10_000, "auto-pin to settle on nodeB");

            // Regression for the auto-pin write-back race: a fire that acquired the trigger
            // while it was still auto-claimed by the dead nodeA (before ClusterRecover reset it
            // to "*") must not write the dead node back. The pin must stay on nodeB across
            // subsequent fires.
            RecordingJob.Reset();
            await WaitForExecutionCount(2, 10_000);
            ITrigger settled = await nodeB.GetTrigger(failoverKey);
            Assert.That(settled.PreferredNode, Is.EqualTo("nodeB"),
                "Pin must remain on the surviving node and never revert to the dead node");
            Assert.That(settled.IsPreferredNodeAuto, Is.True,
                "The stolen pin must remain auto-claimed so it can fail over again");
        }
        finally
        {
            await nodeB.Clear();
            await nodeB.Shutdown(false);
        }
    }

    [Test]
    public async Task UpdateTriggerDetails_PinChange_RedirectsExecutionToNewNode()
    {
        IScheduler nodeA = await CreateScheduler("nodeA");
        IScheduler nodeB = await CreateScheduler("nodeB");
        try
        {
            await nodeA.Start();
            await nodeB.Start();
            await Task.Delay(2000);

            IJobDetail job = JobBuilder.Create<RecordingJob>()
                .WithIdentity("redirectJob", "clusteredTest")
                .StoreDurably()
                .Build();
            await nodeA.AddJob(job, true);

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("redirectTrigger", "clusteredTest")
                .ForJob(job)
                .WithPreferredNode("nodeA")
                .WithSimpleSchedule(s => s
                    .WithIntervalInSeconds(1)
                    .RepeatForever())
                .StartNow()
                .Build();
            await nodeA.ScheduleJob(trigger);

            // Establish baseline: pinned trigger executes on nodeA only
            await WaitForExecutionCount(2, 15_000);
            Assert.That(RecordingJob.Executions, Has.All.EqualTo("nodeA"),
                "Before the update, the pinned trigger should execute only on nodeA");

            // Redirect the pin to nodeB at runtime
            await nodeA.UpdateTriggerDetails(
                trigger.Key,
                new TriggerDetailsUpdate().WithPreferredNode("nodeB"));

            // A fire already acquired by nodeA at update time is tolerated; wait until
            // nodeB takes over, then verify the takeover is stable.
            await WaitForCondition(
                () => Task.FromResult(RecordingJob.Executions.Contains("nodeB")),
                15_000,
                "execution to move to nodeB after pin update");

            RecordingJob.Reset();
            await WaitForExecutionCount(2, 10_000);
            Assert.That(RecordingJob.Executions, Has.All.EqualTo("nodeB"),
                "After the pin update, the trigger should execute only on nodeB");
        }
        finally
        {
            await nodeA.Clear();
            await nodeA.Shutdown(false);
            await nodeB.Shutdown(false);
        }
    }
}
