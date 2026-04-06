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

    private async Task<IScheduler> CreateScheduler(string instanceId)
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

        StdSchedulerFactory factory = new StdSchedulerFactory(properties);
        return await factory.GetScheduler().ConfigureAwait(false);
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
}
