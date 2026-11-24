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

using FakeItEasy;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.HttpApiContract;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Whether a scheduler's next start stores a trigger again, over a change made to it since: what the dashboard
/// warns an editor with (#4412).
/// </summary>
/// <remarks>
/// Each scheduler is a container of its own, built over the in-memory store or one SQLite file. The declarations
/// are recorded as it is built, and its first start closes the record.
/// </remarks>
public sealed class DeclaredAtStartupTest
{
    private static readonly JobKey job = new("J", "declared");
    private static readonly TriggerKey declared = new("T-declared", "declared");
    private static readonly TriggerKey addedAtRunTime = new("T-run-time", "declared");

    private SqliteTestDatabase? database;
    private readonly List<ServiceProvider> containers = [];
    private readonly List<IScheduler> schedulers = [];

    [TearDown]
    public async Task TearDown()
    {
        try
        {
            foreach (IScheduler scheduler in schedulers)
            {
                await scheduler.Shutdown(waitForJobsToComplete: true);
            }

            for (int i = containers.Count - 1; i >= 0; i--)
            {
                await containers[i].DisposeAsync();
            }
        }
        finally
        {
            schedulers.Clear();
            containers.Clear();
            database?.Dispose();
            database = null;
        }
    }

    [Test]
    public async Task ADeclaredTriggerIsStoredAgainAtTheNextStartAndOneAddedAtRunTimeIsNot([Values] bool persistent)
    {
        IScheduler scheduler = await Started(persistent);
        await AddAtRunTime(scheduler);

        (await StartupDeclarations.IsDeclaredAtStartup(scheduler, declared)).Should().BeTrue(
            "an AddTrigger registration stores its trigger over the stored one at every start, OverwriteExistingData being the default");
        (await StartupDeclarations.IsDeclaredAtStartup(scheduler, addedAtRunTime)).Should().BeFalse(
            "no declaration names a trigger added while the scheduler runs");
        (await StartupDeclarations.IsDeclaredAtStartup(scheduler, new TriggerKey("missing", "declared"))).Should().BeFalse();
    }

    [Test]
    public async Task TheInjectedSchedulerAndADecoratorAnswerAsTheSchedulerDoes()
    {
        IScheduler built = await Started(persistent: false);
        IScheduler injected = containers[^1].GetRequiredService<IScheduler>();
        DelegatingScheduler decorated = new(injected);

        (await StartupDeclarations.IsDeclaredAtStartup(injected, declared)).Should().BeTrue("the container's handle asks the scheduler it resolves to");
        (await StartupDeclarations.IsDeclaredAtStartup(decorated, declared)).Should().BeTrue("a decorator asks the scheduler it ends in");
        (await StartupDeclarations.IsDeclaredAtStartup(decorated, addedAtRunTime)).Should().BeFalse();
        built.Should().NotBeSameAs(injected);
    }

    [Test]
    public async Task ADeclarationThatDoesNotStoreOverTheStoredCopyLeavesAnEditInPlace()
    {
        // IgnoreDuplicates turns OverwriteExistingData off when it is not stated: the next start passes over the
        // stored trigger, edited or not.
        IScheduler scheduler = await Started(persistent: true, scheduling: s => s.IgnoreDuplicates = true);

        (await StartupDeclarations.IsDeclaredAtStartup(scheduler, declared)).Should().BeFalse(
            "the next start keeps the stored trigger, so an edit to it lasts");
    }

    [Test]
    public async Task AStoreThatKeepsNothingStoresEveryDeclaredTriggerAgain()
    {
        IScheduler scheduler = await Started(persistent: false, scheduling: s => s.IgnoreDuplicates = true);

        (await StartupDeclarations.IsDeclaredAtStartup(scheduler, declared)).Should().BeTrue(
            "the in-memory store starts empty, so whatever the directives say, the declaration is what the next start stores");
    }

    [Test]
    public async Task APruningSchedulerAnswersFromTheRecordItPrunesWith()
    {
        IScheduler scheduler = await Started(persistent: true, scheduling: s => s.PruneUndeclaredInGroups.Add("declared"));
        await AddAtRunTime(scheduler);

        (await StartupDeclarations.IsDeclaredAtStartup(scheduler, declared)).Should().BeTrue();
        (await StartupDeclarations.IsDeclaredAtStartup(scheduler, addedAtRunTime)).Should().BeFalse();
    }

    [Test]
    public async Task TheTriggerStateRouteCarriesTheAnswer()
    {
        IScheduler scheduler = await Started(persistent: false);
        await AddAtRunTime(scheduler);

        TriggerStateDto ofDeclared = await SchedulerOperations.GetTriggerState(scheduler, declared, CancellationToken.None);
        TriggerStateDto ofAdded = await SchedulerOperations.GetTriggerState(scheduler, addedAtRunTime, CancellationToken.None);
        TriggerStateDto ofMissing = await SchedulerOperations.GetTriggerState(scheduler, new TriggerKey("missing", "declared"), CancellationToken.None);

        ofDeclared.DeclaredAtStartup.Should().BeTrue();
        ofAdded.DeclaredAtStartup.Should().BeFalse();
        ofMissing.State.Should().Be(TriggerState.None);
        ofMissing.DeclaredAtStartup.Should().BeNull("there is no trigger to edit, so the route does not ask");
    }

    [Test]
    public async Task ASchedulerThatCannotSayAnswersNull()
    {
        IScheduler foreign = A.Fake<IScheduler>();

        (await StartupDeclarations.IsDeclaredAtStartup(foreign, declared)).Should().BeNull(
            "an IScheduler of an application's own has no record of what its start declared");
    }

    private static async Task AddAtRunTime(IScheduler scheduler)
    {
        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(addedAtRunTime)
            .ForJob(job)
            .WithCronSchedule("0 0 7 * * ?")
            .Build());
    }

    private async Task<IScheduler> Started(bool persistent, Action<SchedulingOptions>? scheduling = null)
    {
        if (persistent)
        {
            database ??= new SqliteTestDatabase("declared-at-startup");
        }

        string? connectionString = database?.ConnectionString;

        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "declared-at-startup-" + Guid.NewGuid().ToString("N"));
            if (persistent)
            {
                q.UsePersistentStore(store =>
                {
                    store.UseSqlite(SqliteFactory.Instance, connectionString!);
                    store.ProvisionSchema();
                });
            }

            q.AddJob<NoOpJob>(j => j.WithIdentity(job).StoreDurably());
            q.AddTrigger(t => t.WithIdentity(declared).ForJob(job).WithCronSchedule("0 0 6 * * ?"));
        });

        if (scheduling is not null)
        {
            services.Configure<QuartzOptions>(options => scheduling(options.Scheduling));
        }

        ServiceProvider provider = services.BuildServiceProvider();
        containers.Add(provider);

        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        schedulers.Add(scheduler);
        await scheduler.Start();
        return scheduler;
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
