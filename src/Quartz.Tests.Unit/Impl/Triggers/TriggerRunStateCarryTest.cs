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

using System;
using System.Collections.Generic;

using FakeItEasy;

using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit.Impl.Triggers;

/// <summary>
/// What a declared trigger re-applied at a start takes over from the trigger the store holds (#4128):
/// its fire count, and only while the two are the same kind on the same schedule.
/// </summary>
public sealed class TriggerRunStateCarryTest
{
    private static readonly DateTimeOffset epoch = new DateTimeOffset(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    private static readonly TimeZoneInfo plusTwo =
        TimeZoneInfo.CreateCustomTimeZone("q4128-plus-two", TimeSpan.FromHours(2), "plus two", "plus two");

    private static IEnumerable<TestCaseData> SameScheduleCases()
    {
        yield return Case("simple", Simple(s => s.WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(3)));
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

        Func<ITrigger> daily = Daily(d => d);
        yield return Case("daily, another interval", daily, Daily(d => d.WithInterval(20, IntervalUnit.Minute)));
        yield return Case("daily, another unit", daily, Daily(d => d.WithInterval(10, IntervalUnit.Second)));
        yield return Case("daily, other days", daily, Daily(d => d.OnDaysOfTheWeek(DayOfWeek.Monday)));
        yield return Case("daily, another daily start", daily, Daily(d => d.StartingDailyAt(new TimeOfDay(9, 0))));
        yield return Case("daily, another daily end", daily, Daily(d => d.EndingDailyAt(new TimeOfDay(18, 0))));
        yield return Case("daily, another repeat count", daily, Daily(d => d.WithRepeatCount(5)));
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

        yield return Case("another kind", simple, Daily(d => d));

        static TestCaseData Case(string name, Func<ITrigger> stored, Func<ITrigger> declared) =>
            new TestCaseData(stored, declared).SetArgDisplayNames(name);
    }

    [TestCaseSource(nameof(SameScheduleCases))]
    public void TheSameScheduleCarriesTheFireCount(Func<ITrigger> build)
    {
        AbstractTrigger stored = (AbstractTrigger) build();
        SetTimesTriggered(stored, 2);
        AbstractTrigger declared = (AbstractTrigger) build();

        declared.CarryRunStateFrom(stored).Should().BeTrue("the declaration says what the stored trigger already does");
        TimesTriggeredOf(declared).Should().Be(2,
            "a start re-applies the trigger the store holds, and must not forget how many times it has fired");
    }

    [TestCaseSource(nameof(ChangedScheduleCases))]
    public void AChangedScheduleStartsFresh(Func<ITrigger> buildStored, Func<ITrigger> buildDeclared)
    {
        AbstractTrigger stored = (AbstractTrigger) buildStored();
        SetTimesTriggered(stored, 2);
        AbstractTrigger declared = (AbstractTrigger) buildDeclared();

        declared.CarryRunStateFrom(stored).Should().BeFalse("the declaration has said something new");
        TimesTriggeredOf(declared).Should().Be(0, "a changed schedule is a new trigger, which has not fired");
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
    public void ACronTriggerHasNothingToCarry()
    {
        AbstractTrigger stored = (AbstractTrigger) Cron();
        AbstractTrigger declared = (AbstractTrigger) Cron();

        declared.CarryRunStateFrom(stored).Should().BeFalse("a cron trigger counts no fires");
    }

    [Test]
    public void ATriggerOfADerivedTypeIsAnotherKind()
    {
        SimpleTriggerImpl stored = (SimpleTriggerImpl) Simple(s => s.WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(3))();
        stored.TimesTriggered = 2;
        DerivedSimpleTrigger declared = new DerivedSimpleTrigger
        {
            RepeatInterval = stored.RepeatInterval,
            RepeatCount = stored.RepeatCount,
        };

        declared.CarryRunStateFrom(stored).Should().BeFalse("a subclass may add to the schedule what the base cannot compare");
        declared.TimesTriggered.Should().Be(0);
    }

    [Test]
    public void AStoredTriggerThatIsNotAnAbstractTriggerCarriesNothing()
    {
        AbstractTrigger declared = (AbstractTrigger) Simple(s => s.WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(3))();

        declared.CarryRunStateFrom(A.Fake<ITrigger>()).Should().BeFalse();
    }

    private static Func<ITrigger> Simple(Action<SimpleScheduleBuilder> schedule) =>
        () => Build(b => b.WithSimpleSchedule(schedule));

    private static ITrigger Cron() =>
        Build(b => b.WithSchedule(CronScheduleBuilder.CronSchedule("0 0 2 * * ?").InTimeZone(plusTwo)));

    private static Func<ITrigger> Daily(Func<DailyTimeIntervalScheduleBuilder, DailyTimeIntervalScheduleBuilder> change) =>
        () => Build(b => b.WithSchedule(change(DailyTimeIntervalScheduleBuilder.Create()
            .WithInterval(10, IntervalUnit.Minute)
            .OnMondayThroughFriday()
            .StartingDailyAt(new TimeOfDay(8, 0))
            .EndingDailyAt(new TimeOfDay(17, 0))
            .WithRepeatCount(10)
            .InTimeZone(plusTwo))));

    private static Func<ITrigger> Calendar(Func<CalendarIntervalScheduleBuilder, CalendarIntervalScheduleBuilder> change) =>
        () => Build(b => b.WithSchedule(change(CalendarIntervalScheduleBuilder.Create()
            .WithInterval(2, IntervalUnit.Day)
            .InTimeZone(plusTwo)
            .PreserveHourOfDayAcrossDaylightSavings(true)
            .SkipDayIfHourDoesNotExist(true))));

    private static Func<ITrigger> Recurrence(string rule, TimeZoneInfo zone) =>
        () => Build(b => b.WithSchedule(RecurrenceScheduleBuilder.Create(rule).InTimeZone(zone)));

    private static ITrigger Build(Func<TriggerBuilder, TriggerBuilder> schedule)
    {
        return schedule(TriggerBuilder.Create()
                .WithIdentity("t", "carry")
                .ForJob("j", "carry")
                .StartAt(epoch))
            .Build();
    }

    private static int TimesTriggeredOf(ITrigger trigger)
    {
        switch (trigger)
        {
            case ISimpleTrigger simple:
                return simple.TimesTriggered;
            case IDailyTimeIntervalTrigger daily:
                return daily.TimesTriggered;
            case ICalendarIntervalTrigger calendar:
                return calendar.TimesTriggered;
            case RecurrenceTriggerImpl recurrence:
                return recurrence.TimesTriggered;
            default:
                return 0;
        }
    }

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

    private sealed class DerivedSimpleTrigger : SimpleTriggerImpl
    {
    }
}
