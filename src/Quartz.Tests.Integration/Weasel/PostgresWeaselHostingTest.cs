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

using Marten;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

using Weasel.CommandLine;
using Weasel.Core.CommandLine;

using Wolverine;
using Wolverine.Postgresql;

namespace Quartz.Tests.Integration.Weasel;

/// <summary>
/// Hosted applications whose PostgreSQL store hands its schema to Weasel — on its own, from JasperFx's
/// command line, and joined to a Marten store beside Wolverine's message store on one database.
/// </summary>
[Category("db-postgres")]
public sealed class PostgresWeaselHostingTest
{
    private static readonly ConcurrentDictionary<string, TaskCompletionSource> firings = new(StringComparer.Ordinal);

    private PostgresWeaselDatabase database = null!;

    [SetUp]
    public async Task CreateEmptyDatabase()
    {
        database = await PostgresWeaselDatabase.CreateAsync();
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
        AddQuartz(builder, "weasel-pg-hosted", store => store.UseWeaselForPostgres());

        await StartScheduleAndStopAsync(builder, "weasel-pg-hosted");
    }

    [Test]
    public async Task AutoCreateNoneOnAnEmptyDatabaseLeavesItToTheStoresValidation()
    {
        HostApplicationBuilder builder = Builder();
        AddQuartz(builder, "weasel-pg-none", store => store.UseWeaselForPostgres(weasel => weasel.AutoCreate = AutoCreate.None));

        using IHost host = builder.Build();
        Func<Task> start = () => host.StartAsync();

        (await start.Should().ThrowAsync<SchedulerException>())
            .Which.ToString().Should().Contain("Database schema validation failed");
    }

    [Test]
    public async Task TheCommandLineAssertsPatchesAndApplies()
    {
        string patchFile = Path.Combine(Path.GetTempPath(), $"quartz-weasel-pg-{Guid.NewGuid():N}.sql");

        try
        {
            (await new AssertCommand().Execute(Input(new WeaselInput()))).Should().BeFalse("an empty database is not the model");

            (await new PatchCommand().Execute(Input(new PatchInput { FileName = patchFile }))).Should().BeTrue();
            (await File.ReadAllTextAsync(patchFile)).Should().Contain("CREATE TABLE IF NOT EXISTS public.qrtz_job_details");

            (await new ApplyCommand().Execute(Input(new WeaselInput()))).Should().BeTrue();
            (await new AssertCommand().Execute(Input(new WeaselInput { DatabaseFlag = "quartz://scheduler/weasel-pg-cli" }))).Should().BeTrue();
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
                q.ConfigureScheduler(options => options.InstanceName = "weasel-pg-cli");
                q.UsePersistentStore(store =>
                {
                    store.UsePostgres(NpgsqlFactory.Instance, database.ConnectionString);
                    store.UseWeaselForPostgres();
                });
            }));

            return input;
        }
    }

    /// <summary>
    /// The join mode, in the company it is for: Marten applies Quartz's tables with its own, Wolverine's
    /// message store sits beside both, and Marten's cleanup — which drops everything it owns — leaves the
    /// schedule alone.
    /// </summary>
    [Test]
    public async Task JoinedToMartenBesideWolverineTheTablesSurviveMartensCleanup()
    {
        HostApplicationBuilder builder = Builder();

        builder.UseWolverine(opts =>
        {
            opts.ApplicationAssembly = typeof(PostgresWeaselHostingTest).Assembly;
            opts.Discovery.DisableConventionalDiscovery();

            // Nothing to compile: there are no handlers, and Static mode is what lets Wolverine start
            // without the Roslyn it no longer ships.
            opts.CodeGeneration.TypeLoadMode = TypeLoadMode.Static;
            opts.PersistMessagesWithPostgresql(database.ConnectionString, "wolverine");
        });

        builder.Services.AddMarten(options =>
        {
            options.Connection(database.ConnectionString);
            options.DatabaseSchemaName = "marten";
        }).ApplyAllDatabaseChangesOnStartup();

        builder.Services.ConfigureMarten((services, options) => options.Storage.Add(QuartzPostgresFeatureSchema.ForScheduler(services)));

        // No UseWeaselForPostgres: in the join mode Marten owns the tables and Quartz only validates them.
        AddQuartz(builder, "weasel-pg-marten", store => { });

        using IHost host = builder.Build();
        await host.StartAsync();

        try
        {
            IScheduler scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
            await scheduler.AddJob(JobBuilder.Create<SignallingJob>().WithIdentity("kept").StoreDurably().Build());

            (await database.ScalarAsync("SELECT count(*) FROM information_schema.tables WHERE table_schema = 'wolverine'"))
                .Should().NotBe(0L, "Wolverine's message store is on the same database");

            await host.Services.GetRequiredService<IDocumentStore>().Advanced.Clean.CompletelyRemoveAllAsync();

            (await database.ScalarAsync("SELECT count(*) FROM qrtz_job_details WHERE job_name = 'kept'")).Should().Be(1L,
                "a feature schema is Marten's to apply and not to drop; ExtendedSchemaObjects would have gone with CASCADE");
        }
        finally
        {
            await host.StopAsync();
        }

        typeof(global::Weasel.Core.Migrator).Assembly.GetName().Version.Should().BeGreaterThanOrEqualTo(new Version(9, 36, 0),
            "Marten 9.40 asks for Weasel 9.35.2 and Wolverine 6.41 for 9.35.1, and Quartz.Weasel's floor raises the graph to one newer Weasel");
        typeof(JasperFxOptions).Assembly.GetName().Version.Should().BeGreaterThanOrEqualTo(new Version(2, 76, 0),
            "Weasel 9.36.0 floors JasperFx above what Marten and Wolverine ask for, and they run on it");
        typeof(WolverineOptions).Assembly.GetName().Version!.Major.Should().Be(6);
    }

    [Test]
    public async Task StandaloneAndJoinedForOneSchedulerIsRefused()
    {
        HostApplicationBuilder builder = Builder();

        builder.Services.AddMarten(options => options.Connection(database.ConnectionString)).ApplyAllDatabaseChangesOnStartup();
        builder.Services.ConfigureMarten((services, options) => options.Storage.Add(QuartzPostgresFeatureSchema.ForScheduler(services)));

        AddQuartz(builder, "weasel-pg-two-owners", store => store.UseWeaselForPostgres());

        using IHost host = builder.Build();
        Func<Task> start = () => host.StartAsync();

        (await start.Should().ThrowAsync<Exception>())
            .Which.ToString().Should().Contain("two owners");
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
                store.UsePostgres(NpgsqlFactory.Instance, database.ConnectionString);
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
