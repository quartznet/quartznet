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

using System.Data.Common;
using System.Transactions;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Jobs;

using IsolationLevel = System.Transactions.IsolationLevel;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// Against the one shipped driver that cannot join an ambient transaction, on a real database file.
/// <c>Microsoft.Data.Sqlite</c> overrides no <see cref="DbConnection.EnlistTransaction" />, so a
/// connection opened inside a <see cref="TransactionScope" /> never joins it — and until the store
/// established the enlistment rather than assuming it, the scheduling written through that connection
/// committed on the spot and survived a scope that was never completed. Reported as
/// https://github.com/quartznet/quartznet/issues/3666.
/// </summary>
/// <remarks>
/// A file database rather than a fake: the driver's own answer is the whole of what is being pinned,
/// and it is exactly the part a stand-in cannot supply.
/// </remarks>
public sealed class EnlistmentRefusalSqliteTest
{
    private string databaseFile = null!;
    private string connectionString = null!;
    private ServiceProvider? container;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-enlistment-refusal-{Guid.NewGuid():N}.db");
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

    [Test]
    public async Task EnlistingAConnectionInsideATransactionScopeIsRefused()
    {
        IScheduler scheduler = await GetScheduler(nameof(EnlistingAConnectionInsideATransactionScopeIsRefused));

        TransactionOptions options = new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted };
        using (new TransactionScope(TransactionScopeOption.RequiresNew, options, TransactionScopeAsyncFlowOption.Enabled))
        {
            await using SqliteConnection connection = new(connectionString);
            await connection.OpenAsync();

            Action enlist = () => scheduler.EnlistConnection(connection);

            enlist.Should().Throw<SchedulerException>(
                    "the scope governs nothing this connection does, so accepting it would commit the scheduling "
                    + "whatever the application decided")
                .Which.Message.Should().Contain("Microsoft.Data.Sqlite.SqliteConnection",
                    "the failure has to name the driver, because that is what the reader has to change or work around")
                .And.Contain("EnlistTransaction(connection.BeginTransaction())",
                    "and the form that does work on this very driver");
        }
    }

    /// <summary>
    /// The way out the refusal points at, on the driver that is refused: a transaction of the
    /// connection's own governs the writes, and rolling it back takes the schedule with it.
    /// </summary>
    [Test]
    public async Task EnlistingTheConnectionsOwnTransactionStillDiscardsTheScheduleOnRollback()
    {
        IScheduler scheduler = await GetScheduler(nameof(EnlistingTheConnectionsOwnTransactionStillDiscardsTheScheduleOnRollback));
        JobKey jobKey = new JobKey("enlisted", "sqlite");

        await using (SqliteConnection connection = new(connectionString))
        {
            await connection.OpenAsync();
            await using DbTransaction transaction = await connection.BeginTransactionAsync();

            using (scheduler.EnlistTransaction(transaction))
            {
                await scheduler.ScheduleJob(
                    JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).StoreDurably().Build(),
                    TriggerBuilder.Create().WithIdentity(jobKey.Name, jobKey.Group).ForJob(jobKey)
                        .StartAt(DateTimeOffset.UtcNow.AddHours(1)).Build());

                (await scheduler.Exists(jobKey)).Should().BeTrue("the job store wrote through the enlisted transaction");
            }

            await transaction.RollbackAsync();
        }

        (await scheduler.Exists(jobKey)).Should().BeFalse(
            "the schedule belongs to the transaction the application rolled back, which is the guarantee the "
            + "refusal above exists to keep honest");
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
}
