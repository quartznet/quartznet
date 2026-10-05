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

using FirebirdSql.Data.FirebirdClient;

using JasperFx;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Weasel.CommandLine;
using Weasel.Core.CommandLine;

namespace Quartz.Tests.Integration.Weasel;

/// <summary>
/// Hosted applications whose Firebird store hands its schema to Weasel — at startup, and from JasperFx's
/// command line, whose patch runs unchanged through isql.
/// </summary>
[Category("db-firebird")]
public sealed class FirebirdWeaselHostingTest
{
    private static readonly ConcurrentDictionary<string, TaskCompletionSource> firings = new(StringComparer.Ordinal);

    private readonly List<FirebirdWeaselDatabase> databases = [];
    private FirebirdWeaselDatabase database = null!;

    [SetUp]
    public async Task CreateEmptyDatabase()
    {
        database = await CreateDatabaseAsync();
    }

    [TearDown]
    public async Task DropDatabases()
    {
        foreach (FirebirdWeaselDatabase created in databases)
        {
            await created.DisposeAsync();
        }

        databases.Clear();
    }

    [Test]
    public async Task AHostOnAnEmptyDatabaseCreatesTheSchemaAndRunsAJob()
    {
        HostApplicationBuilder builder = Builder();
        AddQuartz(builder, "weasel-fb-hosted", store => store.UseWeaselForFirebird());

        await StartScheduleAndStopAsync(builder, "weasel-fb-hosted");

        (await database.CountAsync("SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$RELATION_NAME STARTING WITH 'QRTZ_'"))
            .Should().Be(15, "Weasel created every table at startup, before the store validated them");
    }

    [Test]
    public async Task AutoCreateNoneOnAnEmptyDatabaseLeavesItToTheStoresValidation()
    {
        HostApplicationBuilder builder = Builder();
        AddQuartz(builder, "weasel-fb-none", store => store.UseWeaselForFirebird(weasel => weasel.AutoCreate = AutoCreate.None));

        using IHost host = builder.Build();
        Func<Task> start = () => host.StartAsync();

        (await start.Should().ThrowAsync<SchedulerException>())
            .Which.ToString().Should().Contain("Database schema validation failed");
    }

    /// <summary>
    /// <c>db-assert</c>, <c>db-patch</c> and <c>db-apply</c> against one scheduler named by
    /// <c>-d quartz://scheduler/…</c>, and the patch run by isql on a second empty database, where it
    /// has to leave exactly the model: every statement Weasel.Firebird writes is followed by
    /// <c>COMMIT;</c>, without which isql loses a guarded object at commit.
    /// </summary>
    [Test]
    public async Task TheCommandLineAssertsPatchesAndApplies()
    {
        string patchFile = Path.Combine(Path.GetTempPath(), $"quartz-weasel-fb-{Guid.NewGuid():N}.sql");
        const string subject = "quartz://scheduler/weasel-fb-cli";

        try
        {
            (await new AssertCommand().Execute(Input(new WeaselInput(), database))).Should().BeFalse("an empty database is not the model");

            (await new PatchCommand().Execute(Input(new PatchInput { FileName = patchFile }, database))).Should().BeTrue();
            string patch = await File.ReadAllTextAsync(patchFile);
            patch.Should().Contain("CREATE TABLE QRTZ_JOB_DETAILS").And.Contain("COMMIT;").And.Contain("SET TERM ^ ;");

            FirebirdWeaselDatabase patched = await CreateDatabaseAsync();
            await patched.RunIsqlAsync(patch);
            (await new AssertCommand().Execute(Input(new WeaselInput { DatabaseFlag = subject }, patched))).Should().BeTrue(
                "the patch, run by isql, builds the schema the model describes");

            (await new ApplyCommand().Execute(Input(new WeaselInput { DatabaseFlag = subject }, database))).Should().BeTrue();
            (await new AssertCommand().Execute(Input(new WeaselInput { DatabaseFlag = subject }, database))).Should().BeTrue();
        }
        finally
        {
            File.Delete(patchFile);
            File.Delete(patchFile.Replace(".sql", ".drop.sql", StringComparison.Ordinal));
        }

        static T Input<T>(T input, FirebirdWeaselDatabase target) where T : WeaselInput
        {
            input.HostBuilder = Host.CreateDefaultBuilder().ConfigureServices(services => services.AddQuartz(q =>
            {
                q.ConfigureScheduler(options => options.InstanceName = "weasel-fb-cli");
                q.UsePersistentStore(store =>
                {
                    store.UseFirebird(FirebirdClientFactory.Instance, target.ConnectionString);
                    store.UseWeaselForFirebird();
                });
            }));

            return input;
        }
    }

    private async Task<FirebirdWeaselDatabase> CreateDatabaseAsync()
    {
        FirebirdWeaselDatabase created = await FirebirdWeaselDatabase.CreateAsync("UTF8");
        databases.Add(created);
        return created;
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
                store.UseFirebird(FirebirdClientFactory.Instance, database.ConnectionString);
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
