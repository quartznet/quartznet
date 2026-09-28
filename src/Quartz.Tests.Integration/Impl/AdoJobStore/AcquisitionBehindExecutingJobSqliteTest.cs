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
using System.Threading;
using System.Threading.Tasks;

using FakeItEasy;

using Microsoft.Data.Sqlite;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Two nodes of one store, driven by hand through the interleaving that left a clustered node idle
/// behind a <see cref="DisallowConcurrentExecutionAttribute" /> job running on the other (#3926).
/// </summary>
/// <remarks>
/// <para>
/// The interleaving: node A reserves one trigger of the job and node B the other, A fires its trigger
/// — which moves every other trigger of the job to <c>BLOCKED</c>, B's reservation included — and B's
/// fire finds its trigger no longer reservable and lets go of it. The release used to put the trigger
/// back to <c>WAITING</c>, undoing the block: first in the fire-time order and skipped by every node
/// as executing, it hid what was due behind it from a node reading one row at a time, and nothing
/// unblocked it because nothing considered it blocked.
/// </para>
/// <para>
/// Two <see cref="JobStoreTX" /> instances on one SQLite file rather than a clustered engine: every
/// step here is a store call awaited in sequence, so the nodes need no lock between them, and the
/// states are read straight off the rows. The same scenario under two live scheduler threads is
/// <c>ClusteredAcquisitionSkipTestBase</c>.
/// </para>
/// </remarks>
[NonParallelizable]
[Category("db-sqlite")]
public sealed class AcquisitionBehindExecutingJobSqliteTest
{
    private const string SchedulerName = "acquisition-behind-executing-job";
    private const string Group = "behind";
    private static readonly JobKey SerialJobKey = new JobKey("serial", Group);
    private static readonly JobKey OrdinaryJobKey = new JobKey("ordinary", Group);

    private string databaseFile;
    private string connectionString;
    private JobStoreTX storeA;
    private JobStoreTX storeB;
    private DateTimeOffset due;

    [SetUp]
    public async Task CreateTwoNodesOnOneDatabase()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-acquisition-behind-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";
        InstallSchemaFromFreshInstallScript();

        CountingSqliteDelegate.Reset();
        storeA = await BuildNode(connectionString, "node-a");
        storeB = await BuildNode(connectionString, "node-b");

        // Ahead of now, so the misfire cutoff keeps out of the acquisition read, and by a known margin,
        // so the fire-time order — the serial job's triggers first, the ordinary ones behind them — is
        // the order every read below returns.
        due = DateTimeOffset.UtcNow.AddSeconds(30);
    }

    [TearDown]
    public async Task DisposeNodes()
    {
        await storeB.Shutdown();
        await storeA.Shutdown();

        SqliteConnection.ClearAllPools();
        if (File.Exists(databaseFile))
        {
            File.Delete(databaseFile);
        }
    }

    /// <summary>
    /// The path the issue suspected: B's reservation, blocked by A's fire, is released after B's fire
    /// finds it blocked — and stays blocked, for A's completion to let go of.
    /// </summary>
    [Test]
    public async Task AReservationBlockedByAnotherNodesFireStaysBlockedWhenItsFireIsRefusedAndItIsReleased()
    {
        await ScheduleSerialTriggers(2);

        IOperableTrigger onA = await ReserveOne(storeA);
        IOperableTrigger onB = await ReserveOne(storeB);
        onA.Key.Name.Should().Be("serial-1");
        onB.Key.Name.Should().Be("serial-2", "the second node reserves the job's other trigger, since nothing is executing yet");

        TriggerFiredBundle firedOnA = await Fire(storeA, onA);
        (await StateOf("serial-2")).Should().Be("BLOCKED",
            "A's fire moves every other trigger of the job out of reach, B's reservation included");

        IReadOnlyCollection<TriggerFiredResult> refusedOnB = await storeB.TriggersFired(new[] { onB });
        TriggerFiredResult refused = refusedOnB.Should().ContainSingle().Subject;
        refused.TriggerFiredBundle.Should().BeNull("the trigger is no longer B's to fire");
        refused.Exception.Should().BeNull("this is the not-fired answer the scheduler thread releases after");

        await storeB.ReleaseAcquiredTrigger(onB);

        (await StateOf("serial-2")).Should().Be("BLOCKED",
            "the job is executing on A, so the release must leave the trigger where A's fire put it: a WAITING row of "
            + "an executing job is skipped by every node's acquisition and sits first in the order until the job ends");
        (await FiredRows()).Should().Equal(new[] { "EXECUTING" }, "B's reservation row is gone and A's execution is all that is left");

        await storeA.TriggeredJobComplete(firedOnA.Trigger, firedOnA.JobDetail, SchedulerInstruction.NoInstruction);

        (await StateOf("serial-2")).Should().Be("WAITING", "A's completion is what lets go of the job's triggers");
        (await FiredRows()).Should().BeEmpty();
    }

    /// <summary>
    /// The release the scheduler thread makes before firing — on a schedule change, or at shutdown —
    /// meets the same block and must keep it just the same.
    /// </summary>
    [Test]
    public async Task AReservationReleasedWithoutFiringStaysBlockedWhileItsJobExecutesElsewhere()
    {
        await ScheduleSerialTriggers(2);

        IOperableTrigger onA = await ReserveOne(storeA);
        IOperableTrigger onB = await ReserveOne(storeB);
        TriggerFiredBundle firedOnA = await Fire(storeA, onA);

        await storeB.ReleaseAcquiredTrigger(onB);

        (await StateOf("serial-2")).Should().Be("BLOCKED", "the job is executing on A whether or not B ever tried to fire");

        await storeA.TriggeredJobComplete(firedOnA.Trigger, firedOnA.JobDetail, SchedulerInstruction.NoInstruction);
        (await StateOf("serial-2")).Should().Be("WAITING");
    }

    /// <summary>
    /// The control: with nothing of the job executing, a released reservation is back to waiting.
    /// </summary>
    [Test]
    public async Task AReservationOfAnIdleJobIsReleasedToWaiting()
    {
        await ScheduleSerialTriggers(1);

        IOperableTrigger onB = await ReserveOne(storeB);
        (await StateOf("serial-1")).Should().Be("ACQUIRED");

        await storeB.ReleaseAcquiredTrigger(onB);

        (await StateOf("serial-1")).Should().Be("WAITING", "nothing holds the job, so nothing holds its trigger");
        (await FiredRows()).Should().BeEmpty();
    }

    /// <summary>
    /// The other half of the issue, on what a node without the fix — a 3.22.1 node in the same cluster
    /// — still leaves behind: WAITING rows of a job that is executing. A node reading one row at a time
    /// used to read the first of them three times over and give up, and the ordinary trigger due
    /// behind them waited out the node's idle time.
    /// </summary>
    [Test]
    public async Task ANodeAcquiringOneTriggerAtATimeReadsPastTheRowsItSkips()
    {
        await ScheduleSerialTriggers(4);
        await ScheduleOrdinaryTriggers(2);

        IOperableTrigger onA = await ReserveOne(storeA);
        await Fire(storeA, onA);
        await LeaveWaitingAsAnUnfixedNodeWould("serial-2", "serial-3", "serial-4");
        CountingSqliteDelegate.Reset();

        IReadOnlyCollection<IOperableTrigger> acquired = await AcquireAtMost(storeB, 1);

        acquired.Should().ContainSingle(
            "the read is widened past the skipped rows, but what is taken is still capped at the one trigger asked for")
            .Which.Key.Name.Should().Be("ordinary-1", "the first trigger due that is not held behind the executing job");
        CountingSqliteDelegate.AcquisitionReadCounts.Should().Equal(new[] { 1, 3, 7 },
            "the first read is the count asked for and nothing more; each read after a round that skipped everything "
            + "reaches at least twice as far past the count as the rows it skipped, so the rounds stay few");
        (await StateOf("ordinary-1")).Should().Be("ACQUIRED");
        (await StateOf("ordinary-2")).Should().Be("WAITING", "the cap: one trigger was asked for");
    }

    /// <summary>
    /// An acquisition that skips nothing costs what it always did: one read of the count asked for.
    /// </summary>
    [Test]
    public async Task AnAcquisitionThatSkipsNothingReadsOnce()
    {
        await ScheduleOrdinaryTriggers(2);
        CountingSqliteDelegate.Reset();

        IReadOnlyCollection<IOperableTrigger> acquired = await AcquireAtMost(storeB, 1);

        acquired.Should().ContainSingle().Which.Key.Name.Should().Be("ordinary-1");
        CountingSqliteDelegate.AcquisitionReadCounts.Should().Equal(new[] { 1 }, "nothing was skipped, so there was nothing to read past");
    }

    /// <summary>
    /// Reading past ends the moment a read comes back short of its limit: there is nothing behind the
    /// skipped rows, and the round says so after one wider look rather than after three identical ones.
    /// </summary>
    [Test]
    public async Task ARoundThatSkipsEverythingWithNothingBehindItStopsAfterOneWiderRead()
    {
        await ScheduleSerialTriggers(2);

        IOperableTrigger onA = await ReserveOne(storeA);
        await Fire(storeA, onA);
        await LeaveWaitingAsAnUnfixedNodeWould("serial-2");
        CountingSqliteDelegate.Reset();

        IReadOnlyCollection<IOperableTrigger> acquired = await AcquireAtMost(storeB, 1);

        acquired.Should().BeEmpty("the only row due belongs to the executing job");
        CountingSqliteDelegate.AcquisitionReadCounts.Should().Equal(new[] { 1, 3 },
            "the wider read came back with the one skipped row and nothing behind it, which is the answer");
    }

    /// <summary>
    /// A row due beyond the batch is not a skipped row: everything behind it is due later still, so
    /// there is nothing to read past to, however many rows the round could not use.
    /// </summary>
    [Test]
    public async Task ARoundThatEndsAtTheBatchWindowDoesNotReadPast()
    {
        await ScheduleSerialTriggers(2);
        await ScheduleOrdinaryTriggers(1, dueAfter: TimeSpan.FromMinutes(5));

        IOperableTrigger onA = await ReserveOne(storeA);
        await Fire(storeA, onA);
        await LeaveWaitingAsAnUnfixedNodeWould("serial-2");
        CountingSqliteDelegate.Reset();

        // The ordinary trigger is inside the read's window but beyond what this batch may take.
        IReadOnlyCollection<IOperableTrigger> acquired = await storeB.AcquireNextTriggers(
            due.AddMinutes(1),
            2,
            TimeSpan.FromMinutes(10));

        acquired.Should().BeEmpty();
        CountingSqliteDelegate.AcquisitionReadCounts.Should().Equal(new[] { 2 }, "the round reached the end of the batch, and there is nothing due behind that");
    }

    /// <summary>
    /// A candidate that is gone by the time its row is read back is a race, not a skip: the round is
    /// retried at the same length, as it always was, and reads nothing extra.
    /// </summary>
    [Test]
    public async Task ACandidateThatVanishesBetweenTheReadsIsRetriedAtTheSameLength()
    {
        await ScheduleOrdinaryTriggers(1);
        CountingSqliteDelegate.Reset();
        CountingSqliteDelegate.DropFirstCandidateOnce = true;

        IReadOnlyCollection<IOperableTrigger> acquired = await AcquireAtMost(storeB, 1);

        acquired.Should().ContainSingle().Which.Key.Name.Should().Be("ordinary-1", "the retry found the row again");
        CountingSqliteDelegate.AcquisitionReadCounts.Should().Equal(new[] { 1, 1 },
            "a row that left the round is not one a longer read is needed for: the same read, made again, no longer returns it");
    }

    private static Task<IReadOnlyCollection<IOperableTrigger>> AcquireAtMost(IJobStore store, int maxCount)
    {
        return store.AcquireNextTriggers(DateTimeOffset.UtcNow.AddMinutes(5), maxCount, TimeSpan.Zero);
    }

    private static async Task<IOperableTrigger> ReserveOne(IJobStore store)
    {
        IReadOnlyCollection<IOperableTrigger> acquired = await AcquireAtMost(store, 1);
        return acquired.Should().ContainSingle().Subject;
    }

    private static async Task<TriggerFiredBundle> Fire(IJobStore store, IOperableTrigger trigger)
    {
        IReadOnlyCollection<TriggerFiredResult> fired = await store.TriggersFired(new[] { trigger });
        TriggerFiredBundle bundle = fired.Should().ContainSingle().Subject.TriggerFiredBundle;
        bundle.Should().NotBeNull("the premise: this node's reservation was still its own to fire");
        return bundle;
    }

    private async Task ScheduleSerialTriggers(int count)
    {
        await storeA.StoreJob(JobBuilder.Create<SerialJob>().WithIdentity(SerialJobKey).StoreDurably().Build(), replaceExisting: true);
        for (int i = 1; i <= count; i++)
        {
            await StoreTrigger(TriggerBuilder.Create()
                .WithIdentity($"serial-{i}", Group)
                .ForJob(SerialJobKey)
                .StartAt(due.AddMilliseconds(i))
                .Build());
        }
    }

    private async Task ScheduleOrdinaryTriggers(int count, TimeSpan dueAfter = default)
    {
        await storeA.StoreJob(JobBuilder.Create<OrdinaryJob>().WithIdentity(OrdinaryJobKey).StoreDurably().Build(), replaceExisting: true);
        for (int i = 1; i <= count; i++)
        {
            // Behind every serial trigger in the fire-time order.
            await StoreTrigger(TriggerBuilder.Create()
                .WithIdentity($"ordinary-{i}", Group)
                .ForJob(OrdinaryJobKey)
                .StartAt(due.AddSeconds(1).Add(dueAfter).AddMilliseconds(i))
                .Build());
        }
    }

    /// <summary>
    /// What the scheduler does with a trigger before handing it to the store.
    /// </summary>
    private async Task StoreTrigger(ITrigger trigger)
    {
        IOperableTrigger operable = (IOperableTrigger) trigger;
        operable.ComputeFirstFireTimeUtc(null);
        await storeA.StoreTrigger(operable, replaceExisting: false);
    }

    /// <summary>
    /// What a node without this fix leaves behind for the rest of the cluster: a trigger of a job that is
    /// executing, released to WAITING rather than left BLOCKED.
    /// </summary>
    private async Task LeaveWaitingAsAnUnfixedNodeWould(params string[] triggerNames)
    {
        foreach (string triggerName in triggerNames)
        {
            (await StateOf(triggerName)).Should().Be("BLOCKED", "the premise: A's fire blocked it");
            await ExecuteNonQuery("UPDATE QRTZ_TRIGGERS SET TRIGGER_STATE = 'WAITING' WHERE TRIGGER_NAME = @name", triggerName);
        }
    }

    private Task<string> StateOf(string triggerName)
    {
        return ReadScalar("SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name", triggerName);
    }

    private async Task<List<string>> FiredRows()
    {
        using SqliteConnection connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT STATE FROM QRTZ_FIRED_TRIGGERS ORDER BY STATE";

        List<string> states = new List<string>();
        using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            states.Add(reader.GetString(0));
        }

        return states;
    }

    private async Task<string> ReadScalar(string sql, string name)
    {
        using SqliteConnection connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@name", name);
        return (string) await command.ExecuteScalarAsync();
    }

    private async Task ExecuteNonQuery(string sql, string name)
    {
        using SqliteConnection connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@name", name);
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
    }

    private static async Task<JobStoreTX> BuildNode(string connectionString, string instanceId)
    {
        // A data source per node, as two processes would have: a store's shutdown shuts its own down.
        string dataSource = $"acquisition-behind-{instanceId}-{Guid.NewGuid():N}";
        DBConnectionManager.Instance.AddConnectionProvider(dataSource, new DbProvider("SQLite-Microsoft", connectionString));

        SystemTextJsonObjectSerializer serializer = new SystemTextJsonObjectSerializer();
        serializer.Initialize();

        JobStoreTX store = new JobStoreTX
        {
            DataSource = dataSource,
            TablePrefix = "QRTZ_",
            InstanceName = SchedulerName,
            InstanceId = instanceId,
            DriverDelegateType = typeof(CountingSqliteDelegate).AssemblyQualifiedName,
            ObjectSerializer = serializer,
        };

        // Initialized and never started: nothing here is left to a scheduler thread, a misfire pass or
        // a startup recovery, each of which would move the rows these tests read.
        await store.Initialize(new SimpleTypeLoadHelper(), A.Fake<ISchedulerSignaler>());
        return store;
    }

    private void InstallSchemaFromFreshInstallScript()
    {
        using SqliteConnection connection = new SqliteConnection(connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = File.ReadAllText(ResolveRepositoryFile("database", "tables", "tables_sqlite.sql"));
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

    /// <summary>
    /// The shipped SQLite delegate, recording how long each acquisition read was asked to be.
    /// </summary>
    /// <remarks>
    /// Static, because the stores build the delegate from its type name and both nodes share the
    /// tally; the fixture zeroes it before the acquisition it is about. Every
    /// <c>SelectTriggerToAcquire</c> overload is recorded, so the tally does not depend on which of
    /// them the store's acquisition takes - and overriding the preferred-node one too keeps the store on
    /// the path a fresh 3.x schema gives it.
    /// </remarks>
    public sealed class CountingSqliteDelegate : SQLiteDelegate
    {
        private static readonly List<int> acquisitionReadCounts = new List<int>();
        private static volatile bool dropFirstCandidateOnce;

        /// <summary>The <c>maxCount</c> of every acquisition read, in order.</summary>
        public static List<int> AcquisitionReadCounts
        {
            get
            {
                lock (acquisitionReadCounts)
                {
                    return acquisitionReadCounts.ToList();
                }
            }
        }

        /// <summary>
        /// Makes the next trigger read come back empty, as if the trigger had been deleted between the
        /// acquisition read and the read of the row it named.
        /// </summary>
        public static bool DropFirstCandidateOnce
        {
            get => dropFirstCandidateOnce;
            set => dropFirstCandidateOnce = value;
        }

        public static void Reset()
        {
            lock (acquisitionReadCounts)
            {
                acquisitionReadCounts.Clear();
            }

            dropFirstCandidateOnce = false;
        }

        public override Task<IReadOnlyCollection<TriggerAcquireResult>> SelectTriggerToAcquire(
            ConnectionAndTransactionHolder conn,
            DateTimeOffset noLaterThan,
            DateTimeOffset noEarlierThan,
            int maxCount,
            CancellationToken cancellationToken = default)
        {
            Record(maxCount);
            return base.SelectTriggerToAcquire(conn, noLaterThan, noEarlierThan, maxCount, cancellationToken);
        }

        public override Task<List<TriggerAcquireResult>> SelectTriggerToAcquire(
            ConnectionAndTransactionHolder conn,
            DateTimeOffset noLaterThan,
            DateTimeOffset noEarlierThan,
            int maxCount,
            Dictionary<string, int?> executionLimits,
            CancellationToken cancellationToken = default)
        {
            Record(maxCount);
            return base.SelectTriggerToAcquire(conn, noLaterThan, noEarlierThan, maxCount, executionLimits, cancellationToken);
        }

        public override Task<List<TriggerAcquireResult>> SelectTriggerToAcquire(
            ConnectionAndTransactionHolder conn,
            DateTimeOffset noLaterThan,
            DateTimeOffset noEarlierThan,
            int maxCount,
            Dictionary<string, int?> executionLimits,
            string instanceId,
            long liveNodeCutoff,
            CancellationToken cancellationToken = default)
        {
            Record(maxCount);
            return base.SelectTriggerToAcquire(conn, noLaterThan, noEarlierThan, maxCount, executionLimits, instanceId, liveNodeCutoff, cancellationToken);
        }

        public override Task<IOperableTrigger> SelectTrigger(
            ConnectionAndTransactionHolder conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            if (dropFirstCandidateOnce)
            {
                dropFirstCandidateOnce = false;
                return Task.FromResult<IOperableTrigger>(null);
            }

            return base.SelectTrigger(conn, triggerKey, cancellationToken);
        }

        private static void Record(int maxCount)
        {
            lock (acquisitionReadCounts)
            {
                acquisitionReadCounts.Add(maxCount);
            }
        }
    }

    [DisallowConcurrentExecution]
    public sealed class SerialJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }

    public sealed class OrdinaryJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
