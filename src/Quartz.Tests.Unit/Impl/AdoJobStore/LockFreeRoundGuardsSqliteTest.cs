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

using System.Data.Common;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Calendar;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The writes that guard a firing round taking no <c>TRIGGER_ACCESS</c> lock (#4067), run against real
/// rows: a misfire write, an edit and a calendar update each read a trigger's row and write it back, and
/// each is held to the row as read.
/// </summary>
/// <remarks>
/// SQLite never fires lock-free — it serializes every operation — so the race itself is
/// <c>LockFreeRoundRacesPostgresTest</c>'s to show. What is shown here is the guard end to end, on the
/// shipped statements: a seam in the SQLite delegate moves the row in the writer's own transaction
/// between its read and its write, which is the state a round's committed fire leaves, and the writer
/// behaves as the guard says it must.
/// </remarks>
public sealed class LockFreeRoundGuardsSqliteTest
{
    private const string Group = "guards";
    private const string SchedulerName = "LockFreeRoundGuardsSqliteTest";
    private const string CalendarName = "cal";

    /// <summary>Log event <c>MisfireLog.MisfireWriteSkipped</c>.</summary>
    private const int MisfireWriteSkipped = 3608;

    /// <summary>Log event <c>AdoJobStoreLog.TriggerEditReapplied</c>.</summary>
    private const int TriggerEditReapplied = 3059;

    /// <summary>On the hour, UTC, far from any machine's own clock.</summary>
    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    private SqliteTestDatabase database = null!;
    private IDbProvider dbProvider = null!;
    private FakeTimeProvider clock = null!;
    private RecordingSignaler signals = null!;
    private MovingSqliteDelegate seam = null!;
    private RecordingLoggerProvider logs = null!;
    private ILoggerFactory loggerFactory = null!;
    private LocalTransactionJobStore store = null!;

    [OneTimeSetUp]
    public async Task CreateDatabase()
    {
        database = new SqliteTestDatabase("lock-free-guards");

        await using (SqliteConnection connection = new(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = new(LoadSqliteTableScript(), connection);
            await command.ExecuteNonQueryAsync();
        }

        dbProvider = new DbProvider("SQLite-Microsoft", database.ConnectionString);
    }

    [OneTimeTearDown]
    public void DropDatabase()
    {
        database.Dispose();
    }

    [SetUp]
    public async Task BuildStore()
    {
        clock = new FakeTimeProvider(epoch);
        signals = new RecordingSignaler();
        seam = new MovingSqliteDelegate();
        logs = new RecordingLoggerProvider();
        loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));

        store = new LocalTransactionJobStore(TestJobStores.Dependencies(
            signaler: signals,
            timeProvider: clock,
            schedulerOptions: TestJobStores.SchedulerOptions(instanceName: SchedulerName, instanceId: "node-a"),
            storeOptions: TestJobStores.StoreOptions("lock-free-guards"),
            dbProvider: dbProvider,
            driverDelegate: seam,
            loggerFactory: loggerFactory));

        // Initialized but deliberately not started, so no misfire loop races an assertion.
        await store.Initialize(TestJobStores.Identity(instanceName: SchedulerName, instanceId: "node-a"));
        await store.Clear();
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        await store.Shutdown();
        loggerFactory.Dispose();
        logs.Dispose();
    }

    [Test]
    public async Task AMisfireWriteLandsOnlyOnTheRowThePassRead()
    {
        TriggerKey key = await ScheduleOneOff("late", epoch.AddMinutes(-5));
        DateTimeOffset movedTo = epoch.AddHours(1);
        seam.AfterMisfireRead = conn => MoveFireTime(conn, key, movedTo);

        RecoverMisfiredJobsResult pass = await store.RecoverMisfires(Guid.NewGuid());

        pass.ProcessedMisfiredTriggerCount.Should().Be(1, "the pass looked at the row");
        seam.AfterMisfireRead.Should().BeNull("the seam fired between the pass's read and its write");

        IOperableTrigger? stored = await store.GetTrigger(key);
        stored.Should().NotBeNull();
        stored!.NextFireTimeUtc.Should().Be(movedTo, "the write was held to the fire time the pass read, so the row the fire left is what stays");
        (await store.GetTriggerState(key)).Should().Be(TriggerState.Normal);

        signals.Misfired.Should().BeEmpty("the occurrence the pass read was fired, not missed, so nobody is told it misfired");
        signals.Finalized.Should().BeEmpty();
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == MisfireWriteSkipped);
    }

    [Test]
    public async Task AMisfireWriteOnTheRowAsReadAppliesAndIsReportedAfterTheCommit()
    {
        TriggerKey key = await ScheduleOneOff("late", epoch.AddMinutes(-5));

        RecoverMisfiredJobsResult pass = await store.RecoverMisfires(Guid.NewGuid());

        pass.ProcessedMisfiredTriggerCount.Should().Be(1);
        IOperableTrigger? stored = await store.GetTrigger(key);
        stored!.NextFireTimeUtc.Should().Be(epoch, "the one-off's smart policy fires it now");

        signals.Misfired.Should().ContainSingle().Which.Should().Match<ITrigger>(
            t => t.Key.Equals(key) && t.NextFireTimeUtc == epoch.AddMinutes(-5),
            "the listeners are told about the trigger as it was before the policy, once the write has committed");
        logs.Entries.Should().NotContain(x => x.EventId.Id == MisfireWriteSkipped);
    }

    [Test]
    public async Task AnEditOfARowAFiringMovedIsAppliedToTheRowTheFiringLeft()
    {
        TriggerKey key = await ScheduleRepeating("edited", epoch.AddHours(1));
        DateTimeOffset movedTo = epoch.AddHours(2);
        seam.AfterTriggerRead = conn => MoveFireTime(conn, key, movedTo);

        bool updated = await store.UpdateTriggerDetails(key, new TriggerDetailsUpdate().WithDescription("edited").WithPriority(7));

        updated.Should().BeTrue();
        seam.AfterTriggerRead.Should().BeNull("the seam fired between the edit's read and its write");

        IOperableTrigger? stored = await store.GetTrigger(key);
        stored!.Description.Should().Be("edited");
        stored.Priority.Should().Be(7);
        stored.NextFireTimeUtc.Should().Be(movedTo, "the edit was applied to the row as the firing left it, not written back over it");

        logs.Entries.Should().ContainSingle(x => x.EventId.Id == TriggerEditReapplied);
        seam.GuardedWrites.Should().Equal([epoch.AddHours(1), movedTo], "the first write was held to the row as first read and missed; the second was held to the row the firing left and took");
    }

    [Test]
    public async Task ACalendarUpdateOfARowAFiringMovedIsRecomputedFromTheRowTheFiringLeft()
    {
        await store.AddCalendar(CalendarName, new BaseCalendar());
        TriggerKey key = await ScheduleRepeating("on-calendar", epoch.AddHours(1), CalendarName);
        DateTimeOffset movedTo = epoch.AddHours(2);
        seam.AfterCalendarTriggersRead = conn => MoveFireTime(conn, key, movedTo);

        await store.AddCalendar(CalendarName, new BaseCalendar(), new AddCalendarOptions { Replace = true, UpdateTriggers = true });

        seam.AfterCalendarTriggersRead.Should().BeNull("the seam fired between the calendar's read of its triggers and its write");
        seam.GuardedWrites.Should().Equal([epoch.AddHours(1), movedTo], "the recomputation was written held to the row as read, missed, and done again from the row the firing left");
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == TriggerEditReapplied);

        IOperableTrigger? stored = await store.GetTrigger(key);
        stored!.CalendarName.Should().Be(CalendarName);
        (await store.GetTriggerState(key)).Should().Be(TriggerState.Normal);
    }

    private async Task<TriggerKey> ScheduleOneOff(string name, DateTimeOffset at)
    {
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity(name, Group).StoreDurably().Build();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(at)
            .Build();

        trigger.ComputeFirstFireTimeUtc(calendar: null);
        await store.ScheduleJob(job, trigger);
        return trigger.Key;
    }

    private async Task<TriggerKey> ScheduleRepeating(string name, DateTimeOffset at, string? calendarName = null)
    {
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity(name, Group).StoreDurably().Build();
        TriggerBuilder<IJob> builder = TriggerBuilder.Create()
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(at)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever());

        if (calendarName is not null)
        {
            builder = builder.WithCalendarName(calendarName);
        }

        IOperableTrigger trigger = (IOperableTrigger) builder.Build();
        trigger.ComputeFirstFireTimeUtc(calendar: null);
        await store.ScheduleJob(job, trigger);
        return trigger.Key;
    }

    /// <summary>
    /// What a firing round's committed fire leaves: the row's fire time moved on. Written in the writer's
    /// own transaction, since SQLite's single writer admits no second one.
    /// </summary>
    private static async Task MoveFireTime(ConnectionAndTransactionHolder conn, TriggerKey key, DateTimeOffset to)
    {
        await using DbCommand command = conn.Connection.CreateCommand();
        command.Transaction = conn.Transaction;
        command.CommandText = "UPDATE QRTZ_TRIGGERS SET NEXT_FIRE_TIME = @t WHERE SCHED_NAME = @s AND TRIGGER_NAME = @n AND TRIGGER_GROUP = @g";
        Bind(command, "@t", to.UtcTicks);
        Bind(command, "@s", SchedulerName);
        Bind(command, "@n", key.Name);
        Bind(command, "@g", key.Group);

        (await command.ExecuteNonQueryAsync()).Should().Be(1, "the row under test is there to be moved");
    }

    private static void Bind(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string LoadSqliteTableScript()
    {
        string path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        return File.ReadAllText(path);
    }

    /// <summary>
    /// The shipped SQLite delegate with a seam after each of the three reads a guarded writer makes, fired
    /// once, and a record of the fire time each guarded write was held to.
    /// </summary>
    private sealed class MovingSqliteDelegate : SQLiteDelegate
    {
        public Func<ConnectionAndTransactionHolder, Task>? AfterMisfireRead { get; set; }

        public Func<ConnectionAndTransactionHolder, Task>? AfterTriggerRead { get; set; }

        public Func<ConnectionAndTransactionHolder, Task>? AfterCalendarTriggersRead { get; set; }

        public List<DateTimeOffset?> GuardedWrites { get; } = [];

        public override async ValueTask<MisfiredTriggerBatch> SelectMisfiredTriggersToRecover(
            ConnectionAndTransactionHolder conn,
            StoredTriggerState state,
            DateTimeOffset misfireTime,
            int count,
            CancellationToken cancellationToken = default)
        {
            MisfiredTriggerBatch batch = await base.SelectMisfiredTriggersToRecover(conn, state, misfireTime, count, cancellationToken);
            await Fire(conn, () => AfterMisfireRead, () => AfterMisfireRead = null);
            return batch;
        }

        public override async ValueTask<IOperableTrigger?> SelectTrigger(
            ConnectionAndTransactionHolder conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            IOperableTrigger? trigger = await base.SelectTrigger(conn, triggerKey, cancellationToken);
            await Fire(conn, () => AfterTriggerRead, () => AfterTriggerRead = null);
            return trigger;
        }

        public override async ValueTask<List<IOperableTrigger>> SelectTriggersForCalendar(
            ConnectionAndTransactionHolder conn,
            string calendarName,
            CancellationToken cancellationToken = default)
        {
            List<IOperableTrigger> triggers = await base.SelectTriggersForCalendar(conn, calendarName, cancellationToken);
            await Fire(conn, () => AfterCalendarTriggersRead, () => AfterCalendarTriggersRead = null);
            return triggers;
        }

        public override ValueTask<int> UpdateTriggerIfUnchanged(
            ConnectionAndTransactionHolder conn,
            IOperableTrigger trigger,
            StoredTriggerState state,
            IJobDetail jobDetail,
            DateTimeOffset? expectedNextFireTimeUtc,
            CancellationToken cancellationToken = default)
        {
            GuardedWrites.Add(expectedNextFireTimeUtc);
            return base.UpdateTriggerIfUnchanged(conn, trigger, state, jobDetail, expectedNextFireTimeUtc, cancellationToken);
        }

        private static async Task Fire(ConnectionAndTransactionHolder conn, Func<Func<ConnectionAndTransactionHolder, Task>?> hook, Action disarm)
        {
            Func<ConnectionAndTransactionHolder, Task>? armed = hook();
            if (armed is not null)
            {
                disarm();
                await armed(conn);
            }
        }
    }

    /// <summary>Records what the store told the listeners, in order.</summary>
    private sealed class RecordingSignaler : ISchedulerSignaler
    {
        public List<ITrigger> Misfired { get; } = [];

        public List<ITrigger> Finalized { get; } = [];

        public ValueTask NotifyTriggerListenersMisfired(ITrigger trigger, CancellationToken cancellationToken = default)
        {
            Misfired.Add(trigger);
            return default;
        }

        public ValueTask NotifySchedulerListenersFinalized(ITrigger trigger, CancellationToken cancellationToken = default)
        {
            Finalized.Add(trigger);
            return default;
        }

        public ValueTask NotifySchedulerListenersJobDeleted(JobKey jobKey, CancellationToken cancellationToken = default) => default;

        public ValueTask SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersError(SchedulerErrorContext errorContext, CancellationToken cancellationToken = default) => default;
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
