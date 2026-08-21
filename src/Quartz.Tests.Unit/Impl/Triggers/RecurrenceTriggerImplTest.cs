using Quartz.Impl.AdoJobStore;
using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Extensibility;

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
        Assert.IsNotNull(trigger.NextFireTimeUtc);
        Assert.AreEqual(new DateTimeOffset(2025, 1, 2, 9, 0, 0, TimeSpan.Zero), trigger.NextFireTimeUtc);
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
        Assert.IsNotNull(trigger.NextFireTimeUtc);

        // Fire 2 - should exhaust COUNT
        trigger.Triggered(null);

        // TimesTriggered is now 2, which equals COUNT=2
        // GetFireTimeAfter should return null
        Assert.AreEqual(2, trigger.TimesTriggered);
        Assert.IsNull(trigger.GetFireTimeAfter(trigger.PreviousFireTimeUtc));
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

        Assert.IsNull(trigger.NextFireTimeUtc);
    }

    [Test]
    public void TestCalendarExclusionSkipsExcludedDates()
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl();
        trigger.RecurrenceRule = "FREQ=DAILY";
        trigger.StartTimeUtc = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        trigger.TimeZone = TimeZoneInfo.Utc;

        // Exclude Jan 2 via AnnualCalendar
        AnnualCalendar calendar = new AnnualCalendar();
        calendar.AddExcludedDay(new MonthDay(1, 2));

        trigger.ComputeFirstFireTimeUtc(calendar);
        // First fire = Jan 1
        Assert.AreEqual(new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero), trigger.NextFireTimeUtc);

        // After triggering, next should skip Jan 2 (excluded) and land on Jan 3
        trigger.Triggered(calendar);
        Assert.AreEqual(new DateTimeOffset(2025, 1, 3, 9, 0, 0, TimeSpan.Zero), trigger.NextFireTimeUtc);
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
        trigger.MisfireInstructionCode = MisfireInstruction.RecurrenceTrigger.DoNothing;

        IScheduleBuilder sb = trigger.GetScheduleBuilder();
        ITrigger rebuilt = TriggerBuilder.Create()
            .WithIdentity("test")
            .WithSchedule(sb)
            .StartAt(trigger.StartTimeUtc)
            .Build();

        Assert.IsInstanceOf<IRecurrenceTrigger>(rebuilt);
        IRecurrenceTrigger recTrigger = (IRecurrenceTrigger)rebuilt;
        Assert.AreEqual("FREQ=MONTHLY;BYDAY=2MO", recTrigger.RecurrenceRule);
        Assert.AreEqual(MisfireInstruction.RecurrenceTrigger.DoNothing, rebuilt.MisfireInstructionCode);
    }

    [Test]
    public void TestMisfireInstructionValidation()
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl();
        trigger.RecurrenceRule = "FREQ=DAILY";
        trigger.StartTimeUtc = DateTimeOffset.UtcNow;

        // Valid values
        trigger.MisfireInstructionCode = MisfireInstruction.SmartPolicy;
        trigger.MisfireInstructionCode = MisfireInstruction.IgnoreMisfirePolicy;
        trigger.MisfireInstructionCode = MisfireInstruction.RecurrenceTrigger.FireOnceNow;
        trigger.MisfireInstructionCode = MisfireInstruction.RecurrenceTrigger.DoNothing;

        // Invalid value
        Assert.Throws<ArgumentException>(() => trigger.MisfireInstructionCode = 99);
    }

    [Test]
    public void TestMayFireAgain()
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl();
        trigger.RecurrenceRule = "FREQ=DAILY";
        trigger.StartTimeUtc = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        trigger.TimeZone = TimeZoneInfo.Utc;

        trigger.ComputeFirstFireTimeUtc(null);
        Assert.IsTrue(trigger.MayFireAgain);
    }

    [Test]
    public void TestStartTimeKeepsItsMilliseconds()
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl();
        trigger.StartTimeUtc = new DateTimeOffset(1982, 6, 28, 13, 5, 5, 233, TimeSpan.Zero);

        // The trigger reports no millisecond precision, but it overrides StartTimeUtc and so never
        // reaches TriggerBase's round-down-to-the-second - unlike CronTriggerImpl, which does.
        // Recorded as it behaves: rounding here would move the fire times of existing triggers.
        trigger.StartTimeUtc.Millisecond.Should().Be(233);
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
        Assert.AreEqual(new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero), trigger.NextFireTimeUtc);

        trigger.Triggered(null);
        // Next should be Friday Jan 3
        Assert.AreEqual(new DateTimeOffset(2025, 1, 3, 9, 0, 0, TimeSpan.Zero), trigger.NextFireTimeUtc);

        trigger.Triggered(null);
        // Next should be Monday Jan 6
        Assert.AreEqual(new DateTimeOffset(2025, 1, 6, 9, 0, 0, TimeSpan.Zero), trigger.NextFireTimeUtc);
    }

    [Test]
    public void TestBuilderCreatesCorrectTrigger()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("test", "group")
            .WithRecurrenceSchedule("FREQ=MONTHLY;BYDAY=2MO", b => b
                .InTimeZone(TimeZoneInfo.Utc)
                .WithMisfireInstruction(RecurrenceTriggerMisfireInstruction.DoNothing))
            .StartAt(new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero))
            .Build();

        Assert.IsInstanceOf<RecurrenceTriggerImpl>(trigger);
        IRecurrenceTrigger recTrigger = (IRecurrenceTrigger)trigger;
        Assert.AreEqual("FREQ=MONTHLY;BYDAY=2MO", recTrigger.RecurrenceRule);
        Assert.AreEqual(TimeZoneInfo.Utc, recTrigger.TimeZone);
        Assert.AreEqual(MisfireInstruction.RecurrenceTrigger.DoNothing, trigger.MisfireInstructionCode);
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

        // Verify state applier restores the fire count onto the rebuilt trigger
        bundle.ApplyState.Should().NotBeNull("the recurrence delegate persists TimesTriggered");
        bundle.ApplyState!((RecurrenceTriggerImpl) rebuilt);
        ((RecurrenceTriggerImpl) rebuilt).TimesTriggered.Should().Be(7);
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

    #region UpdateAfterMisfire

    /// <summary>
    /// The schedule every misfire test below starts from: daily at 09:00 UTC from 2025-01-01.
    /// </summary>
    private static readonly DateTimeOffset MisfireStartTime = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The frozen clock misfire handling runs on. It is four days and three hours past the schedule's
    /// first fire, so the trigger is badly past due and every rescheduling policy has to move it.
    /// </summary>
    private static readonly DateTimeOffset MisfireNow = new DateTimeOffset(2025, 1, 5, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A daily trigger whose one computed fire time, <see cref="MisfireStartTime" />, is long past by
    /// the time the frozen <see cref="MisfireNow" /> clock is read - the state the misfire handler
    /// finds a trigger in.
    /// </summary>
    private static RecurrenceTriggerImpl CreateMisfiredTrigger(int misfireInstruction, string rule = "FREQ=DAILY")
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl("misfired", "test", rule, new FixedTimeProvider(MisfireNow))
        {
            TimeZone = TimeZoneInfo.Utc,
            StartTimeUtc = MisfireStartTime,
            MisfireInstructionCode = misfireInstruction
        };
        trigger.ComputeFirstFireTimeUtc(null);
        trigger.NextFireTimeUtc.Should().Be(MisfireStartTime, "the fixture depends on the trigger being past due");
        return trigger;
    }

    [Test]
    public void UpdateAfterMisfire_DoNothing_SkipsThePastDueFiresAndResumesAfterNow()
    {
        RecurrenceTriggerImpl trigger = CreateMisfiredTrigger(MisfireInstruction.RecurrenceTrigger.DoNothing);

        trigger.UpdateAfterMisfire(null);

        trigger.NextFireTimeUtc.Should().Be(new DateTimeOffset(2025, 1, 6, 9, 0, 0, TimeSpan.Zero),
            "DoNothing recomputes from 'now', and the first daily 09:00 after 2025-01-05 12:00 is the next day");
        trigger.TimesTriggered.Should().Be(0, "the fires that were skipped never happened, so none of them may count against COUNT");
        trigger.PreviousFireTimeUtc.Should().BeNull("misfire handling is not a firing");
    }

    [Test]
    public void UpdateAfterMisfire_FireOnceNow_SchedulesTheTriggerForExactlyNow()
    {
        RecurrenceTriggerImpl trigger = CreateMisfiredTrigger(MisfireInstruction.RecurrenceTrigger.FireOnceNow);

        trigger.UpdateAfterMisfire(null);

        trigger.NextFireTimeUtc.Should().Be(MisfireNow, "FireOnceNow catches up with a single fire, taken from the trigger's clock");
        trigger.TimesTriggered.Should().Be(0);
    }

    [Test]
    public void UpdateAfterMisfire_SmartPolicy_IsFireOnceNow()
    {
        RecurrenceTriggerImpl trigger = CreateMisfiredTrigger(MisfireInstruction.SmartPolicy);

        trigger.UpdateAfterMisfire(null);

        trigger.NextFireTimeUtc.Should().Be(MisfireNow,
            "the recurrence family resolves SmartPolicy to FireOnceNow, which RecurrenceTriggerMisfireInstruction.SmartPolicy documents");
    }

    [Test]
    public void UpdateAfterMisfire_IgnoreMisfirePolicy_LeavesTheScheduleUntouched()
    {
        RecurrenceTriggerImpl trigger = CreateMisfiredTrigger(MisfireInstruction.IgnoreMisfirePolicy);

        trigger.UpdateAfterMisfire(null);

        trigger.NextFireTimeUtc.Should().Be(MisfireStartTime,
            "ignoring misfires means the past-due fire time stays put, so the trigger fires its way back up to date");
        trigger.TimesTriggered.Should().Be(0);
        trigger.PreviousFireTimeUtc.Should().BeNull();
    }

    [Test]
    public void UpdateAfterMisfire_DoNothing_SkipsCalendarExcludedFireTimes()
    {
        RecurrenceTriggerImpl trigger = CreateMisfiredTrigger(MisfireInstruction.RecurrenceTrigger.DoNothing);

        // The day DoNothing would land on is excluded, so it has to walk on to the seventh.
        AnnualCalendar calendar = new AnnualCalendar { TimeZone = TimeZoneInfo.Utc };
        calendar.AddExcludedDay(new MonthDay(1, 6));

        trigger.UpdateAfterMisfire(calendar);

        trigger.NextFireTimeUtc.Should().Be(new DateTimeOffset(2025, 1, 7, 9, 0, 0, TimeSpan.Zero),
            "2025-01-06 is excluded, so the rescheduled fire time is the next included occurrence");
    }

    [Test]
    public void UpdateAfterMisfire_DoNothing_ClearsTheFireTimeWhenTheEndTimeHasPassed()
    {
        RecurrenceTriggerImpl trigger = CreateMisfiredTrigger(MisfireInstruction.RecurrenceTrigger.DoNothing);

        // The end time expired while the scheduler was down, so there is nothing left to resume at.
        trigger.EndTimeUtc = new DateTimeOffset(2025, 1, 3, 9, 0, 0, TimeSpan.Zero);

        trigger.UpdateAfterMisfire(null);

        trigger.NextFireTimeUtc.Should().BeNull("no occurrence remains after 'now' and before the end time");
        trigger.MayFireAgain.Should().BeFalse("a null fire time is how the stores learn the trigger is complete");
    }

    [Test]
    public void UpdateAfterMisfire_DoNothing_ClearsTheFireTimeWhenTheCountIsExhausted()
    {
        RecurrenceTriggerImpl trigger = CreateMisfiredTrigger(MisfireInstruction.RecurrenceTrigger.DoNothing, "FREQ=DAILY;COUNT=3");
        trigger.TimesTriggered = 3;

        trigger.UpdateAfterMisfire(null);

        trigger.NextFireTimeUtc.Should().BeNull(
            "TimesTriggered is the single source of truth for COUNT, and it is already at the limit");
        trigger.MayFireAgain.Should().BeFalse();
    }

    [Test]
    public void UpdateAfterMisfire_FireOnceNow_FiresPastTheEndTime()
    {
        RecurrenceTriggerImpl trigger = CreateMisfiredTrigger(MisfireInstruction.RecurrenceTrigger.FireOnceNow);
        trigger.EndTimeUtc = new DateTimeOffset(2025, 1, 3, 9, 0, 0, TimeSpan.Zero);

        trigger.UpdateAfterMisfire(null);

        trigger.NextFireTimeUtc.Should().Be(MisfireNow,
            "FireOnceNow assigns 'now' without consulting the end time - the same shape cron, calendar-interval and " +
            "daily-time-interval triggers have, and unlike SimpleTrigger's reschedule-now policies, which do check it");
    }

    [Test]
    public void UpdateAfterMisfire_FireOnceNow_FiresEvenWithTheCountExhausted()
    {
        RecurrenceTriggerImpl trigger = CreateMisfiredTrigger(MisfireInstruction.RecurrenceTrigger.FireOnceNow, "FREQ=DAILY;COUNT=3");
        trigger.TimesTriggered = 3;

        trigger.UpdateAfterMisfire(null);

        trigger.NextFireTimeUtc.Should().Be(MisfireNow,
            "FireOnceNow never asks the rule for a time, so an exhausted COUNT does not stop the catch-up fire; " +
            "the fire after it is null, which is where the trigger completes");
        trigger.GetFireTimeAfter(MisfireNow).Should().BeNull();
    }

    [Test]
    public void UpdateAfterMisfire_DoNothing_IsIdempotent()
    {
        RecurrenceTriggerImpl trigger = CreateMisfiredTrigger(MisfireInstruction.RecurrenceTrigger.DoNothing);

        trigger.UpdateAfterMisfire(null);
        DateTimeOffset? afterFirst = trigger.NextFireTimeUtc;
        trigger.UpdateAfterMisfire(null);

        trigger.NextFireTimeUtc.Should().Be(afterFirst,
            "the recomputation reads 'now' rather than the current fire time, so a repeated recovery pass must not walk the schedule forward");
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    #endregion
}
