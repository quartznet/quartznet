
using NUnit.Framework;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Spi;

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
    public void TestCalendarExclusionSkipsExcludedDates()
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl();
        trigger.RecurrenceRule = "FREQ=DAILY";
        trigger.StartTimeUtc = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        trigger.TimeZone = TimeZoneInfo.Utc;

        // Exclude Jan 2 via AnnualCalendar
        AnnualCalendar cal = new AnnualCalendar();
        cal.SetDayExcluded(new DateTime(2025, 1, 2), true);

        trigger.ComputeFirstFireTimeUtc(cal);
        // First fire = Jan 1
        Assert.AreEqual(new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero), trigger.GetNextFireTimeUtc());

        // After triggering, next should skip Jan 2 (excluded) and land on Jan 3
        trigger.Triggered(cal);
        Assert.AreEqual(new DateTimeOffset(2025, 1, 3, 9, 0, 0, TimeSpan.Zero), trigger.GetNextFireTimeUtc());
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

    [Test]
    public void TestFinalFireTimeUtcWithCount()
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl();
        trigger.RecurrenceRule = "FREQ=DAILY;COUNT=5";
        trigger.StartTimeUtc = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        trigger.TimeZone = TimeZoneInfo.Utc;

        // FinalFireTimeUtc should be the 5th occurrence = Jan 5
        DateTimeOffset? finalFire = trigger.FinalFireTimeUtc;
        Assert.IsNotNull(finalFire);
        Assert.AreEqual(5, finalFire!.Value.Day);
        Assert.AreEqual(1, finalFire.Value.Month);
    }

    [Test]
    public void TestFinalFireTimeUtcWithEndTimeAligned()
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl();
        trigger.RecurrenceRule = "FREQ=DAILY";
        trigger.StartTimeUtc = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        trigger.EndTimeUtc = new DateTimeOffset(2025, 1, 10, 9, 0, 0, TimeSpan.Zero);
        trigger.TimeZone = TimeZoneInfo.Utc;

        // EndTime aligns with a fire time (daily at 9:00, EndTime at 9:00 Jan 10)
        Assert.AreEqual(trigger.EndTimeUtc, trigger.FinalFireTimeUtc);
    }

    [Test]
    public void TestFinalFireTimeUtcWithEndTimeMisaligned()
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl();
        trigger.RecurrenceRule = "FREQ=DAILY";
        trigger.StartTimeUtc = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        // EndTime at 8:00 AM — doesn't align with the 9:00 AM fire time
        trigger.EndTimeUtc = new DateTimeOffset(2025, 1, 10, 8, 0, 0, TimeSpan.Zero);
        trigger.TimeZone = TimeZoneInfo.Utc;

        // Last actual fire should be Jan 9 at 9:00, not Jan 10 at 8:00
        DateTimeOffset? finalFire = trigger.FinalFireTimeUtc;
        Assert.IsNotNull(finalFire);
        Assert.AreEqual(new DateTimeOffset(2025, 1, 9, 9, 0, 0, TimeSpan.Zero), finalFire);
    }

    [Test]
    public void TestFinalFireTimeUtcNoEnd()
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl();
        trigger.RecurrenceRule = "FREQ=DAILY";
        trigger.StartTimeUtc = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        trigger.TimeZone = TimeZoneInfo.Utc;

        Assert.IsNull(trigger.FinalFireTimeUtc);
    }

    [Test]
    public void TestPersistenceDelegateRoundTrip()
    {
        RecurrenceTriggerPersistenceDelegate del = new RecurrenceTriggerPersistenceDelegate();

        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl();
        trigger.RecurrenceRule = "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR";
        trigger.TimesTriggered = 7;
        trigger.TimeZone = TimeZoneInfo.Utc;
        trigger.StartTimeUtc = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);

        Assert.IsTrue(del.CanHandleTriggerType(trigger));
        Assert.AreEqual("RECUR", del.GetHandledTriggerTypeDiscriminator());

        // Serialize to properties
        SimplePropertiesTriggerProperties props = (SimplePropertiesTriggerProperties)
            typeof(RecurrenceTriggerPersistenceDelegate)
                .GetMethod("GetTriggerProperties", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(del, new object[] { trigger })!;

        Assert.AreEqual("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR", props.String1);
        Assert.AreEqual(7, props.Int1);
        Assert.AreEqual("UTC", props.TimeZoneId);

        // Deserialize from properties
        TriggerPropertyBundle bundle = (TriggerPropertyBundle)
            typeof(RecurrenceTriggerPersistenceDelegate)
                .GetMethod("GetTriggerPropertyBundle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(del, new object[] { props })!;

        IMutableTrigger rebuilt = bundle.ScheduleBuilder.Build();
        Assert.IsInstanceOf<RecurrenceTriggerImpl>(rebuilt);

        IRecurrenceTrigger recRebuilt = (IRecurrenceTrigger)rebuilt;
        Assert.AreEqual("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR", recRebuilt.RecurrenceRule);
        Assert.AreEqual(TimeZoneInfo.Utc, recRebuilt.TimeZone);

        // Verify state properties
        Assert.AreEqual("timesTriggered", bundle.StatePropertyNames![0]);
        Assert.AreEqual(7, bundle.StatePropertyValues![0]);
    }

    [Test]
    public void TestCountExhaustionViaTimesTriggered()
    {
        // Verify that TimesTriggered is the single source of truth for COUNT
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl();
        trigger.RecurrenceRule = "FREQ=DAILY;COUNT=3";
        trigger.StartTimeUtc = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        trigger.TimeZone = TimeZoneInfo.Utc;

        // Simulate that trigger has already fired 3 times externally
        trigger.TimesTriggered = 3;

        // GetFireTimeAfter should return null because TimesTriggered >= COUNT
        DateTimeOffset? next = trigger.GetFireTimeAfter(new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero));
        Assert.IsNull(next);
    }
}
