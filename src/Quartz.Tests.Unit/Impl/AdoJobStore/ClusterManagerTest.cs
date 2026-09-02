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

    [Test]
    public void ComputeTimeToSleep_ShouldSubtractTranspiredTime()
    {
        TimeSpan timeToSleep = ClusterManager.ComputeTimeToSleep(
            clusterCheckinInterval: TimeSpan.FromMilliseconds(7500),
            transpiredTime: TimeSpan.FromSeconds(2),
            dbRetryInterval: TimeSpan.FromSeconds(15),
            numFails: 0);

        timeToSleep.Should().Be(TimeSpan.FromMilliseconds(5500));
    }

    [Test]
    public void ComputeTimeToSleep_ShouldUseShortPause_WhenCheckinIsOverdue()
    {
        TimeSpan timeToSleep = ClusterManager.ComputeTimeToSleep(
            clusterCheckinInterval: TimeSpan.FromMilliseconds(7500),
            transpiredTime: TimeSpan.FromSeconds(20),
            dbRetryInterval: TimeSpan.FromSeconds(15),
            numFails: 0);

        timeToSleep.Should().Be(TimeSpan.FromMilliseconds(100));
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
            clusterCheckinInterval: TimeSpan.FromMilliseconds(7500),
            transpiredTime: TimeSpan.FromDays(-1),
            dbRetryInterval: TimeSpan.FromSeconds(15),
            numFails: 0);

        timeToSleep.Should().Be(TimeSpan.FromMilliseconds(7500));
    }

    [Test]
    public void ComputeTimeToSleep_ShouldPreferDbRetryInterval_WhenCheckinsHaveFailed()
    {
        TimeSpan timeToSleep = ClusterManager.ComputeTimeToSleep(
            clusterCheckinInterval: TimeSpan.FromMilliseconds(7500),
            transpiredTime: TimeSpan.FromDays(-1),
            dbRetryInterval: TimeSpan.FromSeconds(15),
            numFails: 1);

        timeToSleep.Should().Be(TimeSpan.FromSeconds(15));
    }

    private sealed class TestAdoJobStoreBase : AdoJobStoreBase
    {
        public TestAdoJobStoreBase()
        // A short check-in interval so that if the Run loop starts, it quickly checks the
        // cancellation token and exits, letting shutdown tests complete faster.
        : base(TestJobStores.Dependencies(
            schedulerOptions: TestJobStores.SchedulerOptions("TestInstance", "TestInstanceId"),
            clusteringOptions: TestJobStores.ClusteringOptions(configure: options => options.CheckinInterval = TimeSpan.FromMilliseconds(100))))
        {
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
