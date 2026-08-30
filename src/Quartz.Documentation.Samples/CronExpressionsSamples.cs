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
