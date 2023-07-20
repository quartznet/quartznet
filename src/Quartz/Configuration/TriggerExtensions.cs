namespace Quartz;

public static class TriggerExtensions
{
    public static ITriggerConfigurator WithDailyTimeIntervalSchedule(
        this ITriggerConfigurator triggerBuilder,
        Action<DailyTimeIntervalScheduleBuilder>? action = null)
    {
        DailyTimeIntervalScheduleBuilder builder = DailyTimeIntervalScheduleBuilder.Create();
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