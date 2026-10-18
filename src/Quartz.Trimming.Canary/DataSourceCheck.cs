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

using System.Data.Common;
using System.Runtime.CompilerServices;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Trimming.Canary;

/// <summary>
/// A persistent store whose connections come from a <see cref="DbDataSource" /> in the container, out of
/// a trimmed or natively compiled publish: schema provisioned, a job scheduled, fired and read back.
/// </summary>
/// <remarks>
/// <para>
/// <c>Microsoft.Data.Sqlite</c> ships no data source, and the drivers that do need a server, so
/// <see cref="CanaryDataSource" /> is the canary's own: a few lines over <see cref="SqliteConnection" />,
/// registered as a singleton under its own type and as <see cref="DbDataSource" />, which is what
/// <c>AddNpgsqlDataSource</c> does with Npgsql's. Quartz takes it with <c>UseRegisteredDataSource</c>
/// and is given no connection string, so the database file is something only the data source knows.
/// </para>
/// <para>
/// The store is registered with <c>UseDriverDelegate&lt;SQLiteDelegate&gt;()</c> and
/// <c>UseDataSource(…)</c> rather than <c>UseSqlite(db =&gt; db.UseRegisteredDataSource = true)</c>.
/// The two are the same registration, which is what that overload's body is, but the overload says
/// <c>[RequiresUnreferencedCode]</c> because the same callback could set a connection string instead,
/// and its warning would surface here, in a canary that has to report nothing against a Quartz type.
/// </para>
/// <para>
/// What the run proves, past the job firing: the store's connection provider is the data-source one,
/// over this very instance, and reports no connection string; and the data source handed out the
/// connections. A store with no connection string and a provider that holds only the data source has no
/// other way to reach the database.
/// </para>
/// </remarks>
internal static class DataSourceCheck
{
    private static readonly TaskCompletionSource fired = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Runs the check, returning <see langword="null" /> when it passed and a message when it did not.
    /// </summary>
    public static async Task<string?> Run()
    {
        string databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-canary-datasource-{Guid.NewGuid():N}.db");

        try
        {
            CanaryDataSource dataSource = new($"Data Source={databaseFile}");

            ServiceCollection services = new();

            // The registration an application makes for a driver that ships a data source: the instance
            // under its own type, and the same instance as the DbDataSource anything else asks for.
            services.AddSingleton(dataSource);
            services.AddSingleton<DbDataSource>(provider => provider.GetRequiredService<CanaryDataSource>());

            services.AddQuartz(q =>
            {
                q.ConfigureScheduler(options =>
                {
                    options.InstanceName = "CanaryDataSource";
                    options.InstanceId = "one";
                });

                q.UsePersistentStore(store =>
                {
                    store.UseDriverDelegate<SQLiteDelegate>();
                    store.UseDataSource(db =>
                    {
                        // The provider name chooses the half of the driver description that names no
                        // type: how a parameter is spelled. The connection is the data source's.
                        db.Provider = DataSourceOptions.Providers.Sqlite;
                        db.UseRegisteredDataSource = true;
                    });
                    store.ProvisionSchema();
                });
            });

            ServiceProvider container = services.BuildServiceProvider();
            await using ConfiguredAsyncDisposable containerDisposal = container.ConfigureAwait(false);

            IDbProvider dbProvider = container.GetRequiredService<IDbProvider>();
            if (dbProvider is not DataSourceDbProvider { DataSource: var source } || !ReferenceEquals(source, dataSource))
            {
                return $"FAIL data-source: the store's connection provider is a {dbProvider.GetType().FullName}, not one over the registered DbDataSource.";
            }

            if (dbProvider.ConnectionString.Length != 0)
            {
                return $"FAIL data-source: the store's connection provider holds the connection string '{dbProvider.ConnectionString}', so its connections need not have come from the data source.";
            }

            IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler().ConfigureAwait(false);

            JobKey jobKey = new("data-source", "store");
            TriggerKey triggerKey = new("data-source", "store");

            await scheduler.ScheduleJob(
                JobBuilder.Create<DataSourceJob>()
                    .WithIdentity(jobKey)
                    .UsingJobData("payload", Payload)
                    .Build(),
                TriggerBuilder.Create()
                    .WithIdentity(triggerKey)
                    .StartNow()
                    .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                    .Build()).ConfigureAwait(false);

            await scheduler.Start().ConfigureAwait(false);

            Task completed = await Task.WhenAny(fired.Task, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);
            if (completed != fired.Task)
            {
                return "FAIL data-source: the job never fired within a minute, so the store never handed a trigger to the scheduler.";
            }

            IJobDetail? job = await scheduler.GetJobDetail(jobKey).ConfigureAwait(false);
            if (job?.JobDataMap.GetString("payload") != Payload)
            {
                return $"FAIL data-source: the job came back as {(job is null ? "nothing" : $"one carrying '{job.JobDataMap.GetString("payload")}'")}.";
            }

            ITrigger? trigger = await scheduler.GetTrigger(triggerKey).ConfigureAwait(false);
            if (trigger is null || !Equals(trigger.JobKey, jobKey))
            {
                return $"FAIL data-source: the trigger came back as {(trigger is null ? "nothing" : $"one pointing at '{trigger.JobKey}'")}.";
            }

            await scheduler.Shutdown(waitForJobsToComplete: true).ConfigureAwait(false);

            int connections = dataSource.ConnectionsCreated;
            if (connections == 0)
            {
                return "FAIL data-source: the store ran, but the data source never created a connection.";
            }

            Console.WriteLine($"PASS data-source: provisioned, scheduled, fired and read back through a SQLite store given no connection string; all {connections} of its connections came from the DbDataSource registered in the container.");
            return null;
        }
        catch (Exception e)
        {
            return $"FAIL data-source: {e.GetType().FullName}: {e.Message}{Environment.NewLine}{e}";
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(databaseFile);
        }
    }

    private const string Payload = "the store round-trips a job data map over a registered DbDataSource";

    private static void TryDelete(string databaseFile)
    {
        try
        {
            File.Delete(databaseFile);
        }
        catch (IOException)
        {
            // A leftover temporary file is not a failure of anything this checks.
        }
    }

    /// <summary>
    /// A data source over <see cref="SqliteConnection" />, standing in for the one a server driver ships,
    /// and counting the connections it hands out.
    /// </summary>
    /// <remarks>
    /// The two members a data source has to supply; opening, and the commands and batches a data source
    /// can make on its own, are <see cref="DbDataSource" />'s defaults over them.
    /// </remarks>
    public sealed class CanaryDataSource : DbDataSource
    {
        private readonly string connectionString;
        private int connectionsCreated;

        public CanaryDataSource(string connectionString)
        {
            this.connectionString = connectionString;
        }

        public override string ConnectionString => connectionString;

        public int ConnectionsCreated => Volatile.Read(ref connectionsCreated);

        protected override DbConnection CreateDbConnection()
        {
            Interlocked.Increment(ref connectionsCreated);
            return new SqliteConnection(connectionString);
        }
    }

    /// <summary>
    /// Signals that the store handed this job to the scheduler.
    /// </summary>
    public sealed class DataSourceJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            fired.TrySetResult();
            return default;
        }
    }
}
