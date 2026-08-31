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
using System.Diagnostics.CodeAnalysis;

using FakeItEasy;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// What the ADO store's lock and transaction plumbing makes of a cancellation. The rule is one
/// sentence: the caller asking to stop is reported as itself, and anything else is classified as it
/// always was.
/// </summary>
/// <remarks>
/// <para>
/// The guard around a single delegate call has said this since #3499, but the plumbing around it did
/// not: <c>ExecuteInLocalTransactionLock</c> converted every failure it had no other name for into
/// <c>JobPersistenceException("Unexpected runtime exception: …")</c>, and the two connection-opening
/// blocks into <c>Failed to obtain DB connection</c>. A shutdown or a timed-out request therefore
/// reached the caller as a database that had broken, which is #3503.
/// </para>
/// <para>
/// Stated over a connection double rather than over a database, because every claim here is about
/// which exception leaves the method and none of them is about SQL.
/// <c>AdoCancellationSqliteTest</c> makes the same claims against a file.
/// </para>
/// </remarks>
public class AdoJobStoreCancellationTest
{
    /// <summary>
    /// The case #3503 is about: the token fires while a locked, transacted operation is running.
    /// </summary>
    [Test]
    public async Task ACancellationInsideALockedOperationIsReportedAsCancellation()
    {
        using CancellationTokenSource cancellation = new();
        CancellationTestStore store = CancellationTestStore.Create();

        Func<Task> act = async () => await store.Run<object?>(SchedulerLock.TriggerAccess, _ =>
        {
            // What a provider does when the token it was handed fires mid-statement.
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
            return new ValueTask<object?>((object?) null);
        }, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "the caller asked to stop, and a caller matching on cancellation has to see cancellation "
            + "rather than a report that the database failed");
    }

    /// <summary>
    /// The lock is released on the way out, so the operation after a cancelled one is not waiting on a
    /// lock nobody is holding.
    /// </summary>
    [Test]
    public async Task ACancelledOperationStillReleasesItsLock()
    {
        using CancellationTokenSource cancellation = new();
        CancellationTestStore store = CancellationTestStore.Create();

        Func<Task> cancelled = async () => await store.Run<object?>(SchedulerLock.TriggerAccess, _ =>
        {
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
            return new ValueTask<object?>((object?) null);
        }, cancellation.Token);

        await cancelled.Should().ThrowAsync<OperationCanceledException>();

        // A leaked lock would park this forever rather than fail, so it is given a deadline of its own.
        Task<string> next = store.Run(SchedulerLock.TriggerAccess, _ => new ValueTask<string>("done"), CancellationToken.None).AsTask();

        (await next.WaitAsync(TimeSpan.FromSeconds(30))).Should().Be("done",
            "the lock the cancelled operation took was released in its finally, so the next one can take it");
    }

    /// <summary>
    /// The other half of the rule, and the reason the fix is not "let every
    /// <see cref="OperationCanceledException" /> through". The caller's token is not cancelled here:
    /// something inside the store gave up on its own, the caller did not ask for anything to stop, and
    /// telling them the operation was cancelled would answer a question they never asked.
    /// </summary>
    [Test]
    public async Task ACancellationTheCallerDidNotAskForIsStillAPersistenceFailure()
    {
        using CancellationTokenSource unrelated = new();
        await unrelated.CancelAsync();

        CancellationTestStore store = CancellationTestStore.Create();

        Func<Task> act = async () => await store.Run<object?>(
            SchedulerLock.TriggerAccess,
            _ => throw new OperationCanceledException(unrelated.Token),
            CancellationToken.None);

        await act.Should().ThrowAsync<JobPersistenceException>(
                "the caller's token never fired, so from where they stand the store failed")
            .WithMessage("Unexpected runtime exception*");
    }

    /// <summary>
    /// The retry loop keeps trying until the store shuts down, which is right for a database that is
    /// temporarily gone and wrong for a caller who has asked to stop.
    /// </summary>
    /// <remarks>
    /// The reported failure is what makes this observable: the loop tells every scheduler listener that
    /// the store failed, and it does so on the very first attempt here because the threshold is set to
    /// one. Cancellation is not that news.
    /// </remarks>
    [Test]
    public async Task TheRetryLoopStopsOnCancellationWithoutReportingAStoreFailure()
    {
        using CancellationTokenSource cancellation = new();
        ISchedulerSignaler signaler = A.Fake<ISchedulerSignaler>();
        CancellationTestStore store = CancellationTestStore.Create(signaler: signaler, errorLogThreshold: 1);

        int attempts = 0;
        Func<Task> act = async () => await store.RunWithRetry<object?>(SchedulerLock.TriggerAccess, _ =>
        {
            attempts++;
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
            return new ValueTask<object?>((object?) null);
        }, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        attempts.Should().Be(1, "there is nothing to retry once the caller has asked to stop");
        A.CallTo(() => signaler.NotifySchedulerListenersError(A<SchedulerErrorContext>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    /// <summary>
    /// A token that is already cancelled when the operation starts never reaches the callback: the
    /// connection open is the first thing that observes it. That block used to report the caller's own
    /// cancellation as a data source it could not reach.
    /// </summary>
    [Test]
    public async Task ACancelledTokenIsNotReportedAsAFailureToObtainAConnection()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        TestConnection connection = new();
        CancellationTestStore store = CancellationTestStore.Create(connection);

        Func<Task> act = async () => await store.Run(
            SchedulerLock.TriggerAccess,
            _ => new ValueTask<string>("never runs"),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "nothing is wrong with the data source - the caller cancelled before the connection opened");
        connection.Opened.Should().BeFalse("a cancelled token is observed before the connection is opened");
    }

    /// <summary>
    /// And when the token fires between the open and the transaction, the connection that was opened is
    /// still closed on the way out. That is why the block catches rather than filters.
    /// </summary>
    [Test]
    public async Task ACancellationWhileTheTransactionIsStartedStillClosesTheConnection()
    {
        using CancellationTokenSource cancellation = new();

        // Cancels once the connection is open, which puts the failure in BeginTransactionAsync.
        TestConnection connection = new(onOpen: () => cancellation.Cancel());
        CancellationTestStore store = CancellationTestStore.Create(connection);

        Func<Task> act = async () => await store.Run(
            SchedulerLock.TriggerAccess,
            _ => new ValueTask<string>("never runs"),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        connection.Opened.Should().BeTrue("the cancellation is arranged to happen after the open");
        connection.Closed.Should().BeTrue(
            "the connection this block opened is its own to close, whichever way the failure is reported");
    }

    /// <summary>
    /// The container-managed store opens its connection in a block of its own, and had the same wrap.
    /// </summary>
    [Test]
    public async Task TheExternalTransactionStoreDoesNotReportCancellationAsAConnectionFailureEither()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        TestConnection connection = new();
        ExternalCancellationTestStore store = new(new TestDbProvider(() => connection));

        Func<Task> act = async () => await store.Connect(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// A store whose connection is whatever the test hands it, exposing the two lock-and-transaction
    /// entry points so a test can drive one directly.
    /// </summary>
    private sealed class CancellationTestStore : LocalTransactionJobStore
    {
        private CancellationTestStore(IDbProvider dbProvider, ISchedulerSignaler? signaler, int errorLogThreshold)
            : base(TestJobStores.Dependencies(
                signaler: signaler,
                storeOptions: TestJobStores.StoreOptions(configure: options =>
                {
                    options.RetryableActionErrorLogThreshold = errorLogThreshold;
                    options.TransientRetryInterval = TimeSpan.Zero;
                    options.DbRetryInterval = TimeSpan.Zero;
                }),
                dbProvider: dbProvider))
        {
        }

        public static CancellationTestStore Create(
            DbConnection? connection = null,
            ISchedulerSignaler? signaler = null,
            int errorLogThreshold = 4)
        {
            // A connection per call when the test did not name one, because the store opens a fresh one
            // for every operation and closes it again.
            Func<DbConnection> factory = connection is null
                ? static () => new TestConnection()
                : () => connection;

            return new CancellationTestStore(new TestDbProvider(factory), signaler, errorLogThreshold);
        }

        public ValueTask<T> Run<T>(
            SchedulerLock? lockKind,
            Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
            CancellationToken cancellationToken)
        {
            return ExecuteInLocalTransactionLock(lockKind, txCallback, cancellationToken: cancellationToken);
        }

        public ValueTask<T> RunWithRetry<T>(
            SchedulerLock? lockKind,
            Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
            CancellationToken cancellationToken)
        {
            return RetryExecuteInLocalTransactionLock(lockKind, txCallback, cancellationToken: cancellationToken);
        }
    }

    /// <inheritdoc cref="CancellationTestStore" />
    private sealed class ExternalCancellationTestStore : ExternalTransactionJobStore
    {
        public ExternalCancellationTestStore(IDbProvider dbProvider)
            : base(TestJobStores.Dependencies(
                storeOptions: TestJobStores.StoreOptions(configure: options => options.OpenConnection = true),
                dbProvider: dbProvider))
        {
        }

        public ValueTask<ConnectionAndTransactionHolder> Connect(CancellationToken cancellationToken)
        {
            return GetLocalTransactionConnection(cancellationToken);
        }
    }

    private sealed class TestDbProvider : IDbProvider
    {
        private readonly Func<DbConnection> factory;

        public TestDbProvider(Func<DbConnection> factory) => this.factory = factory;

        public string ConnectionString => "";

        public DbMetadata Metadata { get; } = new();

        public DbCommand CreateCommand() => throw new NotSupportedException();

        public DbConnection CreateConnection() => factory();

        public void Shutdown()
        {
        }
    }

    /// <summary>
    /// A connection that honours a cancellation token where a real driver does — which is in
    /// <see cref="DbConnection" />'s own async members, so those are deliberately not overridden.
    /// </summary>
    private sealed class TestConnection : DbConnection
    {
        private readonly Action? onOpen;
        private ConnectionState state = ConnectionState.Closed;

        public TestConnection(Action? onOpen = null) => this.onOpen = onOpen;

        public bool Opened { get; private set; }

        public bool Closed { get; private set; }

        [AllowNull]
        public override string ConnectionString { get; set; } = "";

        public override string Database => "";

        public override string DataSource => "";

        public override string ServerVersion => "";

        public override ConnectionState State => state;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close()
        {
            state = ConnectionState.Closed;
            Closed = true;
        }

        public override void Open()
        {
            state = ConnectionState.Open;
            Opened = true;
            onOpen?.Invoke();
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => new TestTransaction(this, isolationLevel);

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }

    private sealed class TestTransaction : DbTransaction
    {
        public TestTransaction(DbConnection connection, IsolationLevel isolationLevel)
        {
            DbConnection = connection;
            IsolationLevel = isolationLevel;
        }

        public override IsolationLevel IsolationLevel { get; }

        protected override DbConnection? DbConnection { get; }

        public override void Commit()
        {
        }

        public override void Rollback()
        {
        }
    }
}
