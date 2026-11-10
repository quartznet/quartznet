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
using System.Data.Common;
using System.IO;
using System.Linq;
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
/// <see cref="IJobStore.ReleaseAcquiredTrigger" /> on the persistent store, given a reservation that never
/// fired and a fire whose commit landed although the scheduler was told it failed (#4259).
/// </summary>
/// <remarks>
/// <para>
/// The scheduler thread releases every trigger of a batch whose fire it believes failed. When the commit
/// had in fact reached the database, the trigger's fired row is <c>EXECUTING</c>, and for a job that
/// disallows concurrent execution the fire has moved the job's other triggers to <c>BLOCKED</c> and
/// <c>PAUSED_BLOCKED</c>. No completion is coming for a firing the scheduler never dispatched, so the
/// release is what has to let go of them, unless another firing of the job is still executing.
/// </para>
/// <para>
/// A fire that commits normally leaves the database exactly as one whose commit landed unseen, so these
/// tests fire normally and then release, as the scheduler thread does when it could not tell.
/// <see cref="TriggersFiredBrokenCommitSqliteTest" /> drives the broken commit itself. The store is set up
/// as that test sets it up: one <see cref="JobStoreTX" /> on a SQLite file nobody else touches,
/// initialized and never started, on a clock the test sets, with the shipped SQLite delegate recording
/// the statements it prepares.
/// </para>
/// </remarks>
[NonParallelizable]
[Category("db-sqlite")]
public sealed class ReleaseAcquiredTriggerSqliteTest
{
    private const string SchedulerName = "ReleaseAcquiredTriggerSqliteTest";
    private const string Group = "release";

    /// <summary>On the hour, UTC, far from any machine's own clock.</summary>
    private static readonly DateTimeOffset Epoch = new DateTimeOffset(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    private Func<DateTimeOffset> originalUtcNow;
    private DateTimeOffset now;
    private string databaseFile;
    private string connectionString;
    private RecordingJobStore store;

    [SetUp]
    public async Task BuildStore()
    {
        originalUtcNow = SystemTime.UtcNow;
        now = Epoch;
        SystemTime.UtcNow = () => now;

        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-release-acquired-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";
        InstallSchemaFromFreshInstallScript(connectionString);

        string dataSource = $"release-acquired-{Guid.NewGuid():N}";
        DBConnectionManager.Instance.AddConnectionProvider(dataSource, new DbProvider("SQLite-Microsoft", connectionString));

        SystemTextJsonObjectSerializer serializer = new SystemTextJsonObjectSerializer();
        serializer.Initialize();

        store = new RecordingJobStore
        {
            DataSource = dataSource,
            TablePrefix = "QRTZ_",
            InstanceName = SchedulerName,
            InstanceId = "node-a",
            DriverDelegateType = typeof(RecordingSqliteDelegate).AssemblyQualifiedName,
            ObjectSerializer = serializer,
        };

        // Initialized but deliberately not started, so no misfire pass or recovery moves the rows read here.
        await store.Initialize(new SimpleTypeLoadHelper(), A.Fake<ISchedulerSignaler>());
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        await store.Shutdown();
        SqliteConnection.ClearAllPools();
        if (File.Exists(databaseFile))
        {
            File.Delete(databaseFile);
        }

        SystemTime.UtcNow = originalUtcNow;
    }

    /// <summary>
    /// The fire committed and the scheduler released it. Nothing else of the job is executing, so the
    /// job's other triggers go back to where the fire found them, as a completion would put them.
    /// </summary>
    [Test]
    public async Task ReleasingAFireThatCommittedLetsGoOfItsSerialJobsOtherTriggers()
    {
        IJobDetail job = SerialJob();
        IOperableTrigger due = Hourly("due", job, now);
        IOperableTrigger waiting = OneShot("waiting", job, now.AddHours(1));
        IOperableTrigger paused = OneShot("paused", job, now.AddHours(1));
        await Schedule(job, due, waiting, paused);
        await store.PauseTrigger(paused.Key);

        IOperableTrigger acquired = await AcquireOne(due.Key);
        await FireCommitted(acquired);

        (await StoredState(waiting.Key)).Should().Be(AdoConstants.StateBlocked, "the premise: the fire holds the job's other triggers back");
        (await StoredState(paused.Key)).Should().Be(AdoConstants.StatePausedBlocked, "the premise: a paused one too");

        await store.ReleaseAcquiredTrigger(acquired);

        (await FiredEntries()).Should().BeEmpty("the release deletes the fired row whatever its state");
        (await StoredState(waiting.Key)).Should().Be(AdoConstants.StateWaiting,
            "nothing of the job is executing and no completion is coming, so the release lets go of the job's other triggers");
        (await StoredState(paused.Key)).Should().Be(AdoConstants.StatePaused, "a paused trigger the fire held back goes back to paused");
        (await StoredState(due.Key)).Should().Be(AdoConstants.StateWaiting, "the released trigger is let go of too, at the fire time the fire moved it to");
    }

    /// <summary>
    /// A trigger held back by the fire may have gone past its fire time by the time the release lets go of
    /// it. The release applies its misfire policy, as a completion does for the triggers it lets go of.
    /// </summary>
    [Test]
    public async Task ReleasingAFireThatCommittedAppliesTheMisfirePolicyOfATriggerThatWentLateWhileHeld()
    {
        IJobDetail job = SerialJob();
        IOperableTrigger due = OneShot("due", job, now);
        IOperableTrigger late = OneShot("late", job, now.AddMinutes(1), MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount);
        await Schedule(job, due, late);

        IOperableTrigger acquired = await AcquireOne(due.Key);
        await FireCommitted(acquired);
        (await StoredState(late.Key)).Should().Be(AdoConstants.StateBlocked, "the premise: the fire holds it back");

        // Well past the misfire threshold of a trigger due a minute after the fire.
        now = now.AddHours(1);

        await store.ReleaseAcquiredTrigger(acquired);

        (await StoredState(late.Key)).Should().BeNull(
            "its only firing was missed while it was held, and its policy leaves it nothing to fire, so it is deleted as a completion deletes it");
    }

    /// <summary>
    /// The fire committed, but another firing of the same job is executing too, as a firing on another
    /// node of a cluster can be. That firing's completion is what lets go of the job's triggers, so the
    /// release leaves them held.
    /// </summary>
    [Test]
    public async Task ReleasingAFireThatCommittedLeavesItsJobsTriggersHeldWhileAnotherFiringOfTheJobExecutes()
    {
        IJobDetail job = SerialJob();
        IOperableTrigger due = Hourly("due", job, now);
        IOperableTrigger waiting = OneShot("waiting", job, now.AddHours(1));
        IOperableTrigger paused = OneShot("paused", job, now.AddHours(1));
        await Schedule(job, due, waiting, paused);
        await store.PauseTrigger(paused.Key);

        IOperableTrigger acquired = await AcquireOne(due.Key);
        await FireCommitted(acquired);
        await InsertExecutingFiringOnAnotherNode("node-b-firing", waiting.Key, job.Key);

        await store.ReleaseAcquiredTrigger(acquired);

        (await FiredEntries()).Should().Equal(new[] { "node-b-firing" }, "the released row is gone and the other node's firing is not the release's to touch");
        (await StoredState(waiting.Key)).Should().Be(AdoConstants.StateBlocked, "the job is still executing on the other node");
        (await StoredState(paused.Key)).Should().Be(AdoConstants.StatePausedBlocked, "the job is still executing on the other node");
        (await StoredState(due.Key)).Should().Be(AdoConstants.StateBlocked, "the released trigger is held back with the rest of the job");
    }

    /// <summary>
    /// The release of a reservation that never fired, while another firing of its job executes: the
    /// reservation stays where that fire put it, and so do the job's other triggers (#3926).
    /// </summary>
    [Test]
    public async Task ReleasingAReservationLeavesItsJobsTriggersHeldWhileAnotherFiringOfTheJobExecutes()
    {
        IJobDetail job = SerialJob();
        IOperableTrigger reserved = OneShot("reserved", job, now);
        IOperableTrigger running = OneShot("running", job, now.AddMilliseconds(500));
        IOperableTrigger waiting = OneShot("waiting", job, now.AddHours(1));
        await Schedule(job, reserved, running, waiting);

        IOperableTrigger acquired = await AcquireOne(reserved.Key);
        IOperableTrigger other = await AcquireOne(running.Key);
        TriggerFiredBundle executing = await FireCommitted(other);

        (await StoredState(reserved.Key)).Should().Be(AdoConstants.StateBlocked, "the premise: the other fire holds back the reservation as well");

        await store.ReleaseAcquiredTrigger(acquired);

        (await FiredEntries()).Should().Equal(new[] { executing.Trigger.FireInstanceId }, "the reservation's row is gone and the running firing's is left");
        (await StoredState(reserved.Key)).Should().Be(AdoConstants.StateBlocked, "the job is executing, so the reservation is held back with the rest");
        (await StoredState(waiting.Key)).Should().Be(AdoConstants.StateBlocked, "the job is executing");
    }

    /// <summary>
    /// The ordinary release: a reservation the scheduler gives back unfired — a sooner trigger, standby,
    /// shutdown. Only its own trigger moves, with the four statements the release has always made.
    /// </summary>
    [Test]
    public async Task ReleasingAReservationThatNeverFiredPutsBackItsTriggerAloneWithTheStatementsItAlwaysMade()
    {
        IJobDetail job = SerialJob();
        IOperableTrigger due = OneShot("due", job, now);
        IOperableTrigger waiting = OneShot("waiting", job, now.AddHours(1));
        await Schedule(job, due, waiting);

        IOperableTrigger acquired = await AcquireOne(due.Key);

        store.Recorder.Start();
        await store.ReleaseAcquiredTrigger(acquired);
        List<string> statements = store.Recorder.Stop();

        (await FiredEntries()).Should().BeEmpty();
        (await StoredState(due.Key)).Should().Be(AdoConstants.StateWaiting);
        (await StoredState(waiting.Key)).Should().Be(AdoConstants.StateWaiting);

        statements.Should().HaveCount(4,
            "the job's fired rows are read, the reservation's row is deleted, and the trigger is moved from ACQUIRED and from BLOCKED: "
            + string.Join(" | ", statements));
        statements.Should().NotContain(Sql(StdAdoConstants.SqlUpdateJobTriggerStatesFromOtherState),
            "a reservation that never fired held nothing of its job back, so there is nothing of the job's to let go of");
    }

    /// <summary>
    /// A job that allows concurrent execution has nothing of the job's to let go of when its fire is
    /// released: the fire held none of the job's other triggers back. The release leaves the job's other
    /// firing and its trigger alone, and makes no statement about the job as a whole.
    /// </summary>
    /// <remarks>
    /// On <c>main</c> the other trigger is held <c>BLOCKED</c> by its own overlap policy while its firing
    /// runs; 3.x has no overlap policy, so what the release must not touch is shown by what it asks of the
    /// database.
    /// </remarks>
    [Test]
    public async Task ReleasingAFireThatCommittedForAJobThatAllowsConcurrencyLeavesTheJobsOtherFiringAlone()
    {
        IJobDetail job = JobBuilder.Create<ConcurrentJobUnderTest>().WithIdentity("concurrent", Group).StoreDurably().Build();
        IOperableTrigger other = Hourly("other", job, now);
        IOperableTrigger due = OneShot("due", job, now);
        await Schedule(job, other, due);

        IReadOnlyCollection<IOperableTrigger> acquired = await store.AcquireNextTriggers(now.AddSeconds(1), 2, TimeSpan.Zero);
        acquired.Select(x => x.Key).Should().BeEquivalentTo(new[] { other.Key, due.Key }, "the premise: both are due, and the job allows both to run");

        IReadOnlyCollection<TriggerFiredResult> fired = await store.TriggersFired(acquired);
        fired.Should().AllSatisfy(x => x.TriggerFiredBundle.Should().NotBeNull());

        store.Recorder.Start();
        await store.ReleaseAcquiredTrigger(acquired.Single(x => x.Key.Equals(due.Key)));
        List<string> statements = store.Recorder.Stop();

        statements.Should().NotContain(Sql(StdAdoConstants.SqlUpdateJobTriggerStatesFromOtherState),
            "the fire held nothing of a job that allows concurrent execution back, so there is nothing of the job's to let go of");
        (await FiredEntries()).Should().ContainSingle("the other trigger's firing is left alone");
        (await StoredState(other.Key)).Should().Be(AdoConstants.StateWaiting, "the other trigger stays where its own fire put it");
    }

    private static IJobDetail SerialJob()
    {
        return JobBuilder.Create<SerialJobUnderTest>().WithIdentity("serial", Group).StoreDurably().Build();
    }

    private async Task Schedule(IJobDetail job, params IOperableTrigger[] triggers)
    {
        await store.StoreJob(job, replaceExisting: true);
        foreach (IOperableTrigger trigger in triggers)
        {
            await store.StoreTrigger(trigger, replaceExisting: true);
        }
    }

    /// <summary>Acquires the next trigger due, as the scheduler thread does, and says it is the expected one.</summary>
    private async Task<IOperableTrigger> AcquireOne(TriggerKey expected)
    {
        IReadOnlyCollection<IOperableTrigger> acquired = await store.AcquireNextTriggers(now.AddSeconds(1), 1, TimeSpan.Zero);

        IOperableTrigger trigger = acquired.Should().ContainSingle().Subject;
        trigger.Key.Should().Be(expected, "the premise: the trigger under test is the one acquired");
        return trigger;
    }

    /// <summary>Fires an acquired trigger with a commit that succeeds, and returns its bundle.</summary>
    private async Task<TriggerFiredBundle> FireCommitted(IOperableTrigger acquired)
    {
        IReadOnlyCollection<TriggerFiredResult> fired = await store.TriggersFired(new[] { acquired });
        TriggerFiredBundle bundle = fired.Should().ContainSingle().Subject.TriggerFiredBundle;
        bundle.Should().NotBeNull("the premise: the fire committed");
        return bundle;
    }

    /// <summary>
    /// Writes the fired row a firing of the job on another node leaves while it runs, as that node's fire
    /// would have written it.
    /// </summary>
    private async Task InsertExecutingFiringOnAnotherNode(string entryId, TriggerKey trigger, JobKey job)
    {
        using SqliteConnection connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO QRTZ_FIRED_TRIGGERS (SCHED_NAME, ENTRY_ID, TRIGGER_NAME, TRIGGER_GROUP, INSTANCE_NAME, FIRED_TIME, SCHED_TIME, PRIORITY, STATE, JOB_NAME, JOB_GROUP, IS_NONCONCURRENT, REQUESTS_RECOVERY) "
            + "VALUES (@scheduler, @entry, @name, @group, 'node-b', @time, @time, 5, 'EXECUTING', @jobName, @jobGroup, 1, 0)";
        command.Parameters.AddWithValue("@scheduler", SchedulerName);
        command.Parameters.AddWithValue("@entry", entryId);
        command.Parameters.AddWithValue("@name", trigger.Name);
        command.Parameters.AddWithValue("@group", trigger.Group);
        command.Parameters.AddWithValue("@time", now.UtcTicks);
        command.Parameters.AddWithValue("@jobName", job.Name);
        command.Parameters.AddWithValue("@jobGroup", job.Group);
        await command.ExecuteNonQueryAsync();
    }

    private static IOperableTrigger OneShot(string name, IJobDetail job, DateTimeOffset startAt, int misfireInstruction = MisfireInstruction.SmartPolicy)
    {
        IOperableTrigger trigger = Build(TriggerBuilder.Create()
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(startAt)
            .WithSimpleSchedule(x => x.WithRepeatCount(0)));

        trigger.MisfireInstruction = misfireInstruction;
        return trigger;
    }

    private static IOperableTrigger Hourly(string name, IJobDetail job, DateTimeOffset startAt)
    {
        return Build(TriggerBuilder.Create()
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(startAt)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever()));
    }

    private static IOperableTrigger Build(TriggerBuilder builder)
    {
        IOperableTrigger trigger = (IOperableTrigger) builder.Build();

        // What the scheduler does with a trigger before handing it to the store.
        trigger.ComputeFirstFireTimeUtc(null);
        return trigger;
    }

    private async Task<string> StoredState(TriggerKey key)
    {
        using SqliteConnection connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name AND TRIGGER_GROUP = @group";
        command.Parameters.AddWithValue("@name", key.Name);
        command.Parameters.AddWithValue("@group", key.Group);
        return (string) await command.ExecuteScalarAsync();
    }

    private async Task<List<string>> FiredEntries()
    {
        using SqliteConnection connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ENTRY_ID FROM QRTZ_FIRED_TRIGGERS";

        List<string> entries = new List<string>();
        using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(reader.GetString(0));
        }

        return entries;
    }

    private static string Sql(string constant) => AdoJobStoreUtil.ReplaceTablePrefix(constant, "QRTZ_");

    private static void InstallSchemaFromFreshInstallScript(string connectionString)
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

    /// <summary>The shipped store, handing out the delegate it made so the test can read what it recorded.</summary>
    private sealed class RecordingJobStore : JobStoreTX
    {
        public RecordingSqliteDelegate Recorder => (RecordingSqliteDelegate) Delegate;
    }

    /// <summary>The shipped SQLite delegate, recording each statement it prepares between a start and a stop.</summary>
    public sealed class RecordingSqliteDelegate : SQLiteDelegate
    {
        private readonly object gate = new object();
        private List<string> statements;

        public void Start()
        {
            lock (gate)
            {
                statements = new List<string>();
            }
        }

        public List<string> Stop()
        {
            lock (gate)
            {
                List<string> recorded = statements;
                statements = null;
                return recorded;
            }
        }

        public override DbCommand PrepareCommand(ConnectionAndTransactionHolder cth, string commandText)
        {
            lock (gate)
            {
                statements?.Add(commandText);
            }

            return base.PrepareCommand(cth, commandText);
        }
    }

    /// <summary>Never executed: these tests drive the store, not a scheduler.</summary>
    [DisallowConcurrentExecution]
    public sealed class SerialJobUnderTest : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }

    /// <summary>Never executed: these tests drive the store, not a scheduler.</summary>
    public sealed class ConcurrentJobUnderTest : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
