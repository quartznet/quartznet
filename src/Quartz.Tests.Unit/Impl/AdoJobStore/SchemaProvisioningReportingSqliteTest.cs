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

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// What schema provisioning says about itself: it announces a creation only when it made something, and
/// a schema that is not there names every way of dealing with that.
/// </summary>
/// <remarks>
/// A SQLite file, because both claims are about a store starting twice against the same database.
/// </remarks>
public sealed class SchemaProvisioningReportingSqliteTest
{
    private const int SchemaCreatedEventId = 3039;

    private string databaseFile = null!;
    private string connectionString = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-provisioning-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";
    }

    [TearDown]
    public void DeleteDatabase()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(databaseFile))
        {
            File.Delete(databaseFile);
        }
    }

    /// <summary>
    /// The creation is announced once, by the start that made the objects. A restart, and every node of
    /// a cluster after the first, made nothing and says nothing at Information.
    /// </summary>
    /// <remarks>
    /// The scripts are guarded throughout, so running one against a complete schema is a no-op — which
    /// is why the line appeared at every start of every node while being true of none of them.
    /// </remarks>
    [Test]
    public async Task TheCreationIsAnnouncedOnlyByTheStartThatMadeSomething()
    {
        RecordingLoggerProvider firstRun = new();
        await using (SchedulerHandle first = await BuildScheduler(firstRun, provisionSchema: true))
        {
            firstRun.Entries.Should().Contain(entry => entry.EventId.Id == SchemaCreatedEventId,
                "this start found an empty database and made the schema");
        }

        RecordingLoggerProvider secondRun = new();
        await using (SchedulerHandle second = await BuildScheduler(secondRun, provisionSchema: true))
        {
            secondRun.Entries.Should().NotContain(entry => entry.EventId.Id == SchemaCreatedEventId,
                "the schema was already complete, so nothing was created and nothing is claimed");
            secondRun.Entries.Should().Contain(
                entry => entry.Level == LogLevel.Debug && entry.Message.Contains("is complete", StringComparison.Ordinal),
                "a start that made nothing still says what it found, at a level an operator opts in to");
        }
    }

    /// <summary>
    /// A schema that is not there names the typed option, the builder call and the value that creates
    /// it — not just the flat key and how to switch the check off.
    /// </summary>
    [Test]
    public async Task ASchemaThatIsNotThereNamesEveryWayOfDealingWithIt()
    {
        Func<Task> act = async () =>
        {
            await using SchedulerHandle handle = await BuildScheduler(new RecordingLoggerProvider(), provisionSchema: false);
        };

        (await act.Should().ThrowAsync<SchedulerException>())
            .WithMessage("*ProvisionSchema()*", "the one call that fixes this is named")
            .WithMessage("*SchemaProvisioning*", "and so is the option it sets")
            .WithMessage("*CreateIfMissing*", "and the value that means 'make what is missing'")
            .WithMessage("*database/tables/*", "and where the scripts are, for a schema you create yourself");
    }

    private async Task<SchedulerHandle> BuildScheduler(RecordingLoggerProvider recorder, bool provisionSchema)
    {
        ServiceCollection services = new();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(recorder);
        });

        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "provisioning-reporting";
                options.InstanceId = "one";
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, connectionString);
                if (provisionSchema)
                {
                    store.ProvisionSchema();
                }
            });
        });

        ServiceProvider container = services.BuildServiceProvider();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        return new SchedulerHandle(container, scheduler);
    }

    private sealed class SchedulerHandle : IAsyncDisposable
    {
        private readonly ServiceProvider container;
        private readonly IScheduler scheduler;

        public SchedulerHandle(ServiceProvider container, IScheduler scheduler)
        {
            this.container = container;
            this.scheduler = scheduler;
        }

        public async ValueTask DisposeAsync()
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
            await container.DisposeAsync();
        }
    }
}
