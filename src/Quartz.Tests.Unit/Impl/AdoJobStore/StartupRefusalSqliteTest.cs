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

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The two configurations the persistent store refuses to start under, both of which
/// <c>tutorial/job-stores.md</c> promises are startup failures rather than something that surfaces
/// later under load.
/// </summary>
/// <remarks>
/// Neither had a test anywhere: <c>SchedulerStartRefusedException</c> appeared only in product code,
/// and <c>InvalidConfigurationException</c> in neither test project. A refusal nobody exercises is a
/// refusal that can quietly become a hang — which for the enlistment one is exactly the failure it
/// was written to replace.
/// </remarks>
public sealed class StartupRefusalSqliteTest
{
    private string databaseFile = null!;
    private string connectionString = null!;
    private ServiceProvider? container;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-startup-refusal-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";
    }

    [TearDown]
    public async Task DeleteDatabase()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
            container = null;
        }

        SqliteConnection.ClearAllPools();

        if (File.Exists(databaseFile))
        {
            File.Delete(databaseFile);
        }
    }

    /// <summary>
    /// Starting a scheduler for the first time from inside an enlistment scope is refused, and the
    /// message says why.
    /// </summary>
    /// <remarks>
    /// Start-up competes for the same <c>TRIGGER_ACCESS</c> lock the enlisted transaction is holding,
    /// and the caller cannot commit while it is awaiting <c>Start()</c>. The alternative to this
    /// refusal is a deadlock with no diagnostic of its own, so the message has to name the enlistment
    /// and say what to do instead.
    /// </remarks>
    [Test]
    public async Task StartingFromInsideAnEnlistmentScopeIsRefusedAndSaysSo()
    {
        IScheduler scheduler = await GetScheduler(nameof(StartingFromInsideAnEnlistmentScopeIsRefusedAndSaysSo));

        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using DbTransaction transaction = await connection.BeginTransactionAsync();

        using (scheduler.EnlistTransaction(transaction))
        {
            Func<Task> act = async () => await scheduler.Start();

            (await act.Should().ThrowAsync<SchedulerException>(
                    "start-up waits for locks the enlisted transaction holds until the application "
                    + "commits, which it cannot do while awaiting Start()"))
                .WithMessage("*enlistment scope*",
                    "the reader has to be told which of the things they did is the problem")
                .And.Message.Should().Contain("outside the scope",
                    "and what to do instead, since the answer is not to stop enlisting");
        }

        await transaction.RollbackAsync();

        scheduler.Status.Should().Be(SchedulerStatus.Created,
            "the refusal happened before anything was started, so the scheduler is where it was");
    }

    /// <summary>
    /// And the scheduler stays startable: the refusal happened before the store created anything, so
    /// the corrected call runs the whole start-up sequence rather than the resume path.
    /// </summary>
    /// <remarks>
    /// This is the half that makes the refusal a refusal rather than a failure. Were the start marker
    /// latched by the attempt that was refused, the retry would skip job recovery, the misfire handler
    /// and cluster check-in while still starting the acquire loop.
    /// </remarks>
    [Test]
    public async Task OnceTheScopeIsGoneTheSameSchedulerStarts()
    {
        IScheduler scheduler = await GetScheduler(nameof(OnceTheScopeIsGoneTheSameSchedulerStarts));

        await using (SqliteConnection connection = new(connectionString))
        {
            await connection.OpenAsync();
            await using DbTransaction transaction = await connection.BeginTransactionAsync();

            using (scheduler.EnlistTransaction(transaction))
            {
                Func<Task> refused = async () => await scheduler.Start();
                await refused.Should().ThrowAsync<SchedulerException>();
            }

            await transaction.RollbackAsync();
        }

        Func<Task> act = async () => await scheduler.Start();

        await act.Should().NotThrowAsync(
            "nothing had been created when the first attempt was refused, so the scheduler is startable "
            + "once the scope the caller was inside is gone");

        scheduler.Status.Should().Be(SchedulerStatus.Running);

        await scheduler.Shutdown();
    }

    /// <summary>
    /// SQLite locks in process rather than in the database, so the row locks a cluster coordinates
    /// through do not hold between nodes. Asking for both is refused as the store initializes.
    /// </summary>
    /// <remarks>
    /// Through the configuration API rather than by constructing the store, because the pairing a
    /// reader can write is <c>UseSqlite(…)</c> beside <c>UseClustering()</c> and what they need to
    /// know is that it fails then and there.
    /// </remarks>
    [Test]
    public async Task SqliteTogetherWithClusteringIsRefusedAsTheStoreInitializes()
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = nameof(SqliteTogetherWithClusteringIsRefusedAsTheStoreInitializes);
                options.InstanceId = "one";
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, connectionString);
                store.ProvisionSchema();
                store.UseClustering();
            });
        });

        container = services.BuildServiceProvider();

        Func<Task> act = async () => await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        Exception failure = (await act.Should().ThrowAsync<Exception>(
            "a cluster over SQLite is a configuration that cannot work, and the promise is that it "
            + "fails at start-up rather than later under load")).Which;

        MessagesOf(failure).Should().ContainMatch("*SQLite cannot be used as clustered mode*",
            "the message names the database and the setting, which is the whole of what the reader has "
            + "to change");

        ExceptionsOf(failure).Should().ContainItemsAssignableTo<InvalidConfigurationException>(
            "job-stores.md names Quartz.Impl.AdoJobStore.InvalidConfigurationException, so an "
            + "application catching that type has to be the one that catches this");
    }

    /// <summary>
    /// The other half of the same block, and the reason the refusal above is not simply a warning: for
    /// everything SQLite can be told to do differently, the store overrides the setting rather than
    /// refusing, so clustering is the one that genuinely cannot be made to work.
    /// </summary>
    [Test]
    public async Task ASqliteStoreOverridesTheSettingsItCanAndRefusesOnlyClustering()
    {
        LocalTransactionJobStore store = CreateSqliteStore(clustered: false, configure: options =>
        {
            options.AcquireTriggersWithinLock = false;
            options.TransactionIsolationLevel = IsolationLevel.ReadCommitted;
            options.SchemaProvisioning = SchemaProvisioning.None;
        });

        await store.Initialize(TestJobStores.Identity());

        store.AcquireTriggersWithinLock.Should().BeTrue(
            "acquisition outside the lock is a second writer, which SQLite refuses with 'database is locked'");
        store.LockAllOperations.Should().BeTrue(
            "and so is any other operation that was not going to take the lock");
        store.TransactionIsolationLevel.Should().Be(IsolationLevel.Serializable,
            "a lower level is not a preference here but a failure mode, so an explicit one is overridden "
            + "rather than kept");

        store.LockHandler.Should().BeOfType<SqliteLockHandler>(
            "the row-lock handlers coordinate through the database, which is the one thing SQLite's "
            + "locking does not do");
    }

    /// <summary>
    /// And the same store told to cluster refuses, whatever else it was configured with.
    /// </summary>
    [Test]
    public async Task AClusteredSqliteStoreRefusesToInitialize()
    {
        LocalTransactionJobStore store = CreateSqliteStore(clustered: true);

        Func<Task> act = async () => await store.Initialize(TestJobStores.Identity());

        (await act.Should().ThrowAsync<InvalidConfigurationException>(
                "the refusal is the store's own, before a connection is ever opened, so it does not "
                + "depend on the database being reachable"))
            .WithMessage("*SQLite*");
    }

    private static LocalTransactionJobStore CreateSqliteStore(bool clustered, Action<AdoJobStoreOptions>? configure = null)
    {
        return new LocalTransactionJobStore(TestJobStores.Dependencies(
            storeOptions: TestJobStores.StoreOptions(configure: configure),
            clusteringOptions: TestJobStores.ClusteringOptions(options => options.Enabled = clustered),
            driverDelegate: new SQLiteDelegate())
        with
        {
            LockHandler = null,
        });
    }

    private async Task<IScheduler> GetScheduler(string schedulerName)
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = schedulerName;
                options.InstanceId = "one";
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, connectionString);
                store.ProvisionSchema();
                store.ConfigureStore(options => options.AcceptEnlistedTransactions = true);
            });
        });

        container = services.BuildServiceProvider();

        return await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
    }

    /// <summary>Every message in an exception chain, outermost first.</summary>
    private static List<string> MessagesOf(Exception exception) => [.. ExceptionsOf(exception).Select(x => x.Message)];

    /// <summary>Every exception in a chain, outermost first.</summary>
    private static List<Exception> ExceptionsOf(Exception exception)
    {
        List<Exception> chain = [];

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            chain.Add(current);
        }

        return chain;
    }
}
