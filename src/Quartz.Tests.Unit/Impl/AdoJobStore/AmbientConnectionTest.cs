#nullable enable

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

using System.Data;
using System.Data.Common;
using System.Transactions;

using FakeItEasy;

using Quartz.Core;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;


using IsolationLevel = System.Data.IsolationLevel;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// Covers the job store using a connection the application enlisted, and staying out of an ambient
/// <see cref="TransactionScope" /> when nothing was enlisted.
/// </summary>
public sealed class AmbientConnectionTest
{
    private const string SchedulerName = "AmbientConnectionTestScheduler";

    [Test]
    public async Task EnlistTransaction_MakesTheJobStoreUseTheApplicationsConnection()
    {
        var applicationConnection = new RecordingDbConnection();
        applicationConnection.Open();
        DbTransaction applicationTransaction = applicationConnection.BeginTransaction();

        var jobStore = CreateJobStore(acceptEnlistedTransactions: true, out var ownConnection);

        using (CreateScheduler().EnlistTransaction(applicationTransaction))
        {
            var holder = await jobStore.CallGetConnection();

            holder.Connection.Should().BeSameAs(applicationConnection);
            holder.Transaction.Should().BeSameAs(applicationTransaction);
            holder.OwnsResources.Should().BeFalse("the application owns what it enlisted");
        }

        ownConnection.OpenCount.Should().Be(0, "the job store should not have opened a connection of its own");
        applicationConnection.BeginTransactionCount.Should().Be(1, "only the application's own transaction was started");
    }

    [Test]
    public async Task EnlistConnection_OpensTheConnectionWhenItIsClosed()
    {
        var applicationConnection = new RecordingDbConnection();
        var jobStore = CreateJobStore(acceptEnlistedTransactions: true, out _);

        using (new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled))
        using (CreateScheduler().EnlistConnection(applicationConnection))
        {
            var holder = await jobStore.CallGetConnection();

            holder.Connection.Should().BeSameAs(applicationConnection);
            holder.Transaction.Should().BeNull("the ambient scope owns the transaction");
        }

        applicationConnection.OpenCount.Should().Be(1);
        applicationConnection.BeginTransactionCount.Should().Be(0, "an enlisted connection joins the transaction the application already has");
    }

    [Test]
    public void EnlistConnection_WithNothingToJoin_IsRefused()
    {
        var applicationConnection = new RecordingDbConnection();

        Action enlist = () => CreateScheduler().EnlistConnection(applicationConnection);

        enlist.Should().Throw<ArgumentException>()
            .WithParameterName("connection")
            .WithMessage("*no transaction to join*",
                "without one every statement would commit on the spot while looking like a working enlistment");
    }

    [Test]
    public void EnlistConnection_WithATransactionFromAnotherConnection_IsRefused()
    {
        var applicationConnection = new RecordingDbConnection();
        var otherConnection = new RecordingDbConnection();
        otherConnection.Open();
        DbTransaction foreign = otherConnection.BeginTransaction();

        Action enlist = () => CreateScheduler().EnlistConnection(applicationConnection, foreign);

        enlist.Should().Throw<ArgumentException>()
            .WithParameterName("transaction")
            .WithMessage("*belongs to a different connection*");
    }

    [Test]
    public async Task Enlistment_EndsWhenTheScopeIsDisposed()
    {
        var applicationConnection = new RecordingDbConnection();
        var jobStore = CreateJobStore(acceptEnlistedTransactions: true, out var ownConnection);

        using (Enlist(CreateScheduler(), applicationConnection))
        {
        }

        var holder = await jobStore.CallGetConnection();

        holder.Connection.Should().BeSameAs(ownConnection);
        holder.OwnsResources.Should().BeTrue();
    }

    [Test]
    public async Task Enlistment_IsScopedToTheScheduler()
    {
        var applicationConnection = new RecordingDbConnection();
        var jobStore = CreateJobStore(acceptEnlistedTransactions: true, out var ownConnection);

        using (Enlist(CreateScheduler("SomeOtherScheduler"), applicationConnection))
        {
            var holder = await jobStore.CallGetConnection();

            holder.Connection.Should().BeSameAs(ownConnection, "the enlistment belongs to a different scheduler");
        }
    }

    [Test]
    public async Task Enlistment_Nests()
    {
        var outerConnection = new RecordingDbConnection();
        var innerConnection = new RecordingDbConnection();
        var jobStore = CreateJobStore(acceptEnlistedTransactions: true, out _);

        using (Enlist(CreateScheduler(), outerConnection))
        {
            using (Enlist(CreateScheduler(), innerConnection))
            {
                (await jobStore.CallGetConnection()).Connection.Should().BeSameAs(innerConnection);
            }

            (await jobStore.CallGetConnection()).Connection.Should().BeSameAs(outerConnection, "disposing the inner scope restores the outer one");
        }
    }

    [Test]
    public async Task Enlistment_DoesNotLeakIntoFlowsStartedOutsideIt()
    {
        var applicationConnection = new RecordingDbConnection();
        var jobStore = CreateJobStore(acceptEnlistedTransactions: true, out var ownConnection);

        // Started outside the scope, so it must never see the enlistment however long it runs. The gate
        // makes the enlistment exist before the flow does any work, which a plain static field would fail.
        var enlisted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unrelatedFlow = Task.Run(async () =>
        {
            await enlisted.Task;
            return (await jobStore.CallGetConnection()).Connection;
        });

        using (Enlist(CreateScheduler(), applicationConnection))
        {
            enlisted.SetResult(true);

            (await unrelatedFlow).Should().BeSameAs(ownConnection);
        }
    }

    [Test]
    public async Task ForkingInsideAnEnlistment_RefusesTheSecondConcurrentUse()
    {
        var applicationConnection = new RecordingDbConnection();
        var jobStore = CreateJobStore(acceptEnlistedTransactions: true, out _);

        using (Enlist(CreateScheduler(), applicationConnection))
        {
            // Both children inherit the enlistment - that is the point of AsyncLocal - and one connection
            // cannot serve two operations, so the loser is told why rather than left to hit
            // "a command is already in progress" from the provider.
            var held = await jobStore.CallGetConnection();

            Func<Task> forked = () => Task.Run(async () => await jobStore.CallGetConnection());

            await forked.Should().ThrowAsync<JobPersistenceException>()
                .WithMessage("*cannot be used concurrently*");

            await jobStore.CallCleanupConnection(held);
        }
    }

    [Test]
    public async Task Enlistment_IsRefusedWhenTheStoreDoesNotAcceptIt()
    {
        var applicationConnection = new RecordingDbConnection();
        var jobStore = CreateJobStore(acceptEnlistedTransactions: false, out _);

        using (Enlist(CreateScheduler(), applicationConnection))
        {
            Func<Task> operate = async () => await jobStore.CallGetConnection();

            // Ignoring it would commit the scheduling separately while the caller believes it is part of
            // their transaction. This is also what covers a scheduler the call site could not inspect.
            await operate.Should().ThrowAsync<JobPersistenceException>()
                .WithMessage("*not configured to take part in transactions the application owns*");
        }
    }

    [Test]
    public async Task UsingAnEnlistmentAfterItsTransactionCompleted_IsRefused()
    {
        var applicationConnection = new RecordingDbConnection();
        applicationConnection.Open();
        DbTransaction applicationTransaction = applicationConnection.BeginTransaction();

        var jobStore = CreateJobStore(acceptEnlistedTransactions: true, out _);

        using (CreateScheduler().EnlistTransaction(applicationTransaction))
        {
            applicationTransaction.Commit();

            Func<Task> afterCommit = async () => await jobStore.CallGetConnection();

            // Carrying on would attach a completed transaction, or - worse - run in autocommit where a
            // half-finished write can no longer be rolled back.
            await afterCommit.Should().ThrowAsync<JobPersistenceException>()
                .WithMessage("*already been committed or rolled back*");
        }
    }

    [Test]
    public async Task SchedulerOwnedWorkInsideAnAmbientScope_StaysOutOfIt()
    {
        var applicationConnection = new RecordingDbConnection();
        var jobStore = CreateJobStore(acceptEnlistedTransactions: true, out var ownConnection);

        using (new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled))
        using (CreateScheduler().EnlistConnection(applicationConnection))
        using (AmbientConnection.Suppress())
        {
            var holder = await jobStore.CallGetConnection();

            holder.Connection.Should().BeSameAs(ownConnection, "suppressed work must not borrow the application's connection");
            holder.Transaction.Should().NotBeNull("suppressed work runs in a transaction of its own");
            ownConnection.EnlistedIn.Should().BeNull("and that transaction must not be the application's");
        }
    }

    [Test]
    public async Task AmbientTransactionScopeAlone_DoesNotMakeTheJobStoreJoinIt()
    {
        var jobStore = CreateJobStore(acceptEnlistedTransactions: true, out var ownConnection);

        using (new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled))
        {
            var holder = await jobStore.CallGetConnection();

            // Taking part means handing over a connection. Joining the scope with a connection of our own
            // would put a second connection in it - a distributed transaction, which not every provider
            // supports - and leave the commit outside our control.
            holder.Connection.Should().BeSameAs(ownConnection);
            holder.Transaction.Should().NotBeNull("the job store manages its own connection's transaction");
            holder.OwnsResources.Should().BeTrue();
            ownConnection.EnlistedIn.Should().BeNull("the job store's own connection stays out of the ambient transaction");
        }
    }

    [Test]
    public async Task AmbientTransactionScope_IsLeftAloneWhenTheFeatureIsOff()
    {
        var jobStore = CreateJobStore(acceptEnlistedTransactions: false, out var ownConnection);

        using (new TransactionScope(TransactionScopeOption.RequiresNew, TransactionScopeAsyncFlowOption.Enabled))
        {
            var holder = await jobStore.CallGetConnection();

            holder.Transaction.Should().NotBeNull("behaviour must not change unless the feature is switched on");
            ownConnection.EnlistedIn.Should().NotBeNull(
                "with the feature off the connection enlists as it always did - suppressing it would be a behaviour change of its own");
        }
    }

    [Test]
    public void EnlistTransaction_RejectsATransactionThatLostItsConnection()
    {
        DbTransaction orphaned = new OrphanedDbTransaction();

        Action enlist = () => CreateScheduler().EnlistTransaction(orphaned);

        enlist.Should().Throw<ArgumentException>().WithParameterName("transaction");
    }

    [Test]
    public async Task WithoutAnApplicationTransaction_TheJobStoreStartsItsOwn()
    {
        var jobStore = CreateJobStore(acceptEnlistedTransactions: true, out var ownConnection);

        var holder = await jobStore.CallGetConnection();

        holder.Connection.Should().BeSameAs(ownConnection);
        holder.Transaction.Should().NotBeNull();
        holder.OwnsResources.Should().BeTrue();
        ownConnection.BeginTransactionCount.Should().Be(1);
    }

    [Test]
    public void EnlistingOnASchedulerWithoutAPersistentStore_IsRefused()
    {
        // The fakes used elsewhere in this fixture are not StdScheduler, so the call-site guard
        // short-circuits for them; this drives it through a real one.
        var scheduler = new StdScheduler(BuildRamScheduler());

        var connection = new RecordingDbConnection();
        connection.Open();

        Action enlist = () => scheduler.EnlistTransaction(connection.BeginTransaction());

        enlist.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not store anything in the application's database*",
                "an in-memory store would let the scheduling survive the application's rollback");
    }

    [Test]
    public async Task EnlistingThroughADecorator_ReachesTheSameJobStore()
    {
        // The guard unwraps decorators, and the enlistment is keyed by the name the job store resolves
        // rather than by whatever the outermost wrapper reports.
        var applicationConnection = new RecordingDbConnection();
        applicationConnection.Open();

        var jobStore = CreateJobStore(acceptEnlistedTransactions: true, out _);
        var scheduler = new DelegatingScheduler(CreateScheduler());

        using (scheduler.EnlistTransaction(applicationConnection.BeginTransaction()))
        {
            var holder = await jobStore.CallGetConnection();

            holder.Connection.Should().BeSameAs(applicationConnection);
        }
    }

    /// <summary>
    /// Enlists a connection together with a transaction of its own, the shape most callers use.
    /// </summary>
    private static IDisposable Enlist(IScheduler scheduler, RecordingDbConnection connection)
    {
        if (connection.State == ConnectionState.Closed)
        {
            connection.Open();
        }

        return scheduler.EnlistTransaction(connection.BeginTransaction());
    }

    private static QuartzScheduler BuildRamScheduler()
    {
        var resources = new QuartzSchedulerResources
        {
            Name = "AmbientConnectionGuardScheduler",
            InstanceId = "NON_CLUSTERED",
            ThreadPool = new DefaultThreadPool(),
            JobStore = TestJobStores.Ram(),
            JobRunShellFactory = new StdJobRunShellFactory(TestJobStores.Logger<Quartz.Core.JobRunShell>()),
            TimeProvider = TimeProvider.System,
        };

        return new QuartzScheduler(resources);
    }

    private static IScheduler CreateScheduler(string name = SchedulerName)
    {
        var scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns(name);
        return scheduler;
    }

    private static TestJobStore CreateJobStore(bool acceptEnlistedTransactions, out RecordingDbConnection ownConnection)
    {
        var connection = new RecordingDbConnection();
        ownConnection = connection;

        return new TestJobStore(
            new RecordingDbProvider(connection),
            TestJobStores.StoreOptions(configure: options =>
            {
                options.AcceptEnlistedTransactions = acceptEnlistedTransactions;
            }),
            SchedulerName);
    }

    private sealed class TestJobStore : LocalTransactionJobStore
    {
        internal TestJobStore(
            IDbProvider dbProvider,
            Microsoft.Extensions.Options.IOptions<AdoJobStoreOptions> storeOptions,
            string instanceName)
            : base(TestJobStores.Dependencies(
                schedulerOptions: TestJobStores.SchedulerOptions(instanceName),
                storeOptions: storeOptions,
                dbProvider: dbProvider))
        {
        }

        internal ValueTask<ConnectionAndTransactionHolder> CallGetConnection() => GetConnection();

        internal ValueTask CallCleanupConnection(ConnectionAndTransactionHolder conn) => CleanupConnection(conn);
    }

    private sealed class RecordingDbProvider : IDbProvider
    {
        private readonly DbConnection connection;

        internal RecordingDbProvider(DbConnection connection)
        {
            this.connection = connection;
        }

        public string ConnectionString { get; set; } = "";

        public DbMetadata Metadata { get; } = new();

        public void Initialize()
        {
        }

        public DbCommand CreateCommand() => throw new NotSupportedException();

        public DbConnection CreateConnection() => connection;

        public void Shutdown()
        {
        }
    }

    private sealed class RecordingDbConnection : DbConnection
    {
        private ConnectionState state = ConnectionState.Closed;

        internal int OpenCount { get; private set; }

        internal int BeginTransactionCount { get; private set; }

        /// <summary>
        /// The ambient transaction this connection would have auto-enlisted in when it was opened,
        /// which is how ADO.NET providers behave unless enlistment is suppressed or switched off.
        /// </summary>
        internal Transaction? EnlistedIn { get; private set; }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = "";

        public override string Database => "";

        public override string DataSource => "";

        public override string ServerVersion => "";

        public override ConnectionState State => state;

        public override void ChangeDatabase(string databaseName)
        {
        }

        public override void Close() => state = ConnectionState.Closed;

        public override void Open()
        {
            OpenCount++;
            state = ConnectionState.Open;
            EnlistedIn = Transaction.Current;
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            BeginTransactionCount++;
            return new RecordingDbTransaction(this, isolationLevel);
        }

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }

    private sealed class RecordingDbTransaction : DbTransaction
    {
        private readonly IsolationLevel isolationLevel;
        private DbConnection? connection;

        internal RecordingDbTransaction(DbConnection connection, IsolationLevel isolationLevel)
        {
            this.connection = connection;
            this.isolationLevel = isolationLevel;
        }

        // ADO.NET providers drop the connection reference once the transaction completes, which is how
        // a completed transaction is recognised.
        protected override DbConnection? DbConnection => connection;

        public override IsolationLevel IsolationLevel => isolationLevel;

        public override void Commit() => connection = null;

        public override void Rollback() => connection = null;
    }

    private sealed class OrphanedDbTransaction : DbTransaction
    {
        protected override DbConnection? DbConnection => null;

        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        public override void Commit()
        {
        }

        public override void Rollback()
        {
        }
    }
}
