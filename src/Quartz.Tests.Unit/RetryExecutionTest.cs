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

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Diagnostics;

namespace Quartz.Tests.Unit;

/// <summary>
/// A job that actually fails and is actually retried, through a real scheduler and the in-memory
/// store — the decision, the store write, the second firing and what an operator sees.
/// </summary>
/// <remarks>
/// The waits are short enough for a test to sit through, which is the only concession made: nothing
/// here fakes a clock, because the point is that the scheduling loop picks the retry up on its own.
/// </remarks>
[NonParallelizable]
public sealed class RetryExecutionTest
{
    // Spelled out rather than read from the constant the product publishes it from: the wire name is
    // what a dashboard is written against, and reading both sides from one constant would let a
    // rename pass unnoticed.
    private const string RetryCounter = "quartz.trigger.retry";

    private readonly ConcurrentBag<RetryMeasurement> measurements = [];
    private MeterListener meterListener;

    private sealed record RetryMeasurement(string Instrument, string Unit, long Value, Dictionary<string, object> Tags);

    [SetUp]
    public void SetUp()
    {
        measurements.Clear();

        meterListener = new MeterListener
        {
            InstrumentPublished = static (instrument, listener) =>
            {
                if (instrument.Meter.Name == QuartzInstrumentation.MeterName && instrument.Name == RetryCounter)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            Dictionary<string, object> copy = new(tags.Length, StringComparer.Ordinal);
            foreach (KeyValuePair<string, object> tag in tags)
            {
                copy[tag.Key] = tag.Value;
            }

            measurements.Add(new RetryMeasurement(instrument.Name, instrument.Unit, value, copy));
        });
        meterListener.Start();
    }

    [TearDown]
    public void TearDown()
    {
        meterListener?.Dispose();
    }

    /// <summary>
    /// A job that throws the first <c>failures</c> times it is asked, and records what it was told
    /// about each firing.
    /// </summary>
    private sealed class FlakyJob : IJob
    {
        internal static readonly ConcurrentQueue<(int RetryAttempt, int RefireCount, DateTimeOffset? ScheduledFireTimeUtc)> Firings = new();
        internal static readonly SemaphoreSlim Succeeded = new(0);
        internal static int RemainingFailures;

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Firings.Enqueue((context.RetryAttempt, context.RefireCount, context.ScheduledFireTimeUtc));

            if (Interlocked.Decrement(ref RemainingFailures) >= 0)
            {
                throw new InvalidOperationException("not this time");
            }

            Succeeded.Release();
            return default;
        }
    }

    [Test]
    public async Task AFailedJobIsRetriedOnItsPolicyAndTheContextSaysWhichAttemptItIs()
    {
        FlakyJob.Firings.Clear();
        FlakyJob.RemainingFailures = 2;

        string id = Guid.NewGuid().ToString("N");
        JobKey jobKey = new($"flaky-{id}", "retries");

        ServiceCollection services = new();
        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options => options.InstanceName = $"retry-{id}");
            quartz.AddJob<FlakyJob>(job => job.WithIdentity(jobKey));
            quartz.AddTrigger<FlakyJob>(trigger => trigger
                .ForJob(jobKey)
                .WithIdentity($"trigger-{id}", "retries")
                // Exactly one scheduled occurrence, so every firing after the first is a retry and
                // nothing else. A repeating trigger started with StartNow fires twice here before any
                // retry is involved, which would make the count ambiguous.
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(0))
                .StartNow()
                .WithRetryPolicy(RetryPolicy.Fixed(3, TimeSpan.FromMilliseconds(300))));
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        try
        {
            await scheduler.Start();

            (await FlakyJob.Succeeded.WaitAsync(TimeSpan.FromSeconds(30)))
                .Should().BeTrue("the job should have failed twice, been retried twice, and then succeeded");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        (int RetryAttempt, int RefireCount, DateTimeOffset? ScheduledFireTimeUtc)[] firings = [.. FlakyJob.Firings];

        firings.Should().HaveCount(3, "one regular fire and two retries");
        firings.Select(x => x.RetryAttempt).Should().Equal([0, 1, 2],
            "the context reports which attempt at this occurrence the firing is: 0 on the regular fire, n on the n-th retry");
        firings.Select(x => x.RefireCount).Should().AllBeEquivalentTo(0,
            "a retry is a fresh firing, not an iteration of the in-process refire loop, so RefireCount stays where it was");
        firings.Select(x => x.ScheduledFireTimeUtc).Distinct().Should().ContainSingle(
            "a retry is another attempt at one occurrence, so all three firings report the occurrence the schedule "
            + "called for - which is what leaving PreviousFireTimeUtc alone buys");
    }

    [Test]
    public async Task EachScheduledRetryIsCounted()
    {
        FlakyJob.Firings.Clear();
        FlakyJob.RemainingFailures = 1;

        string id = Guid.NewGuid().ToString("N");
        JobKey jobKey = new($"counted-{id}", "retries");

        ServiceCollection services = new();
        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options => options.InstanceName = $"retry-meter-{id}");
            quartz.AddJob<FlakyJob>(job => job.WithIdentity(jobKey));
            quartz.AddTrigger<FlakyJob>(trigger => trigger
                .ForJob(jobKey)
                .WithIdentity($"trigger-{id}", "retries")
                .WithExecutionGroup("heavy")
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(0))
                .StartNow()
                .WithRetryPolicy(RetryPolicy.Fixed(2, TimeSpan.FromMilliseconds(300))));
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        try
        {
            await scheduler.Start();
            (await FlakyJob.Succeeded.WaitAsync(TimeSpan.FromSeconds(30))).Should().BeTrue();
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        RetryMeasurement retry = measurements
            .Where(m => Equals(m.Tags.GetValueOrDefault("quartz.scheduler.id"), scheduler.SchedulerInstanceId))
            .Should().ContainSingle("one retry was scheduled, so one measurement was published")
            .Subject;

        retry.Value.Should().Be(1);
        retry.Unit.Should().Be("{trigger}");
        retry.Tags.Should().Contain(new KeyValuePair<string, object>("quartz.trigger.group", "retries"))
            .And.Contain(new KeyValuePair<string, object>("quartz.execution.group", "heavy"));
        retry.Tags.Should().NotContainKey("quartz.trigger.name",
            "a job limping along on its retries is a property of a group, and one series per trigger is a "
            + "cardinality no alert can be built on");
    }

    [Test]
    public async Task ATriggerWithNoPolicyIsNotRetriedAndNothingIsCounted()
    {
        FlakyJob.Firings.Clear();

        // Fails every time it is asked. Without a policy it is fired once and that is that.
        FlakyJob.RemainingFailures = int.MaxValue;

        string id = Guid.NewGuid().ToString("N");
        JobKey jobKey = new($"unpolicied-{id}", "retries");

        ServiceCollection services = new();
        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options => options.InstanceName = $"retry-none-{id}");
            quartz.AddJob<FlakyJob>(job => job.WithIdentity(jobKey));
            quartz.AddTrigger<FlakyJob>(trigger => trigger
                .ForJob(jobKey)
                .WithIdentity($"trigger-{id}", "retries")
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(0))
                .StartNow());
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        string instanceId = scheduler.SchedulerInstanceId;

        try
        {
            await scheduler.Start();

            // Long enough that a retry on any plausible policy would have fired by now.
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        FlakyJob.Firings.Should().HaveCount(1, "a trigger with no retry policy fires once and reports the failure");
        measurements.Where(m => Equals(m.Tags.GetValueOrDefault("quartz.scheduler.id"), instanceId))
            .Should().BeEmpty("nothing was retried, so nothing was counted");
    }
}
