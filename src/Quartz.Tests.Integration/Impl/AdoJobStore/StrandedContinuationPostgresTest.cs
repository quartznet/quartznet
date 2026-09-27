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

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The one race a completion without <c>TRIGGER_ACCESS</c> (#3863) opens, made to happen on PostgreSQL
/// and closed by the misfire pass: a continuation added to a running one-off parent after the parent's
/// completion has looked for continuations and before it has committed.
/// </summary>
/// <remarks>
/// <para>
/// Two stores over one database, as two threads or two nodes would be. The parent's completion runs on
/// the first; a seam in that store's delegate, fired once from its continuation scan, schedules the
/// children on the second store, which commits them while the completion is still between its scan
/// and its delete. Under read-committed isolation the completion never sees them and takes the parent
/// with it, which is the stranded state the sweep exists for. Under the lock, 4.2 made this
/// impossible; without the sweep it is a trigger that waits for ever.
/// </para>
/// <para>
/// SQLite cannot host the race — its single writer refuses the second transaction — so the unit test
/// for the sweep writes the stranded state directly, and this is where the race itself is shown.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
[Category("db-postgres")]
public sealed class StrandedContinuationPostgresTest
{
    private const string SchedulerName = "StrandedContinuationPostgresTest";
    private const string Group = "stranded";

    private RacingPostgreSqlDelegate seam;
    private CountingLockHandler locks;
    private LocalTransactionJobStore completing;
    private LocalTransactionJobStore adding;

    [SetUp]
    public async Task BuildStores()
    {
        string connectionString = ContainerConnectionString();

        seam = new RacingPostgreSqlDelegate();
        locks = new CountingLockHandler();
        completing = await BuildStore("node-a", seam, locks, connectionString);
        adding = await BuildStore("node-b", new PostgreSQLDelegate(), new InProcessLockHandler(), connectionString);

        await completing.Clear();
    }

    [TearDown]
    public async Task ShutDownStores()
    {
        await completing.Clear();
        await completing.Shutdown();
        await adding.Shutdown();
    }

    [Test]
    public async Task AContinuationAddedDuringTheParentsLockFreeCompletionIsSettledByTheMisfirePass()
    {
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("parent", Group).StoreDurably().Build();
        IOperableTrigger parent = OneOff("parent", job, DateTimeOffset.UtcNow);
        await completing.ScheduleJob(job, parent);

        IOperableTrigger firing = await Fire(parent.Key);

        TriggerKey indifferent = new("indifferent", Group);
        TriggerKey particular = new("particular", Group);
        seam.AfterContinuationScan = async () =>
        {
            // The other store, the other connection: committed before the completion writes a thing,
            // and invisible to the statements it has already run.
            await ScheduleContinuation(adding, indifferent, parent.Key, ContinuationCondition.OnAnyOutcome);
            await ScheduleContinuation(adding, particular, parent.Key, ContinuationCondition.OnSuccess);
        };

        await completing.FiringComplete(new TriggeredJobCompleteContext
        {
            Trigger = firing,
            JobDetail = job,
            Instruction = SchedulerInstruction.DeleteTrigger,
            Outcome = ExecutionOutcome.Succeeded
        });

        seam.AfterContinuationScan.Should().BeNull("the seam fired, from the completion's lock-free scan");
        (await completing.GetTrigger(parent.Key)).Should().BeNull("the parent is spent and its completion deleted it");
        (await completing.GetTriggerState(indifferent)).Should().Be(TriggerState.Awaiting,
            "the completion never saw the children, so they wait for a parent that is gone: the race, reproduced");
        (await completing.GetTriggerState(particular)).Should().Be(TriggerState.Awaiting);

        locks.Reset();
        RecoverMisfiredJobsResult pass = await completing.RecoverMisfires(Guid.NewGuid());

        pass.ProcessedMisfiredTriggerCount.Should().Be(2, "both settlements change the schedule, so the pass wakes the scheduler thread");
        locks.TriggerAccess.Should().Be(1, "the probe found the two rows without the lock, and the sweep took it once to settle them");

        (await completing.GetTriggerState(indifferent)).Should().Be(TriggerState.Normal,
            "'however it ends' is released, as it would have been by a deleted parent");
        (await completing.GetTriggerState(particular)).Should().Be(TriggerState.Error,
            "a condition nobody can answer any more parks the trigger for an operator, as a deleted parent does");

        locks.Reset();
        (await completing.RecoverMisfires(Guid.NewGuid())).ProcessedMisfiredTriggerCount.Should().Be(0);
        locks.TriggerAccess.Should().Be(0, "a pass with nothing stranded asks one question without the lock and takes none");
    }

    private async Task<IOperableTrigger> Fire(TriggerKey key)
    {
        List<IOperableTrigger> acquired = await completing.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow.AddSeconds(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero
        });

        acquired.Select(x => x.Key).Should().Equal([key], "the trigger under test is the one due");

        List<TriggerFiredResult> results = await completing.TriggersFired(acquired);
        results.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull("a firing has to start before it can complete");
        return results[0].TriggerFiredBundle.Trigger;
    }

    private static async Task ScheduleContinuation(IJobStore store, TriggerKey key, TriggerKey parent, ContinuationCondition condition)
    {
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity(key.Name, key.Group).Build();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(key)
            .ForJob(job)
            // Behind now, so the release's max(now, START_TIME) is now.
            .StartAt(DateTimeOffset.UtcNow.AddMinutes(-1))
            .StartAfter(parent, condition)
            .Build();

        trigger.ComputeFirstFireTimeUtc(calendar: null);
        await store.ScheduleJob(job, trigger);
    }

    private static IOperableTrigger OneOff(string name, IJobDetail job, DateTimeOffset at)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(at)
            .Build();

        trigger.ComputeFirstFireTimeUtc(calendar: null);
        return trigger;
    }

    private static async Task<LocalTransactionJobStore> BuildStore(string instanceId, IDriverDelegate driverDelegate, ILockHandler lockHandler, string connectionString)
    {
        LocalTransactionJobStore store = new(TestJobStores.Dependencies(
            schedulerOptions: TestJobStores.SchedulerOptions(SchedulerName, instanceId),
            storeOptions: TestJobStores.StoreOptions("stranded-continuations"),
            dbProvider: new DbProvider(DataSourceOptions.Providers.Npgsql, connectionString),
            driverDelegate: driverDelegate,
            lockHandler: lockHandler));

        await store.Initialize(new SchedulerIdentity { SchedulerName = SchedulerName, InstanceId = instanceId });
        return store;
    }

    private static string ContainerConnectionString()
    {
        string connectionString = Environment.GetEnvironmentVariable("PG_CONNECTION_STRING");
        connectionString.Should().NotBeNullOrWhiteSpace(
            "PG_CONNECTION_STRING is set by the container this assembly starts; run the fixture through the db-postgres leg");
        return connectionString;
    }

    /// <summary>
    /// The PostgreSQL delegate with a hook after the continuation scan, fired once: the moment between
    /// a lock-free completion's first statement and its first write.
    /// </summary>
    private sealed class RacingPostgreSqlDelegate : PostgreSQLDelegate
    {
        public Func<Task> AfterContinuationScan { get; set; }

        public override async ValueTask<List<AwaitingContinuation>> SelectAwaitingContinuations(
            ConnectionAndTransactionHolder conn,
            TriggerKey parent,
            CancellationToken cancellationToken = default)
        {
            List<AwaitingContinuation> awaiting = await base.SelectAwaitingContinuations(conn, parent, cancellationToken);

            Func<Task> hook = AfterContinuationScan;
            if (hook is not null)
            {
                AfterContinuationScan = null;
                await hook();
            }

            return awaiting;
        }
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
