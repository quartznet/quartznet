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

using Microsoft.Extensions.Time.Testing;

using Quartz.Documentation.Samples.Tutorial;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// The random-delay middleware the job-execution-middleware page shows, invoked directly on a fake
/// clock: a job that asks for a delay waits for exactly the draw the random source makes and then runs,
/// one that does not ask runs at once, and one interrupted while it waits never runs.
/// </summary>
public sealed class StartJitterMiddlewareSampleTest
{
    private const int Seed = 4417;
    private const int MaxSeconds = 30;

    [Test]
    public async Task AJobThatAsksForADelayRunsOnceTheDrawnDelayHasPassed()
    {
        FakeTimeProvider clock = new FakeTimeProvider();
        TimeSpan expected = TimeSpan.FromSeconds(new Random(Seed).NextDouble() * MaxSeconds);
        StartJitterMiddleware middleware = new StartJitterMiddleware(clock, new Random(Seed));
        Counter job = new Counter();

        using JobExecutionContextImpl firing = Firing(MaxSeconds);
        Task waiting = middleware.Invoke(firing, job.Run, firing.CancellationToken).AsTask();

        // Task.Delay arms its timer in whole milliseconds, so the margin either side is two of them.
        TimeSpan margin = TimeSpan.FromMilliseconds(2);
        clock.Advance(expected - margin);

        waiting.IsCompleted.Should().BeFalse("the clock is just short of the delay the random source drew");
        job.Runs.Should().Be(0, "a firing that is still waiting has not called the job yet");

        clock.Advance(margin * 2);
        await waiting.WaitAsync(TimeSpan.FromSeconds(30));

        job.Runs.Should().Be(1, "the job runs once, as soon as the delay is over");
    }

    [Test]
    public async Task AJobThatDoesNotAskForADelayRunsAtOnce()
    {
        FakeTimeProvider clock = new FakeTimeProvider();
        StartJitterMiddleware middleware = new StartJitterMiddleware(clock, new Random(Seed));
        Counter job = new Counter();

        using JobExecutionContextImpl firing = Firing(maxDelaySeconds: null);
        await middleware.Invoke(firing, job.Run, firing.CancellationToken);

        job.Runs.Should().Be(1, "without the key there is nothing to wait for, and the clock never moved");
    }

    [Test]
    public async Task AMaximumOfZeroRunsAtOnce()
    {
        FakeTimeProvider clock = new FakeTimeProvider();
        StartJitterMiddleware middleware = new StartJitterMiddleware(clock, new Random(Seed));
        Counter job = new Counter();

        using JobExecutionContextImpl firing = Firing(maxDelaySeconds: 0);
        await middleware.Invoke(firing, job.Run, firing.CancellationToken);

        job.Runs.Should().Be(1, "a maximum that is not above zero means no delay, and the clock never moved");
    }

    [Test]
    public async Task AJobInterruptedWhileItWaitsNeverRuns()
    {
        FakeTimeProvider clock = new FakeTimeProvider();
        StartJitterMiddleware middleware = new StartJitterMiddleware(clock, new Random(Seed));
        Counter job = new Counter();

        using CancellationTokenSource interrupt = new CancellationTokenSource();
        using JobExecutionContextImpl firing = Firing(MaxSeconds);
        Task waiting = middleware.Invoke(firing, job.Run, interrupt.Token).AsTask();

        await interrupt.CancelAsync();

        Func<Task> act = () => waiting;
        await act.Should().ThrowAsync<OperationCanceledException>(
            "the wait is on the firing's token, so an interrupt ends it, and the run shell reports that as a cancelled firing");
        job.Runs.Should().Be(0, "a firing interrupted while it waited never reaches the job");
    }

    private static JobExecutionContextImpl Firing(int? maxDelaySeconds)
    {
        JobBuilder<NoOpJob> jobBuilder = JobBuilder.Create<NoOpJob>().WithIdentity("report");
        if (maxDelaySeconds is { } seconds)
        {
            jobBuilder.UsingJobData(StartJitter.MaxDelaySeconds, seconds);
        }

        ITrigger trigger = TriggerBuilder.Create()
            .ForJob("report")
            .StartNow()
            .Build();

        return JobExecutionContextBuilder.For(new NoOpJob())
            .WithJob(jobBuilder.Build())
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
