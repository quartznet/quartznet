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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MySqlConnector;

using Weasel.CommandLine;
using Weasel.Core.CommandLine;

namespace Quartz.Tests.Integration.Weasel;

/// <summary>
/// Hosted applications whose MySQL store hands its schema to Weasel: at startup, and from JasperFx's
/// command line.
/// </summary>
[Category("db-mysql")]
public sealed class MySqlWeaselHostingTest
{
    private static readonly ConcurrentDictionary<string, TaskCompletionSource> firings = new(StringComparer.Ordinal);

    private MySqlWeaselDatabase database = null!;

    [SetUp]
    public async Task CreateEmptyDatabase()
    {
        database = await MySqlWeaselDatabase.CreateAsync();
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
        AddQuartz(builder, "weasel-my-hosted", store => store.UseWeaselForMySql());

        TaskCompletionSource fired = firings.GetOrAdd("weasel-my-hosted", _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

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

    [Test]
    public async Task AutoCreateNoneOnAnEmptyDatabaseLeavesItToTheStoresValidation()
    {
        HostApplicationBuilder builder = Builder();
        AddQuartz(builder, "weasel-my-none", store => store.UseWeaselForMySql(weasel => weasel.AutoCreate = AutoCreate.None));

        using IHost host = builder.Build();
        Func<Task> start = () => host.StartAsync();

        (await start.Should().ThrowAsync<SchedulerException>())
            .Which.ToString().Should().Contain("Database schema validation failed");
    }

    [Test]
    public async Task TheCommandLineAssertsPatchesAndApplies()
    {
        string patchFile = Path.Combine(Path.GetTempPath(), $"quartz-weasel-my-{Guid.NewGuid():N}.sql");

        try
        {
            (await new AssertCommand().Execute(Input(new WeaselInput()))).Should().BeFalse("an empty database is not the model");

            (await new PatchCommand().Execute(Input(new PatchInput { FileName = patchFile }))).Should().BeTrue();
            (await File.ReadAllTextAsync(patchFile)).Should().Contain($"CREATE TABLE IF NOT EXISTS `{database.Name}`.`QRTZ_JOB_DETAILS`")
                .And.Contain("`PRIORITY` DESC");

            (await new ApplyCommand().Execute(Input(new WeaselInput()))).Should().BeTrue();
            (await new AssertCommand().Execute(Input(new WeaselInput { DatabaseFlag = "quartz://scheduler/weasel-my-cli" }))).Should().BeTrue();
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
                q.ConfigureScheduler(options => options.InstanceName = "weasel-my-cli");
                q.UsePersistentStore(store =>
                {
                    store.UseMySqlConnector(MySqlConnectorFactory.Instance, database.ConnectionString);
                    store.UseWeaselForMySql(weasel => weasel.LockName = database.Name);
                });
            }));

            return input;
        }
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
                store.UseMySqlConnector(MySqlConnectorFactory.Instance, database.ConnectionString);
                store.UseSystemTextJsonSerializer();
                weasel(store);
            });
        });

        builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
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
