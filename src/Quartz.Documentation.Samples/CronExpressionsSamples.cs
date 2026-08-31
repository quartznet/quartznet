namespace Quartz.Documentation.Samples;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/cron-expressions.md.
/// </summary>
public static class CronExpressionsSamples
{
    public static void HashSeededByTriggerName()
    {
        #region sample_cron_expressions_hash_from_trigger_name

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("nightly-cleanup")
            .WithCronSchedule("0 H H(0-7) * * ?")
            .Build();

        #endregion
    }

    public static void HashSeededExplicitly()
    {
        #region sample_cron_expressions_hash_key_on_expression

        ITrigger trigger = TriggerBuilder.Create()
            .WithCronSchedule(new CronExpression("0 H H(0-7) * * ?", "nightly-cleanup"))
            .Build();

        #endregion
    }

    public static void HashKeyOnItsOwn()
    {
        #region sample_cron_expressions_hash_key

        var expr = new CronExpression("0 H H(0-7) * * ?", "nightly-cleanup");

        #endregion
    }

    public static void ReadingACrontabLine()
    {
        #region sample_cron_expressions_unix_format

        // "at 04:30 on Mondays", written the way crontab writes it
        CronExpression expression = CronExpression.Parse("30 4 * * 1", CronFormat.Unix);

        // ...and held the way Quartz writes it: "0 30 4 ? * MON"
        string canonical = expression.CronExpressionString;

        #endregion
    }

    public static void SchedulingFromACrontabLine()
    {
        #region sample_cron_expressions_unix_format_trigger

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("weekday-report")
            .WithSchedule(CronScheduleBuilder.Create("15 10 * * 1-5", CronFormat.Unix))
            .Build();

        // WithCronSchedule has no format overload; compose one when you need its other options
        ITrigger composed = TriggerBuilder.Create()
            .WithIdentity("weekday-report-2")
            .WithCronSchedule(CronExpression.Parse("15 10 * * 1-5", CronFormat.Unix))
            .Build();

        #endregion
    }

    public static void UsingAMacro()
    {
        #region sample_cron_expressions_macro

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("nightly")
            .WithCronSchedule("@daily") // stored, and shown, as "0 0 0 * * ?"
            .Build();

        #endregion
    }

    public static void BuildingAnExpression()
    {
        #region sample_cron_expressions_builder

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("myTrigger")
            .WithCronSchedule(CronExpressionBuilder.Create()
                .WithSecond(0)
                .WithMinuteIncrements(0, 15) // every 15 minutes
                .WithHourRange(8, 17)        // between 8:00 and 17:59
                .OnWeekdays())               // "0 0/15 8-17 ? * MON-FRI"
            .Build();

        #endregion
    }

    public static void ATimeOfDay()
    {
        #region sample_cron_expressions_at_time

        CronExpressionBuilder.Create().AtTime(new TimeOnly(9, 30));            // "0 30 9 ? * *"

        CronExpressionBuilder.Create()
            .AtTime(new TimeOnly(9, 30))
            .WithDaysOfWeek(DayOfWeek.Monday, DayOfWeek.Thursday);            // "0 30 9 ? * MON,THU"

        CronExpressionBuilder.Create()
            .AtTime(new TimeOnly(9, 30))
            .WithDayOfMonth(15);                                              // "0 30 9 15 * ?"

        #endregion
    }

    public static void TheAwkwardDayRules()
    {
        #region sample_cron_expressions_day_rules

        CronExpressionBuilder.Create().OnLastDayOfMonth();                         // "* * * L * ?"
        CronExpressionBuilder.Create().OnNearestWeekdayOfMonth(15);                // "* * * 15W * ?"
        CronExpressionBuilder.Create().OnNthDayOfWeekOfMonth(DayOfWeek.Friday, 3); // "* * * ? * FRI#3"
        CronExpressionBuilder.Create().OnLastDayOfWeekOfMonth(DayOfWeek.Friday);   // "* * * ? * FRIL"

        #endregion
    }
}
