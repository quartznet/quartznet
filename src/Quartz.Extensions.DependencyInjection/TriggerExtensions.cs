using System;

namespace Quartz
{
    public static class TriggerExtensions
    {
        public static IServiceCollectionTriggerConfigurator WithDailyTimeIntervalSchedule(this IServiceCollectionTriggerConfigurator triggerBuilder, Action<DailyTimeIntervalScheduleBuilder>? action = null)
        {
            DailyTimeIntervalScheduleBuilder builder = DailyTimeIntervalScheduleBuilder.Create();
            action?.Invoke(builder);
            triggerBuilder.WithSchedule(builder);
            return triggerBuilder;
        }

        public static IServiceCollectionTriggerConfigurator WithDailyTimeIntervalSchedule(this IServiceCollectionTriggerConfigurator triggerBuilder, DailyTimeIntervalScheduleBuilder schedule)
        {
            triggerBuilder.WithSchedule(schedule);
            return triggerBuilder;
        }

        public static IServiceCollectionTriggerConfigurator WithCalendarIntervalSchedule(this IServiceCollectionTriggerConfigurator triggerBuilder, Action<CalendarIntervalScheduleBuilder>? action = null)
        {
            CalendarIntervalScheduleBuilder builder = CalendarIntervalScheduleBuilder.Create();
            action?.Invoke(builder);
            triggerBuilder.WithSchedule(builder);
            return triggerBuilder;
        }

        public static IServiceCollectionTriggerConfigurator WithCalendarIntervalSchedule(this IServiceCollectionTriggerConfigurator triggerBuilder, CalendarIntervalScheduleBuilder schedule)
        {
            triggerBuilder.WithSchedule(schedule);
            return triggerBuilder;
        }

        public static IServiceCollectionTriggerConfigurator WithCronSchedule(this IServiceCollectionTriggerConfigurator triggerBuilder, string cronExpression, Action<CronScheduleBuilder>? action = null)
        {
            CronScheduleBuilder builder = CronScheduleBuilder.CronSchedule(cronExpression);
            action?.Invoke(builder);
            triggerBuilder.WithSchedule(builder);
            return triggerBuilder;
        }

        public static IServiceCollectionTriggerConfigurator WithCronSchedule(this IServiceCollectionTriggerConfigurator triggerBuilder, CronScheduleBuilder schedule)
        {
            triggerBuilder.WithSchedule(schedule);
            return triggerBuilder;
        }

        public static IServiceCollectionTriggerConfigurator WithSimpleSchedule(this IServiceCollectionTriggerConfigurator triggerBuilder, Action<SimpleScheduleBuilder>? action = null)
        {
            SimpleScheduleBuilder builder = SimpleScheduleBuilder.Create();
            action?.Invoke(builder);
            triggerBuilder.WithSchedule(builder);
            return triggerBuilder;
        }

        public static IServiceCollectionTriggerConfigurator WithSimpleSchedule(this IServiceCollectionTriggerConfigurator triggerBuilder, SimpleScheduleBuilder schedule)
        {
            triggerBuilder.WithSchedule(schedule);
            return triggerBuilder;
        }
    }
}