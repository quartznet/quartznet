using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// A lock statement whose failure ended the transaction — the loser of a write conflict on a
/// memory-optimized <c>QRTZ_LOCKS</c> (SQL Server error 41302), a deadlock victim (1205) — is not retried
/// inside it. SQL Server runs a command still bound to an ended <c>SqlTransaction</c> outside any
/// transaction, so the retry that used to follow could report a lock nobody held and let the
/// operation's statements commit one by one, until the store's own commit found the transaction gone.
/// The semaphore reports the failure instead, and the store retries the whole operation on a fresh
/// transaction.
/// </summary>
public class DbSemaphoreEndedTransactionTest
{
    private const string WriteConflict = "The current transaction attempted to update a record that has been updated since this transaction started. The transaction was aborted.";

    /// <summary>
    /// Bounds a semaphore that still backs off, so that a regression fails instead of taking the whole
    /// retry schedule.
    /// </summary>
    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    [Test]
    public async Task AnUpdateLockRowSemaphoreStopsRetryingOnceTheDatabaseHasEndedTheTransaction()
    {
        using FakeConnection connection = new FakeConnection();
        EndableTransaction transaction = new EndableTransaction(connection);
        ConflictingDbProvider provider = new ConflictingDbProvider(transaction.End);
        UpdateLockRowSemaphore semaphore = new UpdateLockRowSemaphore(provider) { SchedName = "TESTSCHED" };

        await ShouldReportTheConflict(semaphore, connection, transaction,
            "the conflict is reported to the store, whose transient retry runs the operation again on a fresh transaction");

        provider.CommandsExecuted.Should().Be(1,
            "a second attempt inside the ended transaction would run outside any transaction and report a lock nobody holds");
    }

    [Test]
    public async Task AMemoryOptimizedUpdateLockRowSemaphoreStopsRetryingOnceTheDatabaseHasEndedTheTransaction()
    {
        using FakeConnection connection = new FakeConnection();
        EndableTransaction transaction = new EndableTransaction(connection);
        ConflictingDbProvider provider = new ConflictingDbProvider(transaction.End);
        UpdateLockRowSemaphoreMOT semaphore = new UpdateLockRowSemaphoreMOT(provider) { SchedName = "TESTSCHED" };

        await ShouldReportTheConflict(semaphore, connection, transaction,
            "a memory-optimized row is where a contended update ends the transaction rather than waiting");

        provider.CommandsExecuted.Should().Be(1, "RetryCount is 5 here, and none of the other four attempts has a transaction to run in");
    }

    [Test]
    public async Task AStdRowLockSemaphoreStopsRetryingOnceTheDatabaseHasEndedTheTransaction()
    {
        using FakeConnection connection = new FakeConnection();
        EndableTransaction transaction = new EndableTransaction(connection);
        ConflictingDbProvider provider = new ConflictingDbProvider(transaction.End);
        StdRowLockSemaphore semaphore = new StdRowLockSemaphore(provider)
        {
            SchedName = "TESTSCHED",
            RetryPeriod = TimeSpan.FromMilliseconds(10),
        };

        await ShouldReportTheConflict(semaphore, connection, transaction,
            "a deadlock victim's transaction is gone too, and the same retry would run the same way");

        provider.CommandsExecuted.Should().Be(1, "MaxRetry is 3, and none of the other attempts has a transaction to run in");
    }

    [Test]
    public async Task AFailureThatLeavesTheTransactionAliveIsStillRetried()
    {
        using FakeConnection connection = new FakeConnection();
        EndableTransaction transaction = new EndableTransaction(connection);
        ConflictingDbProvider provider = new ConflictingDbProvider(() => { });
        UpdateLockRowSemaphore semaphore = new UpdateLockRowSemaphore(provider) { SchedName = "TESTSCHED" };

        using ConnectionAndTransactionHolder holder = new ConnectionAndTransactionHolder(connection, transaction);
        Func<Task> act = () => WithPatience(semaphore.ObtainLock(Guid.NewGuid(), holder, "TRIGGER_ACCESS"));

        await act.Should().ThrowAsync<LockException>();

        provider.CommandsExecuted.Should().Be(2,
            "a failure the database did not end the transaction over is what the semaphore's own retry is for");
    }

    private static async Task ShouldReportTheConflict(
        ISemaphore semaphore,
        DbConnection connection,
        DbTransaction transaction,
        string because)
    {
        using ConnectionAndTransactionHolder holder = new ConnectionAndTransactionHolder(connection, transaction);
        Func<Task> act = () => WithPatience(semaphore.ObtainLock(Guid.NewGuid(), holder, "TRIGGER_ACCESS"));

        (await act.Should().ThrowAsync<LockException>(because))
            .WithInnerException<InvalidOperationException>()
            .WithMessage(WriteConflict, "the failure that ended the transaction is the cause the store classifies");
    }

    private static async Task WithPatience(Task task)
    {
        Task first = await Task.WhenAny(task, Task.Delay(patience)).ConfigureAwait(false);
        if (first != task)
        {
            throw new TimeoutException($"The lock was neither obtained nor refused within {patience}.");
        }

        await task.ConfigureAwait(false);
    }

    /// <summary>
    /// A transaction the database can end from its side, which a driver reports the way SqlClient does:
    /// the transaction object stays, and its connection is gone.
    /// </summary>
    private sealed class EndableTransaction : DbTransaction
    {
        private DbConnection connection;

        public EndableTransaction(DbConnection connection)
        {
            this.connection = connection;
        }

        public void End() => connection = null;

        protected override DbConnection DbConnection => connection;

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
    private sealed class ConflictingDbProvider : IDbProvider
    {
        private readonly Action onExecute;
        private int commandsExecuted;

        public ConflictingDbProvider(Action onExecute)
        {
            this.onExecute = onExecute;
        }

        public int CommandsExecuted => Volatile.Read(ref commandsExecuted);

        public string ConnectionString { get; set; } = "";

        public DbMetadata Metadata { get; } = new DbMetadata
        {
            ProductName = "Fake",
            ParameterNamePrefix = "@",
            BindByName = true,
        };

        public void Initialize()
        {
        }

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

    private sealed class ConflictingCommand : DbCommand
    {
        private readonly Action onExecute;

        public ConflictingCommand(Action onExecute)
        {
            this.onExecute = onExecute;
        }

        public override string CommandText { get; set; } = "";

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; }

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection DbConnection { get; set; }

        protected override DbParameterCollection DbParameterCollection { get; } = new FakeParameterCollection();

        protected override DbTransaction DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery() => Fail();

        public override object ExecuteScalar() => Fail();

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => new FakeParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            Fail();
            return null;
        }

        private int Fail()
        {
            onExecute();
            throw new InvalidOperationException(WriteConflict);
        }
    }

    private sealed class FakeConnection : DbConnection
    {
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

    private sealed class FakeParameter : DbParameter
    {
        public override DbType DbType { get; set; }

        public override ParameterDirection Direction { get; set; }

        public override bool IsNullable { get; set; }

        public override string ParameterName { get; set; } = "";

        public override string SourceColumn { get; set; } = "";

        public override object Value { get; set; }

        public override bool SourceColumnNullMapping { get; set; }

        public override int Size { get; set; }

        public override void ResetDbType()
        {
        }
    }

    private sealed class FakeParameterCollection : DbParameterCollection
    {
        private readonly System.Collections.Generic.List<object> items = new System.Collections.Generic.List<object>();

        public override int Count => items.Count;

        public override object SyncRoot => items;

        public override int Add(object value)
        {
            items.Add(value);
            return items.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (object value in values)
            {
                items.Add(value);
            }
        }

        public override void Clear() => items.Clear();

        public override bool Contains(object value) => items.Contains(value);

        public override bool Contains(string value) => false;

        public override void CopyTo(Array array, int index) => ((System.Collections.ICollection) items).CopyTo(array, index);

        public override System.Collections.IEnumerator GetEnumerator() => items.GetEnumerator();

        public override int IndexOf(object value) => items.IndexOf(value);

        public override int IndexOf(string parameterName) => -1;

        public override void Insert(int index, object value) => items.Insert(index, value);

        public override void Remove(object value) => items.Remove(value);

        public override void RemoveAt(int index) => items.RemoveAt(index);

        public override void RemoveAt(string parameterName)
        {
        }

        protected override DbParameter GetParameter(int index) => (DbParameter) items[index];

        protected override DbParameter GetParameter(string parameterName) => null;

        protected override void SetParameter(int index, DbParameter value) => items[index] = value;

        protected override void SetParameter(string parameterName, DbParameter value)
        {
        }
    }
}
