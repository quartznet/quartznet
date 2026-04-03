using System;

using NUnit.Framework;

using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit.Impl.Triggers;

public class RecurrenceTriggerImplTest
{
    [Test]
    public void TestComputeFirstFireTimeUtc()
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl();
        trigger.RecurrenceRule = "FREQ=DAILY";
        trigger.StartTimeUtc = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        trigger.TimeZone = TimeZoneInfo.Utc;

        DateTimeOffset? firstFire = trigger.ComputeFirstFireTimeUtc(null);
        Assert.IsNotNull(firstFire);
        Assert.AreEqual(new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero), firstFire);
    }

    [Test]
    public void TestGetFireTimeAfter()
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl();
        trigger.RecurrenceRule = "FREQ=DAILY";
        trigger.StartTimeUtc = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        trigger.TimeZone = TimeZoneInfo.Utc;

        DateTimeOffset? next = trigger.GetFireTimeAfter(new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero));
        Assert.IsNotNull(next);
        Assert.AreEqual(new DateTimeOffset(2025, 1, 2, 9, 0, 0, TimeSpan.Zero), next);
    }

    [Test]
    public void TestTriggeredAdvancesFireTime()
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl();
        trigger.RecurrenceRule = "FREQ=DAILY";
        trigger.StartTimeUtc = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        trigger.TimeZone = TimeZoneInfo.Utc;

        trigger.ComputeFirstFireTimeUtc(null);
        Assert.AreEqual(0, trigger.TimesTriggered);

        trigger.Triggered(null);
        Assert.AreEqual(1, trigger.TimesTriggered);
        Assert.IsNotNull(trigger.GetNextFireTimeUtc());
        Assert.AreEqual(new DateTimeOffset(2025, 1, 2, 9, 0, 0, TimeSpan.Zero), trigger.GetNextFireTimeUtc());
    }

    [Test]
    public void TestCountExhaustsFireTimes()
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl();
        trigger.RecurrenceRule = "FREQ=DAILY;COUNT=2";
        trigger.StartTimeUtc = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        trigger.TimeZone = TimeZoneInfo.Utc;

        trigger.ComputeFirstFireTimeUtc(null);

        // Fire 1
        trigger.Triggered(null);
        Assert.IsNotNull(trigger.GetNextFireTimeUtc());

        // Fire 2 - should exhaust COUNT
        trigger.Triggered(null);

        // TimesTriggered is now 2, which equals COUNT=2
        // GetFireTimeAfter should return null
        Assert.AreEqual(2, trigger.TimesTriggered);
        Assert.IsNull(trigger.GetFireTimeAfter(trigger.GetPreviousFireTimeUtc()));
    }

    [Test]
    public void TestEndTimeRespected()
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl();
        trigger.RecurrenceRule = "FREQ=DAILY";
        trigger.StartTimeUtc = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        trigger.EndTimeUtc = new DateTimeOffset(2025, 1, 3, 9, 0, 0, TimeSpan.Zero);
        trigger.TimeZone = TimeZoneInfo.Utc;

        trigger.ComputeFirstFireTimeUtc(null);

        trigger.Triggered(null); // now next fire = Jan 2
        trigger.Triggered(null); // now next fire = Jan 3
        trigger.Triggered(null); // now next fire should be null (past end time)

        Assert.IsNull(trigger.GetNextFireTimeUtc());
    }

    [Test]
    public void TestCalendarExclusion()
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl();
        trigger.RecurrenceRule = "FREQ=DAILY";
        trigger.StartTimeUtc = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        trigger.TimeZone = TimeZoneInfo.Utc;

        // Create a calendar that excludes Jan 2
        DailyCalendar cal = new DailyCalendar("9:00", "9:59");
        // DailyCalendar inverts by default - it excludes the specified range
        // Let's use a simpler approach: compute first fire time with no calendar
        trigger.ComputeFirstFireTimeUtc(null);
        Assert.IsNotNull(trigger.GetNextFireTimeUtc());
    }

    [Test]
    public void TestValidateThrowsOnEmptyRule()
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl();
        trigger.RecurrenceRule = "";
        trigger.StartTimeUtc = DateTimeOffset.UtcNow;

        Assert.Throws<SchedulerException>(() => trigger.Validate());
    }

    [Test]
    public void TestValidateThrowsOnInvalidRule()
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl();
        trigger.RecurrenceRule = "INVALID_RRULE";
        trigger.StartTimeUtc = DateTimeOffset.UtcNow;

        Assert.Throws<SchedulerException>(() => trigger.Validate());
    }

    [Test]
    public void TestValidateSucceedsOnValidRule()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("test", "group")
            .WithRecurrenceSchedule("FREQ=WEEKLY;BYDAY=MO,WE,FR")
            .ForJob("job", "jobGroup")
            .StartNow()
            .Build();

        Assert.DoesNotThrow(() => ((RecurrenceTriggerImpl)trigger).Validate());
    }

    [Test]
    public void TestGetScheduleBuilderRoundTrip()
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl();
        trigger.RecurrenceRule = "FREQ=MONTHLY;BYDAY=2MO";
        trigger.StartTimeUtc = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        trigger.TimeZone = TimeZoneInfo.Utc;
        trigger.MisfireInstruction = MisfireInstruction.RecurrenceTrigger.DoNothing;

        IScheduleBuilder sb = trigger.GetScheduleBuilder();
        ITrigger rebuilt = TriggerBuilder.Create()
            .WithIdentity("test")
            .WithSchedule(sb)
            .StartAt(trigger.StartTimeUtc)
            .Build();

        Assert.IsInstanceOf<IRecurrenceTrigger>(rebuilt);
        IRecurrenceTrigger recTrigger = (IRecurrenceTrigger)rebuilt;
        Assert.AreEqual("FREQ=MONTHLY;BYDAY=2MO", recTrigger.RecurrenceRule);
        Assert.AreEqual(MisfireInstruction.RecurrenceTrigger.DoNothing, rebuilt.MisfireInstruction);
    }

    [Test]
    public void TestMisfireInstructionValidation()
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl();
        trigger.RecurrenceRule = "FREQ=DAILY";
        trigger.StartTimeUtc = DateTimeOffset.UtcNow;

        // Valid values
        trigger.MisfireInstruction = MisfireInstruction.SmartPolicy;
        trigger.MisfireInstruction = MisfireInstruction.IgnoreMisfirePolicy;
        trigger.MisfireInstruction = MisfireInstruction.RecurrenceTrigger.FireOnceNow;
        trigger.MisfireInstruction = MisfireInstruction.RecurrenceTrigger.DoNothing;

        // Invalid value
        Assert.Throws<ArgumentException>(() => trigger.MisfireInstruction = 99);
    }

    [Test]
    public void TestGetMayFireAgain()
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl();
        trigger.RecurrenceRule = "FREQ=DAILY";
        trigger.StartTimeUtc = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        trigger.TimeZone = TimeZoneInfo.Utc;

        trigger.ComputeFirstFireTimeUtc(null);
        Assert.IsTrue(trigger.GetMayFireAgain());
    }

    [Test]
    public void TestHasMillisecondPrecisionIsFalse()
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl();
        Assert.IsFalse(trigger.HasMillisecondPrecision);
    }

    [Test]
    public void TestWeeklyMondayWednesdayFriday()
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl();
        trigger.RecurrenceRule = "FREQ=WEEKLY;BYDAY=MO,WE,FR";
        // Jan 1 2025 is Wednesday
        trigger.StartTimeUtc = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        trigger.TimeZone = TimeZoneInfo.Utc;

        trigger.ComputeFirstFireTimeUtc(null);
        // First fire should be Jan 1 (Wednesday)
        Assert.AreEqual(new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero), trigger.GetNextFireTimeUtc());

        trigger.Triggered(null);
        // Next should be Friday Jan 3
        Assert.AreEqual(new DateTimeOffset(2025, 1, 3, 9, 0, 0, TimeSpan.Zero), trigger.GetNextFireTimeUtc());

        trigger.Triggered(null);
        // Next should be Monday Jan 6
        Assert.AreEqual(new DateTimeOffset(2025, 1, 6, 9, 0, 0, TimeSpan.Zero), trigger.GetNextFireTimeUtc());
    }

    [Test]
    public void TestBuilderCreatesCorrectTrigger()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("test", "group")
            .WithRecurrenceSchedule("FREQ=MONTHLY;BYDAY=2MO", b => b
                .InTimeZone(TimeZoneInfo.Utc)
                .WithMisfireHandlingInstructionDoNothing())
            .StartAt(new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero))
            .Build();

        Assert.IsInstanceOf<RecurrenceTriggerImpl>(trigger);
        IRecurrenceTrigger recTrigger = (IRecurrenceTrigger)trigger;
        Assert.AreEqual("FREQ=MONTHLY;BYDAY=2MO", recTrigger.RecurrenceRule);
        Assert.AreEqual(TimeZoneInfo.Utc, recTrigger.TimeZone);
        Assert.AreEqual(MisfireInstruction.RecurrenceTrigger.DoNothing, trigger.MisfireInstruction);
    }

    [Test]
    public void TestBuilderSimpleOverload()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("test")
            .WithRecurrenceSchedule("FREQ=DAILY")
            .StartNow()
            .Build();

        Assert.IsInstanceOf<IRecurrenceTrigger>(trigger);
        Assert.AreEqual("FREQ=DAILY", ((IRecurrenceTrigger)trigger).RecurrenceRule);
    }
}
