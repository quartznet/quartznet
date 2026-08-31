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
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Integration.Impl;

/// <summary>
/// The scaffolding behind the misfire matrix: an in-memory store and a real SQLite one, each on a
/// clock the test moves, each driven through exactly one misfire pass by hand.
/// </summary>
/// <remarks>
/// <para>
/// The pattern is #3303's: initialize the store and never call <c>SchedulerStarted()</c>, so the ADO
/// store's <c>MisfireHandler</c> loop is never spawned and cannot recover a trigger out from under
/// the assertions. The clock is a <see cref="FakeTimeProvider" /> so that "past the misfire
/// threshold" is an assignment rather than a wait.
/// </para>
/// <para>
/// <b>A trigger reads the clock of whoever produced it, and these are all produced on the store's.</b>
/// <c>TriggerBuilder.Create(clock)</c> hands its clock to the trigger it builds, and the ADO store
/// hands its own to every trigger it materializes out of its rows, so <em>what</em>
/// <c>UpdateAfterMisfire</c> computes is on the fake clock in both stores, not only <em>whether</em> a
/// store treats a trigger as late. Nothing here reads real time, so every expected instant is exact —
/// a "fire now" one included, since a <see cref="FakeTimeProvider" /> does not move unless the test
/// moves it. Schedules are still anchored half a period out (twelve hours either side of
/// <c>anchor</c>) so that no cell sits on a schedule boundary.
/// </para>
/// </remarks>
[NonParallelizable]
public abstract class MisfireThroughAStoreTestBase
{
    /// <summary>The group every job and trigger in these fixtures lives in.</summary>
    protected const string Group = "misfire-through-a-store";

    /// <summary>
    /// The threshold both stores are configured with. Generous next to the twelve hours a matrix
    /// trigger is late by, and small enough that the threshold-edge cases are legible.
    /// </summary>
    public static readonly TimeSpan Threshold = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Half of every schedule's period. A matrix trigger's missed firing sits this far before
    /// <c>anchor</c> and its next slot this far after, so neither is near a schedule boundary.
    /// </summary>
    public static readonly TimeSpan HalfPeriod = TimeSpan.FromHours(12);

    private const string TablePrefix = "QRTZ_";
    private const string DataSourceName = "misfire-through-a-store-sqlite";

    private string databaseFileName;
    private IDbProvider dbProvider;
    private readonly List<MisfireStoreUnderTest> stores = [];
    private int storeCounter;

    [OneTimeSetUp]
    public async Task CreateDatabase()
    {
        databaseFileName = $"test-misfire-store-{Guid.NewGuid():N}.db";

        await using (SqliteConnection connection = new($"Data Source={databaseFileName};"))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = new(LoadSqliteTableScript(), connection);
            await command.ExecuteNonQueryAsync();
        }

        dbProvider = new DbProvider("SQLite-Microsoft", $"Data Source={databaseFileName};");
    }

    [OneTimeTearDown]
    public void DropDatabase()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(databaseFileName))
        {
            try
            {
                File.Delete(databaseFileName);
            }
            catch (IOException)
            {
                // the file is only test scratch space, leaving it behind is not worth failing over
            }
        }
    }

    [TearDown]
    public async Task ShutDownStores()
    {
        foreach (MisfireStoreUnderTest store in stores)
        {
            await store.Store.Shutdown();
        }

        stores.Clear();
    }

    /// <summary>
    /// The instant every schedule in a test is anchored on, and the value both store clocks reach once
    /// the test has advanced them.
    /// </summary>
    /// <remarks>
    /// Real time only as a seed: nothing reads the machine clock again after this, so a test is
    /// deterministic relative to whatever instant it started from.
    /// </remarks>
    protected static DateTimeOffset Anchor() => TimeProvider.System.GetUtcNow();

    /// <summary>
    /// A clock frozen where both stores' clocks stand once a test has advanced them, for a detached
    /// copy that has to compute what a store computed but is built before either store exists.
    /// </summary>
    protected static FakeTimeProvider ClockAt(DateTimeOffset instant) => new FakeTimeProvider(instant);

    /// <summary>
    /// Both stores, in the order they are reported in: the in-memory one first, because a failure
    /// there is a failure of the simpler of the two.
    /// </summary>
    protected async ValueTask<MisfireStoreUnderTest[]> BothStores(DateTimeOffset anchor)
    {
        return [await InMemoryStore(anchor), await SqliteStore(anchor)];
    }

    /// <summary>
    /// An in-memory store whose clock starts at <paramref name="anchor" /> less half a period — which
    /// is where a matrix trigger's missed firing sits, so nothing is late until the test says so.
    /// </summary>
    protected async ValueTask<MisfireStoreUnderTest> InMemoryStore(DateTimeOffset anchor)
    {
        FakeTimeProvider clock = new(anchor - HalfPeriod);
        RAMJobStore store = TestJobStores.Ram(timeProvider: clock);
        store.MisfireThreshold = Threshold;

        await store.Initialize(TestJobStores.Identity(instanceId: NextInstanceId()));

        return Track(new InMemoryMisfireStore(store, clock));
    }

    /// <summary>
    /// A SQLite-backed ADO store over the fixture's own database file, cleared so that each test sees
    /// only its own rows.
    /// </summary>
    protected async ValueTask<MisfireStoreUnderTest> SqliteStore(DateTimeOffset anchor)
    {
        FakeTimeProvider clock = new(anchor - HalfPeriod);
        string instanceId = NextInstanceId();

        LocalTransactionJobStore store = new(TestJobStores.Dependencies(
            timeProvider: clock,
            schedulerOptions: TestJobStores.SchedulerOptions(instanceName: "MisfireThroughAStoreTest", instanceId: instanceId),
            storeOptions: TestJobStores.StoreOptions(DataSourceName, TablePrefix, options =>
            {
                options.MisfireThreshold = Threshold;
            }),
            dbProvider: dbProvider,
            driverDelegate: new SQLiteDelegate()));

        // Initialized but deliberately not started: SchedulerStarted() spawns the MisfireHandler loop,
        // which would sweep on its own thread and race every assertion below.
        await store.Initialize(TestJobStores.Identity(instanceName: "MisfireThroughAStoreTest", instanceId: instanceId));
        await store.Clear();

        return Track(new SqliteMisfireStore(store, clock));
    }

    private MisfireStoreUnderTest Track(MisfireStoreUnderTest store)
    {
        stores.Add(store);
        return store;
    }

    /// <summary>
    /// A fresh instance id per store, so two stores in one test are two nodes rather than one node
    /// talking to itself.
    /// </summary>
    private string NextInstanceId() => "node-" + (++storeCounter);

    /// <summary>
    /// Stores a job and its trigger with the trigger's next fire time pinned half a period in the past.
    /// </summary>
    /// <remarks>
    /// Writing the fire time back by hand is the whole trick: <c>ComputeFirstFireTimeUtc</c> advances a
    /// past-due schedule to its next future slot, so a trigger built with a past start time is not
    /// overdue by the time it reaches a store. A trigger nobody got around to firing looks like this.
    /// </remarks>
    protected static async ValueTask<IOperableTrigger> Store(
        MisfireStoreUnderTest store,
        IJobDetail job,
        IOperableTrigger trigger,
        DateTimeOffset scheduledFireTimeUtc,
        ICalendar calendar = null)
    {
        trigger.ComputeFirstFireTimeUtc(calendar);
        trigger.NextFireTimeUtc = scheduledFireTimeUtc;

        await store.Store.ScheduleJob(job, trigger);
        return trigger;
    }

    /// <summary>
    /// Stores a second trigger for a job that is already there, with its next fire time pinned the same
    /// way <see cref="Store" /> pins the first one's.
    /// </summary>
    protected static async ValueTask<IOperableTrigger> StoreTrigger(
        MisfireStoreUnderTest store,
        IOperableTrigger trigger,
        DateTimeOffset scheduledFireTimeUtc,
        ICalendar calendar = null)
    {
        trigger.ComputeFirstFireTimeUtc(calendar);
        trigger.NextFireTimeUtc = scheduledFireTimeUtc;

        await store.Store.AddTrigger(trigger);
        return trigger;
    }

    /// <summary>
    /// The job the matrix's triggers point at. It never runs: these tests drive the store, not the
    /// scheduler.
    /// </summary>
    protected static IJobDetail Job(JobKey jobKey) => JobBuilder.Create<MisfireTestJob>().WithIdentity(jobKey).Build();

    private static string LoadSqliteTableScript()
    {
        string path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        return File.ReadAllText(path);
    }

    /// <summary>A job that does nothing, because nothing here ever fires one.</summary>
    public sealed class MisfireTestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// A job that forbids concurrent execution, for the blocked-trigger cases.
    /// </summary>
    [DisallowConcurrentExecution]
    public sealed class NonConcurrentMisfireTestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}

/// <summary>
/// One store under test, its clock, and the single operation that makes it apply a trigger's misfire
/// policy.
/// </summary>
public abstract class MisfireStoreUnderTest
{
    protected MisfireStoreUnderTest(FakeTimeProvider clock)
    {
        Clock = clock;
    }

    /// <summary>The store's name, as it reads in a failure message.</summary>
    public abstract string Name { get; }

    /// <summary>The store itself.</summary>
    public abstract IJobStore Store { get; }

    /// <summary>The clock the store reads. The test moves it; nothing waits on it.</summary>
    public FakeTimeProvider Clock { get; }

    /// <summary>
    /// Runs exactly one misfire pass.
    /// </summary>
    /// <param name="noLaterThan">
    /// Only the in-memory store reads this; see <see cref="InMemoryMisfireStore.Sweep" />.
    /// </param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public abstract ValueTask Sweep(DateTimeOffset noLaterThan, CancellationToken cancellationToken = default);

    public override string ToString() => Name;
}

/// <summary>
/// The in-memory store. It has no misfire sweep of its own — <c>RAMJobStore.ApplyMisfireNoLock</c> is
/// reached from exactly two places, <c>AcquireNextTriggers</c> and <c>ResumeTrigger</c> — so
/// acquisition <em>is</em> the pass.
/// </summary>
public sealed class InMemoryMisfireStore : MisfireStoreUnderTest
{
    private readonly RAMJobStore store;

    internal InMemoryMisfireStore(RAMJobStore store, FakeTimeProvider clock) : base(clock)
    {
        this.store = store;
    }

    public override string Name => "RAMJobStore";

    public override IJobStore Store => store;

    /// <summary>
    /// Asks for triggers due no later than <paramref name="noLaterThan" />, which the caller sets
    /// <em>before</em> the misfired trigger's own fire time. Acquisition applies the misfire policy
    /// before it decides whether the trigger is in the batch, so a window that excludes the trigger
    /// still runs the policy and then leaves the trigger alone — the state that comes out is the
    /// misfire's doing and nothing else's.
    /// </summary>
    public override async ValueTask Sweep(DateTimeOffset noLaterThan, CancellationToken cancellationToken = default)
    {
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(
            new TriggerAcquisitionRequest { NoLaterThan = noLaterThan, MaxCount = 1 },
            cancellationToken);

        acquired.Should().BeEmpty(
            "the acquisition window is set before the misfired trigger's own fire time, so the pass must "
            + "apply the misfire policy without also acquiring the trigger and clouding its state");
    }
}

/// <summary>
/// The ADO store over SQLite. Its pass is the misfire handler's own, called by hand.
/// </summary>
public sealed class SqliteMisfireStore : MisfireStoreUnderTest
{
    private readonly LocalTransactionJobStore store;

    internal SqliteMisfireStore(LocalTransactionJobStore store, FakeTimeProvider clock) : base(clock)
    {
        this.store = store;
    }

    public override string Name => "SQLite ADO store";

    public override IJobStore Store => store;

    public override async ValueTask Sweep(DateTimeOffset noLaterThan, CancellationToken cancellationToken = default)
    {
        await store.RecoverMisfires(Guid.NewGuid(), cancellationToken);
    }

    /// <summary>
    /// Tells the store the scheduler is running, without starting it.
    /// </summary>
    /// <remarks>
    /// <c>AdoJobStoreBase.ResumeTrigger</c> applies a resumed trigger's misfire policy only when
    /// <c>schedulerRunning</c> is set, and the only two things that set it are <c>SchedulerStarted</c>
    /// — which also spawns the misfire handler loop these tests exist to keep out — and
    /// <c>SchedulerResumed</c>, which sets the flag and nothing else. The in-memory store has no such
    /// condition, so without this the two stores would differ for a reason that belongs to the test
    /// rather than to either store.
    /// </remarks>
    public ValueTask MarkSchedulerRunning() => store.SchedulerResumed();
}
