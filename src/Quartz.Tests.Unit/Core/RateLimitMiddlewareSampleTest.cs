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

using System.Diagnostics;
using System.Threading.RateLimiting;

using Quartz.Documentation.Samples.Tutorial;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// The rate-limiting middleware the execution-groups page shows, invoked directly: a firing over the
/// limit waits for a permit and then runs, and one whose wait ends early never runs the job.
/// </summary>
/// <remarks>
/// <c>System.Threading.RateLimiting</c> reads the real clock and takes no <see cref="TimeProvider" />,
/// so the window is a real second, and the test that waits for it sits through it once.
/// </remarks>
public sealed class RateLimitMiddlewareSampleTest
{
    private static readonly TimeSpan window = TimeSpan.FromSeconds(1);

    [Test]
    public async Task AFiringOverTheLimitWaitsForAPermitAndThenRuns()
    {
        await using PartitionedRateLimiter<IJobExecutionContext> limiter = OnePerWindow(queueLimit: 1);
        RateLimitMiddleware middleware = new RateLimitMiddleware(limiter);
        Counter job = new Counter();

        Stopwatch stopwatch = Stopwatch.StartNew();
        using JobExecutionContextImpl first = Firing();
        await middleware.Invoke(first, job.Run, first.CancellationToken);

        using JobExecutionContextImpl second = Firing();
        Task waiting = middleware.Invoke(second, job.Run, second.CancellationToken).AsTask();

        waiting.IsCompleted.Should().BeFalse("the group's one permit for this window went to the first firing, so the second has to wait for the next");
        job.Runs.Should().Be(1, "a firing that is waiting for a permit has not called the job yet");

        await waiting.WaitAsync(TimeSpan.FromSeconds(30));

        job.Runs.Should().Be(2, "the waiting firing runs once the window gives its permit back, rather than being dropped");
        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(window / 2, "the permit comes back when the window moves on, not straight away");
    }

    [Test]
    public async Task AFiringInterruptedWhileItWaitsNeverRunsTheJob()
    {
        await using PartitionedRateLimiter<IJobExecutionContext> limiter = OnePerWindow(queueLimit: 1);
        RateLimitMiddleware middleware = new RateLimitMiddleware(limiter);
        Counter job = new Counter();

        using JobExecutionContextImpl first = Firing();
        await middleware.Invoke(first, job.Run, first.CancellationToken);

        using CancellationTokenSource interrupt = new CancellationTokenSource();
        using JobExecutionContextImpl second = Firing();
        Task waiting = middleware.Invoke(second, job.Run, interrupt.Token).AsTask();

        await interrupt.CancelAsync();

        Func<Task> act = () => waiting;
        await act.Should().ThrowAsync<OperationCanceledException>(
            "the wait is on the firing's token, so an interrupt ends it, and the run shell reports that as a cancelled firing");
        job.Runs.Should().Be(1, "a firing interrupted while it waited never reaches the job");
    }

    [Test]
    public async Task AFiringTheQueueHasNoRoomForFailsWithoutRunningTheJob()
    {
        await using PartitionedRateLimiter<IJobExecutionContext> limiter = OnePerWindow(queueLimit: 0);
        RateLimitMiddleware middleware = new RateLimitMiddleware(limiter);
        Counter job = new Counter();

        using JobExecutionContextImpl first = Firing();
        await middleware.Invoke(first, job.Run, first.CancellationToken);

        using JobExecutionContextImpl second = Firing();
        Func<Task> act = () => middleware.Invoke(second, job.Run, second.CancellationToken).AsTask();

        await act.Should().ThrowAsync<JobExecutionException>(
                "a lease the limiter refuses is a failure, so the trigger's retry policy decides what follows")
            .WithMessage("*tenant:acme*");
        job.Runs.Should().Be(1, "a refused firing never reaches the job");
    }

    /// <summary>
    /// One start per <see cref="window" /> for each execution group, the partitioning the page's
    /// registration uses.
    /// </summary>
    private static PartitionedRateLimiter<IJobExecutionContext> OnePerWindow(int queueLimit)
    {
        return PartitionedRateLimiter.Create<IJobExecutionContext, string>(context =>
            RateLimitPartition.GetSlidingWindowLimiter(context.Trigger.ExecutionGroup ?? string.Empty, _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 1,
                Window = window,
                SegmentsPerWindow = 1,
                QueueLimit = queueLimit,
            }));
    }

    private static JobExecutionContextImpl Firing()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .ForJob("tenant-report")
            .WithExecutionGroup("tenant:acme")
            .StartNow()
            .Build();

        return JobExecutionContextBuilder.For(new NoOpJob())
            .WithTrigger(trigger)
            .Build();
    }

    /// <summary>
    /// Stands for the rest of the pipeline and the job at its end, and counts how often it was reached.
    /// </summary>
    private sealed class Counter
    {
        private int runs;

        public int Runs => Volatile.Read(ref runs);

        public ValueTask Run(IJobExecutionContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref runs);
            return default;
        }
    }

    private sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
