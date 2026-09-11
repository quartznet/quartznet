using Quartz.Impl.AdoJobStore.Common;
using Quartz.Util;
using Quartz.Tests;
using Quartz.Extensibility;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

using System.Data.Common;

using FakeItEasy;

using Microsoft.Extensions.Time.Testing;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

public class ClusterManagerTest
{
    [Test]
    public async Task Shutdown_ShouldNotDeadlock_WhenDisposedBeforeTaskStarts()
    {
        // Arrange
        var jobStoreSupport = new TestAdoJobStoreBase();
        var clusterManager = new ClusterManager(jobStoreSupport, NullLogger<ClusterManager>.Instance);

        // Act - Initialize the manager and immediately shut it down
        // This simulates the race condition where shutdown happens before the task scheduler
        // has a chance to schedule the Run() task
        await clusterManager.Initialize();

        // Create a timeout task to detect deadlock
        var shutdownTask = clusterManager.Shutdown();
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
        var completedTask = await Task.WhenAny(shutdownTask, timeoutTask);

        // Assert - Should complete without deadlock
        completedTask.Should().Be(shutdownTask, "Shutdown should complete without hanging");
    }

    [Test]
    public async Task Shutdown_ShouldComplete_WhenTaskIsRunning()
    {
        // Arrange
        var jobStoreSupport = new TestAdoJobStoreBase();
        var clusterManager = new ClusterManager(jobStoreSupport, NullLogger<ClusterManager>.Instance);

        // Act - Initialize and give the task time to start
        await clusterManager.Initialize();
        await Task.Delay(100); // Give task time to start running

        // Now shutdown
        var shutdownTask = clusterManager.Shutdown();
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
        var completedTask = await Task.WhenAny(shutdownTask, timeoutTask);

        // Assert
        completedTask.Should().Be(shutdownTask, "Shutdown should complete");
    }

    /// <summary>
    /// The manager releases its token source on the way down, and a released source answers
    /// <c>Cancel</c> with an <see cref="ObjectDisposedException" /> rather than doing nothing — so
    /// shutting down twice has to be a shutdown and then a no-op.
    /// </summary>
    [Test]
    public async Task ShuttingDownTwiceIsAShutdownAndThenNothing()
    {
        TestAdoJobStoreBase jobStoreSupport = new();
        ClusterManager clusterManager = new(jobStoreSupport, NullLogger<ClusterManager>.Instance);

        await clusterManager.Initialize();
        await clusterManager.Shutdown();

        Func<Task> act = () => clusterManager.Shutdown();

        await act.Should().NotThrowAsync(
            "the store's shutdown is not the only thing that can reach this, and a second call finding "
            + "a released token source would fail a scheduler that is already down");
    }

    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(7500);
    private static readonly TimeSpan Threshold = TimeSpan.FromMilliseconds(7500);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ShortPause = TimeSpan.FromMilliseconds(100);

    [Test]
    public void ComputeTimeToSleep_ShouldSubtractTranspiredTime()
    {
        TimeSpan timeToSleep = ClusterManager.ComputeTimeToSleep(
            clusterCheckinInterval: Interval,
            transpiredTime: TimeSpan.FromSeconds(2),
            dbRetryInterval: RetryInterval,
            numFails: 0,
            clusterCheckinMisfireThreshold: Threshold);

        timeToSleep.Should().Be(TimeSpan.FromMilliseconds(5500));
    }

    [Test]
    public void ComputeTimeToSleep_ShouldUseShortPause_WhenCheckinIsOverdue()
    {
        TimeSpan timeToSleep = ClusterManager.ComputeTimeToSleep(
            clusterCheckinInterval: Interval,
            transpiredTime: TimeSpan.FromSeconds(20),
            dbRetryInterval: RetryInterval,
            numFails: 0,
            clusterCheckinMisfireThreshold: Threshold);

        timeToSleep.Should().Be(ShortPause);
    }

    /// <summary>
    /// A backward system clock jump makes the transpired time negative, which used to inflate
    /// the sleep by the length of the jump and stall check-ins long enough for peer nodes to
    /// consider this instance failed. See GitHub issue #1508.
    /// </summary>
    [Test]
    public void ComputeTimeToSleep_ShouldClampToCheckinInterval_WhenClockJumpsBackward()
    {
        TimeSpan timeToSleep = ClusterManager.ComputeTimeToSleep(
            clusterCheckinInterval: Interval,
            transpiredTime: TimeSpan.FromDays(-1),
            dbRetryInterval: RetryInterval,
            numFails: 0,
            clusterCheckinMisfireThreshold: Threshold);

        timeToSleep.Should().Be(Interval);
    }

    /// <summary>
    /// The peers write this node off once interval + threshold has passed since the row it last wrote,
    /// and a retry that lands after that arrives convicted. Before #3777 a failed check-in slept the
    /// full <c>DbRetryInterval</c>: on the defaults the next row went out 22.5 s after the last one,
    /// 7.5 s after the peers had stopped trusting it, so one database blip during a check-in got a live
    /// node recovered. The retry is now spent inside the window — half of what is left of it.
    /// </summary>
    [Test]
    public void ComputeTimeToSleep_ShouldRetryAtHalfTheRemainingWindow_WhenACheckinFails()
    {
        TimeSpan timeToSleep = ClusterManager.ComputeTimeToSleep(
            clusterCheckinInterval: Interval,
            transpiredTime: Interval,
            dbRetryInterval: RetryInterval,
            numFails: 1,
            clusterCheckinMisfireThreshold: Threshold);

        timeToSleep.Should().Be(TimeSpan.FromMilliseconds(3750),
            "a check-in that failed at the interval has the whole threshold left, and the retry lands halfway through it rather than DbRetryInterval later");
    }

    [Test]
    public void ComputeTimeToSleep_ShouldKeepHalvingTheWindow_WhileRetriesKeepFailing()
    {
        TimeSpan timeToSleep = ClusterManager.ComputeTimeToSleep(
            clusterCheckinInterval: Interval,
            transpiredTime: TimeSpan.FromMilliseconds(11250),
            dbRetryInterval: RetryInterval,
            numFails: 2,
            clusterCheckinMisfireThreshold: Threshold);

        timeToSleep.Should().Be(TimeSpan.FromMilliseconds(1875),
            "3.75 s of the window is left, so the next retry lands halfway through that");
    }

    [Test]
    public void ComputeTimeToSleep_ShouldNotRetryFasterThanTheShortPause_WhenTheWindowIsNearlySpent()
    {
        TimeSpan timeToSleep = ClusterManager.ComputeTimeToSleep(
            clusterCheckinInterval: Interval,
            transpiredTime: TimeSpan.FromMilliseconds(14950),
            dbRetryInterval: RetryInterval,
            numFails: 5,
            clusterCheckinMisfireThreshold: Threshold);

        timeToSleep.Should().Be(ShortPause,
            "half of 50 ms is under the loop's floor, and the floor is what an overdue check-in already waits");
    }

    /// <summary>
    /// The boundary belongs to the back-off: with nothing left of the window the node is already
    /// convictable, and there is no longer anything to hurry for.
    /// </summary>
    [TestCase(15_000)]
    [TestCase(22_500)]
    [TestCase(60_000)]
    public void ComputeTimeToSleep_ShouldBackOffDbRetryInterval_OnceTheWindowIsSpent(int transpiredMilliseconds)
    {
        TimeSpan timeToSleep = ClusterManager.ComputeTimeToSleep(
            clusterCheckinInterval: Interval,
            transpiredTime: TimeSpan.FromMilliseconds(transpiredMilliseconds),
            dbRetryInterval: RetryInterval,
            numFails: 1,
            clusterCheckinMisfireThreshold: Threshold);

        timeToSleep.Should().Be(RetryInterval,
            "past the window the ordinary back-off applies, exactly as before #3777");
    }

    [Test]
    public void ComputeTimeToSleep_ShouldCapTheInWindowRetryAtDbRetryInterval()
    {
        TimeSpan timeToSleep = ClusterManager.ComputeTimeToSleep(
            clusterCheckinInterval: Interval,
            transpiredTime: Interval,
            dbRetryInterval: RetryInterval,
            numFails: 1,
            clusterCheckinMisfireThreshold: TimeSpan.FromMinutes(1));

        timeToSleep.Should().Be(RetryInterval,
            "a minute-long threshold leaves 30 s to retry in, but the node still backs off no longer than DbRetryInterval");
    }

    [Test]
    public void ComputeTimeToSleep_ShouldHonourAShorterDbRetryInterval_InsideTheWindow()
    {
        TimeSpan timeToSleep = ClusterManager.ComputeTimeToSleep(
            clusterCheckinInterval: Interval,
            transpiredTime: Interval,
            dbRetryInterval: TimeSpan.FromSeconds(1),
            numFails: 1,
            clusterCheckinMisfireThreshold: Threshold);

        timeToSleep.Should().Be(TimeSpan.FromSeconds(1),
            "an operator who asked for a one-second back-off gets it; the window only ever shortens the wait");
    }

    /// <summary>
    /// A zero <c>DbRetryInterval</c> is legal, and before #3777 it already meant "retry an overdue
    /// check-in every short pause"; it still does, inside the window and after it.
    /// </summary>
    [TestCase(7_500)]
    [TestCase(20_000)]
    public void ComputeTimeToSleep_ShouldUseTheShortPause_WhenDbRetryIntervalIsZero(int transpiredMilliseconds)
    {
        TimeSpan timeToSleep = ClusterManager.ComputeTimeToSleep(
            clusterCheckinInterval: Interval,
            transpiredTime: TimeSpan.FromMilliseconds(transpiredMilliseconds),
            dbRetryInterval: TimeSpan.Zero,
            numFails: 1,
            clusterCheckinMisfireThreshold: Threshold);

        timeToSleep.Should().Be(ShortPause);
    }

    /// <summary>
    /// With no threshold the window closes at the interval, which is when the check-in was attempted,
    /// so there is nothing to retry inside and the back-off applies as it always did. A negative
    /// threshold, which nothing refuses on 3.x, reads the same way.
    /// </summary>
    [TestCase(0)]
    [TestCase(-5_000)]
    public void ComputeTimeToSleep_ShouldBackOffDbRetryInterval_WhenThereIsNoWindowToRetryIn(int thresholdMilliseconds)
    {
        TimeSpan timeToSleep = ClusterManager.ComputeTimeToSleep(
            clusterCheckinInterval: Interval,
            transpiredTime: Interval,
            dbRetryInterval: RetryInterval,
            numFails: 1,
            clusterCheckinMisfireThreshold: TimeSpan.FromMilliseconds(thresholdMilliseconds));

        timeToSleep.Should().Be(RetryInterval);
    }

    /// <summary>
    /// A backward clock jump during a failure streak makes the elapsed time negative. It must not widen
    /// the window: the real elapsed time is unknown but not less than zero, so the node retries as if
    /// the whole window were left — within one interval, which is also what the healthy branch does for
    /// the same jump — rather than sleeping <c>DbRetryInterval</c>, which is what this case used to pin.
    /// </summary>
    [Test]
    public void ComputeTimeToSleep_ShouldRetryWithinOneWindow_WhenTheClockJumpedBackwardDuringAFailure()
    {
        TimeSpan timeToSleep = ClusterManager.ComputeTimeToSleep(
            clusterCheckinInterval: Interval,
            transpiredTime: TimeSpan.FromDays(-1),
            dbRetryInterval: RetryInterval,
            numFails: 1,
            clusterCheckinMisfireThreshold: Threshold);

        timeToSleep.Should().Be(TimeSpan.FromMilliseconds(7500),
            "half of the full 15 s window, capped by nothing since DbRetryInterval is longer");
    }

    /// <summary>
    /// Whatever is left of the window, the retry lands inside it and never later than the back-off —
    /// down to where half of what is left is under the floor, which the floor test above covers.
    /// </summary>
    [TestCase(7_500)]
    [TestCase(9_000)]
    [TestCase(11_000)]
    [TestCase(13_000)]
    [TestCase(14_800)]
    public void ComputeTimeToSleep_ShouldLandTheRetryInsideTheWindow(int transpiredMilliseconds)
    {
        TimeSpan transpired = TimeSpan.FromMilliseconds(transpiredMilliseconds);
        TimeSpan windowLeft = Interval + Threshold - transpired;

        TimeSpan timeToSleep = ClusterManager.ComputeTimeToSleep(
            clusterCheckinInterval: Interval,
            transpiredTime: transpired,
            dbRetryInterval: RetryInterval,
            numFails: 1,
            clusterCheckinMisfireThreshold: Threshold);

        timeToSleep.Should().BeLessThan(windowLeft, "the retry has to be attempted before the peers stop trusting the last row");
        timeToSleep.Should().BeLessThanOrEqualTo(RetryInterval, "the window only ever shortens the back-off");
        timeToSleep.Should().BeGreaterThanOrEqualTo(ShortPause, "the loop never spins");
    }

    /// <summary>
    /// The timeline #3777 reports, replayed through the sleep computation with every attempt failing
    /// at once: last row written at 0, first failure at 7.5 s. Before the fix the one retry came at
    /// 22.5 s. Now every retry before 15 s lands strictly inside the window, and the first one after
    /// it is the ordinary back-off later. A failure that takes a while to report eats the window it
    /// is retried in, so a 3-second failure gets two attempts inside it rather than eight.
    /// </summary>
    [TestCase(0, 8)]
    [TestCase(3_000, 2)]
    public void AFailedCheckinIsRetriedInsideTheWindow_ThenBacksOff(int failureLatencyMilliseconds, int expectedAttemptsInsideTheWindow)
    {
        TimeSpan failureLatency = TimeSpan.FromMilliseconds(failureLatencyMilliseconds);
        TimeSpan windowEnd = Interval + Threshold;
        List<TimeSpan> attempts = [];

        TimeSpan now = Interval; // the first check-in after the last successful one, at the interval
        int numFails = 0;
        while (now < windowEnd)
        {
            attempts.Add(now);
            now += failureLatency; // the attempt fails, taking this long to say so
            numFails++;
            now += ClusterManager.ComputeTimeToSleep(Interval, now, RetryInterval, numFails, Threshold);
        }

        attempts.Should().HaveCount(expectedAttemptsInsideTheWindow);
        attempts.Should().OnlyContain(t => t >= Interval && t < windowEnd,
            "every attempt inside the window comes after the check-in that failed and before the peers stop trusting the last row");
        attempts.Should().BeInAscendingOrder();

        TimeSpan lastRetryDelay = ClusterManager.ComputeTimeToSleep(Interval, now, RetryInterval, numFails, Threshold);
        lastRetryDelay.Should().Be(RetryInterval,
            "once the window is spent the node is convictable anyway, and the ordinary back-off applies");
    }

    /// <summary>
    /// The wiring, which <see cref="ClusterManager.ComputeTimeToSleep" /> cannot pin: the loop has to
    /// time its retries from the last check-in that reached the database, not from the store's
    /// <see cref="AdoJobStoreBase.LastCheckin" />. A check-in that fails to read the state table stamps
    /// that too — on purpose, for <c>CalcFailedIfAfter</c> — and the scan runs before the write, so that
    /// is the stamp a database blip leaves. A loop timed from it would believe a full window was left
    /// and sleep 7.5 s, landing on the boundary; timed from the write it sleeps 3.75 s.
    /// </summary>
    [Test]
    public async Task TheLoopTimesItsRetriesFromTheLastCheckinThatReachedTheDatabase_NotFromTheStampAFailedScanLeaves()
    {
        DateTimeOffset start = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
        FakeTimeProvider clock = new(start);
        RecordingTimeProvider recorder = new(clock);
        IDriverDelegate driverDelegate = A.Fake<IDriverDelegate>();
        A.CallTo(() => driverDelegate.SelectSchedulerStateRecords(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<string>.Ignored,
                A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<SchedulerStateRecord>>([new SchedulerStateRecord("TestInstanceId", start, Interval)]))
            .Once()
            .Then
            .Throws(new InvalidOperationException("state table unavailable"));

        TestAdoJobStoreBase jobStoreSupport = new(recorder, driverDelegate, Interval);
        jobStoreSupport.SetFirstCheckIn(false);
        ClusterManager clusterManager = new(jobStoreSupport, NullLogger<ClusterManager>.Instance);

        try
        {
            // The first check-in, inline in Initialize, succeeds and writes the row the peers will read.
            await clusterManager.Initialize();
            jobStoreSupport.LastCheckin.Should().Be(start, "the successful check-in stamps the write's own time");

            await WaitUntil(() => recorder.Delays.Count >= 1);
            recorder.Delays[0].Should().Be(Interval, "a healthy node sleeps one interval");

            // The scan fails at the interval and stamps the store's LastCheckin with the failure time.
            clock.Advance(Interval);
            await WaitUntil(() => recorder.Delays.Count >= 2);
            jobStoreSupport.LastCheckin.Should().Be(start + Interval,
                "a failed scan stamps LastCheckin so that CalcFailedIfAfter does not count this node's silence against its peers");
            recorder.Delays[1].Should().Be(TimeSpan.FromMilliseconds(3750),
                "the retry is timed from the row that reached the database, which was written 7.5 s ago and has 7.5 s of window left");

            clock.Advance(TimeSpan.FromMilliseconds(3750));
            await WaitUntil(() => recorder.Delays.Count >= 3);
            recorder.Delays[2].Should().Be(TimeSpan.FromMilliseconds(1875), "half of the 3.75 s that is left");
        }
        finally
        {
            await clusterManager.Shutdown();
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        // Real time: the loop's continuation after a fake timer fires runs on a pool thread, and only
        // wall time reliably lets it get there.
        for (int i = 0; i < 1000 && !condition(); i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(2));
        }

        condition().Should().BeTrue("the check-in loop was expected to have registered its next sleep by now");
    }

    /// <summary>
    /// A <see cref="FakeTimeProvider" /> that also records the due time of every timer created on it.
    /// The check-in loop sleeps through <c>Task.Delay(…, timeProvider, token)</c>, so the recorded due
    /// times are exactly the values it computed, and the test can wait for a registration instead of
    /// guessing when to advance.
    /// </summary>
    private sealed class RecordingTimeProvider : TimeProvider
    {
        private readonly FakeTimeProvider inner;
        private readonly List<TimeSpan> delays = [];

        public RecordingTimeProvider(FakeTimeProvider inner)
        {
            this.inner = inner;
        }

        public IReadOnlyList<TimeSpan> Delays
        {
            get
            {
                lock (delays)
                {
                    return delays.ToArray();
                }
            }
        }

        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

        public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

        public override long TimestampFrequency => inner.TimestampFrequency;

        public override long GetTimestamp() => inner.GetTimestamp();

        public override ITimer CreateTimer(TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period)
        {
            lock (delays)
            {
                delays.Add(dueTime);
            }

            return inner.CreateTimer(callback, state, dueTime, period);
        }
    }

    private sealed class TestAdoJobStoreBase : AdoJobStoreBase
    {
        public TestAdoJobStoreBase()
        // A short check-in interval so that if the Run loop starts, it quickly checks the
        // cancellation token and exits, letting shutdown tests complete faster.
        : this(TimeProvider.System, driverDelegate: null, checkinInterval: TimeSpan.FromMilliseconds(100))
        {
        }

        public TestAdoJobStoreBase(TimeProvider timeProvider, IDriverDelegate driverDelegate, TimeSpan checkinInterval)
            : base(TestJobStores.Dependencies(
                timeProvider: timeProvider,
                schedulerOptions: TestJobStores.SchedulerOptions("TestInstance", "TestInstanceId"),
                clusteringOptions: TestJobStores.ClusteringOptions(configure: options => options.CheckinInterval = checkinInterval),
                driverDelegate: driverDelegate))
        {
        }

        /// <summary>
        /// Writes the private flag that tells the check-in path this is the node's first pass, so that a
        /// test can start from the cheap path and never reach the locks.
        /// </summary>
        internal void SetFirstCheckIn(bool value)
        {
            System.Reflection.FieldInfo fieldInfo = typeof(AdoJobStoreBase).GetField("firstCheckIn", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            fieldInfo.Should().NotBeNull("the first-check-in branch of CheckIn is gated on that field");
            fieldInfo.SetValue(this, value);
        }

        protected override ValueTask<ConnectionAndTransactionHolder> GetLocalTransactionConnection(CancellationToken cancellationToken = default)
        {
            // Return a fake connection that will be used but won't actually do anything
            var fakeConnection = A.Fake<DbConnection>();
            return new ValueTask<ConnectionAndTransactionHolder>(
                new ConnectionAndTransactionHolder(fakeConnection, null));
        }

        protected override ValueTask<T> ExecuteInLock<T>(
            SchedulerLock? lockKind,
            Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
            CancellationToken cancellationToken = default)
        {
            // For testing, return default value to avoid actual database operations
            // The tests don't rely on the return values from ExecuteInLock
            return new ValueTask<T>(default(T));
        }
    }
}
