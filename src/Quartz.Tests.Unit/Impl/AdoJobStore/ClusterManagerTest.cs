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
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

using FakeItEasy;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

public class ClusterManagerTest
{
    [Test]
    public async Task Shutdown_ShouldNotDeadlock_WhenDisposedBeforeTaskStarts()
    {
        // Arrange
        var jobStoreSupport = new TestJobStoreSupport();
        var clusterManager = new ClusterManager(jobStoreSupport);

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
        var jobStoreSupport = new TestJobStoreSupport();
        var clusterManager = new ClusterManager(jobStoreSupport);

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
    /// threshold, which nothing refuses here, reads the same way.
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
        List<TimeSpan> attempts = new List<TimeSpan>();

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

    private class TestJobStoreSupport : JobStoreSupport
    {
        public TestJobStoreSupport()
        {
            InstanceName = "TestInstance";
            InstanceId = "TestInstanceId";
            // Set a short frequency so that if the Run loop starts, it quickly checks
            // the cancellation token and exits, allowing shutdown tests to complete faster
            ClusterCheckinInterval = TimeSpan.FromMilliseconds(100);
        }

        protected override ConnectionAndTransactionHolder GetNonManagedTXConnection()
        {
            // Return a fake connection that will be used but won't actually do anything
            var fakeConnection = A.Fake<DbConnection>();
            return new ConnectionAndTransactionHolder(fakeConnection, null);
        }

        protected override Task<T> ExecuteInLock<T>(
            string lockName,
            Func<ConnectionAndTransactionHolder, Task<T>> txCallback,
            CancellationToken cancellationToken = default)
        {
            // For testing, return default value to avoid actual database operations
            // The tests don't rely on the return values from ExecuteInLock
            return Task.FromResult(default(T));
        }
    }
}
