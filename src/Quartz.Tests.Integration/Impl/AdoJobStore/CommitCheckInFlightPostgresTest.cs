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
using System.Threading.Tasks;

using FakeItEasy;

using Npgsql;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A fire whose commit reaches PostgreSQL late, after the client was told it failed, so the store's check of
/// whether it landed starts while the commit is still settling (#4259).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LateCommitDbProvider" /> stands in for the server: the transaction stays open, holding its row
/// locks, for half a second after the commit failed. The check runs on a connection of its own under a lock
/// its requestor already holds in memory, so no database lock stands between it and that commit. A check
/// that only reads sees the fired rows as they were before the fire.
/// </para>
/// <para>
/// The check's first statement writes a trigger row the commit wrote, and PostgreSQL makes it wait on the
/// writing transaction, so the read after it sees the commit. <see cref="TriggersFiredBrokenCommitSqliteTest" />
/// stands in for the race on SQLite, which settles every commit before it returns; this is the database's own
/// row lock.
/// </para>
/// </remarks>
[NonParallelizable]
[Category("db-postgres")]
public sealed class CommitCheckInFlightPostgresTest
{
    private const string SchedulerName = "CommitCheckInFlightPostgresTest";
    private const string Group = "commit-check-in-flight";

    /// <summary>
    /// Bounds a store that waits for something, so that a regression fails the test instead of hanging it.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private string connectionString;
    private LateCommitDbProvider provider;
    private JobStoreTX store;

    [SetUp]
    public async Task BuildStore()
    {
        connectionString = TestConstants.PostgresConnectionString;
        provider = new LateCommitDbProvider(new DbProvider(TestConstants.PostgresProvider, connectionString));

        string dataSource = $"commit-check-in-flight-{Guid.NewGuid():N}";
        DBConnectionManager.Instance.AddConnectionProvider(dataSource, provider);

        SystemTextJsonObjectSerializer serializer = new SystemTextJsonObjectSerializer();
        serializer.Initialize();

        store = new JobStoreTX
        {
            DataSource = dataSource,
            TablePrefix = "QRTZ_",
            InstanceName = SchedulerName,
            InstanceId = "node",
            DriverDelegateType = typeof(PostgreSQLDelegate).AssemblyQualifiedName,
            ObjectSerializer = serializer,
        };

        // Initialized but deliberately not started, so no misfire pass or recovery moves the rows read here.
        await store.Initialize(new SimpleTypeLoadHelper(), A.Fake<ISchedulerSignaler>());
        await store.ClearAllSchedulingData();
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        if (store != null)
        {
            await provider.LateCommit;
            await store.ClearAllSchedulingData();
            await store.Shutdown();
            store = null;
        }
    }

    /// <summary>
    /// The scheduler thread fires an acquired trigger of a serial job, and the commit settles after the client
    /// heard it fail. The check waits for it and finds the fire, so the firing goes ahead, and its end lets go
    /// of the job's other trigger. A check that only read found nothing: the store threw, and the scheduler
    /// released the fire and lost its occurrence.
    /// </summary>
    [Test]
    public async Task AFireStillSettlingWhenCheckedIsWaitedForAndGoesAhead()
    {
        (TriggerKey due, TriggerKey sibling) = await GivenASerialJobDueNow();
        IReadOnlyCollection<IOperableTrigger> acquired = await store.AcquireNextTriggers(DateTimeOffset.UtcNow.AddSeconds(1), 1, TimeSpan.Zero);
        acquired.Should().ContainSingle().Which.Key.Should().Be(due, "the premise: the trigger due now is the one acquired");

        provider.CommitNextLate();
        IReadOnlyCollection<TriggerFiredResult> fired = await WithPatience(store.TriggersFired(acquired));

        TriggerFiredBundle bundle = fired.Should().ContainSingle().Subject.TriggerFiredBundle;
        bundle.Should().NotBeNull("the fire landed, so the firing runs rather than its occurrence being released");
        await provider.LateCommit;
        (await FiredStates()).Should().Equal(new[] { "EXECUTING" });
        (await StoredState(sibling)).Should().Be("BLOCKED", "the premise: the fire that landed holds the serial job's other trigger back");

        await WithPatience(store.TriggeredJobComplete(bundle.Trigger, bundle.JobDetail, SchedulerInstruction.NoInstruction));

        (await FiredStates()).Should().BeEmpty();
        (await StoredState(sibling)).Should().Be("WAITING", "the firing's end lets go of the job's other trigger");
    }

    /// <summary>
    /// Stores a job that disallows concurrent execution with a trigger due now and one due in an hour.
    /// </summary>
    private async Task<(TriggerKey Due, TriggerKey Sibling)> GivenASerialJobDueNow()
    {
        IJobDetail job = JobBuilder.Create<SerialJob>().WithIdentity("serial", Group).StoreDurably().Build();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IOperableTrigger due = OneShot("due", job, now.AddMilliseconds(-100));
        IOperableTrigger later = OneShot("later", job, now.AddHours(1));

        await store.StoreJob(job, replaceExisting: true);
        await store.StoreTrigger(due, replaceExisting: true);
        await store.StoreTrigger(later, replaceExisting: true);

        return (due.Key, later.Key);
    }

    private static IOperableTrigger OneShot(string name, IJobDetail job, DateTimeOffset startAt)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(startAt)
            .WithSimpleSchedule(x => x.WithRepeatCount(0))
            .Build();

        // What the scheduler does with a trigger before handing it to the store.
        trigger.ComputeFirstFireTimeUtc(null);
        return trigger;
    }

    private async Task<string> StoredState(TriggerKey key)
    {
        await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        using NpgsqlCommand command = new NpgsqlCommand(
            "SELECT trigger_state FROM qrtz_triggers WHERE sched_name = @scheduler AND trigger_name = @name AND trigger_group = @group",
            connection);
        command.Parameters.AddWithValue("scheduler", SchedulerName);
        command.Parameters.AddWithValue("name", key.Name);
        command.Parameters.AddWithValue("group", key.Group);
        return (string) await command.ExecuteScalarAsync();
    }

    private async Task<List<string>> FiredStates()
    {
        await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        using NpgsqlCommand command = new NpgsqlCommand("SELECT state FROM qrtz_fired_triggers WHERE sched_name = @scheduler", connection);
        command.Parameters.AddWithValue("scheduler", SchedulerName);

        List<string> states = new List<string>();
        using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            states.Add(reader.GetString(0));
        }

        return states;
    }

    private static async Task WithPatience(Task task)
    {
        Task first = await Task.WhenAny(task, Task.Delay(Patience));
        if (first != task)
        {
            throw new TimeoutException($"The store did not answer within {Patience}.");
        }

        await task;
    }

    private static async Task<T> WithPatience<T>(Task<T> task)
    {
        await WithPatience((Task) task);
        return await task;
    }

    /// <summary>Never executed: these tests drive the store, not a scheduler.</summary>
    [DisallowConcurrentExecution]
    public sealed class SerialJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
