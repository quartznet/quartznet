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
/// A node that acquired a trigger ahead of its fire time holds a copy of it until then. Another node
/// replaces the trigger meanwhile and acquires the replacement. The held copy is not fired, does not
/// write the old definition back, and its release does not undo the other node's acquisition (#4323).
/// </summary>
/// <remarks>
/// <para>
/// Two <see cref="JobStoreTX" /> instances on one SQLite file, as in
/// <see cref="AcquisitionBehindExecutingJobSqliteTest" />: each step is a store call awaited in sequence,
/// so the interleaving is the test's, and the rows are read straight off the tables. The clustered
/// PostgreSQL run of the same scenario is <see cref="ReplacedTriggerClusteredPostgresTest" />.
/// </para>
/// <para>
/// Run twice: through the shipped delegate, whose fire reads the trigger and its reservation in one
/// statement, and through a subclass of it, which keeps the single-trigger members it may override and
/// so reads the reservation on its own.
/// </para>
/// </remarks>
[TestFixture(true)]
[TestFixture(false)]
[NonParallelizable]
[Category("db-sqlite")]
public sealed class ReplacedTriggerAcquisitionSqliteTest
{
    private const string SchedulerName = "replaced-trigger-acquisition";
    private const string Group = "replaced";
    private static readonly JobKey jobKey = new JobKey("job", Group);
    private static readonly TriggerKey triggerKey = new TriggerKey("trigger", Group);

    private readonly bool shippedDelegate;

    private string databaseFile;
    private string connectionString;
    private JobStoreTX storeA;
    private JobStoreTX storeB;
    private DateTimeOffset due;

    public ReplacedTriggerAcquisitionSqliteTest(bool shippedDelegate)
    {
        this.shippedDelegate = shippedDelegate;
    }

    [SetUp]
    public async Task CreateTwoNodesOnOneDatabase()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-replaced-trigger-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";
        InstallSchemaFromFreshInstallScript();

        storeA = await BuildNode("node-a");
        storeB = await BuildNode("node-b");

        // Ahead of now, so that no misfire handling moves a fire time under the test.
        due = DateTimeOffset.UtcNow.AddSeconds(30);
        due = due.AddTicks(-(due.Ticks % TimeSpan.TicksPerSecond));

        await storeA.StoreJob(JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).StoreDurably().Build(), replaceExisting: false);
        await storeA.StoreTrigger(Trigger(TimeSpan.FromHours(1), due), replaceExisting: false);
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
    /// The issue's case, at the hardest fire time: the replacement is due when the held copy is, so
    /// comparing fire times alone would let the old copy fire and write its definition back.
    /// </summary>
    [Test]
    public async Task AHeldCopyOfATriggerAnotherNodeReplacedIsNotFiredAndDoesNotWriteItsDefinitionBack()
    {
        IOperableTrigger heldByA = await ReserveOne(storeA);

        (await storeB.ReplaceTrigger(triggerKey, Trigger(TimeSpan.FromHours(2), due))).Should().BeTrue();
        IOperableTrigger heldByB = await ReserveOne(storeB);
        heldByB.GetNextFireTimeUtc().Should().Be(heldByA.GetNextFireTimeUtc(), "the premise: the replacement is due when A's copy is");

        IReadOnlyCollection<TriggerFiredResult> onA = await storeA.TriggersFired(new[] { heldByA });
        onA.Should().ContainSingle().Which.TriggerFiredBundle.Should().BeNull(
            "A's acquisition was of the trigger B replaced, which is no longer stored");
        (await IntervalStored()).Should().Be(TimeSpan.FromHours(2), "A's copy is not written over the replacement");

        await storeA.ReleaseAcquiredTrigger(heldByA);
        (await StateOf()).Should().Be("ACQUIRED", "the row is B's acquisition, and A's release is not B's to undo");
        (await FiredRows()).Should().Equal(new[] { ("node-b", "ACQUIRED") }, "A's reservation went with the trigger it reserved");

        TriggerFiredBundle firedOnB = (await storeB.TriggersFired(new[] { heldByB })).Should().ContainSingle().Subject.TriggerFiredBundle;
        firedOnB.Should().NotBeNull("B's acquisition is still its own");
        ((ISimpleTrigger) firedOnB.Trigger).RepeatInterval.Should().Be(TimeSpan.FromHours(2));
        (await IntervalStored()).Should().Be(TimeSpan.FromHours(2));
        (await NextFireTimeStored()).Should().Be(due.AddHours(2), "the replacement's schedule is the one that moves on");
    }

    /// <summary>
    /// A held copy released before its fire time — the scheduler's schedule change, standby, shutdown —
    /// leaves the replacement as another node acquired it.
    /// </summary>
    [Test]
    public async Task TheReleaseOfAHeldCopyOfAReplacedTriggerLeavesTheOtherNodesAcquisition()
    {
        IOperableTrigger heldByA = await ReserveOne(storeA);

        await storeB.ReplaceTrigger(triggerKey, Trigger(TimeSpan.FromHours(2), due.AddSeconds(5)));
        IOperableTrigger heldByB = await ReserveOne(storeB);

        await storeA.ReleaseAcquiredTrigger(heldByA);

        (await StateOf()).Should().Be("ACQUIRED",
            "moved back to WAITING, the row could be acquired by a third node for the fire time B holds");
        (await FiredRows()).Should().Equal(new[] { ("node-b", "ACQUIRED") });
        (await storeB.TriggersFired(new[] { heldByB })).Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull(
            "B's acquisition survives A's release");
    }

    /// <summary>
    /// A replace by key that writes the row over rather than deleting it — a store call told to replace
    /// what is stored, on its own or with its job — cancels the held acquisition just the same.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task AReplaceInPlaceCancelsTheHeldAcquisitionToo(bool alongWithItsJob)
    {
        IOperableTrigger heldByA = await ReserveOne(storeA);

        IOperableTrigger replacement = Trigger(TimeSpan.FromHours(2), due);
        if (alongWithItsJob)
        {
            IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).StoreDurably().Build();
            await storeB.StoreJobsAndTriggers(
                new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> { [job] = new ITrigger[] { replacement } },
                replace: true);
        }
        else
        {
            await storeB.StoreTrigger(replacement, replaceExisting: true);
        }

        IOperableTrigger heldByB = await ReserveOne(storeB);

        (await storeA.TriggersFired(new[] { heldByA })).Should().ContainSingle().Which.TriggerFiredBundle.Should().BeNull();
        (await IntervalStored()).Should().Be(TimeSpan.FromHours(2));
        await storeA.ReleaseAcquiredTrigger(heldByA);

        (await storeB.TriggersFired(new[] { heldByB })).Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull();
        (await IntervalStored()).Should().Be(TimeSpan.FromHours(2));
    }

    /// <summary>
    /// A held copy whose fire time another node has fired since is not fired a second time, even while its
    /// reservation is still there: here a pause and resume let B acquire and fire the same fire time.
    /// </summary>
    [Test]
    public async Task AFireTimeAnotherNodeFiredSinceIsNotFiredAgainFromAnEarlierAcquisition()
    {
        IOperableTrigger heldByA = await ReserveOne(storeA);

        await storeB.PauseTrigger(triggerKey);
        await storeB.ResumeTrigger(triggerKey);
        TriggerFiredBundle firedOnB = (await storeB.TriggersFired(new[] { await ReserveOne(storeB) })).Should().ContainSingle().Subject.TriggerFiredBundle;
        firedOnB.Should().NotBeNull("the premise: B fired the fire time A still holds");
        await storeB.TriggeredJobComplete(firedOnB.Trigger, firedOnB.JobDetail, SchedulerInstruction.NoInstruction);
        IOperableTrigger nextOnB = await ReserveOne(storeB);
        nextOnB.GetNextFireTimeUtc().Should().Be(due.AddHours(1), "the premise: B holds the fire time after it");

        (await storeA.TriggersFired(new[] { heldByA })).Should().ContainSingle().Which.TriggerFiredBundle.Should().BeNull(
            $"{due:O} has run, on B, and A's acquisition of it is out of date");
        (await NextFireTimeStored()).Should().Be(due.AddHours(1), "A's fire did not move the trigger");
    }

    /// <summary>
    /// What a replace cancels is a reservation, not an execution: the fired-trigger row of a firing still
    /// running stays, for its completion or for recovery to settle (#3759).
    /// </summary>
    [Test]
    public async Task AReplaceKeepsTheRowOfAFiringStillRunning()
    {
        TriggerFiredBundle running = (await storeA.TriggersFired(new[] { await ReserveOne(storeA) })).Should().ContainSingle().Subject.TriggerFiredBundle;
        running.Should().NotBeNull();
        IOperableTrigger heldByA = await ReserveOne(storeA);

        await storeB.ReplaceTrigger(triggerKey, Trigger(TimeSpan.FromHours(2), due.AddHours(3)));

        (await FiredRows()).Should().Equal(new[] { ("node-a", "EXECUTING") },
            "the reservation of the held copy is cancelled, the row of the firing still running is not");
        (await storeA.TriggersFired(new[] { heldByA })).Should().ContainSingle().Which.TriggerFiredBundle.Should().BeNull();
        await storeA.ReleaseAcquiredTrigger(heldByA);
        (await StateOf()).Should().Be("WAITING", "nothing holds the replacement");

        await storeA.TriggeredJobComplete(running.Trigger, running.JobDetail, SchedulerInstruction.NoInstruction);
        (await FiredRows()).Should().BeEmpty();
    }

    /// <summary>
    /// The control: an acquisition nothing replaced is fired and released as before.
    /// </summary>
    [Test]
    public async Task AnAcquisitionNothingReplacedIsFiredAndReleasedAsBefore()
    {
        IOperableTrigger held = await ReserveOne(storeA);
        await storeA.ReleaseAcquiredTrigger(held);
        (await StateOf()).Should().Be("WAITING");
        (await FiredRows()).Should().BeEmpty();

        TriggerFiredBundle fired = (await storeA.TriggersFired(new[] { await ReserveOne(storeA) })).Should().ContainSingle().Subject.TriggerFiredBundle;
        fired.Should().NotBeNull();
        (await NextFireTimeStored()).Should().Be(due.AddHours(1));
    }

    /// <summary>
    /// A trigger as the scheduler hands it to the store: its first fire time worked out.
    /// </summary>
    private static IOperableTrigger Trigger(TimeSpan interval, DateTimeOffset startAt)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartAt(startAt)
            .WithSimpleSchedule(x => x.WithInterval(interval).RepeatForever())
            .Build();

        trigger.ComputeFirstFireTimeUtc(null);
        return trigger;
    }

    private static async Task<IOperableTrigger> ReserveOne(IJobStore store)
    {
        IReadOnlyCollection<IOperableTrigger> acquired = await store.AcquireNextTriggers(DateTimeOffset.UtcNow.AddDays(1), 1, TimeSpan.Zero);
        return acquired.Should().ContainSingle("the trigger is the only one stored, and it is waiting").Subject;
    }

    private async Task<TimeSpan> IntervalStored()
    {
        return TimeSpan.FromMilliseconds(await ReadLong("SELECT REPEAT_INTERVAL FROM QRTZ_SIMPLE_TRIGGERS WHERE TRIGGER_NAME = @name"));
    }

    private async Task<DateTimeOffset> NextFireTimeStored()
    {
        return new DateTimeOffset(await ReadLong("SELECT NEXT_FIRE_TIME FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name"), TimeSpan.Zero);
    }

    private async Task<string> StateOf()
    {
        using SqliteConnection connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name";
        command.Parameters.AddWithValue("@name", triggerKey.Name);
        return (string) await command.ExecuteScalarAsync();
    }

    private async Task<long> ReadLong(string sql)
    {
        using SqliteConnection connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@name", triggerKey.Name);
        return (long) await command.ExecuteScalarAsync();
    }

    private async Task<List<(string Instance, string State)>> FiredRows()
    {
        using SqliteConnection connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT INSTANCE_NAME, STATE FROM QRTZ_FIRED_TRIGGERS ORDER BY INSTANCE_NAME, STATE";

        List<(string, string)> rows = new List<(string, string)>();
        using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        return rows;
    }

    private async Task<JobStoreTX> BuildNode(string instanceId)
    {
        // A data source per node, as two processes would have: a store's shutdown shuts its own down.
        string dataSource = $"replaced-trigger-{instanceId}-{Guid.NewGuid():N}";
        DBConnectionManager.Instance.AddConnectionProvider(dataSource, new DbProvider("SQLite-Microsoft", connectionString));

        SystemTextJsonObjectSerializer serializer = new SystemTextJsonObjectSerializer();
        serializer.Initialize();

        JobStoreTX store = new JobStoreTX
        {
            DataSource = dataSource,
            TablePrefix = "QRTZ_",
            InstanceName = SchedulerName,
            InstanceId = instanceId,
            DriverDelegateType = (shippedDelegate ? typeof(SQLiteDelegate) : typeof(SubclassedSqliteDelegate)).AssemblyQualifiedName,
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
    /// A delegate of somebody's own: the store asks it only the members it may have overridden.
    /// </summary>
    public sealed class SubclassedSqliteDelegate : SQLiteDelegate
    {
    }

    public sealed class NoOpJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
