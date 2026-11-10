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

using Npgsql;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

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
/// writing transaction, so the read after it sees the commit. The unit tests stand in for the race on
/// SQLite, which settles every commit before it returns; this is the database's own row lock.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
[Category("db-postgres")]
public sealed class CommitCheckInFlightPostgresTest
{
    private const string SchedulerName = "CommitCheckInFlightPostgresTest";
    private const string Group = "commit-check-in-flight";

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    private string connectionString;
    private LateCommitDbProvider provider;
    private LocalTransactionJobStore store;

    [SetUp]
    public async Task BuildStore()
    {
        connectionString = Environment.GetEnvironmentVariable("PG_CONNECTION_STRING");
        connectionString.Should().NotBeNullOrWhiteSpace(
            "PG_CONNECTION_STRING is set by the container this assembly starts; run the fixture through the db-postgres leg");

        provider = new LateCommitDbProvider(new DbProvider(DataSourceOptions.Providers.Npgsql, connectionString));
        store = new LocalTransactionJobStore(TestJobStores.Dependencies(
            schedulerOptions: TestJobStores.SchedulerOptions(SchedulerName, "node"),
            storeOptions: TestJobStores.StoreOptions("commit-check-in-flight"),
            dbProvider: provider,
            driverDelegate: new PostgreSQLDelegate()));

        await store.Initialize(TestJobStores.Identity(SchedulerName, "node"));
        await store.Clear();
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        if (store is not null)
        {
            await provider.LateCommit;
            await store.Clear();
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
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(Request(maxCount: 1));
        acquired.Should().ContainSingle().Which.Key.Should().Be(due, "the premise: the trigger due now is the one acquired");

        provider.CommitNextLate();
        List<TriggerFiredResult> fired = await store.TriggersFired(acquired).AsTask().WaitAsync(patience);

        TriggerFiredBundle bundle = fired.Should().ContainSingle().Subject.TriggerFiredBundle;
        bundle.Should().NotBeNull("the fire landed, so the firing runs rather than its occurrence being released");
        await provider.LateCommit;
        (await FiredStates()).Should().Equal(["EXECUTING"]);
        (await StoredState(sibling)).Should().Be("BLOCKED", "the premise: the fire that landed holds the serial job's other trigger back");

        await Complete(bundle);

        (await FiredStates()).Should().BeEmpty();
        (await StoredState(sibling)).Should().Be("WAITING", "the firing's end lets go of the job's other trigger");
    }

    /// <summary>
    /// A folded fire-on-acquire round, under the lock for its serial job, whose commit settles after the client
    /// heard it fail. Its fired row was inserted, which nothing waits on, so the check waits on the row of the
    /// trigger the round claimed, and then finds the round. Its firing is handed over and runs. A check that
    /// only read found no row: the round threw, nothing released it, and its <c>EXECUTING</c> row and the
    /// serial job's <c>BLOCKED</c> trigger stayed until the node restarted.
    /// </summary>
    [Test]
    public async Task AFireOnAcquireRoundStillSettlingWhenCheckedHandsItsFiringOver()
    {
        (TriggerKey due, TriggerKey sibling) = await GivenASerialJobDueNow();

        // More than one, so that the round is folded rather than acquired and fired as two transactions.
        provider.CommitNextLate();
        TriggerAcquisitionResult round = await store.AcquireNextTriggersAndFireDue(Request(maxCount: 2)).AsTask().WaitAsync(patience);

        round.LockReason.Should().Be(RoundLockReason.SerialJob.ToTag(), "the premise: the round is folded, under the lock its serial job takes");
        TriggerFiredBundle bundle = round.Fired.Should().ContainSingle().Subject.TriggerFiredBundle;
        bundle.Should().NotBeNull("the round landed, so its firing runs rather than being left behind");
        bundle.Trigger.Key.Should().Be(due);
        await provider.LateCommit;
        (await FiredStates()).Should().Equal(["EXECUTING"]);
        (await StoredState(sibling)).Should().Be("BLOCKED", "the premise: the round that landed holds the serial job's other trigger back");

        await Complete(bundle);

        (await FiredStates()).Should().BeEmpty("the firing's end deletes its row");
        (await StoredState(sibling)).Should().Be("WAITING", "the firing's end lets go of the job's other trigger");
    }

    private static TriggerAcquisitionRequest Request(int maxCount)
    {
        return new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow.AddSeconds(1),
            MaxCount = maxCount,
            TimeWindow = TimeSpan.Zero,
        };
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

        await store.ScheduleJobs(
            new Dictionary<IJobDetail, IReadOnlyCollection<IOperableTrigger>> { [job] = [due, later] },
            ScheduleJobOptions.Replacing);

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

        trigger.ComputeFirstFireTimeUtc(calendar: null);
        return trigger;
    }

    private Task Complete(TriggerFiredBundle bundle)
    {
        return store.FiringComplete(new TriggeredJobCompleteContext
        {
            Trigger = bundle.Trigger,
            JobDetail = bundle.JobDetail,
            Instruction = SchedulerInstruction.NoInstruction,
            Outcome = ExecutionOutcome.Succeeded,
        }).AsTask().WaitAsync(patience);
    }

    private async Task<string> StoredState(TriggerKey key)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(
            "SELECT trigger_state FROM qrtz_triggers WHERE sched_name = @scheduler AND trigger_name = @name AND trigger_group = @group",
            connection);
        command.Parameters.AddWithValue("scheduler", SchedulerName);
        command.Parameters.AddWithValue("name", key.Name);
        command.Parameters.AddWithValue("group", key.Group);
        return (string) await command.ExecuteScalarAsync();
    }

    private async Task<List<string>> FiredStates()
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new("SELECT state FROM qrtz_fired_triggers WHERE sched_name = @scheduler", connection);
        command.Parameters.AddWithValue("scheduler", SchedulerName);

        List<string> states = [];
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            states.Add(reader.GetString(0));
        }

        return states;
    }

    /// <summary>Never executed: these tests drive the store, not a scheduler.</summary>
    [DisallowConcurrentExecution]
    public sealed class SerialJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
