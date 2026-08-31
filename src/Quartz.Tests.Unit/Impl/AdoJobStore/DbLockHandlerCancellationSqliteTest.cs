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
/// The two shipped row-lock handlers under a cancelled acquire, against a real database.
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
/// SQLite is a file, so both statements run for real here rather than only in the container legs. The
/// row lock itself is the transaction's rather than the handler's, and these handlers are given a
/// holder with no transaction; what is under test is the base class's bookkeeping, which is the same
/// whichever database is underneath.
/// </para>
/// </remarks>
public sealed class DbLockHandlerCancellationSqliteTest
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
}
