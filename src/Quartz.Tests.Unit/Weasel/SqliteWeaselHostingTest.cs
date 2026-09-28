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

using System.Collections.Concurrent;

using JasperFx;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Npgsql;

namespace Quartz.Tests.Unit.Weasel;

/// <summary>
/// A hosted application whose SQLite store hands its schema to Weasel: what happens at startup, and
/// what is refused there.
/// </summary>
public sealed class SqliteWeaselHostingTest
{
    private static readonly ConcurrentDictionary<string, TaskCompletionSource> firings = new(StringComparer.Ordinal);

    private SqliteTestDatabase database = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("weasel-hosting");
    }

    [TearDown]
    public void DeleteDatabase()
    {
        database.Dispose();
    }

    [Test]
    public async Task AHostOnAnEmptyDatabaseCreatesTheSchemaAndRunsAJob()
    {
        const string SchedulerName = "weasel-hosted";
        TaskCompletionSource fired = firings.GetOrAdd(SchedulerName, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        using IHost host = BuildHost(SchedulerName, store => store.UseWeaselForSqlite());
        await host.StartAsync();

        try
        {
            IScheduler scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
            scheduler.SchedulerName.Should().Be(SchedulerName);

            await scheduler.ScheduleJob(
                JobBuilder.Create<SignallingJob>().WithIdentity("signal").Build(),
                TriggerBuilder.Create().WithIdentity("signal").StartNow().Build());

            (await Task.WhenAny(fired.Task, Task.Delay(TimeSpan.FromSeconds(30)))).Should().Be(fired.Task,
                "the schema Weasel created at startup is one the store stores and fires from");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Test]
    public async Task AutoCreateNoneOnAnEmptyDatabaseLeavesItToTheStoresValidation()
    {
        using IHost host = BuildHost("weasel-none", store => store.UseWeaselForSqlite(weasel => weasel.AutoCreate = AutoCreate.None));

        Func<Task> start = () => host.StartAsync();

        (await start.Should().ThrowAsync<SchedulerException>())
            .Which.ToString().Should().Contain("Database schema validation failed",
                "None applies nothing, and the store then refuses a database with no schema, exactly as without Weasel");
    }

    [Test]
    public async Task TheActiveJasperFxProfileDecidesWhenTheApplicationDidNot()
    {
        using IHost host = BuildHost(
            "weasel-profile",
            store => store.UseWeaselForSqlite(),
            services => services.AddJasperFx(options => options.Production.ResourceAutoCreate = AutoCreate.None));

        Func<Task> start = () => host.StartAsync();

        (await start.Should().ThrowAsync<SchedulerException>())
            .Which.ToString().Should().Contain("Database schema validation failed",
                "the Production profile says None, so Weasel applies nothing, the way Marten and Wolverine would not");
    }

    /// <summary>
    /// An apply that fails fails the startup, which is the default — and the store would otherwise start
    /// on a schema nobody finished.
    /// </summary>
    [Test]
    public async Task AFailedApplyFailsTheStartup()
    {
        await SqliteSchema.CreateWithTriggersTableMissingItsForeignKeyAsync(database.ConnectionString, extraColumn: "USER_NOTE TEXT NULL");

        using IHost host = BuildHost("weasel-fail-fast", store => store.UseWeaselForSqlite());

        Func<Task> start = () => host.StartAsync();

        await start.Should().ThrowAsync<SchedulerException>().WithMessage("*would rebuild table QRTZ_TRIGGERS*");
    }

    /// <summary>
    /// With the profile's <c>ContinueOnFailures</c>, the failure is logged and the store's own validation
    /// decides, the way Marten and Wolverine treat their migrations.
    /// </summary>
    [Test]
    public async Task AFailedApplyIsLoggedAndStartupContinuesWhenTheProfileSaysSo()
    {
        await SqliteSchema.CreateWithTriggersTableMissingItsForeignKeyAsync(database.ConnectionString, extraColumn: "USER_NOTE TEXT NULL");

        using IHost host = BuildHost(
            "weasel-continue",
            store => store.UseWeaselForSqlite(),
            services => services.AddJasperFx(options =>
            {
                options.Production.ResourceMigrationFailureMode = ResourceMigrationFailureMode.ContinueOnFailures;
            }));

        await host.StartAsync();
        await host.StopAsync();

        (await SqliteSchema.ScalarAsync(database.ConnectionString, "SELECT count(*) FROM pragma_table_info('QRTZ_TRIGGERS') WHERE name = 'USER_NOTE'"))
            .Should().Be(1L, "the refused rebuild changed nothing, and the store started on the schema that was there");
    }

    [Test]
    public async Task ProvisionSchemaBesideWeaselIsRefused()
    {
        using IHost host = BuildHost("weasel-two-owners", store =>
        {
            store.ProvisionSchema();
            store.UseWeaselForSqlite();
        });

        Func<Task> start = () => host.StartAsync();

        await start.Should().ThrowAsync<OptionsValidationException>()
            .WithMessage("*UseWeaselForSqlite()*ProvisionSchema()*one owner*");
    }

    [Test]
    public async Task AStoreOnAnotherDatabaseIsRefused()
    {
        using IHost host = BuildHost(
            "weasel-wrong-database",
            store => store.UseWeaselForSqlite(),
            configureStore: store => store.UsePostgres(NpgsqlFactory.Instance, "Host=localhost;Database=quartz"));

        Func<Task> start = () => host.StartAsync();

        await start.Should().ThrowAsync<SchedulerConfigException>()
            .WithMessage("*UseWeaselForSqlite() manages a SQLite schema*NpgsqlConnection*UseSqlite*");
    }

    private IHost BuildHost(
        string schedulerName,
        Action<IPersistentStoreBuilder> weasel,
        Action<IServiceCollection>? services = null,
        Action<IPersistentStoreBuilder>? configureStore = null)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            EnvironmentName = Environments.Production,
        });

        builder.Services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = schedulerName);
            q.UsePersistentStore(store =>
            {
                if (configureStore is null)
                {
                    store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                }
                else
                {
                    configureStore(store);
                }

                store.UseSystemTextJsonSerializer();
                weasel(store);
            });
        });

        builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
        services?.Invoke(builder.Services);

        return builder.Build();
    }

    private sealed class SignallingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (firings.TryGetValue(context.Scheduler.SchedulerName, out TaskCompletionSource? fired))
            {
                fired.TrySetResult();
            }

            return default;
        }
    }
}
