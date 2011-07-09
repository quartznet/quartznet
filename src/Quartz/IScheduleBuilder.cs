using Quartz.Spi;

namespace Quartz
{
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