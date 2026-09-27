#nullable enable

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Time.Testing;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// A lock statement whose failure ended the transaction — the loser of a write conflict on a
/// memory-optimized <c>QRTZ_LOCKS</c> (SQL Server error 41302), a deadlock victim — is not retried inside
/// it. SQL Server runs a command still bound to an ended <c>SqlTransaction</c> outside any transaction,
/// so the retry that used to follow could report a lock nobody held and let the operation's statements
/// commit one by one, until the store's own commit found the transaction gone (#3903). The handler
/// reports the failure instead, and the store retries the whole operation on a fresh transaction.
/// </summary>
public class DbLockHandlerEndedTransactionTest
{
    private static readonly DbMetadata FakeDriver = new()
    {
        ProductName = "Fake",
        ParameterNamePrefix = "@",
        BindByName = true,
    };

    /// <summary>
    /// Bounds a handler that still backs off: a wait on the fake clock never elapses, so without this a
    /// regression would hang the test instead of failing it.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Test]
    public async Task AnUpdateRowLockHandlerStopsRetryingOnceTheDatabaseHasEndedTheTransaction()
    {
        using FakeConnection connection = new();
        EndableTransaction transaction = new(connection);
        ConflictingDbProvider provider = new(transaction.End);

        UpdateRowLockHandler lockHandler = new(provider);
        lockHandler.Initialize(Context(new FakeTimeProvider()));
        using ConnectionAndTransactionHolder holder = new(connection, transaction);

        Func<Task> act = () => lockHandler.AcquireLock(Guid.NewGuid(), holder, SchedulerLock.TriggerAccess).AsTask().WaitAsync(Patience);

        (await act.Should().ThrowAsync<LockException>(
                "the conflict is reported to the store, whose transient retry runs the operation again on a fresh transaction"))
            .WithInnerException<InvalidOperationException>()
            .WithMessage(WriteConflict, "the failure that ended the transaction is the cause the store classifies");

        provider.CommandsExecuted.Should().Be(1,
            "a second attempt inside the ended transaction would run outside any transaction and report a lock nobody holds");
    }

    [Test]
    public async Task ASelectForUpdateLockHandlerStopsRetryingOnceTheDatabaseHasEndedTheTransaction()
    {
        using FakeConnection connection = new();
        EndableTransaction transaction = new(connection);
        ConflictingDbProvider provider = new(transaction.End);

        SelectForUpdateLockHandler lockHandler = new(provider);
        lockHandler.Initialize(Context(new FakeTimeProvider()));
        using ConnectionAndTransactionHolder holder = new(connection, transaction);

        Func<Task> act = () => lockHandler.AcquireLock(Guid.NewGuid(), holder, SchedulerLock.TriggerAccess).AsTask().WaitAsync(Patience);

        (await act.Should().ThrowAsync<LockException>(
                "a deadlock victim's transaction is gone too, and the same retry would run the same way"))
            .WithInnerException<InvalidOperationException>()
            .WithMessage(WriteConflict);

        provider.CommandsExecuted.Should().Be(1, "MaxRetry is 3, and none of the other two attempts has a transaction to run in");
    }

    [Test]
    public async Task AFailureThatLeavesTheTransactionAliveIsStillRetried()
    {
        using FakeConnection connection = new();
        EndableTransaction transaction = new(connection);
        ConflictingDbProvider provider = new(onExecute: () => { });

        // The real clock, with the shortest backoff a timer will take: this test is about the number of
        // attempts, not about what they wait on, which DbLockHandlerRetryTest covers.
        UpdateRowLockHandler lockHandler = new(provider) { RetryPeriod = TimeSpan.FromMilliseconds(1) };
        lockHandler.Initialize(Context(TimeProvider.System));
        using ConnectionAndTransactionHolder holder = new(connection, transaction);

        Func<Task> act = () => lockHandler.AcquireLock(Guid.NewGuid(), holder, SchedulerLock.TriggerAccess).AsTask().WaitAsync(Patience);

        await act.Should().ThrowAsync<LockException>();

        provider.CommandsExecuted.Should().Be(2,
            "a failure the database did not end the transaction over is what the handler's own retry is for");
    }

    private const string WriteConflict = "The current transaction attempted to update a record that has been updated since this transaction started. The transaction was aborted.";

    private static LockHandlerContext Context(TimeProvider timeProvider) => new()
    {
        SchedulerName = "TESTSCHED",
        InstanceId = "node-1",
        TablePrefix = "QRTZ_",
        TimeProvider = timeProvider,
    };

    /// <summary>
    /// A transaction the database can end from its side, which a driver reports the way SqlClient does:
    /// the transaction object stays, and its connection is gone.
    /// </summary>
    private sealed class EndableTransaction(DbConnection connection) : DbTransaction
    {
        private DbConnection? connection = connection;

        public void End() => connection = null;

        protected override DbConnection? DbConnection => connection;

        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        public override void Commit()
        {
        }

        public override void Rollback()
        {
        }
    }

    /// <summary>
    /// A provider whose every statement fails as the loser of a write conflict does, after running the
    /// callback that decides whether the failure also ended the transaction.
    /// </summary>
    private sealed class ConflictingDbProvider(Action onExecute) : IDbProvider
    {
        private int commandsExecuted;

        public int CommandsExecuted => Volatile.Read(ref commandsExecuted);

        public string ConnectionString => "";

        public DbMetadata Metadata { get; } = FakeDriver;

        public DbCommand CreateCommand() => new ConflictingCommand(() =>
        {
            Interlocked.Increment(ref commandsExecuted);
            onExecute();
        });

        public DbConnection CreateConnection() => new FakeConnection();

        public void Shutdown()
        {
        }
    }

    private sealed class ConflictingCommand(Action onExecute) : DbCommand
    {
        [AllowNull]
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

        public override int ExecuteNonQuery() => Fail();

        public override object? ExecuteScalar() => Fail();

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => new FakeParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            Fail();
            return null!;
        }

        private int Fail()
        {
            onExecute();
            throw new InvalidOperationException(WriteConflict);
        }
    }
}
