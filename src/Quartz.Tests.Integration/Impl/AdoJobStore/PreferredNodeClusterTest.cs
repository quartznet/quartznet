using System;
using System.Collections.Specialized;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Tests preferred-node (node affinity) persistence and single-node behavior with a
/// real ADO.NET job store (SQLite). Covers round-trip, auto-pin on first fire,
/// UpdateTriggerDetails, combined preferred node + execution group, and pre-start
/// scheduling. Full multi-node acquisition filtering and failover require a clustered
/// database (PostgreSQL/SQL Server) and are exercised by SmokeTestPerformer in CI.
/// </summary>
[Category("db-sqlite")]
public sealed class PreferredNodeClusterTest
{
    private string dbFile;
    private string connectionString;

    [SetUp]
    public void SetUp()
    {
        dbFile = $"test-pn-cluster-{Guid.NewGuid():N}.db";
        connectionString = $"Data Source={dbFile};";

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        string sql = LoadSqliteTableScript();
        using var command = new SqliteCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(dbFile))
            {
                File.Delete(dbFile);
            }
        }
        catch
        {
            // best effort cleanup
        }
    }

    [Test]
    public async Task PreferredNode_ManualPin_RoundTrips()
    {
        IScheduler scheduler = await CreateScheduler("nodeA").ConfigureAwait(false);
        await scheduler.Start().ConfigureAwait(false);

        IJobDetail job = JobBuilder.Create<NoOpJob>()
            .WithIdentity("pinnedJob")
            .StoreDurably()
            .Build();
        await scheduler.AddJob(job, true).ConfigureAwait(false);

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("pinnedTrigger")
            .ForJob(job)
            .WithPreferredNode("nodeA")
            .WithSimpleSchedule(s => s.WithRepeatCount(0))
            .StartAt(DateTimeOffset.UtcNow.AddHours(1))
            .Build();
        await scheduler.ScheduleJob(trigger).ConfigureAwait(false);

        // Verify round-trip
        ITrigger retrieved = await scheduler.GetTrigger(trigger.Key).ConfigureAwait(false);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(((AbstractTrigger) retrieved).PreferredNode, Is.EqualTo("nodeA"));

        await scheduler.Shutdown(false).ConfigureAwait(false);
    }

    [Test]
    public async Task PreferredNode_AutoPin_PinsToFirstFiringNode()
    {
        IScheduler nodeA = await CreateScheduler("nodeA").ConfigureAwait(false);
        try
        {
            await nodeA.Start().ConfigureAwait(false);

            IJobDetail job = JobBuilder.Create<NoOpJob>()
                .WithIdentity("autoPinJob")
                .StoreDurably()
                .Build();
            await nodeA.AddJob(job, true).ConfigureAwait(false);

            // Use RepeatForever so the trigger persists after firing for reliable assertion
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("autoPinTrigger")
                .ForJob(job)
                .WithPreferredNode("*")
                .WithSimpleSchedule(s => s
                    .WithIntervalInSeconds(2)
                    .RepeatForever())
                .StartNow()
                .Build();
            await nodeA.ScheduleJob(trigger).ConfigureAwait(false);

            // Wait for at least one fire
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMilliseconds(10_000);
            ITrigger retrieved = null;
            while (DateTimeOffset.UtcNow < deadline)
            {
                retrieved = await nodeA.GetTrigger(trigger.Key).ConfigureAwait(false);
                if (retrieved != null)
                {
                    string prefNode = ((AbstractTrigger) retrieved).PreferredNode;
                    if (prefNode != null && prefNode != "*")
                    {
                        break;
                    }
                }
                await Task.Delay(200).ConfigureAwait(false);
            }

            Assert.That(retrieved, Is.Not.Null, "RepeatForever trigger should still exist");
            // Public getter normalizes (strips "auto:" prefix), so we see the plain node name
            Assert.That(((AbstractTrigger) retrieved).PreferredNode, Is.Not.Null.And.Not.EqualTo("*"),
                "Auto-pin should have resolved to a specific node after first fire");
        }
        finally
        {
            await nodeA.Shutdown(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task PreferredNode_UpdateViaApi_Persisted()
    {
        IScheduler scheduler = await CreateScheduler("nodeA").ConfigureAwait(false);
        try
        {
            await scheduler.Start().ConfigureAwait(false);

            IJobDetail job = JobBuilder.Create<NoOpJob>()
                .WithIdentity("updateJob")
                .StoreDurably()
                .Build();
            await scheduler.AddJob(job, true).ConfigureAwait(false);

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("updateTrigger")
                .ForJob(job)
                .WithPreferredNode("nodeA")
                .WithSimpleSchedule(s => s.WithRepeatCount(0))
                .StartAt(DateTimeOffset.UtcNow.AddHours(1))
                .Build();
            await scheduler.ScheduleJob(trigger).ConfigureAwait(false);

            // Update preferred node via API
            await scheduler.UpdateTriggerDetails(
                trigger.Key,
                new TriggerDetailsUpdate().WithPreferredNode("nodeB")).ConfigureAwait(false);

            ITrigger retrieved = await scheduler.GetTrigger(trigger.Key).ConfigureAwait(false);
            Assert.That(retrieved, Is.Not.Null);
            Assert.That(((AbstractTrigger) retrieved).PreferredNode, Is.EqualTo("nodeB"),
                "PreferredNode should be updatable via UpdateTriggerDetails");

            // Clear preferred node
            await scheduler.UpdateTriggerDetails(
                trigger.Key,
                new TriggerDetailsUpdate().WithPreferredNode(null)).ConfigureAwait(false);

            retrieved = await scheduler.GetTrigger(trigger.Key).ConfigureAwait(false);
            Assert.That(((AbstractTrigger) retrieved).PreferredNode, Is.Null,
                "PreferredNode should be clearable via UpdateTriggerDetails");
        }
        finally
        {
            await scheduler.Shutdown(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task PreferredNode_WithExecutionGroup_BothPersisted()
    {
        IScheduler scheduler = await CreateScheduler("node1").ConfigureAwait(false);
        await scheduler.Start().ConfigureAwait(false);

        IJobDetail job = JobBuilder.Create<NoOpJob>()
            .WithIdentity("combinedJob")
            .StoreDurably()
            .Build();
        await scheduler.AddJob(job, true).ConfigureAwait(false);

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("combinedTrigger")
            .ForJob(job)
            .WithPreferredNode("node1")
            .WithExecutionGroup("batch")
            .WithSimpleSchedule(s => s.WithRepeatCount(0))
            .StartAt(DateTimeOffset.UtcNow.AddHours(1))
            .Build();
        await scheduler.ScheduleJob(trigger).ConfigureAwait(false);

        ITrigger retrieved = await scheduler.GetTrigger(trigger.Key).ConfigureAwait(false);
        Assert.That(retrieved, Is.Not.Null);
        AbstractTrigger at = (AbstractTrigger) retrieved;
        Assert.That(at.PreferredNode, Is.EqualTo("node1"));
        Assert.That(at.ExecutionGroup, Is.EqualTo("batch"));

        await scheduler.Shutdown(false).ConfigureAwait(false);
    }

    [Test]
    public async Task PreferredNode_ScheduleBeforeStart_Persisted()
    {
        IScheduler scheduler = await CreateScheduler("node1").ConfigureAwait(false);

        // Schedule BEFORE Start() — this tests that column probing happens during Initialize
        IJobDetail job = JobBuilder.Create<NoOpJob>()
            .WithIdentity("preStartJob")
            .StoreDurably()
            .Build();
        await scheduler.AddJob(job, true).ConfigureAwait(false);

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("preStartTrigger")
            .ForJob(job)
            .WithPreferredNode("node1")
            .WithSimpleSchedule(s => s.WithRepeatCount(0))
            .StartAt(DateTimeOffset.UtcNow.AddHours(1))
            .Build();
        await scheduler.ScheduleJob(trigger).ConfigureAwait(false);

        // Now start and verify
        await scheduler.Start().ConfigureAwait(false);

        ITrigger retrieved = await scheduler.GetTrigger(trigger.Key).ConfigureAwait(false);
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(((AbstractTrigger) retrieved).PreferredNode, Is.EqualTo("node1"),
            "PreferredNode should persist even when scheduled before Start()");

        await scheduler.Shutdown(false).ConfigureAwait(false);
    }

    [Test]
    public async Task PreferredNode_ColumnMissing_GracefullyDegrades()
    {
        // The PREFERRED_NODE column is optional — on an old schema the feature must be
        // silently disabled: scheduling succeeds, the trigger fires, the pin reads null.
        ExecuteNonQuery("ALTER TABLE QRTZ_TRIGGERS DROP COLUMN PREFERRED_NODE");

        CountingJob.Reset();
        IScheduler scheduler = await CreateScheduler("nodeA").ConfigureAwait(false);
        try
        {
            await scheduler.Start().ConfigureAwait(false);

            IJobDetail job = JobBuilder.Create<CountingJob>()
                .WithIdentity("oldSchemaJob")
                .StoreDurably()
                .Build();
            await scheduler.AddJob(job, true).ConfigureAwait(false);

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("oldSchemaTrigger")
                .ForJob(job)
                .WithPreferredNode("nodeA")
                .WithSimpleSchedule(s => s
                    .WithIntervalInSeconds(1)
                    .RepeatForever())
                .StartNow()
                .Build();
            await scheduler.ScheduleJob(trigger).ConfigureAwait(false);

            await WaitForCondition(
                () => Task.FromResult(CountingJob.ExecutionCount >= 1),
                10_000, "trigger to fire on an old schema without PREFERRED_NODE").ConfigureAwait(false);

            ITrigger retrieved = await scheduler.GetTrigger(trigger.Key).ConfigureAwait(false);
            Assert.That(retrieved, Is.Not.Null);
            Assert.That(((AbstractTrigger) retrieved).PreferredNode, Is.Null,
                "Without the PREFERRED_NODE column, the pin must be silently dropped");
        }
        finally
        {
            await scheduler.Shutdown(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task PreferredNode_GhostNodePin_TriggerStillFires()
    {
        // Explicit pin to a node that never existed: the pin is a strong preference,
        // not a hard constraint — a live node must fire the trigger (spec: liveness
        // beats placement), and the explicit pin must be preserved (never stolen).
        CountingJob.Reset();
        IScheduler scheduler = await CreateScheduler("nodeA").ConfigureAwait(false);
        try
        {
            await scheduler.Start().ConfigureAwait(false);

            IJobDetail job = JobBuilder.Create<CountingJob>()
                .WithIdentity("ghostPinJob")
                .StoreDurably()
                .Build();
            await scheduler.AddJob(job, true).ConfigureAwait(false);

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("ghostPinTrigger")
                .ForJob(job)
                .WithPreferredNode("ghost-node")
                .WithSimpleSchedule(s => s
                    .WithIntervalInSeconds(1)
                    .RepeatForever())
                .StartNow()
                .Build();
            await scheduler.ScheduleJob(trigger).ConfigureAwait(false);

            await WaitForCondition(
                () => Task.FromResult(CountingJob.ExecutionCount >= 1),
                10_000, "trigger pinned to a nonexistent node to fire on a live node").ConfigureAwait(false);

            ITrigger retrieved = await scheduler.GetTrigger(trigger.Key).ConfigureAwait(false);
            Assert.That(((AbstractTrigger) retrieved).PreferredNode, Is.EqualTo("ghost-node"),
                "An explicit pin must be preserved even when another node fires the trigger");
        }
        finally
        {
            await scheduler.Shutdown(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task PreferredNode_StaleAutoPin_StolenByFiringNode()
    {
        // A trigger auto-pinned to a stale/vanished node is acquired by a live node via
        // the liveness fallback; the firing node must steal the auto-pin (sticky failover)
        // so the claim converges to a live node instead of writing the stale owner back.
        CountingJob.Reset();
        IScheduler scheduler = await CreateScheduler("nodeA").ConfigureAwait(false);
        try
        {
            IJobDetail job = JobBuilder.Create<CountingJob>()
                .WithIdentity("stealJob")
                .StoreDurably()
                .Build();
            await scheduler.AddJob(job, true).ConfigureAwait(false);

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("stealTrigger")
                .ForJob(job)
                .WithPreferredNode("*")
                .WithSimpleSchedule(s => s
                    .WithIntervalInSeconds(1)
                    .RepeatForever())
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(2))
                .Build();
            await scheduler.ScheduleJob(trigger).ConfigureAwait(false);

            // Simulate a stale auto-pin left behind by a dead node that ClusterRecover
            // never repaired (its scheduler state row is long gone).
            ExecuteNonQuery("UPDATE QRTZ_TRIGGERS SET PREFERRED_NODE = 'auto:ghost' WHERE TRIGGER_NAME = 'stealTrigger'");

            await scheduler.Start().ConfigureAwait(false);

            await WaitForCondition(
                () => Task.FromResult(CountingJob.ExecutionCount >= 1),
                10_000, "trigger auto-pinned to a stale node to fire").ConfigureAwait(false);

            // Public getter strips the "auto:" prefix — the steal shows up as "nodeA"
            await WaitForCondition(async () =>
            {
                ITrigger t = await scheduler.GetTrigger(trigger.Key).ConfigureAwait(false);
                return t != null && ((AbstractTrigger) t).PreferredNode == "nodeA";
            }, 10_000, "stale auto-pin to be stolen by the firing node").ConfigureAwait(false);
        }
        finally
        {
            await scheduler.Shutdown(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task PreferredNode_MisfiredTrigger_KeepsPin()
    {
        // Misfire handling rewrites the trigger; the pin must survive it.
        CountingJob.Reset();
        IScheduler scheduler = await CreateScheduler(
            "nodeA",
            p => p["quartz.jobStore.misfireThreshold"] = "1000").ConfigureAwait(false);
        try
        {
            IJobDetail job = JobBuilder.Create<CountingJob>()
                .WithIdentity("misfireJob")
                .StoreDurably()
                .Build();
            await scheduler.AddJob(job, true).ConfigureAwait(false);

            // Already deeply misfired at schedule time; NowWithRemainingCount fires
            // immediately once the misfire handler processes it.
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("misfireTrigger")
                .ForJob(job)
                .WithPreferredNode("nodeA")
                .WithSimpleSchedule(s => s
                    .WithIntervalInHours(1)
                    .RepeatForever()
                    .WithMisfireHandlingInstructionNowWithRemainingCount())
                .StartAt(DateTimeOffset.UtcNow.AddMinutes(-10))
                .Build();
            await scheduler.ScheduleJob(trigger).ConfigureAwait(false);

            await scheduler.Start().ConfigureAwait(false);

            await WaitForCondition(
                () => Task.FromResult(CountingJob.ExecutionCount >= 1),
                15_000, "misfired pinned trigger to fire").ConfigureAwait(false);

            ITrigger retrieved = await scheduler.GetTrigger(trigger.Key).ConfigureAwait(false);
            Assert.That(retrieved, Is.Not.Null);
            Assert.That(((AbstractTrigger) retrieved).PreferredNode, Is.EqualTo("nodeA"),
                "Misfire handling must not clear or alter the preferred node");
        }
        finally
        {
            await scheduler.Shutdown(false).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task PreferredNode_AcquireWithoutFire_DoesNotClaimAutoPin()
    {
        // The auto-pin claim is written by TriggerFired, not by acquisition. A trigger
        // that is acquired but released without firing (scheduler shutdown) must keep
        // the "*" sentinel and return to WAITING.
        IScheduler scheduler = await CreateScheduler("nodeA").ConfigureAwait(false);
        bool shutdown = false;
        try
        {
            IJobDetail job = JobBuilder.Create<NoOpJob>()
                .WithIdentity("releaseJob")
                .StoreDurably()
                .Build();
            await scheduler.AddJob(job, true).ConfigureAwait(false);

            // Fires within the scheduler thread's acquisition window (idle wait 30s) but
            // far enough away that it cannot fire during the test.
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("releaseTrigger")
                .ForJob(job)
                .WithPreferredNode("*")
                .WithSimpleSchedule(s => s.WithRepeatCount(0))
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(20))
                .Build();
            await scheduler.ScheduleJob(trigger).ConfigureAwait(false);

            await scheduler.Start().ConfigureAwait(false);

            // Wait until the scheduler thread has acquired the trigger
            await WaitForCondition(
                () => Task.FromResult("ACQUIRED" == (string) ExecuteScalar(
                    "SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = 'releaseTrigger'")),
                10_000, "trigger to reach ACQUIRED state").ConfigureAwait(false);

            Assert.That(ExecuteScalar("SELECT PREFERRED_NODE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = 'releaseTrigger'"),
                Is.EqualTo("*"), "Acquisition alone must not claim the auto-pin");

            // Shutdown releases the acquired-but-unfired trigger
            await scheduler.Shutdown(false).ConfigureAwait(false);
            shutdown = true;

            Assert.That(ExecuteScalar("SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = 'releaseTrigger'"),
                Is.EqualTo("WAITING"), "Released trigger must return to WAITING");
            Assert.That(ExecuteScalar("SELECT PREFERRED_NODE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = 'releaseTrigger'"),
                Is.EqualTo("*"), "Releasing an acquired trigger must not claim the auto-pin");
        }
        finally
        {
            if (!shutdown)
            {
                await scheduler.Shutdown(false).ConfigureAwait(false);
            }
        }
    }

    private async Task<IScheduler> CreateScheduler(string instanceId, Action<NameValueCollection> configure = null)
    {
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "PreferredNodeClusterTestScheduler",
            ["quartz.scheduler.instanceId"] = instanceId,
            ["quartz.threadPool.threadCount"] = "2",
            ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz",
            ["quartz.jobStore.driverDelegateType"] = "Quartz.Impl.AdoJobStore.SQLiteDelegate, Quartz",
            ["quartz.jobStore.dataSource"] = "default",
            ["quartz.jobStore.tablePrefix"] = "QRTZ_",
            ["quartz.jobStore.clustered"] = "false",
            ["quartz.dataSource.default.provider"] = "SQLite-Microsoft",
            ["quartz.dataSource.default.connectionString"] = connectionString,
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
        };

        configure?.Invoke(properties);

        StdSchedulerFactory factory = new StdSchedulerFactory(properties);
        IScheduler scheduler = await factory.GetScheduler().ConfigureAwait(false);

        // All tests share the scheduler instance name, and StdSchedulerFactory's non-proxy
        // repository lookup is name-only: without unbinding, a test that fails before its
        // Shutdown leaks a running scheduler that the next CreateScheduler would silently
        // return (bound to the previous test's now-deleted SQLite DB), masking the real failure.
        SchedulerRepository.Instance.Remove(scheduler.SchedulerName, scheduler.SchedulerInstanceId);

        return scheduler;
    }

    private void ExecuteNonQuery(string sql)
    {
        using SqliteConnection connection = new SqliteConnection(connectionString);
        connection.Open();
        using SqliteCommand command = new SqliteCommand(sql, connection);
        command.ExecuteNonQuery();
    }

    private object ExecuteScalar(string sql)
    {
        using SqliteConnection connection = new SqliteConnection(connectionString);
        connection.Open();
        using SqliteCommand command = new SqliteCommand(sql, connection);
        return command.ExecuteScalar();
    }

    private static async Task WaitForCondition(Func<Task<bool>> condition, int timeoutMs, string message)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition().ConfigureAwait(false))
            {
                return;
            }
            await Task.Delay(200).ConfigureAwait(false);
        }
        Assert.Fail($"Timed out waiting for condition: {message}");
    }

    private static async Task WaitForTriggerCompletion(IScheduler scheduler, TriggerKey triggerKey, int timeoutMs)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            TriggerState state = await scheduler.GetTriggerState(triggerKey).ConfigureAwait(false);
            if (state == TriggerState.Complete || state == TriggerState.None)
            {
                return;
            }
            await Task.Delay(200).ConfigureAwait(false);
        }
    }

    private static string LoadSqliteTableScript()
    {
        string path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        return File.ReadAllText(path);
    }

    [DisallowConcurrentExecution]
    public sealed class NoOpJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }

    [DisallowConcurrentExecution]
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
