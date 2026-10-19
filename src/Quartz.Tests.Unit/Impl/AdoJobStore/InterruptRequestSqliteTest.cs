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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using Quartz.Core;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// What the ADO.NET store does with an interrupt asked of another node's firing (#4071): records it on
/// the firing's <c>QRTZ_FIRED_TRIGGERS</c> row, and answers it to the node running the firing alone.
/// </summary>
/// <remarks>
/// Two stores on one SQLite file, told they are clustered after they started — a SQLite store refuses
/// to be configured as one — and driven call by call, so every state is read straight off the rows. The
/// same claim with both nodes started and checking in on their own is
/// <c>ClusteredInterruptTestBase</c> in the integration suite.
/// </remarks>
[NonParallelizable]
public sealed class InterruptRequestSqliteTest
{
    private const string SchedulerName = "InterruptRequestSqliteTest";
    private const string DataSourceName = "interrupt-request";
    private const string Group = "interrupt-request";

    /// <summary>Log events <c>ClusterLog.InterruptRequestRecorded</c> and <c>ClusterLog.InterruptRequestsUndeliverable</c>.</summary>
    private const int InterruptRequestRecorded = 3519;
    private const int InterruptRequestsUndeliverable = 3520;

    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    private SqliteTestDatabase database = null!;
    private IDbProvider dbProvider = null!;
    private FakeTimeProvider clock = null!;
    private RecordingLoggerProvider logs = null!;
    private ILoggerFactory loggerFactory = null!;
    private PollRecorder polls = null!;
    private LocalTransactionJobStore nodeA = null!;
    private LocalTransactionJobStore nodeB = null!;
    private Dictionary<string, TriggerFiredBundle> firedOnA = null!;

    [SetUp]
    public async Task CreateTwoNodesOnOneDatabase()
    {
        database = new SqliteTestDatabase("interrupt-request");
        await using (SqliteConnection connection = new(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = new(await File.ReadAllTextAsync(TablesScript()), connection);
            await command.ExecuteNonQueryAsync();
        }

        dbProvider = new DbProvider("SQLite-Microsoft", database.ConnectionString);
        clock = new FakeTimeProvider(epoch);
        logs = new RecordingLoggerProvider();
        loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        polls = new PollRecorder();
        firedOnA = new Dictionary<string, TriggerFiredBundle>(StringComparer.Ordinal);

        nodeA = await Node("node-a");
        nodeB = await Node("node-b");
    }

    [TearDown]
    public async Task ShutDownNodes()
    {
        await nodeB.Shutdown();
        await nodeA.Shutdown();
        loggerFactory.Dispose();
        logs.Dispose();
        database.Dispose();
    }

    [Test]
    public async Task ARequestIsRecordedOnTheRowOfAFiringAnotherNodeRunsAndReadByThatNodeAlone()
    {
        string firing = await FireOnA("t1");

        (await nodeB.RequestInterrupt(firing)).Should().BeTrue("the firing executes, on node A, and its row is there to carry the request");

        (await InterruptRequestedTime(firing)).Should().Be(epoch.UtcTicks, "the column holds when the request was made, in ticks like FIRED_TIME");
        (await nodeA.GetInterruptRequests()).Should().Equal([firing], "node A runs the firing, so its read answers it");
        (await nodeB.GetInterruptRequests()).Should().BeEmpty("node B runs nothing; a request is answered to the node running the firing");

        logs.Entries.Should().ContainSingle(x => x.EventId.Id == InterruptRequestRecorded)
            .Which.Message.Should().Contain(firing).And.Contain("node-a");
    }

    [Test]
    public async Task ARequestForAFiringThatIsNotExecutingRecordsNothing()
    {
        await Schedule("t1");
        List<IOperableTrigger> reserved = await nodeA.AcquireNextTriggers(Due());
        string reservation = reserved.Should().ContainSingle().Subject.FireInstanceId!;

        (await nodeB.RequestInterrupt(reservation)).Should().BeFalse("a reservation has no job running yet, as a local interrupt would find");
        (await nodeB.RequestInterrupt("no-such-firing")).Should().BeFalse("nothing to record the request on");

        (await InterruptRequestedTime(reservation)).Should().BeNull();
        (await nodeA.GetInterruptRequests()).Should().BeEmpty();
        logs.Entries.Should().NotContain(x => x.EventId.Id == InterruptRequestRecorded);
    }

    [Test]
    public async Task TheFiringsCompletionTakesItsRequestWithIt()
    {
        string firing = await FireOnA("t1");
        (await nodeB.RequestInterrupt(firing)).Should().BeTrue();

        TriggerFiredBundle bundle = firedOnA[firing];
        await nodeA.TriggeredJobComplete(bundle.Trigger, bundle.JobDetail, SchedulerInstruction.NoInstruction);

        (await nodeA.GetInterruptRequests()).Should().BeEmpty("the row went with the firing, and the request with the row");
    }

    [Test]
    public async Task AStoreThatIsNotClusteredRecordsAndReadsNothing()
    {
        string firing = await FireOnA("t1");
        nodeA.Clustered = false;
        nodeB.Clustered = false;

        (await nodeB.RequestInterrupt(firing)).Should().BeFalse("no node of a store that is not clustered runs a check-in to read a request by");
        (await InterruptRequestedTime(firing)).Should().BeNull();

        await WriteInterruptRequestedTime(firing, epoch.UtcTicks);
        (await nodeA.GetInterruptRequests()).Should().BeEmpty("nor does it read one");
    }

    [Test]
    public async Task APeerIsKnownToReadRequestsOnceACheckInHasReadItAt46()
    {
        nodeB.ReadsInterruptRequests("node-a").Should().BeFalse("nothing is known about a peer before a check-in has read the state table");
        nodeB.ReadsInterruptRequests("node-b").Should().BeTrue("a node reads its own requests");

        await nodeA.CheckIn(Guid.NewGuid());
        await nodeB.CheckIn(Guid.NewGuid());

        nodeB.ReadsInterruptRequests("node-a").Should().BeTrue("node A's row carries this node's version, which reads them");
        nodeB.ReadsInterruptRequests("node-z").Should().BeFalse("a node the check-in did not read is not known to read anything");
    }

    [Test]
    public async Task ARequestForAFiringOnANodeOlderThan46IsRecordedAndSaidOnceToGoUnanswered()
    {
        await nodeA.CheckIn(Guid.NewGuid());
        await nodeB.CheckIn(Guid.NewGuid());
        await ExecuteNonQuery("UPDATE QRTZ_SCHEDULER_STATE SET QUARTZ_VERSION = NULL WHERE INSTANCE_NAME = 'node-a'");
        await nodeB.CheckIn(Guid.NewGuid());

        nodeB.ReadsInterruptRequests("node-a").Should().BeFalse("a row without a version is a node older than 4.6, which never reads the column");

        string first = await FireOnA("t1");
        string second = await FireOnA("t2");

        (await nodeB.RequestInterrupt(first)).Should().BeTrue("the request is recorded all the same; it is the answer that never comes");
        (await nodeB.RequestInterrupt(second)).Should().BeTrue();

        logs.Entries.Count(x => x.EventId.Id == InterruptRequestRecorded).Should().Be(2);
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == InterruptRequestsUndeliverable,
                "an operator is told once per node that its firings cannot be reached this way, not once per request")
            .Which.Message.Should().Contain("node-a").And.Contain("none");
    }

    [Test]
    public async Task ACheckInSignalsTheSchedulerToReadItsRequests()
    {
        nodeA.SignalInterruptRequestPoll();

        polls.Count.Should().Be(1, "the signaler the store was built with is the scheduler's, which reads the requests");
    }

    private async Task<LocalTransactionJobStore> Node(string instanceId)
    {
        LocalTransactionJobStore store = new(TestJobStores.Dependencies(
            signaler: polls,
            timeProvider: clock,
            schedulerOptions: TestJobStores.SchedulerOptions(instanceName: SchedulerName, instanceId: instanceId),
            storeOptions: TestJobStores.StoreOptions(DataSourceName),
            dbProvider: dbProvider,
            driverDelegate: new SQLiteDelegate(),
            loggerFactory: loggerFactory));

        // Initialized but not started, so no loop races an assertion; clustered once it is, which a
        // SQLite store refuses to be configured as.
        await store.Initialize(TestJobStores.Identity(instanceName: SchedulerName, instanceId: instanceId));
        store.Clustered = true;
        return store;
    }

    private async Task Schedule(string name)
    {
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity(name, Group).Build();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(clock.GetUtcNow())
            .Build();
        trigger.ComputeFirstFireTimeUtc(null);
        await nodeA.ScheduleJob(job, trigger);
    }

    private async Task<string> FireOnA(string name)
    {
        await Schedule(name);
        List<IOperableTrigger> acquired = await nodeA.AcquireNextTriggers(Due());
        List<TriggerFiredResult> fired = await nodeA.TriggersFired(acquired);
        TriggerFiredBundle bundle = fired.Should().ContainSingle().Subject.TriggerFiredBundle!;
        bundle.Should().NotBeNull("the premise: node A's reservation is its own to fire");

        string fireInstanceId = bundle.Trigger.FireInstanceId!;
        firedOnA[fireInstanceId] = bundle;
        return fireInstanceId;
    }

    private TriggerAcquisitionRequest Due()
    {
        return new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddSeconds(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero,
        };
    }

    private async Task<long?> InterruptRequestedTime(string fireInstanceId)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT INTERRUPT_REQUESTED_TIME FROM QRTZ_FIRED_TRIGGERS WHERE ENTRY_ID = @id";
        command.Parameters.AddWithValue("@id", fireInstanceId);
        object? value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task WriteInterruptRequestedTime(string fireInstanceId, long ticks)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE QRTZ_FIRED_TRIGGERS SET INTERRUPT_REQUESTED_TIME = @ticks WHERE ENTRY_ID = @id";
        command.Parameters.AddWithValue("@ticks", ticks);
        command.Parameters.AddWithValue("@id", fireInstanceId);
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
    }

    private async Task ExecuteNonQuery(string sql)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
    }

    private static string TablesScript()
    {
        return File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";
    }

    /// <summary>
    /// A signaler that counts the polls a store asks of the scheduler, and says nothing else.
    /// </summary>
    private sealed class PollRecorder : ISchedulerSignaler, IInterruptRequestPoller
    {
        private int count;

        public int Count => Volatile.Read(ref count);

        public void PollInterruptRequests() => Interlocked.Increment(ref count);

        public ValueTask NotifyTriggerListenersMisfired(ITrigger trigger, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersFinalized(ITrigger trigger, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersJobDeleted(JobKey jobKey, CancellationToken cancellationToken = default) => default;

        public ValueTask SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersError(SchedulerErrorContext errorContext, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>Never executed: these tests drive the stores, not a scheduler.</summary>
    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
