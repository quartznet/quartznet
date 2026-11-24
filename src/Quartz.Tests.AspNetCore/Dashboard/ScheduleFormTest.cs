using Microsoft.Extensions.Time.Testing;

using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// <see cref="ScheduleForm" />: the one place a schedule of any of the five kinds becomes the trigger the dashboard
/// posts (#4412), cloned so that nothing #3294 lost is lost again.
/// </summary>
public sealed class ScheduleFormTest
{
    /// <summary>A Monday, 10:30 UTC.</summary>
    private static readonly DateTimeOffset now = new(2031, 6, 16, 10, 30, 0, TimeSpan.Zero);

    /// <summary>A year before <see cref="now" />, at 09:00: every trigger here started long ago.</summary>
    private static readonly DateTimeOffset longAgo = new(2030, 6, 16, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider clock = new(now);

    /// <summary>Each kind by name: the kinds are internal, and a test method's parameters are public.</summary>
    public static IEnumerable<string> EveryKind() => Enum.GetNames<ScheduleKind>();

    [TestCaseSource(nameof(EveryKind))]
    public void TheFormReadsTheTriggersOwnSchedule(string kindName)
    {
        ScheduleKind kind = Enum.Parse<ScheduleKind>(kindName);
        ITrigger trigger = Stored(kind);

        ScheduleForm form = ScheduleForm.From(trigger)!;

        form.Kind.Should().Be(kind);
        form.SameScheduleAs(ScheduleForm.From(trigger)!).Should().BeTrue();
        ScheduleBuild build = form.Apply(trigger, calendar: null, clock);
        build.Errors.Should().BeEmpty("a schedule read back as it is is a schedule that builds");
        ((TriggerBase) build.Trigger!).HasSameScheduleAs((TriggerBase) trigger).Should().BeTrue(
            "an unedited form posts the schedule the trigger has");
    }

    [Test(Description = "https://github.com/quartznet/quartznet/issues/3294")]
    [TestCaseSource(nameof(EveryKind))]
    public void AnEditChangesTheScheduleAndCarriesEverythingElseThroughUntouched(string kindName)
    {
        ScheduleKind kind = Enum.Parse<ScheduleKind>(kindName);
        ITrigger trigger = Stored(kind, calendarName: null);
        ScheduleForm form = ScheduleForm.From(trigger)!;
        Edit(form);

        ITrigger payload = form.Apply(trigger, calendar: null, clock).Trigger!;

        payload.Should().BeOfType(trigger.GetType(), "the edit must not change what kind of trigger this is");
        payload.Should().NotBeSameAs(trigger);
        ((TriggerBase) payload).HasSameScheduleAs((TriggerBase) trigger).Should().BeFalse("the schedule is what was edited");
        payload.CalendarName.Should().BeNull("an empty calendar name names a calendar that cannot be found, and the trigger then never fires again");
        payload.Key.Should().Be(trigger.Key);
        payload.JobKey.Should().Be(trigger.JobKey);
        payload.Description.Should().Be("nightly import");
        payload.ExecutionGroup.Should().Be("imports");
        payload.Priority.Should().Be(7);
        payload.MisfireInstructionCode.Should().Be(trigger.MisfireInstructionCode);
        payload.PreferredNode.Node.Should().Be("node-a", "the hand-written payload dropped the node pin, which silently unpinned the trigger");
        payload.JobDataMap["colour"].Should().Be("green");
    }

    [TestCaseSource(nameof(EveryKind))]
    public void TheTriggerItWasGivenIsNotTouched(string kindName)
    {
        ScheduleKind kind = Enum.Parse<ScheduleKind>(kindName);
        ITrigger trigger = Stored(kind);
        DateTimeOffset? next = trigger.NextFireTimeUtc;
        ScheduleForm form = ScheduleForm.From(trigger)!;
        Edit(form);

        form.Apply(trigger, calendar: null, clock).Trigger.Should().NotBeNull();

        ((TriggerBase) trigger).HasSameScheduleAs((TriggerBase) Stored(kind)).Should().BeTrue(
            "the page still shows the trigger it loaded until the reschedule comes back");
        trigger.NextFireTimeUtc.Should().Be(next);
    }

    /// <summary>
    /// A reschedule replaces the trigger, and the confirm dialog says what goes with the old one.
    /// </summary>
    [TestCaseSource(nameof(EveryKind))]
    public void WhatTheOldTriggerHadDoneIsReset(string kindName)
    {
        ScheduleKind kind = Enum.Parse<ScheduleKind>(kindName);
        ITrigger trigger = Stored(kind);
        IOperableTrigger operable = (IOperableTrigger) trigger;
        operable.PreviousFireTimeUtc = now.AddHours(-1);
        ((TriggerBase) trigger).RetryAttempt = 2;
        SetTimesTriggered(trigger, 12);
        ScheduleForm form = ScheduleForm.From(trigger)!;
        Edit(form);

        IOperableTrigger payload = form.Apply(trigger, calendar: null, clock).Trigger!;

        payload.PreviousFireTimeUtc.Should().BeNull();
        payload.RetryAttempt.Should().Be(0, "a pending retry is dropped");
        TimesTriggered(payload).Should().Be(0, "a repeat count starts over");
    }

    /// <summary>
    /// Every kind but cron counts its first fire from its start, and a start a year gone would be stored as a fire a
    /// year late: a misfire the moment the trigger is saved.
    /// </summary>
    [TestCaseSource(nameof(EveryKind))]
    public void TheFirstFireIsTheSchedulesFirstAfterNow(string kindName)
    {
        ScheduleKind kind = Enum.Parse<ScheduleKind>(kindName);
        ITrigger trigger = Stored(kind);
        ScheduleForm form = ScheduleForm.From(trigger)!;
        Edit(form);

        IOperableTrigger payload = form.Apply(trigger, calendar: null, clock).Trigger!;

        payload.NextFireTimeUtc.Should().NotBeNull().And.BeOnOrAfter(now);
        payload.NextFireTimeUtc.Should().Be(Expected(kind), "it is the edited schedule's first fire after now, in the phase its start gives it");
    }

    [Test]
    public void ASimpleTriggerStartsAtItsFirstFireSoTheSchedulerDoesNotMoveIt()
    {
        ITrigger trigger = Stored(ScheduleKind.Simple);
        ScheduleForm form = ScheduleForm.From(trigger)!;
        form.SimpleInterval = "30";

        IOperableTrigger payload = form.Apply(trigger, calendar: null, clock).Trigger!;

        payload.StartTimeUtc.Should().Be(payload.NextFireTimeUtc!.Value,
            "RescheduleJob moves a simple trigger's start that is behind it up to the moment it stores it, and every fire after the first would move with it");
        TriggerFireTimes.Compute(payload, null, 3).Should().Equal(
            [new DateTimeOffset(2031, 6, 16, 11, 0, 0, TimeSpan.Zero), new DateTimeOffset(2031, 6, 16, 11, 30, 0, TimeSpan.Zero), new DateTimeOffset(2031, 6, 16, 12, 0, 0, TimeSpan.Zero)]);
    }

    [Test]
    public void TheFirstFireSkipsWhatTheCalendarExcludes()
    {
        ITrigger trigger = Stored(ScheduleKind.CalendarInterval, calendarName: "holidays");
        ScheduleForm form = ScheduleForm.From(trigger)!;
        HolidayCalendar holidays = new() { TimeZone = TimeZoneInfo.Utc };
        holidays.AddExcludedDay(new DateOnly(2031, 6, 17));

        IOperableTrigger payload = form.Apply(trigger, holidays, clock).Trigger!;

        payload.NextFireTimeUtc.Should().Be(new DateTimeOffset(2031, 6, 18, 9, 0, 0, TimeSpan.Zero),
            "the 17th is a holiday, so the daily 09:00 fire after now is the 18th's, as the scheduler would store it");
    }

    [Test]
    public void ADailyWindowMovesWholeEarlierOrLater()
    {
        ITrigger trigger = Stored(ScheduleKind.DailyTimeInterval);
        ScheduleForm later = ScheduleForm.From(trigger)!;
        later.DailyStart = "18:00";
        later.DailyEnd = "20:00";
        ScheduleForm earlier = ScheduleForm.From(trigger)!;
        earlier.DailyStart = "06:00";
        earlier.DailyEnd = "07:00";

        DailyTimeIntervalTriggerImpl movedLater = (DailyTimeIntervalTriggerImpl) later.Apply(trigger, null, clock).Trigger!;
        DailyTimeIntervalTriggerImpl movedEarlier = (DailyTimeIntervalTriggerImpl) earlier.Apply(trigger, null, clock).Trigger!;

        movedLater.StartTimeOfDay.Should().Be(new TimeOnly(18, 0));
        movedLater.EndTimeOfDay.Should().Be(new TimeOnly(20, 0), "the end is moved first, so the start never passes it on the way");
        movedEarlier.StartTimeOfDay.Should().Be(new TimeOnly(6, 0));
        movedEarlier.EndTimeOfDay.Should().Be(new TimeOnly(7, 0));
    }

    [Test]
    public void ATriggerOfATypeTheFormCannotRebuildIsRefused()
    {
        // A cron schedule this cannot set without knowing the type. Guessing means posting back some other trigger
        // than the one on screen, which is how a custom cron trigger used to be rewritten as a plain one.
        ICronTrigger foreign = FakeItEasy.A.Fake<ICronTrigger>();

        ScheduleForm.From(foreign).Should().BeNull();
        ScheduleForm.KindOf(foreign).Should().BeNull();
    }

    [Test]
    public void ATypeDerivedFromABuiltInOneSurvivesTheEditAsItself()
    {
        CustomSimpleTrigger trigger = new()
        {
            Key = new TriggerKey("custom", "group1"),
            JobKey = new JobKey("job1", "group1"),
            StartTimeUtc = longAgo,
            RepeatInterval = TimeSpan.FromMinutes(15),
            RepeatCount = SimpleTriggerImpl.RepeatIndefinitely,
        };
        ScheduleForm form = ScheduleForm.From(trigger)!;
        form.SimpleInterval = "20";

        form.Apply(trigger, null, clock).Trigger.Should().BeOfType<CustomSimpleTrigger>();
    }

    [TestCase("Cron", "CronExpression")]
    [TestCase("Simple", "SimpleInterval")]
    [TestCase("CalendarInterval", "CalendarInterval")]
    [TestCase("DailyTimeInterval", "DailyEnd")]
    [TestCase("Recurrence", "RecurrenceRule")]
    public void AWrongFieldIsSaidBesideItAndNothingIsBuilt(string kindName, string fieldName)
    {
        ScheduleKind kind = Enum.Parse<ScheduleKind>(kindName);
        ScheduleField field = Enum.Parse<ScheduleField>(fieldName);
        ITrigger trigger = Stored(kind);
        ScheduleForm form = ScheduleForm.From(trigger)!;
        switch (kind)
        {
            case ScheduleKind.Cron:
                form.CronExpression = "0 0 25 * * ?";
                break;
            case ScheduleKind.Simple:
                form.SimpleInterval = "0";
                break;
            case ScheduleKind.CalendarInterval:
                form.CalendarInterval = "none";
                break;
            case ScheduleKind.DailyTimeInterval:
                form.DailyStart = "17:00";
                form.DailyEnd = "09:00";
                break;
            default:
                form.RecurrenceRule = "FREQ=FORTNIGHTLY";
                break;
        }

        ScheduleBuild build = form.Apply(trigger, null, clock);

        build.Trigger.Should().BeNull();
        build.Errors.Should().ContainSingle().Which.Field.Should().Be(field);
    }

    [Test]
    public void ADailyScheduleOnNoDayAndAnUnknownZoneAreRefused()
    {
        ITrigger trigger = Stored(ScheduleKind.DailyTimeInterval);
        ScheduleForm form = ScheduleForm.From(trigger)!;
        form.DailyDays.Clear();
        form.TimeZoneId = "Mars/Olympus_Mons";

        ScheduleBuild build = form.Apply(trigger, null, clock);

        build.Errors.Select(x => x.Field).Should().BeEquivalentTo([ScheduleField.DailyDays, ScheduleField.TimeZone]);
    }

    [Test]
    public void AScheduleThatNeverFiresAgainIsRefused()
    {
        ITrigger trigger = Stored(ScheduleKind.Cron);
        ((IOperableTrigger) trigger).EndTimeUtc = now.AddDays(-1);
        ScheduleForm form = ScheduleForm.From(trigger)!;
        form.CronExpression = "0 0 12 * * ?";

        ScheduleBuild build = form.Apply(trigger, null, clock);

        build.Errors.Should().ContainSingle().Which.Should().Match<ScheduleFieldError>(
            error => error.Field == ScheduleField.Schedule && error.Message == "This schedule does not fire again after now.");
    }

    [TestCaseSource(nameof(EveryKind))]
    public void ANewTriggerOfEachKindBuildsFromTheFormsDefaults(string kindName)
    {
        ScheduleKind kind = Enum.Parse<ScheduleKind>(kindName);
        ScheduleForm form = ScheduleForm.ForNew(kind, TimeZoneInfo.Utc.Id);
        IOperableTrigger template = ScheduleForm.NewTrigger(kind);
        template.Key = new TriggerKey("new", "group1");
        template.JobKey = new JobKey("job1", "group1");
        template.StartTimeUtc = now;

        ScheduleBuild build = form.Apply(template, null, clock);

        build.Errors.Should().BeEmpty();
        build.Trigger!.NextFireTimeUtc.Should().NotBeNull().And.BeOnOrAfter(now);
        build.Trigger.DescribeSchedule().Should().NotBeNullOrEmpty();
    }

    [Test]
    public void AFormOfOneKindRefusesATriggerOfAnother()
    {
        ScheduleForm form = ScheduleForm.ForNew(ScheduleKind.Recurrence, TimeZoneInfo.Utc.Id);

        ScheduleBuild build = form.Apply(Stored(ScheduleKind.Cron), null, clock);

        build.Trigger.Should().BeNull();
        build.Errors.Should().ContainSingle().Which.Field.Should().Be(ScheduleField.Schedule);
    }

    /// <summary>
    /// A trigger of <paramref name="kind" /> as the API client hands the detail page: started a year ago at 09:00
    /// UTC, every field populated, including the ones the page never displays.
    /// </summary>
    private static ITrigger Stored(ScheduleKind kind, string? calendarName = "SomeCalendar")
    {
        TriggerBuilder<IJob> builder = TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .ForJob("job1", "group1")
            .WithDescription("nightly import")
            .WithCalendarName(calendarName)
            .UsingJobData("colour", "green")
            .StartAt(longAgo)
            .WithPriority(7)
            .WithExecutionGroup("imports")
            .WithPreferredNode(PreferredNode.For("node-a"));

        builder = kind switch
        {
            ScheduleKind.Cron => builder.WithCronSchedule("0 0 9 * * ?", x => x.InTimeZone(TimeZoneInfo.Utc)),
            ScheduleKind.Simple => builder.WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(15)).RepeatForever()),
            ScheduleKind.CalendarInterval => builder.WithCalendarIntervalSchedule(x => x.WithInterval(1, IntervalUnit.Day).InTimeZone(TimeZoneInfo.Utc)),
            ScheduleKind.DailyTimeInterval => builder.WithDailyTimeIntervalSchedule(x => x
                .OnMondayThroughFriday()
                .StartingDailyAt(new TimeOnly(9, 0))
                .EndingDailyAt(new TimeOnly(17, 0))
                .WithInterval(1, IntervalUnit.Hour)
                .InTimeZone(TimeZoneInfo.Utc)),
            _ => builder.WithRecurrenceSchedule("FREQ=DAILY;BYHOUR=9;BYMINUTE=0;BYSECOND=0", x => x.InTimeZone(TimeZoneInfo.Utc)),
        };

        ITrigger trigger = builder.Build();
        ((IOperableTrigger) trigger).NextFireTimeUtc = now.AddMinutes(10);
        return trigger;
    }

    /// <summary>One edit of each kind's schedule.</summary>
    private static void Edit(ScheduleForm form)
    {
        switch (form.Kind)
        {
            case ScheduleKind.Cron:
                form.CronExpression = "0 0 12 * * ?";
                break;
            case ScheduleKind.Simple:
                form.SimpleInterval = "30";
                break;
            case ScheduleKind.CalendarInterval:
                form.CalendarInterval = "2";
                break;
            case ScheduleKind.DailyTimeInterval:
                form.DailyStart = "08:00";
                form.DailyEnd = "18:00";
                break;
            default:
                form.RecurrenceRule = "FREQ=WEEKLY;BYDAY=TU;BYHOUR=9;BYMINUTE=0;BYSECOND=0";
                break;
        }
    }

    /// <summary>The first fire after <see cref="now" /> of each kind's <see cref="Edit" />.</summary>
    private static DateTimeOffset Expected(ScheduleKind kind) => kind switch
    {
        // Noon today.
        ScheduleKind.Cron => new DateTimeOffset(2031, 6, 16, 12, 0, 0, TimeSpan.Zero),

        // Every 30 minutes from 09:00 a year ago: 11:00 is the first after 10:30.
        ScheduleKind.Simple => new DateTimeOffset(2031, 6, 16, 11, 0, 0, TimeSpan.Zero),

        // Every two days at 09:00 from 2030-06-16: 2031-06-16 is 365 days on, an odd count, so the 17th.
        ScheduleKind.CalendarInterval => new DateTimeOffset(2031, 6, 17, 9, 0, 0, TimeSpan.Zero),

        // Hourly from 08:00 on weekdays: 11:00 today.
        ScheduleKind.DailyTimeInterval => new DateTimeOffset(2031, 6, 16, 11, 0, 0, TimeSpan.Zero),

        // Tuesdays at 09:00: tomorrow.
        _ => new DateTimeOffset(2031, 6, 17, 9, 0, 0, TimeSpan.Zero),
    };

    private static void SetTimesTriggered(ITrigger trigger, int count)
    {
        switch (trigger)
        {
            case SimpleTriggerImpl simple:
                simple.TimesTriggered = count;
                break;
            case CalendarIntervalTriggerImpl calendar:
                calendar.TimesTriggered = count;
                break;
            case DailyTimeIntervalTriggerImpl daily:
                daily.TimesTriggered = count;
                break;
            case RecurrenceTriggerImpl recurrence:
                recurrence.TimesTriggered = count;
                break;
        }
    }

    private static int TimesTriggered(ITrigger trigger) => trigger switch
    {
        ISimpleTrigger simple => simple.TimesTriggered,
        ICalendarIntervalTrigger calendar => calendar.TimesTriggered,
        IDailyTimeIntervalTrigger daily => daily.TimesTriggered,
        IRecurrenceTrigger recurrence => recurrence.TimesTriggered,
        _ => 0,
    };

    private sealed class CustomSimpleTrigger : SimpleTriggerImpl;
}