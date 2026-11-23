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
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Time.Testing;

using Quartz.Diagnostics;
using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Diagnostics;

/// <summary>
/// The due-trigger gauges (#4401): the ADO.NET store counts its due backlog in the misfire pass, and the
/// gauges report that count per execution group.
/// </summary>
/// <remarks>
/// Driven against the store on a clock the test owns, initialized but never started, so no misfire loop
/// races an assertion. Every due time is inside the misfire threshold, so the pass re-arms nothing.
/// </remarks>
public sealed class DueTriggerGaugeSqliteTest
{
    private const string Group = "due";
    private const string SchedulerName = "DueTriggerGaugeSqliteTest";
    private const string SchedulerId = "node-a";
    private const string DataSourceName = "due-trigger-gauges";
    private const string Ungrouped = "(none)";

    private static readonly TimeSpan frequency = TimeSpan.FromSeconds(30);

    /// <summary>On the hour, UTC, far from any machine's own clock.</summary>
    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    private SqliteTestDatabase database = null!;
    private IDbProvider dbProvider = null!;
    private FakeTimeProvider clock = null!;
    private CountingSqliteDelegate statements = null!;
    private Meters meters = null!;
    private LocalTransactionJobStore store = null!;
    private bool storeShutDown;
    private MetricCollector<long>? due;
    private MetricCollector<double>? dueAge;

    [OneTimeSetUp]
    public async Task CreateDatabase()
    {
        database = new SqliteTestDatabase("due-trigger-gauges");

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
        statements = new CountingSqliteDelegate();
        meters = new Meters(meterFactory: null);
        store = new LocalTransactionJobStore(TestJobStores.Dependencies(
            timeProvider: clock,
            schedulerOptions: TestJobStores.SchedulerOptions(instanceName: SchedulerName, instanceId: SchedulerId),
            storeOptions: TestJobStores.StoreOptions(DataSourceName, configure: x => x.MisfireHandlerFrequency = frequency),
            dbProvider: dbProvider,
            driverDelegate: statements));

        store.Meters = meters;
        storeShutDown = false;

        await store.Initialize(TestJobStores.Identity(instanceName: SchedulerName, instanceId: SchedulerId));
        await store.Clear();
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        due?.Dispose();
        dueAge?.Dispose();
        due = null;
        dueAge = null;

        if (!storeShutDown)
        {
            await store.Shutdown();
        }

        meters.Dispose();
    }

    [Test]
    public async Task TheBacklogIsCountedPerExecutionGroupWithTheAgeOfItsEarliestTrigger()
    {
        await ScheduleDue("a1", "a", TimeSpan.FromSeconds(20));
        await ScheduleDue("a2", "a", TimeSpan.FromSeconds(40));
        await ScheduleDue("b1", "b", TimeSpan.FromSeconds(10));
        await ScheduleDue("u1", executionGroup: null, TimeSpan.FromSeconds(30));
        await ScheduleDue("later", "a", TimeSpan.FromSeconds(-60));
        Listen();

        await store.RecoverMisfires(Guid.NewGuid());

        IReadOnlyList<CollectedMeasurement<long>> counts = CollectDue();
        ByGroup(counts).Should().BeEquivalentTo(new Dictionary<string, long>
        {
            ["a"] = 2,
            ["b"] = 1,
            [Ungrouped] = 1,
        }, "a trigger whose fire time is still ahead is not due");

        ByGroup(CollectDueAge()).Should().BeEquivalentTo(new Dictionary<string, double>
        {
            ["a"] = 40,
            ["b"] = 10,
            [Ungrouped] = 30,
        }, "each group's age is that of its earliest due trigger");

        counts.Should().AllSatisfy(x =>
        {
            x.Tags.Should().Contain(ActivityTags.SchedulerName, SchedulerName);
            x.Tags.Should().Contain(ActivityTags.SchedulerId, SchedulerId);
        });

        counts.Should().ContainSingle(x => !x.Tags.ContainsKey(ActivityTags.ExecutionGroup),
            "a trigger in no execution group carries no such attribute, rather than an empty one");
    }

    [Test]
    public async Task PausedAcquiredAndBlockedTriggersAreNotBacklog()
    {
        await ScheduleDue("waiting", executionGroup: null, TimeSpan.FromSeconds(10));
        await SetState(await ScheduleDue("paused", executionGroup: null, TimeSpan.FromSeconds(10)), "PAUSED");
        await SetState(await ScheduleDue("acquired", executionGroup: null, TimeSpan.FromSeconds(10)), "ACQUIRED");
        await SetState(await ScheduleDue("blocked", executionGroup: null, TimeSpan.FromSeconds(10)), "BLOCKED");
        Listen();

        await store.RecoverMisfires(Guid.NewGuid());

        CollectDue().Should().ContainSingle().Which.Value.Should().Be(1,
            "a paused or blocked trigger waits on purpose, and an acquired one is about to fire");
    }

    [Test]
    public async Task NothingIsQueriedWhileNothingListens()
    {
        await ScheduleDue("t", executionGroup: null, TimeSpan.FromSeconds(10));

        await store.RecoverMisfires(Guid.NewGuid());
        statements.DueQueries.Should().Be(0, "an application that does not collect the gauges never pays for the query");

        Listen();
        await store.RecoverMisfires(Guid.NewGuid());
        statements.DueQueries.Should().Be(1, "the query starts with the first collector");
    }

    [Test]
    public async Task TheBacklogIsCountedAtMostOncePerMisfireHandlerInterval()
    {
        await ScheduleDue("t", executionGroup: null, TimeSpan.FromSeconds(10));
        Listen();

        await store.RecoverMisfires(Guid.NewGuid());
        clock.Advance(TimeSpan.FromSeconds(10));
        await store.RecoverMisfires(Guid.NewGuid());
        statements.DueQueries.Should().Be(1, "a pass that reruns within the interval, as one with misfires left does after 50 ms, reuses the count");

        clock.Advance(frequency);
        await store.RecoverMisfires(Guid.NewGuid());
        statements.DueQueries.Should().Be(2);
    }

    [Test]
    public async Task AStaleCountIsNotReported()
    {
        await ScheduleDue("t", executionGroup: null, TimeSpan.FromSeconds(10));
        Listen();

        await store.RecoverMisfires(Guid.NewGuid());
        clock.Advance(frequency * 2);
        CollectDue().Should().ContainSingle("a count two intervals old is still current");

        clock.Advance(TimeSpan.FromSeconds(1));
        CollectDue().Should().BeEmpty("a frozen last value would hide the stalled store the gauge exists to show");
        CollectDueAge().Should().BeEmpty();
    }

    [Test]
    public async Task AGroupWhoseBacklogClearedReportsZero()
    {
        TriggerKey key = await ScheduleDue("t", "a", TimeSpan.FromSeconds(10));
        Listen();

        await store.RecoverMisfires(Guid.NewGuid());
        ByGroup(CollectDue()).Should().Equal(new Dictionary<string, long> { ["a"] = 1 });

        await store.DeleteTrigger(key);
        clock.Advance(frequency);
        await store.RecoverMisfires(Guid.NewGuid());

        ByGroup(CollectDue()).Should().Equal(new Dictionary<string, long> { ["a"] = 0 },
            "the series drops to zero rather than vanishing, so missing data only ever means no current count");
        ByGroup(CollectDueAge()).Should().Equal(new Dictionary<string, double> { ["a"] = 0 });
    }

    [Test]
    public async Task AStoreThatShutDownReportsNothing()
    {
        await ScheduleDue("t", executionGroup: null, TimeSpan.FromSeconds(10));
        Listen();

        await store.RecoverMisfires(Guid.NewGuid());
        CollectDue().Should().ContainSingle();

        await store.Shutdown();
        storeShutDown = true;

        CollectDue().Should().BeEmpty("a store that has shut down no longer reports its last count");
    }

    private void Listen()
    {
        due = new MetricCollector<long>(meters.Meter, QuartzInstrumentation.Instruments.TriggerDue, clock);
        dueAge = new MetricCollector<double>(meters.Meter, QuartzInstrumentation.Instruments.TriggerDueAge, clock);
    }

    private IReadOnlyList<CollectedMeasurement<long>> CollectDue()
    {
        due!.RecordObservableInstruments();
        return due.GetMeasurementSnapshot(clear: true);
    }

    private IReadOnlyList<CollectedMeasurement<double>> CollectDueAge()
    {
        dueAge!.RecordObservableInstruments();
        return dueAge.GetMeasurementSnapshot(clear: true);
    }

    private static Dictionary<string, T> ByGroup<T>(IReadOnlyList<CollectedMeasurement<T>> measurements) where T : struct =>
        measurements.ToDictionary(
            x => x.Tags.TryGetValue(ActivityTags.ExecutionGroup, out object? group) ? (string) group! : Ungrouped,
            x => x.Value);

    private async Task<TriggerKey> ScheduleDue(string name, string? executionGroup, TimeSpan dueAgo)
    {
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity(name, Group).Build();
        TriggerBuilder<IJob> builder = TriggerBuilder.Create(clock)
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(clock.GetUtcNow() - dueAgo);

        if (executionGroup is not null)
        {
            builder = builder.WithExecutionGroup(executionGroup);
        }

        IOperableTrigger trigger = (IOperableTrigger) builder.Build();
        trigger.ComputeFirstFireTimeUtc(calendar: null);
        await store.ScheduleJob(job, trigger);
        return trigger.Key;
    }

    /// <summary>Written directly, so the trigger keeps its due fire time in the state under test.</summary>
    private async Task SetState(TriggerKey key, string state)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE QRTZ_TRIGGERS SET TRIGGER_STATE = @state WHERE TRIGGER_NAME = @name AND TRIGGER_GROUP = @group";
        command.Parameters.AddWithValue("@state", state);
        command.Parameters.AddWithValue("@name", key.Name);
        command.Parameters.AddWithValue("@group", key.Group);
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
    }

    private static string LoadSqliteTableScript()
    {
        string path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        return File.ReadAllText(path);
    }

    /// <summary>The shipped SQLite delegate, counting the due-backlog queries.</summary>
    private sealed class CountingSqliteDelegate : SQLiteDelegate
    {
        private int dueQueries;

        public int DueQueries => Volatile.Read(ref dueQueries);

        internal override ValueTask<List<DueTriggers>> SelectDueTriggersByExecutionGroup(
            ConnectionAndTransactionHolder conn,
            DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref dueQueries);
            return base.SelectDueTriggersByExecutionGroup(conn, now, cancellationToken);
        }
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
