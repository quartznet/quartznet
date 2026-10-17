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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading.Tasks;

using Quartz.Core;
using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// A manual firing counts against the one execution group every trigger of its job carries (#4092).
/// </summary>
/// <remarks>
/// <c>TriggerJob</c> built its one-shot trigger with no group, so a manual firing of a <c>gpu</c> job ran
/// beside the firing holding the only <c>gpu</c> slot. 3.x has no <c>[ExecutionGroup]</c> attribute and no
/// routing, so the group the job's triggers share is the only answer it has.
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

    private readonly List<IScheduler> schedulers = new List<IScheduler>();

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
    }

    [Test]
    public async Task AManualFiringTakesTheGroupOnlyWhenEveryTriggerOfTheJobCarriesIt()
    {
        IScheduler scheduler = await Build();
        JobKey agreeing = await AddDurable(scheduler, "agreeing");
        await AddTrigger(scheduler, agreeing, "agreeing-1", Reports);
        await AddTrigger(scheduler, agreeing, "agreeing-2", Reports);
        JobKey mixed = await AddDurable(scheduler, "mixed");
        await AddTrigger(scheduler, mixed, "mixed-1", Reports);
        await AddTrigger(scheduler, mixed, "mixed-2", "batch");
        JobKey partly = await AddDurable(scheduler, "partly");
        await AddTrigger(scheduler, partly, "partly-1", Reports);
        await AddTrigger(scheduler, partly, "partly-2", executionGroup: null);
        JobKey untriggered = await AddDurable(scheduler, "untriggered");

        foreach (JobKey job in new[] { agreeing, mixed, partly, untriggered })
        {
            await scheduler.TriggerJob(job);
        }

        (await GroupOfTheManualFiring(scheduler, agreeing)).Should().Be(Reports,
            "every trigger of the job carries reports, so that is the job's group");
        (await GroupOfTheManualFiring(scheduler, mixed)).Should().BeNull("two groups is no one group");
        (await GroupOfTheManualFiring(scheduler, partly)).Should().BeNull("a grouped and an ungrouped trigger is no one group");
        (await GroupOfTheManualFiring(scheduler, untriggered)).Should().BeNull("no trigger says anything");
    }

    /// <summary>
    /// What carrying the group is for: with <c>gpu = 1</c> and a <c>gpu</c> firing holding the slot, the
    /// manual firing of a job whose triggers are all <c>gpu</c> waits, and runs once the slot is given back.
    /// </summary>
    [Test]
    public async Task AManualFiringWaitsForASlotInTheGroupItsJobsTriggersShare()
    {
        IScheduler scheduler = await Build(new NameValueCollection
        {
            ["quartz.executionLimit.gpu"] = "1"
        });

        await scheduler.ScheduleJob(
            JobBuilder.Create<HoldingJob>().WithIdentity("holder", JobGroup).Build(),
            TriggerBuilder.Create().WithIdentity("holder", JobGroup).WithExecutionGroup(Gpu).StartNow().Build());
        JobKey job = await AddDurable(scheduler, "render");
        await AddTrigger(scheduler, job, "nightly", Gpu);

        await scheduler.Start();
        (await Finishes(HoldingJob.Started)).Should().BeTrue("the premise: the holder runs and takes the only gpu slot");

        await scheduler.TriggerJob(job);
        await Task.Delay(settle);

        Runs.Happened(job).Should().BeFalse("gpu = 1, the holder has the slot, and the manual firing is a gpu firing");

        HoldingJob.Release();

        (await Finishes(Runs.Of(job))).Should().BeTrue("the slot was given back, and the manual firing took it");
        (await Runs.Of(job)).Should().Be(Gpu);
    }

    /// <summary>
    /// The overload that takes a built trigger: a group the caller named is kept, and a trigger without one
    /// is given its job's.
    /// </summary>
    [Test]
    public async Task AnOperableTriggerKeepsItsOwnGroupAndIsGivenItsJobsWhenItHasNone()
    {
        IScheduler scheduler = await Build();
        JobKey job = await AddDurable(scheduler, "render");
        await AddTrigger(scheduler, job, "nightly", Gpu);
        QuartzScheduler core = ((StdScheduler) scheduler).sched;

        IOperableTrigger named = (IOperableTrigger) TriggerBuilder.Create().WithIdentity("named", JobGroup).ForJob(job).WithExecutionGroup("batch").StartNow().Build();
        IOperableTrigger unnamed = (IOperableTrigger) TriggerBuilder.Create().WithIdentity("unnamed", JobGroup).ForJob(job).StartNow().Build();

        // The unnamed one first: once the named one is stored, the job's triggers no longer share a group.
        await core.TriggerJob(unnamed);
        await core.TriggerJob(named);

        ((AbstractTrigger) await scheduler.GetTrigger(named.Key)).ExecutionGroup.Should().Be("batch",
            "the caller named a group, and that is the firing's");
        ((AbstractTrigger) await scheduler.GetTrigger(unnamed.Key)).ExecutionGroup.Should().Be(Gpu,
            "no group was named, so the one the job's triggers share is taken");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Helpers
    //////////////////////////////////////////////////////////////////////////////////////////////

    private async Task<IScheduler> Build(NameValueCollection extra = null)
    {
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.scheduler.instanceName"] = "manual-firing-" + Guid.NewGuid().ToString("N"),
            ["quartz.scheduler.idleWaitTime"] = "1000"
        };

        if (extra != null)
        {
            properties.Add(extra);
        }

        IScheduler scheduler = await new StdSchedulerFactory(properties).GetScheduler();
        schedulers.Add(scheduler);
        return scheduler;
    }

    private static async Task<JobKey> AddDurable(IScheduler scheduler, string name)
    {
        JobKey key = new JobKey(name, JobGroup);
        await scheduler.AddJob(JobBuilder.Create<RecordingJob>().WithIdentity(key).StoreDurably().Build(), replace: false);
        return key;
    }

    /// <remarks>
    /// Due tomorrow, so it never fires; it is here for the group it carries.
    /// </remarks>
    private static async Task AddTrigger(IScheduler scheduler, JobKey job, string name, string executionGroup)
    {
        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(name, JobGroup)
            .ForJob(job)
            .WithExecutionGroup(executionGroup)
            .StartAt(DateTimeOffset.UtcNow.AddDays(1))
            .Build());
    }

    /// <summary>
    /// The execution group of the one manual firing stored for the job, whose trigger name starts with
    /// <c>MT_</c>.
    /// </summary>
    private static async Task<string> GroupOfTheManualFiring(IScheduler scheduler, JobKey job)
    {
        IReadOnlyCollection<ITrigger> triggers = await scheduler.GetTriggersOfJob(job);
        ITrigger manual = triggers.Should().ContainSingle(
                trigger => trigger.Key.Name.StartsWith("MT_", StringComparison.Ordinal),
                "one manual firing of the job was stored")
            .Subject;

        return ((AbstractTrigger) manual).ExecutionGroup;
    }

    private static async Task<bool> Finishes(Task task)
    {
        return await Task.WhenAny(task, Task.Delay(waitLimit)) == task;
    }

    /// <summary>The execution group each job's first run carried, by job.</summary>
    private static class Runs
    {
        private static readonly ConcurrentDictionary<JobKey, TaskCompletionSource<string>> firstRuns = new ConcurrentDictionary<JobKey, TaskCompletionSource<string>>();

        public static Task<string> Of(JobKey job) => SourceFor(job).Task;

        public static bool Happened(JobKey job) => firstRuns.TryGetValue(job, out TaskCompletionSource<string> source) && source.Task.IsCompleted;

        public static void Record(IJobExecutionContext context) => SourceFor(context.JobDetail.Key).TrySetResult(((AbstractTrigger) context.Trigger).ExecutionGroup);

        public static void Reset() => firstRuns.Clear();

        private static TaskCompletionSource<string> SourceFor(JobKey job)
        {
            return firstRuns.GetOrAdd(job, _ => new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously));
        }
    }

    public sealed class RecordingJob : IJob
    {
        public Task Execute(IJobExecutionContext context)
        {
            Runs.Record(context);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Holds its execution slot until the test lets it go.
    /// </summary>
    public sealed class HoldingJob : IJob
    {
        private static TaskCompletionSource<bool> started = NewSource();
        private static TaskCompletionSource<bool> released = NewSource();

        public static Task Started => started.Task;

        public static void Release() => released.TrySetResult(true);

        public static void Reset()
        {
            started = NewSource();
            released = NewSource();
        }

        public async Task Execute(IJobExecutionContext context)
        {
            started.TrySetResult(true);
            await released.Task.ConfigureAwait(false);
        }

        private static TaskCompletionSource<bool> NewSource() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
