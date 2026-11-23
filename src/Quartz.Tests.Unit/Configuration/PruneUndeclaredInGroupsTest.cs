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

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Tests.Unit.Core;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// <see cref="SchedulingOptions.PruneUndeclaredInGroups" />: the first start deletes the stored jobs and
/// triggers in the owned groups that no declaration names (#4415).
/// </summary>
/// <remarks>
/// Each start is a container of its own over one SQLite file, the shape of
/// <see cref="ConfiguredScheduleSwitchedOffTest" />: the declarations are stored as the scheduler is built,
/// and the prune runs when it is started. Every scheduler a test opens is shut down in the teardown, also
/// when the test fails.
/// </remarks>
public sealed class PruneUndeclaredInGroupsTest
{
    /// <summary>Log event <c>ConfigurationLog.UndeclaredJobDeleted</c>.</summary>
    private const int JobDeleted = 4034;

    /// <summary>Log event <c>ConfigurationLog.UndeclaredTriggerUnscheduled</c>.</summary>
    private const int TriggerUnscheduled = 4035;

    /// <summary>Log event <c>ConfigurationLog.UndeclaredContentPruneFailed</c>.</summary>
    private const int PruneFailed = 4036;

    /// <summary>Log event <c>ConfigurationLog.TriggerSwitchedOffByConfiguration</c>.</summary>
    private const int TriggerSwitchedOff = 4032;

    private const string SchedulerName = "prune";
    private const string Owned = "app";

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    private static readonly JobKey job = new("J", Owned);
    private static readonly TriggerKey first = new("T1", Owned);
    private static readonly TriggerKey renamed = new("T2", Owned);

    private SqliteTestDatabase database = null!;
    private RecordingLoggerProvider logs = null!;
    private Firings firings = null!;
    private DirectoryInfo? files;
    private readonly List<ServiceProvider> containers = [];
    private readonly List<IScheduler> schedulers = [];

    [SetUp]
    public void SetUp()
    {
        database = new SqliteTestDatabase("prune-undeclared");
        logs = new RecordingLoggerProvider();
        firings = new Firings();
    }

    [TearDown]
    public async Task TearDown()
    {
        try
        {
            for (int i = schedulers.Count - 1; i >= 0; i--)
            {
                if (schedulers[i].Status is not (SchedulerStatus.ShuttingDown or SchedulerStatus.Shutdown))
                {
                    await schedulers[i].Shutdown(waitForJobsToComplete: true);
                }
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
            logs.Dispose();
            database.Dispose();
            files?.Delete(recursive: true);
            files = null;
        }
    }

    // (1)
    [Test]
    public async Task ARenamedTriggerIsUnscheduledAndTheJobAndTheNewTriggerAreKept()
    {
        await Started(Declare(first));

        IScheduler scheduler = await Started(Declare(renamed), owned: [Owned]);

        (await scheduler.GetTrigger(first)).Should().BeNull("the code no longer declares it, and 'app' is owned");
        (await scheduler.GetTrigger(renamed)).Should().NotBeNull("the trigger the code declares now is stored");
        (await scheduler.GetJobDetail(job)).Should().NotBeNull("the job is still declared");

        logs.Entries.Where(x => x.EventId.Id == JobDeleted).Should().BeEmpty();
        LogEntry entry = logs.Entries.Should().ContainSingle(x => x.EventId.Id == TriggerUnscheduled, "one stale trigger is one line").Which;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Be(
            $"Scheduler '{SchedulerName}' unscheduled trigger '{first}' of job '{job}': no declaration names it, and PruneUndeclaredInGroups lists the group 'app'");
    }

    // (1), the control: what a start does today, and on main.
    [Test]
    public async Task WithoutTheOptionARenamedTriggerKeepsFiring()
    {
        await Started(Declare(first));

        IScheduler scheduler = await Started(Declare(renamed));

        (await scheduler.GetTrigger(first)).Should().NotBeNull(
            "nothing deletes what the code no longer declares unless a group is owned, so the old trigger goes on firing");
        logs.Entries.Where(x => x.EventId.Id is JobDeleted or TriggerUnscheduled or PruneFailed).Should().BeEmpty();
    }

    // (2)
    [Test]
    public async Task AJobNoLongerDeclaredIsDeletedWithItsTriggers()
    {
        JobKey removed = new("J2", Owned);
        TriggerKey removedTrigger = new("T-J2", Owned);

        await Started(q =>
        {
            Declare(first)(q);
            q.AddJob<RecordingJob>(j => j.WithIdentity(removed).StoreDurably());
            q.AddTrigger(t => t.WithIdentity(removedTrigger).ForJob(removed).WithCronSchedule("0 0 3 * * ?"));
        });

        IScheduler scheduler = await Started(Declare(first), owned: [Owned]);

        (await scheduler.GetJobDetail(removed)).Should().BeNull();
        (await scheduler.GetTrigger(removedTrigger)).Should().BeNull("a job's triggers go with it");
        (await scheduler.GetTrigger(first)).Should().NotBeNull();

        logs.Entries.Should().ContainSingle(x => x.EventId.Id == JobDeleted)
            .Which.Message.Should().Be($"Scheduler '{SchedulerName}' deleted job '{removed}' and its triggers: no declaration names it, and PruneUndeclaredInGroups lists its group 'app'");
        logs.Entries.Where(x => x.EventId.Id == TriggerUnscheduled).Should().BeEmpty("the trigger went with its job, which is the line that says so");
    }

    // (3)
    [Test]
    public async Task OnlyTheOwnedGroupsArePruned()
    {
        JobKey operators = new("X", "ops");
        TriggerKey operatorsTrigger = new("X-hourly", "ops");
        JobKey addedAtRunTime = new("Y", Owned);

        IScheduler before = await Opened(Declare(first));
        await before.ScheduleJob(
            JobBuilder.Create<RecordingJob>().WithIdentity(operators).Build(),
            TriggerBuilder.Create().WithIdentity(operatorsTrigger).WithCronSchedule("0 0 * * * ?").Build());
        await before.AddJob(JobBuilder.Create<RecordingJob>().WithIdentity(addedAtRunTime).StoreDurably().Build());
        await before.Shutdown(waitForJobsToComplete: true);

        IScheduler scheduler = await Started(Declare(first), owned: [Owned]);

        (await scheduler.GetJobDetail(operators)).Should().NotBeNull("'ops' is not owned, so an operator's job there is never touched");
        (await scheduler.GetTrigger(operatorsTrigger)).Should().NotBeNull();
        (await scheduler.GetJobDetail(addedAtRunTime)).Should().BeNull("'app' is owned, and no declaration names a job added at run time");
    }

    // (4)
    [Test]
    public async Task ATriggerOfADeclaredJobIsPrunedInAGroupThatIsNotOwned()
    {
        TriggerKey old = new("old", TriggerKey.DefaultGroup);
        JobKey elsewhere = new("K", "ops");
        TriggerKey elsewhereTrigger = new("K-nightly", TriggerKey.DefaultGroup);

        IScheduler before = await Opened(q =>
        {
            Declare(first)(q);
            q.AddTrigger(t => t.WithIdentity(old).ForJob(job).WithCronSchedule("0 0 1 * * ?"));
        });
        await before.ScheduleJob(
            JobBuilder.Create<RecordingJob>().WithIdentity(elsewhere).Build(),
            TriggerBuilder.Create().WithIdentity(elsewhereTrigger).WithCronSchedule("0 0 2 * * ?").Build());
        await before.Shutdown(waitForJobsToComplete: true);

        IScheduler scheduler = await Started(Declare(first), owned: [Owned]);

        (await scheduler.GetTrigger(old)).Should().BeNull(
            "it fires a declared job in an owned group, so it is in scope whatever its own group, and no declaration names it");
        (await scheduler.GetTrigger(elsewhereTrigger)).Should().NotBeNull(
            "DEFAULT is not owned, and its job is in a group that is not owned either");
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == TriggerUnscheduled)
            .Which.Message.Should().EndWith("PruneUndeclaredInGroups lists the group 'app'", "the line names the owned group that brought it in");
    }

    // (5)
    [Test]
    public async Task APendingManualFiringIsKeptInAnOwnedGroup()
    {
        JobKey defaultJob = new("D", JobKey.DefaultGroup);
        TriggerKey adHoc = new("ad-hoc", TriggerKey.DefaultGroup);
        Action<IQuartzBuilder> declareDefault = q => q.AddJob<RecordingJob>(j => j.WithIdentity(defaultJob).StoreDurably());

        IScheduler before = await Opened(declareDefault);
        await before.TriggerJob(defaultJob);
        await before.ScheduleJob(TriggerBuilder.Create().WithIdentity(adHoc).ForJob(defaultJob).WithCronSchedule("0 0 5 * * ?").Build());
        await before.PauseJob(defaultJob);
        await before.Shutdown(waitForJobsToComplete: true);

        IScheduler scheduler = await Started(declareDefault, owned: [JobKey.DefaultGroup]);

        List<TriggerKey> left = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(TriggerKey.DefaultGroup));
        left.Should().ContainSingle("the manual firing is somebody's request rather than a declaration")
            .Which.Name.Should().StartWith("MT_");
        (await scheduler.GetTrigger(adHoc)).Should().BeNull("a trigger without the mark is no declaration's");
    }

    // (6)
    [TestCase("")]
    [TestCase("  ")]
    [TestCase("*")]
    [TestCase(SchedulerConstants.DefaultRecoveryGroup)]
    [TestCase(SchedulerConstants.DefaultFailOverGroup)]
    [TestCase(SchedulerConstants.ScheduledJobGroup)]
    [TestCase(SchedulerConstants.StartupGroup)]
    [TestCase(SchedulerConstants.BackfillGroupPrefix + "x")]
    public void AGroupThatCannotBeOwnedIsRefused(string group)
    {
        SchedulingOptions options = new();
        options.PruneUndeclaredInGroups.Add(Owned);
        options.PruneUndeclaredInGroups.Add(group);

        ValidateOptionsResult result = new SchedulingOptionsValidator().Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().ContainSingle("only the refused name fails, 'app' is fine")
            .Which.Should().StartWith("SchedulingOptions.PruneUndeclaredInGroups (Quartz:Scheduling:PruneUndeclaredInGroups) lists ");
        if (!string.IsNullOrWhiteSpace(group))
        {
            result.Failures.Single().Should().Contain($"'{group}'", "the message names the value it refuses");
        }
    }

    [Test]
    public async Task ARefusedGroupStopsTheSchedulerBeingBuilt()
    {
        Func<Task> act = () => Opened(Declare(first), owned: [SchedulerConstants.DefaultRecoveryGroup]);

        (await act.Should().ThrowAsync<SchedulerConfigException>("bad configuration is reported as a SchedulerConfigException"))
            .Which.Message.Should().Contain($"lists '{SchedulerConstants.DefaultRecoveryGroup}'");
    }

    // (7)
    [Test]
    public async Task AKeyIgnoreDuplicatesPassedOverIsNotPruned()
    {
        await Started(Declare(first, cron: "0 0 4 * * ?"));

        IScheduler scheduler = await Started(
            Declare(first, cron: "0 0 6 * * ?"),
            owned: [Owned],
            services: s => s.Configure<QuartzOptions>(o => o.Scheduling.IgnoreDuplicates = true));

        ((ICronTrigger) (await scheduler.GetTrigger(first))!).CronExpressionString.Should().Be("0 0 4 * * ?",
            "the start passed over the stored copy, and a key passed over is still declared");
        logs.Entries.Where(x => x.EventId.Id is JobDeleted or TriggerUnscheduled).Should().BeEmpty();
    }

    // (8)
    [Test]
    public async Task ASwitchedOffTriggerIsSwitchedOffRatherThanPruned()
    {
        const string cronKey = "Jobs:Report:Cron";
        TriggerKey report = new("report", Owned);
        Action<IQuartzBuilder> declare = q =>
        {
            q.AddJob<RecordingJob>(j => j.WithIdentity(job).StoreDurably());
            q.AddConfiguredTrigger<IJob>(cronKey, static (services, key) => services.GetService<IConfiguration>()?[key],
                (_, configured, trigger) => trigger.WithIdentity(report).ForJob(job).WithCronSchedule(configured ?? "0 0 6 * * ?"));
        };

        await Started(declare, services: s => Configuration(s, new() { [cronKey] = "0 0 4 * * ?" }));

        IScheduler scheduler = await Started(declare, owned: [Owned], services: s => Configuration(s, new() { [cronKey] = "-" }));

        (await scheduler.GetTrigger(report)).Should().BeNull();
        (await scheduler.GetJobDetail(job)).Should().NotBeNull("the job is durable and declared");
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == TriggerSwitchedOff, "4032 says it, as before")
            .Which.Message.Should().EndWith("A copy already stored was unscheduled: True");
        logs.Entries.Where(x => x.EventId.Id is JobDeleted or TriggerUnscheduled).Should().BeEmpty(
            "a trigger switched off is declared, so the prune has nothing to say about it");
    }

    // (9)
    [Test]
    public async Task WhatAnXmlSchedulingFileDeclaresIsKeptWithItsRunState()
    {
        string file = WriteFile("jobs.xml", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <job-scheduling-data xmlns="http://quartznet.sourceforge.net/JobSchedulingData" version="2.0">
              <schedule>
                <job>
                  <name>sweep</name>
                  <group>{Owned}</group>
                  <job-type>{typeof(RecordingJob).AssemblyQualifiedName}</job-type>
                  <durable>true</durable>
                  <recover>false</recover>
                </job>
                <trigger>
                  <simple>
                    <name>sweep-hourly</name>
                    <group>{Owned}</group>
                    <job-name>sweep</job-name>
                    <job-group>{Owned}</job-group>
                    <repeat-count>5</repeat-count>
                    <repeat-interval>3600000</repeat-interval>
                  </simple>
                </trigger>
              </schedule>
            </job-scheduling-data>
            """);

        await TheFilesContentIsKept(q => q.UseXmlSchedulingConfiguration(x =>
        {
            x.Files.Add(file);
            x.FailOnSchedulingError = true;
        }));
    }

    // (9)
    [Test]
    public async Task WhatAJsonSchedulingFileDeclaresIsKeptWithItsRunState()
    {
        string file = WriteFile("jobs.json", $$"""
            {
              "Schedule": {
                "Jobs": [{ "Name": "sweep", "Group": "{{Owned}}", "JobType": "{{typeof(RecordingJob).AssemblyQualifiedName}}", "Durable": true }],
                "Triggers": [{
                  "Name": "sweep-hourly", "Group": "{{Owned}}", "JobName": "sweep", "JobGroup": "{{Owned}}",
                  "Simple": { "Interval": "01:00:00", "RepeatCount": 5 }
                }]
              }
            }
            """);

        await TheFilesContentIsKept(q => q.UseJsonSchedulingConfiguration(x =>
        {
            x.Files.Add(file);
            x.FailOnSchedulingError = true;
        }));
    }

    /// <summary>
    /// A file that does not load declares nothing anyone knows of, so pruning against it would delete what
    /// it declared at the previous start.
    /// </summary>
    [Test]
    public async Task AFileThatDoesNotLoadStopsThePrune()
    {
        JobKey stale = new("stale", Owned);
        IScheduler before = await Opened(_ => { });
        await before.AddJob(JobBuilder.Create<RecordingJob>().WithIdentity(stale).StoreDurably().Build());
        await before.Shutdown(waitForJobsToComplete: true);

        string file = WriteFile("broken.xml", "<job-scheduling-data>this is not a schedule");

        IScheduler scheduler = await Started(q => q.UseXmlSchedulingConfiguration(x => x.Files.Add(file)), owned: [Owned]);

        (await scheduler.GetJobDetail(stale)).Should().NotBeNull("nothing is pruned at a start whose declarations are not all known");
        scheduler.Status.Should().Be(SchedulerStatus.Running, "the start goes on");
        LogEntry entry = logs.Entries.Should().ContainSingle(x => x.EventId.Id == PruneFailed).Which;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Exception.Message.Should().Contain(file).And.Contain("nothing was pruned");
    }

    // (10)
    [Test]
    public async Task TheScheduleSectionsDeclarationsCount()
    {
        TriggerKey nightly = new("nightly", "files");
        TriggerKey stale = new("stale", "files");
        JobKey sweep = new("sweep", "files");
        Dictionary<string, string?> section = new()
        {
            ["Quartz:Schedule:Jobs:0:Name"] = "sweep",
            ["Quartz:Schedule:Jobs:0:Group"] = "files",
            ["Quartz:Schedule:Jobs:0:JobType"] = typeof(RecordingJob).AssemblyQualifiedName,
            ["Quartz:Schedule:Jobs:0:Durable"] = "true",
            ["Quartz:Schedule:Triggers:0:Name"] = "nightly",
            ["Quartz:Schedule:Triggers:0:Group"] = "files",
            ["Quartz:Schedule:Triggers:0:JobName"] = "sweep",
            ["Quartz:Schedule:Triggers:0:JobGroup"] = "files",
            ["Quartz:Schedule:Triggers:0:Cron:Expression"] = "0 0 2 * * ?",
        };

        IScheduler before = await Opened(_ => { }, section: section);
        await before.ScheduleJob(TriggerBuilder.Create().WithIdentity(stale).ForJob(sweep).WithCronSchedule("0 0 3 * * ?").Build());
        await before.Shutdown(waitForJobsToComplete: true);

        // Owned through the section, as an application configures it.
        section["Quartz:Scheduling:PruneUndeclaredInGroups:0"] = "files";
        IScheduler scheduler = await Started(_ => { }, section: section);

        (await scheduler.GetTrigger(nightly)).Should().NotBeNull("the Quartz:Schedule section declares it");
        (await scheduler.GetJobDetail(sweep)).Should().NotBeNull();
        (await scheduler.GetTrigger(stale)).Should().BeNull();
    }

    // (11)
    [Test]
    public async Task TheInMemoryStorePrunesNothing()
    {
        JobKey addedAtRunTime = new("Y", Owned);
        IScheduler scheduler = await Opened(Declare(first), owned: [Owned], persistent: false);
        await scheduler.AddJob(JobBuilder.Create<RecordingJob>().WithIdentity(addedAtRunTime).StoreDurably().Build());

        await scheduler.Start();

        (await scheduler.GetJobDetail(addedAtRunTime)).Should().NotBeNull("a store that keeps nothing across starts has nothing stale to prune");
        logs.Entries.Where(x => x.EventId.Id is JobDeleted or TriggerUnscheduled or PruneFailed).Should().BeEmpty();
    }

    // (12)
    [Test]
    public void TheOptionBindsFromTheSchedulingSection()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Scheduling:PruneUndeclaredInGroups:0"] = "app",
            ["Scheduling:PruneUndeclaredInGroups:1"] = "reports",
        }).Build();

        ServiceCollection services = new();
        services.AddQuartz(configuration);
        services.Configure<QuartzOptions>(o => o.Scheduling.PruneUndeclaredInGroups.Add("ops"));

        using ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<QuartzOptions>>().Value.Scheduling.PruneUndeclaredInGroups
            .Should().Equal(["app", "reports", "ops"], "the section's array is read in order, and code adds to it");
    }

    // (12)
    [Test]
    public void TheOptionBindsFromTheFlatKey()
    {
        ServiceCollection services = new();
        services.AddQuartz(new System.Collections.Specialized.NameValueCollection
        {
            ["quartz.scheduler.pruneUndeclaredInGroups"] = "app, reports",
        });

        using ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<QuartzOptions>>().Value.Scheduling.PruneUndeclaredInGroups
            .Should().Equal(["app", "reports"], "comma-separated and trimmed, and listed once although the key is read twice");
    }

    [Test]
    public void TheFlatKeyIsReadWhenItArrivesThroughQuartzOptions()
    {
        ServiceCollection services = new();
        services.AddQuartz(_ => { });
        services.Configure<QuartzOptions>(o => o.Properties["quartz.scheduler.pruneUndeclaredInGroups"] = "app");

        using ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<QuartzOptions>>().Value.Scheduling.PruneUndeclaredInGroups
            .Should().Equal(["app"], "a key set in a callback is in the bag only once the options are resolved");
    }

    // (13)
    [Test]
    public async Task AFailedPruneIsLoggedAndTheSchedulerStarts()
    {
        JobKey removed = new("J2", Owned);
        await Started(q =>
        {
            Declare(first)(q);
            q.AddJob<RecordingJob>(j => j.WithIdentity(removed).StoreDurably());
        });

        WatchedStore? store = null;
        IScheduler scheduler = await Started(
            Declare(first),
            owned: [Owned],
            wrap: inner => store = new WatchedStore(inner) { FailDeleteJobs = true });

        scheduler.Status.Should().Be(SchedulerStatus.Running, "a failed cleanup leaves what a start without it leaves, and the start goes on");
        store!.DeleteJobsCalls.Should().Be(1, "the premise: the prune got as far as deleting");
        (await scheduler.GetJobDetail(removed)).Should().NotBeNull();

        LogEntry entry = logs.Entries.Should().ContainSingle(x => x.EventId.Id == PruneFailed).Which;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Message.Should().Be($"Scheduler '{SchedulerName}' did not finish pruning the jobs and triggers no declaration names in the groups 'app'; nothing else is deleted at this start, and the start goes on");
        entry.Exception.Should().BeOfType<JobPersistenceException>().Which.Message.Should().Be("refused by the test");
    }

    // (14)
    [Test]
    public async Task AStaleTriggerDueNowNeverFiresOnThePruningNode()
    {
        TriggerKey due = new("due", Owned);
        await Opened(q =>
        {
            Declare(first)(q);
            q.AddTrigger(t => t.WithIdentity(due).ForJob(job).StartNow().WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromHours(1)).RepeatForever()));
        });

        WatchedStore? store = null;
        IScheduler scheduler = await Started(Declare(first), owned: [Owned], wrap: inner => store = new WatchedStore(inner));

        await store!.Acquisitions.Reaches(1).WaitAsync(patience);
        store.AcquiredBeforeThePrune.Should().BeFalse("the loop is unpaused only once the prune is done");
        store.Acquired.Should().NotContain(due, "a trigger the prune deletes is never handed to this node's loop");
        (await scheduler.GetTrigger(due)).Should().BeNull();
        firings.Fired.Entries.Should().NotContain(due);
    }

    // (15)
    [Test]
    public async Task TwoNodesStartingTogetherPruneEachKeyOnce()
    {
        JobKey removed = new("J2", Owned);
        TriggerKey dropped = new("dropped", Owned);
        await Opened(q =>
        {
            Declare(first)(q);
            q.AddJob<RecordingJob>(j => j.WithIdentity(removed).StoreDurably());
            q.AddTrigger(t => t.WithIdentity(dropped).ForJob(job).WithCronSchedule("0 0 1 * * ?"));
        });

        // SQLite refuses clustering, so the two nodes share the file unclustered: what is tested is two
        // prunes over the same rows at once, which is all a cluster adds to it.
        IScheduler one = await Opened(Declare(first), owned: [Owned], instanceId: "one");
        IScheduler two = await Opened(Declare(first), owned: [Owned], instanceId: "two");

        await Task.WhenAll(one.Start().AsTask(), two.Start().AsTask());

        (await one.GetJobDetail(removed)).Should().BeNull();
        (await one.GetTrigger(dropped)).Should().BeNull();
        logs.Entries.Where(x => x.EventId.Id == PruneFailed).Should().BeEmpty("the second node finds the keys gone, which is not an error");
        logs.Entries.Where(x => x.EventId.Id == JobDeleted).Should().ContainSingle("each key is deleted once, by whichever node got there first");
        logs.Entries.Where(x => x.EventId.Id == TriggerUnscheduled).Should().ContainSingle();
    }

    // (16)
    [Test]
    public async Task EachSchedulerOnADatabasePrunesOnlyItsOwnRows()
    {
        JobKey north = new("north-job", Owned);
        JobKey south = new("south-job", Owned);
        JobKey northStale = new("north-stale", Owned);

        IScheduler northBefore = await Opened(q => q.AddJob<RecordingJob>(j => j.WithIdentity(north).StoreDurably()), name: "north");
        await northBefore.AddJob(JobBuilder.Create<RecordingJob>().WithIdentity(northStale).StoreDurably().Build());
        await northBefore.Shutdown(waitForJobsToComplete: true);
        await Opened(q => q.AddJob<RecordingJob>(j => j.WithIdentity(south).StoreDurably()), name: "south");

        IScheduler scheduler = await Started(q => q.AddJob<RecordingJob>(j => j.WithIdentity(north).StoreDurably()), owned: [Owned], name: "north");

        (await scheduler.GetJobDetail(northStale)).Should().BeNull("north prunes its own rows");
        IScheduler southAfter = await Opened(_ => { }, name: "south");
        (await southAfter.GetJobDetail(south)).Should().NotBeNull("south's rows are south's, whatever group they are in");
        logs.Entries.Should().ContainSingle(x => x.EventId.Id == JobDeleted).Which.Message.Should().Contain("'north'");
    }

    /// <summary>
    /// A trigger the scheduler stores for itself is no declaration's, even as a trigger of a declared job in
    /// an owned group: the start-up run is stored by its plugin just before the prune, and must still fire.
    /// </summary>
    [Test]
    public async Task ARunAtStartupTriggerOfADeclaredJobIsKept()
    {
        await Started(q => q.AddJob<RecordingJob>(j => j.WithIdentity(job).StoreDurably()).RunAtStartup(job), owned: [Owned]);

        await firings.Fired.Reaches(1).WaitAsync(patience);
        firings.Fired.Entries.Should().ContainSingle().Which.Group.Should().Be(SchedulerConstants.StartupGroup);
        logs.Entries.Where(x => x.EventId.Id is TriggerUnscheduled or PruneFailed).Should().BeEmpty();
    }

    [Test]
    public async Task OnlyTheFirstStartPrunes()
    {
        JobKey addedAtRunTime = new("Y", Owned);
        IScheduler scheduler = await Started(Declare(first), owned: [Owned]);
        await scheduler.Standby();
        await scheduler.AddJob(JobBuilder.Create<RecordingJob>().WithIdentity(addedAtRunTime).StoreDurably().Build());

        await scheduler.Start();

        (await scheduler.GetJobDetail(addedAtRunTime)).Should().NotBeNull("a start after standby resumes; it does not prune again");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Helpers
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// Fires the file's trigger at the first start and pauses it there, then starts again owning its group:
    /// the trigger is still stored, and still remembers that firing.
    /// </summary>
    private async Task TheFilesContentIsKept(Action<IQuartzBuilder> plugin)
    {
        TriggerKey hourly = new("sweep-hourly", Owned);
        JobKey stale = new("stale", Owned);

        IScheduler before = await Started(plugin);
        await firings.Fired.Reaches(1).WaitAsync(patience);
        await before.PauseTrigger(hourly);
        await before.AddJob(JobBuilder.Create<RecordingJob>().WithIdentity(stale).StoreDurably().Build());
        DateTimeOffset? fired = (await before.GetTrigger(hourly))!.PreviousFireTimeUtc;
        await before.Shutdown(waitForJobsToComplete: true);
        fired.Should().NotBeNull("the premise: the first start fired the file's trigger");

        IScheduler scheduler = await Started(plugin, owned: [Owned]);

        ITrigger? kept = await scheduler.GetTrigger(hourly);
        kept.Should().NotBeNull("the file declares it, and a file is a declaration as much as code is");
        kept!.PreviousFireTimeUtc.Should().Be(fired, "the start re-applied the file over the stored trigger rather than the prune deleting it");
        (await scheduler.GetTriggerState(hourly)).Should().Be(TriggerState.Paused);
        (await scheduler.GetJobDetail(stale)).Should().BeNull("the premise: the prune ran, and took what no declaration names");
        logs.Entries.Where(x => x.EventId.Id == TriggerUnscheduled).Should().BeEmpty();
    }

    private string WriteFile(string name, string content)
    {
        files ??= Directory.CreateTempSubdirectory("quartz-prune-undeclared-");
        string path = Path.Combine(files.FullName, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>
    /// <see cref="job" />, durable, and the one trigger named.
    /// </summary>
    private static Action<IQuartzBuilder> Declare(TriggerKey trigger, string cron = "0 0 6 * * ?")
    {
        return q =>
        {
            q.AddJob<RecordingJob>(j => j.WithIdentity(job).StoreDurably());
            q.AddTrigger(t => t.WithIdentity(trigger).ForJob(job).WithCronSchedule(cron));
        };
    }

    private static void Configuration(IServiceCollection services, Dictionary<string, string?> values)
    {
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }

    private async Task<IScheduler> Started(
        Action<IQuartzBuilder> declare,
        string[]? owned = null,
        Action<IServiceCollection>? services = null,
        Func<IJobStore, IJobStore>? wrap = null,
        Dictionary<string, string?>? section = null,
        string name = SchedulerName)
    {
        IScheduler scheduler = await Opened(declare, owned, services, wrap, section, name: name);
        await scheduler.Start();
        return scheduler;
    }

    private async Task<IScheduler> Opened(
        Action<IQuartzBuilder> declare,
        string[]? owned = null,
        Action<IServiceCollection>? services = null,
        Func<IJobStore, IJobStore>? wrap = null,
        Dictionary<string, string?>? section = null,
        bool persistent = true,
        string name = SchedulerName,
        string instanceId = "one")
    {
        // A start that has an earlier one before it runs alone, as a single-version deploy does.
        foreach (IScheduler earlier in schedulers.Where(x => x.Status is not (SchedulerStatus.ShuttingDown or SchedulerStatus.Shutdown) && x.SchedulerInstanceId == instanceId && x.SchedulerName == name).ToList())
        {
            await earlier.Shutdown(waitForJobsToComplete: true);
        }

        ServiceCollection collection = new();
        collection.AddLogging(logging => logging.AddProvider(logs));
        collection.AddSingleton(firings);

        void Configure(IQuartzBuilder q)
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = name;
                options.InstanceId = instanceId;
            });

            if (wrap is not null)
            {
                // First wins: the persistent store below is configured, and this is the store built.
                q.UseJobStore(provider => wrap(ActivatorUtilities.CreateInstance<LocalTransactionJobStore>(provider)));
            }

            if (persistent)
            {
                q.UsePersistentStore(store =>
                {
                    store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                    store.ProvisionSchema();
                });
            }

            declare(q);
        }

        if (section is null)
        {
            collection.AddQuartz(Configure);
        }
        else
        {
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(section).Build();
            collection.AddSingleton(configuration);
            collection.AddQuartz(configuration.GetSection("Quartz"), Configure);
        }

        if (owned is not null)
        {
            collection.Configure<QuartzOptions>(o => o.Scheduling.PruneUndeclaredInGroups.AddRange(owned));
        }

        services?.Invoke(collection);

        ServiceProvider provider = collection.BuildServiceProvider();
        containers.Add(provider);

        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        schedulers.Add(scheduler);
        return scheduler;
    }

    public sealed class Firings
    {
        public CallLog<TriggerKey> Fired { get; } = new();
    }

    public sealed class RecordingJob(Firings firings) : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            firings.Fired.Record(context.Trigger.Key);
            return default;
        }
    }

    /// <summary>
    /// The persistent store, saying in which order the prune and the loop reached it, and failing a job
    /// deletion when told to.
    /// </summary>
    private sealed class WatchedStore(IJobStore inner) : DelegatingJobStore(inner)
    {
        /// <summary>
        /// How long the prune's trigger deletion waits for an acquisition, so a loop unpaused too early is
        /// caught in the act rather than by luck.
        /// </summary>
        private static readonly TimeSpan loopWindow = TimeSpan.FromMilliseconds(250);

        private int deleteJobsCalls;

        public bool FailDeleteJobs { get; init; }

        public int DeleteJobsCalls => Volatile.Read(ref deleteJobsCalls);

        public CallLog<TriggerAcquisitionRequest> Acquisitions { get; } = new();

        public ConcurrentQueue<TriggerKey> Acquired { get; } = new();

        public bool AcquiredBeforeThePrune { get; private set; }

        public override ValueTask<List<JobKey>> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref deleteJobsCalls);
            if (FailDeleteJobs)
            {
                throw new JobPersistenceException("refused by the test");
            }

            return base.DeleteJobs(jobKeys, cancellationToken);
        }

        public override async ValueTask<List<TriggerKey>> DeleteTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
        {
            await Task.WhenAny(Acquisitions.Reaches(1), Task.Delay(loopWindow, cancellationToken)).ConfigureAwait(false);
            AcquiredBeforeThePrune |= Acquisitions.Count > 0;
            return await base.DeleteTriggers(triggerKeys, cancellationToken).ConfigureAwait(false);
        }

        public override async ValueTask<List<IOperableTrigger>> AcquireNextTriggers(TriggerAcquisitionRequest request, CancellationToken cancellationToken = default)
        {
            Acquisitions.Record(request);
            List<IOperableTrigger> acquired = await base.AcquireNextTriggers(request, cancellationToken).ConfigureAwait(false);
            foreach (IOperableTrigger trigger in acquired)
            {
                Acquired.Enqueue(trigger.Key);
            }

            return acquired;
        }
    }
}
