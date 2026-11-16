using System.Globalization;

using FakeItEasy;

using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit;

/// <summary>
/// What <see cref="TriggerScheduleDescriptionExtensions.DescribeSchedule" /> says for each trigger kind, and
/// that it falls back to the expression rather than saying something the schedule does not do.
/// </summary>
public class TriggerScheduleDescriptionTest
{
    // The issue's table (#4243), word for word.
    [TestCase("0 0 7 ? * MON-FRI", "America/Chicago", "Every weekday at 07:00 (America/Chicago)")]
    [TestCase("0 0 12 L * ?", "UTC", "At 12:00 on the last day of every month (UTC)")]
    // L, LW, L-n, L-nW, W and a day of the month beside L
    [TestCase("0 0 12 LW * ?", "UTC", "At 12:00 on the last weekday of every month (UTC)")]
    [TestCase("0 0 12 L-1 * ?", "UTC", "At 12:00 on the day before the last day of every month (UTC)")]
    [TestCase("0 0 12 L-3 * ?", "UTC", "At 12:00 on the day 3 days before the last day of every month (UTC)")]
    [TestCase("0 0 12 L-3W * ?", "UTC", "At 12:00 on the weekday nearest the day 3 days before the last day of every month (UTC)")]
    [TestCase("0 0 12 15W * ?", "UTC", "At 12:00 on the weekday nearest the 15th of every month (UTC)")]
    [TestCase("0 0 12 1,L * ?", "UTC", "At 12:00 on the 1st and the last day of every month (UTC)")]
    // # and the last of a weekday, by number and by name
    [TestCase("0 15 10 ? * 6#3", "UTC", "At 10:15 on the third Friday of every month (UTC)")]
    [TestCase("0 15 10 ? * FRI#3", "UTC", "At 10:15 on the third Friday of every month (UTC)")]
    [TestCase("0 15 10 ? * 6L", "UTC", "At 10:15 on the last Friday of every month (UTC)")]
    [TestCase("0 15 10 ? * FRIL", "UTC", "At 10:15 on the last Friday of every month (UTC)")]
    [TestCase("0 0 9 ? * L", "UTC", "Every Saturday at 09:00 (UTC)")]
    // the year field
    [TestCase("0 15 10 ? * 6L 2002-2005", "UTC", "At 10:15 on the last Friday of every month from 2002 to 2005 (UTC)")]
    [TestCase("0 15 10 * * ? 2030", "UTC", "Every day at 10:15 in 2030 (UTC)")]
    [TestCase("0 15 10 * * ? 2030,2032", "UTC", "Every day at 10:15 in 2030 and 2032 (UTC)")]
    [TestCase("0 15 10 * * ? 2026/2", "UTC", "Every day at 10:15 every 2 years from 2026 (UTC)")]
    [TestCase("0 15 10 * * ? *", "UTC", "Every day at 10:15 (UTC)")]
    // seconds
    [TestCase("30 0 9 * * ?", "UTC", "Every day at 09:00:30 (UTC)")]
    [TestCase("* * * * * ?", "UTC", "Every second (UTC)")]
    [TestCase("0/30 * * * * ?", "UTC", "Every 30 seconds (UTC)")]
    [TestCase("*/10 * 14 * * ?", "UTC", "Every 10 seconds between 14:00:00 and 14:59:50 (UTC)")]
    [TestCase("0/10 30 14 * * ?", "UTC", "Every 10 seconds between 14:30:00 and 14:30:50 (UTC)")]
    [TestCase("10-20 * * * * ?", "UTC", "Every second from second 10 to second 20 of every minute (UTC)")]
    [TestCase("30 */5 * * * ?", "UTC", "Every 5 minutes, at second 30 (UTC)")]
    // ranges
    [TestCase("0 0-5 14 * * ?", "UTC", "Every minute between 14:00 and 14:05 (UTC)")]
    [TestCase("0 30 9-17 * * ?", "UTC", "Every hour from 09:30 to 17:30 (UTC)")]
    [TestCase("0 0 22-2 * * ?", "UTC", "Every hour from 22:00 to 02:00 (UTC)")]
    [TestCase("0 * 14 * * ?", "UTC", "Every minute between 14:00 and 14:59 (UTC)")]
    [TestCase("0 0-5 * * * ?", "UTC", "Every minute from minute 0 to minute 5 of every hour (UTC)")]
    // steps
    [TestCase("0 0/5 * * * ?", "UTC", "Every 5 minutes (UTC)")]
    [TestCase("0 5/15 * * * ?", "UTC", "Every 15 minutes, starting at minute 5 (UTC)")]
    [TestCase("0 0/25 * * * ?", "UTC", "Every 25 minutes from minute 0 to minute 50 of every hour (UTC)")]
    [TestCase("0 0/5 14 * * ?", "UTC", "Every 5 minutes between 14:00 and 14:55 (UTC)")]
    [TestCase("0 0/15 9-17 * * ?", "UTC", "Every 15 minutes between 09:00 and 17:45 (UTC)")]
    [TestCase("0 0 */2 * * ?", "UTC", "Every 2 hours (UTC)")]
    [TestCase("0 30 */2 * * ?", "UTC", "Every 2 hours from 00:30 (UTC)")]
    [TestCase("0 0 */5 * * ?", "UTC", "Every 5 hours from 00:00 to 20:00 (UTC)")]
    [TestCase("0 0 9-17/2 * * ?", "UTC", "Every 2 hours from 09:00 to 17:00 (UTC)")]
    [TestCase("0 0 12 1/5 * ?", "UTC", "At 12:00 on every 5th day from the 1st of every month (UTC)")]
    [TestCase("0 0 9 ? * MON/2", "UTC", "Every Monday, Wednesday and Friday at 09:00 (UTC)")]
    // lists
    [TestCase("0 0 8,12,18 * * ?", "UTC", "Every day at 08:00, 12:00 and 18:00 (UTC)")]
    [TestCase("0 0/5 14,18 * * ?", "UTC", "Every 5 minutes between 14:00 and 14:55 and between 18:00 and 18:55 (UTC)")]
    [TestCase("0 0,30 * * * ?", "UTC", "Every hour at minutes 0 and 30 (UTC)")]
    [TestCase("0 0 12 1,15 * ?", "UTC", "At 12:00 on the 1st and the 15th of every month (UTC)")]
    [TestCase("0 0 12 1-7 * ?", "UTC", "At 12:00 on the 1st to the 7th of every month (UTC)")]
    [TestCase("0 15 * * * ?", "UTC", "Every hour at minute 15 (UTC)")]
    // names, and ? and * in the day fields
    [TestCase("0 10,44 14 ? MAR WED", "UTC", "Every Wednesday at 14:10 and 14:44 in March (UTC)")]
    [TestCase("0 0 9 ? JAN,JUL MON", "UTC", "Every Monday at 09:00 in January and July (UTC)")]
    [TestCase("0 0 12 1 MAR-OCT ?", "UTC", "At 12:00 on the 1st of every month from March to October (UTC)")]
    [TestCase("0 0 12 1 11,12,1,2 ?", "UTC", "At 12:00 on the 1st of every month from November to February (UTC)")]
    [TestCase("0 0 10 ? * SAT,SUN", "UTC", "Every Saturday and Sunday at 10:00 (UTC)")]
    [TestCase("0 0 10 ? * MON-THU", "UTC", "Every Monday to Thursday at 10:00 (UTC)")]
    [TestCase("0 0 10 ? * 2-6", "UTC", "Every weekday at 10:00 (UTC)")]
    [TestCase("0 0 12 * * ?", "UTC", "Every day at 12:00 (UTC)")]
    [TestCase("0 0 12 ? * *", "UTC", "Every day at 12:00 (UTC)")]
    [TestCase("0 0 12 ? * SUN-SAT", "UTC", "Every day at 12:00 (UTC)")]
    [TestCase("0 0/15 9-17 ? * MON-FRI", "UTC", "Every 15 minutes between 09:00 and 17:45, every weekday (UTC)")]
    [TestCase("0 0 12 L * ?", "America/Chicago", "At 12:00 on the last day of every month (America/Chicago)")]
    // both day fields name days: the expression fires on either
    [TestCase("0 0 0 13 * FRI", "UTC", "At 00:00 on the 13th of every month and every Friday (UTC)")]
    [TestCase("0 0 0 13 3 FRI", "UTC", "At 00:00 on the 13th and every Friday in March (UTC)")]
    // the macros
    [TestCase("@daily", "UTC", "Every day at 00:00 (UTC)")]
    [TestCase("@hourly", "UTC", "Every hour (UTC)")]
    [TestCase("@weekly", "UTC", "Every Sunday at 00:00 (UTC)")]
    [TestCase("@monthly", "UTC", "At 00:00 on the 1st of every month (UTC)")]
    [TestCase("@yearly", "UTC", "At 00:00 on the 1st of January (UTC)")]
    // shapes it does not phrase are the expression itself
    [TestCase("0 0-5 9-17 * * ?", "UTC", "0 0-5 9-17 * * ? (UTC)")]
    [TestCase("0 0 12 LW-2 * ?", "UTC", "0 0 12 LW-2 * ? (UTC)")]
    [TestCase("0,15,30,45 0,30 8,20 * * ?", "UTC", "0,15,30,45 0,30 8,20 * * ? (UTC)")]
    [TestCase("0 0 12 13 * 6#3", "UTC", "0 0 12 13 * 6#3 (UTC)")]
    public void ACronTriggerReadsItsExpressionInItsZone(string expression, string zone, string expected)
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithCronSchedule(expression, x => x.InTimeZone(TimeZones.FindById(zone)))
            .Build();

        trigger.DescribeSchedule().Should().Be(expected);
    }

    [TestCase("30 4 * * 1", "Every Monday at 04:30 (UTC)")]
    [TestCase("30 4 * * 0", "Every Sunday at 04:30 (UTC)")]
    [TestCase("30 4 * * 7", "Every Sunday at 04:30 (UTC)")]
    [TestCase("*/15 9-17 * * 1-5", "Every 15 minutes between 09:00 and 17:45, every weekday (UTC)")]
    [TestCase("0 0 1 * *", "At 00:00 on the 1st of every month (UTC)")]
    public void AUnixExpressionReadsAsTheScheduleItWasRewrittenInto(string expression, string expected)
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithSchedule(CronScheduleBuilder.Create(expression, CronFormat.Unix).InTimeZone(TimeZoneInfo.Utc))
            .Build();

        trigger.DescribeSchedule().Should().Be(expected);
    }

    [Test]
    public void ACronTriggerWithACalendarSaysSo()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithCronSchedule("0 0 7 ? * MON-FRI", x => x.InTimeZone(TimeZoneInfo.Utc))
            .WithCalendarName("holidays")
            .Build();

        trigger.DescribeSchedule().Should().Be("Every weekday at 07:00 (UTC), excluding calendar 'holidays'");
    }

    // The issue's simple row first.
    [TestCase(30, -1, true, "30 seconds after each run completes")]
    [TestCase(30, 2, true, "30 seconds after each run completes, 3 times")]
    [TestCase(30, -1, false, "Every 30 seconds")]
    [TestCase(300, 2, false, "Every 5 minutes, 3 times")]
    [TestCase(3600, -1, false, "Every hour")]
    [TestCase(5400, -1, false, "Every 90 minutes")]
    [TestCase(86400, -1, false, "Every day")]
    [TestCase(60, 0, false, "Once")]
    public void ASimpleTriggerReadsItsIntervalAndCountAndNamesNoZone(int seconds, int repeatCount, bool fromCompletion, string expected)
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithSimpleSchedule(x =>
            {
                x.WithInterval(TimeSpan.FromSeconds(seconds)).MeasureIntervalFromCompletion(fromCompletion);
                if (repeatCount < 0)
                {
                    x.RepeatForever();
                }
                else
                {
                    x.WithRepeatCount(repeatCount);
                }
            })
            .Build();

        trigger.DescribeSchedule().Should().Be(expected);
    }

    [Test]
    public void ASimpleTriggerOfLessThanASecondCountsMilliseconds()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMilliseconds(1500)).RepeatForever())
            .Build();

        trigger.DescribeSchedule().Should().Be("Every 1500 milliseconds");
    }

    [TestCase(2, IntervalUnit.Month, "UTC", "Every 2 months (UTC)")]
    [TestCase(1, IntervalUnit.Week, "America/Chicago", "Every week (America/Chicago)")]
    [TestCase(1, IntervalUnit.Day, "UTC", "Every day (UTC)")]
    [TestCase(3, IntervalUnit.Year, "UTC", "Every 3 years (UTC)")]
    public void ACalendarIntervalTriggerReadsItsInterval(int interval, IntervalUnit unit, string zone, string expected)
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithCalendarIntervalSchedule(x => x.WithInterval(interval, unit).InTimeZone(TimeZones.FindById(zone)))
            .Build();

        trigger.DescribeSchedule().Should().Be(expected);
    }

    [Test]
    public void ADailyTimeIntervalTriggerReadsItsIntervalWindowAndDays()
    {
        // The issue's daily-time-interval row.
        ITrigger trigger = TriggerBuilder.Create()
            .WithDailyTimeIntervalSchedule(x => x
                .WithInterval(15, IntervalUnit.Minute)
                .StartingDailyAt(new TimeOnly(9, 0))
                .EndingDailyAt(new TimeOnly(17, 0))
                .OnMondayThroughFriday()
                .InTimeZone(TimeZoneInfo.Utc))
            .Build();

        trigger.DescribeSchedule().Should().Be("Every 15 minutes from 09:00 to 17:00, Monday to Friday (UTC)");
    }

    [Test]
    public void ADailyTimeIntervalTriggerOverTheWholeDayAndWeekSaysOnlyItsInterval()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithDailyTimeIntervalSchedule(x => x.WithInterval(1, IntervalUnit.Hour).OnEveryDay().InTimeZone(TimeZoneInfo.Utc))
            .Build();

        trigger.DescribeSchedule().Should().Be("Every hour (UTC)");
    }

    [Test]
    public void ADailyTimeIntervalTriggerCountsItsFiringsPerDay()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithDailyTimeIntervalSchedule(x => x
                .WithInterval(30, IntervalUnit.Minute)
                .StartingDailyAt(new TimeOnly(8, 0))
                .EndingDailyAt(new TimeOnly(18, 0, 30))
                .OnSaturdayAndSunday()
                .WithRepeatCount(3)
                .InTimeZone(TimeZoneInfo.Utc))
            .Build();

        trigger.DescribeSchedule().Should().Be("Every 30 minutes from 08:00:00 to 18:00:30, Saturday and Sunday, 4 times a day (UTC)");
    }

    // The issue's recurrence row first.
    [TestCase("FREQ=MONTHLY;BYDAY=2TU", "Monthly on the second Tuesday (UTC)")]
    [TestCase("FREQ=DAILY", "Daily (UTC)")]
    [TestCase("RRULE:FREQ=DAILY;INTERVAL=3", "Every 3 days (UTC)")]
    [TestCase("FREQ=HOURLY;INTERVAL=4", "Every 4 hours (UTC)")]
    [TestCase("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR", "Every 2 weeks on Monday, Wednesday and Friday (UTC)")]
    [TestCase("FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR", "Weekly on weekdays (UTC)")]
    [TestCase("FREQ=WEEKLY;BYDAY=SA,SU", "Weekly on Saturday and Sunday (UTC)")]
    [TestCase("FREQ=MONTHLY;BYMONTHDAY=1,15", "Monthly on the 1st and the 15th (UTC)")]
    [TestCase("FREQ=MONTHLY;BYMONTHDAY=-1", "Monthly on the last day (UTC)")]
    [TestCase("FREQ=MONTHLY;BYMONTHDAY=-2", "Monthly on the second-to-last day (UTC)")]
    [TestCase("FREQ=MONTHLY;BYDAY=-1FR", "Monthly on the last Friday (UTC)")]
    [TestCase("FREQ=MONTHLY;BYDAY=1MO,-1FR", "Monthly on the first Monday and the last Friday (UTC)")]
    [TestCase("FREQ=YEARLY;BYMONTH=11;BYDAY=4TH", "Yearly on the fourth Thursday of November (UTC)")]
    [TestCase("FREQ=YEARLY;BYMONTH=7;BYMONTHDAY=4", "Yearly on the 4th of July (UTC)")]
    [TestCase("FREQ=YEARLY;BYMONTH=7,1", "Yearly in January and July (UTC)")]
    [TestCase("FREQ=DAILY;BYHOUR=9,17;BYMINUTE=0", "Daily at 09:00 and 17:00 (UTC)")]
    [TestCase("FREQ=DAILY;COUNT=10", "Daily, 10 times (UTC)")]
    [TestCase("FREQ=WEEKLY;BYDAY=MO;UNTIL=20261231T000000Z", "Weekly on Monday, until 2026-12-31 UTC (UTC)")]
    [TestCase("FREQ=WEEKLY;BYDAY=MO;UNTIL=20261231T170000", "Weekly on Monday, until 2026-12-31 17:00 (UTC)")]
    // what it does not phrase is the rule itself
    [TestCase("FREQ=MONTHLY;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1", "FREQ=MONTHLY;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1 (UTC)")]
    [TestCase("FREQ=MONTHLY;BYDAY=FR;BYMONTHDAY=13", "FREQ=MONTHLY;BYDAY=FR;BYMONTHDAY=13 (UTC)")]
    [TestCase("FREQ=YEARLY;BYWEEKNO=20", "FREQ=YEARLY;BYWEEKNO=20 (UTC)")]
    [TestCase("FREQ=DAILY;BYHOUR=9", "FREQ=DAILY;BYHOUR=9 (UTC)")]
    public void ARecurrenceTriggerReadsItsRule(string rule, string expected)
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithSchedule(RecurrenceScheduleBuilder.Create(rule).InTimeZone(TimeZoneInfo.Utc))
            .Build();

        trigger.DescribeSchedule().Should().Be(expected);
    }

    [Test]
    public void ARecurrenceTriggerNamesItsOwnZone()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithSchedule(RecurrenceScheduleBuilder.Create("FREQ=MONTHLY;BYDAY=2TU").InTimeZone(TimeZones.FindById("America/Chicago")))
            .Build();

        trigger.DescribeSchedule().Should().Be("Monthly on the second Tuesday (America/Chicago)");
    }

    /// <summary>
    /// A schedule reads in the wall-clock time it is written in, whatever its zone's offset on the day. A time
    /// that America/Chicago's spring-forward day skips (02:30 on 2026-03-08) or its fall-back day repeats (01:30
    /// on 2026-11-01) reads as written, and a trigger that starts in winter reads the same as one that starts in
    /// summer.
    /// </summary>
    [Test]
    public void ATimeTheDaylightSavingChangeSkipsOrRepeatsReadsAsWrittenInEverySeason()
    {
        TimeZoneInfo chicago = TimeZones.FindById("America/Chicago");
        (Func<TriggerBuilder<IJob>, TriggerBuilder<IJob>> Schedule, string Expected)[] cases =
        [
            (x => x.WithCronSchedule("0 30 2 * * ?", c => c.InTimeZone(chicago)), "Every day at 02:30 (America/Chicago)"),
            (x => x.WithCronSchedule("0 30 1 * * ?", c => c.InTimeZone(chicago)), "Every day at 01:30 (America/Chicago)"),
            (x => x.WithCronSchedule("0 0 2 ? * SUN", c => c.InTimeZone(chicago)), "Every Sunday at 02:00 (America/Chicago)"),
            (x => x.WithDailyTimeIntervalSchedule(d => d
                    .WithInterval(15, IntervalUnit.Minute)
                    .StartingDailyAt(new TimeOnly(1, 30))
                    .EndingDailyAt(new TimeOnly(2, 30))
                    .InTimeZone(chicago)),
                "Every 15 minutes from 01:30 to 02:30 (America/Chicago)"),
            (x => x.WithSchedule(RecurrenceScheduleBuilder.Create("FREQ=DAILY;BYHOUR=2;BYMINUTE=30").InTimeZone(chicago)),
                "Daily at 02:30 (America/Chicago)"),
            (x => x.WithCalendarIntervalSchedule(c => c
                    .WithInterval(1, IntervalUnit.Day)
                    .PreserveHourOfDayAcrossDaylightSavings(true)
                    .InTimeZone(chicago)),
                "Every day (America/Chicago)")
        ];

        DateTimeOffset[] starts =
        [
            new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero),
            new(2026, 3, 8, 7, 0, 0, TimeSpan.Zero),
            new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero),
            new(2026, 11, 1, 6, 0, 0, TimeSpan.Zero)
        ];

        foreach ((Func<TriggerBuilder<IJob>, TriggerBuilder<IJob>> schedule, string expected) in cases)
        {
            foreach (DateTimeOffset start in starts)
            {
                ITrigger trigger = schedule(TriggerBuilder.Create().StartAt(start)).Build();

                trigger.DescribeSchedule().Should().Be(expected,
                    $"the text is the schedule as written, not the offset Chicago has on {start:yyyy-MM-dd}");
            }
        }
    }

    /// <summary>
    /// The zone is named by its id, including one whose offset is not a whole hour and one with no daylight
    /// saving time.
    /// </summary>
    [TestCase("Asia/Kathmandu", "Every day at 09:00 (Asia/Kathmandu)")]
    [TestCase("Australia/Lord_Howe", "Every day at 09:00 (Australia/Lord_Howe)")]
    [TestCase("Asia/Tokyo", "Every day at 09:00 (Asia/Tokyo)")]
    public void TheZoneIsNamedByItsId(string zone, string expected)
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithCronSchedule("0 0 9 * * ?", x => x.InTimeZone(TimeZones.FindById(zone)))
            .Build();

        trigger.DescribeSchedule().Should().Be(expected);
    }

    /// <summary>
    /// The text is English with invariant numbers in every culture: Finnish writes the time 17.00 and calls March
    /// <c>maaliskuu</c>, and Saudi Arabic counts years on the Umm al-Qura calendar.
    /// </summary>
    [TestCase("fi-FI")]
    [TestCase("ar-SA")]
    public void TheTextIsTheSameInEveryCulture(string cultureName)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        CultureInfo uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);

            TriggerBuilder.Create()
                .WithCronSchedule("0 10,44 14 ? MAR WED", x => x.InTimeZone(TimeZoneInfo.Utc))
                .Build()
                .DescribeSchedule()
                .Should().Be("Every Wednesday at 14:10 and 14:44 in March (UTC)");
            TriggerBuilder.Create()
                .WithSchedule(RecurrenceScheduleBuilder.Create("FREQ=WEEKLY;BYDAY=MO;UNTIL=20261231T170000").InTimeZone(TimeZoneInfo.Utc))
                .Build()
                .DescribeSchedule()
                .Should().Be("Weekly on Monday, until 2026-12-31 17:00 (UTC)");
            TriggerBuilder.Create()
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMilliseconds(1500)).WithRepeatCount(1999))
                .Build()
                .DescribeSchedule()
                .Should().Be("Every 1500 milliseconds, 2000 times");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    [Test]
    public void ATriggerOfAKindItDoesNotKnowIsNotDescribed()
    {
        ITrigger trigger = A.Fake<ITrigger>();

        trigger.DescribeSchedule().Should().BeNull("a trigger of the application's own says nothing this could read");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void ACronTriggerWithNoExpressionIsNotDescribed(string expression)
    {
        ICronTrigger trigger = A.Fake<ICronTrigger>();
        A.CallTo(() => trigger.CronExpressionString).Returns(expression);

        trigger.DescribeSchedule().Should().BeNull();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void ARecurrenceTriggerWithNoRuleIsNotDescribed(string rule)
    {
        IRecurrenceTrigger trigger = A.Fake<IRecurrenceTrigger>();
        A.CallTo(() => trigger.RecurrenceRule).Returns(rule);

        trigger.DescribeSchedule().Should().BeNull();
    }

    [Test]
    public void ANullTriggerIsRefused()
    {
        Action describe = () => ((ITrigger) null!).DescribeSchedule();

        describe.Should().Throw<ArgumentNullException>().WithParameterName("trigger");
    }

    [TestCase("not a cron expression")]
    [TestCase("0 0 12 * * ? 2030 extra")]
    [TestCase("0 0 12 ** * ?")]
    [TestCase("0 0 99 * * ?")]
    [TestCase("0 0 12 32W * ?")]
    [TestCase("0 0 12 ? * 8#9")]
    [TestCase("0 0 12 ? * MON-")]
    [TestCase("0 0 12 ? * -MON")]
    [TestCase("0 0 12 ? * /")]
    [TestCase("0 0 12 ? * */0")]
    [TestCase("0 0 12 L-99W * ?")]
    public void ACronTriggerOfItsOwnWithAnExpressionNothingParsesFallsBackToTheExpression(string expression)
    {
        // An ICronTrigger of an application's own need not have been through the parser.
        ICronTrigger trigger = A.Fake<ICronTrigger>();
        A.CallTo(() => trigger.CronExpressionString).Returns(expression);
        A.CallTo(() => trigger.TimeZone).Returns(TimeZoneInfo.Utc);

        trigger.DescribeSchedule().Should().Be(expression + " (UTC)");
    }

    [Test]
    public void ATriggerOfItsOwnThatAnswersNoZoneIsDescribedWithoutOne()
    {
        ICronTrigger cron = A.Fake<ICronTrigger>();
        A.CallTo(() => cron.CronExpressionString).Returns("0 0 12 * * ?");
        A.CallTo(() => cron.TimeZone).Returns(null);
        IDailyTimeIntervalTrigger daily = A.Fake<IDailyTimeIntervalTrigger>();
        A.CallTo(() => daily.RepeatInterval).Returns(1);
        A.CallTo(() => daily.RepeatIntervalUnit).Returns(IntervalUnit.Hour);
        A.CallTo(() => daily.StartTimeOfDay).Returns(TimeOnly.MinValue);
        A.CallTo(() => daily.EndTimeOfDay).Returns(new TimeOnly(23, 59, 59));
        A.CallTo(() => daily.DaysOfWeek).Returns(null);
        A.CallTo(() => daily.RepeatCount).Returns(-1);
        A.CallTo(() => daily.TimeZone).Returns(null);

        cron.DescribeSchedule().Should().Be("Every day at 12:00");
        daily.DescribeSchedule().Should().Be("Every hour");
    }

    [TestCase("FREQ=FORTNIGHTLY")]
    [TestCase("FREQ=DAILY;INTERVAL=99999999999")]
    [TestCase("BYDAY=MO")]
    public void ARecurrenceTriggerOfItsOwnWithARuleNothingParsesFallsBackToTheRule(string rule)
    {
        IRecurrenceTrigger trigger = A.Fake<IRecurrenceTrigger>();
        A.CallTo(() => trigger.RecurrenceRule).Returns(rule);
        A.CallTo(() => trigger.TimeZone).Returns(TimeZoneInfo.Utc);

        trigger.DescribeSchedule().Should().Be(rule + " (UTC)");
    }

    /// <summary>
    /// Every shape the parser accepts is either phrased or shown as written; none throws. The expressions are
    /// the ones the cron tests already exercise, plus every special in more than one position.
    /// </summary>
    [Test]
    public void NoValidCronExpressionThrows()
    {
        string[] expressions =
        [
            "0 0 12 ? * 2#1", "0 0 12 ? * 1#5", "0 0 12 L-30 * ?", "0 0 12 L-0W * ?", "0 0 12 1W,15W * ?",
            "0 0 12 1,L-2,LW * ?", "0 0 12 28-3 * ?", "0 0 12 1-15/2 * ?", "0 0 12 */7 * ?", "0 0 12 ? * FRI-MON",
            "0 0 12 ? * */2", "0 0 12 ? * MON-FRI/2", "0 0 12 ? NOV-FEB *", "0 0 12 ? */3 *", "0 0 12 ? 1-12/4 MON",
            "0 59 23 L DEC ? 2099", "0 0 0 ? * * 2026-2030/2", "59 59 23 31 12 ? 2099", "0 50-10 * * * ?",
            "50-10 * * * * ?", "0 0 20-4/3 * * ?", "1,2,3 4,5 6 * * ?", "0 0 0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23 * * ?",
            "*/7 */7 */7 * * ?", "0 0 0 1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31 * ?",
            "0 0 0 1-31 * MON", "0 0 0 ? * 1,2,3,4,5,6,7", "0/1 0/1 0/1 * * ?", "0 */1 * * * ?"
        ];

        foreach (string expression in expressions)
        {
            CronExpression.TryParse(expression, out _).Should().BeTrue($"'{expression}' is meant to be a valid expression");

            string description = CronScheduleDescriber.Describe(expression, TimeZoneInfo.Utc);

            description.Should().NotBeNullOrWhiteSpace($"'{expression}' is described or shown");
        }
    }

    [TestCase("0 0 12 28-3 * ?", "At 12:00 on the 28th to the 3rd of every month (UTC)")]
    [TestCase("0 0 0 1-31 * MON", "Every day at 00:00 (UTC)")]
    [TestCase("0 0 0 ? * 1,2,3,4,5,6,7", "Every day at 00:00 (UTC)")]
    [TestCase("0 0 12 ? * FRI-MON", "Every Friday to Monday at 12:00 (UTC)")]
    [TestCase("0 0 12 ? * SUN,MON", "Every Sunday and Monday at 12:00 (UTC)")]
    [TestCase("0 0 12 ? * SUN,WED", "Every Wednesday and Sunday at 12:00 (UTC)")]
    [TestCase("0 0 0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23 * * ?", "Every hour (UTC)")]
    [TestCase("0 0 12 ? * MON-FRI/2", "Every Monday, Wednesday and Friday at 12:00 (UTC)")]
    public void TheEdgesOfTheDayFieldsReadAsTheParserReadsThem(string expression, string expected)
    {
        CronScheduleDescriber.Describe(expression, TimeZoneInfo.Utc).Should().Be(expected);
    }

    /// <summary>
    /// A step or a range that reaches only one value is that value, not an interval: <c>20/6</c> in hours fires at
    /// 20:00 and nowhere else that day. Each sentence is held to the fire times the parser computes from
    /// 2026-01-01T00:00Z, not only to a string.
    /// </summary>
    [TestCase("0 0 20/6 * * ?", "Every day at 20:00 (UTC)",
        new[] { "2026-01-01T20:00:00Z", "2026-01-02T20:00:00Z", "2026-01-03T20:00:00Z" })]
    [TestCase("0 30/45 * * * ?", "Every hour at minute 30 (UTC)",
        new[] { "2026-01-01T00:30:00Z", "2026-01-01T01:30:00Z", "2026-01-01T02:30:00Z" })]
    [TestCase("0 0 12/13 * * ?", "Every day at 12:00 (UTC)",
        new[] { "2026-01-01T12:00:00Z", "2026-01-02T12:00:00Z", "2026-01-03T12:00:00Z" })]
    [TestCase("0 0 5-5 * * ?", "Every day at 05:00 (UTC)",
        new[] { "2026-01-01T05:00:00Z", "2026-01-02T05:00:00Z", "2026-01-03T05:00:00Z" })]
    [TestCase("40/30 0 9 * * ?", "Every day at 09:00:40 (UTC)",
        new[] { "2026-01-01T09:00:40Z", "2026-01-02T09:00:40Z", "2026-01-03T09:00:40Z" })]
    [TestCase("0 0 12 25/10 * ?", "At 12:00 on the 25th of every month (UTC)",
        new[] { "2026-01-25T12:00:00Z", "2026-02-25T12:00:00Z", "2026-03-25T12:00:00Z" })]
    [TestCase("0 0 12 5-5 * ?", "At 12:00 on the 5th of every month (UTC)",
        new[] { "2026-01-05T12:00:00Z", "2026-02-05T12:00:00Z", "2026-03-05T12:00:00Z" })]
    public void AStepOrRangeThatReachesOneValueReadsAsThatValue(string expression, string expected, string[] firstFires)
    {
        CronScheduleDescriber.Describe(expression, TimeZoneInfo.Utc).Should().Be(expected);

        FireTimes(expression, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), firstFires.Length)
            .Should().Equal(firstFires.Select(x => DateTimeOffset.Parse(x, CultureInfo.InvariantCulture)),
                "the sentence says exactly when the expression fires");
    }

    /// <summary>
    /// A year step that reaches one year is that year: <c>2026-2027/2</c> fires every day of 2026 and never again.
    /// </summary>
    [Test]
    public void AYearStepThatReachesOneYearReadsAsThatYear()
    {
        const string Expression = "0 0 12 * * ? 2026-2027/2";
        CronExpression cron = new(Expression, TimeZoneInfo.Utc);

        CronScheduleDescriber.Describe(Expression, TimeZoneInfo.Utc).Should().Be("Every day at 12:00 in 2026 (UTC)");

        cron.GetNextValidTimeAfter(new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero))
            .Should().Be(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        cron.GetNextValidTimeAfter(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero))
            .Should().Be(new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero), "it fires every day of the year it names");
        cron.GetNextValidTimeAfter(new DateTimeOffset(2026, 12, 31, 12, 0, 0, TimeSpan.Zero))
            .Should().BeNull("2028 is past the end of the range, so 2026 is the only year");
    }

    /// <summary>
    /// An hour range that crosses midnight reads as one overnight window only when every day fires. With a day
    /// filter it fires the morning and the evening of the same day, never the night that runs into the next day, so
    /// no window would be true and the expression shows as written. <c>0 0 22-2 ? * MON</c> fires Monday 00:00 to
    /// 02:00 and Monday 22:00 to 23:00, and nothing on Tuesday.
    /// </summary>
    [TestCase("0 0 22-2 ? * MON")]
    [TestCase("0 0/15 22-2 ? * MON-FRI")]
    [TestCase("*/30 * 22-2 ? * SUN")]
    [TestCase("0 0 22-2 1 * ?")]
    [TestCase("0 0 22-2 L * ?")]
    public void AnHourRangeOverMidnightWithADayFilterIsShownAsWritten(string expression)
    {
        CronScheduleDescriber.Phrase(expression).Should().BeNull();
        CronScheduleDescriber.Describe(expression, TimeZoneInfo.Utc).Should().Be(expression + " (UTC)");
    }

    [Test]
    public void AnHourRangeOverMidnightWithADayFilterNeverFiresIntoTheNextDay()
    {
        CronExpression cron = new("0 0 22-2 ? * MON", TimeZoneInfo.Utc);

        // 2026-01-05 is a Monday.
        cron.GetNextValidTimeAfter(new DateTimeOffset(2026, 1, 5, 2, 0, 0, TimeSpan.Zero))
            .Should().Be(new DateTimeOffset(2026, 1, 5, 22, 0, 0, TimeSpan.Zero));
        cron.GetNextValidTimeAfter(new DateTimeOffset(2026, 1, 5, 23, 0, 0, TimeSpan.Zero))
            .Should().Be(new DateTimeOffset(2026, 1, 12, 0, 0, 0, TimeSpan.Zero),
                "the window does not run on into Tuesday, which is why no overnight sentence describes it");
    }

    /// <summary>
    /// On every day the same ranges are one window through midnight, and say so.
    /// </summary>
    [TestCase("0 0 22-2 * * ?", "Every hour from 22:00 to 02:00 (UTC)")]
    [TestCase("0 0/15 22-2 * * ?", "Every 15 minutes between 22:00 and 02:45 (UTC)")]
    [TestCase("0 0/15 22-2 * MAR ?", "Every 15 minutes between 22:00 and 02:45 in March (UTC)")]
    public void AnHourRangeOverMidnightOnEveryDayReadsAsOneWindow(string expression, string expected)
    {
        CronScheduleDescriber.Describe(expression, TimeZoneInfo.Utc).Should().Be(expected);
    }

    [Test]
    public void AWindowOverMidnightOnEveryDayFiresThroughMidnight()
    {
        CronExpression cron = new("0 0/15 22-2 * * ?", TimeZoneInfo.Utc);

        cron.GetNextValidTimeAfter(new DateTimeOffset(2026, 1, 5, 23, 45, 0, TimeSpan.Zero))
            .Should().Be(new DateTimeOffset(2026, 1, 6, 0, 0, 0, TimeSpan.Zero));
        cron.GetNextValidTimeAfter(new DateTimeOffset(2026, 1, 6, 2, 45, 0, TimeSpan.Zero))
            .Should().Be(new DateTimeOffset(2026, 1, 6, 22, 0, 0, TimeSpan.Zero));
    }

    private static List<DateTimeOffset> FireTimes(string expression, DateTimeOffset after, int count)
    {
        CronExpression cron = new(expression, TimeZoneInfo.Utc);
        List<DateTimeOffset> times = [];
        DateTimeOffset cursor = after;
        while (times.Count < count && cron.GetNextValidTimeAfter(cursor) is { } next)
        {
            times.Add(next);
            cursor = next;
        }

        return times;
    }
}
