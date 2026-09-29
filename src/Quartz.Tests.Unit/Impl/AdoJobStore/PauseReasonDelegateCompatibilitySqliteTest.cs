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
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// A driver delegate written against 4.2, deriving from the shipped one to change how a pause is
/// written, keeps deciding how every pause without a reason is written in 4.3.
/// </summary>
/// <remarks>
/// <para>
/// 4.3 added pause members to <see cref="IDriverDelegate" /> that write the reason in the statement that
/// moves the state. A pause without a reason must not go through them: an override of the member the
/// store used to call would be passed by, and a minor release does not change what an existing store
/// call does for an extension that already exists. Only a pause that says something goes through the
/// new members.
/// </para>
/// <para>
/// On a SQLite file, so the counts are of calls the store really made and the pauses really landed.
/// </para>
/// </remarks>
public sealed class PauseReasonDelegateCompatibilitySqliteTest
{
    private const string Group = "compat";
    private const string SchedulerName = "PauseReasonDelegateCompatibilitySqliteTest";

    private static readonly PauseDetails maintenance = new() { Reason = "database maintenance", RequestedBy = "alice" };

    private SqliteTestDatabase database = null!;
    private CountingSqliteDelegate driverDelegate = null!;
    private CountingLockHandler lockHandler = null!;
    private CountingDbProvider dbProvider = null!;
    private LocalTransactionJobStore store = null!;
    private FakeTimeProvider clock = null!;

    [SetUp]
    public async Task BuildStore()
    {
        database = new SqliteTestDatabase("pause-delegate-compat");

        await using (SqliteConnection connection = new(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = new(LoadSqliteTableScript(), connection);
            await command.ExecuteNonQueryAsync();
        }

        clock = new FakeTimeProvider(new DateTimeOffset(2031, 6, 17, 10, 0, 0, TimeSpan.Zero));
        driverDelegate = new CountingSqliteDelegate();
        dbProvider = new CountingDbProvider(new DbProvider("SQLite-Microsoft", database.ConnectionString));
        store = new LocalTransactionJobStore(TestJobStores.Dependencies(
            timeProvider: clock,
            schedulerOptions: TestJobStores.SchedulerOptions(instanceName: SchedulerName, instanceId: "node-a"),
            storeOptions: TestJobStores.StoreOptions("pause-delegate-compat"),
            dbProvider: dbProvider,
            driverDelegate: driverDelegate));

        // Initialized but not started, so no misfire loop makes calls of its own.
        await store.Initialize(TestJobStores.Identity(instanceName: SchedulerName, instanceId: "node-a"));

        // Wrapped once initialized, because that is when a SQLite store settles on its own lock handler.
        lockHandler = new CountingLockHandler(store.LockHandler);
        store.LockHandler = lockHandler;
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        await store.Shutdown();
        database.Dispose();
    }

    [Test]
    public async Task AReasonlessPauseOfOneTriggerIsWrittenByTheOverriddenUpdate()
    {
        TriggerKey reasonless = await Schedule("reasonless");
        TriggerKey withNull = await Schedule("with-null");
        TriggerKey withReason = await Schedule("with-reason");

        (await store.PauseTrigger(reasonless)).Should().BeTrue();
        (await store.PauseTriggerWith(withNull, null)).Should().BeTrue();

        driverDelegate.Calls(nameof(IDriverDelegate.UpdateTriggerState)).Should().Be(2,
            "a pause without a reason is written by the member 4.2 wrote it with, which this delegate overrides");
        driverDelegate.Calls(nameof(IDriverDelegate.PauseTriggerStates)).Should().Be(0);

        (await store.PauseTriggerWith(withReason, maintenance)).Should().BeTrue();

        driverDelegate.Calls(nameof(IDriverDelegate.PauseTriggerStates)).Should().Be(1,
            "a pause with a reason is a new call, and goes through the member that records it");
        driverDelegate.Calls(nameof(IDriverDelegate.UpdateTriggerState)).Should().Be(2,
            "and not through the reasonless one, so the override did not write the reason's pause");

        (await store.GetTriggerState(reasonless)).Should().Be(TriggerState.Paused, "the override's write is the pause");
        (await store.GetTriggerPause(withReason)).Should().NotBeNull();
    }

    [Test]
    public async Task AReasonlessPauseOfASetOrAJobIsWrittenByTheOverriddenUpdate()
    {
        TriggerKey single = await Schedule("single");
        IJobDetail job = await StoreJob("reports");
        await Schedule("of-job", job);
        IJobDetail other = await StoreJob("exports");
        await Schedule("of-other", other);

        (await store.PauseTriggers([single])).Should().Equal([single]);
        (await store.PauseJob(job.Key)).Should().BeTrue();
        (await store.PauseJobWith(other.Key, new PauseDetails { Reason = " " })).Should().BeTrue(
            "details that say nothing are the reasonless pause");

        driverDelegate.Calls(nameof(IDriverDelegate.UpdateTriggerStatesFromOtherStates)).Should().Be(3,
            "the key-set pause, the job pause and the pause that said nothing each move their triggers through the overridden member");
        driverDelegate.Calls(nameof(IDriverDelegate.PauseTriggerStates)).Should().Be(0);

        IJobDetail third = await StoreJob("imports");
        await Schedule("of-third", third);
        (await store.PauseJobWith(third.Key, maintenance)).Should().BeTrue();

        driverDelegate.Calls(nameof(IDriverDelegate.PauseTriggerStates)).Should().Be(1);
        driverDelegate.Calls(nameof(IDriverDelegate.UpdateTriggerStatesFromOtherStates)).Should().Be(3,
            "the pause with a reason did not use the override");
    }

    [Test]
    public async Task AKeySetPauseThatSaysNothingIsWrittenByTheOverriddenUpdate()
    {
        TriggerKey first = await Schedule("first");
        TriggerKey second = await Schedule("second");
        TriggerKey third = await Schedule("third");

        (await store.PauseTriggersWith([first], null)).Should().Equal([first]);
        (await store.PauseTriggersWith([second, third], new PauseDetails { Reason = " ", RequestedBy = "" })).Should().Equal([second, third]);

        driverDelegate.Calls(nameof(IDriverDelegate.UpdateTriggerStatesFromOtherStates)).Should().Be(2,
            "a key-set pause that says nothing is written by the member 4.3 wrote PauseTriggers with, which this delegate overrides");
        driverDelegate.Calls(nameof(IDriverDelegate.PauseTriggerStates)).Should().Be(0);
        (await store.GetTriggerPause(second)).Should().BeNull("a pause that said nothing recorded nothing");
    }

    [Test]
    public async Task AKeySetPauseWithAReasonIsOneLockOneTransactionAndOneRecordingStatement()
    {
        TriggerKey first = await Schedule("first");
        TriggerKey second = await Schedule("second");
        TriggerKey third = await Schedule("third");
        int locksBefore = lockHandler.TriggerAccessAcquisitions;
        int connectionsBefore = dbProvider.ConnectionsCreated;

        (await store.PauseTriggersWith([third, first, second], maintenance)).Should().Equal([third, first, second],
            "the answer keeps the order the keys were given in");

        lockHandler.TriggerAccessAcquisitions.Should().Be(locksBefore + 1, "the whole set is paused under one lock");
        dbProvider.ConnectionsCreated.Should().Be(connectionsBefore + 1, "and in one transaction, on one connection");
        driverDelegate.Calls(nameof(IDriverDelegate.PauseTriggerStates)).Should().Be(1,
            "three waiting triggers are one transition, so one statement records the pause on all of them");
        driverDelegate.Calls(nameof(IDriverDelegate.UpdateTriggerStatesFromOtherStates)).Should().Be(0,
            "a pause with a reason does not go through the reasonless override");

        PauseInfo expected = new("database maintenance", "alice", clock.GetUtcNow());
        (await store.GetTriggerPause(first)).Should().Be(expected);
        (await store.GetTriggerPause(third)).Should().Be(expected, "every trigger of the set is stamped with the same instant");
    }

    [Test]
    public async Task AKeySetJobPauseIsWrittenByTheOverriddenUpdateUnlessItSaysSomething()
    {
        IJobDetail reports = await StoreJob("reports");
        TriggerKey ofReports = await Schedule("of-reports", reports);
        IJobDetail exports = await StoreJob("exports");
        await Schedule("of-exports", exports);
        IJobDetail imports = await StoreJob("imports");
        await Schedule("of-imports", imports);

        (await store.PauseJobsWith([reports.Key], new PauseDetails())).Should().Equal([reports.Key]);

        driverDelegate.Calls(nameof(IDriverDelegate.UpdateTriggerStatesFromOtherStates)).Should().Be(1,
            "a job pause that says nothing moves the job's triggers through the overridden member");
        driverDelegate.Calls(nameof(IDriverDelegate.PauseTriggerStates)).Should().Be(0);
        (await store.GetTriggerPause(ofReports)).Should().BeNull();

        int locksBefore = lockHandler.TriggerAccessAcquisitions;
        int connectionsBefore = dbProvider.ConnectionsCreated;
        JobKey missing = new("missing", "jobs");

        (await store.PauseJobsWith([imports.Key, missing, exports.Key], maintenance)).Should().Equal([imports.Key, exports.Key],
            "a key that names no job is absent, and the rest keep the order they were given in");

        lockHandler.TriggerAccessAcquisitions.Should().Be(locksBefore + 1, "the whole set is paused under one lock");
        dbProvider.ConnectionsCreated.Should().Be(connectionsBefore + 1, "and in one transaction");
        driverDelegate.Calls(nameof(IDriverDelegate.PauseTriggerStates)).Should().Be(2, "one recording statement per job that has triggers");
        driverDelegate.Calls(nameof(IDriverDelegate.UpdateTriggerStatesFromOtherStates)).Should().Be(1,
            "the pause with a reason did not use the override");
    }

    [Test]
    public async Task AReasonlessPauseOfGroupsIsWrittenByTheOverriddenMembers()
    {
        await Schedule("in-group");

        await store.PauseTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals(Group));
        await store.PauseJobGroups(GroupMatcher<JobKey>.GroupEquals("jobs"));

        driverDelegate.Calls(nameof(IDriverDelegate.UpdateTriggerGroupStateFromOtherStates)).Should().Be(1);
        driverDelegate.Calls(nameof(IDriverDelegate.UpdateTriggerGroupStateFromOtherState)).Should().Be(1,
            "the paused-blocked half of a group pause is the single-state member, as it was in 4.2");
        driverDelegate.Calls(nameof(IDriverDelegate.InsertPausedTriggerGroup)).Should().Be(1);
        driverDelegate.Calls(nameof(IDriverDelegate.InsertPausedJobGroups)).Should().Be(1);
        driverDelegate.Calls(nameof(IDriverDelegate.PauseTriggerGroupStates)).Should().Be(0);
        driverDelegate.Calls(nameof(IDriverDelegate.InsertTriggerGroupPause)).Should().Be(0);
        driverDelegate.Calls(nameof(IDriverDelegate.InsertJobGroupPauses)).Should().Be(0);

        await store.PauseTriggerGroupsWith(GroupMatcher<TriggerKey>.GroupEquals("other"), maintenance);
        await store.PauseJobGroupsWith(GroupMatcher<JobKey>.GroupEquals("other-jobs"), maintenance);

        driverDelegate.Calls(nameof(IDriverDelegate.PauseTriggerGroupStates)).Should().Be(2);
        driverDelegate.Calls(nameof(IDriverDelegate.InsertTriggerGroupPause)).Should().Be(1);
        driverDelegate.Calls(nameof(IDriverDelegate.InsertJobGroupPauses)).Should().Be(1);
        driverDelegate.Calls(nameof(IDriverDelegate.InsertPausedTriggerGroup)).Should().Be(1, "the pauses with a reason did not use the overrides");
        driverDelegate.Calls(nameof(IDriverDelegate.InsertPausedJobGroups)).Should().Be(1);
    }

    [Test]
    public async Task AReasonlessPauseAllIsWrittenByTheOverriddenMembers()
    {
        await Schedule("in-group");

        await store.PauseAll();

        driverDelegate.Calls(nameof(IDriverDelegate.UpdateTriggerGroupStateFromOtherStates)).Should().Be(1);
        driverDelegate.Calls(nameof(IDriverDelegate.InsertPausedTriggerGroup)).Should().Be(2,
            "the group's row and the pause-all marker, both written as 4.2 wrote them");
        driverDelegate.Calls(nameof(IDriverDelegate.InsertTriggerGroupPause)).Should().Be(0);

        await store.ResumeAll();
        await store.PauseAllWith(maintenance);

        driverDelegate.Calls(nameof(IDriverDelegate.InsertTriggerGroupPause)).Should().Be(2);
        driverDelegate.Calls(nameof(IDriverDelegate.InsertPausedTriggerGroup)).Should().Be(2);
    }

    private async Task<IJobDetail> StoreJob(string name)
    {
        IJobDetail job = JobBuilder.Create<CompatJob>().WithIdentity(name, "jobs").StoreDurably().Build();
        await store.AddJob(job);
        return job;
    }

    private async Task<TriggerKey> Schedule(string name, IJobDetail? job = null)
    {
        job ??= await StoreJob("job-" + name);

        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(clock.GetUtcNow())
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

        trigger.ComputeFirstFireTimeUtc(null);
        await store.AddTrigger(trigger);
        return trigger.Key;
    }

    private static string LoadSqliteTableScript()
    {
        string path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        return File.ReadAllText(path);
    }

    /// <summary>Never executed: these tests drive the store, not a scheduler.</summary>
    public sealed class CompatJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// The store's own lock handler, counting how often the trigger-access lock is taken.
    /// </summary>
    private sealed class CountingLockHandler(ILockHandler inner) : ILockHandler
    {
        public int TriggerAccessAcquisitions { get; private set; }

        public bool RequiresConnection => inner.RequiresConnection;

        public ValueTask<bool> AcquireLock(Guid requestorId, ConnectionAndTransactionHolder? conn, SchedulerLock lockKind, CancellationToken cancellationToken = default)
        {
            if (lockKind == SchedulerLock.TriggerAccess)
            {
                TriggerAccessAcquisitions++;
            }

            return inner.AcquireLock(requestorId, conn, lockKind, cancellationToken);
        }

        public ValueTask ReleaseLock(Guid requestorId, SchedulerLock lockKind, CancellationToken cancellationToken = default)
        {
            return inner.ReleaseLock(requestorId, lockKind, cancellationToken);
        }

        public ValueTask Shutdown(CancellationToken cancellationToken = default) => inner.Shutdown(cancellationToken);
    }

    /// <summary>
    /// The SQLite provider, counting the connections the store opens: each is one transaction.
    /// </summary>
    private sealed class CountingDbProvider(IDbProvider inner) : IDbProvider
    {
        public int ConnectionsCreated { get; private set; }

        public string ConnectionString => inner.ConnectionString;

        public DbMetadata Metadata => inner.Metadata;

        public System.Data.Common.DbCommand CreateCommand() => inner.CreateCommand();

        public System.Data.Common.DbConnection CreateConnection()
        {
            ConnectionsCreated++;
            return inner.CreateConnection();
        }

        public void Shutdown() => inner.Shutdown();
    }

    /// <summary>
    /// The shipped SQLite delegate as a 4.2 extension would derive from it: overriding the members a
    /// pause is written through, here only to count the calls, and counting the 4.3 pause members too.
    /// </summary>
    private sealed class CountingSqliteDelegate : SQLiteDelegate
    {
        private readonly Dictionary<string, int> calls = new(StringComparer.Ordinal);

        public int Calls(string member) => calls.GetValueOrDefault(member);

        private void Count(string member) => calls[member] = Calls(member) + 1;

        public override ValueTask<int> UpdateTriggerState(
            ConnectionAndTransactionHolder conn,
            TriggerKey triggerKey,
            StoredTriggerState state,
            CancellationToken cancellationToken = default)
        {
            Count(nameof(UpdateTriggerState));
            return base.UpdateTriggerState(conn, triggerKey, state, cancellationToken);
        }

        public override ValueTask<int> UpdateTriggerStatesFromOtherStates(
            ConnectionAndTransactionHolder conn,
            IReadOnlyCollection<TriggerKey> triggerKeys,
            StoredTriggerState newState,
            IReadOnlyCollection<StoredTriggerState> oldStates,
            CancellationToken cancellationToken = default)
        {
            Count(nameof(UpdateTriggerStatesFromOtherStates));
            return base.UpdateTriggerStatesFromOtherStates(conn, triggerKeys, newState, oldStates, cancellationToken);
        }

        public override ValueTask<int> UpdateTriggerGroupStateFromOtherStates(
            ConnectionAndTransactionHolder conn,
            GroupMatcher<TriggerKey> matcher,
            StoredTriggerState newState,
            IReadOnlyCollection<StoredTriggerState> oldStates,
            CancellationToken cancellationToken = default)
        {
            Count(nameof(UpdateTriggerGroupStateFromOtherStates));
            return base.UpdateTriggerGroupStateFromOtherStates(conn, matcher, newState, oldStates, cancellationToken);
        }

        public override ValueTask<int> UpdateTriggerGroupStateFromOtherState(
            ConnectionAndTransactionHolder conn,
            GroupMatcher<TriggerKey> matcher,
            StoredTriggerState newState,
            StoredTriggerState oldState,
            CancellationToken cancellationToken = default)
        {
            Count(nameof(UpdateTriggerGroupStateFromOtherState));
            return base.UpdateTriggerGroupStateFromOtherState(conn, matcher, newState, oldState, cancellationToken);
        }

        public override ValueTask<int> InsertPausedTriggerGroup(
            ConnectionAndTransactionHolder conn,
            string groupName,
            CancellationToken cancellationToken = default)
        {
            Count(nameof(InsertPausedTriggerGroup));
            return base.InsertPausedTriggerGroup(conn, groupName, cancellationToken);
        }

        public override ValueTask InsertPausedJobGroups(
            ConnectionAndTransactionHolder conn,
            IReadOnlyCollection<string> groupNames,
            CancellationToken cancellationToken = default)
        {
            Count(nameof(InsertPausedJobGroups));
            return base.InsertPausedJobGroups(conn, groupNames, cancellationToken);
        }

        public override ValueTask<int> PauseTriggerStates(
            ConnectionAndTransactionHolder conn,
            IReadOnlyCollection<TriggerKey> triggerKeys,
            StoredTriggerState newState,
            IReadOnlyCollection<StoredTriggerState> oldStates,
            PauseInfo pause,
            CancellationToken cancellationToken = default)
        {
            Count(nameof(PauseTriggerStates));
            return base.PauseTriggerStates(conn, triggerKeys, newState, oldStates, pause, cancellationToken);
        }

        public override ValueTask<int> PauseTriggerGroupStates(
            ConnectionAndTransactionHolder conn,
            GroupMatcher<TriggerKey> matcher,
            StoredTriggerState newState,
            IReadOnlyCollection<StoredTriggerState> oldStates,
            PauseInfo pause,
            CancellationToken cancellationToken = default)
        {
            Count(nameof(PauseTriggerGroupStates));
            return base.PauseTriggerGroupStates(conn, matcher, newState, oldStates, pause, cancellationToken);
        }

        public override ValueTask<int> InsertTriggerGroupPause(
            ConnectionAndTransactionHolder conn,
            string groupName,
            PauseInfo pause,
            CancellationToken cancellationToken = default)
        {
            Count(nameof(InsertTriggerGroupPause));
            return base.InsertTriggerGroupPause(conn, groupName, pause, cancellationToken);
        }

        public override ValueTask InsertJobGroupPauses(
            ConnectionAndTransactionHolder conn,
            IReadOnlyCollection<string> groupNames,
            PauseInfo pause,
            CancellationToken cancellationToken = default)
        {
            Count(nameof(InsertJobGroupPauses));
            return base.InsertJobGroupPauses(conn, groupNames, pause, cancellationToken);
        }
    }
}
