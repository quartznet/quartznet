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

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// What <c>Scheduling.IgnoreDuplicates</c> does to a declared job whose key a persistent store already
/// holds, which is the only place the answer is observable at all.
/// </summary>
/// <remarks>
/// <para>
/// A SQLite file, because "what is already there survives a restart" needs a store that survives one.
/// The declared job is applied by <c>ContainerConfigurationProcessor</c> when the scheduler is created,
/// so a second container over the same file is a second start of the same application.
/// </para>
/// <para>
/// The one-liner under test — <c>options.Scheduling.IgnoreDuplicates = true</c> and nothing else — is
/// what the migration guide recommends and what this repository's own Worker and ASP.NET Core examples
/// write. On beta.1 it was a fatal startup error, because <c>OverwriteExistingData</c> defaults to true
/// and the validator refused the pair.
/// </para>
/// </remarks>
public sealed class SchedulingDuplicatesSqliteTest
{
    private static readonly JobKey jobKey = new("declared", "declared-group");

    private string databaseFile = null!;
    private string connectionString = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-scheduling-duplicates-{Guid.NewGuid():N}.db");
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

    [Test]
    public async Task IgnoringDuplicatesOnItsOwnLeavesWhatTheStoreAlreadyHolds()
    {
        await using (SchedulerHandle first = await BuildScheduler("first-run", scheduling: null))
        {
            IJobDetail? stored = await first.Scheduler.GetJobDetail(jobKey);
            stored.Should().NotBeNull();
            stored!.Description.Should().Be("first-run");
        }

        await using SchedulerHandle second = await BuildScheduler(
            "second-run",
            scheduling => scheduling.IgnoreDuplicates = true);

        IJobDetail? afterRestart = await second.Scheduler.GetJobDetail(jobKey);
        afterRestart.Should().NotBeNull();
        afterRestart!.Description.Should().Be("first-run",
            "IgnoreDuplicates on its own means the declared job is passed over, and the default of true "
            + "for OverwriteExistingData is a default rather than a statement");
    }

    /// <summary>
    /// The default is unchanged: a declared job replaces what is stored under its key.
    /// </summary>
    [Test]
    public async Task WithNeitherSettingTheDeclaredJobStillReplacesWhatIsStored()
    {
        await using (SchedulerHandle first = await BuildScheduler("first-run", scheduling: null))
        {
            (await first.Scheduler.GetJobDetail(jobKey)).Should().NotBeNull();
        }

        await using SchedulerHandle second = await BuildScheduler("second-run", scheduling: null);

        IJobDetail? afterRestart = await second.Scheduler.GetJobDetail(jobKey);
        afterRestart!.Description.Should().Be("second-run");
    }

    /// <summary>
    /// Clearing overwriting without asking for duplicates to be ignored is still an error, which is the
    /// third of the three positions the two settings describe.
    /// </summary>
    [Test]
    public async Task ClearingOverwriteWithoutIgnoringDuplicatesStillFailsOnADuplicate()
    {
        await using (SchedulerHandle first = await BuildScheduler("first-run", scheduling: null))
        {
            (await first.Scheduler.GetJobDetail(jobKey)).Should().NotBeNull();
        }

        Func<Task> act = async () =>
        {
            await using SchedulerHandle second = await BuildScheduler(
                "second-run",
                scheduling => scheduling.OverwriteExistingData = false);
        };

        await act.Should().ThrowAsync<ObjectAlreadyExistsException>();
    }

    private async Task<SchedulerHandle> BuildScheduler(string description, Action<SchedulingOptions>? scheduling)
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "scheduling-duplicates";
                options.InstanceId = "one";
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, connectionString);
                store.ProvisionSchema();
            });

            q.AddJob<DeclaredJob>(job => job.WithIdentity(jobKey).WithDescription(description).StoreDurably());
        });

        if (scheduling is not null)
        {
            services.Configure<QuartzOptions>(options => scheduling(options.Scheduling));
        }

        ServiceProvider container = services.BuildServiceProvider();

        // Never started: the declared content is applied when the scheduler is created, which is the
        // whole of what this test is about.
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        return new SchedulerHandle(container, scheduler);
    }

    private sealed class DeclaredJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    private sealed class SchedulerHandle : IAsyncDisposable
    {
        private readonly ServiceProvider container;

        public SchedulerHandle(ServiceProvider container, IScheduler scheduler)
        {
            this.container = container;
            Scheduler = scheduler;
        }

        public IScheduler Scheduler { get; }

        public async ValueTask DisposeAsync()
        {
            await Scheduler.Shutdown(waitForJobsToComplete: false);
            await container.DisposeAsync();
        }
    }
}
