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

using System.Data.Common;

using Npgsql;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A fire-on-acquire round on PostgreSQL, where the shipped delegate sends its claims and fire writes as
/// batches (#3864): one lock, every due trigger fired once, and a fire that fails inside a batch found and
/// failed alone (#3931).
/// </summary>
/// <remarks>
/// The failure is the database's own: a trigger on the fired-trigger table refuses the one row, so the
/// shipped delegate is exercised as it ships, which no subclass injecting a fault could do — a subclass is
/// never batched. SQLite, the only database the unit tests reach, does not batch at all.
/// </remarks>
[TestFixture]
[NonParallelizable]
[Category("db-postgres")]
public sealed class FireOnAcquireBatchPostgresTest
{
    private const string SchedulerName = "FireOnAcquireBatchPostgresTest";
    private const string Group = "fire-on-acquire-batch";
    private const string Poison = "fire-on-acquire-poison";

    private const string CreatePoison =
        "CREATE OR REPLACE FUNCTION qrtz_fire_on_acquire_poison() RETURNS trigger AS $$ BEGIN "
        + "IF NEW.trigger_name = '" + Poison + "' AND NEW.state = 'EXECUTING' THEN RAISE EXCEPTION 'the poison fire is refused'; END IF; "
        + "RETURN NEW; END $$ LANGUAGE plpgsql; "
        + "DROP TRIGGER IF EXISTS qrtz_fire_on_acquire_poison ON qrtz_fired_triggers; "
        + "CREATE TRIGGER qrtz_fire_on_acquire_poison BEFORE INSERT OR UPDATE ON qrtz_fired_triggers "
        + "FOR EACH ROW EXECUTE FUNCTION qrtz_fire_on_acquire_poison();";

    private const string DropPoison =
        "DROP TRIGGER IF EXISTS qrtz_fire_on_acquire_poison ON qrtz_fired_triggers; "
        + "DROP FUNCTION IF EXISTS qrtz_fire_on_acquire_poison();";

    private string connectionString;
    private CountingLockHandler locks;
    private LocalTransactionJobStore store;

    [SetUp]
    public async Task BuildStore()
    {
        connectionString = Environment.GetEnvironmentVariable("PG_CONNECTION_STRING");
        connectionString.Should().NotBeNullOrWhiteSpace(
            "PG_CONNECTION_STRING is set by the container this assembly starts; run the fixture through the db-postgres leg");

        locks = new CountingLockHandler();
        store = new LocalTransactionJobStore(TestJobStores.Dependencies(
            schedulerOptions: TestJobStores.SchedulerOptions(SchedulerName, "node"),
            storeOptions: TestJobStores.StoreOptions("fire-on-acquire-batch"),
            dbProvider: new DbProvider(DataSourceOptions.Providers.Npgsql, connectionString),
            driverDelegate: new PostgreSQLDelegate(),
            lockHandler: locks));

        await store.Initialize(new SchedulerIdentity { SchedulerName = SchedulerName, InstanceId = "node" });
        await store.Clear();
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        await Execute(DropPoison);
        await store.Clear();
        await store.Shutdown();
    }

    /// <summary>
    /// Six due triggers are claimed and fired under one lock, their rows written as the shipped delegate
    /// batches them.
    /// </summary>
    [Test]
    public async Task ARoundFiresItsDueTriggersUnderOneLock()
    {
        await ScheduleOneOffs("due-", 6);
        locks.Reset();

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 6);

        round.Due.Should().HaveCount(6);
        round.Fired.Should().OnlyContain(x => x.TriggerFiredBundle != null);
        locks.TriggerAccess.Should().Be(1, "the round is one transaction under one lock");
        (await FireInstances(FireInstanceState.Executing)).Should().HaveCount(6, "each fire wrote its row as EXECUTING");
        (await FireInstances(FireInstanceState.Acquired)).Should().BeEmpty("nothing is left reserved");
    }

    /// <summary>
    /// A fire the database refuses inside a batch fails the batch whole. The round is rolled back, runs
    /// again one trigger at a time to find the fire that failed, and commits without it: the rest fire once,
    /// and the refused trigger is reserved and answered failed, for the scheduler to release (#3931).
    /// </summary>
    [Test]
    public async Task AFireRefusedInsideABatchIsFoundAndTheRestFireOnce()
    {
        await Schedule("ordinary-1", priority: 10);
        await Schedule(Poison, priority: 9);
        await Schedule("ordinary-2", priority: 8);
        await Execute(CreatePoison);
        locks.Reset();

        TriggerAcquisitionResult round = await AcquireAndFire(maxCount: 3);

        round.Due.Select(x => x.Key.Name).Should().Equal(["ordinary-1", Poison, "ordinary-2"]);
        round.Fired[0].TriggerFiredBundle.Should().NotBeNull();
        round.Fired[1].TriggerFiredBundle.Should().BeNull();
        round.Fired[1].Exception.Should().BeOfType<JobPersistenceException>()
            .Which.InnerException.Should().BeAssignableTo<DbException>("the database's refusal, which the scheduler releases the trigger on");
        round.Fired[2].TriggerFiredBundle.Should().NotBeNull();

        locks.TriggerAccess.Should().Be(3,
            "the batch that failed, the run one trigger at a time that found the refused fire, and the run that committed without it");

        List<FireInstance> executing = await FireInstances(FireInstanceState.Executing);
        executing.Select(x => x.TriggerKey.Name).Should().BeEquivalentTo(["ordinary-1", "ordinary-2"],
            "each fired once: the attempts before were rolled back whole");
        (await FireInstances(FireInstanceState.Acquired)).Select(x => x.TriggerKey.Name).Should().Equal([Poison],
            "the refused trigger is reserved as acquisition reserves any it does not fire");

        await store.ReleaseAcquiredTrigger(round.Due[1]);
        (await FireInstances(FireInstanceState.Acquired)).Should().BeEmpty();
        (await store.GetTriggerState(new TriggerKey(Poison, Group))).Should().Be(TriggerState.Normal, "released, for the next round");
    }

    private ValueTask<TriggerAcquisitionResult> AcquireAndFire(int maxCount)
    {
        return store.AcquireNextTriggersAndFireDue(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow.AddMinutes(1),
            MaxCount = maxCount,
            TimeWindow = TimeSpan.FromSeconds(5),
        });
    }

    private async Task ScheduleOneOffs(string prefix, int count)
    {
        for (int i = 0; i < count; i++)
        {
            await Schedule(prefix + i, TriggerConstants.DefaultPriority);
        }
    }

    private async Task Schedule(string name, int priority)
    {
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity(name, Group).Build();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(DateTimeOffset.UtcNow.AddMilliseconds(-100))
            .WithPriority(priority)
            .Build();

        trigger.ComputeFirstFireTimeUtc(calendar: null);
        await store.ScheduleJob(job, trigger);
    }

    private async Task<List<FireInstance>> FireInstances(FireInstanceState state)
    {
        PagedResult<FireInstance> instances = await store.QueryFireInstances(new FireInstanceQuery { State = state, Take = PagedQuery.All });
        return [.. instances.Items];
    }

    private async Task Execute(string sql)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>The in-process lock, counting how often <see cref="SchedulerLock.TriggerAccess" /> is taken.</summary>
    private sealed class CountingLockHandler : ILockHandler
    {
        private readonly InProcessLockHandler inner = new();
        private int triggerAccess;

        public int TriggerAccess => Volatile.Read(ref triggerAccess);

        public void Reset() => Interlocked.Exchange(ref triggerAccess, 0);

        public ValueTask<bool> AcquireLock(Guid requestorId, ConnectionAndTransactionHolder conn, SchedulerLock lockKind, CancellationToken cancellationToken = default)
        {
            if (lockKind == SchedulerLock.TriggerAccess)
            {
                Interlocked.Increment(ref triggerAccess);
            }

            return inner.AcquireLock(requestorId, conn, lockKind, cancellationToken);
        }

        public ValueTask ReleaseLock(Guid requestorId, SchedulerLock lockKind, CancellationToken cancellationToken = default)
        {
            return inner.ReleaseLock(requestorId, lockKind, cancellationToken);
        }

        public bool RequiresConnection => false;
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
