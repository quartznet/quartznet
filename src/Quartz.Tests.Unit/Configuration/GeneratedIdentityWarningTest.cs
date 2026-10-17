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

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// A job or trigger registered through the container with no <c>WithIdentity</c> is stored under a key
/// generated afresh on every start, so a persistent store keeps one more copy each time and every copy
/// of a trigger fires. The scheduler says so as it applies the registrations (#4095).
/// </summary>
/// <remarks>
/// A SQLite file is the persistent store; the scheduler is created and never started, because the
/// registrations are applied when it is created.
/// </remarks>
public sealed class GeneratedIdentityWarningTest
{
    /// <summary>Log event <c>ConfigurationLog.GeneratedIdentityInPersistentStore</c>.</summary>
    private const int GeneratedIdentityInPersistentStore = 4030;

    private SqliteTestDatabase database = null!;
    private RecordingLoggerProvider logs = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("generated-identity");
        logs = new RecordingLoggerProvider();
    }

    [TearDown]
    public void DeleteDatabase()
    {
        logs.Dispose();
        database.Dispose();
    }

    [Test]
    public async Task AJobRegisteredWithoutAnIdentityWarnsOverAPersistentStore()
    {
        IReadOnlyList<IJobDetail> stored = await Apply(persistent: true, q => q.AddJob<ReportJob>(j => j.StoreDurably()));

        LogEntry warning = logs.Entries.Should().ContainSingle(x => x.EventId.Id == GeneratedIdentityInPersistentStore,
            "one registration without an identity is one warning").Which;
        warning.Level.Should().Be(LogLevel.Warning);
        warning.Message.Should().StartWith($"AddJob<{typeof(ReportJob).FullName}> gave its job no identity",
            "the registration is named by its call and its job type, which is how the reader finds it in code");
        warning.Message.Should().Contain($"'{stored.Single().Key}'", "the generated key is the one in the store")
            .And.Contain("each start adds another copy")
            .And.Contain("WithIdentity(...)");
    }

    [Test]
    public async Task AJobGivenAnIdentityDoesNotWarn()
    {
        await Apply(persistent: true, q => q.AddJob<ReportJob>(j => j.WithIdentity("reports").StoreDurably()));

        logs.Entries.Should().NotContain(x => x.EventId.Id == GeneratedIdentityInPersistentStore,
            "a chosen key is the same on every start, so the store replaces rather than adds");
    }

    [Test]
    public async Task TheInMemoryStoreDoesNotWarn()
    {
        await Apply(persistent: false, q => q.AddJob<ReportJob>(j => j.StoreDurably()));

        logs.Entries.Should().NotContain(x => x.EventId.Id == GeneratedIdentityInPersistentStore,
            "the in-memory store starts empty, so a generated key leaves nothing behind");
    }

    [Test]
    public async Task AJobNamedWithAGuidOnPurposeDoesNotWarn()
    {
        string name = Guid.NewGuid().ToString();

        await Apply(persistent: true, q => q.AddJob<ReportJob>(j => j.WithIdentity(name).StoreDurably()));

        logs.Entries.Should().NotContain(x => x.EventId.Id == GeneratedIdentityInPersistentStore,
            "the warning is about the registration having given no identity, not about what the key looks like");
    }

    [Test]
    public async Task AScheduledJobWithoutAnIdentityWarnsOnceForItsTrigger()
    {
        IReadOnlyList<ITrigger> stored = await ApplyAndReadTriggers(q => q.ScheduleJob<ReportJob>(t => t
            .StartAt(DateTimeOffset.UtcNow.AddHours(1))
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())));

        LogEntry warning = logs.Entries.Should().ContainSingle(x => x.EventId.Id == GeneratedIdentityInPersistentStore,
            "the job takes the trigger's generated key, so the two are one registration and one warning").Which;
        warning.Message.Should().StartWith($"ScheduleJob<{typeof(ReportJob).FullName}> gave its trigger no identity")
            .And.Contain($"'{stored.Single().Key}'");
    }

    [Test]
    public async Task AScheduledJobWithAnIdentityOnlyOnItsJobStillWarnsForItsTrigger()
    {
        await ApplyAndReadTriggers(q => q.ScheduleJob<ReportJob>(
            t => t.StartAt(DateTimeOffset.UtcNow.AddHours(1)),
            j => j.WithIdentity("reports")));

        logs.Entries.Should().ContainSingle(x => x.EventId.Id == GeneratedIdentityInPersistentStore,
                "each start adds another trigger for the same job, and each of them fires it")
            .Which.Message.Should().StartWith($"ScheduleJob<{typeof(ReportJob).FullName}> gave its trigger no identity");
    }

    [Test]
    public async Task AScheduledJobWhoseTriggerHasAnIdentityDoesNotWarn()
    {
        await ApplyAndReadTriggers(q => q.ScheduleJob<ReportJob>(t => t
            .WithIdentity("hourly-reports")
            .StartAt(DateTimeOffset.UtcNow.AddHours(1))));

        logs.Entries.Should().NotContain(x => x.EventId.Id == GeneratedIdentityInPersistentStore,
            "the job takes the trigger's chosen key, so neither is generated");
    }

    [Test]
    public async Task ATriggerAddedByJobKeyWithoutAnIdentityNamesItsJob()
    {
        IReadOnlyList<ITrigger> stored = await ApplyAndReadTriggers(q =>
        {
            q.AddJob<ReportJob>(j => j.WithIdentity("reports").StoreDurably());
            q.AddTrigger(t => t.ForJob("reports").StartAt(DateTimeOffset.UtcNow.AddHours(1)));
        });

        logs.Entries.Should().ContainSingle(x => x.EventId.Id == GeneratedIdentityInPersistentStore)
            .Which.Message.Should().StartWith("AddTrigger for job 'DEFAULT.reports' gave its trigger no identity",
                "a trigger added by key names no job type, so the job it fires stands in for one")
            .And.Contain($"'{stored.Single().Key}'");
    }

    [Test]
    public async Task ATriggerAddedForAJobTypeWithoutAnIdentityNamesTheType()
    {
        await ApplyAndReadTriggers(q =>
        {
            q.AddJob<ReportJob>(j => j.WithIdentity("reports").StoreDurably());
            q.AddTrigger<ReportJob>(t => t.ForJob("reports").StartAt(DateTimeOffset.UtcNow.AddHours(1)));
        });

        logs.Entries.Should().ContainSingle(x => x.EventId.Id == GeneratedIdentityInPersistentStore)
            .Which.Message.Should().StartWith($"AddTrigger<{typeof(ReportJob).FullName}> gave its trigger no identity");
    }

    [Test]
    public async Task EachRegistrationWithoutAnIdentityWarnsOnItsOwn()
    {
        await Apply(persistent: true, q =>
        {
            q.AddJob<ReportJob>(j => j.StoreDurably());
            q.AddJob(typeof(ReportJob), j => j.StoreDurably());
            q.AddJob<ReportJob>(j => j.WithIdentity("named").StoreDurably());
        });

        logs.Entries.Where(x => x.EventId.Id == GeneratedIdentityInPersistentStore).Select(x => x.Message)
            .Should().HaveCount(2, "two of the three registrations gave no identity")
            .And.Contain(x => x.StartsWith($"AddJob(typeof({typeof(ReportJob).FullName})) gave its job no identity", StringComparison.Ordinal));
    }

    private async Task<IReadOnlyList<ITrigger>> ApplyAndReadTriggers(Action<IQuartzBuilder> configure)
    {
        List<ITrigger> triggers = [];
        await Apply(persistent: true, configure, async scheduler =>
        {
            foreach (TriggerKey key in await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.AnyGroup()))
            {
                triggers.Add((await scheduler.GetTrigger(key))!);
            }
        });

        return triggers;
    }

    private async Task<IReadOnlyList<IJobDetail>> Apply(
        bool persistent,
        Action<IQuartzBuilder> configure,
        Func<IScheduler, Task>? inspect = null)
    {
        ServiceCollection services = new();
        services.AddLogging(logging => logging.AddProvider(logs));
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "generated-identity";
                options.InstanceId = "one";
            });

            if (persistent)
            {
                q.UsePersistentStore(store =>
                {
                    store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                    store.ProvisionSchema();
                });
            }

            configure(q);
        });

        await using ServiceProvider container = services.BuildServiceProvider();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            List<IJobDetail> jobs = [];
            foreach (JobKey key in await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup()))
            {
                jobs.Add((await scheduler.GetJobDetail(key))!);
            }

            if (inspect is not null)
            {
                await inspect(scheduler);
            }

            return jobs;
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    private sealed class ReportJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }
}
