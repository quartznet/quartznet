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

using System;
using System.Data.Common;
using System.IO;
using System.Threading.Tasks;
using System.Transactions;

using Microsoft.Data.Sqlite;

using Quartz.Job;

using IsolationLevel = System.Transactions.IsolationLevel;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

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
[Category("db-sqlite")]
public sealed class EnlistmentRefusalSqliteTest
{
    private string databaseFile;
    private string connectionString;
    private IScheduler scheduler;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-enlistment-refusal-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";

        InstallSchemaFromFreshInstallScript();
    }

    [TearDown]
    public async Task DeleteDatabase()
    {
        if (scheduler != null)
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
            scheduler = null;
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
        scheduler = await GetScheduler(nameof(EnlistingAConnectionInsideATransactionScopeIsRefused));

        TransactionOptions options = new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted };
        using (new TransactionScope(TransactionScopeOption.RequiresNew, options, TransactionScopeAsyncFlowOption.Enabled))
        using (SqliteConnection connection = new SqliteConnection(connectionString))
        {
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
        scheduler = await GetScheduler(nameof(EnlistingTheConnectionsOwnTransactionStillDiscardsTheScheduleOnRollback));
        JobKey jobKey = new JobKey("enlisted", "sqlite");

        using (SqliteConnection connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            using (DbTransaction transaction = connection.BeginTransaction())
            {
                using (scheduler.EnlistTransaction(transaction))
                {
                    await scheduler.ScheduleJob(
                        JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).StoreDurably().Build(),
                        TriggerBuilder.Create().WithIdentity(jobKey.Name, jobKey.Group).ForJob(jobKey)
                            .StartAt(DateTimeOffset.UtcNow.AddHours(1)).Build());

                    (await scheduler.CheckExists(jobKey)).Should().BeTrue("the job store wrote through the enlisted transaction");
                }

                transaction.Rollback();
            }
        }

        (await scheduler.CheckExists(jobKey)).Should().BeFalse(
            "the schedule belongs to the transaction the application rolled back, which is the guarantee the "
            + "refusal above exists to keep honest");
    }

    private Task<IScheduler> GetScheduler(string schedulerName)
    {
        SchedulerBuilder config = SchedulerBuilder.Create("one", schedulerName);
        config.SetProperty("quartz.jobStore.acceptEnlistedTransactions", "true");
        config.UsePersistentStore(store =>
        {
            store.UseMicrosoftSQLite(connectionString);
            store.UseSystemTextJsonSerializer();
        });

        return config.BuildScheduler();
    }

    private void InstallSchemaFromFreshInstallScript()
    {
        using (SqliteConnection connection = new SqliteConnection(connectionString))
        {
            connection.Open();

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = File.ReadAllText(ResolveRepositoryFile("database", "tables", "tables_sqlite.sql"));
                command.ExecuteNonQuery();
            }
        }
    }

    private static string ResolveRepositoryFile(params string[] pathSegments)
    {
        string relativePath = Path.Combine(pathSegments);
        DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            string candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find '{relativePath}' above '{AppContext.BaseDirectory}'.");
    }
}
