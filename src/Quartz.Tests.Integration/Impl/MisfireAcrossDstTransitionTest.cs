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

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Triggers;
using Quartz.Tests.Unit;

namespace Quartz.Tests.Integration.Impl;

/// <summary>
/// One misfire sweep, over a trigger whose missed fire time is on the far side of a daylight saving
/// transition from the clock that sweeps it up - through a real database, and through the in-memory
/// store beside it.
/// </summary>
/// <remarks>
/// <para>
/// <c>TriggerDstMisfireTests</c> asks a trigger what <c>UpdateAfterMisfire</c> computes. These ask
/// what a store <em>writes</em>: which trigger the sweep selects, what is left behind afterwards -
/// in <c>NEXT_FIRE_TIME</c> for the database - and whether that value is a wall clock the zone
/// actually has.
/// </para>
/// <para>
/// Both directions are set up the same way: the trigger's next fire time is written by hand, because
/// <c>ComputeFirstFireTimeUtc</c> advances a past-due first fire to the trigger's own "now" and a
/// fixture that leaves it to compute does not get the overdue trigger it thinks it does. The clock
/// then moves past the misfire threshold, and exactly one sweep runs.
/// </para>
/// <para>
/// SQLite runs on a file, so this needs no container and carries no <c>db-*</c> category. The store
/// is initialized and deliberately never started: <c>SchedulerStarted()</c> spawns the misfire
/// handler, whose own sweep would race the one the test runs by hand.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class MisfireAcrossDstTransitionTest
{
    private const string TablePrefix = "QRTZ_";
    private const string DataSourceName = "misfire-dst-sqlite";
    private const string SchedulerName = "MisfireAcrossDstTransitionTest";
    private const string Group = "dst-misfire";

    /// <summary>Anything overdue by more than five minutes has misfired.</summary>
    private static readonly TimeSpan MisfireThreshold = TimeSpan.FromMinutes(5);

    private string databaseFile;
    private IDbProvider dbProvider;
    private RecoverableJobStore adoJobStore;

    [SetUp]
    public async Task CreateDatabase()
    {
        databaseFile = $"dst-misfire-{Guid.NewGuid():N}.db";

        await using (SqliteConnection connection = new SqliteConnection(ConnectionString))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = new SqliteCommand(LoadTableScript(), connection);
            await command.ExecuteNonQueryAsync();
        }

        dbProvider = new DbProvider("SQLite-Microsoft", ConnectionString);
    }

    [TearDown]
    public async Task DropDatabase()
    {
        if (adoJobStore is not null)
        {
            await adoJobStore.Shutdown();
            adoJobStore = null;
        }

        SqliteConnection.ClearAllPools();

        if (databaseFile is not null && File.Exists(databaseFile))
        {
            try
            {
                File.Delete(databaseFile);
            }
            catch (IOException)
            {
                // scratch space; leaving one behind is not worth failing a passing test over
            }
        }

        databaseFile = null;
    }

    /// <summary>
    /// The in-memory store keeps the trigger object it was handed, clock and all, so a sweep there
    /// computes against the store's own "now" and the expected fire times can be stated outright.
    /// </summary>
    /// <remarks>
    /// The fall-back rows are the ones worth reading twice: a fire missed at 03:30 +03:00 is
    /// recovered onto 03:30 +02:00, the second occurrence of the very same wall clock, an hour of
    /// elapsed time later.
    /// </remarks>
    [TestCase("SpringForward", MisfireInstruction.CronTrigger.FireOnceNow, "2024-03-31 04:15 +03:00")]
    [TestCase("SpringForward", MisfireInstruction.CronTrigger.DoNothing, "2024-03-31 04:30 +03:00")]
    [TestCase("FallBack", MisfireInstruction.CronTrigger.FireOnceNow, "2024-10-27 03:15 +02:00")]
    [TestCase("FallBack", MisfireInstruction.CronTrigger.DoNothing, "2024-10-27 03:30 +02:00")]
    public async Task TheInMemoryStoreRecoversAgainstItsOwnClock(string direction, int misfireInstruction, string expectedNextFireTime)
    {
        TimeZoneInfo zone = TestTimeZones.Helsinki;
        DstMisfire scenario = ResolveScenario(direction, zone);

        FakeTimeProvider clock = new FakeTimeProvider(scenario.MissedFireTime);

        Quartz.Impl.RAMJobStore store = TestJobStores.Ram(timeProvider: clock);
        store.MisfireThreshold = MisfireThreshold;
        await store.Initialize(TestJobStores.Identity());

        TriggerKey triggerKey = new TriggerKey("hourly-half-past", Group);
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("recovered", Group).Build();

        await store.ScheduleJob(job, CreateTrigger(triggerKey, job.Key, zone, clock, misfireInstruction, scenario.MissedFireTime));

        // Pausing and resuming is the store's own documented way of applying a misfire instruction by
        // hand: the in-memory store has no sweep of its own, it reappraises a trigger when it is
        // resumed or acquired.
        await store.PauseTrigger(triggerKey);
        clock.SetUtcNow(scenario.SweepAt);
        await store.ResumeTrigger(triggerKey);

        IOperableTrigger recovered = await store.GetTrigger(triggerKey);

        recovered.NextFireTimeUtc.Should().Be(TestTimeZones.Local(expectedNextFireTime),
            "the store's clock says {0:O}, so the misfire policy has to resolve against that instant and not against any other",
            scenario.SweepAt);

        AssertRecoveredTimeIsSane(recovered.NextFireTimeUtc, scenario, zone);

        recovered.NextFireTimeUtc.Should().Be(ExpectedByTriggerArithmetic(triggerKey, job.Key, zone, clock, misfireInstruction, scenario),
            "what the store leaves behind is what the trigger's own misfire policy computes - the store applies it, it does not invent one");
    }

    /// <summary>
    /// The same sweep against a database: one pass, the trigger back in waiting, and the value that
    /// survives a restart written into <c>NEXT_FIRE_TIME</c> rather than only onto an object.
    /// </summary>
    /// <remarks>
    /// The expected instants are the in-memory twin's, cell for cell. They can be: the trigger the
    /// store rebuilds out of the row is handed the store's clock, so the two stores' misfire
    /// arithmetic is the same arithmetic. Before that they were not — the rebuilt trigger read
    /// <c>TimeProvider.System</c> and recovery landed on whenever the test happened to run.
    /// </remarks>
    [TestCase("SpringForward", MisfireInstruction.CronTrigger.FireOnceNow, "2024-03-31 04:15 +03:00")]
    [TestCase("SpringForward", MisfireInstruction.CronTrigger.DoNothing, "2024-03-31 04:30 +03:00")]
    [TestCase("FallBack", MisfireInstruction.CronTrigger.FireOnceNow, "2024-10-27 03:15 +02:00")]
    [TestCase("FallBack", MisfireInstruction.CronTrigger.DoNothing, "2024-10-27 03:30 +02:00")]
    public async Task OneSweepRecoversTheTriggerAndWritesTheColumn(string direction, int misfireInstruction, string expectedNextFireTime)
    {
        TimeZoneInfo zone = TestTimeZones.Helsinki;
        DstMisfire scenario = ResolveScenario(direction, zone);

        FakeTimeProvider clock = new FakeTimeProvider(scenario.MissedFireTime);

        adoJobStore = new RecoverableJobStore(dbProvider, clock);
        await adoJobStore.Initialize(new SchedulerIdentity { SchedulerName = SchedulerName, InstanceId = "AUTO" });

        TriggerKey triggerKey = new TriggerKey("hourly-half-past", Group);
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("recovered", Group).Build();

        await adoJobStore.ScheduleJob(job, CreateTrigger(triggerKey, job.Key, zone, clock, misfireInstruction, scenario.MissedFireTime));

        // The clock moves; nothing else does. The trigger is now overdue by far more than the
        // threshold, which is what a node that was down over the transition leaves behind.
        clock.SetUtcNow(scenario.SweepAt);

        RecoverMisfiredJobsResult result = await adoJobStore.RecoverMisfires();

        result.ProcessedMisfiredTriggerCount.Should().Be(1,
            "the sweep decides what has misfired from the store's own clock, and at {0:O} a fire time of {1:O} is {2} past due against a {3} threshold",
            scenario.SweepAt, scenario.MissedFireTime, scenario.SweepAt - scenario.MissedFireTime, MisfireThreshold);

        (await adoJobStore.GetTriggerState(triggerKey)).Should().Be(TriggerState.Normal,
            "a recovered trigger goes back to waiting rather than staying misfired");

        IOperableTrigger recovered = await adoJobStore.GetTrigger(triggerKey);

        AssertRecoveredTimeIsSane(recovered.NextFireTimeUtc, scenario, zone);

        // The column, not just the object: NEXT_FIRE_TIME holds UTC ticks, and reading the value back
        // through the store would pass even if the write had gone somewhere else.
        (await ReadNextFireTimeColumn(triggerKey)).Should().Be(recovered.NextFireTimeUtc,
            "NEXT_FIRE_TIME is what a restarted scheduler reads, so it is what recovery has to have written");

        DateTimeOffset next = recovered.NextFireTimeUtc.Value;

        next.Should().Be(TestTimeZones.Local(expectedNextFireTime),
            "the store's clock says {0:O}, and the trigger it rebuilt out of the row holds that same clock, so the "
            + "{1} transition is accounted for exactly once - and to the same instant the in-memory twin lands on",
            scenario.SweepAt, direction);

        next.Should().Be(ExpectedByTriggerArithmetic(triggerKey, job.Key, zone, clock, misfireInstruction, scenario),
            "what recovery writes is what the trigger's own misfire policy computes - the store applies it, it does not invent one");
    }

    /// <summary>
    /// The invariants that hold whichever instruction ran: recovery moves forward, and it lands on a
    /// wall clock the zone really has rather than inside a gap.
    /// </summary>
    private static void AssertRecoveredTimeIsSane(DateTimeOffset? nextFireTimeUtc, DstMisfire scenario, TimeZoneInfo zone)
    {
        nextFireTimeUtc.Should().NotBeNull("the schedule repeats forever, so recovery always leaves a next fire time");

        DateTimeOffset next = nextFireTimeUtc.Value;

        next.Should().BeAfter(scenario.MissedFireTime,
            "a recovered trigger must move forward, never back onto the fire it missed");

        DateTime nextLocal = TimeZoneInfo.ConvertTime(next, zone).DateTime;
        zone.IsInvalidTime(nextLocal).Should().BeFalse(
            "the recovered fire time {0:O} reads as {1:yyyy-MM-dd HH:mm} in {2}, which has to be a wall clock the zone really has",
            next, nextLocal, zone.Id);
    }

    /// <summary>
    /// What the same trigger, holding the same clock the store holds, computes for this misfire.
    /// </summary>
    private static DateTimeOffset ExpectedByTriggerArithmetic(
        TriggerKey triggerKey,
        JobKey jobKey,
        TimeZoneInfo zone,
        TimeProvider clock,
        int misfireInstruction,
        DstMisfire scenario)
    {
        CronTriggerImpl expectation = CreateTrigger(triggerKey, jobKey, zone, clock, misfireInstruction, scenario.MissedFireTime);
        expectation.UpdateAfterMisfire(null);
        return expectation.NextFireTimeUtc.Value;
    }

    /// <summary>
    /// Spring forward: the missed fire is the last one before the transition and the sweep runs after
    /// it, so the wall clock the recovery computes against is an hour further on than the elapsed
    /// time suggests. Fall back: the missed fire is in the first pass of the repeated hour and the
    /// sweep runs during the second, so the sweep's wall clock reads <em>earlier</em> than the fire it
    /// is recovering.
    /// </summary>
    private static DstMisfire ResolveScenario(string direction, TimeZoneInfo zone)
    {
        switch (direction)
        {
            case "SpringForward":
                // 02:30 +02:00 was missed; the sweep runs at 04:15 +03:00, 45 elapsed minutes later.
                TestTimeZones.AssumeInvalidLocalTime(zone, new DateTime(2024, 3, 31, 3, 30, 0));
                return new DstMisfire(
                    new DateTimeOffset(2024, 3, 31, 0, 30, 0, TimeSpan.Zero),
                    new DateTimeOffset(2024, 3, 31, 1, 15, 0, TimeSpan.Zero));

            case "FallBack":
                // 03:30 +03:00 was missed; the sweep runs at 03:15 +02:00, 45 elapsed minutes later
                // and fifteen wall-clock minutes earlier.
                TestTimeZones.AssumeAmbiguousLocalTime(zone, new DateTime(2024, 10, 27, 3, 30, 0));
                return new DstMisfire(
                    new DateTimeOffset(2024, 10, 27, 0, 30, 0, TimeSpan.Zero),
                    new DateTimeOffset(2024, 10, 27, 1, 15, 0, TimeSpan.Zero));

            default:
                throw new ArgumentOutOfRangeException(nameof(direction), direction, "unknown direction");
        }
    }

    private static CronTriggerImpl CreateTrigger(
        TriggerKey triggerKey,
        JobKey jobKey,
        TimeZoneInfo zone,
        TimeProvider clock,
        int misfireInstruction,
        DateTimeOffset missedFireTime)
    {
        // Built by hand rather than through TriggerBuilder: the last step states NextFireTimeUtc,
        // which only the implementation exposes. TriggerBuilder.Create(clock) would hand over the
        // clock just as well, but this doubles as the oracle for what the store rebuilds, and an
        // oracle that goes through the same builder the store does would move with it.
        CronTriggerImpl trigger = new CronTriggerImpl(clock)
        {
            Key = triggerKey,
            JobKey = jobKey,
            CronExpressionString = "0 30 * * * ?",
            TimeZone = zone,
            StartTimeUtc = missedFireTime.AddDays(-1),
            MisfireInstructionCode = misfireInstruction
        };

        trigger.ComputeFirstFireTimeUtc(null);

        // Stated rather than computed, which is the whole setup: this is a trigger nobody got around
        // to firing.
        trigger.NextFireTimeUtc = missedFireTime;

        return trigger;
    }

    private async Task<DateTimeOffset?> ReadNextFireTimeColumn(TriggerKey triggerKey)
    {
        await using SqliteConnection connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = new SqliteCommand(
            $"SELECT NEXT_FIRE_TIME FROM {TablePrefix}TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @name AND TRIGGER_GROUP = @group",
            connection);

        command.Parameters.AddWithValue("@schedulerName", SchedulerName);
        command.Parameters.AddWithValue("@name", triggerKey.Name);
        command.Parameters.AddWithValue("@group", triggerKey.Group);

        object value = await command.ExecuteScalarAsync();

        return value is long ticks ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
    }

    private string ConnectionString => $"Data Source={databaseFile};";

    private static string LoadTableScript()
    {
        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "database", "tables", "tables_sqlite.sql");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate database/tables/tables_sqlite.sql from " + AppContext.BaseDirectory);
    }

    private sealed record DstMisfire(DateTimeOffset MissedFireTime, DateTimeOffset SweepAt);

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// The store with its misfire sweep reachable, so that the test runs exactly one pass rather than
    /// waiting for a handler thread to run one.
    /// </summary>
    private sealed class RecoverableJobStore : LocalTransactionJobStore
    {
        public RecoverableJobStore(IDbProvider dbProvider, TimeProvider timeProvider)
            : base(
                TestJobStores.Signaler(),
                TestJobStores.TypeLoader(),
                timeProvider,
                TestJobStores.SchedulerOptions(SchedulerName, "AUTO"),
                TestJobStores.StoreOptions(
                    DataSourceName,
                    MisfireAcrossDstTransitionTest.TablePrefix,
                    options => options.MisfireThreshold = MisfireAcrossDstTransitionTest.MisfireThreshold),
                TestJobStores.ClusteringOptions(),
                TestJobStores.Serializer(),
                dbProvider,
                new SQLiteDelegate(),
                TestJobStores.LockHandler())
        {
        }

        public ValueTask<RecoverMisfiredJobsResult> RecoverMisfires()
            => DoRecoverMisfires(Guid.NewGuid(), CancellationToken.None);
    }
}
