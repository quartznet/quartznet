namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Tests for preferred-node failover semantics using PostgreSQL.
/// Validates that auto-pinned triggers are re-pinned during ClusterRecover
/// while explicitly pinned triggers are preserved through failover.
/// Runs against the assembly-wide PostgreSQL database (see ClusteredPostgresTestBase).
/// </summary>
[Category("db-postgres")]
[NonParallelizable]
public sealed class PreferredNodeFailoverPostgresTest : ClusteredPostgresTestBase
{
    protected override string SchedulerName => "FailoverTest";

    [Test]
    public async Task ExplicitPin_PreservedThroughFailover()
    {
        // Explicit pin to nodeA — should NOT be re-pinned during failover.
        IScheduler nodeA = await CreateScheduler("nodeA");
        try
        {
            await nodeA.Start();
            await Task.Delay(2000);

            IJobDetail job = JobBuilder.Create<RecordingJob>()
                .WithIdentity("explicitPinJob", "failoverTest")
                .StoreDurably()
                .Build();
            await nodeA.AddJob(job, true);

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("explicitPinTrigger", "failoverTest")
                .ForJob(job)
                .WithPreferredNode("nodeA")
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

        // Wait for nodeA's checkin to become stale
        await Task.Delay(10_000);

        IScheduler nodeB = await CreateScheduler("nodeB");
        try
        {
            await nodeB.Start();

            // The trigger should fire on nodeB via the NOT IN failover SQL
            await WaitForExecutionCount(1, 15_000);
            Assert.That(RecordingJob.Executions, Does.Contain("nodeB"),
                "The job should have executed on nodeB via NOT IN failover");

            ITrigger retrieved = await nodeB.GetTrigger(new TriggerKey("explicitPinTrigger", "failoverTest"));
            Assert.That(retrieved, Is.Not.Null, "Repeating trigger should still exist after failover");

            // Explicit pin should NOT be re-pinned — stays as "nodeA" even after nodeB fired it
            Assert.That(retrieved.PreferredNode, Is.EqualTo("nodeA"),
                "Explicit pin should be preserved through failover, not re-pinned to nodeB");
        }
        finally
        {
            RecordingJob.Reset();
            await nodeB.Clear();
            await nodeB.Shutdown(false);
        }
    }

    [Test]
    public async Task AutoPin_RepinnedThroughFailover()
    {
        // Auto-pin (*) to nodeA — should be re-pinned to nodeB during failover.
        IScheduler nodeA = await CreateScheduler("nodeA");
        try
        {
            await nodeA.Start();
            await Task.Delay(2000);

            IJobDetail job = JobBuilder.Create<RecordingJob>()
                .WithIdentity("autoPinFailoverJob", "failoverTest")
                .StoreDurably()
                .Build();
            await nodeA.AddJob(job, true);

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("autoPinFailoverTrigger", "failoverTest")
                .ForJob(job)
                .WithPreferredNode("*")
                .WithSimpleSchedule(s => s
                    .WithIntervalInSeconds(1)
                    .RepeatForever())
                .StartNow()
                .Build();
            await nodeA.ScheduleJob(trigger);

            // Wait for auto-pin to happen (first fire pins to nodeA)
            await WaitForExecutionCount(1, 10_000);

            ITrigger pinnedTrigger = await nodeA.GetTrigger(trigger.Key);
            Assert.That(pinnedTrigger, Is.Not.Null);
            // The node name is stored verbatim; the auto-claim is recorded out-of-band
            Assert.That(pinnedTrigger.PreferredNode, Is.EqualTo("nodeA"),
                "Auto-pin should resolve to 'nodeA' after first fire");
            Assert.That(pinnedTrigger.IsPreferredNodeAuto, Is.True,
                "A pin claimed by auto-pin should be flagged as auto-claimed");

            await nodeA.Shutdown(false);
        }
        catch
        {
            await nodeA.Shutdown(false);
            throw;
        }

        // Reset recordings so nodeA's earlier executions don't satisfy nodeB's assertions
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
            TriggerKey failoverKey = new TriggerKey("autoPinFailoverTrigger", "failoverTest");
            await WaitForCondition(async () =>
            {
                ITrigger t = await nodeB.GetTrigger(failoverKey);
                return t != null && t.PreferredNode == "nodeB";
            }, 10_000, "auto-pin to settle on nodeB");

            // Regression for the auto-pin write-back race: a fire that acquired the trigger while
            // it was still auto-claimed by the dead nodeA (before ClusterRecover reset it) must not
            // write the dead node back. The pin must remain on nodeB across subsequent fires.
            RecordingJob.Reset();
            await WaitForExecutionCount(2, 10_000);
            Assert.That(RecordingJob.Executions, Has.All.EqualTo("nodeB"));
            ITrigger settled = await nodeB.GetTrigger(failoverKey);
            Assert.That(settled.PreferredNode, Is.EqualTo("nodeB"),
                "Pin must remain on the surviving node and never revert to the dead node");
        }
        finally
        {
            RecordingJob.Reset();
            await nodeB.Clear();
            await nodeB.Shutdown(false);
        }
    }

    [Test]
    public async Task ThreeNode_AutoPinnedTrigger_SettlesOnSingleSurvivorAfterFailover()
    {
        IScheduler nodeA = await CreateScheduler("nodeA");
        IScheduler nodeB = await CreateScheduler("nodeB");
        IScheduler survivor = null;
        string survivorId = null;
        try
        {
            await nodeA.Start();
            await nodeB.Start();
            await Task.Delay(2000);

            IJobDetail job = JobBuilder.Create<RecordingJob>()
                .WithIdentity("threeNodeJob", "failoverTest")
                .StoreDurably()
                .Build();
            await nodeA.AddJob(job, true);

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("threeNodeTrigger", "failoverTest")
                .ForJob(job)
                .WithPreferredNode("*")
                .WithSimpleSchedule(s => s
                    .WithIntervalInSeconds(1)
                    .RepeatForever())
                .StartNow()
                .Build();
            await nodeA.ScheduleJob(trigger);

            // Wait for auto-pin to first fire on whichever node acquires it
            await WaitForExecutionCount(1, 10_000);

            // Shut down the pinned node; the other original node keeps running as a survivor
            string pinnedNodeId = RecordingJob.Executions.First();
            if (pinnedNodeId == "nodeA")
            {
                await nodeA.Shutdown(false);
                survivor = nodeB;
                survivorId = "nodeB";
            }
            else
            {
                await nodeB.Shutdown(false);
                survivor = nodeA;
                survivorId = "nodeA";
            }
        }
        catch
        {
            await nodeA.Shutdown(false);
            await nodeB.Shutdown(false);
            throw;
        }

        RecordingJob.Reset();

        // Start nodeC so two nodes are live while the pinned node's checkin goes stale
        IScheduler nodeC = await CreateScheduler("nodeC");
        try
        {
            await nodeC.Start();

            // Wait for the dead node's checkin to go stale + ClusterRecover + takeover fires
            await WaitForExecutionCount(3, 30_000);

            // The trigger must settle on exactly one of the two live survivors
            TriggerKey threeNodeKey = new TriggerKey("threeNodeTrigger", "failoverTest");
            string[] liveNodes = { survivorId, "nodeC" };
            await WaitForCondition(async () =>
            {
                ITrigger t = await nodeC.GetTrigger(threeNodeKey);
                string pin = t != null ? t.PreferredNode : null;
                return pin != null && pin != "*" && liveNodes.Contains(pin);
            }, 15_000, "auto-pin to settle on a live survivor");

            // Let any fire acquired just before the claim landed drain (such a fire may
            // legitimately move the claim once more), then snapshot the settled pin.
            await Task.Delay(2000);
            ITrigger claimed = await nodeC.GetTrigger(threeNodeKey);
            string settledPin = claimed.PreferredNode;
            Assert.That(liveNodes, Does.Contain(settledPin));

            // Stability: once claimed, all further executions happen on the claimant only
            RecordingJob.Reset();
            await WaitForExecutionCount(3, 10_000);
            Assert.That(RecordingJob.Executions, Has.All.EqualTo(settledPin),
                "After the claim settles, the trigger must not bounce between survivors");

            ITrigger settled = await nodeC.GetTrigger(threeNodeKey);
            Assert.That(settled.PreferredNode, Is.EqualTo(settledPin),
                "The settled pin must be stable across subsequent fires");
        }
        finally
        {
            RecordingJob.Reset();
            await nodeC.Clear();
            await nodeC.Shutdown(false);
            if (survivor != null)
            {
                await survivor.Shutdown(false);
            }
        }
    }

    [Test]
    public async Task Failover_ExecutionGroupSaturatedSurvivor_DoesNotClaimTrigger()
    {
        // ClusterRecover resets auto-pins to "*" (instead of re-pinning to the recovering
        // node) precisely so that execution group limits are respected: a survivor that is
        // not allowed to run the trigger's group must not end up owning it.
        IScheduler nodeA = await CreateScheduler("nodeA");
        try
        {
            await nodeA.Start();
            await Task.Delay(2000);

            IJobDetail job = JobBuilder.Create<RecordingJob>()
                .WithIdentity("saturationJob", "failoverTest")
                .StoreDurably()
                .Build();
            await nodeA.AddJob(job, true);

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("saturationTrigger", "failoverTest")
                .ForJob(job)
                .WithPreferredNode("*")
                .WithExecutionGroup("heavy")
                .WithSimpleSchedule(s => s
                    .WithIntervalInSeconds(1)
                    .RepeatForever())
                .StartNow()
                .Build();
            await nodeA.ScheduleJob(trigger);

            // First fire auto-pins the trigger to nodeA
            await WaitForExecutionCount(1, 10_000);

            await nodeA.Shutdown(false);
        }
        catch
        {
            await nodeA.Shutdown(false);
            throw;
        }

        RecordingJob.Reset();

        // Two survivors: one forbidden from running group "heavy" (limit 0), one eligible
        IScheduler saturated = await CreateScheduler(
            "saturatedNode",
            configure: p => p["quartz.executionLimit.heavy"] = "0");
        IScheduler eligible = await CreateScheduler("eligibleNode");
        try
        {
            await saturated.Start();
            await eligible.Start();

            // Wait for the dead node to go stale, ClusterRecover to reset the pin, and the
            // eligible node to take over
            try
            {
                await WaitForExecutionCount(2, 30_000);
            }
            catch (Exception)
            {
                TestContext.Out.WriteLine("DB state at timeout:\n" + await DumpDatabaseState());
                throw;
            }
            Assert.That(RecordingJob.Executions, Has.All.EqualTo("eligibleNode"),
                "Only the node allowed to run group 'heavy' may execute the trigger");

            TriggerKey saturationKey = new TriggerKey("saturationTrigger", "failoverTest");
            await WaitForCondition(async () =>
            {
                ITrigger t = await eligible.GetTrigger(saturationKey);
                return t != null && t.PreferredNode == "eligibleNode";
            }, 10_000, "auto-pin to settle on the eligible node");
        }
        finally
        {
            RecordingJob.Reset();
            await eligible.Clear();
            await saturated.Shutdown(false);
            await eligible.Shutdown(false);
        }
    }
}
