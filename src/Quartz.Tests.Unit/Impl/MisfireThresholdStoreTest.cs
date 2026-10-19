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

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// Which store <see cref="MisfireThresholdStoreTest" /> is run against.
/// </summary>
public enum MisfireThresholdStoreKind
{
    InMemory,
    Sqlite
}

/// <summary>
/// A trigger's own <see cref="ITrigger.MisfireThreshold" /> decides its misfire everywhere a store decides
/// one — acquisition, the misfire scan, a trigger released from <c>BLOCKED</c>, a resumed trigger — in the
/// in-memory store and in a SQLite-backed ADO store (#4070).
/// </summary>
/// <remarks>
/// Driven against the store on a clock the test owns, initialized but never started, as
/// <c>OverlapPolicyStoreTest</c> is. The store's own threshold is a minute; the triggers under test carry
/// five seconds or ten minutes.
/// </remarks>
[TestFixture(MisfireThresholdStoreKind.InMemory)]
[TestFixture(MisfireThresholdStoreKind.Sqlite)]
public sealed class MisfireThresholdStoreTest
{
    private const string Group = "threshold";
    private const string SchedulerName = "MisfireThresholdStoreTest";
    private const string DataSourceName = "misfire-threshold";

    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan storeThreshold = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan shortThreshold = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan longThreshold = TimeSpan.FromMinutes(10);

    private readonly MisfireThresholdStoreKind kind;

    private SqliteTestDatabase? database;
    private IDbProvider? dbProvider;

    private FakeTimeProvider clock = null!;
    private RecordingSignaler signals = null!;
    private IJobStore store = null!;

    public MisfireThresholdStoreTest(MisfireThresholdStoreKind kind)
    {
        this.kind = kind;
    }

    [OneTimeSetUp]
    public async Task CreateDatabase()
    {
        if (kind != MisfireThresholdStoreKind.Sqlite)
        {
            return;
        }

        database = new SqliteTestDatabase("misfire-threshold");

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
        database?.Dispose();
    }

    [SetUp]
    public async Task BuildStore()
    {
        clock = new FakeTimeProvider(epoch);
        signals = new RecordingSignaler();
        store = kind == MisfireThresholdStoreKind.InMemory ? await InMemoryStore() : await SqliteStore();
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        await store.Shutdown();
    }

    [Test]
    public async Task AShorterThresholdMisfiresATriggerTheStoreWouldStillFire()
    {
        IOperableTrigger tight = await Schedule("tight", shortThreshold);
        IOperableTrigger control = await Schedule("control", threshold: null);

        clock.Advance(TimeSpan.FromSeconds(30));

        (await AcquireKeys()).Should().Equal([control.Key],
            "thirty seconds late is inside the store's minute but past the five seconds the tight trigger allows");

        await RecoverMisfires();

        signals.Misfired.Should().Equal([tight.Key], "the tight trigger's own threshold is what its misfire is decided by");
        (await store.GetTrigger(tight.Key))!.NextFireTimeUtc.Should().Be(epoch.AddHours(1),
            "its smart policy skipped the missed hour and waits for the next");
    }

    [Test]
    public async Task ALongerThresholdFiresATriggerTheStoreWouldMisfire()
    {
        IOperableTrigger tolerant = await Schedule("tolerant", longThreshold);
        IOperableTrigger control = await Schedule("control", threshold: null);

        clock.Advance(TimeSpan.FromMinutes(2));

        (await AcquireKeys()).Should().Equal([tolerant.Key],
            "two minutes late is past the store's minute but inside the ten the tolerant trigger allows");

        await RecoverMisfires();

        signals.Misfired.Should().Equal([control.Key],
            "only the trigger on the store's threshold is a misfire; the scan leaves the tolerant one to acquisition");
    }

    [Test]
    public async Task ALongerThresholdStillMisfiresOncePastIt()
    {
        IOperableTrigger tolerant = await Schedule("tolerant", longThreshold);

        clock.Advance(TimeSpan.FromMinutes(11));

        (await AcquireKeys()).Should().BeEmpty("eleven minutes late is past even the trigger's own ten");
        await RecoverMisfires();

        signals.Misfired.Should().Equal([tolerant.Key]);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ATriggerReleasedFromBlockedIsJudgedByItsOwnThreshold(bool tight)
    {
        IJobDetail job = JobBuilder.Create<SerialThresholdJob>().WithIdentity("serial", Group).StoreDurably().Build();
        await store.AddJob(job);

        IOperableTrigger first = await Schedule("first", threshold: null, job: job);
        IOperableTrigger second = await Schedule("second", tight ? shortThreshold : null, job: job, startAt: epoch.AddSeconds(10));

        List<IOperableTrigger> firing = await AcquireAndFire(first.Key);

        // The second trigger came due while the first firing held the job, and is thirty seconds late when
        // the job is let go.
        clock.Advance(TimeSpan.FromSeconds(40));
        await store.FiringComplete(new TriggeredJobCompleteContext
        {
            Trigger = firing[0],
            JobDetail = job,
            Instruction = SchedulerInstruction.NoInstruction,
            Outcome = ExecutionOutcome.Succeeded
        });
        await RecoverMisfires();

        if (tight)
        {
            signals.Misfired.Should().Equal([second.Key], "released thirty seconds late, past its own five");
        }
        else
        {
            signals.Misfired.Should().BeEmpty("released thirty seconds late, inside the store's minute");
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task AResumedTriggerIsJudgedByItsOwnThreshold(bool tight)
    {
        IOperableTrigger trigger = await Schedule("paused", tight ? shortThreshold : null);
        await store.PauseTrigger(trigger.Key);

        // The database store applies a resumed trigger's misfire only while the scheduler runs; resuming
        // the store says so without starting its misfire loop.
        await store.SchedulerResumed();

        clock.Advance(TimeSpan.FromSeconds(30));
        await store.ResumeTrigger(trigger.Key);

        if (tight)
        {
            signals.Misfired.Should().Equal([trigger.Key], "resumed thirty seconds late, past its own five");
        }
        else
        {
            signals.Misfired.Should().BeEmpty("resumed thirty seconds late, inside the store's minute");
            (await AcquireKeys()).Should().Equal([trigger.Key]);
        }
    }

    [Test]
    public async Task AnUpdateSetsAndClearsTheThreshold()
    {
        IOperableTrigger trigger = await Schedule("edited", threshold: null);

        (await store.UpdateTriggerDetails(trigger.Key, new TriggerDetailsUpdate().WithMisfireThreshold(shortThreshold))).Should().BeTrue();
        (await store.GetTrigger(trigger.Key))!.MisfireThreshold.Should().Be(shortThreshold);
        await ColumnShouldBe(trigger.Key, 5000L);

        clock.Advance(TimeSpan.FromSeconds(30));
        (await AcquireKeys()).Should().BeEmpty("the threshold the edit set decides from the next acquisition");
        await RecoverMisfires();
        signals.Misfired.Should().Equal([trigger.Key]);

        (await store.UpdateTriggerDetails(trigger.Key, new TriggerDetailsUpdate().WithMisfireThreshold(null))).Should().BeTrue();
        (await store.GetTrigger(trigger.Key))!.MisfireThreshold.Should().BeNull("null puts the trigger back on the store's threshold");
        await ColumnShouldBe(trigger.Key, null);
    }

    [Test]
    public async Task TheThresholdIsStoredAsWholeMillisecondsAndReadBack()
    {
        IOperableTrigger tight = await Schedule("tight", TimeSpan.FromMilliseconds(5250));
        IOperableTrigger control = await Schedule("control", threshold: null);

        (await store.GetTrigger(tight.Key))!.MisfireThreshold.Should().Be(TimeSpan.FromMilliseconds(5250));
        (await store.GetTrigger(control.Key))!.MisfireThreshold.Should().BeNull();

        await ColumnShouldBe(tight.Key, 5250L);
        await ColumnShouldBe(control.Key, null);
    }

    /// <summary>
    /// The count the misfire handler peeks with, the sweep's read and the acquisition statement agree about
    /// every row: a trigger belongs to the misfire handler or to acquisition, never to both or neither.
    /// </summary>
    /// <remarks>
    /// Asked of the delegate directly: the SQLite store takes the lock before it looks, so it never peeks.
    /// </remarks>
    [Test]
    public async Task TheCountTheSweepAndAcquisitionAgreeAboutEveryRow()
    {
        IgnoreUnlessSqlite("the statements are the ADO store's");

        await Schedule("tight", shortThreshold);
        await Schedule("tolerant", longThreshold);
        await Schedule("control", threshold: null);

        clock.Advance(TimeSpan.FromSeconds(90));

        SQLiteDelegate driver = new();
        driver.Initialize(new DriverDelegateContext
        {
            TablePrefix = "QRTZ_",
            SchedulerName = SchedulerName,
            InstanceId = "node-a",
            TypeLoader = new SimpleTypeLoader(),
            DbProvider = dbProvider!,
            TimeProvider = clock,
        });

        await using SqliteConnection connection = new(database!.ConnectionString);
        await connection.OpenAsync();
        ConnectionAndTransactionHolder conn = new(connection, null);

        DateTimeOffset misfireTime = clock.GetUtcNow() - storeThreshold;

        (await driver.CountMisfiredTriggersInState(conn, StoredTriggerState.Waiting, misfireTime)).Should().Be(2,
            "ninety seconds late misfires the tight trigger and the control, and not the tolerant one");

        MisfiredTriggerBatch batch = await driver.SelectMisfiredTriggersToRecover(conn, StoredTriggerState.Waiting, misfireTime, 10);
        batch.Triggers.Select(x => x.Key.Name).Should().BeEquivalentTo(["tight", "control"]);

        List<TriggerAcquireResult> candidates = await driver.SelectTriggersToAcquire(conn, new TriggerAcquisitionCriteria
        {
            NoLaterThan = clock.GetUtcNow().AddSeconds(1),
            NoEarlierThan = misfireTime,
            MaxCount = 10,
            LiveNodeCutoff = clock.GetUtcNow().AddMinutes(-1),
        });
        candidates.Select(x => x.TriggerKey.Name).Should().Equal(["tolerant"], "acquisition takes exactly what the sweep leaves");
    }

    private void IgnoreUnlessSqlite(string reason)
    {
        if (kind != MisfireThresholdStoreKind.Sqlite)
        {
            Assert.Ignore(reason);
        }
    }

    private async ValueTask<IJobStore> InMemoryStore()
    {
        RAMJobStore ram = TestJobStores.Ram(signals, clock);
        ram.MisfireThreshold = storeThreshold;
        await ram.Initialize(TestJobStores.Identity(instanceName: SchedulerName));
        return ram;
    }

    private async ValueTask<IJobStore> SqliteStore()
    {
        LocalTransactionJobStore ado = new(TestJobStores.Dependencies(
            signaler: signals,
            timeProvider: clock,
            schedulerOptions: TestJobStores.SchedulerOptions(instanceName: SchedulerName, instanceId: "node-a"),
            storeOptions: TestJobStores.StoreOptions(DataSourceName, configure: x => x.MisfireThreshold = storeThreshold),
            dbProvider: dbProvider,
            driverDelegate: new SQLiteDelegate()));

        // Initialized but deliberately not started, so no misfire loop races an assertion.
        await ado.Initialize(TestJobStores.Identity(instanceName: SchedulerName, instanceId: "node-a"));
        await ado.Clear();
        return ado;
    }

    private async Task<IOperableTrigger> Schedule(string name, TimeSpan? threshold, IJobDetail? job = null, DateTimeOffset? startAt = null)
    {
        IJobDetail owner = job ?? JobBuilder.Create<ThresholdJob>().WithIdentity(name, Group).StoreDurably().Build();

        TriggerBuilder<IJob> builder = TriggerBuilder.Create(clock)
            .WithIdentity(name, Group)
            .ForJob(owner)
            .StartAt(startAt ?? clock.GetUtcNow())
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever());

        if (threshold is { } value)
        {
            builder.WithMisfireThreshold(value);
        }

        IOperableTrigger trigger = (IOperableTrigger) builder.Build();
        trigger.ComputeFirstFireTimeUtc(null);

        if (job is null)
        {
            await store.ScheduleJob(owner, trigger);
        }
        else
        {
            await store.AddTrigger(trigger);
        }

        return trigger;
    }

    private async Task<List<IOperableTrigger>> AcquireAndFire(params TriggerKey[] keys)
    {
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddSeconds(1),
            MaxCount = 10,
            TimeWindow = TimeSpan.Zero
        });

        acquired.Select(x => x.Key).Should().BeEquivalentTo(keys, "the triggers under test are the ones due");

        List<TriggerFiredResult> results = await store.TriggersFired(acquired);
        results.Should().OnlyContain(x => x.TriggerFiredBundle != null);
        return results.Select(x => x.TriggerFiredBundle!.Trigger).ToList();
    }

    /// <summary>
    /// What an acquisition would take right now, handed straight back so it changes nothing.
    /// </summary>
    private async Task<List<TriggerKey>> AcquireKeys()
    {
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddSeconds(1),
            MaxCount = 10,
            TimeWindow = TimeSpan.Zero
        });

        foreach (IOperableTrigger trigger in acquired)
        {
            await store.ReleaseAcquiredTrigger(trigger);
        }

        return acquired.Select(x => x.Key).ToList();
    }

    /// <summary>
    /// A misfire pass over whatever is overdue: the database store's handler, or the in-memory store's
    /// acquisition, which is where that store applies misfire instructions.
    /// </summary>
    private async Task RecoverMisfires()
    {
        if (store is LocalTransactionJobStore ado)
        {
            await ado.RecoverMisfires(Guid.NewGuid());
            return;
        }

        await AcquireKeys();
    }

    private async Task ColumnShouldBe(TriggerKey key, long? milliseconds)
    {
        if (kind != MisfireThresholdStoreKind.Sqlite)
        {
            return;
        }

        await using SqliteConnection connection = new(database!.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT MISFIRE_THRESHOLD FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name AND TRIGGER_GROUP = @group";
        command.Parameters.AddWithValue("@name", key.Name);
        command.Parameters.AddWithValue("@group", key.Group);

        object? value = await command.ExecuteScalarAsync();
        (value is DBNull or null ? (long?) null : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)).Should().Be(milliseconds,
            "the column holds the trigger's own threshold in milliseconds, and NULL for the store's");
    }

    private static string LoadSqliteTableScript()
    {
        string path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        return File.ReadAllText(path);
    }

    /// <summary>
    /// Records which triggers the store reported misfired.
    /// </summary>
    private sealed class RecordingSignaler : ISchedulerSignaler
    {
        public List<TriggerKey> Misfired { get; } = [];

        public ValueTask NotifyTriggerListenersMisfired(ITrigger trigger, CancellationToken cancellationToken = default)
        {
            Misfired.Add(trigger.Key);
            return default;
        }

        public ValueTask NotifySchedulerListenersFinalized(ITrigger trigger, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersJobDeleted(JobKey jobKey, CancellationToken cancellationToken = default) => default;

        public ValueTask SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersError(SchedulerErrorContext errorContext, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>Never executed: these tests drive the store, not a scheduler.</summary>
    public sealed class ThresholdJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>A job that forbids concurrent execution, so its second trigger waits <c>BLOCKED</c>.</summary>
    [DisallowConcurrentExecution]
    public sealed class SerialThresholdJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
