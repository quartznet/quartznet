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

using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit.Impl.Triggers;

/// <summary>
/// What a declared trigger re-applied at a start takes over from the trigger the store holds (#4128):
/// its fire count and a pending retry, and only while the two are the same kind on the same schedule.
/// </summary>
public sealed class TriggerRunStateCarryTest
{
    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    private static readonly TimeZoneInfo plusTwo =
        TimeZoneInfo.CreateCustomTimeZone("q4128-plus-two", TimeSpan.FromHours(2), "plus two", "plus two");

    private static IEnumerable<TestCaseData> SameScheduleCases()
    {
        yield return Case("simple", Simple(s => s.WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(3)));
        yield return Case("cron", Cron("0 0 2 * * ?", plusTwo));
        yield return Case("daily time interval", Daily(d => d));
        yield return Case("calendar interval", Calendar(c => c));
        yield return Case("recurrence", Recurrence("FREQ=DAILY;COUNT=5", plusTwo));

        static TestCaseData Case(string name, Func<ITrigger> build) => new TestCaseData(build).SetArgDisplayNames(name);
    }

    private static IEnumerable<TestCaseData> ChangedScheduleCases()
    {
        Func<ITrigger> simple = Simple(s => s.WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(3));
        yield return Case("simple, another repeat count", simple, Simple(s => s.WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(4)));
        yield return Case("simple, another interval", simple, Simple(s => s.WithInterval(TimeSpan.FromHours(2)).WithRepeatCount(3)));

        Func<ITrigger> cron = Cron("0 0 2 * * ?", plusTwo);
        yield return Case("cron, another expression", cron, Cron("0 0 3 * * ?", plusTwo));
        yield return Case("cron, another zone", cron, Cron("0 0 2 * * ?", TimeZoneInfo.Utc));

        Func<ITrigger> daily = Daily(d => d);
        yield return Case("daily, another interval", daily, Daily(d => d.WithInterval(20, IntervalUnit.Minute)));
        yield return Case("daily, another unit", daily, Daily(d => d.WithInterval(10, IntervalUnit.Second)));
        yield return Case("daily, other days", daily, Daily(d => d.OnDaysOfTheWeek(DayOfWeek.Monday)));
        yield return Case("daily, another daily start", daily, Daily(d => d.StartingDailyAt(new TimeOnly(9, 0))));
        yield return Case("daily, another daily end", daily, Daily(d => d.EndingDailyAt(new TimeOnly(18, 0))));
        yield return Case("daily, another daily repeat count", daily, Daily(d => d.WithRepeatCount(5)));
        yield return Case("daily, another zone", daily, Daily(d => d.InTimeZone(TimeZoneInfo.Utc)));

        Func<ITrigger> calendar = Calendar(c => c);
        yield return Case("calendar, another interval", calendar, Calendar(c => c.WithInterval(3, IntervalUnit.Day)));
        yield return Case("calendar, another unit", calendar, Calendar(c => c.WithInterval(2, IntervalUnit.Week)));
        yield return Case("calendar, another zone", calendar, Calendar(c => c.InTimeZone(TimeZoneInfo.Utc)));
        yield return Case("calendar, hour of day not preserved", calendar, Calendar(c => c.PreserveHourOfDayAcrossDaylightSavings(false)));
        yield return Case("calendar, missing hour not skipped", calendar, Calendar(c => c.SkipDayIfHourDoesNotExist(false)));

        Func<ITrigger> recurrence = Recurrence("FREQ=DAILY;COUNT=5", plusTwo);
        yield return Case("recurrence, another rule", recurrence, Recurrence("FREQ=DAILY;COUNT=6", plusTwo));
        yield return Case("recurrence, another zone", recurrence, Recurrence("FREQ=DAILY;COUNT=5", TimeZoneInfo.Utc));

        yield return Case("another kind", simple, Cron("0 0 2 * * ?", plusTwo));

        static TestCaseData Case(string name, Func<ITrigger> stored, Func<ITrigger> declared) =>
            new TestCaseData(stored, declared).SetArgDisplayNames(name);
    }

    [TestCaseSource(nameof(SameScheduleCases))]
    public void TheSameScheduleCarriesTheFireCount(Func<ITrigger> build)
    {
        TriggerBase stored = (TriggerBase) build();
        SetTimesTriggered(stored, 2);
        TriggerBase declared = (TriggerBase) build();

        declared.CarryRunStateFrom(stored).Should().BeTrue("the declaration says what the stored trigger already does");
        TimesTriggeredOf(declared).Should().Be(TimesTriggeredOf(stored),
            "a start re-applies the trigger the store holds, and must not forget how many times it has fired");
    }

    [TestCaseSource(nameof(ChangedScheduleCases))]
    public void AChangedScheduleStartsFresh(Func<ITrigger> buildStored, Func<ITrigger> buildDeclared)
    {
        TriggerBase stored = (TriggerBase) buildStored();
        SetTimesTriggered(stored, 2);
        PutInRetry(stored, epoch.AddMinutes(5));
        TriggerBase declared = (TriggerBase) buildDeclared();

        declared.CarryRunStateFrom(stored).Should().BeFalse("the declaration has said something new");
        TimesTriggeredOf(declared).Should().Be(0, "a changed schedule is a new trigger, which has not fired");
        declared.RetryAttempt.Should().Be(0, "a retry of the old schedule's occurrence is not the new schedule's");
    }

    [Test]
    public void ACountTheDeclarationHasOfItsOwnIsKept()
    {
        Func<ITrigger> build = Simple(s => s.WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(3));
        SimpleTriggerImpl stored = (SimpleTriggerImpl) build();
        stored.TimesTriggered = 2;
        SimpleTriggerImpl declared = (SimpleTriggerImpl) build();
        declared.TimesTriggered = 1;

        declared.CarryRunStateFrom(stored);

        declared.TimesTriggered.Should().Be(1, "carrying a value over is for a declaration that said nothing about it");
    }

    [Test]
    public void APendingRetryCarriesItsAttemptItsFireTimeAndTheStartItWasScheduledFrom()
    {
        Action<SimpleScheduleBuilder> hourly = s => s.WithInterval(TimeSpan.FromHours(1)).RepeatForever();
        TriggerBase stored = (TriggerBase) Simple(hourly)();
        PutInRetry(stored, epoch.AddMinutes(5));
        TriggerBase declared = (TriggerBase) Simple(hourly, epoch.AddMinutes(2))();

        declared.CarryRunStateFrom(stored).Should().BeTrue();

        declared.RetryAttempt.Should().Be(1, "the retry pending at shutdown is still the attempt it was");
        declared.NextFireTimeUtc.Should().Be(epoch.AddMinutes(5), "the retry still happens when it was due");
        declared.StartTimeUtc.Should().Be(epoch,
            "RescheduleJob recomputes a next fire time that is before the start time, and the declaration started at the restart");
    }

    [Test]
    public void NoRetryIsCarriedWhenTheStoredTriggerIsNotWaitingOnOne()
    {
        Action<SimpleScheduleBuilder> hourly = s => s.WithInterval(TimeSpan.FromHours(1)).RepeatForever();
        TriggerBase stored = (TriggerBase) Simple(hourly)();
        stored.NextFireTimeUtc = epoch.AddHours(1);
        TriggerBase declared = (TriggerBase) Simple(hourly, epoch.AddMinutes(2))();

        declared.CarryRunStateFrom(stored);

        declared.RetryAttempt.Should().Be(0);
        declared.NextFireTimeUtc.Should().BeNull("an ordinary next fire time is computed from the declaration, as it always was");
        declared.StartTimeUtc.Should().Be(epoch.AddMinutes(2), "the declaration's start stands");
    }

    [Test]
    public void ARetryTheDeclaredEndTimeNoLongerAllowsIsNotCarried()
    {
        TriggerBase stored = (TriggerBase) Simple(s => s.WithInterval(TimeSpan.FromHours(1)).RepeatForever())();
        PutInRetry(stored, epoch.AddMinutes(5));

        TriggerBase declared = (TriggerBase) TriggerBuilder.Create(new FakeTimeProvider(epoch.AddMinutes(2)))
            .WithIdentity("t", "carry")
            .ForJob("j", "carry")
            .StartAt(epoch.AddMinutes(2))
            .EndAt(epoch.AddMinutes(4))
            .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

        declared.CarryRunStateFrom(stored);

        declared.RetryAttempt.Should().Be(0, "a trigger does not fire after its end time, and a retry is a fire");
        declared.StartTimeUtc.Should().Be(epoch.AddMinutes(2));
    }

    [Test]
    public void ARetryTheDeclarationHasOfItsOwnIsKept()
    {
        Action<SimpleScheduleBuilder> hourly = s => s.WithInterval(TimeSpan.FromHours(1)).RepeatForever();
        TriggerBase stored = (TriggerBase) Simple(hourly)();
        PutInRetry(stored, epoch.AddMinutes(5));
        TriggerBase declared = (TriggerBase) Simple(hourly, epoch.AddMinutes(2))();
        declared.RetryAttempt = 2;

        declared.CarryRunStateFrom(stored);

        declared.RetryAttempt.Should().Be(2);
        declared.NextFireTimeUtc.Should().BeNull();
    }

    [Test]
    public void ACronTriggerCarriesAPendingRetry()
    {
        TriggerBase stored = (TriggerBase) Cron("0 0 * * * ?", TimeZoneInfo.Utc)();
        PutInRetry(stored, epoch.AddMinutes(5));
        TriggerBase declared = (TriggerBase) Cron("0 0 * * * ?", TimeZoneInfo.Utc, epoch.AddMinutes(2))();

        declared.CarryRunStateFrom(stored).Should().BeTrue();

        declared.RetryAttempt.Should().Be(1);
        declared.NextFireTimeUtc.Should().Be(epoch.AddMinutes(5));
    }

    [Test]
    public void ATriggerOfADerivedTypeIsAnotherKind()
    {
        SimpleTriggerImpl stored = (SimpleTriggerImpl) Simple(s => s.WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(3))();
        stored.TimesTriggered = 2;
        DerivedSimpleTrigger declared = new()
        {
            RepeatInterval = stored.RepeatInterval,
            RepeatCount = stored.RepeatCount,
        };

        declared.CarryRunStateFrom(stored).Should().BeFalse("a subclass may add to the schedule what the base cannot compare");
        declared.TimesTriggered.Should().Be(0);
    }

    [Test]
    public void AKindThatDoesNotCompareItsScheduleCarriesNothing()
    {
        OwnTrigger stored = new() { RetryAttempt = 1, NextFireTimeUtc = epoch.AddMinutes(5) };
        OwnTrigger declared = new();

        declared.CarryRunStateFrom(stored).Should().BeFalse(
            "a trigger type of the application's own says nothing about its schedule, so it starts fresh as before");
        declared.RetryAttempt.Should().Be(0);
    }

    [Test]
    public void AStoredTriggerThatIsNotATriggerBaseCarriesNothing()
    {
        TriggerBase declared = (TriggerBase) Simple(s => s.WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(3))();

        declared.CarryRunStateFrom(FakeItEasy.A.Fake<ITrigger>()).Should().BeFalse();
    }

    private static Func<ITrigger> Simple(Action<SimpleScheduleBuilder> schedule, DateTimeOffset? start = null) =>
        () => Build(start ?? epoch, b => b.WithSimpleSchedule(schedule));

    private static Func<ITrigger> Cron(string expression, TimeZoneInfo zone, DateTimeOffset? start = null) =>
        () => Build(start ?? epoch, b => b.WithSchedule(CronScheduleBuilder.Create(expression).InTimeZone(zone)));

    private static Func<ITrigger> Daily(Func<DailyTimeIntervalScheduleBuilder, DailyTimeIntervalScheduleBuilder> change) =>
        () => Build(epoch, b => b.WithSchedule(change(DailyTimeIntervalScheduleBuilder.Create()
            .WithInterval(10, IntervalUnit.Minute)
            .OnMondayThroughFriday()
            .StartingDailyAt(new TimeOnly(8, 0))
            .EndingDailyAt(new TimeOnly(17, 0))
            .WithRepeatCount(10)
            .InTimeZone(plusTwo))));

    private static Func<ITrigger> Calendar(Func<CalendarIntervalScheduleBuilder, CalendarIntervalScheduleBuilder> change) =>
        () => Build(epoch, b => b.WithSchedule(change(CalendarIntervalScheduleBuilder.Create()
            .WithInterval(2, IntervalUnit.Day)
            .InTimeZone(plusTwo)
            .PreserveHourOfDayAcrossDaylightSavings()
            .SkipDayIfHourDoesNotExist())));

    private static Func<ITrigger> Recurrence(string rule, TimeZoneInfo zone) =>
        () => Build(epoch, b => b.WithSchedule(RecurrenceScheduleBuilder.Create(rule).InTimeZone(zone)));

    private static ITrigger Build(DateTimeOffset start, Func<TriggerBuilder<IJob>, TriggerBuilder<IJob>> schedule)
    {
        return schedule(TriggerBuilder.Create(new FakeTimeProvider(start))
                .WithIdentity("t", "carry")
                .ForJob("j", "carry")
                .StartAt(start))
            .Build();
    }

    private static void PutInRetry(TriggerBase trigger, DateTimeOffset retryAt)
    {
        trigger.NextFireTimeUtc = retryAt;
        trigger.RetryAttempt = 1;
    }

    private static int TimesTriggeredOf(ITrigger trigger) => trigger switch
    {
        ISimpleTrigger simple => simple.TimesTriggered,
        IDailyTimeIntervalTrigger daily => daily.TimesTriggered,
        ICalendarIntervalTrigger calendar => calendar.TimesTriggered,
        IRecurrenceTrigger recurrence => recurrence.TimesTriggered,
        _ => 0,
    };

    private static void SetTimesTriggered(ITrigger trigger, int value)
    {
        switch (trigger)
        {
            case SimpleTriggerImpl simple:
                simple.TimesTriggered = value;
                break;
            case DailyTimeIntervalTriggerImpl daily:
                daily.TimesTriggered = value;
                break;
            case CalendarIntervalTriggerImpl calendar:
                calendar.TimesTriggered = value;
                break;
            case RecurrenceTriggerImpl recurrence:
                recurrence.TimesTriggered = value;
                break;
        }
    }

    private sealed class DerivedSimpleTrigger : SimpleTriggerImpl;

    /// <summary>
    /// A trigger type of an application's own, which does not tell the scheduler how to compare its
    /// schedule. Nothing here is called but the carry.
    /// </summary>
    private sealed class OwnTrigger : TriggerBase
    {
        public override DateTimeOffset? FinalFireTimeUtc => null;
        public override DateTimeOffset? PreviousFireTimeUtc { get; set; }
        public override DateTimeOffset? NextFireTimeUtc { get; set; }
        public override bool MayFireAgain => false;
        protected override bool HasMillisecondPrecision => true;

        public override IScheduleBuilder GetScheduleBuilder() => throw new NotSupportedException();
        public override void Triggered(ICalendar? calendar) => throw new NotSupportedException();
        public override DateTimeOffset? ComputeFirstFireTimeUtc(ICalendar? calendar) => throw new NotSupportedException();
        public override DateTimeOffset? GetFireTimeAfter(DateTimeOffset? afterTime) => throw new NotSupportedException();
        protected override bool ValidateMisfireInstruction(int misfireInstruction) => true;
        public override void UpdateAfterMisfire(ICalendar? calendar) => throw new NotSupportedException();
        public override void UpdateWithNewCalendar(ICalendar calendar, TimeSpan misfireThreshold) => throw new NotSupportedException();
    }
}
