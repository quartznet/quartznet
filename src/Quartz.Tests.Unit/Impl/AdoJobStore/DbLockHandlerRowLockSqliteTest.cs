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
using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The two shipped row-lock handlers against a real database: what a re-entrant acquire answers,
/// what a release from somebody else does, and what an abandoned acquire leaves behind.
/// </summary>
/// <remarks>
/// <para>
/// <c>DbLockHandlerRetryTest</c> pins the exception their retry loops end in, over a provider whose
/// every statement fails. What it cannot show is the other half of the contract on
/// <see cref="ILockHandler.AcquireLock" />: that the abandoned attempt recorded no ownership. That
/// matters because <see cref="DbLockHandler" /> answers <see langword="false" /> to a requestor it
/// already lists as an owner — the store's word for "you already hold this, do not release it" — so a
/// failed attempt that left a mark behind would make the requestor's next acquire look re-entrant, and
/// the operation it guards would run with no lock and never give one back. #3583.
/// </para>
/// <para>
/// The re-entry answer itself is what <c>how-tos/lock-handler.md</c> calls the single most important
/// rule of a lock handler, and it lives in the shared base — so one case over the source below covers
/// <see cref="UpdateRowLockHandler" />, <see cref="SelectForUpdateLockHandler" /> and the two
/// dialect subclasses that inherit it.
/// </para>
/// <para>
/// SQLite is a file, so both statements run for real here rather than only in the container legs. The
/// row lock itself is the transaction's rather than the handler's, and these handlers are given a
/// holder with no transaction; what is under test is the base class's bookkeeping, which is the same
/// whichever database is underneath.
/// </para>
/// </remarks>
public sealed class DbLockHandlerRowLockSqliteTest
{
    private const string SchedulerName = "row-locks";

    /// <summary>
    /// The stock <c>SELECT … FOR UPDATE</c> is not SQLite syntax, and the constructor that takes the
    /// statement is the supported way to give a dialect its own. The lock it takes is weaker here, which
    /// costs this test nothing: the bookkeeping under test happens after the statement has run.
    /// </summary>
    private const string SelectWithoutForUpdate =
        "SELECT * FROM {0}LOCKS WHERE SCHED_NAME = @schedulerName AND LOCK_NAME = @lockName";

    private string databaseFile = null!;
    private string connectionString = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-row-lock-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";
    }

    [TearDown]
    public void DeleteDatabase()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(databaseFile))
        {
            File.Delete(databaseFile);
        }
    }

    private static IEnumerable<TestCaseData> RowLockHandlers()
    {
        yield return new TestCaseData(new Func<IDbProvider, DbLockHandler>(
            provider => new UpdateRowLockHandler(provider))) { TestName = "{m}(UpdateRowLockHandler)" };

        yield return new TestCaseData(new Func<IDbProvider, DbLockHandler>(
            provider => new SelectForUpdateLockHandler(
                AdoConstants.DefaultTablePrefix,
                SchedulerName,
                SelectWithoutForUpdate,
                provider))) { TestName = "{m}(SelectForUpdateLockHandler)" };
    }

    /// <summary>
    /// The caller asked to stop, so that is what comes back — not a <see cref="LockException" />, which
    /// would say the database refused the lock, and not <see langword="false" />, which would say the
    /// caller already had it.
    /// </summary>
    [TestCaseSource(nameof(RowLockHandlers))]
    public async Task ACancelledAcquireReportsTheCancellation(Func<IDbProvider, DbLockHandler> create)
    {
        await ProvisionSchema();

        DbLockHandler lockHandler = Initialize(create);

        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();
        using ConnectionAndTransactionHolder holder = new(connection, transaction: null);

        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> act = async () => await lockHandler.AcquireLock(Guid.NewGuid(), holder, SchedulerLock.TriggerAccess, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "cancellation is the caller stopping rather than the lock being refused, and false is the "
            + "store's word for a re-entrant acquire");
    }

    /// <summary>
    /// And the requestor whose acquire was cancelled is not left recorded as an owner: its next acquire
    /// is a fresh one, answered <see langword="true" />, so the caller knows it has to release.
    /// </summary>
    [TestCaseSource(nameof(RowLockHandlers))]
    public async Task ACancelledAcquireLeavesNoOwnershipBehind(Func<IDbProvider, DbLockHandler> create)
    {
        await ProvisionSchema();

        DbLockHandler lockHandler = Initialize(create);

        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();
        using ConnectionAndTransactionHolder holder = new(connection, transaction: null);

        Guid requestorId = Guid.NewGuid();

        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> cancelled = async () => await lockHandler.AcquireLock(requestorId, holder, SchedulerLock.TriggerAccess, cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();

        bool obtained = await lockHandler.AcquireLock(requestorId, holder, SchedulerLock.TriggerAccess);

        obtained.Should().BeTrue(
            "the attempt that was cancelled took nothing, so this one takes the lock; had the handler "
            + "recorded the requestor as an owner on its way out, this would come back false and the "
            + "caller would run its operation unlocked and never release anything");
    }

    /// <summary>
    /// The same two handlers, each in a subclass that counts the times the base class runs its
    /// statement. Counting there rather than at the provider is what the store's own command
    /// preparation forces: it mints a command from the concrete provider type, so a counting decorator
    /// around <see cref="ProviderFactoryDbProvider" /> would be handed to the fallback branch and get a
    /// command attached to no connection.
    /// </summary>
    private static IEnumerable<TestCaseData> CountingRowLockHandlers()
    {
        yield return new TestCaseData(new Func<IDbProvider, DbLockHandler>(
            provider => new CountingUpdateRowLockHandler(provider))) { TestName = "{m}(UpdateRowLockHandler)" };

        yield return new TestCaseData(new Func<IDbProvider, DbLockHandler>(
            provider => new CountingSelectForUpdateLockHandler(SelectWithoutForUpdate, provider))) { TestName = "{m}(SelectForUpdateLockHandler)" };
    }

    /// <summary>
    /// The single most important rule a lock handler keeps (<c>how-tos/lock-handler.md</c>): an acquire
    /// re-entered by the requestor that already holds the lock is answered <see langword="false" /> and
    /// takes no second lock.
    /// </summary>
    /// <remarks>
    /// The answer is a release obligation rather than a report of success. A handler answering
    /// <see langword="true" /> here would have the inner operation release a lock the outer one is
    /// still relying on, and the rest of that outer operation would run unguarded — which for
    /// <c>TRIGGER_ACCESS</c> is two nodes acquiring the same trigger.
    /// </remarks>
    [TestCaseSource(nameof(CountingRowLockHandlers))]
    public async Task AReEntrantAcquireIsAnsweredFalseAndTakesNoSecondLock(Func<IDbProvider, DbLockHandler> create)
    {
        await ProvisionSchema();

        DbLockHandler lockHandler = Initialize(create);
        ICountStatements counter = (ICountStatements) lockHandler;

        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();
        using ConnectionAndTransactionHolder holder = new(connection, transaction: null);

        Guid requestorId = Guid.NewGuid();

        (await lockHandler.AcquireLock(requestorId, holder, SchedulerLock.TriggerAccess)).Should().BeTrue(
            "the first acquire is the one that took the lock, so its caller is the one that releases it");

        int afterTheFirst = counter.Statements;
        afterTheFirst.Should().BePositive("the first acquire reached the database");

        (await lockHandler.AcquireLock(requestorId, holder, SchedulerLock.TriggerAccess)).Should().BeFalse(
            "the same requestor already holds this lock, and false is how the store is told not to "
            + "release it when the inner operation ends");

        counter.Statements.Should().Be(afterTheFirst,
            "and no second lock was taken — a handler that ran its statement again would be waiting on "
            + "a row its own transaction holds, which is the deadlock the rule exists to prevent");
    }

    /// <summary>
    /// And the rule is about the requestor and the lock together, not about the lock alone: the two
    /// locks are held independently, and somebody else asking for one this requestor holds is told
    /// <see langword="true" /> because they are the one who took it.
    /// </summary>
    [TestCaseSource(nameof(RowLockHandlers))]
    public async Task FalseIsSaidOnlyToTheRequestorThatAlreadyHoldsThatOneLock(Func<IDbProvider, DbLockHandler> create)
    {
        await ProvisionSchema();

        DbLockHandler lockHandler = Initialize(create);

        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();
        using ConnectionAndTransactionHolder holder = new(connection, transaction: null);

        Guid requestorId = Guid.NewGuid();

        await lockHandler.AcquireLock(requestorId, holder, SchedulerLock.TriggerAccess);

        (await lockHandler.AcquireLock(requestorId, holder, SchedulerLock.StateAccess)).Should().BeTrue(
            "the two locks are held separately — cluster check-in runs on its own transaction so that it "
            + "cannot deadlock against trigger work, which needs its lock to be a different one");

        (await lockHandler.AcquireLock(Guid.NewGuid(), holder, SchedulerLock.TriggerAccess)).Should().BeTrue(
            "and another requestor's acquire is a fresh one it has to release, whoever else is holding "
            + "the row lock");
    }

    /// <summary>
    /// A release from somebody who does not hold the lock warns rather than throwing, and leaves the
    /// holder holding.
    /// </summary>
    /// <remarks>
    /// Throwing would turn a bookkeeping mistake into a failed scheduling operation; dropping the entry
    /// would be worse, because the owner's next acquire would then look fresh and the owner would go on
    /// to release a lock it had already given back.
    /// </remarks>
    [TestCaseSource(nameof(RowLockHandlers))]
    public async Task AReleaseFromANonOwnerDoesNotThrowAndLeavesTheHolderHolding(Func<IDbProvider, DbLockHandler> create)
    {
        await ProvisionSchema();

        DbLockHandler lockHandler = Initialize(create);

        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();
        using ConnectionAndTransactionHolder holder = new(connection, transaction: null);

        Guid owner = Guid.NewGuid();
        await lockHandler.AcquireLock(owner, holder, SchedulerLock.TriggerAccess);

        Func<Task> act = async () => await lockHandler.ReleaseLock(Guid.NewGuid(), SchedulerLock.TriggerAccess);

        await act.Should().NotThrowAsync(
            "a release by somebody who never took the lock is a mistake to report, not a scheduling "
            + "operation to fail");

        (await lockHandler.AcquireLock(owner, holder, SchedulerLock.TriggerAccess)).Should().BeFalse(
            "the owner is still recorded as holding it: a stranger's release that dropped the entry "
            + "would make this look like a fresh acquire, and the owner would release a lock twice");
    }

    private DbLockHandler Initialize(Func<IDbProvider, DbLockHandler> create)
    {
        DbMetadata metadata = DbMetadataResolver.BuiltIn().ResolveWithoutTypes("SQLite-Microsoft");
        ProviderFactoryDbProvider provider = new(metadata, SqliteFactory.Instance, connectionString);

        DbLockHandler lockHandler = create(provider);
        lockHandler.Initialize(new LockHandlerContext
        {
            SchedulerName = SchedulerName,
            InstanceId = "node-1",
            TablePrefix = AdoConstants.DefaultTablePrefix,
        });

        return lockHandler;
    }

    /// <summary>
    /// Creates the tables the way an application would, so the lock table this test writes to is the
    /// shipped one rather than one the test invented.
    /// </summary>
    private async Task ProvisionSchema()
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "row-lock-schema";
                options.InstanceId = "provisioning";
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, connectionString);
                store.ProvisionSchema();
            });
        });

        await using ServiceProvider container = services.BuildServiceProvider();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Shutdown();
    }

    /// <summary>
    /// How many times the handler ran its lock statement.
    /// </summary>
    private interface ICountStatements
    {
        int Statements { get; }
    }

    private sealed class CountingUpdateRowLockHandler : UpdateRowLockHandler, ICountStatements
    {
        internal CountingUpdateRowLockHandler(IDbProvider dbProvider) : base(dbProvider)
        {
        }

        public int Statements { get; private set; }

        protected override ValueTask ExecuteSql(
            Guid requestorId,
            ConnectionAndTransactionHolder conn,
            string lockName,
            string expandedSql,
            string expandedInsertSql,
            CancellationToken cancellationToken = default)
        {
            Statements++;
            return base.ExecuteSql(requestorId, conn, lockName, expandedSql, expandedInsertSql, cancellationToken);
        }
    }

    private sealed class CountingSelectForUpdateLockHandler : SelectForUpdateLockHandler, ICountStatements
    {
        internal CountingSelectForUpdateLockHandler(string selectWithLockSql, IDbProvider dbProvider)
            : base(AdoConstants.DefaultTablePrefix, DbLockHandlerRowLockSqliteTest.SchedulerName, selectWithLockSql, dbProvider)
        {
        }

        public int Statements { get; private set; }

        protected override ValueTask ExecuteSql(
            Guid requestorId,
            ConnectionAndTransactionHolder conn,
            string lockName,
            string expandedSql,
            string expandedInsertSql,
            CancellationToken cancellationToken = default)
        {
            Statements++;
            return base.ExecuteSql(requestorId, conn, lockName, expandedSql, expandedInsertSql, cancellationToken);
        }
    }
}
