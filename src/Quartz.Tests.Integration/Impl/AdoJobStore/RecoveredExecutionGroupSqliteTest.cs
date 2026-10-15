#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using FakeItEasy;

using Microsoft.Data.Sqlite;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Matchers;
using Quartz.Impl.Triggers;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A recovery trigger keeps the execution group of the trigger whose firing it replays, after cluster
/// recovery of a dead node and after a node recovers its own firing when it starts again (#4081).
/// </summary>
/// <remarks>
/// <para>
/// 3.x writes no execution group to FIRED_TRIGGERS, so recovery reads it from the original trigger's
/// row, beside the job data map it already reads there, and only when the startup probe found the
/// column. Without it a recovered <c>gpu</c> job was stored ungrouped and ran beside the live one holding
/// the only <c>gpu</c> slot. The ungrouped cases are the control: a trigger with no group recovers with
/// none.
/// </para>
/// <para>
/// Two <see cref="JobStoreTX" /> instances on one SQLite file stand in for two nodes. The dead node fires
/// a trigger and never completes it. The survivor is told it is clustered after it is initialized, and
/// its first check-in finds the dead node's row orphaned, which is what recovers it.
/// </para>
/// </remarks>
[NonParallelizable]
[Category("db-sqlite")]
public sealed class RecoveredExecutionGroupSqliteTest
{
    private const string SchedulerName = "recovered-execution-group";
    private const string Gpu = "gpu";
    private static readonly JobKey JobKey = new JobKey("render", "recovery");
    private static readonly TriggerKey TriggerKey = new TriggerKey("t-render", "recovery");

    private readonly List<JobStoreTX> stores = new List<JobStoreTX>();
    private string databaseFile;
    private string connectionString;

    [SetUp]
    public void CreateDatabaseFile()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-recovered-execution-group-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";
    }

    [TearDown]
    public async Task DisposeNodes()
    {
        foreach (JobStoreTX store in stores)
        {
            await store.Shutdown();
        }

        SqliteConnection.ClearAllPools();
        if (File.Exists(databaseFile))
        {
            File.Delete(databaseFile);
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ClusterRecoveryOfADeadNodeKeepsTheExecutionGroup(bool grouped)
    {
        string executionGroup = grouped ? Gpu : null;
        InstallSchema(withExecutionGroup: true);
        await LeaveAFiringBehind(await BuildNode("node-a"), executionGroup);

        NodeStore survivor = await BuildNode("node-b");
        await RecoverTheDeadNode(survivor);

        (await RecoveryTriggerExecutionGroups()).Should().Equal(new[] { executionGroup },
            "the recovery trigger replays the dead node's firing, and is limited as that firing was");
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task StartupRecoveryOfTheNodesOwnFiringKeepsTheExecutionGroup(bool grouped)
    {
        string executionGroup = grouped ? Gpu : null;
        InstallSchema(withExecutionGroup: true);
        NodeStore killed = await BuildNode("one");
        await LeaveAFiringBehind(killed, executionGroup);
        await killed.Shutdown();
        stores.Remove(killed);

        NodeStore restarted = await BuildNode("one");
        await restarted.SchedulerStarted();

        (await RecoveryTriggerExecutionGroups()).Should().Equal(new[] { executionGroup },
            "the recovery trigger replays the interrupted firing, and is limited as that firing was");
    }

    /// <summary>
    /// What keeping the group is for: a recovered <c>gpu</c> firing waits for a <c>gpu</c> slot rather
    /// than running beside the live job that holds it.
    /// </summary>
    [Test]
    public async Task ARecoveredFiringWaitsForASlotInItsExecutionGroup()
    {
        InstallSchema(withExecutionGroup: true);
        await LeaveAFiringBehind(await BuildNode("node-a"), Gpu);

        NodeStore survivor = await BuildNode("node-b");
        await RecoverTheDeadNode(survivor);

        // The scheduler thread hands the store what is left of each limit, so gpu = 1 with a live gpu
        // job holding the slot arrives as 0.
        IReadOnlyCollection<IOperableTrigger> heldBack = await AcquireWithGpuSlots(survivor, 0);
        heldBack.Should().BeEmpty("the recovered firing is a gpu firing, and gpu has no slot left");

        IReadOnlyCollection<IOperableTrigger> acquired = await AcquireWithGpuSlots(survivor, 1);
        acquired.Should().ContainSingle("a free gpu slot lets the recovered firing run")
            .Which.Key.Group.Should().Be(SchedulerConstants.DefaultRecoveryGroup);
    }

    /// <summary>
    /// A schema that never ran the 3.18 migration: the probe answers no column, recovery runs as it did
    /// before execution groups existed, and the recovery trigger has no group.
    /// </summary>
    [Test]
    public async Task WithoutTheColumnRecoveryStillWorksAndTheRecoveryTriggerIsUngrouped()
    {
        InstallSchema(withExecutionGroup: false);
        NodeStore dead = await BuildNode("node-a");
        dead.HasExecutionGroupColumn.Should().BeFalse("the premise: the probe found no EXECUTION_GROUP column");
        await LeaveAFiringBehind(dead, Gpu);

        NodeStore survivor = await BuildNode("node-b");
        await RecoverTheDeadNode(survivor);

        IReadOnlyCollection<TriggerKey> keys = await survivor.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(SchedulerConstants.DefaultRecoveryGroup));
        TriggerKey recoveryKey = keys.Should().ContainSingle("the firing is recovered without the column").Subject;

        IOperableTrigger recovery = await survivor.RetrieveTrigger(recoveryKey);
        ((AbstractTrigger) recovery).ExecutionGroup.Should().BeNull("there is no column to read a group from, nor to store one in");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Helpers
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// What a node killed mid-execution leaves in the database: the job and its trigger, and one
    /// fired-trigger row saying the trigger is executing on that node and asks to be recovered.
    /// </summary>
    private async Task LeaveAFiringBehind(JobStoreTX node, string executionGroup)
    {
        await node.StoreJob(JobBuilder.Create<NoOpJob>().WithIdentity(JobKey).RequestRecovery().StoreDurably().Build(), replaceExisting: false);

        // Repeating, so the firing moves the trigger an hour on and the recovery trigger is the only one due.
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(TriggerKey)
            .ForJob(JobKey)
            .StartNow()
            .WithExecutionGroup(executionGroup)
            .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();
        trigger.ComputeFirstFireTimeUtc(null);
        await node.StoreTrigger(trigger, replaceExisting: false);

        IReadOnlyCollection<IOperableTrigger> acquired = await node.AcquireNextTriggers(DateTimeOffset.UtcNow.AddMinutes(1), 1, TimeSpan.Zero);
        acquired.Should().ContainSingle().Which.Key.Should().Be(TriggerKey);
        IReadOnlyCollection<TriggerFiredResult> fired = await node.TriggersFired(acquired);
        fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull();

        // No completion: this is where the node dies.
        (await ReadColumn("SELECT STATE FROM QRTZ_FIRED_TRIGGERS WHERE REQUESTS_RECOVERY = 1")).Should().Equal(new[] { "EXECUTING" },
            "the premise: the row left behind asks to be recovered");
    }

    /// <summary>
    /// The survivor's first check-in, as a clustered node. The dead node never checked in, so its row has
    /// no scheduler state beside it and the check-in recovers it as orphaned.
    /// </summary>
    private static async Task RecoverTheDeadNode(NodeStore survivor)
    {
        survivor.Clustered = true;
        (await survivor.DoCheckin(Guid.NewGuid())).Should().BeTrue("the premise: the first check-in finds the dead node's row and recovers it");
    }

    private static Task<IReadOnlyCollection<IOperableTrigger>> AcquireWithGpuSlots(NodeStore store, int slots)
    {
        return ((INextVersionJobStore) store).AcquireNextTriggers(
            DateTimeOffset.UtcNow.AddMinutes(1),
            1,
            TimeSpan.Zero,
            new Dictionary<string, int?> { [Gpu] = slots });
    }

    private Task<List<string>> RecoveryTriggerExecutionGroups()
    {
        return ReadColumn($"SELECT EXECUTION_GROUP FROM QRTZ_TRIGGERS WHERE TRIGGER_GROUP = '{SchedulerConstants.DefaultRecoveryGroup}'");
    }

    private async Task<List<string>> ReadColumn(string sql)
    {
        using SqliteConnection connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        List<string> values = new List<string>();
        using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.IsDBNull(0) ? null : reader.GetString(0));
        }

        return values;
    }

    private async Task<NodeStore> BuildNode(string instanceId)
    {
        // A data source per node, as two processes would have: a store's shutdown shuts its own down.
        string dataSource = $"recovered-execution-group-{instanceId}-{Guid.NewGuid():N}";
        DBConnectionManager.Instance.AddConnectionProvider(dataSource, new DbProvider("SQLite-Microsoft", connectionString));

        SystemTextJsonObjectSerializer serializer = new SystemTextJsonObjectSerializer();
        serializer.Initialize();

        NodeStore store = new NodeStore
        {
            DataSource = dataSource,
            TablePrefix = "QRTZ_",
            InstanceName = SchedulerName,
            InstanceId = instanceId,
            DriverDelegateType = typeof(SQLiteDelegate).AssemblyQualifiedName,
            ObjectSerializer = serializer,
        };

        // Initialized and not started: nothing runs that the test does not call for.
        await store.Initialize(new SimpleTypeLoadHelper(), A.Fake<ISchedulerSignaler>());
        stores.Add(store);
        return store;
    }

    /// <param name="withExecutionGroup">
    /// Whether the schema has the EXECUTION_GROUP columns the 3.18 migration adds; the fresh-install
    /// script has them, and leaving its two lines out gives a schema that never ran the migration.
    /// </param>
    private void InstallSchema(bool withExecutionGroup)
    {
        IEnumerable<string> lines = File.ReadAllLines(ResolveRepositoryFile("database", "tables", "tables_sqlite.sql"));
        if (!withExecutionGroup)
        {
            lines = lines.Where(line => line.IndexOf("EXECUTION_GROUP", StringComparison.Ordinal) < 0);
        }

        using SqliteConnection connection = new SqliteConnection(connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = string.Join("\n", lines);
        command.ExecuteNonQuery();
    }

    private static string ResolveRepositoryFile(params string[] pathSegments)
    {
        string relativePath = Path.Combine(pathSegments);
        DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find '{relativePath}' above '{AppContext.BaseDirectory}'.");
    }

    /// <summary>The store, with the probe's answer in reach of the test.</summary>
    private sealed class NodeStore : JobStoreTX
    {
        public bool HasExecutionGroupColumn => ((INextVersionDelegate) Delegate).HasExecutionGroupColumn;
    }

    public sealed class NoOpJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
