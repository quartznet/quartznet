using System.Data;
using System.Data.Common;

using FakeItEasy;

using NUnit.Framework;

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

    private sealed class TestDbTransaction : DbTransaction
    {
        private readonly DbConnection dbConnection;

        public TestDbTransaction(DbConnection dbConnection)
        {
            this.dbConnection = dbConnection;
        }

        public bool RollbackCalled { get; private set; }

        protected override DbConnection DbConnection => dbConnection;

        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        public override void Commit()
        {
        }

        public override void Rollback()
        {
            RollbackCalled = true;
        }
    }
}
