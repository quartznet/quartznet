namespace Quartz;

public static class TriggerExtensions
{
    /// <summary>
    /// Sets up a trigger schedule for one or more occurrences every day.
    /// </summary>
    /// <param name="triggerBuilder"></param>
    /// <param name="action">Ability to configure the scheduling.</param>
    /// <returns>Mutated trigger configurator</returns>
    public static ITriggerConfigurator<TJob> WithDailyTimeIntervalSchedule<TJob>(
        this ITriggerConfigurator<TJob> triggerBuilder,
        Action<DailyTimeIntervalScheduleBuilder> action) where TJob : IJob
    {
        DailyTimeIntervalScheduleBuilder builder = DailyTimeIntervalScheduleBuilder.Create();
        action(builder);
        triggerBuilder.WithSchedule(builder);
        return triggerBuilder;
    }

    public static ITriggerConfigurator<TJob> WithDailyTimeIntervalSchedule<TJob>(
        this ITriggerConfigurator<TJob> triggerBuilder,
        DailyTimeIntervalScheduleBuilder schedule) where TJob : IJob
    {
        triggerBuilder.WithSchedule(schedule);
        return triggerBuilder;
    }

    /// <summary>
    /// Sets up a trigger schedule for one or more occurrences every day.
    /// </summary>
    /// <remarks>
    /// You need to configure the interval for when the trigger fires the job. If you only want one execution per day,
    /// call EndingDailyAfterCount(1) or set the interval accordingly.
    /// </remarks>
    /// <param name="triggerBuilder"></param>
    /// <param name="interval">The interval count to configure on the builder initially , e.g. 12*hours</param>
    /// <param name="intervalUnit">The unit for the defaultInterval count. Defaults to hours.</param>
    /// <param name="action">Ability to further configure the scheduling.</param>
    /// <seealso cref="DailyTimeIntervalScheduleBuilder.EndingDailyAfterCount"/>
    /// <seealso cref="DailyTimeIntervalScheduleBuilder.EndingDailyAt"/>
    /// <seealso cref="WithCronSchedule{TJob}(ITriggerConfigurator{TJob},string,Action{CronScheduleBuilder})"/>
    /// <returns>Mutated trigger configurator</returns>
    public static ITriggerConfigurator<TJob> WithDailyTimeIntervalSchedule<TJob>(
        this ITriggerConfigurator<TJob> triggerBuilder,
        int interval,
        IntervalUnit intervalUnit,
        Action<DailyTimeIntervalScheduleBuilder>? action = null) where TJob : IJob
    {
        DailyTimeIntervalScheduleBuilder builder = DailyTimeIntervalScheduleBuilder.Create();
        builder.WithInterval(interval, intervalUnit);
        action?.Invoke(builder);
        triggerBuilder.WithSchedule(builder);
        return triggerBuilder;
    }

    public static ITriggerConfigurator<TJob> WithCalendarIntervalSchedule<TJob>(
        this ITriggerConfigurator<TJob> triggerBuilder,
        Action<CalendarIntervalScheduleBuilder>? action = null) where TJob : IJob
    {
        CalendarIntervalScheduleBuilder builder = CalendarIntervalScheduleBuilder.Create();
        action?.Invoke(builder);
        triggerBuilder.WithSchedule(builder);
        return triggerBuilder;
    }

    public static ITriggerConfigurator<TJob> WithCalendarIntervalSchedule<TJob>(
        this ITriggerConfigurator<TJob> triggerBuilder,
        CalendarIntervalScheduleBuilder schedule) where TJob : IJob
    {
        triggerBuilder.WithSchedule(schedule);
        return triggerBuilder;
    }

    public static ITriggerConfigurator<TJob> WithCronSchedule<TJob>(
        this ITriggerConfigurator<TJob> triggerBuilder,
        string cronExpression,
        Action<CronScheduleBuilder>? action = null) where TJob : IJob
    {
        CronScheduleBuilder builder = CronScheduleBuilder.CronSchedule(cronExpression);
        action?.Invoke(builder);
        triggerBuilder.WithSchedule(builder);
        return triggerBuilder;
    }

    public static ITriggerConfigurator<TJob> WithCronSchedule<TJob>(
        this ITriggerConfigurator<TJob> triggerBuilder,
        CronScheduleBuilder schedule) where TJob : IJob
    {
        triggerBuilder.WithSchedule(schedule);
        return triggerBuilder;
    }

    public static ITriggerConfigurator<TJob> WithSimpleSchedule<TJob>(
        this ITriggerConfigurator<TJob> triggerBuilder,
        Action<SimpleScheduleBuilder>? action = null) where TJob : IJob
    {
        SimpleScheduleBuilder builder = SimpleScheduleBuilder.Create();
        action?.Invoke(builder);
        triggerBuilder.WithSchedule(builder);
        return triggerBuilder;
    }

    public static ITriggerConfigurator<TJob> WithSimpleSchedule<TJob>(
        this ITriggerConfigurator<TJob> triggerBuilder,
        SimpleScheduleBuilder schedule) where TJob : IJob
    {
        triggerBuilder.WithSchedule(schedule);
        return triggerBuilder;
    }

    /// <summary>
    /// Set the trigger to use an RFC 5545 RRULE-based schedule.
    /// </summary>
    /// <param name="triggerBuilder">The trigger builder.</param>
    /// <param name="recurrenceRule">
    /// An RFC 5545 RRULE string, e.g. "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR".
    /// </param>
    /// <param name="action">Action to further configure the schedule builder.</param>
    public static ITriggerConfigurator<TJob> WithRecurrenceSchedule<TJob>(
        this ITriggerConfigurator<TJob> triggerBuilder,
        string recurrenceRule,
        Action<RecurrenceScheduleBuilder>? action = null) where TJob : IJob
    {
        RecurrenceScheduleBuilder builder = RecurrenceScheduleBuilder.Create(recurrenceRule);
        action?.Invoke(builder);
        triggerBuilder.WithSchedule(builder);
        return triggerBuilder;
    }

    /// <summary>
    /// Set the trigger to use an RFC 5545 RRULE-based schedule.
    /// </summary>
    public static ITriggerConfigurator<TJob> WithRecurrenceSchedule<TJob>(
        this ITriggerConfigurator<TJob> triggerBuilder,
        RecurrenceScheduleBuilder schedule) where TJob : IJob
    {
        triggerBuilder.WithSchedule(schedule);
        return triggerBuilder;
    }
}
