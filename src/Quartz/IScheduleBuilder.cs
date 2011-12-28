using Quartz.Spi;

namespace Quartz
{
    /// <summary>
    /// Schedule builders offer fluent interface and are responsible for creating schedules.
    /// </summary>
    /// <seealso cref="SimpleScheduleBuilder"/>
    /// <seealso cref="CalendarIntervalScheduleBuilder"/>
    /// <seealso cref="CronScheduleBuilder"/>
    /// <seealso cref="DailyTimeIntervalScheduleBuilder"/>
    public interface IScheduleBuilder
    {
        /// <summary>
        /// Build the actual Trigger -- NOT intended to be invoked by end users,
        /// but will rather be invoked by a TriggerBuilder which this
        /// ScheduleBuilder is given to.
        /// </summary>
        /// <seealso cref="TriggerBuilder.WithSchedule" />
        IMutableTrigger Build();
    }
}