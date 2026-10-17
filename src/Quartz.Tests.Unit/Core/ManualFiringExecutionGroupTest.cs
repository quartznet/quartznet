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

using Quartz.Core;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// A manual firing counts against its job's execution group (#4092): the job type's
/// <c>[ExecutionGroup]</c>, else the one group every trigger of the job shares, else none.
/// </summary>
/// <remarks>
/// <c>TriggerJob</c> built its one-shot trigger with no group, so a manual firing of a <c>gpu</c> job ran
/// beside the firing holding the only <c>gpu</c> slot, and on a node that refuses <c>gpu</c>. The HTTP API,
/// the dashboard and the agents all fire through it. The schedulers here use the in-memory store, which
/// honours limits and routing as a clustered store does, except where a test needs a job type this node
/// cannot load: that one is a SQLite row naming a type nobody has.
/// </remarks>
[NonParallelizable]
public sealed class ManualFiringExecutionGroupTest
{
    private const string Gpu = "gpu";
    private const string Reports = "reports";
    private const string JobGroup = "manual-firing";

    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Long enough for a firing nothing holds back to have run: it is due at once, and storing it wakes the
    /// scheduler thread.
    /// </summary>
    private static readonly TimeSpan settle = TimeSpan.FromMilliseconds(1500);

    private readonly List<IScheduler> schedulers = [];
    private SqliteTestDatabase? database;

    [SetUp]
    public void SetUp()
    {
        Runs.Reset();
        HoldingJob.Reset();
    }

    [TearDown]
    public async Task TearDown()
    {
        // Opened first, so a test that failed while the holder held its slot does not hang the shutdown.
        HoldingJob.Release();

        foreach (IScheduler scheduler in schedulers)
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        schedulers.Clear();
        database?.Dispose();
        database = null;
    }

    [Test]
    public async Task AManualFiringCarriesTheGroupItsJobTypeDeclares()
    {
        IScheduler scheduler = await Build();
        JobKey job = await AddDurable<GpuJob>("render");

        await scheduler.TriggerJob(job);

        (await TheManualFiring(scheduler, job)).ExecutionGroup.Should().Be(Gpu,
            "the job type says [ExecutionGroup(\"gpu\")], and a firing from the dashboard is as much a gpu firing as a scheduled one");
    }

    /// <summary>
    /// What carrying the group is for: with <c>gpu = 1</c> and a <c>gpu</c> firing holding the slot, the
    /// manual firing waits, and runs once the slot is given back.
    /// </summary>
    [Test]
    public async Task AManualFiringWaitsForASlotInItsJobsExecutionGroup()
    {
        IScheduler scheduler = await Build(q => q.UseExecutionLimits(limits => limits.ForGroup(Gpu, 1)));
        JobKey holder = new("holder", JobGroup);
        await scheduler.ScheduleJob(
            JobBuilder.Create<HoldingJob>().WithIdentity(holder).Build(),
            TriggerBuilder.Create().WithIdentity("holder", JobGroup).WithExecutionGroup(Gpu).StartNow().Build());
        JobKey job = await AddDurable<GpuJob>("render", scheduler);

        await scheduler.Start();
        await HoldingJob.Started.WaitAsync(waitLimit);

        await scheduler.TriggerJob(job);
        await Task.Delay(settle);

        Runs.Happened(job).Should().BeFalse("gpu = 1, the holder has the slot, and the manual firing is a gpu firing");

        HoldingJob.Release();

        (await Runs.Of(job).WaitAsync(waitLimit)).Should().Be(Gpu, "the slot was given back, and the manual firing took it");
    }

    [Test]
    public async Task ATemplateReadsItsPlaceholderFromTheJobsData()
    {
        IScheduler scheduler = await Build();
        JobKey job = await AddDurable<TenantJob>("tenant-report", configure: x => x.UsingJobData("TenantId", "acme"));

        await scheduler.TriggerJob(job);

        (await TheManualFiring(scheduler, job)).ExecutionGroup.Should().Be("tenant:acme");
    }

    [Test]
    public async Task ATemplateReadsItsPlaceholderFromTheFiringsData()
    {
        IScheduler scheduler = await Build();
        JobKey job = await AddDurable<TenantJob>("tenant-report");

        await scheduler.TriggerJob(job, new JobDataMap { ["TenantId"] = "globex" });

        (await TheManualFiring(scheduler, job)).ExecutionGroup.Should().Be("tenant:globex",
            "the firing's data is part of the map the job will see");
    }

    [Test]
    public async Task TheFiringsDataWinsOverTheJobsData()
    {
        IScheduler scheduler = await Build();
        JobKey job = await AddDurable<TenantJob>("tenant-report", configure: x => x.UsingJobData("TenantId", "acme"));

        await scheduler.TriggerJob(job, new JobDataMap { ["TenantId"] = "globex" });

        (await TheManualFiring(scheduler, job)).ExecutionGroup.Should().Be("tenant:globex",
            "the job sees its own data overlaid by the firing's, and its group is spelled from that map");
    }

    /// <summary>
    /// A "Trigger now" does not fail because a template has nothing to read: it takes the group the job's
    /// triggers share, or none.
    /// </summary>
    [Test]
    public async Task ATemplateWithNoValueFallsThroughToTheGroupTheTriggersShare()
    {
        IScheduler scheduler = await Build();
        JobKey shared = await AddDurable<TenantJob>("tenant-report");
        await AddTrigger(scheduler, shared, "nightly", Reports);
        JobKey alone = await AddDurable<TenantJob>("tenant-export");

        await scheduler.TriggerJob(shared);
        await scheduler.TriggerJob(alone);

        (await TheManualFiring(scheduler, shared)).ExecutionGroup.Should().Be(Reports,
            "nothing in either map names TenantId, so the template has no answer and the job's triggers do");
        (await TheManualFiring(scheduler, alone)).ExecutionGroup.Should().BeNull(
            "with no template value and no trigger, the firing is still stored, with no group");
    }

    [Test]
    public async Task AManualFiringTakesTheTriggersGroupOnlyWhenEveryTriggerCarriesIt()
    {
        IScheduler scheduler = await Build();
        JobKey agreeing = await AddDurable<PlainJob>("agreeing");
        await AddTrigger(scheduler, agreeing, "agreeing-1", Reports);
        await AddTrigger(scheduler, agreeing, "agreeing-2", Reports);
        JobKey mixed = await AddDurable<PlainJob>("mixed");
        await AddTrigger(scheduler, mixed, "mixed-1", Reports);
        await AddTrigger(scheduler, mixed, "mixed-2", "batch");
        JobKey partly = await AddDurable<PlainJob>("partly");
        await AddTrigger(scheduler, partly, "partly-1", Reports);
        await AddTrigger(scheduler, partly, "partly-2", executionGroup: null);
        JobKey untriggered = await AddDurable<PlainJob>("untriggered");

        foreach (JobKey job in new[] { agreeing, mixed, partly, untriggered })
        {
            await scheduler.TriggerJob(job);
        }

        (await TheManualFiring(scheduler, agreeing)).ExecutionGroup.Should().Be(Reports,
            "every trigger of the job carries reports, so that is the job's group");
        (await TheManualFiring(scheduler, mixed)).ExecutionGroup.Should().BeNull("two groups is no one group");
        (await TheManualFiring(scheduler, partly)).ExecutionGroup.Should().BeNull("a grouped and an ungrouped trigger is no one group");
        (await TheManualFiring(scheduler, untriggered)).ExecutionGroup.Should().BeNull("no trigger says anything");
    }

    [Test]
    public async Task TheJobTypesGroupWinsOverTheTriggersGroup()
    {
        IScheduler scheduler = await Build();
        JobKey job = await AddDurable<GpuJob>("render");
        await AddTrigger(scheduler, job, "nightly", Reports);

        await scheduler.TriggerJob(job);

        (await TheManualFiring(scheduler, job)).ExecutionGroup.Should().Be(Gpu, "the type's declaration is the first answer");
    }

    /// <summary>
    /// An attribute whose constructor refuses its argument is unreadable, and a firing does not fail over
    /// it: it falls through as a template with no value does.
    /// </summary>
    [Test]
    public async Task AnUnreadableAttributeFallsThroughToTheGroupTheTriggersShare()
    {
        IScheduler scheduler = await Build();
        JobKey job = await AddDurable<UnreadableGroupJob>("unreadable");
        await AddTrigger(scheduler, job, "nightly", Reports);

        await scheduler.TriggerJob(job);

        (await TheManualFiring(scheduler, job)).ExecutionGroup.Should().Be(Reports);
    }

    /// <summary>
    /// A node without the job's assembly — a dashboard host, say — cannot read the attribute, so it skips
    /// it and takes the group the job's triggers share.
    /// </summary>
    [Test]
    public async Task AJobTypeThisNodeCannotLoadFallsThroughToTheGroupTheTriggersShare()
    {
        database = new SqliteTestDatabase("manual-firing-execution-group");
        IScheduler scheduler = await Build(sqlite: database);
        JobKey job = await AddDurable<GpuJob>("render", scheduler);
        await AddTrigger(scheduler, job, "nightly", Reports);

        await using (SqliteConnection connection = new(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "UPDATE QRTZ_JOB_DETAILS SET JOB_CLASS_NAME = 'Nowhere.RenderJob, Nowhere' WHERE JOB_NAME = 'render'";
            (await command.ExecuteNonQueryAsync()).Should().Be(1, "the premise: the stored job names a type this process does not have");
        }

        await scheduler.TriggerJob(job);

        (await TheManualFiring(scheduler, job)).ExecutionGroup.Should().Be(Reports,
            "the type, and so its [ExecutionGroup(\"gpu\")], is not loadable here; the triggers' group is the answer left");
    }

    /// <summary>
    /// Routing applies through ordinary acquisition once the trigger carries the group: a node that serves
    /// only <c>gpu</c> runs the manual firing, which it would not if the firing carried no group.
    /// </summary>
    [Test]
    public async Task ANodeThatServesTheGroupRunsTheManualFiring()
    {
        IScheduler scheduler = await Build(q => q.ServeExecutionGroups(Gpu));
        JobKey job = await AddDurable<GpuJob>("render", scheduler);
        await scheduler.Start();

        await scheduler.TriggerJob(job);

        (await Runs.Of(job).WaitAsync(waitLimit)).Should().Be(Gpu, "the node serves gpu, and the manual firing is a gpu firing");
    }

    /// <summary>
    /// The other half: a node that refuses <c>gpu</c> leaves the manual firing waiting for a node that
    /// serves it, while it runs an ungrouped firing stored after it.
    /// </summary>
    [Test]
    public async Task ANodeThatRefusesTheGroupDoesNotRunTheManualFiring()
    {
        IScheduler scheduler = await Build(q => q.RefuseExecutionGroups(Gpu));
        JobKey gpu = await AddDurable<GpuJob>("render", scheduler);
        JobKey plain = await AddDurable<PlainJob>("plain", scheduler);
        await scheduler.Start();

        await scheduler.TriggerJob(gpu);
        await scheduler.TriggerJob(plain);
        await Runs.Of(plain).WaitAsync(waitLimit);
        await Task.Delay(settle);

        Runs.Happened(gpu).Should().BeFalse("the node refuses gpu, and the manual firing is a gpu firing");
        ITrigger waiting = await TheManualFiring(scheduler, gpu);
        waiting.ExecutionGroup.Should().Be(Gpu);
        (await scheduler.GetTriggerState(waiting.Key)).Should().Be(TriggerState.Normal,
            "declined rather than acquired: it waits, unmodified, for a node that serves gpu");
    }

    /// <summary>
    /// The overload that takes a built trigger: a group the caller named is kept, and a trigger without one
    /// is given its job's.
    /// </summary>
    [Test]
    public async Task AnOperableTriggerKeepsItsOwnGroupAndIsGivenItsJobsWhenItHasNone()
    {
        IScheduler scheduler = await Build();
        JobKey job = await AddDurable<GpuJob>("render");
        QuartzScheduler core = ((StdScheduler) scheduler).scheduler;

        IOperableTrigger named = (IOperableTrigger) TriggerBuilder.Create().WithIdentity("named", JobGroup).ForJob(job).WithExecutionGroup("batch").StartNow().Build();
        IOperableTrigger unnamed = (IOperableTrigger) TriggerBuilder.Create().WithIdentity("unnamed", JobGroup).ForJob(job).StartNow().Build();

        await core.TriggerJob(named);
        await core.TriggerJob(unnamed);

        (await scheduler.GetTrigger(named.Key))!.ExecutionGroup.Should().Be("batch", "the caller named a group, and that is the firing's");
        (await scheduler.GetTrigger(unnamed.Key))!.ExecutionGroup.Should().Be(Gpu, "no group was named, so the job's is taken");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Helpers
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <param name="configure">Anything further the test needs.</param>
    /// <param name="sqlite">A SQLite database to store in, instead of memory.</param>
    private async Task<IScheduler> Build(Action<IQuartzBuilder>? configure = null, SqliteTestDatabase? sqlite = null)
    {
        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q =>
            {
                q.ConfigureScheduler(options =>
                {
                    options.InstanceName = "manual-firing-" + Guid.NewGuid().ToString("N");
                    options.IdleWaitTime = TimeSpan.FromSeconds(1);
                });

                if (sqlite is not null)
                {
                    q.UsePersistentStore(persistent =>
                    {
                        persistent.UseSqlite(SqliteFactory.Instance, sqlite.ConnectionString);
                        persistent.ProvisionSchema();
                    });
                }
                else
                {
                    q.UseInMemoryStore();
                }

                configure?.Invoke(q);
            })
            .BuildScheduler();

        schedulers.Add(scheduler);
        return scheduler;
    }

    private async Task<JobKey> AddDurable<TJob>(string name, IScheduler? scheduler = null, Func<JobBuilder<TJob>, JobBuilder<TJob>>? configure = null)
        where TJob : IJob
    {
        JobKey key = new(name, JobGroup);
        JobBuilder<TJob> builder = JobBuilder.Create<TJob>().WithIdentity(key).StoreDurably();
        if (configure is not null)
        {
            builder = configure(builder);
        }

        await (scheduler ?? schedulers[^1]).AddJob(builder.Build());
        return key;
    }

    /// <remarks>
    /// Due tomorrow, so it never fires; it is here for the group it carries.
    /// </remarks>
    private static async Task AddTrigger(IScheduler scheduler, JobKey job, string name, string? executionGroup)
    {
        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(name, JobGroup)
            .ForJob(job)
            .WithExecutionGroup(executionGroup)
            .StartAt(TimeProvider.System.GetUtcNow().AddDays(1))
            .Build());
    }

    private static async Task<ITrigger> TheManualFiring(IScheduler scheduler, JobKey job)
    {
        List<ITrigger> triggers = await scheduler.GetTriggersOfJob(job);
        return triggers.Should().ContainSingle(
                trigger => trigger.JobDataMap.ContainsKey(SchedulerConstants.ManualTrigger),
                "one manual firing of the job was stored")
            .Subject;
    }

    /// <summary>The execution group each job's first run carried, by job.</summary>
    private static class Runs
    {
        private static readonly ConcurrentDictionary<JobKey, TaskCompletionSource<string?>> firstRuns = new();

        public static Task<string?> Of(JobKey job) => SourceFor(job).Task;

        public static bool Happened(JobKey job) => firstRuns.TryGetValue(job, out TaskCompletionSource<string?>? source) && source.Task.IsCompleted;

        public static void Record(IJobExecutionContext context) => SourceFor(context.JobDetail.Key).TrySetResult(context.Trigger.ExecutionGroup);

        public static void Reset() => firstRuns.Clear();

        private static TaskCompletionSource<string?> SourceFor(JobKey job)
        {
            return firstRuns.GetOrAdd(job, _ => new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously));
        }
    }

    [ExecutionGroup(Gpu)]
    public sealed class GpuJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Runs.Record(context);
            return default;
        }
    }

    [ExecutionGroup("tenant:{TenantId}")]
    public sealed class TenantJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed class PlainJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Runs.Record(context);
            return default;
        }
    }

    /// <summary>
    /// <c>*</c> is reserved for limits configuration, so reading this attribute throws.
    /// </summary>
    [ExecutionGroup("*")]
    public sealed class UnreadableGroupJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// Holds its execution slot until the test lets it go.
    /// </summary>
    public sealed class HoldingJob : IJob
    {
        private static TaskCompletionSource started = NewSource();
        private static TaskCompletionSource released = NewSource();

        public static Task Started => started.Task;

        public static void Release() => released.TrySetResult();

        public static void Reset()
        {
            started = NewSource();
            released = NewSource();
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await released.Task.WaitAsync(cancellationToken);
        }

        private static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
