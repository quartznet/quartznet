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

using Quartz.Core;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// What a shutdown that does not wait for its jobs still owes the schedule (#3746).
/// </summary>
/// <remarks>
/// Two firings are at stake and they are different firings. One has been committed to the job store —
/// <c>TriggersFired</c> has run, the trigger has moved past it — but not yet handed to the thread pool;
/// dropping that one loses an occurrence outright, because nothing but a job asking for recovery ever
/// gets it back. The other is already executing, and what it owes the store is the completion that
/// unblocks its trigger and deletes its fired-trigger row; losing that one leaves a schedule stopped
/// until a peer times the departed node out, which on a default cluster is half a minute and outside a
/// cluster is until the node next starts.
/// </remarks>
[NonParallelizable]
public sealed class UnwaitedShutdownTest
{
    [SetUp]
    public void ResetJobs()
    {
        CountingJob.Reset();
        ParkingJob.Reset();
    }

    /// <summary>
    /// The scheduler's own firing loop is stopped before the thread pool is closed to new work, so a
    /// firing it has already committed to the store is still dispatched.
    /// </summary>
    /// <remarks>
    /// The store double parks the loop between <c>TriggersFired</c> and the dispatch, which is the
    /// window the loss lived in: it is a real window, not a contrived one, because the acquisition
    /// cycle takes the cluster lock and a database round trip while a shutdown running on another
    /// thread needs neither.
    /// </remarks>
    [Test]
    public async Task AShutdownThatDidNotWaitStillRunsTheFiringItHadAlreadyCommitted()
    {
        GateAfterFiringStore store = null!;
        RecordingThreadPool pool = new(new DefaultThreadPool());

        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q => q
                .ConfigureScheduler(options => options.InstanceName = "unwaited-shutdown-keeps-the-firing")
                .UseThreadPool(pool)
                .UseJobStore(provider =>
                {
                    store = new GateAfterFiringStore(ActivatorUtilities.CreateInstance<RAMJobStore>(provider));
                    return store;
                }))
            .BuildScheduler();

        await scheduler.ScheduleJob(
            JobBuilder.Create<CountingJob>().WithIdentity("counted").Build(),
            TriggerBuilder.Create().WithIdentity("counted").StartNow().Build());

        await scheduler.Start();

        // The firing is now in the store — the trigger has been advanced past it and its fired-trigger
        // row written — and the loop is holding it, one statement short of handing it to the pool.
        await store.FiringCommitted.WaitAsync(TimeSpan.FromSeconds(30));

        Task shutdown = scheduler.Shutdown(waitForJobsToComplete: false).AsTask();

        // Released the moment the pool is closed to new work, which is what the loop used to wake up
        // to and be refused by; where the loop is stopped first, no such call comes and the short
        // delay is what lets it finish its cycle instead.
        await Task.WhenAny(pool.ClosedToNewWork, Task.Delay(TimeSpan.FromSeconds(1)));
        store.ReleaseFiring();

        await shutdown.WaitAsync(TimeSpan.FromSeconds(30));

        CountingJob.Executions.Should().Be(1,
            "TriggersFired has already committed this firing and advanced the trigger, so a shutdown "
            + "that refuses to dispatch it loses the occurrence outright — the trigger will not offer "
            + "it again, and only a job that asked for recovery would ever get it back");
    }

    /// <summary>
    /// An execution that ends while the shutdown is still under way reports its completion to a store
    /// that is still open, rather than to one that has already been torn down.
    /// </summary>
    /// <remarks>
    /// Stated as an ordering rather than as a state, because the state a lost completion leaves is the
    /// job store's business and every store leaves a different one: the in-memory store shrugs it off,
    /// the ADO store answers "JobStore is shutdown" and leaves the trigger BLOCKED with its
    /// fired-trigger row in place. What is common to all of them is that the update has to reach the
    /// store before the store is told to close.
    /// </remarks>
    [Test]
    public async Task AShutdownThatDidNotWaitLetsAnInFlightCompletionReachTheStore()
    {
        RecordingJobStore store = null!;

        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q => q
                .ConfigureScheduler(options => options.InstanceName = "unwaited-shutdown-settles-its-books")
                .UseJobStore(provider =>
                {
                    store = new RecordingJobStore(ActivatorUtilities.CreateInstance<RAMJobStore>(provider));
                    return store;
                }))
            .BuildScheduler();

        await scheduler.ScheduleJob(
            JobBuilder.Create<ParkingJob>().WithIdentity("parked").Build(),
            TriggerBuilder.Create().WithIdentity("parked").StartNow().Build());

        await scheduler.Start();
        await ParkingJob.Started.WaitAsync(TimeSpan.FromSeconds(30));

        // The job ends a fraction of a second after the node is told to come down, which is the
        // ordinary shape of an interrupted firing: it was already running, and it finishes while the
        // shutdown is still working through its steps.
        Task release = Task.Run(async () =>
        {
            await Task.Delay(200);
            ParkingJob.Release();
        });

        await scheduler.Shutdown(waitForJobsToComplete: false);
        await release;

        store.Events.Should().Equal([RecordingJobStore.CompletionRecorded, RecordingJobStore.StoreShutDown],
            "a firing that ran to completion has to be recorded as complete by the node that ran it, "
            + "whether or not that node waited for it — a completion issued after the store has closed "
            + "is refused, and the firing's bookkeeping is left for somebody else to do");

        Drained(scheduler).Should().BeTrue(
            "the execution settled inside the window the shutdown gave it, and the barrier covers the "
            + "store update as well as the job");
    }

    private static bool? Drained(IScheduler scheduler)
    {
        return ((StdScheduler) scheduler).scheduler.RunningWorkDrained;
    }

    /// <summary>
    /// Counts what actually ran.
    /// </summary>
    private sealed class CountingJob : IJob
    {
        private static int executions;

        public static int Executions => Volatile.Read(ref executions);

        public static void Reset() => Interlocked.Exchange(ref executions, 0);

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref executions);
            return default;
        }
    }

    /// <summary>
    /// Runs until the test lets it stop, so that a shutdown can be asked for while it is inside.
    /// </summary>
    private sealed class ParkingJob : IJob
    {
        private static TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static Task Started => started.Task;

        public static void Reset()
        {
            started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public static void Release() => release.TrySetResult();

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            started.TrySetResult();

            // Deliberately not the job's own token: what is under test is a node coming down while a
            // firing is in flight, and a firing that turned into a cancellation here would be a
            // different case with a different answer.
            await release.Task.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Holds the scheduler's firing loop between the firing and the dispatch, which is the only place a
    /// committed occurrence can still be dropped.
    /// </summary>
    private sealed class GateAfterFiringStore : DelegatingJobStore
    {
        private readonly TaskCompletionSource firingCommitted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseFiring = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public GateAfterFiringStore(IJobStore jobStore) : base(jobStore)
        {
        }

        public Task FiringCommitted => firingCommitted.Task;

        public void ReleaseFiring() => releaseFiring.TrySetResult();

        public override async ValueTask<List<TriggerFiredResult>> TriggersFired(
            IReadOnlyCollection<IOperableTrigger> triggers,
            CancellationToken cancellationToken = default)
        {
            List<TriggerFiredResult> fired = await base.TriggersFired(triggers, cancellationToken).ConfigureAwait(false);

            if (fired.Exists(x => x.TriggerFiredBundle is not null))
            {
                firingCommitted.TrySetResult();
                await releaseFiring.Task.ConfigureAwait(false);
            }

            return fired;
        }
    }

    /// <summary>
    /// Records the two events whose order is the promise: the completion of the firing that was in
    /// flight, and the store being told to close.
    /// </summary>
    private sealed class RecordingJobStore : DelegatingJobStore
    {
        public const string CompletionRecorded = "completion recorded";
        public const string StoreShutDown = "job store shut down";

        private readonly List<string> events = [];

        public RecordingJobStore(IJobStore jobStore) : base(jobStore)
        {
        }

        public override bool SupportsPersistence => true;

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

        public override async ValueTask TriggeredJobComplete(
            IOperableTrigger trigger,
            IJobDetail jobDetail,
            SchedulerInstruction triggerInstructionCode,
            CancellationToken cancellationToken = default)
        {
            await base.TriggeredJobComplete(trigger, jobDetail, triggerInstructionCode, cancellationToken).ConfigureAwait(false);
            Record(CompletionRecorded);
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

    /// <summary>
    /// Says when the pool stopped accepting work, which is the instant a firing the loop still holds
    /// becomes undispatchable.
    /// </summary>
    private sealed class RecordingThreadPool : IThreadPool
    {
        private readonly IThreadPool inner;
        private readonly TaskCompletionSource closedToNewWork = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RecordingThreadPool(IThreadPool inner)
        {
            this.inner = inner;
        }

        public Task ClosedToNewWork => closedToNewWork.Task;

        public int PoolSize => inner.PoolSize;

        public ValueTask Initialize(CancellationToken cancellationToken = default) => inner.Initialize(cancellationToken);

        public ValueTask<int> WaitForAvailableThreads(CancellationToken cancellationToken = default)
            => inner.WaitForAvailableThreads(cancellationToken);

        public ValueTask<bool> TryRun(Func<ValueTask> action, CancellationToken cancellationToken = default)
            => inner.TryRun(action, cancellationToken);

        public ValueTask Shutdown(bool waitForJobsToComplete = true, CancellationToken cancellationToken = default)
        {
            closedToNewWork.TrySetResult();
            return inner.Shutdown(waitForJobsToComplete, cancellationToken);
        }

        public ValueTask<bool> Drain(CancellationToken cancellationToken = default)
        {
            closedToNewWork.TrySetResult();
            return inner.Drain(cancellationToken);
        }
    }
}
