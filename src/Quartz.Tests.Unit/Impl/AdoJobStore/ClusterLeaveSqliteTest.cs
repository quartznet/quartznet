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

#nullable enable

using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// A clustered node that shuts down cleanly deletes its own scheduler state row, so its peers do not
/// recover it as a failed node (#4374). One that leaves a firing behind keeps the row, and a peer
/// recovers it as before.
/// </summary>
/// <remarks>
/// Two stores on one SQLite file, told they are clustered after they started — a SQLite store refuses
/// to be configured as one — and driven call by call, so every state is read straight off the rows.
/// Neither store is started, so no check-in loop runs, and a shutdown finds the check-ins ended.
/// </remarks>
[NonParallelizable]
public sealed class ClusterLeaveSqliteTest
{
    private const string SchedulerName = "ClusterLeaveSqliteTest";
    private const string DataSourceName = "cluster-leave";
    private const string Group = "cluster-leave";
    private const string NodeA = "node-a";
    private const string NodeB = "node-b";

    /// <summary>Log event <c>ClusterLog.FailedInstancesDetected</c>, the warning #4374 is about.</summary>
    private const int FailedInstancesDetected = 3503;

    /// <summary>Log events <c>ClusterLog.LeftCluster</c> to <c>ClusterLog.SchedulerStateNotDeleted</c>.</summary>
    private const int LeftCluster = 3522;
    private const int SchedulerStateKeptForFiredTriggers = 3523;
    private const int SchedulerStateKeptForRunningCheckIn = 3524;
    private const int SchedulerStateNotDeleted = 3525;

    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(30);

    private SqliteTestDatabase database = null!;
    private FakeTimeProvider clock = null!;
    private RecordingLoggerProvider logs = null!;
    private ILoggerFactory loggerFactory = null!;
    private LocalTransactionJobStore nodeA = null!;
    private LocalTransactionJobStore nodeB = null!;
    private readonly HashSet<LocalTransactionJobStore> shutDown = [];

    [SetUp]
    public async Task CreateTwoNodesOnOneDatabase()
    {
        database = new SqliteTestDatabase("cluster-leave");
        await using (SqliteConnection connection = new(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = new(await File.ReadAllTextAsync(TablesScript()), connection);
            await command.ExecuteNonQueryAsync();
        }

        clock = new FakeTimeProvider(epoch);
        logs = new RecordingLoggerProvider();
        loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        shutDown.Clear();

        nodeA = await Node(NodeA);
        nodeB = await Node(NodeB);
    }

    [TearDown]
    public async Task ShutDownNodes()
    {
        await ShutDown(nodeB);
        await ShutDown(nodeA);
        loggerFactory.Dispose();
        logs.Dispose();
        database.Dispose();
    }

    /// <summary>
    /// The issue's reproduction: two nodes, one shuts down, and the other checks in past the point at
    /// which it would have judged the first failed. It finds no row to judge.
    /// </summary>
    [Test]
    public async Task ANodeThatShutsDownCleanlyDeletesItsRowAndItsPeerRecoversNothing()
    {
        await nodeA.CheckIn(Guid.NewGuid());
        await nodeB.CheckIn(Guid.NewGuid());
        (await StateRows()).Should().Equal([NodeA, NodeB], "the premise: both nodes have checked in");

        await ShutDown(nodeA);

        (await StateRows()).Should().Equal([NodeB],
            "a node that leaves nothing behind deletes its own row, so its peers have no node to recover");
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == LeftCluster)
            .Which.Message.Should().Contain(NodeA);

        await CheckInPastTheFailureThreshold(nodeB);

        logs.Entries.Should().NotContain(x => x.EventId.Id == FailedInstancesDetected,
            "the warning a crash logs is what every node of a rolling deployment used to log for each node it stopped");
    }

    /// <summary>
    /// A firing the shutdown left executing is found through the node's row alone: the scan for fired
    /// triggers with no row runs only at a node's first check-in. So the row stays, and a peer recovers
    /// the node as it recovers one that crashed.
    /// </summary>
    [Test]
    public async Task ANodeThatLeavesAFiringKeepsItsRowAndItsPeerRecoversTheFiring()
    {
        await nodeA.CheckIn(Guid.NewGuid());
        await nodeB.CheckIn(Guid.NewGuid());
        await FireOnA("t1");

        await ShutDown(nodeA);

        (await StateRows()).Should().Equal([NodeA, NodeB],
            "node A left a firing executing, and a peer finds that firing only through node A's row");
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == SchedulerStateKeptForFiredTriggers)
            .Which.Message.Should().Contain(NodeA).And.Contain("1 fired trigger");
        logs.Entries.Should().NotContain(x => x.EventId.Id == LeftCluster);

        await CheckInPastTheFailureThreshold(nodeB);

        logs.Entries.Should().ContainSingle(x => x.EventId.Id == FailedInstancesDetected,
            "a node that did not leave cleanly is recovered as a crashed one is");
        (await StateRows()).Should().Equal([NodeB], "the recovery deletes the row");
        (await RecoveryTriggerCount()).Should().Be(1,
            "the job asked for recovery, so the peer schedules again the firing node A left behind");
    }

    /// <summary>
    /// The node releases its automatic pins as a peer's recovery of it would, so another node claims the
    /// trigger on its next fire. An explicit pin waits for the node to come back.
    /// </summary>
    [Test]
    public async Task ANodeReleasesThePinsItClaimedAutomaticallyAsItLeaves()
    {
        await nodeA.CheckIn(Guid.NewGuid());
        await Schedule("auto");
        await Schedule("explicit");
        await Pin("auto", NodeA, automatic: true);
        await Pin("explicit", NodeA, automatic: false);

        await ShutDown(nodeA);

        (await PinOf("auto")).Should().Be(new StoredPin(StdAdoConstants.AutoPinSentinel, Automatic: false),
            "an automatic pin goes back to the sentinel, for any eligible node to claim");
        (await PinOf("explicit")).Should().Be(new StoredPin(NodeA, Automatic: false),
            "an explicit pin is the application's, and node A reclaims it when it returns");
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == LeftCluster)
            .Which.Message.Should().Contain("1 auto-pinned trigger");
    }

    /// <summary>
    /// A check-in that outlived the shutdown's wait would write the row back after the delete, or find it
    /// gone and report this node failed out. So the row stays, for a peer to recover.
    /// </summary>
    [Test]
    public async Task ANodeWhoseCheckInMayStillBeRunningKeepsItsRow()
    {
        await nodeA.CheckIn(Guid.NewGuid());

        await nodeA.LeaveCluster(checkInsEnded: false);

        (await StateRows()).Should().Equal([NodeA]);
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == SchedulerStateKeptForRunningCheckIn)
            .Which.Message.Should().Contain(NodeA);
    }

    /// <summary>
    /// A row with this node's id that this run never wrote is a previous run's, which a peer or this
    /// node's next start recovers. A store that never checked in — a window onto another process's
    /// cluster is one — leaves it alone.
    /// </summary>
    [Test]
    public async Task ANodeThatNeverCheckedInLeavesARowWithItsIdAlone()
    {
        await ExecuteNonQuery(
            "INSERT INTO QRTZ_SCHEDULER_STATE (SCHED_NAME, INSTANCE_NAME, LAST_CHECKIN_TIME, CHECKIN_INTERVAL) VALUES (@scheduler, @node, @checkedIn, 7500)",
            ("@scheduler", SchedulerName),
            ("@node", NodeA),
            ("@checkedIn", epoch.UtcTicks));

        await ShutDown(nodeA);

        (await StateRows()).Should().Equal([NodeA]);
        logs.Entries.Should().NotContain(x => x.EventId.Id >= LeftCluster && x.EventId.Id <= SchedulerStateNotDeleted,
            "a store that never joined the cluster has nothing to say about leaving it");
    }

    /// <summary>
    /// The delete failing is the outcome there was before 4.8: the row stays for a peer to recover. It
    /// is logged, and the shutdown goes on.
    /// </summary>
    [Test]
    public async Task ANodeThatCannotDeleteItsRowSaysSoAndStillShutsDown()
    {
        await nodeA.CheckIn(Guid.NewGuid());
        await ExecuteNonQuery("DROP TABLE QRTZ_FIRED_TRIGGERS");

        Func<Task> shutdown = () => ShutDown(nodeA);

        await shutdown.Should().CompleteWithinAsync(waitLimit, "a failure to leave the cluster must not stop the shutdown");
        (await StateRows()).Should().Equal([NodeA], "the transaction rolled back, and the row is a peer's to recover");
        LogEntry failure = logs.Entries.Should().ContainSingle(x => x.EventId.Id == SchedulerStateNotDeleted).Subject;
        failure.Level.Should().Be(LogLevel.Warning);
        failure.Exception.Should().NotBeNull();
    }

    private async Task<LocalTransactionJobStore> Node(string instanceId)
    {
        LocalTransactionJobStore store = new(TestJobStores.Dependencies(
            timeProvider: clock,
            schedulerOptions: TestJobStores.SchedulerOptions(instanceName: SchedulerName, instanceId: instanceId),
            storeOptions: TestJobStores.StoreOptions(DataSourceName),
            // A provider each, as two processes have: a node's shutdown shuts its provider down.
            dbProvider: new DbProvider("SQLite-Microsoft", database.ConnectionString),
            driverDelegate: new SQLiteDelegate(),
            loggerFactory: loggerFactory));

        // Initialized but not started, so no loop races an assertion; clustered once it is, which a
        // SQLite store refuses to be configured as.
        await store.Initialize(TestJobStores.Identity(instanceName: SchedulerName, instanceId: instanceId));
        store.Clustered = true;
        return store;
    }

    private async Task ShutDown(LocalTransactionJobStore node)
    {
        if (shutDown.Add(node))
        {
            await node.Shutdown();
        }
    }

    /// <summary>
    /// Checks in every five seconds until node A's row, last written at the epoch, is past the failure
    /// threshold. A peer's verdict also waits out the time since its own last check-in, so the steps are
    /// shorter than the interval: a single long one would grant the row that much more slack.
    /// </summary>
    private async Task CheckInPastTheFailureThreshold(LocalTransactionJobStore peer)
    {
        for (int step = 0; step < 4; step++)
        {
            clock.Advance(TimeSpan.FromSeconds(5));
            await peer.CheckIn(Guid.NewGuid());
        }
    }

    private async Task Schedule(string name)
    {
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity(name, Group).RequestRecovery().Build();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(clock.GetUtcNow())
            .Build();
        trigger.ComputeFirstFireTimeUtc(null);
        await nodeA.ScheduleJob(job, trigger);
    }

    private async Task FireOnA(string name)
    {
        await Schedule(name);
        List<IOperableTrigger> acquired = await nodeA.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddSeconds(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero,
        });
        List<TriggerFiredResult> fired = await nodeA.TriggersFired(acquired);
        fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull("the premise: node A's reservation is its own to fire");
    }

    private Task Pin(string triggerName, string node, bool automatic)
    {
        return ExecuteNonQuery(
            "UPDATE QRTZ_TRIGGERS SET PREFERRED_NODE = @node, PREFERRED_NODE_AUTO = @automatic WHERE TRIGGER_NAME = @name",
            ("@node", node),
            ("@automatic", automatic ? 1 : 0),
            ("@name", triggerName));
    }

    private async Task<StoredPin> PinOf(string triggerName)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT PREFERRED_NODE, PREFERRED_NODE_AUTO FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name";
        command.Parameters.AddWithValue("@name", triggerName);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue("the trigger is still stored");
        return new StoredPin(reader.GetString(0), Convert.ToBoolean(reader.GetValue(1), CultureInfo.InvariantCulture));
    }

    private async Task<List<string>> StateRows()
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT INSTANCE_NAME FROM QRTZ_SCHEDULER_STATE ORDER BY INSTANCE_NAME";

        List<string> rows = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private async Task<long> RecoveryTriggerCount()
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM QRTZ_TRIGGERS WHERE TRIGGER_GROUP = @group";
        command.Parameters.AddWithValue("@group", SchedulerConstants.DefaultRecoveryGroup);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private async Task ExecuteNonQuery(string sql, params (string Name, object Value)[] parameters)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static string TablesScript()
    {
        return File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";
    }

    private sealed record StoredPin(string Node, bool Automatic);

    /// <summary>Never executed: these tests drive the stores, not a scheduler.</summary>
    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
