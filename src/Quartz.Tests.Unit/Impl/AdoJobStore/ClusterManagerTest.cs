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

using FluentAssertions;

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

        protected override ValueTask<ConnectionAndTransactionHolder> GetNonManagedTXConnection()
        {
            // Return a fake connection that will be used but won't actually do anything
            var fakeConnection = A.Fake<DbConnection>();
            return new ValueTask<ConnectionAndTransactionHolder>(
                new ConnectionAndTransactionHolder(fakeConnection, null));
        }

        protected override ValueTask<T> ExecuteInLock<T>(
            string lockName,
            Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
            CancellationToken cancellationToken = default)
        {
            // For testing, return default value to avoid actual database operations
            // The tests don't rely on the return values from ExecuteInLock
            return new ValueTask<T>(default(T));
        }
    }
}
