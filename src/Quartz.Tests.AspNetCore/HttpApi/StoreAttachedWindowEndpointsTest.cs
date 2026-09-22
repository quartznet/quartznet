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

using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// The HTTP API in a process that holds a store-attached window: the routes that belong to one process
/// refuse it, as the dashboard's client does.
/// </summary>
/// <remarks>
/// <para>
/// A window is a scheduler this process built over a database other processes run, and it is bound in
/// the repository like any other — so every route that looks a scheduler up by name reaches it. Starting
/// it used to run that cluster's start-up here: recovery put the nodes' acquired triggers back to waiting
/// and deleted their fired-trigger rows. The store now refuses the start itself; these routes refuse
/// first, for all three verbs, with the refusal the dashboard gives and in the shape the API answers a
/// scheduler's refusals in.
/// </para>
/// <para>
/// Standby and shutdown are refused as well although neither writes a row. A window that was shut down
/// is unbound from the repository and never rediscovered — the attached store has already decided about
/// its name — so a shutdown here would take the window away for the life of the process, and neither
/// verb reaches the nodes the caller meant.
/// </para>
/// </remarks>
public sealed class StoreAttachedWindowEndpointsTest
{
    private const string Window = "alpha";

    private SqliteTestDatabase database = null!;
    private WebApplicationFactory<Program>? factory;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("http-api-window");
    }

    [TearDown]
    public async Task DisposeApplication()
    {
        if (factory is not null)
        {
            await factory.DisposeAsync();
            factory = null;
        }

        database.Dispose();
    }

    [Test]
    public async Task StartingAWindowIsRefusedAndChangesNothing()
    {
        await using ServiceProvider node = await Node();
        await node.GetRequiredService<IJobStore>().AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow.AddYears(2),
            MaxCount = 1,
            TimeWindow = TimeSpan.FromDays(1)
        });

        (await Scalar("SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS")).Should().Be(1L);

        await using AttachedStore store = await AttachedApplication();
        using HttpClient client = factory!.CreateClient();

        using HttpResponseMessage response = await client.PostAsync($"schedulers/{Window}/start", content: null);

        await ShouldBeRefused(response, "start");

        IScheduler window = factory.Services.GetRequiredService<ISchedulerRepository>().Lookup(Window)!;
        window.Status.Should().Be(SchedulerStatus.Created, "a window is never started, from anywhere");

        (await Scalar("SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = 'at-midnight'")).Should().Be("ACQUIRED",
            "a start would have recovered the node's acquired trigger back to WAITING");
        (await Scalar("SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS")).Should().Be(1L,
            "a start would have deleted the fired-trigger row of the firing the node is in the middle of");
    }

    [Test]
    public async Task ADelayedStartOfAWindowIsRefusedBeforeItIsQueued()
    {
        await using ServiceProvider node = await Node();
        await using AttachedStore store = await AttachedApplication();
        using HttpClient client = factory!.CreateClient();

        using HttpResponseMessage response = await client.PostAsync($"schedulers/{Window}/start?delay=00:00:01", content: null);

        await ShouldBeRefused(response, "start",
            "a delayed start runs on a task nobody observes, so refusing it there would answer 200 and then "
            + "fail in a log line");
    }

    [Test]
    public async Task StandingAWindowDownIsRefused()
    {
        await using ServiceProvider node = await Node();
        await using AttachedStore store = await AttachedApplication();
        using HttpClient client = factory!.CreateClient();

        using HttpResponseMessage response = await client.PostAsync($"schedulers/{Window}/standby", content: null);

        await ShouldBeRefused(response, "stand");
    }

    [Test]
    public async Task ShuttingAWindowDownIsRefusedAndTheWindowStays()
    {
        await using ServiceProvider node = await Node();
        await using AttachedStore store = await AttachedApplication();
        using HttpClient client = factory!.CreateClient();

        using HttpResponseMessage response = await client.PostAsync($"schedulers/{Window}/shutdown", content: null);

        await ShouldBeRefused(response, "shut");

        factory.Services.GetRequiredService<ISchedulerRepository>().Lookup(Window).Should().NotBeNull(
            "a window that was shut down is unbound and never comes back - its name has been decided about - "
            + "so the refusal is what keeps it on the page");

        using HttpResponseMessage details = await client.GetAsync($"schedulers/{Window}");
        details.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// A window whose store keeps no history here is refused on the history routes, as the dashboard
    /// refuses it, rather than answered with this process's own history.
    /// </summary>
    /// <remarks>
    /// This process's history is what its own schedulers ran. Answering a window's route with it would
    /// be an empty page that reads as a cluster which has run nothing.
    /// </remarks>
    [Test]
    public async Task AWindowsHistoryIsRefusedWhereNoneIsKept()
    {
        await using ServiceProvider node = await Node();
        await using AttachedStore store = await AttachedApplication();
        using HttpClient client = factory!.CreateClient();

        using HttpResponseMessage response = await client.GetAsync($"schedulers/{Window}/history/executions");
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);

        using JsonDocument problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty(HttpApiConstants.ProblemDetailsExceptionType).GetString()
            .Should().Be(nameof(SchedulerException));
        problem.RootElement.GetProperty("detail").GetString().Should()
            .StartWith($"The store attached as 'prod', which '{Window}' is a window onto, keeps no execution history",
                "the refusal is the dashboard's, word for word, because it is decided by the same rule");
    }

    [Test]
    public async Task ASchedulerOfThisProcessIsStillStarted()
    {
        await using ServiceProvider node = await Node();
        await using AttachedStore store = await AttachedApplication();
        using HttpClient client = factory!.CreateClient();

        IScheduler local = await factory.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            using HttpResponseMessage response = await client.PostAsync($"schedulers/{local.SchedulerName}/start", content: null);

            response.StatusCode.Should().Be(HttpStatusCode.OK,
                "the refusal is for a window, and deciding it must not cost a scheduler of this process its routes");
            local.Status.Should().Be(SchedulerStatus.Running);
        }
        finally
        {
            await local.Shutdown();
        }
    }

    /// <summary>
    /// The refusal the dashboard's client gives, in the shape the API gives a scheduler's refusal: a
    /// <c>400</c> naming <see cref="SchedulerException" />, which <c>HttpScheduler</c> rethrows as one.
    /// </summary>
    private static async Task ShouldBeRefused(HttpResponseMessage response, string verb, string because = "")
    {
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, because);

        using JsonDocument problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty(HttpApiConstants.ProblemDetailsExceptionType).GetString()
            .Should().Be(nameof(SchedulerException),
                "a remote caller of IScheduler.Start is owed the exception an in-process caller gets");

        problem.RootElement.GetProperty("detail").GetString().Should()
            .Contain(verb).And.Contain("window").And.Contain($"'{Window}'").And.Contain("'prod'",
                "the refusal says what was asked, why it cannot be done here, and which database the window "
                + "looks onto");
    }

    /// <summary>
    /// A scheduler of another process, which leaves its job and its trigger in the shared database.
    /// </summary>
    private async ValueTask<ServiceProvider> Node()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options => options.InstanceName = Window);
            quartz.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
            });
        });

        ServiceProvider provider = services.BuildServiceProvider();
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await scheduler.ScheduleJob(
            JobBuilder.Create<DummyJob>().WithIdentity("nightly", "batch").StoreDurably().Build(),
            TriggerBuilder.Create()
                .WithIdentity("at-midnight", "nightly")
                .StartAt(DateTimeOffset.UtcNow.AddYears(1))
                .Build());

        return provider;
    }

    /// <summary>
    /// The HTTP API's host, with the node's database attached to it and its one scheduler discovered as a
    /// window.
    /// </summary>
    /// <remarks>
    /// The store is attached by hand rather than through <c>AddQuartzDashboard</c>: the dashboard is only
    /// one way a window comes to be in a repository, and the routes under test must not depend on it.
    /// </remarks>
    private async ValueTask<AttachedStore> AttachedApplication()
    {
        TestContentRoot.Apply();
        factory = new WebApplicationFactory<Program>();

        AttachedStore store = new(
            "prod",
            recipe => recipe.UseSqlite(SqliteFactory.Instance, database.ConnectionString),
            factory.Services);

        (await store.Synchronize()).Should().Equal([Window]);
        return store;
    }

    private async Task<object?> Scalar(string sql)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        return await command.ExecuteScalarAsync();
    }
}
