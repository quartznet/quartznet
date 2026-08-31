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

using FakeItEasy;

using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit;

/// <summary>
/// The retry decision itself: what <c>ExecutionComplete</c> does with a failed job, and what
/// <c>RetryFired</c> does when the retry it scheduled comes round.
/// </summary>
/// <remarks>
/// Everything here runs against a trigger and a clock, with no store and no scheduler, because the
/// decision is the trigger's. What the stores do with the instruction is
/// <see cref="Impl.TriggeredJobCompleteTest" />'s, and the end-to-end behaviour is
/// <see cref="RetryExecutionTest" />'s.
/// </remarks>
[TestFixture]
public class RetryEngineTest
{
    private static readonly DateTimeOffset now = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    private FakeTimeProvider clock;
    private IJobExecutionContext context;

    [SetUp]
    public void SetUp()
    {
        clock = new FakeTimeProvider(now);
        context = A.Fake<IJobExecutionContext>();
    }

    /// <summary>A failure the job did not ask anything about, which is what a thrown exception becomes.</summary>
    private static JobExecutionException Failure() => new JobExecutionException(new InvalidOperationException("boom"));

    /// <summary>An hourly trigger, fired at 10:00, so its next scheduled occurrence is 11:00.</summary>
    private IOperableTrigger HourlyTriggerFiredAtTen(RetryPolicy policy)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity("hourly", "retries")
            .ForJob("job", "jobs")
            .WithCronSchedule("0 0 * * * ?")
            .StartAt(now.AddDays(-1))
            .WithRetryPolicy(policy)
            .Build();

        trigger.ComputeFirstFireTimeUtc(null);
        trigger.NextFireTimeUtc = now;
        trigger.Triggered(null);

        trigger.NextFireTimeUtc.Should().Be(now.AddHours(1), "the fixture is an hourly trigger just fired at 10:00");
        return trigger;
    }

    [Test]
    public void AFailureWithAttemptsLeftSchedulesTheRetry()
    {
        IOperableTrigger trigger = HourlyTriggerFiredAtTen(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(5)));

        SchedulerInstruction instruction = trigger.ExecutionComplete(context, Failure());

        instruction.Should().Be(SchedulerInstruction.RetryTrigger);
        trigger.NextFireTimeUtc.Should().Be(now.AddMinutes(5), "the retry instant is now plus the policy's first wait");
        trigger.RetryAttempt.Should().Be(1);
        trigger.MayFireAgain.Should().BeTrue("a trigger waiting to retry has something left to do");
    }

    [Test]
    public void EachFailureTakesTheNextWaitFromThePolicy()
    {
        IOperableTrigger trigger = HourlyTriggerFiredAtTen(RetryPolicy.Explicit(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4)));

        trigger.ExecutionComplete(context, Failure()).Should().Be(SchedulerInstruction.RetryTrigger);
        trigger.NextFireTimeUtc.Should().Be(now.AddMinutes(1));
        trigger.RetryAttempt.Should().Be(1);

        // The retry fires, which puts the trigger back on its schedule, and fails again.
        ((TriggerBase) trigger).RetryFired(null);
        clock.SetUtcNow(now.AddMinutes(1));

        trigger.ExecutionComplete(context, Failure()).Should().Be(SchedulerInstruction.RetryTrigger);
        trigger.NextFireTimeUtc.Should().Be(now.AddMinutes(3), "the second wait is two minutes, measured from the second failure");
        trigger.RetryAttempt.Should().Be(2);
    }

    [Test]
    public void SuccessPutsTheAttemptBack()
    {
        IOperableTrigger trigger = HourlyTriggerFiredAtTen(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(5)));
        trigger.ExecutionComplete(context, Failure());
        ((TriggerBase) trigger).RetryFired(null);

        SchedulerInstruction instruction = trigger.ExecutionComplete(context, result: null);

        instruction.Should().Be(SchedulerInstruction.NoInstruction);
        trigger.RetryAttempt.Should().Be(0, "the occurrence is done with, so the next failure starts from the first wait again");
        trigger.NextFireTimeUtc.Should().Be(now.AddHours(1), "the schedule is untouched by a retry that worked");
        ((TriggerBase) trigger).RetryAttemptCleared.Should().BeTrue("the store has a write to make");
    }

    [Test]
    public void ExhaustedAttemptsGoBackToTheOrdinaryScheduleAndNotToError()
    {
        IOperableTrigger trigger = HourlyTriggerFiredAtTen(RetryPolicy.Fixed(2, TimeSpan.FromMinutes(5)));

        trigger.ExecutionComplete(context, Failure()).Should().Be(SchedulerInstruction.RetryTrigger);
        ((TriggerBase) trigger).RetryFired(null);
        clock.SetUtcNow(now.AddMinutes(5));

        trigger.ExecutionComplete(context, Failure()).Should().Be(SchedulerInstruction.RetryTrigger);
        ((TriggerBase) trigger).RetryFired(null);
        clock.SetUtcNow(now.AddMinutes(10));

        SchedulerInstruction instruction = trigger.ExecutionComplete(context, Failure());

        instruction.Should().Be(SchedulerInstruction.NoInstruction,
            "one bad hour must not kill a cron trigger: spent attempts put it back on its schedule, not into Error");
        trigger.RetryAttempt.Should().Be(0);
        trigger.NextFireTimeUtc.Should().Be(now.AddHours(1));
    }

    [Test]
    public void ARetryThatWouldPassTheNextOccurrenceIsDropped()
    {
        // Ninety minutes is longer than the hour between occurrences, so the 11:00 fire supersedes it.
        IOperableTrigger trigger = HourlyTriggerFiredAtTen(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(90)));

        SchedulerInstruction instruction = trigger.ExecutionComplete(context, Failure());

        instruction.Should().Be(SchedulerInstruction.NoInstruction);
        trigger.NextFireTimeUtc.Should().Be(now.AddHours(1), "the schedule wins; the trigger is not retried twice for one late hour");
        trigger.RetryAttempt.Should().Be(0);
    }

    [Test]
    public void TheSupersedeRuleIsAWholeSecondWideOnBothSides()
    {
        // Exactly one second short of the next occurrence: retryAt + 1s == regularNext, which the rule
        // rejects, because GetFireTimeAfter adds a second before searching and could not tell them apart.
        IOperableTrigger tooClose = HourlyTriggerFiredAtTen(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(60) - TimeSpan.FromSeconds(1)));
        tooClose.ExecutionComplete(context, Failure()).Should().Be(SchedulerInstruction.NoInstruction);
        tooClose.RetryAttempt.Should().Be(0);

        // One tick further away, and there is room.
        IOperableTrigger justFits = HourlyTriggerFiredAtTen(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(60) - TimeSpan.FromSeconds(1) - TimeSpan.FromTicks(1)));
        justFits.ExecutionComplete(context, Failure()).Should().Be(SchedulerInstruction.RetryTrigger);
        justFits.RetryAttempt.Should().Be(1);
    }

    [Test]
    public void ARetryIsNotScheduledPastTheTriggersEndTime()
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity("ending", "retries")
            .ForJob("job", "jobs")
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .StartAt(now)
            .EndAt(now.AddMinutes(2))
            .WithRetryPolicy(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(5)))
            .Build();
        trigger.ComputeFirstFireTimeUtc(null);
        trigger.Triggered(null);

        SchedulerInstruction instruction = trigger.ExecutionComplete(context, Failure());

        instruction.Should().Be(SchedulerInstruction.DeleteTrigger,
            "a trigger does not fire after its end time, and a retry is a fire");
        trigger.RetryAttempt.Should().Be(0);
    }

    /// <summary>
    /// The other side of that boundary: a retry landing exactly <em>on</em> the end time is a fire at
    /// the end time, and the end time is the last instant at which a trigger may fire.
    /// </summary>
    [Test]
    public void ARetryLandingExactlyOnTheEndTimeIsScheduled()
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity("ending-on-the-instant", "retries")
            .ForJob("job", "jobs")
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .StartAt(now)
            .EndAt(now.AddMinutes(5))
            .WithRetryPolicy(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(5)))
            .Build();
        trigger.ComputeFirstFireTimeUtc(null);
        trigger.Triggered(null);

        trigger.NextFireTimeUtc.Should().BeNull("the next hourly occurrence is an hour past the end time");

        trigger.ExecutionComplete(context, Failure()).Should().Be(SchedulerInstruction.RetryTrigger,
            "the retry lands on the end time rather than past it, and a fire on the end time fires");
        trigger.RetryAttempt.Should().Be(1);
        trigger.NextFireTimeUtc.Should().Be(now.AddMinutes(5));

        ((TriggerBase) trigger).RetryFired(null);

        trigger.NextFireTimeUtc.Should().BeNull("nothing fires after the end time, retry or occurrence");
    }

    /// <summary>
    /// A failure on the occurrence before the last one, where the last one lands exactly on the end
    /// time: the retry runs and the boundary occurrence is still there afterwards.
    /// </summary>
    /// <remarks>
    /// Both halves of this are the end time being inclusive (#3458). While it was exclusive for a
    /// simple trigger, <see cref="ITrigger.NextFireTimeUtc" /> was already <see langword="null" />
    /// here, so the trigger was deleted at the end of the failed occurrence and the fire on the end
    /// time - which <see cref="ITrigger.FinalFireTimeUtc" /> named all along - never happened.
    /// </remarks>
    [Test]
    public void ARetryKeepsTheOccurrenceThatLandsOnTheEndTime()
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity("ending-on-an-occurrence", "retries")
            .ForJob("job", "jobs")
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .StartAt(now)
            .EndAt(now.AddHours(1))
            .WithRetryPolicy(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(5)))
            .Build();
        trigger.ComputeFirstFireTimeUtc(null);
        trigger.Triggered(null);

        trigger.NextFireTimeUtc.Should().Be(now.AddHours(1),
            "the last occurrence lands on the end time, which is the last instant at which it may fire");

        trigger.ExecutionComplete(context, Failure()).Should().Be(SchedulerInstruction.RetryTrigger);
        trigger.NextFireTimeUtc.Should().Be(now.AddMinutes(5), "the retry comes before that occurrence");

        ((TriggerBase) trigger).RetryFired(null);

        trigger.NextFireTimeUtc.Should().Be(now.AddHours(1),
            "a retry is another attempt at the occurrence that failed, so the one on the end time is still to come");
        trigger.MayFireAgain.Should().BeTrue();
    }

    [Test]
    public void ATriggerWithNoPolicyIsUnchanged()
    {
        IOperableTrigger trigger = HourlyTriggerFiredAtTen(policy: null!);
        trigger.RetryPolicy = null;

        trigger.ExecutionComplete(context, Failure()).Should().Be(SchedulerInstruction.NoInstruction);
        trigger.NextFireTimeUtc.Should().Be(now.AddHours(1));
        trigger.RetryAttempt.Should().Be(0);
        ((TriggerBase) trigger).RetryAttemptCleared.Should().BeFalse("nothing was cleared, so the stores have no write to make");
    }

    [Test]
    public void ACancellationIsNotAFailureToRetry()
    {
        IOperableTrigger trigger = HourlyTriggerFiredAtTen(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(5)));

        // What the run shell leaves behind when the job stopped because the scheduler's own token was
        // cancelled: no JobExecutionException at all. Shutdown and interrupt are operator decisions.
        SchedulerInstruction instruction = trigger.ExecutionComplete(context, result: null);

        instruction.Should().Be(SchedulerInstruction.NoInstruction);
        trigger.NextFireTimeUtc.Should().Be(now.AddHours(1));
        trigger.RetryAttempt.Should().Be(0);
    }

    [Test]
    public void RefireImmediatelyWinsAndIsNotARetry()
    {
        IOperableTrigger trigger = HourlyTriggerFiredAtTen(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(5)));

        SchedulerInstruction instruction = trigger.ExecutionComplete(context, new JobExecutionException { RefireImmediately = true });

        instruction.Should().Be(SchedulerInstruction.ReExecuteJob,
            "an explicit directive wins over the trigger's policy, and the two are different things");
        trigger.RetryAttempt.Should().Be(0, "the in-process refire loop is not an attempt at the policy");
        trigger.NextFireTimeUtc.Should().Be(now.AddHours(1));
    }

    [Test]
    public void UnschedulingWinsOverTheRetryPolicy()
    {
        IOperableTrigger firing = HourlyTriggerFiredAtTen(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(5)));
        firing.ExecutionComplete(context, new JobExecutionException { UnscheduleFiringTrigger = true })
            .Should().Be(SchedulerInstruction.SetTriggerComplete);
        firing.RetryAttempt.Should().Be(0);

        IOperableTrigger all = HourlyTriggerFiredAtTen(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(5)));
        all.ExecutionComplete(context, new JobExecutionException { UnscheduleAllTriggers = true })
            .Should().Be(SchedulerInstruction.SetAllJobTriggersComplete);
        all.RetryAttempt.Should().Be(0);
    }

    [Test]
    public void AOneShotTriggerMayStillFireAgainWhileItWaitsToRetry()
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity("one-shot", "retries")
            .ForJob("job", "jobs")
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(0))
            .StartAt(now)
            .WithRetryPolicy(RetryPolicy.Fixed(1, TimeSpan.FromMinutes(5)))
            .Build();
        trigger.ComputeFirstFireTimeUtc(null);
        trigger.Triggered(null);

        trigger.NextFireTimeUtc.Should().BeNull("a one-shot trigger has nothing scheduled after its single fire");
        trigger.MayFireAgain.Should().BeFalse();

        trigger.ExecutionComplete(context, Failure()).Should().Be(SchedulerInstruction.RetryTrigger,
            "a null next fire time is not the next occurrence winning; there is no occurrence to lose to");
        trigger.MayFireAgain.Should().BeTrue(
            "the run shell announces TriggerFinalized off MayFireAgain, and a trigger about to retry is not finished");

        // The retry fires and succeeds, and now it really is finished.
        ((TriggerBase) trigger).RetryFired(null);
        trigger.NextFireTimeUtc.Should().BeNull();
        trigger.ExecutionComplete(context, result: null).Should().Be(SchedulerInstruction.DeleteTrigger);
    }

    /// <summary>A one-shot trigger, fired at 10:00, so there is no occurrence for a retry to lose to.</summary>
    private IOperableTrigger OneShotTriggerFiredAtTen(RetryPolicy policy)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity("one-shot", "retries")
            .ForJob("job", "jobs")
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(0))
            .StartAt(now)
            .WithRetryPolicy(policy)
            .Build();

        trigger.ComputeFirstFireTimeUtc(null);
        trigger.Triggered(null);

        trigger.NextFireTimeUtc.Should().BeNull("the fixture is a one-shot trigger that has had its single fire");
        return trigger;
    }

    /// <summary>
    /// The last instant a retry may land on, and the first one it may not. A fire time is a
    /// <see cref="DateTimeOffset"/>, and adding to one of those throws rather than saturating, so the
    /// end of the calendar is a boundary the retry arithmetic has to keep on the right side of.
    /// </summary>
    /// <remarks>
    /// The supersede margin comes off the room available, because the instant is compared against the
    /// next occurrence with that margin added — an instant with no room for the margin would overflow
    /// in the comparison instead.
    /// </remarks>
    [Test]
    public void TheEndOfTheCalendarIsTheLastInstantARetryMayLandOn()
    {
        TimeSpan room = DateTimeOffset.MaxValue - TriggerBase.RetrySupersedeMargin - now;

        IOperableTrigger justFits = OneShotTriggerFiredAtTen(RetryPolicy.Fixed(1, room));
        justFits.ExecutionComplete(context, Failure()).Should().Be(SchedulerInstruction.RetryTrigger);
        justFits.NextFireTimeUtc.Should().Be(DateTimeOffset.MaxValue - TriggerBase.RetrySupersedeMargin);
        justFits.RetryAttempt.Should().Be(1);

        IOperableTrigger oneTickTooFar = OneShotTriggerFiredAtTen(RetryPolicy.Fixed(1, room + TimeSpan.FromTicks(1)));
        oneTickTooFar.ExecutionComplete(context, Failure()).Should().Be(SchedulerInstruction.DeleteTrigger,
            "a retry with nowhere to land is a retry that never comes, so the occurrence settles");
        oneTickTooFar.RetryAttempt.Should().Be(0);
    }

    /// <summary>
    /// An exponential policy runs out of calendar long before it runs out of attempts, and it does so
    /// on numbers anybody might type: ten times a second, thirteen retries in. The waits themselves
    /// saturate at <see cref="TimeSpan.MaxValue"/> by design, and it is turning a saturated wait into a
    /// fire time that used to throw — out of <c>ExecutionComplete</c>, on the job's failure path, where
    /// the trigger had done nothing wrong.
    /// </summary>
    [Test]
    public void AnExponentialPolicyThatOutlivesTheCalendarSettlesInsteadOfThrowing()
    {
        RetryPolicy policy = RetryPolicy.Exponential(maxAttempts: 20, initialDelay: TimeSpan.FromSeconds(1), factor: 10);
        policy.DelayFor(13).Should().Be(TimeSpan.MaxValue, "a factor of ten spends a whole TimeSpan in thirteen retries");

        IOperableTrigger trigger = OneShotTriggerFiredAtTen(policy);

        int scheduled = 0;
        SchedulerInstruction instruction = trigger.ExecutionComplete(context, Failure());
        while (instruction == SchedulerInstruction.RetryTrigger)
        {
            scheduled++;
            ((TriggerBase) trigger).RetryFired(null);
            instruction = trigger.ExecutionComplete(context, Failure());
        }

        scheduled.Should().Be(12, "the twelfth wait is the last one that lands on a date a fire time can hold");
        instruction.Should().Be(SchedulerInstruction.DeleteTrigger,
            "the trigger runs out of retries it can express, not out of the attempts its policy allows");
        trigger.RetryAttempt.Should().Be(0);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // RetryFired
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// Every shipped trigger type, each with a counter or a schedule a retry must not consume.
    /// </summary>
    public static IEnumerable<TestCaseData> RetryFiredCases()
    {
        DateTimeOffset start = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

        yield return new TestCaseData(
            (Func<TimeProvider, IOperableTrigger>) (clock => (IOperableTrigger) TriggerBuilder.Create(clock)
                .WithIdentity("simple", "retries").ForJob("job", "jobs")
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(5))
                .StartAt(start).Build())).SetName("SimpleTrigger");

        yield return new TestCaseData(
            (Func<TimeProvider, IOperableTrigger>) (clock => (IOperableTrigger) TriggerBuilder.Create(clock)
                .WithIdentity("cron", "retries").ForJob("job", "jobs")
                .WithCronSchedule("0 0 * * * ?")
                .StartAt(start).Build())).SetName("CronTrigger");

        yield return new TestCaseData(
            (Func<TimeProvider, IOperableTrigger>) (clock => (IOperableTrigger) TriggerBuilder.Create(clock)
                .WithIdentity("calendar", "retries").ForJob("job", "jobs")
                .WithCalendarIntervalSchedule(x => x.WithInterval(1, IntervalUnit.Hour).InTimeZone(TimeZoneInfo.Utc))
                .StartAt(start).Build())).SetName("CalendarIntervalTrigger");

        yield return new TestCaseData(
            (Func<TimeProvider, IOperableTrigger>) (clock => (IOperableTrigger) TriggerBuilder.Create(clock)
                .WithIdentity("daily", "retries").ForJob("job", "jobs")
                .WithDailyTimeIntervalSchedule(x => x
                    .WithInterval(1, IntervalUnit.Hour)
                    .StartingDailyAt(new TimeOnly(0, 0))
                    .EndingDailyAt(new TimeOnly(23, 0))
                    .InTimeZone(TimeZoneInfo.Utc))
                .StartAt(start).Build())).SetName("DailyTimeIntervalTrigger");

        yield return new TestCaseData(
            (Func<TimeProvider, IOperableTrigger>) (clock => (IOperableTrigger) TriggerBuilder.Create(clock)
                .WithIdentity("recurrence", "retries").ForJob("job", "jobs")
                .WithRecurrenceSchedule("FREQ=HOURLY;COUNT=5", x => x.InTimeZone(TimeZoneInfo.Utc))
                .StartAt(start).Build())).SetName("RecurrenceTrigger");
    }

    /// <summary>
    /// The whole point of <c>RetryFired</c>: a retry is another attempt at an occurrence that has
    /// already been counted, so firing one must land the trigger back on the very occurrence it was
    /// already heading for, without burning a repeat count or an RRULE slot.
    /// </summary>
    [TestCaseSource(nameof(RetryFiredCases))]
    public void RetryFiredLandsOnTheSameOccurrenceTriggeredWouldHave(Func<TimeProvider, IOperableTrigger> build)
    {
        IOperableTrigger trigger = build(clock);
        trigger.ComputeFirstFireTimeUtc(null);
        trigger.Triggered(null);

        DateTimeOffset regularNext = trigger.NextFireTimeUtc.Should().NotBeNull().And.Subject!.Value;
        DateTimeOffset? previousFire = trigger.PreviousFireTimeUtc;

        // What a scheduled retry looks like on the trigger, a minute before the occurrence is due.
        DateTimeOffset retryAt = regularNext.AddMinutes(-1);
        trigger.NextFireTimeUtc = retryAt;

        ((TriggerBase) trigger).RetryFired(null);

        trigger.NextFireTimeUtc.Should().Be(regularNext,
            "a retry sits between two occurrences, so the schedule after it is the occurrence that was already due");
        trigger.PreviousFireTimeUtc.Should().Be(previousFire,
            "a retry reports the original occurrence as its scheduled fire time, so it does not move the previous fire");
    }

    [Test]
    public void RetryFiredBurnsNoRepeatCount()
    {
        SimpleTriggerImpl trigger = (SimpleTriggerImpl) TriggerBuilder.Create(clock)
            .WithIdentity("simple", "retries").ForJob("job", "jobs")
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(5))
            .StartAt(now).Build();
        trigger.ComputeFirstFireTimeUtc(null);
        trigger.Triggered(null);

        int afterOneFire = trigger.TimesTriggered;
        trigger.NextFireTimeUtc = now.AddMinutes(30);

        trigger.RetryFired(null);

        trigger.TimesTriggered.Should().Be(afterOneFire,
            "a repeat count counts occurrences, and a retry is a second go at one that has already been counted");
        trigger.NextFireTimeUtc.Should().Be(now.AddHours(1));
    }

    [Test]
    public void RetryFiredSkipsAnExcludedOccurrenceTheSameWayTriggeredDoes()
    {
        // A calendar that excludes the whole of the hour the next occurrence falls in.
        DailyCalendar calendar = new DailyCalendar(new TimeOnly(11, 0, 0), new TimeOnly(11, 59, 59))
        {
            // The trigger's fire times are UTC, and a calendar reads its range in its own zone: left
            // at the machine's, this excludes some other hour and the test proves nothing.
            TimeZone = TimeZoneInfo.Utc,
            InvertTimeRange = false
        };

        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity("cron", "retries").ForJob("job", "jobs")
            .WithCronSchedule("0 0 * * * ?")
            .StartAt(now.AddDays(-1))
            .Build();
        trigger.ComputeFirstFireTimeUtc(null);
        trigger.NextFireTimeUtc = now;
        trigger.Triggered(calendar);

        DateTimeOffset regularNext = trigger.NextFireTimeUtc!.Value;
        regularNext.Should().Be(now.AddHours(2), "11:00 is excluded, so Triggered skipped to 12:00");

        trigger.NextFireTimeUtc = now.AddMinutes(5);
        ((TriggerBase) trigger).RetryFired(calendar);

        trigger.NextFireTimeUtc.Should().Be(regularNext,
            "the retry fire applies the calendar exactly as the regular fire did, or the two would disagree about the schedule");
    }

    [Test]
    public void ADailyTimeIntervalTriggerThatRunsOutWhileRetryingIsFinished()
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity("daily", "retries").ForJob("job", "jobs")
            .WithDailyTimeIntervalSchedule(x => x
                .WithInterval(1, IntervalUnit.Hour)
                .StartingDailyAt(new TimeOnly(10, 0))
                .EndingDailyAt(new TimeOnly(10, 0))
                .InTimeZone(TimeZoneInfo.Utc))
            .StartAt(now)
            .EndAt(now.AddMinutes(30))
            .WithRetryPolicy(RetryPolicy.Fixed(1, TimeSpan.FromMinutes(5)))
            .Build();
        trigger.ComputeFirstFireTimeUtc(null);
        trigger.Triggered(null);

        trigger.NextFireTimeUtc.Should().BeNull("the trigger's end time is before its next daily slot");

        trigger.ExecutionComplete(context, Failure()).Should().Be(SchedulerInstruction.RetryTrigger);
        trigger.MayFireAgain.Should().BeTrue();

        ((TriggerBase) trigger).RetryFired(null);

        trigger.NextFireTimeUtc.Should().BeNull();
        trigger.MayFireAgain.Should().BeFalse(
            "this trigger records having run out of schedule in a flag of its own, and the retry fire has to set it too");
    }
}
