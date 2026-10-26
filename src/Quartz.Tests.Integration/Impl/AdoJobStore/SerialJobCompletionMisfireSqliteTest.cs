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
/// A <see cref="DisallowConcurrentExecutionAttribute" /> job's completion lets go of the job's other
/// triggers and handles the misfires they accrued while they were blocked (#1646). It reads only the ones
/// that misfired, so what a completion costs does not grow with the job's backlog (#4167).
/// </summary>
/// <remarks>
/// <para>
/// The completion used to load every trigger of the job, read each one's state, and load each waiting
/// one again to decide whether it had misfired, all under <c>TRIGGER_ACCESS</c>: a serial job with a
/// backlog of a few hundred triggers held the lock for tens of seconds per completion.
/// </para>
/// <para>
/// One <see cref="JobStoreTX" /> on a SQLite file, initialized and never started, driven by hand on a
/// clock the test sets, with the shipped SQLite delegate counting what the completion asks of it.
/// </para>
/// </remarks>
[NonParallelizable]
[Category("db-sqlite")]
public sealed class SerialJobCompletionMisfireSqliteTest
{
    private const string SchedulerName = "serial-job-completion-misfire";
    private const string Group = "serial-completion";

    /// <summary>On the hour, UTC, far from any machine's own clock.</summary>
    private static readonly DateTimeOffset Epoch = new DateTimeOffset(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    /// <summary><c>JobStoreSupport.MisfireThreshold</c> as it ships.</summary>
    private static readonly TimeSpan MisfireThreshold = TimeSpan.FromMinutes(1);

    private static readonly JobKey SerialJobKey = new JobKey("serial", Group);

    private static readonly TriggerKey FirstKey = new TriggerKey("first", Group);
    private static readonly TriggerKey FireNowKey = new TriggerKey("late-fire-now", Group);
    private static readonly TriggerKey OneShotKey = new TriggerKey("late-one-shot", Group);
    private static readonly TriggerKey WithinThresholdKey = new TriggerKey("due-within-threshold", Group);
    private static readonly TriggerKey PausedKey = new TriggerKey("late-paused", Group);
    private static readonly TriggerKey IgnoreKey = new TriggerKey("late-ignore-policy", Group);

    private Func<DateTimeOffset> originalUtcNow;
    private DateTimeOffset now;
    private readonly List<Node> nodes = new List<Node>();

    [SetUp]
    public void SetClock()
    {
        originalUtcNow = SystemTime.UtcNow;
        now = Epoch;
        SystemTime.UtcNow = () => now;
        CountingSqliteDelegate.Reset();
    }

    [TearDown]
    public async Task DisposeNodes()
    {
        foreach (Node node in nodes)
        {
            await node.DisposeAsync();
        }

        nodes.Clear();
        SystemTime.UtcNow = originalUtcNow;
        SqliteConnection.ClearAllPools();
    }

    /// <summary>
    /// The regression #4167 reported: with nothing misfired, a completion that lets go of 200 triggers
    /// costs what one that lets go of 10 does, and loads none of them.
    /// </summary>
    [Test]
    public async Task ACompletionCostsTheSameHoweverManyTriggersItLetsGoOf()
    {
        CompletionCost ten = await MeasureCompletion(waitingTriggers: 10);
        CompletionCost twoHundred = await MeasureCompletion(waitingTriggers: 200);

        TestContext.Out.WriteLine($"10 waiting: {ten}");
        TestContext.Out.WriteLine($"200 waiting: {twoHundred}");

        twoHundred.Statements.Should().Be(ten.Statements,
            "only the job's misfired triggers are read, and none has misfired, so the backlog's length is not a cost");
        twoHundred.TriggerLoads.Should().Be(0, "a trigger that has not misfired has nothing to load it for");
        twoHundred.TriggerStateReads.Should().Be(0, "the state is part of the query, not a read per trigger");
        twoHundred.JobTriggerReads.Should().Be(0, "the job's triggers are not listed in order to find the few that misfired");
    }

    /// <summary>
    /// What the completion does to each kind of trigger it lets go of. Only a trigger the misfire handler
    /// would take is handled: one that is late by more than the threshold and does not ignore misfires.
    /// </summary>
    [Test]
    public async Task ACompletionHandlesTheMisfiresOfTheTriggersItLetsGoOfAndNothingElse()
    {
        Node node = await CreateNode();
        await node.StoreSerialJob();

        await node.Schedule(FirstKey, Epoch, priority: 10, repeatHourly: true);
        await node.Schedule(FireNowKey, Epoch, misfireInstruction: MisfireInstruction.SimpleTrigger.FireNow);
        await node.Schedule(OneShotKey, Epoch, misfireInstruction: MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount);
        await node.Schedule(WithinThresholdKey, Epoch.AddMinutes(30).Subtract(MisfireThreshold).AddSeconds(30), repeatHourly: true);
        await node.Schedule(PausedKey, Epoch, repeatHourly: true);
        await node.Schedule(IgnoreKey, Epoch, repeatHourly: true, misfireInstruction: MisfireInstruction.IgnoreMisfirePolicy);

        TriggerFiredBundle firing = await node.AcquireAndFireOne(FirstKey);
        (await node.StateOf(FireNowKey)).Should().Be("BLOCKED", "the premise: firing the serial job blocks its other triggers");
        await node.Store.PauseTrigger(PausedKey);
        (await node.StateOf(PausedKey)).Should().Be("PAUSED_BLOCKED");

        // The job runs half an hour, past every other trigger's fire time.
        now = now.AddMinutes(30);
        await node.Store.TriggeredJobComplete(firing.Trigger, firing.JobDetail, SchedulerInstruction.NoInstruction);

        (await node.StateOf(FireNowKey)).Should().Be("WAITING");
        (await node.Store.RetrieveTrigger(FireNowKey)).GetNextFireTimeUtc().Should().Be(now, "its fire-now policy was applied");
        A.CallTo(() => node.Signaler.NotifyTriggerListenersMisfired(A<ITrigger>.That.Matches(x => x.Key.Equals(FireNowKey)), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        (await node.Store.RetrieveTrigger(OneShotKey)).Should().BeNull(
            "its policy left it no fire time, and a trigger stored COMPLETE that way is deleted rather than left behind (#1646)");
        A.CallTo(() => node.Signaler.NotifyTriggerListenersMisfired(A<ITrigger>.That.Matches(x => x.Key.Equals(OneShotKey)), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        (await node.StateOf(WithinThresholdKey)).Should().Be("WAITING");
        (await node.Store.RetrieveTrigger(WithinThresholdKey)).GetNextFireTimeUtc().Should().Be(Epoch.AddMinutes(30).Subtract(MisfireThreshold).AddSeconds(30),
            "it is due, but by less than the misfire threshold, so acquisition fires it as it is");

        (await node.StateOf(PausedKey)).Should().Be("PAUSED", "let go of, and still paused");
        (await node.Store.RetrieveTrigger(PausedKey)).GetNextFireTimeUtc().Should().Be(Epoch,
            "a paused trigger accrues no misfire: resuming it is what settles the debt");

        (await node.StateOf(IgnoreKey)).Should().Be("WAITING");
        (await node.Store.RetrieveTrigger(IgnoreKey)).GetNextFireTimeUtc().Should().Be(Epoch,
            "a trigger that ignores misfires is fired late as it is, as the misfire handler and RAMJobStore leave it");

        foreach (TriggerKey untouched in new[] { WithinThresholdKey, PausedKey, IgnoreKey })
        {
            A.CallTo(() => node.Signaler.NotifyTriggerListenersMisfired(A<ITrigger>.That.Matches(x => x.Key.Equals(untouched)), A<CancellationToken>._))
                .MustNotHaveHappened();
        }
    }

    /// <summary>
    /// A serial job with <paramref name="waitingTriggers" /> triggers due behind the one that fires, all
    /// inside the misfire threshold when the firing completes - the shape of a backlog <c>TriggerJob</c>
    /// builds up. Answers what that one completion asked of the delegate.
    /// </summary>
    private async Task<CompletionCost> MeasureCompletion(int waitingTriggers)
    {
        now = Epoch;
        Node node = await CreateNode();
        await node.StoreSerialJob();

        await node.Schedule(FirstKey, Epoch, priority: 10);
        for (int i = 1; i <= waitingTriggers; i++)
        {
            await node.Schedule(new TriggerKey($"waiting-{i}", Group), Epoch.AddMilliseconds(i));
        }

        TriggerFiredBundle firing = await node.AcquireAndFireOne(FirstKey);
        (await node.StateOf(new TriggerKey($"waiting-{waitingTriggers}", Group))).Should().Be("BLOCKED");

        now = now.Add(MisfireThreshold).Subtract(TimeSpan.FromSeconds(10));

        CountingSqliteDelegate.Start();
        try
        {
            // The instruction a one-shot trigger's firing completes with.
            await node.Store.TriggeredJobComplete(firing.Trigger, firing.JobDetail, SchedulerInstruction.DeleteTrigger);
        }
        finally
        {
            CountingSqliteDelegate.Stop();
        }

        (await node.StateOf(new TriggerKey($"waiting-{waitingTriggers}", Group))).Should().Be("WAITING", "the completion lets go of the job's triggers");
        return CountingSqliteDelegate.Snapshot();
    }

    private async Task<Node> CreateNode()
    {
        Node node = await Node.Create();
        nodes.Add(node);
        return node;
    }

    /// <summary>What one completion asked of the delegate.</summary>
    internal readonly struct CompletionCost
    {
        public CompletionCost(int statements, int triggerLoads, int triggerStateReads, int jobTriggerReads)
        {
            Statements = statements;
            TriggerLoads = triggerLoads;
            TriggerStateReads = triggerStateReads;
            JobTriggerReads = jobTriggerReads;
        }

        /// <summary>Every statement the delegate prepared.</summary>
        public int Statements { get; }

        /// <summary><c>SelectTrigger</c> calls.</summary>
        public int TriggerLoads { get; }

        /// <summary><c>SelectTriggerState</c> calls.</summary>
        public int TriggerStateReads { get; }

        /// <summary><c>SelectTriggersForJob</c> and <c>SelectTriggerNamesForJob</c> calls.</summary>
        public int JobTriggerReads { get; }

        public override string ToString()
        {
            return $"{Statements} statements, {TriggerLoads} SelectTrigger, {TriggerStateReads} SelectTriggerState, {JobTriggerReads} job trigger list reads";
        }
    }

    /// <summary>One store on its own SQLite file.</summary>
    private sealed class Node
    {
        private readonly string databaseFile;
        private readonly string connectionString;

        private Node(string databaseFile, string connectionString, JobStoreTX store, ISchedulerSignaler signaler)
        {
            this.databaseFile = databaseFile;
            this.connectionString = connectionString;
            Store = store;
            Signaler = signaler;
        }

        public JobStoreTX Store { get; }

        public ISchedulerSignaler Signaler { get; }

        public static async Task<Node> Create()
        {
            string databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-serial-completion-{Guid.NewGuid():N}.db");
            string connectionString = $"Data Source={databaseFile}";
            InstallSchemaFromFreshInstallScript(connectionString);

            string dataSource = $"serial-completion-{Guid.NewGuid():N}";
            DBConnectionManager.Instance.AddConnectionProvider(dataSource, new DbProvider("SQLite-Microsoft", connectionString));

            SystemTextJsonObjectSerializer serializer = new SystemTextJsonObjectSerializer();
            serializer.Initialize();

            JobStoreTX store = new JobStoreTX
            {
                DataSource = dataSource,
                TablePrefix = "QRTZ_",
                InstanceName = SchedulerName,
                InstanceId = "node",
                DriverDelegateType = typeof(CountingSqliteDelegate).AssemblyQualifiedName,
                ObjectSerializer = serializer,
            };

            // Initialized and never started: nothing here is left to a scheduler thread, a misfire pass or
            // a startup recovery, each of which would move the rows these tests read.
            ISchedulerSignaler signaler = A.Fake<ISchedulerSignaler>();
            await store.Initialize(new SimpleTypeLoadHelper(), signaler);
            return new Node(databaseFile, connectionString, store, signaler);
        }

        public async Task DisposeAsync()
        {
            await Store.Shutdown();
            SqliteConnection.ClearAllPools();
            if (File.Exists(databaseFile))
            {
                File.Delete(databaseFile);
            }
        }

        public Task StoreSerialJob()
        {
            return Store.StoreJob(JobBuilder.Create<SerialJob>().WithIdentity(SerialJobKey).StoreDurably().Build(), replaceExisting: true);
        }

        /// <summary>
        /// A simple trigger of the serial job, once at <paramref name="startAt" /> or hourly from it.
        /// </summary>
        public async Task Schedule(
            TriggerKey key,
            DateTimeOffset startAt,
            int priority = TriggerConstants.DefaultPriority,
            bool repeatHourly = false,
            int misfireInstruction = MisfireInstruction.SmartPolicy)
        {
            IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
                .WithIdentity(key)
                .ForJob(SerialJobKey)
                .StartAt(startAt)
                .WithSimpleSchedule(x =>
                {
                    if (repeatHourly)
                    {
                        x.WithInterval(TimeSpan.FromHours(1)).RepeatForever();
                    }
                })
                .WithPriority(priority)
                .Build();
            trigger.MisfireInstruction = misfireInstruction;

            // What the scheduler does with a trigger before handing it to the store.
            trigger.ComputeFirstFireTimeUtc(null);
            await Store.StoreTrigger(trigger, replaceExisting: false);
        }

        /// <summary>
        /// Acquires the serial job's next trigger, which has to be <paramref name="expected" />, and fires it.
        /// </summary>
        public async Task<TriggerFiredBundle> AcquireAndFireOne(TriggerKey expected)
        {
            IReadOnlyCollection<IOperableTrigger> acquired = await Store.AcquireNextTriggers(SystemTime.UtcNow().AddSeconds(1), 10, TimeSpan.Zero);
            acquired.Select(x => x.Key).Should().Equal(new[] { expected }, "a batch takes one trigger of a serial job, the first in fire-time and priority order");

            TriggerFiredResult fired = (await Store.TriggersFired(acquired)).Single();
            fired.TriggerFiredBundle.Should().NotBeNull("the premise: the trigger was this node's to fire");
            return fired.TriggerFiredBundle;
        }

        public async Task<string> StateOf(TriggerKey key)
        {
            using SqliteConnection connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name AND TRIGGER_GROUP = @group";
            command.Parameters.AddWithValue("@name", key.Name);
            command.Parameters.AddWithValue("@group", key.Group);
            return (string) await command.ExecuteScalarAsync();
        }

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
    }

    /// <summary>
    /// The shipped SQLite delegate, counting what it is asked while <see cref="Start" /> is in effect.
    /// </summary>
    /// <remarks>
    /// Static, because the store builds the delegate from its type name. Statements are counted where
    /// the delegate prepares them, which the trigger persistence delegates share.
    /// </remarks>
    public sealed class CountingSqliteDelegate : SQLiteDelegate
    {
        private static volatile bool recording;
        private static int statements;
        private static int triggerLoads;
        private static int triggerStateReads;
        private static int jobTriggerReads;

        public static void Reset()
        {
            recording = false;
            statements = 0;
            triggerLoads = 0;
            triggerStateReads = 0;
            jobTriggerReads = 0;
        }

        public static void Start()
        {
            Reset();
            recording = true;
        }

        public static void Stop() => recording = false;

        internal static CompletionCost Snapshot()
        {
            return new CompletionCost(
                Volatile.Read(ref statements),
                Volatile.Read(ref triggerLoads),
                Volatile.Read(ref triggerStateReads),
                Volatile.Read(ref jobTriggerReads));
        }

        public override DbCommand PrepareCommand(ConnectionAndTransactionHolder cth, string commandText)
        {
            Count(ref statements);
            return base.PrepareCommand(cth, commandText);
        }

        public override Task<IOperableTrigger> SelectTrigger(
            ConnectionAndTransactionHolder conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            Count(ref triggerLoads);
            return base.SelectTrigger(conn, triggerKey, cancellationToken);
        }

        public override Task<string> SelectTriggerState(
            ConnectionAndTransactionHolder conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            Count(ref triggerStateReads);
            return base.SelectTriggerState(conn, triggerKey, cancellationToken);
        }

        public override Task<IReadOnlyCollection<IOperableTrigger>> SelectTriggersForJob(
            ConnectionAndTransactionHolder conn,
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            Count(ref jobTriggerReads);
            return base.SelectTriggersForJob(conn, jobKey, cancellationToken);
        }

        public override Task<IReadOnlyCollection<TriggerKey>> SelectTriggerNamesForJob(
            ConnectionAndTransactionHolder conn,
            JobKey jobKey,
            CancellationToken cancellationToken = default)
        {
            Count(ref jobTriggerReads);
            return base.SelectTriggerNamesForJob(conn, jobKey, cancellationToken);
        }

        private static void Count(ref int counter)
        {
            if (recording)
            {
                Interlocked.Increment(ref counter);
            }
        }
    }

    [DisallowConcurrentExecution]
    public sealed class SerialJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
