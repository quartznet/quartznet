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

using Quartz.Util;

namespace Quartz.Tests.Unit.Util;

/// <summary>
/// What disposing the dedicated-thread scheduler does, now that disposing it releases something.
/// </summary>
/// <remarks>
/// It owned a <see cref="CancellationTokenSource" /> that nothing ever released — its dispose only
/// cancelled — so a scheduler built and torn down per cluster manager and per misfire handler left one
/// behind each time. Releasing it means the call can no longer be repeated blindly: a released source
/// answers <c>Cancel</c> with an <see cref="ObjectDisposedException" />.
/// </remarks>
public sealed class QueuedTaskSchedulerTest
{
    private static QueuedTaskScheduler Scheduler() =>
        new(threadCount: 1, threadName: "QueuedTaskSchedulerTest", useForegroundThreads: false);

    [Test]
    public void DisposingTwiceIsADisposalAndThenNothing()
    {
        QueuedTaskScheduler scheduler = Scheduler();

        scheduler.Dispose();

        Action act = () => scheduler.Dispose();

        act.Should().NotThrow(
            "both of the thread pool's teardown paths end in a Dispose and either can follow the other, "
            + "so a second one has to be a no-op rather than the exception a released source answers with");
    }

    /// <summary>
    /// The dispatch loop reads its token once, before anything can release the source, so work handed
    /// over the ordinary way still runs. Cheap to state and the thing the token-hoisting could have
    /// broken.
    /// </summary>
    [Test]
    public async Task WorkQueuedBeforeDisposalRuns()
    {
        using QueuedTaskScheduler scheduler = Scheduler();

        TaskCompletionSource ran = new(TaskCreationOptions.RunContinuationsAsynchronously);

        await Task.Factory.StartNew(
            () => ran.TrySetResult(),
            CancellationToken.None,
            TaskCreationOptions.HideScheduler,
            scheduler);

        await ran.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
