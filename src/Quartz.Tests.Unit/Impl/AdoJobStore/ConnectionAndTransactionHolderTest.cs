using System;
using System.Data;
using System.Data.Common;

using FakeItEasy;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

public sealed class ConnectionAndTransactionHolderTest
{
    [Test]
    public void Rollback_WhenTransactionDisconnected_SkipsRollbackWithoutError()
    {
        DbConnection connection = A.Fake<DbConnection>();
        TestDbTransaction transaction = new TestDbTransaction(dbConnection: null);

        ConnectionAndTransactionHolder holder = new ConnectionAndTransactionHolder(connection, transaction);

        holder.Rollback(transientError: false);

        Assert.That(transaction.RollbackCalled, Is.False);
    }

    [Test]
    public void Rollback_WhenTransactionConnected_CallsRollback()
    {
        DbConnection connection = A.Fake<DbConnection>();
        TestDbTransaction transaction = new TestDbTransaction(dbConnection: connection);

        ConnectionAndTransactionHolder holder = new ConnectionAndTransactionHolder(connection, transaction);

        holder.Rollback(transientError: false);

        Assert.That(transaction.RollbackCalled, Is.True);
    }

    [Test]
    public void Rollback_WhenTransactionIsNull_DoesNothing()
    {
        DbConnection connection = A.Fake<DbConnection>();

        ConnectionAndTransactionHolder holder = new ConnectionAndTransactionHolder(connection, transaction: null);

        holder.Rollback(transientError: false);
    }

    [Test]
    public void OwnsResources_DefaultsToTrue()
    {
        DbConnection connection = A.Fake<DbConnection>();

        ConnectionAndTransactionHolder holder = new ConnectionAndTransactionHolder(connection, transaction: null);

        holder.OwnsResources.Should().BeTrue("a holder that opened its own connection is responsible for it");
    }

    [Test]
    public void BorrowedHolder_LeavesTheApplicationsTransactionAlone()
    {
        DbConnection connection = A.Fake<DbConnection>();
        TestDbTransaction transaction = new TestDbTransaction(dbConnection: connection);

        ConnectionAndTransactionHolder holder = new ConnectionAndTransactionHolder(connection, transaction, ownsResources: false);

        holder.Commit(openNewTransaction: false);
        holder.Rollback(transientError: false);

        holder.OwnsResources.Should().BeFalse();
        transaction.CommitCalled.Should().BeFalse("the application decides when its transaction commits");
        transaction.RollbackCalled.Should().BeFalse("rolling back here would discard the application's own work too");
    }

    [Test]
    public void BorrowedHolder_LeavesTheApplicationsConnectionOpen()
    {
        TestDbConnection connection = new TestDbConnection();
        TestDbTransaction transaction = new TestDbTransaction(dbConnection: connection);

        ConnectionAndTransactionHolder holder = new ConnectionAndTransactionHolder(connection, transaction, ownsResources: false);

        holder.Close();
        holder.Dispose();

        connection.CloseCalled.Should().BeFalse("the application keeps using its connection after we are done");
        connection.DisposeCalled.Should().BeFalse();
    }

    [Test]
    public void OwnedHolder_ClosesItsConnection()
    {
        TestDbConnection connection = new TestDbConnection();

        ConnectionAndTransactionHolder holder = new ConnectionAndTransactionHolder(connection, transaction: null);

        holder.Close();

        connection.CloseCalled.Should().BeTrue();
    }

    private sealed class TestDbConnection : DbConnection
    {
        public bool CloseCalled { get; private set; }

        public bool DisposeCalled { get; private set; }

        public override string ConnectionString { get; set; } = "";

        public override string Database => "";

        public override string DataSource => "";

        public override string ServerVersion => "";

        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close() => CloseCalled = true;

        public override void Open()
        {
        }

        protected override void Dispose(bool disposing)
        {
            DisposeCalled = true;
            base.Dispose(disposing);
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }

    private sealed class TestDbTransaction : DbTransaction
    {
        private readonly DbConnection dbConnection;

        public TestDbTransaction(DbConnection dbConnection)
        {
            this.dbConnection = dbConnection;
        }

        public bool RollbackCalled { get; private set; }

        public bool CommitCalled { get; private set; }

        protected override DbConnection DbConnection => dbConnection;

        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        public override void Commit()
        {
            CommitCalled = true;
        }

        public override void Rollback()
        {
            RollbackCalled = true;
        }
    }
}
