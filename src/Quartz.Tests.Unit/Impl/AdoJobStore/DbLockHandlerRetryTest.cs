#nullable enable

using System.Data;
using System.Data.Common;
using System.Diagnostics;

using Microsoft.Extensions.Time.Testing;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The row-lock handlers back off between attempts. Until the wait moved onto
/// <see cref="LockHandlerContext.TimeProvider"/> it was a wall-clock <see cref="Task.Delay(TimeSpan, CancellationToken)"/>,
/// so the only way to observe a retry was for the test to sit out the real second — which is why the
/// retry paths of both handlers had no test at all.
/// </summary>
public class DbLockHandlerRetryTest
{
    private static readonly DbMetadata FakeDriver = new()
    {
        ProductName = "Fake",
        ParameterNamePrefix = "@",
        BindByName = true,
    };

    private static LockHandlerContext Context(TimeProvider timeProvider) => new()
    {
        SchedulerName = "TESTSCHED",
        InstanceId = "node-1",
        TablePrefix = "QRTZ_",
        TimeProvider = timeProvider,
    };

    [Test]
    public async Task AnUpdateRowLockHandlerBacksOffOnTheStoresClockRatherThanOnWallTime()
    {
        var clock = new FakeTimeProvider();
        var provider = new FailingDbProvider();

        // Ten minutes: long enough that a handler still waiting on wall time would hang this test rather
        // than pass it slowly, so the assertion cannot be satisfied by accident.
        var lockHandler = new UpdateRowLockHandler(provider) { RetryPeriod = TimeSpan.FromMinutes(10) };
        lockHandler.Initialize(Context(clock));

        using var connection = new FakeConnection();
        using var holder = new ConnectionAndTransactionHolder(connection, transaction: null);

        var started = Stopwatch.GetTimestamp();
        Task<bool> obtain = lockHandler.AcquireLock(Guid.NewGuid(), holder, SchedulerLock.TriggerAccess).AsTask();

        await WaitUntil(() => provider.CommandsExecuted >= 1, obtain);
        obtain.IsCompleted.Should().BeFalse("the first attempt failed and the handler is parked on its ten-minute backoff");

        await AdvanceUntilComplete(clock, obtain, TimeSpan.FromMinutes(10));

        var act = async () => await obtain;
        await act.Should().ThrowAsync<LockException>().WithMessage("*db row lock*");

        provider.CommandsExecuted.Should().Be(2, "UpdateRowLockHandler.RetryCount is 2, so the failure is retried once");
        Stopwatch.GetElapsedTime(started).Should().BeLessThan(TimeSpan.FromMinutes(1),
            "the ten minutes were waited on the fake clock, not on the wall");
    }

    [Test]
    public async Task ASelectForUpdateLockHandlerBacksOffOnTheStoresClockRatherThanOnWallTime()
    {
        var clock = new FakeTimeProvider();
        var provider = new FailingDbProvider();

        var lockHandler = new SelectForUpdateLockHandler(provider)
        {
            MaxRetry = 3,
            RetryPeriod = TimeSpan.FromMinutes(10),
        };
        lockHandler.Initialize(Context(clock));

        using var connection = new FakeConnection();
        using var holder = new ConnectionAndTransactionHolder(connection, transaction: null);

        Task<bool> obtain = lockHandler.AcquireLock(Guid.NewGuid(), holder, SchedulerLock.TriggerAccess).AsTask();

        await WaitUntil(() => provider.CommandsExecuted >= 1, obtain);
        obtain.IsCompleted.Should().BeFalse("the first attempt failed and the handler is parked on its ten-minute backoff");

        await AdvanceUntilComplete(clock, obtain, TimeSpan.FromMinutes(10));

        var act = async () => await obtain;
        await act.Should().ThrowAsync<LockException>();

        provider.CommandsExecuted.Should().Be(3, "MaxRetry is 3, so the select is attempted three times before the handler gives up");
    }

    /// <summary>
    /// A backoff no timer will sit out is refused where it is configured. A lock handler has no options
    /// type and so no startup validator, and the flat <c>quartz.jobStore.lockHandler.retryPeriod</c> key
    /// writes this property by reflection — which the property binder turns into a
    /// <c>SchedulerConfigException</c> naming the key. Left to the wait itself, the report would arrive
    /// from the first contended lock attempt, with the lock unacquired and nothing naming the setting.
    /// </summary>
    [TestCase(60)]
    [TestCase(-1)]
    public void ARetryPeriodNoTimerWillWaitOutIsRefusedWhereItIsConfigured(int days)
    {
        TimeSpan period = TimeSpan.FromDays(days);
        var provider = new FailingDbProvider();

        var updateRow = () => new UpdateRowLockHandler(provider) { RetryPeriod = period };
        updateRow.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*RetryPeriod*49.7 days*");

        var selectForUpdate = () => new SelectForUpdateLockHandler(provider) { RetryPeriod = period };
        selectForUpdate.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*RetryPeriod*49.7 days*");
    }

    /// <summary>
    /// Neither retry loop treats the caller's cancellation as a lock it could not get. The exception
    /// that ends the loop is a <see cref="LockException" />, which is a
    /// <see cref="JobPersistenceException" />, so a caller who asked to stop would be told the database
    /// had refused them the lock — #3503 in the one place the store's own plumbing does not reach.
    /// </summary>
    /// <remarks>
    /// The retry count is one, because that is the configuration where the loop reports rather than
    /// backs off: with the default the first backoff is a <see cref="Task.Delay(TimeSpan)" /> on the
    /// same token, which throws the cancellation before the loop can turn it into anything else.
    /// </remarks>
    [Test]
    public async Task ASelectForUpdateLockHandlerReportsCancellationAsCancellation()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        var provider = new FailingDbProvider();
        var lockHandler = new SelectForUpdateLockHandler(provider) { MaxRetry = 1 };
        lockHandler.Initialize(Context(new FakeTimeProvider()));

        using var connection = new FakeConnection();
        using var holder = new ConnectionAndTransactionHolder(connection, transaction: null);

        var act = async () => await lockHandler.AcquireLock(Guid.NewGuid(), holder, SchedulerLock.TriggerAccess, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <inheritdoc cref="ASelectForUpdateLockHandlerReportsCancellationAsCancellation" />
    [Test]
    public async Task AnUpdateRowLockHandlerReportsCancellationAsCancellation()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        var provider = new FailingDbProvider();
        var lockHandler = new SingleAttemptUpdateRowLockHandler(provider);
        lockHandler.Initialize(Context(new FakeTimeProvider()));

        using var connection = new FakeConnection();
        using var holder = new ConnectionAndTransactionHolder(connection, transaction: null);

        var act = async () => await lockHandler.AcquireLock(Guid.NewGuid(), holder, SchedulerLock.TriggerAccess, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// The retry count is <c>protected virtual</c> rather than settable, so "do not back off at all" is
    /// spelled by a subclass.
    /// </summary>
    private sealed class SingleAttemptUpdateRowLockHandler : UpdateRowLockHandler
    {
        public SingleAttemptUpdateRowLockHandler(IDbProvider provider) : base(provider)
        {
        }

        protected override int RetryCount => 1;
    }

    /// <summary>
    /// Advances the fake clock repeatedly rather than once, because there is no way to ask a
    /// <see cref="FakeTimeProvider"/> whether the code under test has registered its timer yet: a single
    /// advance that lands before the registration would leave the delay scheduled past it forever.
    /// </summary>
    private static async Task AdvanceUntilComplete(FakeTimeProvider clock, Task task, TimeSpan step)
    {
        for (int i = 0; i < 500 && !task.IsCompleted; i++)
        {
            clock.Advance(step);

            // Real time, not Task.Yield(): the handler's continuation between the failed statement and
            // its next Task.Delay registration runs on a thread-pool thread, and on a busy runner five
            // hundred yields can pass in microseconds without that thread ever being scheduled - the
            // advance then lands before the timer exists and releases nothing.
            await Task.Delay(TimeSpan.FromMilliseconds(2));
        }

        // Asserted rather than left to hang: a handler that went back to waiting on wall time would
        // otherwise stall the run for the whole backoff instead of reporting what broke.
        task.IsCompleted.Should().BeTrue(
            "advancing the store's clock past the backoff has to be enough to let the retry through");
    }

    private static async Task WaitUntil(Func<bool> condition, Task running)
    {
        for (int i = 0; i < 500 && !condition() && !running.IsCompleted; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(2));
        }

        condition().Should().BeTrue("the handler was expected to have issued its first statement by now");
    }

    /// <summary>
    /// A provider whose every statement fails, which is the case both retry loops exist for.
    /// </summary>
    private sealed class FailingDbProvider : IDbProvider
    {
        private int commandsExecuted;

        public int CommandsExecuted => Volatile.Read(ref commandsExecuted);

        public string ConnectionString => "";

        public DbMetadata Metadata { get; } = FakeDriver;

        public DbCommand CreateCommand() => new FailingCommand(() => Interlocked.Increment(ref commandsExecuted));

        public DbConnection CreateConnection() => new FakeConnection();

        public void Shutdown()
        {
        }
    }

    private sealed class FailingCommand : DbCommand
    {
        private readonly Action onExecute;

        public FailingCommand(Action onExecute) => this.onExecute = onExecute;

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string CommandText { get; set; } = "";

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; }

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection { get; set; }

        protected override DbParameterCollection DbParameterCollection { get; } = new FakeParameterCollection();

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery()
        {
            onExecute();
            throw new InvalidOperationException("deadlock detected");
        }

        public override object? ExecuteScalar()
        {
            onExecute();
            throw new InvalidOperationException("deadlock detected");
        }

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => new FakeParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            onExecute();
            throw new InvalidOperationException("deadlock detected");
        }
    }

    private sealed class FakeConnection : DbConnection
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = "";

        public override string Database => "";

        public override string DataSource => "";

        public override string ServerVersion => "";

        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close()
        {
        }

        public override void Open()
        {
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }

    private sealed class FakeParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> parameters = [];

        public override int Count => parameters.Count;

        public override object SyncRoot { get; } = new();

        public override int Add(object value)
        {
            parameters.Add((DbParameter) value);
            return parameters.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (object? value in values)
            {
                Add(value!);
            }
        }

        public override void Clear() => parameters.Clear();

        public override bool Contains(object value) => parameters.Contains((DbParameter) value);

        public override bool Contains(string value) => IndexOf(value) >= 0;

        public override void CopyTo(Array array, int index) => ((System.Collections.ICollection) parameters).CopyTo(array, index);

        public override System.Collections.IEnumerator GetEnumerator() => parameters.GetEnumerator();

        public override int IndexOf(object value) => parameters.IndexOf((DbParameter) value);

        public override int IndexOf(string parameterName) => parameters.FindIndex(x => x.ParameterName == parameterName);

        public override void Insert(int index, object value) => parameters.Insert(index, (DbParameter) value);

        public override void Remove(object value) => parameters.Remove((DbParameter) value);

        public override void RemoveAt(int index) => parameters.RemoveAt(index);

        public override void RemoveAt(string parameterName) => RemoveAt(IndexOf(parameterName));

        protected override DbParameter GetParameter(int index) => parameters[index];

        protected override DbParameter GetParameter(string parameterName) => parameters[IndexOf(parameterName)];

        protected override void SetParameter(int index, DbParameter value) => parameters[index] = value;

        protected override void SetParameter(string parameterName, DbParameter value) => parameters[IndexOf(parameterName)] = value;
    }

    private sealed class FakeParameter : DbParameter
    {
        public override DbType DbType { get; set; }

        public override ParameterDirection Direction { get; set; }

        public override bool IsNullable { get; set; }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ParameterName { get; set; } = "";

        public override int Size { get; set; }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string SourceColumn { get; set; } = "";

        public override bool SourceColumnNullMapping { get; set; }

        public override object? Value { get; set; }

        public override void ResetDbType()
        {
        }
    }
}
