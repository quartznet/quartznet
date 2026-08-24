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

using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// What "the scheduler waited for its running jobs" is worth, and what a deadline does to it.
/// </summary>
/// <remarks>
/// The barrier has to cover a job's job store update and not merely the job: <see cref="JobRunShell"/>
/// tells the job listeners the job was executed — which is what drops it out of
/// <see cref="QuartzScheduler.NumberOfJobsExecutingHere"/> — before it calls
/// <c>TriggeredJobComplete</c>. Anything that waited on that count would resume while a persistent store
/// was still being written to. The thread pool's own barrier does not have that hole, because the pool is
/// handed the whole of the execution, of which the store update is the last act; these tests pin both
/// halves of that sentence down.
/// </remarks>
[NonParallelizable]
public class ShutdownDrainTest
{
    private const string StoreUpdateStarted = "job store update started";
    private const string StoreUpdateFinished = "job store update finished";
    private const string StoreShutDown = "job store shut down";

    [Test]
    public async Task AShutdownThatWaitedForItsJobsWaitedForTheirStoreUpdatesToo()
    {
        RecordingJobStore store = null!;
        IScheduler scheduler = await QuartzSchedulerBuilder.Create()
            .ConfigureScheduler(options => options.InstanceName = "drain-covers-the-store-write")
            .UseJobStore(provider =>
            {
                store = new RecordingJobStore(ActivatorUtilities.CreateInstance<RAMJobStore>(provider));
                return store;
            })
            .BuildScheduler();

        await scheduler.ScheduleJob(
            JobBuilder.Create<QuickJob>().WithIdentity("quick").Build(),
            TriggerBuilder.Create().WithIdentity("quick").StartNow().Build());

        await scheduler.Start();

        // The job has run and its store update is under way, held open by the store double until this
        // test releases it.
        await store.StoreUpdateEntered.WaitAsync(TimeSpan.FromSeconds(30));

        ExecutingJobCount(scheduler).Should().Be(0,
            "the job listeners are told the job was executed before its store update is issued, so a barrier built "
            + "on the count of executing jobs would already read zero here - which is exactly why the barrier is not built on it");

        using CancellationTokenSource generous = new(TimeSpan.FromSeconds(60));
        Task shutdown = scheduler.Shutdown(waitForJobsToComplete: true, generous.Token).AsTask();

        await Task.Delay(250);

        shutdown.IsCompleted.Should().BeFalse(
            "the store update is still in flight, and a shutdown that returned now would tear the store down underneath it");

        store.ReleaseStoreUpdate();

        await shutdown.WaitAsync(TimeSpan.FromSeconds(30));

        store.Events.Should().Equal([StoreUpdateStarted, StoreUpdateFinished, StoreShutDown],
            "the job store may only be shut down once the update that ends the last job has been written");
    }

    [Test]
    public async Task AShutdownGivesUpOnTheWaitWhenTheCallersTokenFires()
    {
        IScheduler scheduler = await QuartzSchedulerBuilder.Create()
            .ConfigureScheduler(options => options.InstanceName = "drain-honours-the-deadline")
            .BuildScheduler();

        ShutdownRecordingListener listener = new ShutdownRecordingListener();
        scheduler.ListenerManager.AddSchedulerListener(listener);

        GatedJob.Reset();

        await scheduler.ScheduleJob(
            JobBuilder.Create<GatedJob>().WithIdentity("gated").Build(),
            TriggerBuilder.Create().WithIdentity("gated").StartNow().Build());

        await scheduler.Start();
        await GatedJob.Started.WaitAsync(TimeSpan.FromSeconds(30));

        using CancellationTokenSource deadline = new(TimeSpan.FromMilliseconds(100));

        Func<Task> act = async () => await scheduler.Shutdown(waitForJobsToComplete: true, deadline.Token);

        await act.Should().NotThrowAsync(
            "an expired deadline has to end the wait and let the rest of the shutdown run, not throw out of it");

        scheduler.Status.Should().Be(SchedulerStatus.Shutdown, "the shutdown ran to the end even though the wait was abandoned");
        listener.ShutDown.Should().BeTrue(
            "a shutdown is claimed atomically and cannot be retried, so a deadline that stopped it part-way would leave "
            + "the scheduler neither running nor shut down, with nothing ever told it had stopped");
        GatedJob.Finished.IsCompleted.Should().BeFalse(
            "the deadline bounds the waiting, so the job it stopped waiting for is still running");

        GatedJob.Release();

        await GatedJob.Finished.WaitAsync(TimeSpan.FromSeconds(30));

        GatedJob.WasCancelled.Should().BeFalse(
            "cancelling the wait must not cancel the jobs - whether a shutting-down scheduler interrupts them is "
            + "ShutdownJobInterruption's decision, and it defaults to never");
    }

    private static int ExecutingJobCount(IScheduler scheduler)
    {
        return ((StdScheduler) scheduler).scheduler.NumberOfJobsExecutingHere;
    }

    /// <summary>
    /// A job whose only job is to end, so that what the test watches is the store update that follows it.
    /// </summary>
    [DisallowConcurrentExecution]
    private sealed class QuickJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    /// <summary>
    /// A job that runs until the test lets it stop, and records whether anything cancelled it.
    /// </summary>
    [DisallowConcurrentExecution]
    private sealed class GatedJob : IJob
    {
        private static TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static Task Started => started.Task;

        public static Task Finished => finished.Task;

        public static bool WasCancelled { get; private set; }

        public static void Reset()
        {
            started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            WasCancelled = false;
        }

        public static void Release()
        {
            release.TrySetResult();
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await release.Task.ConfigureAwait(false);
            WasCancelled = cancellationToken.IsCancellationRequested;
            finished.TrySetResult();
        }
    }

    /// <summary>
    /// Records that the scheduler said it had shut down, which is the last thing a shutdown does.
    /// </summary>
    private sealed class ShutdownRecordingListener : ISchedulerListener
    {
        public bool ShutDown { get; private set; }

        public ValueTask SchedulerShutdown(IScheduler scheduler, CancellationToken cancellationToken = default)
        {
            ShutDown = true;
            return default;
        }
    }

    /// <summary>
    /// A store that takes as long over the update which ends a job as the test wants it to, which is what
    /// a persistent store looks like from the shutdown path's point of view.
    /// </summary>
    private sealed class RecordingJobStore : DelegatingJobStore
    {
        private readonly List<string> events = [];
        private readonly TaskCompletionSource storeUpdateEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseStoreUpdate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RecordingJobStore(IJobStore jobStore) : base(jobStore)
        {
        }

        public override bool SupportsPersistence => true;

        public Task StoreUpdateEntered => storeUpdateEntered.Task;

        public List<string> Events
        {
            get
            {
                lock (events)
                {
                    return [.. events];
                }
            }
        }

        public void ReleaseStoreUpdate()
        {
            releaseStoreUpdate.TrySetResult();
        }

        public override async ValueTask TriggeredJobComplete(
            IOperableTrigger trigger,
            IJobDetail jobDetail,
            SchedulerInstruction triggerInstructionCode,
            CancellationToken cancellationToken = default)
        {
            Record(StoreUpdateStarted);
            storeUpdateEntered.TrySetResult();

            await releaseStoreUpdate.Task.ConfigureAwait(false);
            await base.TriggeredJobComplete(trigger, jobDetail, triggerInstructionCode, cancellationToken).ConfigureAwait(false);

            Record(StoreUpdateFinished);
        }

        public override ValueTask Shutdown(CancellationToken cancellationToken = default)
        {
            Record(StoreShutDown);
            return base.Shutdown(cancellationToken);
        }

        private void Record(string what)
        {
            lock (events)
            {
                events.Add(what);
            }
        }
    }
}
