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

using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;

using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A spent one-off's completion, which takes no <c>TRIGGER_ACCESS</c>, racing a replace, a reschedule and an
/// unschedule of the same trigger under the lock (#4399): no deadlock, and a replacement that commits between
/// the completion's read and its deletes is kept. One fixture per database.
/// </summary>
/// <remarks>
/// <para>
/// Deterministic, as the report's program is: a side transaction holds the trigger's
/// <c>QRTZ_SIMPLE_TRIGGERS</c> row, so each statement that needs the row queues behind it in the order the
/// test starts them, and the side transaction's commit lets them all go at once. One store, not clustered
/// but locking through the database, is driven call by call; the job is durable and allows concurrent
/// execution, so its trigger's completion takes no lock.
/// </para>
/// <para>
/// A deadlock does not fail a call: the store retries the victim, and a completion that fails without the
/// lock runs again under it. Both log a warning, which is what the tests read.
/// </para>
/// <para>
/// Where a read waits for a writer (SQL Server without <c>READ_COMMITTED_SNAPSHOT</c>), the completion that
/// comes second waits at its header read rather than at its first write, and then reads the row as the
/// first writer left it. The same assertions hold.
/// </para>
/// </remarks>
public abstract class LockFreeCompletionRacesTestBase : ClusteredJobStoreTestBase
{
    private const string Group = "completionRaces";

    private static readonly JobKey JobKey = new("job", Group);
    private static readonly TriggerKey TriggerKey = new("one-off", Group);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly RecordingLoggerProvider logs = new();

    private LocalTransactionJobStore store;

    protected LockFreeCompletionRacesTestBase(string provider) : base(provider)
    {
    }

    protected LockFreeCompletionRacesTestBase(ClusteredTestDatabase database) : base(database)
    {
    }

    protected override string SchedulerName => "LockFreeCompletionRaces";

    /// <summary>
    /// The side transaction's statement: takes the one-off's <c>QRTZ_SIMPLE_TRIGGERS</c> row for update, by
    /// <c>@schedulerName</c>, <c>@name</c> and <c>@group</c>, and returns a row.
    /// </summary>
    protected abstract string HoldScheduleRowSql { get; }

    /// <summary>
    /// The number of sessions of the database waiting on a lock, or <see langword="null" /> where the test's
    /// login cannot read it: the test then gives each statement a fixed time to reach its wait.
    /// </summary>
    protected virtual string LockWaitersSql => null;

    [SetUp]
    public async Task CreateStore()
    {
        store = new LocalTransactionJobStore(TestJobStores.Dependencies(
            schedulerOptions: TestJobStores.SchedulerOptions(SchedulerName, "node"),
            storeOptions: TestJobStores.StoreOptions("default", "QRTZ_", options => options.UseDbLocks = true),
            clusteringOptions: TestJobStores.ClusteringOptions(options => options.Enabled = false),
            dbProvider: new DbProvider(Database.Provider, Database.ConnectionString),
            driverDelegate: Database.CreateDriverDelegate(),
            loggerFactory: LoggerFactory.Create(builder => builder.AddProvider(logs))) with
        {
            LockHandler = null,
        });

        await store.Initialize(TestJobStores.Identity(SchedulerName, "node"));
        await store.AddJob(JobBuilder.Create<NoOpJob>().WithIdentity(JobKey).StoreDurably().Build());
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        await store.Shutdown();
    }

    /// <summary>
    /// The report's first case: the completion deletes first, the replace writes the row and then needs the
    /// schedule row. The completion takes the row first, so the replace waits for it, finds it gone and
    /// stores the trigger as new (#3863).
    /// </summary>
    [Test]
    public async Task AReplaceWhileASpentOneOffCompletesDoesNotDeadlockAndIsStored()
    {
        TriggerFiredBundle firing = await FireTheOneOff();
        DateTimeOffset replacementDue = InAnHour();
        ScheduleTriggerResult replaced;

        await using (SideTransaction side = await HoldTheScheduleRow())
        {
            Task completion = Complete(firing);
            await side.WaitForWaiters(1, "the completion to wait");
            Task<ScheduleTriggerResult> replace = Run(() => store.StoreTrigger(OneOff(replacementDue), TriggerConflict.Replace));
            await side.WaitForWaiters(2, "the replace to wait");

            await side.Commit();
            await completion.WaitAsync(Timeout);
            replaced = await replace.WaitAsync(Timeout);
        }

        logs.Warnings.Should().BeEmpty("neither statement waits for the other in a cycle, so nothing deadlocks and nothing is retried");
        replaced.Outcome.Should().BeOneOf([ScheduleOutcome.Replaced, ScheduleOutcome.Created],
            "the replace finds the spent row if its read does not wait for the completion's delete, and stores the trigger either way");
        (await NextFireTimeStored()).Should().Be(Ticks(replacementDue), "the replacement is stored");
        (await ScheduleRows()).Should().Be(1, "with its schedule row");
    }

    /// <summary>
    /// The report's second case: the replace commits between the completion's read of the spent row and its
    /// deletes. The completion leaves the replacement alone.
    /// </summary>
    [Test]
    public async Task AReplaceThatCommitsAfterTheCompletionsReadIsKept()
    {
        TriggerFiredBundle firing = await FireTheOneOff();
        DateTimeOffset replacementDue = InAnHour();

        await using (SideTransaction side = await HoldTheScheduleRow())
        {
            Task<ScheduleTriggerResult> replace = Run(() => store.StoreTrigger(OneOff(replacementDue), TriggerConflict.Replace));
            await side.WaitForWaiters(1, "the replace to wait");
            Task completion = Complete(firing);
            await side.WaitForWaiters(2, "the completion to read the spent row and wait");

            await side.Commit();
            (await replace.WaitAsync(Timeout)).Outcome.Should().Be(ScheduleOutcome.Replaced);
            await completion.WaitAsync(Timeout);
        }

        logs.Warnings.Should().BeEmpty();
        (await NextFireTimeStored()).Should().Be(Ticks(replacementDue),
            "the replacement is not the spent row the completion read, and is left alone");
        (await ScheduleRows()).Should().Be(1, "with its schedule row");
    }

    /// <summary>
    /// A reschedule deletes the row and inserts its replacement, of the same type. One that commits between
    /// the completion's read and its deletes is kept.
    /// </summary>
    [Test]
    public async Task ARescheduleThatCommitsAfterTheCompletionsReadIsKept()
    {
        TriggerFiredBundle firing = await FireTheOneOff();
        DateTimeOffset replacementDue = InAnHour();

        await using (SideTransaction side = await HoldTheScheduleRow())
        {
            Task<bool> reschedule = Run(() => store.ReplaceTrigger(TriggerKey, OneOff(replacementDue)));
            await side.WaitForWaiters(1, "the reschedule to wait");
            Task completion = Complete(firing);
            await side.WaitForWaiters(2, "the completion to read the spent row and wait");

            await side.Commit();
            (await reschedule.WaitAsync(Timeout)).Should().BeTrue();
            await completion.WaitAsync(Timeout);
        }

        logs.Warnings.Should().BeEmpty();
        (await NextFireTimeStored()).Should().Be(Ticks(replacementDue), "the rescheduled trigger is not the spent row the completion read");
        (await ScheduleRows()).Should().Be(1, "and keeps its schedule row");
    }

    /// <summary>
    /// An unschedule deletes under the lock what the completion deletes without it, and starts first. Both
    /// take the trigger's row before its schedule row, so neither waits for the other in a cycle. Were only
    /// the completion to take the trigger's row first, the unschedule would win the schedule row and then
    /// wait for the trigger's row, which the completion holds while it waits for the schedule row.
    /// </summary>
    [Test]
    public async Task AnUnscheduleWhileASpentOneOffCompletesDoesNotDeadlock()
    {
        TriggerFiredBundle firing = await FireTheOneOff();

        await using (SideTransaction side = await HoldTheScheduleRow())
        {
            Task<bool> unschedule = Run(() => store.DeleteTrigger(TriggerKey));
            await side.WaitForWaiters(1, "the unschedule to wait");
            Task completion = Complete(firing);
            await side.WaitForWaiters(2, "the completion to wait");

            await side.Commit();
            (await unschedule.WaitAsync(Timeout)).Should().BeTrue("the unschedule came first, and deleted the trigger");
            await completion.WaitAsync(Timeout);
        }

        logs.Warnings.Should().BeEmpty("neither statement waits for the other in a cycle, so nothing deadlocks and nothing is retried");
        (await store.GetTrigger(TriggerKey)).Should().BeNull();
        (await ScheduleRows()).Should().Be(0);
        (await CountRows("SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName", ("schedulerName", SchedulerName)))
            .Should().Be(0, "the firing's row went with its completion");
    }

    /// <summary>
    /// Stores the one-off due now, acquires and fires it: its row is spent, with no fire time left, and its
    /// job, durable and allowed to run beside itself, makes its completion one that takes no lock.
    /// </summary>
    private async Task<TriggerFiredBundle> FireTheOneOff()
    {
        await store.StoreTrigger(OneOff(DateTimeOffset.UtcNow.AddSeconds(-1)), TriggerConflict.Throw);

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow.AddMinutes(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero,
        });
        IOperableTrigger trigger = acquired.Should().ContainSingle().Subject;

        TriggerFiredBundle firing = (await store.TriggersFired([trigger])).Should().ContainSingle().Subject.TriggerFiredBundle;
        firing.Should().NotBeNull();
        firing.Trigger.NextFireTimeUtc.Should().BeNull("the premise: the one-off has nothing left to fire");

        logs.Clear();
        return firing;
    }

    private Task Complete(TriggerFiredBundle firing)
    {
        return Run(async () =>
        {
            await store.TriggeredJobComplete(firing.Trigger, firing.JobDetail, SchedulerInstruction.DeleteTrigger);
            return true;
        });
    }

    /// <summary>A one-off due at <paramref name="due" />, with its fire time worked out as a scheduler would before storing it.</summary>
    private static IOperableTrigger OneOff(DateTimeOffset due)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create().WithIdentity(TriggerKey).ForJob(JobKey).StartAt(due).Build();
        trigger.ComputeFirstFireTimeUtc(null);
        return trigger;
    }

    private static Task<T> Run<T>(Func<ValueTask<T>> operation) => Task.Run(() => operation().AsTask());

    private static long Ticks(DateTimeOffset instant) => instant.UtcDateTime.Ticks;

    /// <summary>A whole second an hour from now: when a replacement is due, so that nothing fires it during the test.</summary>
    private static DateTimeOffset InAnHour()
    {
        DateTimeOffset due = DateTimeOffset.UtcNow.AddHours(1);
        return due.AddTicks(-(due.Ticks % TimeSpan.TicksPerSecond));
    }

    private async Task<long?> NextFireTimeStored()
    {
        object value = await ExecuteScalar(
            "SELECT NEXT_FIRE_TIME FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @name AND TRIGGER_GROUP = @group",
            ("schedulerName", SchedulerName), ("name", TriggerKey.Name), ("group", TriggerKey.Group));
        return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private Task<int> ScheduleRows()
    {
        return CountRows(
            "SELECT COUNT(*) FROM QRTZ_SIMPLE_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @name AND TRIGGER_GROUP = @group",
            ("schedulerName", SchedulerName), ("name", TriggerKey.Name), ("group", TriggerKey.Group));
    }

    /// <summary>
    /// Opens the side transaction and takes the trigger's schedule row with it.
    /// </summary>
    private async Task<SideTransaction> HoldTheScheduleRow()
    {
        DbConnection connection = Database.CreateConnection();
        await connection.OpenAsync();
        DbTransaction transaction = await connection.BeginTransactionAsync();

        await using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = HoldScheduleRowSql;
        AddParameter(command, "schedulerName", SchedulerName);
        AddParameter(command, "name", TriggerKey.Name);
        AddParameter(command, "group", TriggerKey.Group);
        (await command.ExecuteScalarAsync()).Should().NotBeNull("the premise: the one-off's schedule row is there to hold");

        return new SideTransaction(this, connection, transaction);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "@" + name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed class SideTransaction(LockFreeCompletionRacesTestBase test, DbConnection connection, DbTransaction transaction) : IAsyncDisposable
    {
        private static readonly TimeSpan WaitsOnTheRow = TimeSpan.FromMilliseconds(1500);

        /// <summary>
        /// Waits until <paramref name="count" /> sessions of the database wait on a lock, or, where that
        /// cannot be read, long enough for the statement just started to reach its wait.
        /// </summary>
        public async Task WaitForWaiters(int count, string what)
        {
            if (test.LockWaitersSql is not { } sql)
            {
                await Task.Delay(WaitsOnTheRow);
                return;
            }

            await using DbConnection probe = test.Database.CreateConnection();
            await probe.OpenAsync();
            await using DbCommand command = probe.CreateCommand();
            command.CommandText = sql;

            DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(Timeout);
            while (Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) < count)
            {
                DateTimeOffset.UtcNow.Should().BeBefore(deadline, "waiting for " + what);
                await Task.Delay(20);
            }
        }

        public Task Commit() => transaction.CommitAsync();

        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    /// <summary>The warnings the store logs: a retried deadlock victim, a completion run again under the lock.</summary>
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> warnings = new();

        public IReadOnlyCollection<string> Warnings => warnings;

        public void Clear() => warnings.Clear();

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(warnings);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(ConcurrentQueue<string> warnings) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    warnings.Enqueue($"{eventId.Id}: {formatter(state, exception)} {exception?.InnerException?.Message ?? exception?.Message}");
                }
            }
        }
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
