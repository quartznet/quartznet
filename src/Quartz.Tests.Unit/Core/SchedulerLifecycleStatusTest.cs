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
/// The states a scheduler passes through, and what it tells the world on the way.
/// </summary>
/// <remarks>
/// <see cref="IScheduler.Status" /> is the whole of a scheduler's lifecycle state, so what it says and
/// what the listeners hear have to agree — which they did not while the state was three booleans read in
/// different orders by different callers, and while a shutdown announced a standby it was not in.
/// </remarks>
[NonParallelizable]
public class SchedulerLifecycleStatusTest
{
    [Test]
    public async Task ASchedulerAnnouncesEveryStateItPassesThroughInOrder()
    {
        IScheduler scheduler = await Build("lifecycle-events-in-order");
        RecordingSchedulerListener listener = new();
        scheduler.ListenerManager.AddSchedulerListener(listener);

        await scheduler.Start();
        await scheduler.Standby();
        await scheduler.Shutdown();

        listener.Events.Should().Equal(
            [
                nameof(ISchedulerListener.SchedulerStarting),
                nameof(ISchedulerListener.SchedulerStarted),
                nameof(ISchedulerListener.SchedulerInStandbyMode),
                nameof(ISchedulerListener.SchedulerShuttingDown),
                nameof(ISchedulerListener.SchedulerShutdown)
            ],
            "a listener is a scheduler's account of its own lifecycle, so the events are the states in the "
            + "order they happened - a shutdown in particular does not announce a standby it never entered");
    }

    [Test]
    public async Task ASchedulerThatWasNeverStartedIsCreatedRatherThanInStandby()
    {
        IScheduler scheduler = await Build("created-is-not-standby");

        try
        {
            scheduler.Status.Should().Be(SchedulerStatus.Created,
                "a scheduler that has been built and never started fires nothing, but it has also never run");

            await scheduler.Start();
            scheduler.Status.Should().Be(SchedulerStatus.Running);

            await scheduler.Standby();
            scheduler.Status.Should().Be(SchedulerStatus.Standby,
                "standby is where a running scheduler is stood down to, and it is distinguishable from never having run");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public async Task StandingANeverStartedSchedulerDownDoesNothingAtAll()
    {
        CountingJobStore store = null!;
        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q => q
                .ConfigureScheduler(options => options.InstanceName = "standby-before-start")
                .UseJobStore(provider =>
                {
                    store = new CountingJobStore(ActivatorUtilities.CreateInstance<RAMJobStore>(provider));
                    return store;
                }))
            .BuildScheduler();

        try
        {
            RecordingSchedulerListener listener = new();
            scheduler.ListenerManager.AddSchedulerListener(listener);

            await scheduler.Standby();

            scheduler.Status.Should().Be(SchedulerStatus.Created,
                "it was already firing nothing, and 'built but never run' is the more precise of the two answers");
            listener.Events.Should().BeEmpty(
                "nothing was stood down, so nothing may be announced - the thread is born paused and the store "
                + "was never told the scheduler was running");
            store.Paused.Should().Be(0,
                "the job store is told the scheduler paused when it stops firing, and it never started");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public async Task StartingASchedulerThatIsAlreadyRunningDoesNothingAtAll()
    {
        CountingJobStore store = null!;
        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q => q
                .ConfigureScheduler(options => options.InstanceName = "start-twice-is-a-no-op")
                .UseJobStore(provider =>
                {
                    store = new CountingJobStore(ActivatorUtilities.CreateInstance<RAMJobStore>(provider));
                    return store;
                }))
            .BuildScheduler();

        try
        {
            await scheduler.Start();

            RecordingSchedulerListener listener = new();
            scheduler.ListenerManager.AddSchedulerListener(listener);
            int resumesBefore = store.Resumed;

            await scheduler.Start();

            scheduler.Status.Should().Be(SchedulerStatus.Running);
            listener.Events.Should().BeEmpty(
                "nothing started, so nothing may be announced - a listener counting starts must not count this one");
            store.Resumed.Should().Be(resumesBefore,
                "the job store is told the scheduler resumed when it comes back from standby, and it never left");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public async Task ASchedulerThatHasShutDownCannotBeStoodDown()
    {
        IScheduler scheduler = await Build("standby-after-shutdown");
        await scheduler.Start();
        await scheduler.Shutdown();

        Func<Task> act = async () => await scheduler.Standby();

        await act.Should().ThrowAsync<SchedulerException>(
            "shutdown is terminal, so there is nothing left to stand down - answering as though there were "
            + "would leave a caller believing it could start the scheduler again");
    }

    [Test]
    public async Task ASchedulerThatHasShutDownCannotBeStarted()
    {
        IScheduler scheduler = await Build("start-after-shutdown");
        await scheduler.Start();
        await scheduler.Shutdown();

        Func<Task> act = async () => await scheduler.Start();

        await act.Should().ThrowAsync<SchedulerException>("a shut-down scheduler cannot be restarted");
    }

    /// <summary>
    /// A shutdown that is waiting for a running job is <see cref="SchedulerStatus.ShuttingDown" /> for as
    /// long as it waits, and <see cref="SchedulerStatus.Shutdown" /> only once everything it owns is down.
    /// </summary>
    [Test]
    public async Task AWaitingShutdownIsShuttingDownUntilTheStoreIsDown()
    {
        StatusWatchingJobStore store = null!;
        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q => q
                .ConfigureScheduler(options => options.InstanceName = "shutting-down-is-visible")
                .UseJobStore(provider =>
                {
                    store = new StatusWatchingJobStore(ActivatorUtilities.CreateInstance<RAMJobStore>(provider));
                    return store;
                }))
            .BuildScheduler();

        store.Scheduler = scheduler;

        GatedJob.Reset();

        await scheduler.ScheduleJob(
            JobBuilder.Create<GatedJob>().WithIdentity("gated").Build(),
            TriggerBuilder.Create().WithIdentity("gated").StartNow().Build());

        await scheduler.Start();
        await GatedJob.Started.WaitAsync(TimeSpan.FromSeconds(30));

        Task shutdown = scheduler.Shutdown(waitForJobsToComplete: true, CancellationToken.None).AsTask();

        await Task.Delay(250);

        shutdown.IsCompleted.Should().BeFalse("the job is still running and the shutdown was told to wait for it");
        scheduler.Status.Should().Be(SchedulerStatus.ShuttingDown,
            "a scheduler draining its running jobs has neither stopped nor gone into standby, and saying either "
            + "leaves an operator watching a shutdown with nothing to watch");

        GatedJob.Release();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(30));

        scheduler.Status.Should().Be(SchedulerStatus.Shutdown);
        store.StatusWhenShutDown.Should().Be(SchedulerStatus.ShuttingDown,
            "'shut down' is only true once the plugins and the job store are down, so the store must not be able "
            + "to read it about a scheduler that is still tearing it down");
    }

    /// <summary>
    /// A shutdown whose teardown throws still ends as <see cref="SchedulerStatus.Shutdown" />.
    /// </summary>
    /// <remarks>
    /// The shutdown is claimed once and cannot be retried, so a scheduler left ShuttingDown by a failed
    /// teardown would stay that way for the rest of the process: refusing every call, never listed as
    /// gone, and never reaching the state that says why.
    /// </remarks>
    [Test]
    public async Task AShutdownWhoseTeardownThrowsStillEndsShutDown()
    {
        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q => q
                .ConfigureScheduler(options => options.InstanceName = "teardown-throws")
                .UseJobStore(provider =>
                    new ThrowingShutdownJobStore(ActivatorUtilities.CreateInstance<RAMJobStore>(provider))))
            .BuildScheduler();

        await scheduler.Start();

        Func<Task> act = async () => await scheduler.Shutdown();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*store could not be closed*",
                "a teardown that failed is reported rather than swallowed");

        scheduler.Status.Should().Be(SchedulerStatus.Shutdown,
            "the scheduler is down either way, and the shutdown cannot be run again to finish the job");
    }

    private static async Task<IScheduler> Build(string instanceName)
    {
        return await QuartzSchedulerBuilder
            .Create(q => q.ConfigureScheduler(options => options.InstanceName = instanceName))
            .BuildScheduler();
    }

    /// <summary>
    /// Records the lifecycle events, and only those, in the order they arrive.
    /// </summary>
    private sealed class RecordingSchedulerListener : ISchedulerListener
    {
        private readonly List<string> events = [];

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

        public ValueTask SchedulerStarting(IScheduler scheduler, CancellationToken cancellationToken = default) => Record(nameof(SchedulerStarting));

        public ValueTask SchedulerStarted(IScheduler scheduler, CancellationToken cancellationToken = default) => Record(nameof(SchedulerStarted));

        public ValueTask SchedulerInStandbyMode(IScheduler scheduler, CancellationToken cancellationToken = default) => Record(nameof(SchedulerInStandbyMode));

        public ValueTask SchedulerShuttingDown(IScheduler scheduler, CancellationToken cancellationToken = default) => Record(nameof(SchedulerShuttingDown));

        public ValueTask SchedulerShutdown(IScheduler scheduler, CancellationToken cancellationToken = default) => Record(nameof(SchedulerShutdown));

        private ValueTask Record(string what)
        {
            lock (events)
            {
                events.Add(what);
            }

            return default;
        }
    }

    /// <summary>
    /// Counts the times the scheduler said it had resumed, which is what coming back from standby means to
    /// a store.
    /// </summary>
    private sealed class CountingJobStore : DelegatingJobStore
    {
        private int resumed;
        private int paused;

        public CountingJobStore(IJobStore jobStore) : base(jobStore)
        {
        }

        public int Resumed => Volatile.Read(ref resumed);

        public int Paused => Volatile.Read(ref paused);

        public override ValueTask SchedulerResumed(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref resumed);
            return base.SchedulerResumed(cancellationToken);
        }

        public override ValueTask SchedulerPaused(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref paused);
            return base.SchedulerPaused(cancellationToken);
        }
    }

    /// <summary>
    /// Reads the scheduler's status at the moment the store is torn down, which is the last thing a
    /// shutdown does before it calls itself finished.
    /// </summary>
    private sealed class StatusWatchingJobStore : DelegatingJobStore
    {
        public StatusWatchingJobStore(IJobStore jobStore) : base(jobStore)
        {
        }

        public IScheduler? Scheduler { get; set; }

        public SchedulerStatus? StatusWhenShutDown { get; private set; }

        public override ValueTask Shutdown(CancellationToken cancellationToken = default)
        {
            StatusWhenShutDown = Scheduler?.Status;
            return base.Shutdown(cancellationToken);
        }
    }

    /// <summary>
    /// A store that cannot be torn down, which is what a shutdown looks like when the database has gone
    /// away underneath it.
    /// </summary>
    private sealed class ThrowingShutdownJobStore : DelegatingJobStore
    {
        public ThrowingShutdownJobStore(IJobStore jobStore) : base(jobStore)
        {
        }

        public override ValueTask Shutdown(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("the store could not be closed");
        }
    }

    /// <summary>
    /// A job that runs until the test lets it stop.
    /// </summary>
    [DisallowConcurrentExecution]
    private sealed class GatedJob : IJob
    {
        private static TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static Task Started => started.Task;

        public static void Reset()
        {
            started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public static void Release()
        {
            release.TrySetResult();
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await release.Task.ConfigureAwait(false);
        }
    }
}
