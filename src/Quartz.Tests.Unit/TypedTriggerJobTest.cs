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

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;

namespace Quartz.Tests.Unit;

/// <summary>
/// <see cref="SchedulerJobExtensions.TriggerJob{TInput}" /> (#4138): a stored typed job fired now with its
/// input, answering with the key of the trigger that fires it, and the same firing <c>TriggerJob</c> makes.
/// </summary>
[NonParallelizable]
public sealed class TypedTriggerJobTest
{
    private const string JobGroup = "typed-manual";
    private const string Gpu = "gpu";

    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(30);

    private readonly List<ServiceProvider> containers = [];
    private readonly List<IScheduler> schedulers = [];

    [SetUp]
    public void SetUp()
    {
        Runs.Reset();
        PlainRuns.Reset();
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (IScheduler scheduler in schedulers)
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        foreach (ServiceProvider container in containers)
        {
            await container.DisposeAsync();
        }

        schedulers.Clear();
        containers.Clear();
    }

    [Test]
    public async Task TheJobReceivesItsTypedInputOnTheTriggerTheCallAnsweredWith()
    {
        (IScheduler scheduler, _) = await Build();
        JobKey job = await AddDurable<InvoiceJob>(scheduler, "invoice");
        await scheduler.Start();

        TriggerKey key = await scheduler.TriggerJob(job, new Invoice(42, "café"));

        Run run = await Runs.Of(job).WaitAsync(waitLimit);
        run.Input.Should().Be(new Invoice(42, "café"),
            "the input went under the reserved key, was written by the scheduler's serializer, and read back as the job's parameter");
        run.Trigger.Should().Be(key, "the key answered with is the trigger that fired the job");
    }

    [Test]
    public async Task TheHistoryFindsTheRunByTheReturnedKeyAndRecordsItAsManual()
    {
        (IScheduler scheduler, ServiceProvider container) = await Build();
        JobKey job = await AddDurable<InvoiceJob>(scheduler, "invoice");
        await scheduler.Start();

        TriggerKey key = await scheduler.TriggerJob(job, new Invoice(7, "reminder"));

        ExecutionHistoryEntry entry = await Eventually(container, new ExecutionHistoryQuery { SchedulerName = scheduler.SchedulerName, Trigger = key });
        entry.Manual.Should().BeTrue("the trigger carries the manual marker TriggerJob writes, which is what history reads");
        entry.JobName.Should().Be(job.Name);
        entry.Input.Should().NotBeNullOrEmpty("the input was stored as the string the serializer wrote, which history records");
    }

    /// <summary>
    /// The same trigger <c>TriggerJob</c> stores, read back side by side from a scheduler that has not started,
    /// so neither has fired: its name, group, schedule, marker, misfire instruction and execution group.
    /// </summary>
    [Test]
    public async Task TheTypedRunIsTheFiringTriggerJobStores()
    {
        (IScheduler scheduler, _) = await Build();
        JobKey job = await AddDurable<GpuInvoiceJob>(scheduler, "render");

        await scheduler.TriggerJob(job);
        TriggerKey typedKey = await scheduler.TriggerJob(job, new Invoice(1, "typed"));

        List<ITrigger> triggers = await scheduler.GetTriggersOfJob(job);
        triggers.Should().HaveCount(2);
        ISimpleTrigger typed = (ISimpleTrigger) triggers.Single(x => x.Key.Equals(typedKey));
        ISimpleTrigger plain = (ISimpleTrigger) triggers.Single(x => !x.Key.Equals(typedKey));

        typed.Key.Group.Should().Be(plain.Key.Group).And.Be(TriggerKey.DefaultGroup);
        typed.Key.Name.Should().StartWith("MT_", "a manual firing's trigger is named as TriggerJob names it");
        plain.Key.Name.Should().StartWith("MT_");
        typed.RepeatCount.Should().Be(plain.RepeatCount).And.Be(0);
        typed.MisfireInstructionCode.Should().Be(plain.MisfireInstructionCode);
        typed.ExecutionGroup.Should().Be(plain.ExecutionGroup).And.Be(Gpu,
            "the job type says [ExecutionGroup(\"gpu\")], and the typed run resolves it as TriggerJob does");
        typed.JobDataMap.GetString(SchedulerConstants.ManualTrigger).Should().Be(plain.JobDataMap.GetString(SchedulerConstants.ManualTrigger)).And.Be("true");
        typed.JobDataMap.GetString(SchedulerConstants.JobInput).Should().NotBeNullOrEmpty(
            "the input is stored as a string, so it survives every store and wire format");
    }

    [Test]
    public async Task AGroupEveryTriggerOfTheJobCarriesIsTheTypedRunsGroup()
    {
        (IScheduler scheduler, _) = await Build();
        JobKey job = await AddDurable<InvoiceJob>(scheduler, "invoice");
        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity("nightly", JobGroup)
            .ForJob(job)
            .WithExecutionGroup("reports")
            .StartAt(TimeProvider.System.GetUtcNow().AddDays(1))
            .Build());

        TriggerKey key = await scheduler.TriggerJob(job, new Invoice(3, "shared"));

        (await scheduler.GetTrigger(key))!.ExecutionGroup.Should().Be("reports",
            "the job type names no group, so the one its triggers share is the answer, read through the scheduler");
    }

    /// <summary>
    /// The extension stores its trigger through <c>ScheduleJob</c>, which gives an unstated misfire instruction
    /// the scheduler's default. <c>TriggerJob</c> never did, and under <c>DoNothing</c> a late manual run would
    /// be dropped rather than run.
    /// </summary>
    [Test]
    public async Task TheSchedulersDefaultMisfireHandlingLeavesTheTypedRunAsTriggerJobLeavesIt()
    {
        (IScheduler scheduler, _) = await Build(options => options.DefaultMisfireHandling = DefaultMisfireHandling.DoNothing);
        JobKey job = await AddDurable<InvoiceJob>(scheduler, "invoice");

        TriggerKey key = await scheduler.TriggerJob(job, new Invoice(5, "late"));

        (await scheduler.GetTrigger(key))!.MisfireInstructionCode.Should().Be((int) SimpleTriggerMisfireInstruction.SmartPolicy,
            "a one-shot trigger's smart policy fires it late; the scheduler's DoNothing would complete it unfired");
    }

    [Test]
    public async Task AJobThatIsNotStoredIsRefused()
    {
        (IScheduler scheduler, _) = await Build();

        Func<Task> act = async () => await scheduler.TriggerJob(new JobKey("nowhere", JobGroup), new Invoice(1, "lost"));

        await act.Should().ThrowAsync<JobPersistenceException>("the store refuses a trigger whose job it does not have, as for TriggerJob");
    }

    /// <summary>
    /// <c>TriggerJob(jobKey, token)</c> was a compile error before the typed extension, and with it alone would
    /// bind the token as the input. The non-generic overload makes it the fire-now it reads as.
    /// </summary>
    [Test]
    public async Task ATokenAsTheSecondArgumentFiresTheJobWithNoInput()
    {
        (IScheduler scheduler, _) = await Build();
        JobKey job = await AddDurable<PlainJob>(scheduler, "plain");
        await scheduler.Start();
        using CancellationTokenSource source = new();

        await scheduler.TriggerJob(job, source.Token);

        PlainRun run = await PlainRuns.Of(job).WaitAsync(waitLimit);
        run.Manual.Should().Be("true", "it is the firing TriggerJob makes, marked manual");
        run.HasInput.Should().BeFalse("the token is the cancellation instruction, not a payload");
    }

    [Test]
    public async Task ATokenAsTheSecondArgumentReachesTheInterfaceMemberWithNoData()
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        JobKey job = new("invoice", JobGroup);
        using CancellationTokenSource source = new();

        await scheduler.TriggerJob(job, source.Token);

        A.CallTo(() => scheduler.TriggerJob(job, null, source.Token)).MustHaveHappenedOnceExactly();
        A.CallTo(() => scheduler.ScheduleJob(A<ITrigger>._, A<ScheduleJobOptions>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// The instance member still wins for a <see cref="JobDataMap" /> and for <see langword="null" />: neither
    /// call reaches the typed extension, which would read the job and schedule a trigger of its own.
    /// </summary>
    [Test]
    public async Task AMapOrANullAsTheSecondArgumentStillBindsToTheInterfaceMember()
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        JobKey job = new("invoice", JobGroup);
        JobDataMap map = new() { ["reason"] = "rerun" };

        await scheduler.TriggerJob(job, (JobDataMap?) null);
        await scheduler.TriggerJob(job, map);

        A.CallTo(() => scheduler.TriggerJob(job, null, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => scheduler.TriggerJob(job, map, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => scheduler.ScheduleJob(A<ITrigger>._, A<ScheduleJobOptions>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// A token only reaches the typed overload when the type argument is spelled out, and it is not a payload.
    /// </summary>
    [Test]
    public async Task AnExplicitTokenInputIsRefused()
    {
        IScheduler scheduler = A.Fake<IScheduler>();

        Func<Task> act = async () => await scheduler.TriggerJob<CancellationToken>(new JobKey("invoice", JobGroup), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*not a job input*");
        A.CallTo(scheduler).MustNotHaveHappened();
    }

    [Test]
    public async Task TheTokenOverloadRefusesANullSchedulerOrJobKey()
    {
        Func<Task> noScheduler = async () => await SchedulerJobExtensions.TriggerJob(null!, new JobKey("invoice", JobGroup), CancellationToken.None);
        Func<Task> noJob = async () => await A.Fake<IScheduler>().TriggerJob(null!, CancellationToken.None);

        await noScheduler.Should().ThrowAsync<ArgumentNullException>().WithParameterName("scheduler");
        await noJob.Should().ThrowAsync<ArgumentNullException>().WithParameterName("jobKey");
    }

    [Test]
    public async Task ANullSchedulerOrJobKeyIsRefused()
    {
        Func<Task> noScheduler = async () => await SchedulerJobExtensions.TriggerJob(null!, new JobKey("invoice", JobGroup), new Invoice(1, "x"));
        Func<Task> noJob = async () => await A.Fake<IScheduler>().TriggerJob(null!, new Invoice(1, "x"));

        await noScheduler.Should().ThrowAsync<ArgumentNullException>().WithParameterName("scheduler");
        await noJob.Should().ThrowAsync<ArgumentNullException>().WithParameterName("jobKey");
    }

    [Test]
    public async Task ATakenNameIsRetriedUnderANewOne()
    {
        IScheduler scheduler = FakeSchedulerWithNoGroup();
        List<TriggerKey> attempted = [];
        A.CallTo(() => scheduler.ScheduleJob(A<ITrigger>._, A<ScheduleJobOptions>._, A<CancellationToken>._))
            .ReturnsLazily((ITrigger trigger, ScheduleJobOptions _, CancellationToken _) =>
            {
                attempted.Add(trigger.Key);
                if (attempted.Count == 1)
                {
                    throw new ObjectAlreadyExistsException("taken");
                }

                return new ValueTask<DateTimeOffset>(DateTimeOffset.UtcNow);
            });

        TriggerKey key = await scheduler.TriggerJob(new JobKey("invoice", JobGroup), new Invoice(1, "x"));

        attempted.Should().HaveCount(2);
        attempted[0].Should().NotBe(attempted[1], "the second attempt is under a new name");
        key.Should().Be(attempted[1], "the key answered with is the one that was stored");
    }

    [Test]
    public async Task ANameTakenThreeTimesRunningIsThrown()
    {
        IScheduler scheduler = FakeSchedulerWithNoGroup();
        A.CallTo(() => scheduler.ScheduleJob(A<ITrigger>._, A<ScheduleJobOptions>._, A<CancellationToken>._))
            .Throws(new ObjectAlreadyExistsException("taken"));

        Func<Task> act = async () => await scheduler.TriggerJob(new JobKey("invoice", JobGroup), new Invoice(1, "x"));

        await act.Should().ThrowAsync<ObjectAlreadyExistsException>(
            "a scheduler that refuses every name has something wrong with it, and looping on it would hang the caller");
        A.CallTo(() => scheduler.ScheduleJob(A<ITrigger>._, A<ScheduleJobOptions>._, A<CancellationToken>._))
            .MustHaveHappened(3, Times.Exactly);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Helpers
    //////////////////////////////////////////////////////////////////////////////////////////////

    private async Task<(IScheduler Scheduler, ServiceProvider Container)> Build(Action<QuartzSchedulerOptions>? configure = null)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartzExecutionHistory(options => options.RecordInput = true);
        services.AddQuartz(q => q.ConfigureScheduler(options =>
        {
            options.InstanceName = "typed-manual-" + Guid.NewGuid().ToString("N");
            options.IdleWaitTime = TimeSpan.FromSeconds(1);
            configure?.Invoke(options);
        }));

        ServiceProvider container = services.BuildServiceProvider();
        containers.Add(container);

        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        schedulers.Add(scheduler);
        return (scheduler, container);
    }

    private static async Task<JobKey> AddDurable<TJob>(IScheduler scheduler, string name) where TJob : IJob
    {
        JobKey key = new(name, JobGroup);
        await scheduler.AddJob(JobBuilder.Create<TJob>().WithIdentity(key).StoreDurably().Build());
        return key;
    }

    private static IScheduler FakeSchedulerWithNoGroup()
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.TimeProvider).Returns(TimeProvider.System);
        A.CallTo(() => scheduler.GetJobDetail(A<JobKey>._, A<CancellationToken>._)).Returns((IJobDetail?) null);
        A.CallTo(() => scheduler.QueryTriggers(A<TriggerQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeader>([], HasMore: false));
        return scheduler;
    }

    private static async Task<ExecutionHistoryEntry> Eventually(ServiceProvider container, ExecutionHistoryQuery query)
    {
        IExecutionHistoryStore store = container.GetRequiredService<IExecutionHistoryStore>();
        DateTimeOffset deadline = DateTimeOffset.UtcNow + waitLimit;

        while (true)
        {
            PagedResult<ExecutionHistoryEntry> page = await store.QueryExecutions(query);
            if (page.Items.Count > 0)
            {
                return page.Items.Should().ContainSingle("one run was fired under the key").Subject;
            }

            DateTimeOffset.UtcNow.Should().BeBefore(deadline, "the recorder writes as the firing completes");
            await Task.Delay(20);
        }
    }

    public sealed record Invoice(int InvoiceId, string Note);

    private sealed record Run(Invoice Input, TriggerKey Trigger);

    /// <summary>What each job's first run was handed, by job.</summary>
    private static class Runs
    {
        private static readonly ConcurrentDictionary<JobKey, TaskCompletionSource<Run>> firstRuns = new();

        public static Task<Run> Of(JobKey job) => SourceFor(job).Task;

        public static void Record(IJobExecutionContext context, Invoice input) => SourceFor(context.JobDetail.Key).TrySetResult(new Run(input, context.Trigger.Key));

        public static void Reset() => firstRuns.Clear();

        private static TaskCompletionSource<Run> SourceFor(JobKey job)
        {
            return firstRuns.GetOrAdd(job, _ => new TaskCompletionSource<Run>(TaskCreationOptions.RunContinuationsAsynchronously));
        }
    }

    private sealed record PlainRun(string? Manual, bool HasInput);

    /// <summary>What each plain job's first run carried, by job.</summary>
    private static class PlainRuns
    {
        private static readonly ConcurrentDictionary<JobKey, TaskCompletionSource<PlainRun>> firstRuns = new();

        public static Task<PlainRun> Of(JobKey job) => SourceFor(job).Task;

        public static void Record(IJobExecutionContext context) => SourceFor(context.JobDetail.Key).TrySetResult(new PlainRun(
            context.MergedJobDataMap.GetString(SchedulerConstants.ManualTrigger),
            context.MergedJobDataMap.ContainsKey(SchedulerConstants.JobInput)));

        public static void Reset() => firstRuns.Clear();

        private static TaskCompletionSource<PlainRun> SourceFor(JobKey job)
        {
            return firstRuns.GetOrAdd(job, _ => new TaskCompletionSource<PlainRun>(TaskCreationOptions.RunContinuationsAsynchronously));
        }
    }

    public sealed class PlainJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            PlainRuns.Record(context);
            return default;
        }
    }

    public sealed class InvoiceJob : IJob<Invoice>
    {
        public ValueTask Execute(IJobExecutionContext context, Invoice input, CancellationToken cancellationToken = default)
        {
            Runs.Record(context, input);
            return default;
        }
    }

    [ExecutionGroup(Gpu)]
    public sealed class GpuInvoiceJob : IJob<Invoice>
    {
        public ValueTask Execute(IJobExecutionContext context, Invoice input, CancellationToken cancellationToken = default)
        {
            Runs.Record(context, input);
            return default;
        }
    }
}
