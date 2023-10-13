namespace Quartz;

public static class TriggerExtensions
{
    /// <summary>
    /// Sets up a daily schedule with a default interval of *every-minute* unless overridden by the action
    /// by calling WithInterval / WithIntervalInHours / WithIntervalInMinutes.
    ///
    /// For the sake of clarity, you probably want to use WithDailyConfiguredTimeIntervalSchedule instead.
    /// </summary>
    [Obsolete("Use WithDailyConfiguredTimeIntervalSchedule with default interval params instead.")]
    public static ITriggerConfigurator WithDailyTimeIntervalSchedule(
        this ITriggerConfigurator triggerBuilder,
        Action<DailyTimeIntervalScheduleBuilder>? action = null)
    {
        DailyTimeIntervalScheduleBuilder builder = DailyTimeIntervalScheduleBuilder.Create();
        action?.Invoke(builder);
        triggerBuilder.WithSchedule(builder);
        return triggerBuilder;
    }
    
    /// <summary>
    /// Sets up a trigger schedule for one or more occurrences every day.
    /// With parameter defaults, sets up a daily interval of every 12h.
    /// </summary>
    /// <remarks>
    /// You need to configure the interval for when the trigger fires the job. If you only want one execution per day,
    /// call EndingDailyAfterCount(1) or set the interval accordingly.
    /// </remarks>
    /// <param name="triggerBuilder"></param>
    /// <param name="action"></param>
    /// <param name="defaultInterval">The interval count to configure on the builder initially , e.g. 12*hours</param>
    /// <param name="defaultIntervalUnit">The unit for the defaultInterval count. Defaults to hours.</param>
    /// <seealso cref="DailyTimeIntervalScheduleBuilder.EndingDailyAfterCount"/>
    /// <seealso cref="DailyTimeIntervalScheduleBuilder.EndingDailyAt"/>
    /// <seealso cref="WithCronSchedule(Quartz.ITriggerConfigurator,string,System.Action{Quartz.CronScheduleBuilder}?)"/>
    /// <returns>Mutated trigger configurator</returns>
    public static ITriggerConfigurator WithDailyConfiguredTimeIntervalSchedule(
        this ITriggerConfigurator triggerBuilder,
        Action<DailyTimeIntervalScheduleBuilder>? action = null, int defaultInterval=12, IntervalUnit defaultIntervalUnit=IntervalUnit.Hour)
    {
        DailyTimeIntervalScheduleBuilder builder = DailyTimeIntervalScheduleBuilder.Create();
        builder.WithInterval(defaultInterval, defaultIntervalUnit);
        action?.Invoke(builder);
        triggerBuilder.WithSchedule(builder);
        return triggerBuilder;
    }

    public static ITriggerConfigurator WithDailyTimeIntervalSchedule(
        this ITriggerConfigurator triggerBuilder,
        DailyTimeIntervalScheduleBuilder schedule)
    {
        triggerBuilder.WithSchedule(schedule);
        return triggerBuilder;
    }

    public static ITriggerConfigurator WithCalendarIntervalSchedule(
        this ITriggerConfigurator triggerBuilder,
        Action<CalendarIntervalScheduleBuilder>? action = null)
    {
        CalendarIntervalScheduleBuilder builder = CalendarIntervalScheduleBuilder.Create();
        action?.Invoke(builder);
        triggerBuilder.WithSchedule(builder);
        return triggerBuilder;
    }

    public static ITriggerConfigurator WithCalendarIntervalSchedule(
        this ITriggerConfigurator triggerBuilder,
        CalendarIntervalScheduleBuilder schedule)
    {
        triggerBuilder.WithSchedule(schedule);
        return triggerBuilder;
    }

    public static ITriggerConfigurator WithCronSchedule(
        this ITriggerConfigurator triggerBuilder,
        string cronExpression,
        Action<CronScheduleBuilder>? action = null)
    {
        CronScheduleBuilder builder = CronScheduleBuilder.CronSchedule(cronExpression);
        action?.Invoke(builder);
        triggerBuilder.WithSchedule(builder);
        return triggerBuilder;
    }

    public static ITriggerConfigurator WithCronSchedule(
        this ITriggerConfigurator triggerBuilder,
        CronScheduleBuilder schedule)
    {
        triggerBuilder.WithSchedule(schedule);
        return triggerBuilder;
    }

    public static ITriggerConfigurator WithSimpleSchedule(
        this ITriggerConfigurator triggerBuilder,
        Action<SimpleScheduleBuilder>? action = null)
    {
        SimpleScheduleBuilder builder = SimpleScheduleBuilder.Create();
        action?.Invoke(builder);
        triggerBuilder.WithSchedule(builder);
        return triggerBuilder;
    }

    public static ITriggerConfigurator WithSimpleSchedule(
        this ITriggerConfigurator triggerBuilder,
        SimpleScheduleBuilder schedule)
    {
        triggerBuilder.WithSchedule(schedule);
        return triggerBuilder;
    }
}
