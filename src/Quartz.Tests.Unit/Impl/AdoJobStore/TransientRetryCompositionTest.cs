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

using System.Data;
using System.Data.Common;

using FakeItEasy;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The two things about the transient-retry loop that only show up when its parts are composed: that
/// it is switched off inside a transaction the application owns, and that an attempt is rolled back
/// before the next one begins.
/// </summary>
/// <remarks>
/// <para>
/// The retry counts and the classification are pinned in <c>AdoJobStoreBaseTest</c>, and a holder's
/// rollback in <c>ConnectionAndTransactionHolderTest</c>. Neither says what happens when they meet,
/// which is the question a reader of <c>tutorial/job-stores.md</c> actually has: a retry that re-ran a
/// callback on top of a half-written attempt would double-write, and the reason it cannot is that the
/// first attempt is rolled back and the second is handed a connection of its own.
/// </para>
/// <para>
/// A <see cref="TimeoutException" /> is what fails here, so the classification under test is the
/// shipped one rather than an override the fixture supplied.
/// </para>
/// </remarks>
public sealed class TransientRetryCompositionTest
{
    /// <summary>
    /// Inside a transaction the application owns, the callback runs once and the failure is the
    /// caller's — which is what <c>job-stores.md</c> promises, and the only safe answer: on most
    /// providers the first failure has already doomed that transaction, so a second attempt piles
    /// another error on top of work the caller may still want to roll back deliberately.
    /// </summary>
    [Test]
    public async Task InsideACallerOwnedTransactionTheCallbackRunsOnceAndTheFailureIsReported()
    {
        RecordingRetryStore store = new("retry-enlisted", acceptEnlistedTransactions: true);

        using RecordingConnection applicationConnection = new();
        using RecordingTransaction applicationTransaction = new(applicationConnection);

        int calls = 0;
        Func<Task> act;

        using (Enlist("retry-enlisted", applicationTransaction))
        {
            act = async () => await store.Run<string>(_ =>
            {
                calls++;
                throw new JobPersistenceException("the database blinked", new TimeoutException());
            });

            await act.Should().ThrowAsync<JobPersistenceException>(
                "the error belongs to the caller, whose transaction it happened in");
        }

        calls.Should().Be(1,
            "retrying inside somebody else's transaction is pointless and harmful: the transaction is "
            + "already doomed, so the second attempt only replaces the diagnostic with a worse one");
    }

    /// <summary>
    /// The control: the same store, the same failure, nothing enlisted. Without this the case above
    /// would pass just as well for a store that had stopped retrying altogether.
    /// </summary>
    [Test]
    public async Task WithNothingEnlistedTheSameFailureIsRetried()
    {
        RecordingRetryStore store = new("retry-own", acceptEnlistedTransactions: true);

        int calls = 0;

        Func<Task> act = async () => await store.Run<string>(_ =>
        {
            calls++;
            throw new JobPersistenceException("the database blinked", new TimeoutException());
        });

        await act.Should().ThrowAsync<JobPersistenceException>();

        calls.Should().Be(4,
            "the store owns this transaction, so a timeout is worth trying again — three retries after "
            + "the first attempt");
    }

    /// <summary>
    /// The composition: an attempt that wrote and then failed is rolled back before the next one, and
    /// the next one is handed a different connection and transaction.
    /// </summary>
    /// <remarks>
    /// This is the whole reason a retry cannot double-write. Were the failed attempt left uncommitted
    /// but not rolled back, or were the retry handed the same holder, the second attempt would run on
    /// top of the first one's statements — and for trigger acquisition that means the same triggers
    /// handed out twice.
    /// </remarks>
    [Test]
    public async Task ARetryRollsBackTheFailedAttemptAndBeginsOnAConnectionOfItsOwn()
    {
        RecordingRetryStore store = new("retry-rollback", acceptEnlistedTransactions: false);

        List<ConnectionAndTransactionHolder> handed = [];
        int calls = 0;

        string result = await store.Run(conn =>
        {
            handed.Add(conn);
            calls++;

            if (calls == 1)
            {
                throw new JobPersistenceException("wrote, then lost the connection", new TimeoutException());
            }

            return new ValueTask<string>("second attempt");
        });

        result.Should().Be("second attempt");
        handed.Should().HaveCount(2).And.OnlyHaveUniqueItems(
            "each attempt opens a connection and begins a transaction of its own, so nothing the first "
            + "one wrote is still in scope for the second");

        store.Transactions.Should().HaveCount(2);

        store.Transactions[0].RollbackCalled.Should().BeTrue(
            "the failed attempt is rolled back before the retry is even decided on, which is what makes "
            + "the statements it managed to run disappear");
        store.Transactions[0].CommitCalled.Should().BeFalse();

        store.Transactions[1].CommitCalled.Should().BeTrue(
            "and the attempt that succeeded is the one that commits");
        store.Transactions[1].RollbackCalled.Should().BeFalse();
    }

    private static IDisposable Enlist(string schedulerName, DbTransaction transaction)
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns(schedulerName);

        return scheduler.EnlistTransaction(transaction);
    }

    /// <summary>
    /// A store whose locked calls run for real on a connection and transaction it hands out fresh each
    /// time, and remembers.
    /// </summary>
    private sealed class RecordingRetryStore : LocalTransactionJobStore
    {
        private readonly List<RecordingTransaction> transactions = [];

        internal RecordingRetryStore(string schedulerName, bool acceptEnlistedTransactions)
            : base(TestJobStores.Dependencies(
                schedulerOptions: TestJobStores.SchedulerOptions(schedulerName),
                storeOptions: TestJobStores.StoreOptions(configure: options =>
                {
                    options.MaxTransientRetries = 3;
                    options.TransientRetryInterval = TimeSpan.Zero;
                    options.AcceptEnlistedTransactions = acceptEnlistedTransactions;
                })))
        {
        }

        /// <summary>The transactions handed out, in the order the attempts asked for them.</summary>
        internal IReadOnlyList<RecordingTransaction> Transactions => transactions;

        protected override ValueTask<ConnectionAndTransactionHolder> GetLocalTransactionConnection(CancellationToken cancellationToken = default)
        {
            RecordingConnection connection = new();
            RecordingTransaction transaction = new(connection);
            transactions.Add(transaction);

            return new ValueTask<ConnectionAndTransactionHolder>(new ConnectionAndTransactionHolder(connection, transaction));
        }

        internal ValueTask<T> Run<T>(Func<ConnectionAndTransactionHolder, ValueTask<T>> callback)
        {
            return ExecuteInLocalTransactionLock(null, callback, cancellationToken: CancellationToken.None);
        }
    }

    private sealed class RecordingConnection : DbConnection
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

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => new RecordingTransaction(this);

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }

    private sealed class RecordingTransaction : DbTransaction
    {
        private DbConnection? connection;

        internal RecordingTransaction(DbConnection connection) => this.connection = connection;

        internal bool CommitCalled { get; private set; }

        internal bool RollbackCalled { get; private set; }

        // A provider drops the connection reference once the transaction completes, and the holder
        // reads that to decide whether there is anything left to roll back.
        protected override DbConnection? DbConnection => connection;

        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        public override void Commit()
        {
            CommitCalled = true;
            connection = null;
        }

        public override void Rollback()
        {
            RollbackCalled = true;
            connection = null;
        }
    }
}
