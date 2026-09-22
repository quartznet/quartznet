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

using System.Globalization;
using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Serialization.SystemTextJson;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// The history routes read the history of the scheduler the route names, from the store that scheduler
/// keeps it in.
/// </summary>
/// <remarks>
/// <para>
/// A named scheduler that calls <c>UseExecutionHistory()</c> keeps its history in a store keyed by its
/// name, in its own database; the routes read the container's unkeyed store whatever the route named, so
/// they answered such a scheduler with the process's in-memory history — an empty page for a scheduler
/// that had run all day. They now resolve the store the way the dashboard's client does, through the one
/// rule both share.
/// </para>
/// <para>
/// Two schedulers, two SQLite files, one row each: a route that read the wrong store would answer with
/// nothing, or with the other scheduler's row.
/// </para>
/// </remarks>
public sealed class SchedulerHistoryStoreRoutingTest
{
    private readonly List<SqliteTestDatabase> databases = [];
    private readonly List<IScheduler> schedulers = [];
    private WebApplicationFactory<Program>? factory;

    [TearDown]
    public async Task DisposeApplication()
    {
        foreach (IScheduler scheduler in schedulers)
        {
            await scheduler.Shutdown();
        }

        schedulers.Clear();

        if (factory is not null)
        {
            await factory.DisposeAsync();
            factory = null;
        }

        foreach (SqliteTestDatabase database in databases)
        {
            database.Dispose();
        }

        databases.Clear();
    }

    [Test]
    public async Task EachNamedSchedulersRoutesReadItsOwnDatabaseHistory()
    {
        SqliteTestDatabase alphaDatabase = Database("history-alpha");
        SqliteTestDatabase betaDatabase = Database("history-beta");

        TestContentRoot.Apply();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddQuartz("alpha", quartz => quartz.UsePersistentStore(store => History(store, alphaDatabase)));
            services.AddQuartz("beta", quartz => quartz.UsePersistentStore(store => History(store, betaDatabase)));
        }));

        // Built, which provisions each schema and binds each scheduler where the routes look it up.
        schedulers.Add(await factory.Services.GetRequiredKeyedService<ISchedulerFactory>("alpha").GetScheduler());
        schedulers.Add(await factory.Services.GetRequiredKeyedService<ISchedulerFactory>("beta").GetScheduler());

        DateTimeOffset firedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await Record(factory.Services, "alpha", "import", firedAt);
        await Record(factory.Services, "beta", "report", firedAt);

        using HttpClient client = factory.CreateClient();

        (await JobNames(client, "alpha")).Should().Equal(["import"],
            "alpha keeps its history in its own database, and a route that read the container's shared store "
            + "would answer an empty page for a scheduler that has run");

        (await JobNames(client, "beta")).Should().Equal(["report"],
            "and beta's route reads beta's database, not alpha's and not the shared store");

        (await MisfireCount(client, "alpha", firedAt.AddMinutes(-1))).Should().Be(1,
            "the misfire routes resolve the same store as the execution routes");
        (await MisfireCount(client, "beta", firedAt.AddMinutes(-1))).Should().Be(0);
    }

    private SqliteTestDatabase Database(string name)
    {
        SqliteTestDatabase database = new(name);
        databases.Add(database);
        return database;
    }

    private static void History(IPersistentStoreBuilder store, SqliteTestDatabase database)
    {
        store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
        store.ProvisionSchema();
        store.UseExecutionHistory();
    }

    /// <summary>
    /// Writes one execution through the scheduler's own store, and for <c>alpha</c> one misfire as well.
    /// </summary>
    private static async Task Record(IServiceProvider services, string schedulerName, string jobName, DateTimeOffset firedAt)
    {
        IExecutionHistoryStore store = services.GetRequiredKeyedService<IExecutionHistoryStore>(schedulerName);

        await store.AddExecution(new ExecutionHistoryEntry(
            SchedulerName: schedulerName,
            SchedulerInstanceId: "node-a",
            JobGroup: "batch",
            JobName: jobName,
            TriggerGroup: "nightly",
            TriggerName: "at-midnight",
            FiredAtUtc: firedAt,
            Duration: TimeSpan.FromSeconds(1),
            Succeeded: true,
            ExceptionMessage: null));

        if (schedulerName == "alpha")
        {
            await store.AddMisfire(new MisfireHistoryEntry(
                SchedulerName: schedulerName,
                SchedulerInstanceId: "node-a",
                TriggerGroup: "nightly",
                TriggerName: "at-midnight",
                JobKey: new JobKey(jobName, "batch"),
                MisfiredAtUtc: firedAt,
                ScheduledFireTimeUtc: firedAt.AddMinutes(-5)));
        }
    }

    private static async Task<List<string>> JobNames(HttpClient client, string schedulerName)
    {
        using HttpResponseMessage response = await client.GetAsync($"schedulers/{schedulerName}/history/executions");
        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        PagedResultDto<ExecutionHistoryEntryDto> page = JsonSerializer.Deserialize<PagedResultDto<ExecutionHistoryEntryDto>>(
            body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web).ConfigureWireFormat(new SystemTextJsonSerializerRegistry()))!;

        return page.Items.Select(item => item.JobName).ToList();
    }

    private static async Task<int> MisfireCount(HttpClient client, string schedulerName, DateTimeOffset since)
    {
        string sinceText = Uri.EscapeDataString(since.ToString("O", CultureInfo.InvariantCulture));
        using HttpResponseMessage response = await client.GetAsync($"schedulers/{schedulerName}/history/misfires/count?since={sinceText}");
        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        using JsonDocument document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("count").GetInt32();
    }
}
