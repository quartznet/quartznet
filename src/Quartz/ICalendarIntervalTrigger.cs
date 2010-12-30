namespace Quartz
{
    public interface ICalendarIntervalTrigger : ITrigger
    {
        /// <summary>
        /// Get or set the interval unit - the time unit on with the interval applies.
        /// </summary>
        IntervalUnit RepeatIntervalUnit { get; set; }

        /// <summary>
        /// Get the the time interval that will be added to the <see cref="ICalendarIntervalTrigger" />'s
        /// fire time (in the set repeat interval unit) in order to calculate the time of the 
        /// next trigger repeat.
        /// </summary>
        int RepeatInterval { get; set; }

        /// <summary>
        /// Get the number of times the <code>DateIntervalTrigger</code> has already fired.
        /// </summary>
        int TimesTriggered { get; set; }

        TriggerBuilder GetTriggerBuilder();
    }

    public enum IntervalUnit
    {
        Second,
        Minute,
        Hour,
        Day,
        Week,
        Month,
        Year
    }
}