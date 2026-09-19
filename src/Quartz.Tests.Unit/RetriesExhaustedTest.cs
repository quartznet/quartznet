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

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Diagnostics;

namespace Quartz.Tests.Unit;

/// <summary>
/// What a listener is told when a retry policy gives up, and what the execution context says while it
/// is still trying.
/// </summary>
/// <remarks>
/// Through a real scheduler and the in-memory store, as <see cref="RetryExecutionTest" /> is: the
/// notification is raised by the run shell between the two completion notifications, and the values it
/// depends on are written onto the context a few lines earlier, so nothing short of an actual firing
/// exercises the ordering.
/// </remarks>
[NonParallelizable]
public sealed class RetriesExhaustedTest
{
    // Spelled out rather than read from the constant the product publishes it from, as the retry
    // counter's name is in RetryExecutionTest: the wire name is what a dashboard is written against.
    private const string ExhaustedCounter = "quartz.trigger.retries_exhausted";

    private readonly ConcurrentBag<(long Value, string Unit, Dictionary<string, object> Tags)> measurements = [];
    private MeterListener meterListener;

    [SetUp]
    public void SetUp()
    {
        measurements.Clear();
        Recorder.Reset();

        meterListener = new MeterListener
        {
            InstrumentPublished = static (instrument, listener) =>
            {
                if (instrument.Meter.Name == QuartzInstrumentation.MeterName && instrument.Name == ExhaustedCounter)
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

            measurements.Add((value, instrument.Unit, copy));
        });
        meterListener.Start();
    }

    [TearDown]
    public void TearDown()
    {
        meterListener?.Dispose();
    }

    /// <summary>What the listener saw, one entry per completed firing and one per occurrence that gave up.</summary>
    private static class Recorder
    {
        internal static readonly ConcurrentQueue<(SchedulerInstruction Instruction, ExecutionOutcome Outcome, bool RetryScheduled, int RetryAttempt)> Completions = new();
        internal static readonly ConcurrentQueue<(TriggerKey Key, int RetryAttempt, bool RetryScheduled, ExecutionOutcome Outcome, string Message)> Exhausted = new();
        internal static readonly SemaphoreSlim GaveUp = new(0);
        internal static readonly SemaphoreSlim Succeeded = new(0);

        internal static void Reset()
        {
            Completions.Clear();
            Exhausted.Clear();
            while (GaveUp.CurrentCount > 0)
            {
                GaveUp.Wait(0);
            }

            while (Succeeded.CurrentCount > 0)
            {
                Succeeded.Wait(0);
            }
        }
    }

    private sealed class RecordingListener : ITriggerListener
    {
        public ValueTask TriggerComplete(
            ITrigger trigger,
            IJobExecutionContext context,
            SchedulerInstruction triggerInstructionCode,
            CancellationToken cancellationToken = default)
        {
            Recorder.Completions.Enqueue((triggerInstructionCode, context.Outcome, context.RetryScheduled, context.RetryAttempt));
            return default;
        }

        public ValueTask TriggerRetriesExhausted(
            ITrigger trigger,
            IJobExecutionContext context,
            JobExecutionException exception,
            CancellationToken cancellationToken = default)
        {
            // The base exception, because the shell wraps anything that is not already a
            // JobExecutionException - so what the job actually threw is at the bottom of the chain.
            Recorder.Exhausted.Enqueue((trigger.Key, context.RetryAttempt, context.RetryScheduled, context.Outcome, exception.GetBaseException().Message));
            Recorder.GaveUp.Release();
            return default;
        }
    }

    /// <summary>A job that throws the first <c>RemainingFailures</c> times it is asked.</summary>
    private sealed class FlakyJob : IJob
    {
        internal static int RemainingFailures;

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Decrement(ref RemainingFailures) >= 0)
            {
                throw new InvalidOperationException("the upstream system is down");
            }

            Recorder.Succeeded.Release();
            return default;
        }
    }

    /// <summary>
    /// One scheduler, one job, one occurrence, and whatever retry policy the case wants.
    /// </summary>
    private static ServiceProvider BuildScheduler(string id, RetryPolicy policy)
    {
        JobKey jobKey = new($"flaky-{id}", "retries");

        ServiceCollection services = new();
        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options => options.InstanceName = $"exhausted-{id}");
            quartz.AddTriggerListener<RecordingListener>(new RecordingListener(), Quartz.Matchers.AllTriggers());
            quartz.AddJob<FlakyJob>(job => job.WithIdentity(jobKey));
            quartz.AddTrigger<FlakyJob>(trigger =>
            {
                trigger
                    .ForJob(jobKey)
                    .WithIdentity($"trigger-{id}", "retries")
                    .WithExecutionGroup("heavy")
                    // Exactly one scheduled occurrence, so every firing after the first is a retry.
                    .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(0))
                    .StartNow();

                if (policy is not null)
                {
                    trigger.WithRetryPolicy(policy);
                }
            });
        });

        return services.BuildServiceProvider();
    }

    [Test]
    public async Task AnOccurrenceThatSpendsItsAttemptsSaysSoOnceAndTheContextSaysWhichFiringWasTheLast()
    {
        // Fails every time it is asked, so the policy is spent and the occurrence gives up.
        FlakyJob.RemainingFailures = int.MaxValue;

        string id = Guid.NewGuid().ToString("N");
        await using ServiceProvider provider = BuildScheduler(id, RetryPolicy.Fixed(2, TimeSpan.FromMilliseconds(200)));
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        try
        {
            await scheduler.Start();
            (await Recorder.GaveUp.WaitAsync(TimeSpan.FromSeconds(30)))
                .Should().BeTrue("the job fails every time, so the two retries are spent and the occurrence gives up");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        (SchedulerInstruction Instruction, ExecutionOutcome Outcome, bool RetryScheduled, int RetryAttempt)[] completions = [.. Recorder.Completions];

        completions.Should().HaveCount(3, "one regular fire and two retries, each of which completes");
        completions.Select(x => x.Outcome).Should().AllBeEquivalentTo(ExecutionOutcome.Failed,
            "the job ran and threw every time, and an outcome says what the firing did rather than what the schedule makes of it");
        completions.Select(x => x.RetryScheduled).Should().Equal([true, true, false],
            "the first two firings were answered with another attempt and the third was not - which is the whole "
            + "of what a listener could not tell before without knowing the trigger's policy");
        completions.Select(x => x.RetryAttempt).Should().Equal([0, 1, 2],
            "the context reports the attempt the job just made, not the one the trigger is about to make");
        completions.Select(x => x.Instruction).Take(2).Should().AllBeEquivalentTo(SchedulerInstruction.RetryTrigger);

        (TriggerKey Key, int RetryAttempt, bool RetryScheduled, ExecutionOutcome Outcome, string Message)[] exhausted = [.. Recorder.Exhausted];

        exhausted.Should().ContainSingle("the occurrence gave up once, however many attempts it took to get there")
            .Which.Should().Match<(TriggerKey Key, int RetryAttempt, bool RetryScheduled, ExecutionOutcome Outcome, string Message)>(
                x => x.Key.Group == "retries"
                     && x.RetryAttempt == 2
                     && !x.RetryScheduled
                     && x.Outcome == ExecutionOutcome.Failed
                     && x.Message.Contains("the upstream system is down"),
                "the notification carries the last attempt's context and the exception it threw");

        measurements.Where(m => Equals(m.Tags.GetValueOrDefault("quartz.scheduler.id"), scheduler.SchedulerInstanceId))
            .Should().ContainSingle("one occurrence gave up, so one measurement was published")
            .Which.Should().Match<(long Value, string Unit, Dictionary<string, object> Tags)>(
                m => m.Value == 1
                     && m.Unit == "{trigger}"
                     && Equals(m.Tags["quartz.trigger.group"], "retries")
                     && Equals(m.Tags["quartz.execution.group"], "heavy"));
    }

    [Test]
    public async Task AnOccurrenceThatSucceedsOnARetryAnnouncesNothing()
    {
        FlakyJob.RemainingFailures = 1;

        string id = Guid.NewGuid().ToString("N");
        await using ServiceProvider provider = BuildScheduler(id, RetryPolicy.Fixed(3, TimeSpan.FromMilliseconds(200)));
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        string instanceId = scheduler.SchedulerInstanceId;

        try
        {
            await scheduler.Start();
            (await Recorder.Succeeded.WaitAsync(TimeSpan.FromSeconds(30)))
                .Should().BeTrue("the job fails once, is retried, and then works");

            // Long enough that a second retry, had one been scheduled, would have fired.
            await Task.Delay(TimeSpan.FromMilliseconds(800));
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        Recorder.Exhausted.Should().BeEmpty("nothing gave up - the occurrence finished, late but successfully");
        Recorder.Completions.Should().Contain(x => x.Outcome == ExecutionOutcome.Succeeded && !x.RetryScheduled,
            "the firing that worked reports success and no further attempt");
        measurements.Where(m => Equals(m.Tags.GetValueOrDefault("quartz.scheduler.id"), instanceId))
            .Should().BeEmpty("nothing gave up, so nothing was counted");
    }

    [Test]
    public async Task AFailureOnATriggerWithNoPolicyAnnouncesNothing()
    {
        FlakyJob.RemainingFailures = int.MaxValue;

        string id = Guid.NewGuid().ToString("N");
        await using ServiceProvider provider = BuildScheduler(id, policy: null);
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        string instanceId = scheduler.SchedulerInstanceId;

        try
        {
            await scheduler.Start();
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        Recorder.Completions.Should().ContainSingle("a trigger with no retry policy fires once and reports the failure")
            .Which.Should().Match<(SchedulerInstruction Instruction, ExecutionOutcome Outcome, bool RetryScheduled, int RetryAttempt)>(
                x => x.Outcome == ExecutionOutcome.Failed && !x.RetryScheduled);

        Recorder.Exhausted.Should().BeEmpty(
            "nothing gave up, because nothing was ever going to try again - announcing every plain failure as "
            + "'retries exhausted' would make the notification meaningless");
        measurements.Where(m => Equals(m.Tags.GetValueOrDefault("quartz.scheduler.id"), instanceId))
            .Should().BeEmpty();
    }

    [Test]
    public async Task AListenerThatDoesNotOverrideTheNotificationIsAnsweredByTheInterface()
    {
        // The fake implements ITriggerListener without writing a body for the new member, which is what
        // every listener written against 4.0 or 4.1 is. CallsBaseMethod runs the interface's own default
        // implementation rather than the fake's no-op, so this is the default body itself.
        ITriggerListener listener = A.Fake<ITriggerListener>();
        A.CallTo(() => listener.TriggerRetriesExhausted(A<ITrigger>._, A<IJobExecutionContext>._, A<JobExecutionException>._, A<CancellationToken>._))
            .CallsBaseMethod();

        Func<Task> act = async () => await listener.TriggerRetriesExhausted(
            A.Fake<ITrigger>(),
            A.Fake<IJobExecutionContext>(),
            new JobExecutionException("boom"));

        await act.Should().NotThrowAsync(
            "the default implementation does nothing, so a listener that predates the member keeps working");
    }

    [Test]
    public void AContextThatDoesNotAnswerTheNewMembersReportsAFiringThatWentFine()
    {
        // The same for the two context members: a context implemented outside this repository - or a
        // fake in somebody's test - answers the interface's defaults rather than failing to compile.
        IJobExecutionContext context = A.Fake<IJobExecutionContext>();
        A.CallTo(() => context.Outcome).CallsBaseMethod();
        A.CallTo(() => context.RetryScheduled).CallsBaseMethod();

        context.Outcome.Should().Be(ExecutionOutcome.Succeeded, "nothing has gone wrong until the scheduler says so");
        context.RetryScheduled.Should().BeFalse();
    }
}
