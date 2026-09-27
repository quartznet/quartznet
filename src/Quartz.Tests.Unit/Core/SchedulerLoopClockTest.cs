using System.Collections.Concurrent;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

using Quartz.Core;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// The scheduling loop waits on the scheduler's clock (#3869): advancing a <see cref="FakeTimeProvider" />
/// wakes it, a scheduling signal still wakes it without the clock moving, and shutting it down does not
/// depend on anybody advancing the clock. A signal the loop's next look answers anyway does not wake it
/// at all (#3865).
/// </summary>
/// <remarks>
/// <para>
/// Every wait for the scheduler has a real-time deadline, so a loop waiting on the wrong clock fails
/// with a message rather than hanging the run. Each idle wait configured here is longer than that
/// deadline, so a loop sleeping on the wall clock cannot pass by waking up on its own.
/// </para>
/// <para>
/// Where a test has to know the loop has parked before it moves the clock, it waits for the loop's timer
/// to be armed on the clock rather than for time to pass; see <see cref="ArmingRecordingTimeProvider" />.
/// </para>
/// </remarks>
public sealed class SchedulerLoopClockTest
{
    public enum StoreKind
    {
        InMemory,
        Sqlite,
    }

    public enum Parked
    {
        Idle,
        BeforeAFiring,
        InStandby,
    }

    private static readonly DateTimeOffset start = new(2026, 3, 6, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// How long a test waits for the scheduler before calling it stuck. Never a measurement.
    /// </summary>
    private static readonly TimeSpan deadline = TimeSpan.FromSeconds(30);

    /// <summary>How long a paused loop sleeps before it looks at its pause flag again.</summary>
    private static readonly TimeSpan pauseCheck = TimeSpan.FromSeconds(1);

    [TestCase(StoreKind.InMemory)]
    [TestCase(StoreKind.Sqlite)]
    public async Task ATriggerAnHourOutFiresWhenTheClockIsAdvancedAnHour(StoreKind store)
    {
        ArmingRecordingTimeProvider clock = new(new FakeTimeProvider(start));
        TimeSpan idleWaitTime = TimeSpan.FromMinutes(5);
        using SqliteTestDatabase database = new("loop-clock-hour");
        await using StandaloneSchedulerFactory factory = Build(clock, store, database, idleWaitTime);
        IScheduler scheduler = await factory.GetScheduler();
        FiredListener fired = new();
        scheduler.ListenerManager.AddJobListener(fired);

        await scheduler.Start();
        await ShouldArrive(clock.Armed(IdleWait(idleWaitTime)), "a scheduler with nothing to do parks in its idle wait");

        await scheduler.ScheduleJob(Job("hourly"), Trigger(clock, "hourly", start.AddHours(1)));
        clock.Clock.Advance(TimeSpan.FromHours(1));

        IJobExecutionContext context = await ShouldArrive(fired.Fired,
            "the trigger came due on the scheduler's clock, and the loop's idle wait is a timer on that clock");
        context.ScheduledFireTimeUtc.Should().Be(start.AddHours(1), "the firing is the one the trigger was scheduled for");
    }

    /// <summary>
    /// The pre-fire wait: an idle wait of an hour puts a trigger half an hour out inside the loop's
    /// acquisition window, so the loop holds the trigger and waits for its fire time.
    /// </summary>
    [TestCase(StoreKind.InMemory)]
    [TestCase(StoreKind.Sqlite)]
    public async Task ATriggerTheLoopHoldsFiresWhenTheClockReachesIt(StoreKind store)
    {
        ArmingRecordingTimeProvider clock = new(new FakeTimeProvider(start));
        using SqliteTestDatabase database = new("loop-clock-prefire");
        await using StandaloneSchedulerFactory factory = Build(clock, store, database, idleWaitTime: TimeSpan.FromHours(1));
        IScheduler scheduler = await factory.GetScheduler();
        FiredListener fired = new();
        scheduler.ListenerManager.AddJobListener(fired);

        await scheduler.Start();
        await scheduler.ScheduleJob(Job("half-hour"), Trigger(clock, "half-hour", start.AddMinutes(30)));

        await ShouldArrive(clock.Armed(arming => arming.DueTime == TimeSpan.FromMinutes(30)),
            "the loop acquires the trigger and waits for its fire time on the scheduler's clock");
        clock.Clock.Advance(TimeSpan.FromMinutes(30));

        IJobExecutionContext context = await ShouldArrive(fired.Fired,
            "the clock reached the fire time of the trigger the loop was holding");
        context.ScheduledFireTimeUtc.Should().Be(start.AddMinutes(30));
    }

    [Test]
    public async Task AStandbySchedulerLooksAtItsPauseFlagOnItsOwnClock()
    {
        ArmingRecordingTimeProvider clock = new(new FakeTimeProvider(start));
        using SqliteTestDatabase database = new("loop-clock-standby");
        await using StandaloneSchedulerFactory factory = Build(clock, StoreKind.InMemory, database, idleWaitTime: TimeSpan.FromMinutes(5));
        IScheduler scheduler = await factory.GetScheduler();

        await scheduler.Start();
        await scheduler.Standby();

        await ShouldArrive(clock.Armed(PauseCheck),
            "a paused loop sleeps between looks at its pause flag, and the sleep is a timer on the scheduler's clock");
        int looks = clock.CountArmed(PauseCheck);

        clock.Clock.Advance(pauseCheck);

        await ShouldArrive(clock.Armed(PauseCheck, looks + 1),
            "advancing the clock by the check interval ends the sleep: the loop looks again, finds itself still paused and sleeps again");
    }

    [Test]
    public async Task ASignalForAnEarlierTriggerStillWakesTheLoopOnAFrozenClock()
    {
        ArmingRecordingTimeProvider clock = new(new FakeTimeProvider(start));
        TimeSpan idleWaitTime = TimeSpan.FromMinutes(5);
        using SqliteTestDatabase database = new("loop-clock-signal");
        await using StandaloneSchedulerFactory factory = Build(clock, StoreKind.InMemory, database, idleWaitTime);
        IScheduler scheduler = await factory.GetScheduler();
        FiredListener fired = new();
        scheduler.ListenerManager.AddJobListener(fired);

        await scheduler.Start();
        await ShouldArrive(clock.Armed(IdleWait(idleWaitTime)), "a scheduler with nothing to do parks in its idle wait");

        await scheduler.ScheduleJob(Job("now"), Trigger(clock, "now", start));

        await ShouldArrive(fired.Fired,
            "scheduling a trigger that is due signals the loop, and a signal ends a wait without the clock moving");
        clock.GetUtcNow().Should().Be(start, "nothing advanced the clock, so the signal alone woke the loop");
    }

    /// <summary>
    /// The waits for the schedule moved to the scheduler's clock; what ends them at shutdown did not.
    /// Cancellation ends every one of them at once, and the persistent store's own loops give up on a
    /// stuck call in wall time (#3892), so no shutdown waits for a clock nobody advances.
    /// </summary>
    [Test]
    public async Task ShutdownReturnsPromptlyWhereverTheLoopIsParkedOnAFrozenClock(
        [Values] StoreKind store,
        [Values] Parked parked)
    {
        ArmingRecordingTimeProvider clock = new(new FakeTimeProvider(start));
        TimeSpan idleWaitTime = TimeSpan.FromHours(1);
        using SqliteTestDatabase database = new("loop-clock-shutdown");
        await using StandaloneSchedulerFactory factory = Build(clock, store, database, idleWaitTime);
        IScheduler scheduler = await factory.GetScheduler();

        await scheduler.Start();
        switch (parked)
        {
            case Parked.Idle:
                await ShouldArrive(clock.Armed(IdleWait(idleWaitTime)), "a scheduler with nothing to do parks in its idle wait");
                break;
            case Parked.BeforeAFiring:
                await scheduler.ScheduleJob(Job("later"), Trigger(clock, "later", start.AddMinutes(30)));
                await ShouldArrive(clock.Armed(arming => arming.DueTime == TimeSpan.FromMinutes(30)),
                    "the loop holds the trigger and waits for its fire time");
                break;
            default:
                await scheduler.Standby();
                await ShouldArrive(clock.Armed(PauseCheck), "a scheduler in standby parks in its pause wait");
                break;
        }

        Func<Task> shutdown = async () => await scheduler.Shutdown(waitForJobsToComplete: false);

        await shutdown.Should().CompleteWithinAsync(TimeSpan.FromSeconds(10),
            "a shutdown cancels the loop's wait, whichever it is in, and must not wait for a clock nobody advances");
    }

    /// <summary>
    /// The persistent store's misfire scan sleeps on the scheduler's clock too, so advancing it past a
    /// misfire gets the misfire handled with no real waiting.
    /// </summary>
    /// <remarks>
    /// The scheduler is in standby, so the loop never acquires the trigger and the only thing that can
    /// find it late is the misfire handler.
    /// </remarks>
    [Test]
    public async Task AdvancingTheClockDrivesThePersistentStoresMisfireScan()
    {
        ArmingRecordingTimeProvider clock = new(new FakeTimeProvider(start));
        using SqliteTestDatabase database = new("loop-clock-misfire");
        await using StandaloneSchedulerFactory factory = Build(clock, StoreKind.Sqlite, database, idleWaitTime: TimeSpan.FromMinutes(5));
        IScheduler scheduler = await factory.GetScheduler();
        MisfireListener misfired = new();
        scheduler.ListenerManager.AddTriggerListener(misfired);

        await scheduler.Start();
        await scheduler.Standby();
        await ShouldArrive(clock.Armed(PauseCheck), "a scheduler in standby parks in its pause wait");
        await scheduler.ScheduleJob(Job("missed"), Trigger(clock, "missed", start.AddSeconds(10)));

        // The store's misfire threshold, and so its scan interval: one minute unless configured.
        await ShouldArrive(clock.Armed(arming => arming.DueTime == TimeSpan.FromMinutes(1)),
            "the misfire handler scans once when the scheduler starts, then sleeps its interval on the scheduler's clock");
        clock.Clock.Advance(TimeSpan.FromMinutes(5));

        ITrigger trigger = await ShouldArrive(misfired.Misfired,
            "the advance ends the misfire handler's sleep, and its next scan finds the trigger four minutes past its misfire threshold");
        trigger.Key.Should().Be(new TriggerKey("missed"));
    }

    /// <summary>
    /// A trigger due after the loop's next look is found by that look, so scheduling one does not wake
    /// the loop (#3865) — which on a persistent store is a round trip saved per far-future schedule. One
    /// due before it still wakes the loop at once.
    /// </summary>
    [TestCase(StoreKind.InMemory)]
    [TestCase(StoreKind.Sqlite)]
    public async Task SchedulingATriggerDueAfterTheNextLookDoesNotWakeTheLoop(StoreKind store)
    {
        ArmingRecordingTimeProvider clock = new(new FakeTimeProvider(start));
        TimeSpan idleWaitTime = TimeSpan.FromMinutes(5);
        using SqliteTestDatabase database = new("loop-clock-no-wake");
        await using StandaloneSchedulerFactory factory = Build(clock, store, database, idleWaitTime);
        IScheduler scheduler = await factory.GetScheduler();
        FiredListener fired = new();
        scheduler.ListenerManager.AddJobListener(fired);
        QuartzSchedulerThread loop = LoopOf(scheduler);

        await scheduler.Start();
        await ShouldArrive(clock.Armed(IdleWait(idleWaitTime)), "a scheduler with nothing to do parks in its idle wait");
        long wakes = loop.SchedulingWakes;

        await scheduler.ScheduleJob(Job("later"), Trigger(clock, "later", start.AddHours(1)));

        loop.SchedulingWakes.Should().Be(wakes, "the trigger is due an hour after the loop's next look, which finds it without being told");
        loop.IsScheduleChanged().Should().BeFalse("a schedule the next look answers leaves nothing for the loop to act on");

        await scheduler.ScheduleJob(Job("now"), Trigger(clock, "now", start));

        // Greater rather than one more: the firing it starts may complete, and a completion signals too.
        loop.SchedulingWakes.Should().BeGreaterThan(wakes, "a trigger due now is due before the next look");
        await ShouldArrive(fired.FiredFor("now"), "the woken loop acquires and fires the trigger that is due");

        clock.Clock.Advance(TimeSpan.FromHours(1));

        await ShouldArrive(fired.FiredFor("later"), "the trigger that did not wake the loop is found by a later look");
    }

    [Test]
    public async Task ABatchWakesTheLoopOnlyWhenItsEarliestTriggerIsDueBeforeTheNextLook()
    {
        ArmingRecordingTimeProvider clock = new(new FakeTimeProvider(start));
        TimeSpan idleWaitTime = TimeSpan.FromMinutes(5);
        using SqliteTestDatabase database = new("loop-clock-batch");
        await using StandaloneSchedulerFactory factory = Build(clock, StoreKind.InMemory, database, idleWaitTime);
        IScheduler scheduler = await factory.GetScheduler();
        FiredListener fired = new();
        scheduler.ListenerManager.AddJobListener(fired);
        QuartzSchedulerThread loop = LoopOf(scheduler);

        await scheduler.Start();
        await ShouldArrive(clock.Armed(IdleWait(idleWaitTime)), "a scheduler with nothing to do parks in its idle wait");
        long wakes = loop.SchedulingWakes;

        await scheduler.ScheduleJobs(new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>>
        {
            [Job("in-an-hour")] = [Trigger(clock, "in-an-hour", start.AddHours(1))],
            [Job("in-two-hours")] = [Trigger(clock, "in-two-hours", start.AddHours(2))],
        });

        loop.SchedulingWakes.Should().Be(wakes, "every trigger in the batch is due after the loop's next look");

        await scheduler.ScheduleJobs(new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>>
        {
            [Job("in-three-hours")] = [Trigger(clock, "in-three-hours", start.AddHours(3))],
            [Job("due")] = [Trigger(clock, "due", start)],
        });

        loop.SchedulingWakes.Should().BeGreaterThan(wakes, "a batch is signalled with its earliest trigger, and this one's is due now");
        await ShouldArrive(fired.FiredFor("due"), "the woken loop fires the batch's due trigger");
    }

    /// <summary>
    /// A loop in standby is not parked until a known look, so a schedule made then always reaches it,
    /// and is fired once the scheduler starts again.
    /// </summary>
    [Test]
    public async Task AScheduleMadeInStandbyFiresOnceTheSchedulerStartsAgain()
    {
        ArmingRecordingTimeProvider clock = new(new FakeTimeProvider(start));
        using SqliteTestDatabase database = new("loop-clock-standby-schedule");
        await using StandaloneSchedulerFactory factory = Build(clock, StoreKind.InMemory, database, idleWaitTime: TimeSpan.FromMinutes(5));
        IScheduler scheduler = await factory.GetScheduler();
        FiredListener fired = new();
        scheduler.ListenerManager.AddJobListener(fired);
        QuartzSchedulerThread loop = LoopOf(scheduler);

        await scheduler.Start();
        await scheduler.Standby();
        await ShouldArrive(clock.Armed(PauseCheck), "a scheduler in standby parks in its pause wait");
        long wakes = loop.SchedulingWakes;

        await scheduler.ScheduleJob(Job("while-paused"), Trigger(clock, "while-paused", start.AddHours(1)));

        loop.SchedulingWakes.Should().Be(wakes + 1, "a paused loop is signalled whatever the trigger's time");

        await scheduler.Start();
        clock.Clock.Advance(TimeSpan.FromHours(1));

        await ShouldArrive(fired.FiredFor("while-paused"), "the trigger scheduled in standby fires once the scheduler runs and its time comes");
    }

    private static QuartzSchedulerThread LoopOf(IScheduler scheduler)
    {
        return ((StdScheduler) scheduler).scheduler.schedThread;
    }

    private static StandaloneSchedulerFactory Build(TimeProvider clock, StoreKind store, SqliteTestDatabase database, TimeSpan idleWaitTime)
    {
        return QuartzSchedulerBuilder
            .Create(q =>
            {
                q.UseTimeProvider(clock);
                q.ConfigureScheduler(options =>
                {
                    options.InstanceName = $"loop-clock-{Guid.NewGuid():N}";
                    options.IdleWaitTime = idleWaitTime;
                });

                if (store == StoreKind.Sqlite)
                {
                    q.UsePersistentStore(persistent =>
                    {
                        persistent.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                        persistent.ProvisionSchema();
                    });
                }
                else
                {
                    q.UseInMemoryStore();
                }
            })
            .Build();
    }

    private static IJobDetail Job(string name)
    {
        return JobBuilder.Create<ClockTestJob>().WithIdentity(name).Build();
    }

    /// <summary>
    /// A trigger on the test's clock: one built with no clock would take the wall clock's "now".
    /// </summary>
    private static ITrigger Trigger(TimeProvider clock, string name, DateTimeOffset fireTimeUtc)
    {
        return TriggerBuilder.Create(clock)
            .WithIdentity(name)
            .ForJob(name)
            .StartAt(fireTimeUtc)
            .Build();
    }

    /// <summary>
    /// The loop's idle wait: the idle wait time less up to a fifth of it, which is the randomization
    /// that keeps a cluster's nodes from polling in step.
    /// </summary>
    private static Func<TimerArming, bool> IdleWait(TimeSpan idleWaitTime)
    {
        return arming => arming.DueTime <= idleWaitTime && arming.DueTime >= idleWaitTime * 0.8;
    }

    private static bool PauseCheck(TimerArming arming) => arming.DueTime == pauseCheck;

    private static async Task ShouldArrive(Task arrival, string because)
    {
        Func<Task> act = () => arrival;
        await act.Should().CompleteWithinAsync(deadline, because);
    }

    private static async Task<T> ShouldArrive<T>(Task<T> arrival, string because)
    {
        Func<Task<T>> act = () => arrival;
        return (await act.Should().CompleteWithinAsync(deadline, because)).Which;
    }

    public sealed class ClockTestJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    private sealed class FiredListener : IJobListener
    {
        private readonly TaskCompletionSource<IJobExecutionContext> fired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<IJobExecutionContext>> byJob = new(StringComparer.Ordinal);

        /// <summary>The first job to have run.</summary>
        public Task<IJobExecutionContext> Fired => fired.Task;

        /// <summary>The job named <paramref name="jobName" />, once it has run.</summary>
        public Task<IJobExecutionContext> FiredFor(string jobName) => For(jobName).Task;

        public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException, CancellationToken cancellationToken = default)
        {
            fired.TrySetResult(context);
            For(context.JobDetail.Key.Name).TrySetResult(context);
            return default;
        }

        private TaskCompletionSource<IJobExecutionContext> For(string jobName)
        {
            return byJob.GetOrAdd(jobName, static _ => new TaskCompletionSource<IJobExecutionContext>(TaskCreationOptions.RunContinuationsAsynchronously));
        }
    }

    private sealed class MisfireListener : ITriggerListener
    {
        private readonly TaskCompletionSource<ITrigger> misfired = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ITrigger> Misfired => misfired.Task;

        public ValueTask TriggerMisfired(ITrigger trigger, IScheduler scheduler, CancellationToken cancellationToken = default)
        {
            misfired.TrySetResult(trigger);
            return default;
        }
    }
}
