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
/// A firing round without the lock that fires apart (#4117), run against real rows: the claim reserves,
/// the fire claims the due rows again from <c>ACQUIRED</c> and updates each reservation to
/// <c>EXECUTING</c>, and a row moved between the two is left as the mover left it.
/// </summary>
/// <remarks>
/// SQLite never runs a round without the lock — it serializes every operation — and does not fire apart, so
/// the store here is told otherwise once it has started: the statements are the shipped ones, and what
/// SQL Server's own run of them shows under contention is the integration tests' to prove. A seam in the
/// delegate runs just before the fire's guard, in the fire's own transaction, which is the state a write
/// committed between the two transactions leaves.
/// </remarks>
public sealed class LockFreeRoundsApartSqliteTest
{
    private const string Group = "apart";
    private const string SchedulerName = "LockFreeRoundsApartSqliteTest";

    /// <summary>Log event <c>AdoJobStoreLog.RoundFireHandedToLock</c>.</summary>
    private const int RoundFireHandedToLock = 3062;

    /// <summary>On the hour, UTC, far from any machine's own clock.</summary>
    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Due half a minute ago: inside the misfire threshold, so the round takes it.</summary>
    private static readonly DateTimeOffset due = epoch.AddSeconds(-30);

    private SqliteTestDatabase database = null!;
    private IDbProvider dbProvider = null!;
    private ApartSqliteDelegate seam = null!;
    private RecordingLoggerProvider logs = null!;
    private ILoggerFactory loggerFactory = null!;
    private LocalTransactionJobStore store = null!;

    [OneTimeSetUp]
    public async Task CreateDatabase()
    {
        database = new SqliteTestDatabase("lock-free-apart");

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
        seam = new ApartSqliteDelegate();
        logs = new RecordingLoggerProvider();
        loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));

        store = new LocalTransactionJobStore(TestJobStores.Dependencies(
            signaler: new NoOpSignaler(),
            timeProvider: new FakeTimeProvider(epoch),
            schedulerOptions: TestJobStores.SchedulerOptions(instanceName: SchedulerName, instanceId: "node-a"),
            storeOptions: TestJobStores.StoreOptions("lock-free-apart"),
            dbProvider: dbProvider,
            driverDelegate: seam,
            loggerFactory: loggerFactory));

        // Initialized but deliberately not started, so no misfire loop races an assertion; then told what
        // SQLite's forcing took away, so that its rounds may take no lock.
        await store.Initialize(TestJobStores.Identity(instanceName: SchedulerName, instanceId: "node-a"));
        await store.Clear();
        store.LockAllOperations = false;
        store.AcquireTriggersWithinLock = false;
        store.TransactionIsolationLevel = null;
        store.StaticRoundLockReason.Should().BeNull("the premise: the store's rounds may take no lock");
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        await store.Shutdown();
        loggerFactory.Dispose();
        logs.Dispose();
    }

    [Test]
    public async Task TheClaimReservesAndTheFireUpdatesTheReservationInASecondTransaction()
    {
        TriggerKey key = await ScheduleRepeating("fired");

        TriggerAcquisitionResult round = await AcquireAndFire();

        round.Due.Should().ContainSingle().Which.Key.Should().Be(key);
        round.Fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull();
        round.LockName.Should().Be("none");

        seam.ClaimConnection.Should().NotBeNull();
        seam.GuardConnection.Should().NotBeNull();
        seam.GuardConnection.Should().NotBeSameAs(seam.ClaimConnection, "the fire runs in a transaction of its own");

        (await FiredState(round.Due[0].FireInstanceId!)).Should().Be("EXECUTING", "the fire updated the reservation its claim wrote");
        (await FiredRowCount()).Should().Be(1, "one row for the firing, not a reservation beside an inserted row");
        (await TriggerState(key)).Should().Be("WAITING");
        (await NextFireTime(key)).Should().Be(due.AddHours(1), "the fire advanced the trigger");
    }

    [Test]
    public async Task ARowPausedBetweenTheClaimAndTheFireIsNotFiredAndStaysPaused()
    {
        TriggerKey key = await ScheduleRepeating("paused");
        seam.BeforeGuard = conn => Execute(conn, "UPDATE QRTZ_TRIGGERS SET TRIGGER_STATE = 'PAUSED' WHERE TRIGGER_NAME = @n", key.Name);

        TriggerAcquisitionResult round = await AcquireAndFire();

        round.Fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().BeNull("the guard found the row no longer ACQUIRED");
        (await TriggerState(key)).Should().Be("PAUSED", "the fire wrote nothing over the pause");
        (await NextFireTime(key)).Should().Be(due);
        (await FiredState(round.Due[0].FireInstanceId!)).Should().Be("ACQUIRED", "the reservation stays for the scheduler to release");

        await store.ReleaseAcquiredTrigger(round.Due[0]);

        (await FiredRowCount()).Should().Be(0);
        (await TriggerState(key)).Should().Be("PAUSED", "a release moves a row from ACQUIRED only");
    }

    [Test]
    public async Task ARowRescheduledBetweenTheClaimAndTheFireIsNotFired()
    {
        TriggerKey key = await ScheduleRepeating("rescheduled");
        DateTimeOffset movedTo = epoch.AddHours(5);
        seam.BeforeGuard = conn => Execute(
            conn,
            "UPDATE QRTZ_TRIGGERS SET TRIGGER_STATE = 'WAITING', NEXT_FIRE_TIME = " + movedTo.UtcTicks.ToString(CultureInfo.InvariantCulture) + " WHERE TRIGGER_NAME = @n",
            key.Name);

        TriggerAcquisitionResult round = await AcquireAndFire();

        round.Fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().BeNull();
        (await NextFireTime(key)).Should().Be(movedTo, "the row is as the reschedule left it");
        (await TriggerState(key)).Should().Be("WAITING");
    }

    [Test]
    public async Task AJobMadeSerialBetweenTheClaimAndTheFireIsLeftForTheSchedulerToFireUnderTheLock()
    {
        TriggerKey key = await ScheduleRepeating("serial");
        seam.BeforeGuard = conn => Execute(conn, "UPDATE QRTZ_JOB_DETAILS SET IS_NONCONCURRENT = 1 WHERE JOB_NAME = @n", key.Name);

        TriggerAcquisitionResult round = await AcquireAndFire();

        round.Due.Should().BeEmpty();
        round.Pending.Should().ContainSingle().Which.Key.Should().Be(key);
        round.LockReason.Should().Be("definition_changed");
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == RoundFireHandedToLock);
        (await FiredState(round.Pending[0].FireInstanceId!)).Should().Be("ACQUIRED", "the fire rolled back and the claim's reservation stands");
        (await NextFireTime(key)).Should().Be(due, "nothing was fired");

        List<TriggerFiredResult> fired = await store.TriggersFired(round.Pending);

        fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull("the scheduler fires the reserved trigger under the lock, once");
        (await FiredState(round.Pending[0].FireInstanceId!)).Should().Be("EXECUTING");
        (await NextFireTime(key)).Should().Be(due.AddHours(1));
    }

    private ValueTask<TriggerAcquisitionResult> AcquireAndFire()
    {
        return store.AcquireNextTriggersAndFireDue(new TriggerAcquisitionRequest
        {
            NoLaterThan = epoch.AddSeconds(30),
            MaxCount = 2,
        });
    }

    private async Task<TriggerKey> ScheduleRepeating(string name)
    {
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity(name, Group).StoreDurably().Build();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(due)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

        trigger.ComputeFirstFireTimeUtc(calendar: null);
        await store.ScheduleJob(job, trigger);
        return trigger.Key;
    }

    private async Task<string?> FiredState(string fireInstanceId)
    {
        return (string?) await Scalar("SELECT STATE FROM QRTZ_FIRED_TRIGGERS WHERE ENTRY_ID = @n", fireInstanceId);
    }

    private async Task<long> FiredRowCount()
    {
        return Convert.ToInt64(await Scalar("SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @n", SchedulerName), CultureInfo.InvariantCulture);
    }

    private async Task<string?> TriggerState(TriggerKey key)
    {
        return (string?) await Scalar("SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @n", key.Name);
    }

    private async Task<DateTimeOffset> NextFireTime(TriggerKey key)
    {
        long ticks = Convert.ToInt64(await Scalar("SELECT NEXT_FIRE_TIME FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @n", key.Name), CultureInfo.InvariantCulture);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private async Task<object?> Scalar(string sql, string name)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@n", name);
        return await command.ExecuteScalarAsync();
    }

    /// <summary>
    /// A write committed between the round's two transactions, made in the fire's own, since SQLite's single
    /// writer admits no second one.
    /// </summary>
    private static async Task Execute(ConnectionAndTransactionHolder conn, string sql, string name)
    {
        await using DbCommand command = conn.Connection.CreateCommand();
        command.Transaction = conn.Transaction;
        command.CommandText = sql;
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "@n";
        parameter.Value = name;
        command.Parameters.Add(parameter);

        (await command.ExecuteNonQueryAsync()).Should().Be(1, "the row under test is there to be changed");
    }

    private static string LoadSqliteTableScript()
    {
        string path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        return File.ReadAllText(path);
    }

    /// <summary>
    /// The SQLite delegate asking for its rounds to fire apart, as SQL Server's does, with a seam just
    /// before the fire's guard, and a record of the two transactions' connections.
    /// </summary>
    private sealed class ApartSqliteDelegate : SQLiteDelegate
    {
        public Func<ConnectionAndTransactionHolder, Task>? BeforeGuard { get; set; }

        public ConnectionAndTransactionHolder? ClaimConnection { get; private set; }

        public ConnectionAndTransactionHolder? GuardConnection { get; private set; }

        /// <summary>Takes part in the rounds, which the shipped delegate does and a subclass has to say.</summary>
        public override bool SupportsFireOnAcquire => true;

        internal override bool FiresLockFreeRoundsApart => true;

        public override async ValueTask<List<TriggerKey>> UpdateTriggerStatesFromOtherStateWithNextFireTime(
            ConnectionAndTransactionHolder conn,
            IReadOnlyList<TriggerClaim> claims,
            StoredTriggerState newState,
            StoredTriggerState oldState,
            CancellationToken cancellationToken = default)
        {
            if (oldState == StoredTriggerState.Waiting)
            {
                ClaimConnection = conn;
            }
            else if (oldState == StoredTriggerState.Acquired && newState == StoredTriggerState.Acquired)
            {
                GuardConnection = conn;
                if (BeforeGuard is { } armed)
                {
                    BeforeGuard = null;
                    await armed(conn);
                }
            }

            return await base.UpdateTriggerStatesFromOtherStateWithNextFireTime(conn, claims, newState, oldState, cancellationToken);
        }
    }

    private sealed class NoOpSignaler : ISchedulerSignaler
    {
        public ValueTask NotifyTriggerListenersMisfired(ITrigger trigger, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersFinalized(ITrigger trigger, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersJobDeleted(JobKey jobKey, CancellationToken cancellationToken = default) => default;

        public ValueTask SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersError(SchedulerErrorContext errorContext, CancellationToken cancellationToken = default) => default;
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
