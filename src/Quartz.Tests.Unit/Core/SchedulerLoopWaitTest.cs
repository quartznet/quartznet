using FakeItEasy;

using Microsoft.Extensions.Time.Testing;

using Quartz.Core;
using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// How long the scheduling loop's waits on the scheduler's clock are armed for (#3869), when the clock
/// does something other than stand still between the loop's decision to wait and the wait.
/// </summary>
/// <remarks>
/// The loop runs over an empty store on its own, with nothing to fire, so every timer armed on the
/// clock is its idle wait, and no timer fires unless a test advances the clock under it.
/// </remarks>
[NonParallelizable]
public sealed class SchedulerLoopWaitTest
{
    private static readonly TimeSpan observationDeadline = TimeSpan.FromSeconds(30);

    private ArmingRecordingTimeProvider clock;
    private FaultInjectingJobStore store;
    private QuartzSchedulerThread thread;

    [SetUp]
    public async Task SetUp()
    {
        clock = new ArmingRecordingTimeProvider(new FakeTimeProvider(new DateTimeOffset(2026, 3, 6, 8, 0, 0, TimeSpan.Zero)));
        store = new FaultInjectingJobStore();
        await store.Initialize(TestJobStores.Identity());
    }

    [TearDown]
    public async Task TearDown()
    {
        if (thread is not null)
        {
            await thread.Halt(wait: true);
            await thread.Shutdown();
        }

        await store.Shutdown();
    }

    /// <summary>
    /// A timer cannot be armed for longer than <see cref="TimerLimits.MaxDelay" />. An idle wait time
    /// beyond it is waited that long, and the loop looks again, rather than failing to arm at all.
    /// </summary>
    [Test]
    public async Task AnIdleWaitLongerThanATimerAllowsIsArmedForAsLongAsOneDoes()
    {
        StartLoop(idleWaitTime: TimeSpan.FromDays(100));

        await ShouldObserve(clock.Armed(_ => true), "an empty store leaves the loop parked in its idle wait");

        clock.Armings[0].DueTime.Should().Be(TimerLimits.MaxDelay,
            "a hundred days less a fifth at most is still longer than a timer can be armed for");
    }

    /// <summary>
    /// A wait is counted from the reading the loop decided on, so a wall clock stepped back since then
    /// would stretch it by the step. It is never armed for longer than was asked.
    /// </summary>
    [Test]
    public async Task AClockSteppedBackCannotStretchAWait()
    {
        TimeSpan idleWaitTime = TimeSpan.FromSeconds(10);
        clock.StepPerRead = TimeSpan.FromMinutes(-1);
        StartLoop(idleWaitTime);

        await ShouldObserve(clock.Armed(_ => true), "an empty store leaves the loop parked in its idle wait");

        clock.Armings[0].DueTime.Should().BeLessThanOrEqualTo(idleWaitTime,
            "the clock has gone back a minute since the reading the wait counts from, and the wait must not grow by it");
    }

    /// <summary>
    /// A clock moved while the loop is arming its wait arms the timer from the moved time, a whole wait
    /// past the deadline. The reading the loop takes once the timer is armed catches that, and the loop
    /// looks again at once instead of sleeping on a timer nothing will fire.
    /// </summary>
    [Test]
    public async Task AClockMovedWhileTheWaitIsBeingArmedEndsTheWait()
    {
        clock.OnNextArming = () => clock.Skew += TimeSpan.FromHours(1);
        StartLoop(TimeSpan.FromSeconds(10));

        await ShouldObserve(store.Acquisitions.Reaches(2),
            "the clock passed the idle wait's deadline while it was being armed, so the loop has to look again without being woken");
        await ShouldObserve(clock.Armed(_ => true, count: 2), "and then park again, now that the clock stands still");
    }

    private void StartLoop(TimeSpan idleWaitTime)
    {
        IThreadPool threadPool = A.Fake<IThreadPool>();
        A.CallTo(() => threadPool.PoolSize).Returns(4);
        A.CallTo(() => threadPool.WaitForAvailableThreads(A<CancellationToken>.Ignored)).Returns(new ValueTask<int>(4));

        QuartzSchedulerResources resources = new()
        {
            Name = "loopWaitTest",
            InstanceId = "loopWaitTestInstance",
            IdleWaitTime = idleWaitTime,
            MaxBatchSize = 1,
            JobStore = store,
            ThreadPool = threadPool,
            JobRunShellFactory = new ScriptedJobRunShellFactory(),
            TimeProvider = clock,
        };

        thread = new QuartzSchedulerThread(new QuartzScheduler(resources), resources);
        thread.TogglePause(pause: false);
        thread.Start();
    }

    private static async Task ShouldObserve(Task observation, string because)
    {
        Func<Task> act = () => observation;
        await act.Should().CompleteWithinAsync(observationDeadline, because);
    }
}
