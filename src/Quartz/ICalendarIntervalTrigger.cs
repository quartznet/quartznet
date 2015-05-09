using System;

namespace Quartz
{
    /// <summary>
    ///  A <see cref="ITrigger" /> that is used to fire a <see cref="IJobDetail" />
    ///  based upon repeating calendar time intervals.
    ///  </summary>
    public interface ICalendarIntervalTrigger : ITrigger
    {
        /// <summary>
        /// Get or set the interval unit - the time unit on with the interval applies.
        /// </summary>
        IntervalUnit RepeatIntervalUnit { get; set; }

        /// <summary>
        /// Get the time interval that will be added to the <see cref="ICalendarIntervalTrigger" />'s
        /// fire time (in the set repeat interval unit) in order to calculate the time of the 
        /// next trigger repeat.
        /// </summary>
        int RepeatInterval { get; set; }

        /// <summary>
        /// Get the number of times the <see cref="ICalendarIntervalTrigger" /> has already fired.
        /// </summary>
        int TimesTriggered { get; set; }

        /// <summary>
        /// Gets the time zone within which time calculations related to this trigger will be performed.
        /// </summary>
        /// <remarks>
        /// If null, the system default TimeZone will be used.
        /// </remarks>
        TimeZoneInfo TimeZone { get; }

        ///<summary>
        /// If intervals are a day or greater, this property (set to true) will 
        /// cause the firing of the trigger to always occur at the same time of day,
        /// (the time of day of the startTime) regardless of daylight saving time 
        /// transitions.  Default value is false.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For example, without the property set, your trigger may have a start 
        /// time of 9:00 am on March 1st, and a repeat interval of 2 days.  But 
        /// after the daylight saving transition occurs, the trigger may start 
        /// firing at 8:00 am every other day.
        /// </para>
        /// <para>
        /// If however, the time of day does not exist on a given day to fire
        /// (e.g. 2:00 am in the United States on the days of daylight saving
        /// transition), the trigger will go ahead and fire one hour off on 
        /// that day, and then resume the normal hour on other days.  If
        /// you wish for the trigger to never fire at the "wrong" hour, then
        /// you should set the property skipDayIfHourDoesNotExist.
        /// </para>
        ///</remarks>
        /// <seealso cref="SkipDayIfHourDoesNotExist"/>
        /// <seealso cref="TimeZone"/>
        /// <seealso cref="TriggerBuilder.StartAt"/>
        bool PreserveHourOfDayAcrossDaylightSavings { get; }

        /// <summary>
        /// If intervals are a day or greater, and 
        /// preserveHourOfDayAcrossDaylightSavings property is set to true, and the
        /// hour of the day does not exist on a given day for which the trigger 
        /// would fire, the day will be skipped and the trigger advanced a second
        /// interval if this property is set to true.  Defaults to false.
        /// </summary>
        /// <remarks>
        /// <b>CAUTION!</b>  If you enable this property, and your hour of day happens 
        /// to be that of daylight savings transition (e.g. 2:00 am in the United 
        /// States) and the trigger's interval would have had the trigger fire on
        /// that day, then you may actually completely miss a firing on the day of 
        /// transition if that hour of day does not exist on that day!  In such a 
        /// case the next fire time of the trigger will be computed as double (if 
        /// the interval is 2 days, then a span of 4 days between firings will 
        /// occur).
        /// </remarks>
        /// <seealso cref="PreserveHourOfDayAcrossDaylightSavings"/>
        bool SkipDayIfHourDoesNotExist { get; }
    }
}