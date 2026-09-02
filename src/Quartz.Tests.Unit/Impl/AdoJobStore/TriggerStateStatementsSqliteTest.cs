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
using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Jobs;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The seven <see cref="IDriverDelegate" /> members that write or read a trigger's state, run against a
/// real database rather than a faked command.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these binds a <see cref="StoredTriggerState" /> through
/// <see cref="StoredTriggerStates.ToStoredValue" /> and then compares it against a column, so what they
/// are worth checking for is exactly what a fake cannot show: that the string bound is the string
/// stored, and that the <c>WHERE</c> clause therefore matches the rows it means to. A test over a stub
/// command asserts the parameter was bound and learns nothing about whether the update hit anything.
/// </para>
/// <para>
/// SQLite is a file, so a whole database costs a temporary path — which is what lets these be unit
/// tests rather than a container leg. The statements are the standard ones;
/// <c>SQLiteDelegate</c> overrides only row limiting and paging, neither of which appears here.
/// </para>
/// </remarks>
public sealed class TriggerStateStatementsSqliteTest
{
    private const string Group = "state-statements";

    private static readonly JobKey jobKey = new("job", Group);
    private static readonly TriggerKey triggerKey = new("trigger", Group);

    private string databaseFile = null!;
    private string connectionString = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-state-statements-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";
    }

    [TearDown]
    public void DeleteDatabase()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(databaseFile))
        {
            File.Delete(databaseFile);
        }
    }

    [Test]
    public async Task AStateWrittenByKeyIsTheStateReadBack()
    {
        await using Harness harness = await Harness.Create(connectionString);

        (await harness.Delegate.UpdateTriggerState(harness.Connection, triggerKey, StoredTriggerState.Paused))
            .Should().Be(1, "the trigger is there, so the update names a row that exists");

        (await harness.StoredState()).Should().Be(AdoConstants.StatePaused,
            "the value the enum maps to is the value the column holds — the whole point of binding the "
            + "state through one mapping rather than spelling the string at each statement");
    }

    [Test]
    public async Task AConditionalStateChangeAppliesOnlyFromTheStateItNames()
    {
        await using Harness harness = await Harness.Create(connectionString);

        (await harness.Delegate.UpdateTriggerStateFromOtherState(
                harness.Connection, triggerKey, StoredTriggerState.Acquired, StoredTriggerState.Blocked))
            .Should().Be(0, "the trigger is WAITING, so a change conditioned on BLOCKED must not apply");

        (await harness.Delegate.UpdateTriggerStateFromOtherState(
                harness.Connection, triggerKey, StoredTriggerState.Acquired, StoredTriggerState.Waiting))
            .Should().Be(1);

        (await harness.StoredState()).Should().Be(AdoConstants.StateAcquired);
    }

    [Test]
    public async Task AJobsTriggersAreMovedByKeyAndConditionally()
    {
        await using Harness harness = await Harness.Create(connectionString);

        (await harness.Delegate.UpdateTriggerStatesForJob(harness.Connection, jobKey, StoredTriggerState.Blocked))
            .Should().Be(1, "the job has one trigger, and it is named by the job rather than by its own key");

        (await harness.Delegate.UpdateTriggerStatesForJobFromOtherState(
                harness.Connection, jobKey, StoredTriggerState.Waiting, StoredTriggerState.Paused))
            .Should().Be(0, "the trigger is BLOCKED, so a change conditioned on PAUSED must not apply");

        (await harness.Delegate.UpdateTriggerStatesForJobFromOtherState(
                harness.Connection, jobKey, StoredTriggerState.Waiting, StoredTriggerState.Blocked))
            .Should().Be(1);

        (await harness.StoredState()).Should().Be(AdoConstants.StateWaiting);
    }

    [Test]
    public async Task MisfiredTriggersAreCountedInTheStateTheyAreIn()
    {
        await using Harness harness = await Harness.Create(connectionString);

        DateTimeOffset afterTheTrigger = DateTimeOffset.UtcNow.AddDays(2);

        (await harness.Delegate.CountMisfiredTriggersInState(
                harness.Connection, StoredTriggerState.Waiting, afterTheTrigger))
            .Should().Be(1, "the trigger is waiting and its next fire time is behind the misfire cutoff");

        (await harness.Delegate.CountMisfiredTriggersInState(
                harness.Connection, StoredTriggerState.Paused, afterTheTrigger))
            .Should().Be(0, "the count is per state, so a state no row is in counts nothing");

        (await harness.Delegate.CountMisfiredTriggersInState(
                harness.Connection, StoredTriggerState.Waiting, DateTimeOffset.UtcNow.AddDays(-2)))
            .Should().Be(0, "a cutoff before the trigger's next fire time means it has not misfired");
    }

    [Test]
    public async Task AJobWithNoFiredTriggerRowIsNotExecuting()
    {
        await using Harness harness = await Harness.Create(connectionString);

        (await harness.Delegate.IsJobCurrentlyExecuting(harness.Connection, jobKey)).Should().BeFalse(
            "nothing has fired, so the EXECUTING count over FIRED_TRIGGERS is zero — and the state this "
            + "compares against is bound through the same mapping the fire path writes with");
    }

    [Test]
    public async Task ARetryWritesTheAttemptTheNextFireTimeAndTheState()
    {
        await using Harness harness = await Harness.Create(connectionString);

        IOperableTrigger trigger = (await harness.Delegate.SelectTrigger(harness.Connection, triggerKey))!;

        trigger.Should().NotBeNull();
        trigger.RetryAttempt = 2;
        trigger.NextFireTimeUtc = DateTimeOffset.UtcNow.AddMinutes(5);

        (await harness.Delegate.UpdateTriggerForRetry(harness.Connection, trigger, StoredTriggerState.Waiting))
            .Should().Be(1);

        (await harness.StoredState()).Should().Be(AdoConstants.StateWaiting);
        (await harness.Scalar($"SELECT {AdoConstants.ColumnRetryAttempt} FROM QRTZ_TRIGGERS")).Should().Be(2,
            "the retry statement is the one place the attempt counter is written");
    }

    /// <summary>
    /// The acquisition claim is <c>virtual</c>, so a dialect's own version of it is the one that runs —
    /// and the base's is still there to build on.
    /// </summary>
    /// <remarks>
    /// It was the one <see cref="IDriverDelegate" /> member <see cref="StdAdoDelegate" /> implemented
    /// without <c>virtual</c>, so a dialect that wanted to reshape it could only hide it with
    /// <c>new</c> — and the store calls the delegate through the interface, so the hidden member would
    /// never have run. This drives it through <see cref="IDriverDelegate" />, which is how the store
    /// reaches it, and checks both halves: the override ran, and the row it delegated to the base to
    /// write did move.
    /// </remarks>
    [Test]
    public async Task ADialectsOwnClaimOnAnAcquiredTriggerIsTheOneTheStoreWouldReach()
    {
        await using Harness harness = await Harness.Create(
            connectionString,
            store => store.UseDriverDelegate<ClaimRecordingDelegate>());

        ClaimRecordingDelegate dialect = harness.Delegate.Should().BeOfType<ClaimRecordingDelegate>(
            "UseDriverDelegate runs before UseSqlite, and registration is first-wins").Subject;

        // The fire time is the claim's optimistic-concurrency guard: the row moves only while it is
        // still due at exactly the moment the acquiring node read.
        DateTimeOffset dueAt = new(await harness.Scalar($"SELECT {AdoConstants.ColumnNextFireTime} FROM QRTZ_TRIGGERS"), TimeSpan.Zero);

        (await harness.Delegate.UpdateTriggerStateFromOtherStateWithNextFireTime(
                harness.Connection, triggerKey, StoredTriggerState.Acquired, StoredTriggerState.Waiting, dueAt.AddMinutes(1)))
            .Should().Be(0, "another node moved the trigger on, so this claim is stale and applies to nothing");

        (await harness.Delegate.UpdateTriggerStateFromOtherStateWithNextFireTime(
                harness.Connection, triggerKey, StoredTriggerState.Acquired, StoredTriggerState.Waiting, dueAt))
            .Should().Be(1, "the trigger is WAITING and still due when it was read, so the claim applies");

        dialect.Claims.Should().Be(2,
            "the store issues this statement through IDriverDelegate, so a dialect's override only runs "
            + "if the member it overrides is virtual");

        (await harness.StoredState()).Should().Be(AdoConstants.StateAcquired,
            "the override called the base, and the base still issued the statement");
    }

    /// <summary>
    /// A dialect that counts its claims and otherwise leaves the statement alone — the shape of an
    /// override that adds an index hint or a retry, which is why the member is a seam.
    /// </summary>
    private sealed class ClaimRecordingDelegate : SQLiteDelegate
    {
        public int Claims { get; private set; }

        public override ValueTask<int> UpdateTriggerStateFromOtherStateWithNextFireTime(
            ConnectionAndTransactionHolder conn,
            TriggerKey triggerKey,
            StoredTriggerState newState,
            StoredTriggerState oldState,
            DateTimeOffset nextFireTime,
            CancellationToken cancellationToken = default)
        {
            Claims++;
            return base.UpdateTriggerStateFromOtherStateWithNextFireTime(
                conn, triggerKey, newState, oldState, nextFireTime, cancellationToken);
        }
    }

    /// <summary>
    /// A provisioned database with one job and one waiting trigger in it, plus the delegate and the
    /// connection the tests drive.
    /// </summary>
    /// <remarks>
    /// The scheduler is built only to create the schema and write the two rows, which is the shortest
    /// way to a database whose contents the store itself considers valid. Everything under test then
    /// happens against the delegate directly, because these members are below the store and a store
    /// call would not reach several of them at all.
    /// </remarks>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider container;
        private readonly DbConnection connection;

        private Harness(ServiceProvider container, DbConnection connection, IDriverDelegate driverDelegate)
        {
            this.container = container;
            this.connection = connection;
            Delegate = driverDelegate;
            Connection = new ConnectionAndTransactionHolder(connection, null);
        }

        public IDriverDelegate Delegate { get; }

        public ConnectionAndTransactionHolder Connection { get; }

        public static async Task<Harness> Create(
            string connectionString,
            Action<IPersistentStoreBuilder>? configureStore = null)
        {
            ServiceCollection services = new();
            services.AddQuartz(q =>
            {
                q.ConfigureScheduler(options =>
                {
                    options.InstanceName = "state-statements";
                    options.InstanceId = "one";
                });

                q.UsePersistentStore(store =>
                {
                    // Before UseSqlite, because registration is first-wins and UseSqlite names a
                    // driver delegate of its own.
                    configureStore?.Invoke(store);

                    store.UseSqlite(SqliteFactory.Instance, connectionString);
                    store.ProvisionSchema();
                });
            });

            ServiceProvider container = services.BuildServiceProvider();
            IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

            await scheduler.ScheduleJob(
                JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).Build(),
                TriggerBuilder.Create()
                    .WithIdentity(triggerKey)
                    .StartAt(DateTimeOffset.UtcNow.AddDays(1))
                    .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                    .Build());

            // Never started, so nothing acquires the trigger out from under the statements below.
            await scheduler.Shutdown(waitForJobsToComplete: false);

            DbConnection connection = container.GetRequiredService<IDbProvider>().CreateConnection();
            await connection.OpenAsync();

            return new Harness(container, connection, container.GetRequiredService<IDriverDelegate>());
        }

        public Task<string?> StoredState()
        {
            return Read($"SELECT {AdoConstants.ColumnTriggerState} FROM QRTZ_TRIGGERS", value => (string?) value);
        }

        public Task<long> Scalar(string sql)
        {
            return Read(sql, Convert.ToInt64);
        }

        private async Task<T> Read<T>(string sql, Func<object?, T> convert)
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;

            return convert(await command.ExecuteScalarAsync());
        }

        public async ValueTask DisposeAsync()
        {
            await connection.DisposeAsync();
            await container.DisposeAsync();
        }
    }
}
