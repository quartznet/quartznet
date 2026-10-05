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
using Microsoft.Extensions.Logging;

using Quartz.Core;
using Quartz.Diagnostics;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit;

/// <summary>
/// An occurrence that gives up is reported by the policy the trigger actually applied to it, through a
/// real scheduler and the in-memory store.
/// </summary>
/// <remarks>
/// <para>
/// Only <c>TriggerBase.ExecutionComplete</c> applies a job type's or the scheduler's policy. A trigger
/// that decides its completions some other way never retries under one, so it must not be told it ran
/// out of one either — and <c>PauseTriggerWhenRetriesExhausted</c> must not pause it on its first
/// failure.
/// </para>
/// <para>
/// An inherited policy whose retry has no room before the trigger's next occurrence has not run out of
/// anything: the occurrence settles quietly. A trigger's own policy keeps announcing it.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class RetryPolicyAppliedTest
{
    private const string ExhaustedCounter = "quartz.trigger.retries_exhausted";

    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan settle = TimeSpan.FromMilliseconds(1500);

    private static readonly SemaphoreSlim fired = new(0);
    private static int firings;

    private readonly List<ServiceProvider> providers = [];
    private readonly ConcurrentBag<string> exhaustedFor = [];
    private MeterListener meterListener;

    [SetUp]
    public void SetUp()
    {
        firings = 0;
        while (fired.CurrentCount > 0)
        {
            fired.Wait(0);
        }

        exhaustedFor.Clear();
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
        meterListener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (KeyValuePair<string, object> tag in tags)
            {
                if (tag.Key == "quartz.scheduler.id")
                {
                    exhaustedFor.Add((string) tag.Value);
                }
            }
        });
        meterListener.Start();
    }

    [TearDown]
    public async Task TearDown()
    {
        meterListener?.Dispose();
        foreach (ServiceProvider provider in providers)
        {
            await provider.DisposeAsync();
        }

        providers.Clear();
    }

    [Test]
    public async Task ATriggerOfSomebodyElsesIsNotToldItRanOutOfAPolicyItNeverApplied()
    {
        TriggerKey key = new("foreign", "applied");
        ForeignTrigger trigger = new(Hourly(key));

        (IScheduler scheduler, ExhaustedListener listener, _) = await Start(trigger);

        await WaitForFirings(1);
        await Task.Delay(settle);

        firings.Should().Be(1, "a trigger that does not derive from TriggerBase does not retry under the scheduler's default");
        listener.Heard.Should().BeEmpty("a policy the trigger never applied cannot have run out");
        exhaustedFor.Should().NotContain(scheduler.SchedulerInstanceId, "nothing ran out, so nothing is counted");
        (await scheduler.GetTriggerState(key)).Should().Be(TriggerState.Normal,
            "PauseTriggerWhenRetriesExhausted pauses a trigger whose retries ran out, and this one had none to run out of");

        await scheduler.Shutdown(waitForJobsToComplete: true);
    }

    [Test]
    public async Task ATriggerBaseSubclassThatDecidesItsOwnCompletionsIsNotToldEither()
    {
        TriggerKey key = new("self-deciding", "applied");
        SelfDecidingTrigger trigger = new()
        {
            Key = key,
            JobKey = JobKey,
            StartTimeUtc = TimeProvider.System.GetUtcNow(),
            RepeatInterval = TimeSpan.FromHours(1),
            RepeatCount = SimpleTriggerImpl.RepeatIndefinitely
        };

        (IScheduler scheduler, ExhaustedListener listener, _) = await Start(trigger);

        await WaitForFirings(1);
        await Task.Delay(settle);

        firings.Should().Be(1);
        listener.Heard.Should().BeEmpty("the subclass overrides ExecutionComplete without the base, so it applied no inherited policy");
        exhaustedFor.Should().NotContain(scheduler.SchedulerInstanceId);
        (await scheduler.GetTriggerState(key)).Should().Be(TriggerState.Normal);

        await scheduler.Shutdown(waitForJobsToComplete: true);
    }

    [Test]
    public async Task AnInheritedPolicyWithNoRoomBeforeTheNextOccurrenceSettlesQuietly()
    {
        TriggerKey key = new("inherited-no-room", "applied");

        // Every two seconds, under a default whose one retry waits thirty: no retry ever fits.
        (IScheduler scheduler, ExhaustedListener listener, RecordingLoggerProvider log) = await Start(EveryTwoSeconds(key, ownPolicy: null));

        await WaitForFirings(2);

        listener.Heard.Should().BeEmpty(
            "the scheduler's default was supplied without knowing this schedule, so a retry with no room is not a policy running out");
        exhaustedFor.Should().NotContain(scheduler.SchedulerInstanceId);
        (await scheduler.GetTriggerState(key)).Should().Be(TriggerState.Normal,
            "nothing ran out, so PauseTriggerWhenRetriesExhausted has nothing to pause, and the trigger fired again");
        log.EventIds.Should().Contain(1062, "the quiet settlement is still logged, at Debug").And.NotContain(1057);

        await scheduler.Shutdown(waitForJobsToComplete: true);
    }

    [Test]
    public async Task ATriggersOwnPolicyWithNoRoomBeforeTheNextOccurrenceIsStillReported()
    {
        TriggerKey key = new("own-no-room", "applied");

        (IScheduler scheduler, ExhaustedListener listener, RecordingLoggerProvider log) = await Start(EveryTwoSeconds(key, ownPolicy: Policy));

        await WaitForFirings(1);
        await listener.WaitForOne();
        await Task.Delay(settle);

        listener.Heard.Should().ContainSingle("a trigger's own policy was chosen for its schedule, and its giving up is reported as before")
            .Which.Should().Be(key);
        exhaustedFor.Should().Contain(scheduler.SchedulerInstanceId);
        (await scheduler.GetTriggerState(key)).Should().Be(TriggerState.Paused, "PauseTriggerWhenRetriesExhausted paused it");
        log.EventIds.Should().Contain(1057).And.NotContain(1062);

        await scheduler.Shutdown(waitForJobsToComplete: true);
    }

    [Test]
    public void TheAppliedPolicyOfATriggerOfSomebodyElsesIsItsOwn()
    {
        RetryPolicy own = RetryPolicy.Fixed(1, TimeSpan.FromSeconds(1));
        IOperableTrigger trigger = A.Fake<IOperableTrigger>();

        A.CallTo(() => trigger.RetryPolicy).Returns(own);
        JobRunShell.AppliedRetryPolicy(trigger).Should().Be(own);

        A.CallTo(() => trigger.RetryPolicy).Returns(RetryPolicy.None);
        JobRunShell.AppliedRetryPolicy(trigger).Should().BeNull("None is never a policy applied to a failure");

        A.CallTo(() => trigger.RetryPolicy).Returns(null);
        JobRunShell.AppliedRetryPolicy(trigger).Should().BeNull("a trigger of somebody else's applies no inherited policy");
    }

    [Test]
    public void TheContextOfATriggerOfSomebodyElsesClaimsNoInheritedPolicy()
    {
        IOperableTrigger trigger = A.Fake<IOperableTrigger>();
        A.CallTo(() => trigger.RetryPolicy).Returns(null);

        JobExecutionContextImpl context = new(null, Bundle(trigger, JobBuilder.Create<DeclaringJob>().WithIdentity(JobKey).Build()), null)
        {
            SchedulerDefaultRetryPolicy = Policy
        };

        context.RetryPolicy.Should().BeNull(
            "only TriggerBase.ExecutionComplete applies the job type's policy or the scheduler's default, so the context does not claim either for a trigger that does not");
    }

    [Test]
    public void TheContextOfABuiltInTriggerAnswersWithTheSchedulersDefault()
    {
        IOperableTrigger trigger = Hourly(new TriggerKey("built-in", "applied"));

        JobExecutionContextImpl context = new(null, Bundle(trigger, JobBuilder.Create<PlainJob>().WithIdentity(JobKey).Build()), null)
        {
            SchedulerDefaultRetryPolicy = Policy
        };

        context.RetryPolicy.Should().Be(Policy, "the run shell hands the scheduler's default to the context as it builds it");
    }

    private static readonly JobKey JobKey = new("applied-job", "applied");

    private static readonly RetryPolicy Policy = RetryPolicy.Fixed(1, TimeSpan.FromSeconds(30));

    private static SimpleTriggerImpl Hourly(TriggerKey key)
    {
        return (SimpleTriggerImpl) TriggerBuilder.Create()
            .WithIdentity(key)
            .ForJob(JobKey)
            .StartNow()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();
    }

    private static ITrigger EveryTwoSeconds(TriggerKey key, RetryPolicy ownPolicy)
    {
        return TriggerBuilder.Create()
            .WithIdentity(key)
            .ForJob(JobKey)
            .StartNow()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(2)).RepeatForever())
            .WithRetryPolicy(ownPolicy)
            .Build();
    }

    private static TriggerFiredBundle Bundle(IOperableTrigger trigger, IJobDetail job) => new()
    {
        JobDetail = job,
        Trigger = trigger,
        Recovering = false,
        FireTimeUtc = TimeProvider.System.GetUtcNow(),
        ScheduledFireTimeUtc = null,
        PreviousFireTimeUtc = null,
        NextFireTimeUtc = null,
    };

    private async Task<(IScheduler Scheduler, ExhaustedListener Listener, RecordingLoggerProvider Log)> Start(ITrigger trigger)
    {
        ExhaustedListener listener = new();
        RecordingLoggerProvider log = new();

        ServiceCollection services = new();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Debug).AddProvider(log));
        services.AddQuartz(q => q
            .ConfigureScheduler(options => options.InstanceName = "retry-applied-" + Guid.NewGuid().ToString("N"))
            .UseInMemoryStore()
            .UseDefaultRetryPolicy(Policy)
            .PauseTriggerWhenRetriesExhausted()
            .AddTriggerListener(listener));

        ServiceProvider provider = services.BuildServiceProvider();
        providers.Add(provider);

        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.ScheduleJob(JobBuilder.Create<PlainJob>().WithIdentity(JobKey).Build(), trigger);
        await scheduler.Start();
        return (scheduler, listener, log);
    }

    private static async Task WaitForFirings(int count)
    {
        for (int i = 0; i < count; i++)
        {
            (await fired.WaitAsync(waitLimit)).Should().BeTrue("firing {0} of {1} should have happened by now", i + 1, count);
        }
    }

    public sealed class PlainJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref firings);
            fired.Release();
            throw new InvalidOperationException("the upstream system is down");
        }
    }

    [RetryPolicy(3, "00:00:00.250")]
    private sealed class DeclaringJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// A built-in trigger whose subclass decides its completions itself, without the base.
    /// </summary>
    private sealed class SelfDecidingTrigger : SimpleTriggerImpl
    {
        public override SchedulerInstruction ExecutionComplete(IJobExecutionContext context, JobExecutionException result)
        {
            return MayFireAgain ? SchedulerInstruction.NoInstruction : SchedulerInstruction.DeleteTrigger;
        }
    }

    /// <summary>
    /// A trigger that does not derive from <see cref="TriggerBase" />: an hourly simple schedule underneath,
    /// and completions it decides itself, with no retries.
    /// </summary>
    private sealed class ForeignTrigger(SimpleTriggerImpl inner) : IOperableTrigger
    {
        public TriggerKey Key { get => inner.Key; set => inner.Key = value; }
        public JobKey JobKey { get => inner.JobKey; set => inner.JobKey = value; }
        public string Description { get => inner.Description; set => inner.Description = value; }
        public string ExecutionGroup { get => inner.ExecutionGroup; set => inner.ExecutionGroup = value; }
        public PreferredNode PreferredNode { get => inner.PreferredNode; set => inner.PreferredNode = value; }
        public RetryPolicy RetryPolicy { get => inner.RetryPolicy; set => inner.RetryPolicy = value; }
        public int RetryAttempt { get => inner.RetryAttempt; set => inner.RetryAttempt = value; }
        public string CalendarName { get => inner.CalendarName; set => inner.CalendarName = value; }
        public JobDataMap JobDataMap { get => inner.JobDataMap; set => inner.JobDataMap = value; }
        public DateTimeOffset? FinalFireTimeUtc => inner.FinalFireTimeUtc;
        public int MisfireInstructionCode { get => inner.MisfireInstructionCode; set => inner.MisfireInstructionCode = value; }
        public DateTimeOffset? EndTimeUtc { get => inner.EndTimeUtc; set => inner.EndTimeUtc = value; }
        public DateTimeOffset StartTimeUtc { get => inner.StartTimeUtc; set => inner.StartTimeUtc = value; }
        public int Priority { get => inner.Priority; set => inner.Priority = value; }
        public bool MayFireAgain => inner.MayFireAgain;
        public DateTimeOffset? NextFireTimeUtc { get => inner.NextFireTimeUtc; set => inner.NextFireTimeUtc = value; }
        public DateTimeOffset? PreviousFireTimeUtc { get => inner.PreviousFireTimeUtc; set => inner.PreviousFireTimeUtc = value; }
        public string FireInstanceId { get => inner.FireInstanceId; set => inner.FireInstanceId = value; }

        public TriggerBuilder<IJob> GetTriggerBuilder() => inner.GetTriggerBuilder();
        public IScheduleBuilder GetScheduleBuilder() => inner.GetScheduleBuilder();
        public DateTimeOffset? GetFireTimeAfter(DateTimeOffset? afterTime) => inner.GetFireTimeAfter(afterTime);
        public ITrigger Clone() => new ForeignTrigger((SimpleTriggerImpl) inner.Clone());
        public void Triggered(ICalendar calendar) => inner.Triggered(calendar);
        public DateTimeOffset? ComputeFirstFireTimeUtc(ICalendar calendar) => inner.ComputeFirstFireTimeUtc(calendar);
        public void UpdateAfterMisfire(ICalendar calendar) => inner.UpdateAfterMisfire(calendar);
        public void UpdateWithNewCalendar(ICalendar calendar, TimeSpan misfireThreshold) => inner.UpdateWithNewCalendar(calendar, misfireThreshold);
        public void Validate() => inner.Validate();

        public SchedulerInstruction ExecutionComplete(IJobExecutionContext context, JobExecutionException result)
        {
            return MayFireAgain ? SchedulerInstruction.NoInstruction : SchedulerInstruction.DeleteTrigger;
        }
    }

    private sealed class ExhaustedListener : ITriggerListener
    {
        private readonly SemaphoreSlim heard = new(0);

        public ConcurrentQueue<TriggerKey> Heard { get; } = new();

        public string Name => "retry-applied-exhausted";

        public ValueTask TriggerRetriesExhausted(
            ITrigger trigger,
            IJobExecutionContext context,
            JobExecutionException exception,
            CancellationToken cancellationToken = default)
        {
            Heard.Enqueue(trigger.Key);
            heard.Release();
            return default;
        }

        public async Task WaitForOne()
        {
            (await heard.WaitAsync(waitLimit)).Should().BeTrue("the occurrence gave up, which is announced");
        }
    }

    /// <summary>
    /// Records the event id of every line, whatever the category.
    /// </summary>
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<int> eventIds = new();

        public IReadOnlyList<int> EventIds => [.. eventIds];

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(this);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(RecordingLoggerProvider provider) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                provider.eventIds.Enqueue(eventId.Id);
            }
        }
    }
}
