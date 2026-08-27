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

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// Whether a shutting-down scheduler signals cancellation to the jobs still running, in each of the
/// four settings and on both kinds of shutdown.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ShutdownJobInterruption" /> exists because interrupting running jobs is a reasonable
/// thing to want on a shutdown that waits for them, on one that does not, on both, or on neither. Only
/// the mapping from configuration keys to the enum was tested; what the scheduler then does with it was
/// not, so three of the four values could have meant anything.
/// </para>
/// <para>
/// The interrupt happens between "the scheduler listeners were told it is shutting down" and "the
/// thread pool was torn down", and neither of those is a length of time. The pool under test says when
/// the shutdown has reached it, which is strictly after the interrupt decision was made and acted on —
/// so the case that expects no interruption can assert the absence of one instead of waiting to see
/// whether it turns up.
/// </para>
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class ShutdownJobInterruptionTest
{
    private const string Group = "shutdown-interruption";

    /// <summary>
    /// How long a test is willing to wait for the shutdown to reach the thread pool. Long enough that a
    /// loaded build agent never trips it, and never used as a measurement.
    /// </summary>
    private static readonly TimeSpan observationDeadline = TimeSpan.FromSeconds(30);

    [TestCase(ShutdownJobInterruption.Never, false, false)]
    [TestCase(ShutdownJobInterruption.Never, true, false)]
    [TestCase(ShutdownJobInterruption.WhenNotWaitingForJobs, false, true)]
    [TestCase(ShutdownJobInterruption.WhenNotWaitingForJobs, true, false)]
    [TestCase(ShutdownJobInterruption.WhenWaitingForJobs, false, false)]
    [TestCase(ShutdownJobInterruption.WhenWaitingForJobs, true, true)]
    [TestCase(ShutdownJobInterruption.Always, false, true)]
    [TestCase(ShutdownJobInterruption.Always, true, true)]
    public async Task TheSettingDecidesWhetherAShutdownCancelsTheRunningJobs(
        ShutdownJobInterruption setting,
        bool waitForJobsToComplete,
        bool expectedToBeInterrupted)
    {
        RunningJob.Gate gate = new();
        TeardownAnnouncingThreadPool pool = new();

        IScheduler scheduler = await QuartzSchedulerBuilder.Create()
            .ConfigureScheduler(options =>
            {
                options.InstanceName = $"{Group}-{setting}-{waitForJobsToComplete}";
                options.ShutdownJobInterruption = setting;
            })
            .UseThreadPool(pool)
            .BuildScheduler();

        IJobDetail job = JobBuilder.Create<RunningJob>()
            .WithIdentity("job", Group)
            .UsingJobData(new JobDataMap { [RunningJob.Gate.JobDataKey] = gate })
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("trigger", Group)
            .ForJob(job)
            .StartNow()
            .Build();

        await scheduler.ScheduleJob(job, trigger);
        await scheduler.Start();

        await ShouldObserve(gate.Started, "the job has to be running before a shutdown can interrupt it");

        Task shutdown = scheduler.Shutdown(waitForJobsToComplete).AsTask();

        // The pool is torn down after the interruption decision has been made and acted on, so this is
        // the first moment at which "it was not interrupted" is a fact rather than a guess.
        await ShouldObserve(pool.TeardownReached, "the shutdown has to reach the thread pool");

        gate.Interrupted.IsCompleted.Should().Be(expectedToBeInterrupted,
            expectedToBeInterrupted
                ? $"{setting} interrupts running jobs on a shutdown with waitForJobsToComplete: {waitForJobsToComplete}"
                : $"{setting} leaves running jobs alone on a shutdown with waitForJobsToComplete: {waitForJobsToComplete}");

        gate.Open();

        // Waited for in its own right rather than through the shutdown: a shutdown that does not wait
        // for its jobs returns while this one is still finishing, so awaiting only the shutdown would
        // read what the job saw before the job had looked.
        await ShouldObserve(gate.Finished, "the job has to end before what it saw can be read");
        await ShouldObserve(shutdown, "the shutdown has to finish once the job it may be waiting for has ended");

        gate.SawCancellationRequested.Should().Be(expectedToBeInterrupted,
            "what the job reads from its own token has to agree with what the scheduler did to it");
    }

    private static async Task ShouldObserve(Task observation, string because)
    {
        Func<Task> act = () => observation;
        await act.Should().CompleteWithinAsync(observationDeadline, because);
    }

    /// <summary>
    /// A job that runs until the test lets it stop, and says whether anything cancelled it meanwhile.
    /// </summary>
    public sealed class RunningJob : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Gate gate = (Gate) context.MergedJobDataMap[Gate.JobDataKey];

            // Registered rather than polled: the callback runs inside Cancel(), so by the time the
            // scheduler's interrupt call has returned the test can already see it.
            await using CancellationTokenRegistration registration = cancellationToken.Register(gate.RecordInterrupt);

            gate.RecordStarted();
            await gate.Released.ConfigureAwait(false);

            gate.SawCancellationRequested = cancellationToken.IsCancellationRequested;
            gate.RecordFinished();
        }

        /// <summary>
        /// The signals one firing exchanges with the test, handed to it through its data map so that
        /// nothing here is static and no two cases can see each other's firing.
        /// </summary>
        public sealed class Gate
        {
            public const string JobDataKey = "gate";

            private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource interrupted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task Started => started.Task;

            public Task Interrupted => interrupted.Task;

            public Task Released => release.Task;

            /// <summary>
            /// Completes once the job has read its token and is on its way out, which is the only point
            /// at which <see cref="SawCancellationRequested" /> means anything.
            /// </summary>
            public Task Finished => finished.Task;

            /// <summary>What the job read from its own token, once it was let go.</summary>
            public bool SawCancellationRequested { get; set; }

            public void RecordStarted() => started.TrySetResult();

            public void RecordInterrupt() => interrupted.TrySetResult();

            public void RecordFinished() => finished.TrySetResult();

            public void Open() => release.TrySetResult();
        }
    }

    /// <summary>
    /// The default pool, plus a signal for the moment the shutdown reaches it.
    /// </summary>
    /// <remarks>
    /// Both of the pool's teardown members are strictly after the block in <c>QuartzScheduler.Shutdown</c>
    /// that decides whether to interrupt: a shutdown that waits calls <see cref="Drain" />, one that does
    /// not calls <see cref="Shutdown" />, and either way the interrupting is over by then.
    /// </remarks>
    private sealed class TeardownAnnouncingThreadPool : IThreadPool
    {
        private readonly DefaultThreadPool inner = new() { MaxConcurrency = 5 };
        private readonly TaskCompletionSource teardownReached = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task TeardownReached => teardownReached.Task;

        public int PoolSize => inner.PoolSize;

        public ValueTask Initialize(CancellationToken cancellationToken = default) => inner.Initialize(cancellationToken);

        public ValueTask<int> WaitForAvailableThreads(CancellationToken cancellationToken = default)
        {
            return inner.WaitForAvailableThreads(cancellationToken);
        }

        public ValueTask<bool> TryRun(Func<ValueTask> action, CancellationToken cancellationToken = default)
        {
            return inner.TryRun(action, cancellationToken);
        }

        public ValueTask Shutdown(bool waitForJobsToComplete = true, CancellationToken cancellationToken = default)
        {
            teardownReached.TrySetResult();
            return inner.Shutdown(waitForJobsToComplete, cancellationToken);
        }

        public ValueTask<bool> Drain(CancellationToken cancellationToken = default)
        {
            teardownReached.TrySetResult();
            return inner.Drain(cancellationToken);
        }
    }
}
