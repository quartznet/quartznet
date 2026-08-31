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

        public static async Task<Harness> Create(string connectionString)
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
