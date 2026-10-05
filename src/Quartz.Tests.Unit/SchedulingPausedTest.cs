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

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit;

/// <summary>
/// <see cref="ScheduleJobOptions.Paused" />: a trigger stored paused by the call that stores it, through
/// every <see cref="IScheduler" /> member that takes the options, and the fallback for a store that cannot
/// do that (#4018).
/// </summary>
/// <remarks>
/// The store under the scheduler is wrapped by one that asks for the next triggers straight after each
/// store call, as the scheduler thread could: a trigger stored waiting is acquired by that probe, and one
/// stored paused is not. That makes the window the issue is about a deterministic assertion rather than a
/// race to lose.
/// </remarks>
[NonParallelizable]
public sealed class SchedulingPausedTest
{
    private const string Group = "approvals";

    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(30);

    private static readonly ScheduleJobOptions awaitingApproval = new() { PauseReason = "awaiting approval", PauseRequestedBy = "alice" };

    private ProbingJobStore store = null!;
    private EventLog events = null!;
    private IScheduler scheduler = null!;
    private SqliteTestDatabase? database;

    public enum Route
    {
        JobAndTrigger,
        TriggerAlone,
        JobAndTriggers,
        Batch
    }

    public enum Store
    {
        InMemory,
        Sqlite
    }

    [SetUp]
    public void SetUp()
    {
        CountingJob.Reset();
        events = new EventLog();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (scheduler is not null)
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
            scheduler = null!;
        }

        database?.Dispose();
        database = null;
    }

    [TestCase(Route.JobAndTrigger)]
    [TestCase(Route.TriggerAlone)]
    [TestCase(Route.JobAndTriggers)]
    [TestCase(Route.Batch)]
    public async Task ATriggerScheduledPausedIsNeverAcquirableAndCarriesItsReason(Route route)
    {
        await BuildScheduler(storesPaused: true);

        TriggerKey key = await Schedule(route, "nightly", awaitingApproval);

        store.AcquiredRightAfterStoring.Should().BeEmpty(
            "the store wrote the trigger paused in the call that stored it, so there was never a moment in which "
            + "the scheduler thread could acquire it");
        store.PausedOptionsSeen.Should().Equal([true], "a store that can store paused is handed the option");
        store.PauseCalls.Should().Be(0, "nothing was left for the scheduler to pause afterwards");

        (await scheduler.GetTriggerState(key)).Should().Be(TriggerState.Paused);
        PauseInfo? pause = await scheduler.GetTriggerPause(key);
        pause.Should().NotBeNull();
        pause!.Reason.Should().Be("awaiting approval");
        pause.RequestedBy.Should().Be("alice");

        TriggerHeader header = (await scheduler.QueryTriggers(new TriggerQuery { Group = GroupMatcher<TriggerKey>.GroupEquals(Group) }))
            .Items.Single(x => x.Key.Equals(key));
        header.Pause.Should().Be(pause, "the listing carries the record the trigger was stored with");

        events.Heard.Should().Equal([$"scheduled {key}", $"paused {key}"],
            "a listener hears the trigger scheduled and then paused, whichever way the store made it so");
    }

    [Test]
    public async Task AReasonlessPausedScheduleRecordsNothing()
    {
        await BuildScheduler(storesPaused: true);

        TriggerKey key = await Schedule(Route.JobAndTrigger, "nightly", new ScheduleJobOptions { Paused = true });

        store.AcquiredRightAfterStoring.Should().BeEmpty();
        (await scheduler.GetTriggerState(key)).Should().Be(TriggerState.Paused);
        (await scheduler.GetTriggerPause(key)).Should().BeNull("a pause that says nothing records nothing, as PauseTrigger records nothing");
    }

    [Test]
    public async Task ReplacingATriggerWithPausedStoresTheNewOnePaused()
    {
        await BuildScheduler(storesPaused: true);
        TriggerKey key = await Schedule(Route.JobAndTrigger, "nightly", default);
        (await scheduler.GetTriggerState(key)).Should().Be(TriggerState.Normal);

        ITrigger replacement = Trigger("nightly", new JobKey("job-nightly", Group));
        await scheduler.ScheduleJob(replacement, ScheduleJobOptions.Replacing with { PauseReason = "re-planned" });

        (await scheduler.GetTriggerState(key)).Should().Be(TriggerState.Paused, "the replacement is stored paused, as asked");
        (await scheduler.GetTriggerPause(key))!.Reason.Should().Be("re-planned");
    }

    [Test]
    public async Task AContinuationCannotBeScheduledPaused()
    {
        await BuildScheduler(storesPaused: true);
        TriggerKey parent = await Schedule(Route.JobAndTrigger, "parent", default);

        ITrigger continuation = TriggerBuilder.Create()
            .WithIdentity("child", Group)
            .ForJob(new JobKey("job-parent", Group))
            .StartAfter(parent)
            .Build();

        Func<Task> act = async () => await scheduler.ScheduleJob(continuation, awaitingApproval);

        await act.Should().ThrowAsync<SchedulerException>().WithMessage("*continuation*cannot be stored paused*",
            "a continuation is released by its parent's firing whatever a pause said, so storing it paused would be a promise "
            + "the store cannot keep");
        (await scheduler.Exists(new TriggerKey("child", Group))).Should().BeFalse("nothing was stored");
    }

    [TestCase(Route.JobAndTrigger)]
    [TestCase(Route.TriggerAlone)]
    public async Task AStoreThatCannotStorePausedIsPausedStraightAfterwards(Route route)
    {
        await BuildScheduler(storesPaused: false);

        TriggerKey key = await Schedule(route, "nightly", awaitingApproval);

        store.PausedOptionsSeen.Should().OnlyContain(paused => !paused,
            "a store that does not say it can store paused is handed only what it understood before 4.4");
        store.AcquiredRightAfterStoring.Should().Equal([key],
            "this is the window the fallback leaves, and why it is documented: between the store call and the pause, "
            + "the trigger is waiting and can be acquired");
        store.PauseTriggerWithCalls.Should().Be(1, "the scheduler pauses what the store stored, with the reason");

        (await scheduler.GetTriggerState(key)).Should().Be(TriggerState.Paused);
        (await scheduler.GetTriggerPause(key))!.Reason.Should().Be("awaiting approval");
        events.Heard.Should().Equal([$"scheduled {key}", $"paused {key}"]);
    }

    [Test]
    public async Task TheFallbackPausesABatchInOneCallAndAReasonlessOneWithoutDetails()
    {
        await BuildScheduler(storesPaused: false);

        IJobDetail first = Job("first");
        IJobDetail second = Job("second");
        Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> batch = new()
        {
            [first] = [Trigger("first", first.Key)],
            [second] = [Trigger("second", second.Key)]
        };

        await scheduler.ScheduleJobs(batch, awaitingApproval);
        store.PauseTriggersWithCalls.Should().Be(1, "a batch is paused as a set, in one call");

        await scheduler.ScheduleJob(Job("third"), Trigger("third", new JobKey("job-third", Group)), new ScheduleJobOptions { Paused = true });
        store.PauseTriggerCalls.Should().Be(1, "a pause that says nothing is the reasonless member, as PauseTriggerWith makes it");

        (await scheduler.GetTriggerState(new TriggerKey("first", Group))).Should().Be(TriggerState.Paused);
        (await scheduler.GetTriggerState(new TriggerKey("second", Group))).Should().Be(TriggerState.Paused);
        (await scheduler.GetTriggerState(new TriggerKey("third", Group))).Should().Be(TriggerState.Paused);
        (await scheduler.GetTriggerPause(new TriggerKey("third", Group))).Should().BeNull();
    }

    [Test]
    public async Task ScheduleJobsStoresEveryTriggerPausedWithOneRecord()
    {
        await BuildScheduler(storesPaused: true);

        IJobDetail job = Job("reports");
        await scheduler.ScheduleJob(job, [Trigger("morning", job.Key), Trigger("evening", job.Key)], awaitingApproval);

        store.AcquiredRightAfterStoring.Should().BeEmpty();
        PauseInfo? morning = await scheduler.GetTriggerPause(new TriggerKey("morning", Group));
        PauseInfo? evening = await scheduler.GetTriggerPause(new TriggerKey("evening", Group));
        morning.Should().NotBeNull();
        evening.Should().Be(morning, "the triggers of one call are paused by one pause, stamped with one instant");
    }

    [Test]
    public async Task WithoutTheOptionNothingIsPaused()
    {
        await BuildScheduler(storesPaused: true);

        TriggerKey key = await Schedule(Route.JobAndTrigger, "nightly", default);

        store.AcquiredRightAfterStoring.Should().Equal([key], "the probe does see a trigger stored waiting, which is what makes its silence above mean something");
        (await scheduler.GetTriggerState(key)).Should().Be(TriggerState.Normal);
        events.Heard.Should().Equal([$"scheduled {key}"]);
    }

    [TestCase(Store.InMemory)]
    [TestCase(Store.Sqlite)]
    public async Task AStartedSchedulerWithAFastIdleWaitNeverFiresATriggerScheduledPaused(Store kind)
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "scheduling-paused-" + Guid.NewGuid().ToString("N");
                options.InstanceId = "one";
                options.IdleWaitTime = TimeSpan.FromSeconds(1);
            });

            if (kind == Store.Sqlite)
            {
                database = new SqliteTestDatabase("scheduling-paused");
                string connectionString = database.ConnectionString;
                q.UsePersistentStore(persistent =>
                {
                    persistent.UseSqlite(SqliteFactory.Instance, connectionString);
                    persistent.ProvisionSchema();
                });
            }
            else
            {
                q.UseInMemoryStore();
            }
        });

        await using ServiceProvider container = services.BuildServiceProvider();
        scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();

        IJobDetail job = JobBuilder.Create<CountingJob>().WithIdentity("counting", Group).Build();
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("every-20ms", Group)
            .ForJob(job)
            .StartNow()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMilliseconds(20)).RepeatForever())
            .Build();

        await scheduler.ScheduleJob(job, trigger, awaitingApproval);

        // The shortest idle wait there is, and more than seventy of the trigger's own intervals: a trigger
        // stored waiting even for an instant would be acquired and run in that time, since storing it
        // wakes the scheduler thread.
        await Task.Delay(TimeSpan.FromMilliseconds(1500));

        CountingJob.Executions.Should().Be(0, "a trigger stored paused never fires, however soon it was due");
        (await scheduler.GetTriggerPause(trigger.Key))!.Reason.Should().Be("awaiting approval");

        await scheduler.ResumeTrigger(trigger.Key);
        await CountingJob.FirstExecution.Task.WaitAsync(waitLimit);

        await scheduler.Shutdown(waitForJobsToComplete: true);
        scheduler = null!;
    }

    [Test]
    public void TheDefaultSaysAStoreCannotStorePaused()
    {
        IJobStore thirdParty = A.Fake<IJobStore>();
        A.CallTo(() => thirdParty.SupportsStoringPaused).CallsBaseMethod();

        thirdParty.SupportsStoringPaused.Should().BeFalse(
            "a store written against an earlier 4.x ignores the option, so the scheduler must not take its word for it");
    }

    [Test]
    public void TheShippedStoresSayTheyCanStorePaused()
    {
        TestJobStores.Ram().SupportsStoringPaused.Should().BeTrue();
        TestJobStores.Tx().SupportsStoringPaused.Should().BeTrue();
        new DelegatingJobStore(TestJobStores.Ram()).SupportsStoringPaused.Should().BeTrue("a decorator says what the store behind it says");
    }

    [Test]
    public void EitherTextAsksForAPause()
    {
        default(ScheduleJobOptions).Paused.Should().BeFalse("the default stores the triggers as usual");
        new ScheduleJobOptions { PauseReason = "why" }.Paused.Should().BeTrue("a reason is only ever the reason for a pause");
        new ScheduleJobOptions { PauseRequestedBy = "who" }.Paused.Should().BeTrue();
        new ScheduleJobOptions { PauseReason = " " }.Paused.Should().BeFalse("blank says nothing");

        default(AddTriggerOptions).Paused.Should().BeFalse();
        new AddTriggerOptions { PauseReason = "why" }.Paused.Should().BeTrue();
        new AddTriggerOptions { PauseRequestedBy = "who" }.Paused.Should().BeTrue();
    }

    private async Task BuildScheduler(bool storesPaused)
    {
        scheduler = await QuartzSchedulerBuilder
            .Create(q => q.UseJobStore(provider =>
            {
                store = new ProbingJobStore(ActivatorUtilities.CreateInstance<RAMJobStore>(provider), storesPaused);
                return store;
            }))
            .BuildScheduler();

        scheduler.ListenerManager.AddSchedulerListener(events);
    }

    private async Task<TriggerKey> Schedule(Route route, string name, ScheduleJobOptions options)
    {
        IJobDetail job = Job(name);
        ITrigger trigger = Trigger(name, job.Key);

        switch (route)
        {
            case Route.JobAndTrigger:
                await scheduler.ScheduleJob(job, trigger, options);
                break;
            case Route.TriggerAlone:
                await scheduler.AddJob(job, new AddJobOptions { StoreNonDurableWhileAwaitingScheduling = true });
                store.Forget();
                await scheduler.ScheduleJob(trigger, options);
                break;
            case Route.JobAndTriggers:
                await scheduler.ScheduleJob(job, [trigger], options);
                break;
            case Route.Batch:
                await scheduler.ScheduleJobs(new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> { [job] = [trigger] }, options);
                break;
        }

        return trigger.Key;
    }

    private static IJobDetail Job(string name) => JobBuilder.Create<CountingJob>().WithIdentity("job-" + name, Group).Build();

    private static ITrigger Trigger(string name, JobKey job) => TriggerBuilder.Create()
        .WithIdentity(name, Group)
        .ForJob(job)
        .StartNow()
        .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
        .Build();

    /// <summary>
    /// A store over the in-memory one that acquires straight after every store call, as the scheduler
    /// thread could, and releases what it got; and that answers <see cref="IJobStore.SupportsStoringPaused" />
    /// as the test asks, standing in for a third-party store when it answers no.
    /// </summary>
    private sealed class ProbingJobStore(IJobStore inner, bool storesPaused) : DelegatingJobStore(inner)
    {
        public List<TriggerKey> AcquiredRightAfterStoring { get; } = [];

        public List<bool> PausedOptionsSeen { get; } = [];

        public int PauseTriggerCalls { get; private set; }

        public int PauseTriggerWithCalls { get; private set; }

        public int PauseTriggersWithCalls { get; private set; }

        public int PauseCalls => PauseTriggerCalls + PauseTriggerWithCalls + PauseTriggersWithCalls;

        public override bool SupportsStoringPaused => storesPaused && base.SupportsStoringPaused;

        public void Forget()
        {
            AcquiredRightAfterStoring.Clear();
            PausedOptionsSeen.Clear();
        }

        public override async ValueTask ScheduleJob(IJobDetail job, IOperableTrigger trigger, CancellationToken cancellationToken = default)
        {
            PausedOptionsSeen.Add(false);
            await base.ScheduleJob(job, trigger, cancellationToken);
            await Probe(cancellationToken);
        }

        public override async ValueTask ScheduleJobs(
            IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<IOperableTrigger>> triggersAndJobs,
            ScheduleJobOptions options = default,
            CancellationToken cancellationToken = default)
        {
            PausedOptionsSeen.Add(options.Paused);
            await base.ScheduleJobs(triggersAndJobs, options, cancellationToken);
            await Probe(cancellationToken);
        }

        public override async ValueTask AddTrigger(IOperableTrigger trigger, AddTriggerOptions options = default, CancellationToken cancellationToken = default)
        {
            PausedOptionsSeen.Add(options.Paused);
            await base.AddTrigger(trigger, options, cancellationToken);
            await Probe(cancellationToken);
        }

        public override ValueTask<bool> PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            PauseTriggerCalls++;
            return base.PauseTrigger(triggerKey, cancellationToken);
        }

        public override ValueTask<bool> PauseTriggerWith(TriggerKey triggerKey, PauseDetails? details, CancellationToken cancellationToken = default)
        {
            PauseTriggerWithCalls++;
            return base.PauseTriggerWith(triggerKey, details, cancellationToken);
        }

        public override ValueTask<List<TriggerKey>> PauseTriggersWith(IReadOnlyCollection<TriggerKey> triggerKeys, PauseDetails? details, CancellationToken cancellationToken = default)
        {
            PauseTriggersWithCalls++;
            return base.PauseTriggersWith(triggerKeys, details, cancellationToken);
        }

        private async ValueTask Probe(CancellationToken cancellationToken)
        {
            List<IOperableTrigger> acquired = await AcquireNextTriggers(
                new TriggerAcquisitionRequest { NoLaterThan = DateTimeOffset.UtcNow.AddMinutes(1), MaxCount = 100, TimeWindow = TimeSpan.Zero },
                cancellationToken);

            foreach (IOperableTrigger trigger in acquired)
            {
                AcquiredRightAfterStoring.Add(trigger.Key);
                await ReleaseAcquiredTrigger(trigger, cancellationToken);
            }
        }
    }

    private sealed class EventLog : ISchedulerListener
    {
        private readonly Lock gate = new();
        private readonly List<string> heard = [];

        public List<string> Heard
        {
            get
            {
                lock (gate)
                {
                    return [.. heard];
                }
            }
        }

        public ValueTask JobScheduled(IScheduler scheduler, ITrigger trigger, CancellationToken cancellationToken = default)
        {
            return Record($"scheduled {trigger.Key}");
        }

        public ValueTask TriggerPaused(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            return Record($"paused {triggerKey}");
        }

        private ValueTask Record(string what)
        {
            lock (gate)
            {
                heard.Add(what);
            }

            return default;
        }
    }

    public sealed class CountingJob : IJob
    {
        private static int executions;

        public static int Executions => Volatile.Read(ref executions);

        public static TaskCompletionSource FirstExecution { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static void Reset()
        {
            Volatile.Write(ref executions, 0);
            FirstExecution = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref executions);
            FirstExecution.TrySetResult();
            return default;
        }
    }
}
