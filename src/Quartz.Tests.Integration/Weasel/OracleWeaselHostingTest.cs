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

using Weasel.CommandLine;
using Weasel.Core.CommandLine;

namespace Quartz.Tests.Integration.Weasel;

/// <summary>
/// Hosted applications whose Oracle store hands its schema to Weasel: at startup, and from JasperFx's
/// command line.
/// </summary>
[Category("db-oracle")]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[Parallelizable(ParallelScope.All)]
public sealed class OracleWeaselHostingTest
{
    private static readonly ConcurrentDictionary<string, TaskCompletionSource> firings = new(StringComparer.Ordinal);

    private OracleWeaselDatabase database = null!;

    [SetUp]
    public async Task CreateEmptySchema()
    {
        database = await OracleWeaselDatabase.CreateAsync();
    }

    [TearDown]
    public async Task DropSchema()
    {
        await database.DisposeAsync();
    }

    [Test]
    public async Task AHostOnAnEmptySchemaCreatesItAndRunsAJob()
    {
        HostApplicationBuilder builder = Builder();
        AddQuartz(builder, "weasel-ora-hosted", store => store.UseWeaselForOracle());

        TaskCompletionSource fired = firings.GetOrAdd("weasel-ora-hosted", _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

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
    public async Task AutoCreateNoneOnAnEmptySchemaLeavesItToTheStoresValidation()
    {
        HostApplicationBuilder builder = Builder();
        AddQuartz(builder, "weasel-ora-none", store => store.UseWeaselForOracle(weasel => weasel.AutoCreate = AutoCreate.None));

        using IHost host = builder.Build();
        Func<Task> start = () => host.StartAsync();

        (await start.Should().ThrowAsync<SchedulerException>())
            .Which.ToString().Should().Contain("Database schema validation failed");
    }

    [Test]
    public async Task TheCommandLineAssertsPatchesAndApplies()
    {
        string patchFile = Path.Combine(Path.GetTempPath(), $"quartz-weasel-ora-{Guid.NewGuid():N}.sql");

        try
        {
            (await new AssertCommand().Execute(Input(new WeaselInput()))).Should().BeFalse("an empty schema is not the model");

            (await new PatchCommand().Execute(Input(new PatchInput { FileName = patchFile }))).Should().BeTrue();
            (await File.ReadAllTextAsync(patchFile)).Should().Contain($"CREATE TABLE {database.Name}.QRTZ_JOB_DETAILS")
                .And.Contain("PRIORITY DESC");

            (await new ApplyCommand().Execute(Input(new WeaselInput()))).Should().BeTrue();
            (await new AssertCommand().Execute(Input(new WeaselInput { DatabaseFlag = "quartz://scheduler/weasel-ora-cli" }))).Should().BeTrue();
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
                q.ConfigureScheduler(options => options.InstanceName = "weasel-ora-cli");
                q.UsePersistentStore(store =>
                {
                    store.UseOracle(database.ConnectionString);
                    store.UseWeaselForOracle();
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
                store.UseOracle(database.ConnectionString);
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
