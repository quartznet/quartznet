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
using JasperFx.CodeGeneration;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Weasel.CommandLine;
using Weasel.Core;
using Weasel.Core.CommandLine;
using Weasel.Core.Migrations;

using Wolverine;
using Wolverine.SqlServer;

namespace Quartz.Tests.Integration.Weasel;

/// <summary>
/// Hosted applications whose SQL Server store hands its schema to Weasel — on its own, from JasperFx's
/// command line, and beside Wolverine's SQL Server message store on one database.
/// </summary>
[Category("db-sqlserver")]
public sealed class SqlServerWeaselHostingTest
{
    private static readonly ConcurrentDictionary<string, TaskCompletionSource> firings = new(StringComparer.Ordinal);

    private SqlServerWeaselDatabase database = null!;

    [SetUp]
    public async Task CreateEmptyDatabase()
    {
        database = await SqlServerWeaselDatabase.CreateAsync();
    }

    [TearDown]
    public async Task DropDatabase()
    {
        await database.DisposeAsync();
    }

    [Test]
    public async Task AHostOnAnEmptyDatabaseCreatesTheSchemaAndRunsAJob()
    {
        HostApplicationBuilder builder = Builder();
        AddQuartz(builder, "weasel-ss-hosted", store => store.UseWeaselForSqlServer());

        await StartScheduleAndStopAsync(builder, "weasel-ss-hosted");
    }

    [Test]
    public async Task AutoCreateNoneOnAnEmptyDatabaseLeavesItToTheStoresValidation()
    {
        HostApplicationBuilder builder = Builder();
        AddQuartz(builder, "weasel-ss-none", store => store.UseWeaselForSqlServer(weasel => weasel.AutoCreate = AutoCreate.None));

        using IHost host = builder.Build();
        Func<Task> start = () => host.StartAsync();

        (await start.Should().ThrowAsync<SchedulerException>())
            .Which.ToString().Should().Contain("Database schema validation failed");
    }

    [Test]
    public async Task TheCommandLineAssertsPatchesAndApplies()
    {
        string patchFile = Path.Combine(Path.GetTempPath(), $"quartz-weasel-ss-{Guid.NewGuid():N}.sql");

        try
        {
            (await new AssertCommand().Execute(Input(new WeaselInput()))).Should().BeFalse("an empty database is not the model");

            (await new PatchCommand().Execute(Input(new PatchInput { FileName = patchFile }))).Should().BeTrue();
            (await File.ReadAllTextAsync(patchFile)).Should().Contain("CREATE TABLE dbo.QRTZ_JOB_DETAILS")
                .And.Contain("PRIORITY DESC");

            (await new ApplyCommand().Execute(Input(new WeaselInput()))).Should().BeTrue();
            (await new AssertCommand().Execute(Input(new WeaselInput { DatabaseFlag = "quartz://scheduler/weasel-ss-cli" }))).Should().BeTrue();
        }
        finally
        {
            File.Delete(patchFile);
            File.Delete(patchFile.Replace(".sql", ".drop.sql", StringComparison.Ordinal));
        }

        T Input<T>(T input) where T : WeaselInput
        {
            input.HostBuilder = Host.CreateDefaultBuilder().ConfigureServices(services => services.AddQuartz(q =>
            {
                q.ConfigureScheduler(options => options.InstanceName = "weasel-ss-cli");
                q.UsePersistentStore(store =>
                {
                    store.UseSqlServer(SqlClientFactory.Instance, database.ConnectionString);
                    store.UseWeaselForSqlServer();
                });
            }));

            return input;
        }
    }

    /// <summary>
    /// Wolverine's SQL Server message store and Quartz's tables on one database, each applied by its own
    /// owner under its own lock: Quartz's model neither reads nor changes Wolverine's schema.
    /// </summary>
    [Test]
    public async Task BesideWolverinesMessageStoreEachKeepsItsOwnTables()
    {
        HostApplicationBuilder builder = Builder();

        builder.UseWolverine(opts =>
        {
            opts.ApplicationAssembly = typeof(SqlServerWeaselHostingTest).Assembly;
            opts.Discovery.DisableConventionalDiscovery();

            // Nothing to compile: there are no handlers, and Static mode is what lets Wolverine start
            // without the Roslyn it no longer ships.
            opts.CodeGeneration.TypeLoadMode = TypeLoadMode.Static;
            opts.PersistMessagesWithSqlServer(database.ConnectionString, "wolverine");
        });

        AddQuartz(builder, "weasel-ss-wolverine", store => store.UseWeaselForSqlServer());

        using IHost host = builder.Build();
        await host.StartAsync();

        try
        {
            IScheduler scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
            await scheduler.AddJob(JobBuilder.Create<SignallingJob>().WithIdentity("kept").StoreDurably().Build());

            string wolverineBefore = await WolverineSchemaAsync();
            wolverineBefore.Should().NotBeEmpty("Wolverine's message store is on the same database");

            List<IDatabase> databases = [];
            foreach (IDatabaseSource source in host.Services.GetServices<IDatabaseSource>())
            {
                databases.AddRange(await source.BuildDatabases());
            }

            IDatabase quartz = databases.Single(x => x.Describe().Identifier == "weasel-ss-wolverine");
            (await quartz.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.None,
                "the startup apply already brought Quartz's tables to the model");

            (await WolverineSchemaAsync()).Should().Be(wolverineBefore, "Quartz's model looks at none of Wolverine's tables");
            (await database.ScalarAsync("SELECT count(*) FROM dbo.QRTZ_JOB_DETAILS WHERE JOB_NAME = 'kept'")).Should().Be(1);
        }
        finally
        {
            await host.StopAsync();
        }

        typeof(global::Weasel.Core.Migrator).Assembly.GetName().Version.Should().BeGreaterThanOrEqualTo(new Version(9, 39, 0),
            "Wolverine 6.45 asks for Weasel 9.38.0, and Quartz.Weasel's floor raises the graph to one newer Weasel");
        typeof(JasperFxOptions).Assembly.GetName().Version.Should().BeGreaterThanOrEqualTo(new Version(2, 79, 1),
            "Wolverine floors JasperFx above the 2.76.0 Weasel 9.39.0 asks for, and Quartz.Weasel runs on it");
        typeof(WolverineOptions).Assembly.GetName().Version!.Major.Should().Be(6);
    }

    private async Task<string> WolverineSchemaAsync()
    {
        await using SqlConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqlCommand command = new("""
            SELECT STRING_AGG(CONVERT(nvarchar(max), t.name + '.' + c.name + ':' + TYPE_NAME(c.user_type_id)), ',')
                   WITHIN GROUP (ORDER BY t.name, c.column_id)
            FROM sys.tables t
            JOIN sys.columns c ON c.object_id = t.object_id
            WHERE SCHEMA_NAME(t.schema_id) = 'wolverine'
            """, connection);

        return (await command.ExecuteScalarAsync()) as string ?? "";
    }

    private static HostApplicationBuilder Builder() => Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        DisableDefaults = true,
        EnvironmentName = Environments.Production,
    });

    private void AddQuartz(HostApplicationBuilder builder, string schedulerName, Action<IPersistentStoreBuilder> weasel)
    {
        builder.Services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = schedulerName);
            q.UsePersistentStore(store =>
            {
                store.UseSqlServer(SqlClientFactory.Instance, database.ConnectionString);
                store.UseSystemTextJsonSerializer();
                weasel(store);
            });
        });

        builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
    }

    private static async Task StartScheduleAndStopAsync(HostApplicationBuilder builder, string schedulerName)
    {
        TaskCompletionSource fired = firings.GetOrAdd(schedulerName, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        using IHost host = builder.Build();
        await host.StartAsync();

        try
        {
            IScheduler scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
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
