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

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Quartz.Trimming.Canary;

/// <summary>
/// A SQLite store whose schema <c>Quartz.Weasel.SQLite</c> creates as the host starts, out of a trimmed
/// or natively compiled publish: then a job scheduled, fired and read back, as <see cref="StoreCheck" />
/// does over the shipped script.
/// </summary>
/// <remarks>
/// <para>
/// SQLite is the one Weasel dialect that needs no server, so it is the one the canary can run on every
/// runner. It runs under a real <see cref="IHost" />, because that is where Weasel does its work: the
/// schema is applied in a hosted service's <c>StartingAsync</c>, before <c>QuartzHostedService</c> builds
/// the scheduler in <c>StartAsync</c>.
/// </para>
/// <para>
/// The database file is empty when the host starts, and the store is left on its default
/// <see cref="SchemaProvisioning.Validate" />, so the scheduler starting at all says the tables Weasel
/// made are the ones Quartz checks for. The count of tables read afterwards says Weasel made them rather
/// than finding them there.
/// </para>
/// </remarks>
internal static class WeaselCheck
{
    /// <summary>
    /// Runs the check, returning <see langword="null" /> when it passed and a message when it did not.
    /// </summary>
    public static async Task<string?> Run()
    {
        string databaseFile = StoreRoundTrip.NewDatabaseFile("weasel");
        string connectionString = $"Data Source={databaseFile}";

        try
        {
            HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

            builder.Services.AddQuartz(q =>
            {
                q.ConfigureScheduler(options =>
                {
                    options.InstanceName = "CanaryWeasel";
                    options.InstanceId = "one";
                });

                q.UsePersistentStore(store =>
                {
                    store.UseSqlite(SqliteFactory.Instance, connectionString);
                    store.UseWeaselForSqlite();
                });
            });

            builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

            using IHost host = builder.Build();

            if (await CountQuartzTables(connectionString).ConfigureAwait(false) is var before and not 0)
            {
                return $"FAIL weasel: the database already held {before} Quartz tables before the host started, so nothing here would show Weasel made them.";
            }

            await host.StartAsync().ConfigureAwait(false);

            int tables = await CountQuartzTables(connectionString).ConfigureAwait(false);

            // The hosted service started this scheduler, so the round trip only schedules on it.
            IScheduler scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler().ConfigureAwait(false);

            string? roundTripFailure = await StoreRoundTrip.Run(scheduler, "weasel", Payload).ConfigureAwait(false);
            await host.StopAsync().ConfigureAwait(false);

            if (roundTripFailure is not null)
            {
                return roundTripFailure;
            }

            Console.WriteLine($"PASS weasel: Quartz.Weasel.SQLite created {tables} tables as the host started, Quartz's schema validation accepted them, and a store on them scheduled, fired and read back.");
            return null;
        }
        catch (Exception e)
        {
            return $"FAIL weasel: {e.GetType().FullName}: {e.Message}{Environment.NewLine}{e}";
        }
        finally
        {
            StoreRoundTrip.DeleteDatabase(databaseFile);
        }
    }

    private const string Payload = "the store round-trips a job data map over a schema Weasel made";

    /// <summary>
    /// How many of the database's tables are Quartz's, by the default prefix.
    /// </summary>
    private static async Task<int> CountQuartzTables(string connectionString)
    {
        using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name LIKE 'QRTZ!_%' ESCAPE '!'";
        return Convert.ToInt32(await command.ExecuteScalarAsync().ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }
}
