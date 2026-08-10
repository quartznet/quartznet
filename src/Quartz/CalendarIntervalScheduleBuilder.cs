using Quartz.Impl.Triggers;
using Quartz.Extensibility;

namespace Quartz;

/// <summary>
/// CalendarIntervalScheduleBuilder is a <see cref="IScheduleBuilder" />
/// that defines calendar time (day, week, month, year) interval-based
/// schedules for Triggers.
/// </summary>
/// <remarks>
/// <para>
/// Quartz provides a builder-style API for constructing scheduling-related
/// entities via a Domain-Specific Language (DSL).  The DSL can best be
/// utilized through the usage of static imports of the methods on the classes
/// <see cref="TriggerBuilder" />, <see cref="JobBuilder" />,
/// <see cref="DateBuilder" />, <see cref="JobKey" />, <see cref="TriggerKey" />
/// and the various <see cref="IScheduleBuilder" /> implementations.
/// </para>
/// <para>Client code can then use the DSL to write code such as this:</para>
/// <code>
/// IJobDetail job = JobBuilder.Create&lt;MyJob&gt;()
///     .WithIdentity("myJob")
///     .Build();
/// ITrigger trigger = TriggerBuilder.Create()
///     .WithIdentity("myTrigger", "myTriggerGroup")
///     .WithCalendarIntervalSchedule(x => x
///         .WithInterval(1, IntervalUnit.Month))
///     .Build();
/// await scheduler.ScheduleJob(job, trigger);
/// </code>
/// </remarks>
/// <seealso cref="ICalendarIntervalTrigger" />
/// <seealso cref="CronScheduleBuilder" />
/// <seealso cref="IScheduleBuilder" />
/// <seealso cref="SimpleScheduleBuilder" />
/// <seealso cref="TriggerBuilder" />
public sealed class CalendarIntervalScheduleBuilder : IScheduleBuilder
{
    private int interval = 1;
    private IntervalUnit intervalUnit = IntervalUnit.Day;

    private int misfireInstruction = MisfireInstruction.SmartPolicy;
    private TimeZoneInfo? timeZone;
    private bool preserveHourOfDayAcrossDaylightSavings;
    private bool skipDayIfHourDoesNotExist;

    private CalendarIntervalScheduleBuilder()
    {
    }

    /// <summary>
    /// Create a CalendarIntervalScheduleBuilder.
    /// </summary>
    /// <returns></returns>
    public static CalendarIntervalScheduleBuilder Create()
    {
        return new CalendarIntervalScheduleBuilder();
    }

    /// <summary>
    /// Build the actual Trigger -- NOT intended to be invoked by end users,
    /// but will rather be invoked by a TriggerBuilder which this
    /// ScheduleBuilder is given to.
    /// </summary>
    /// <returns></returns>
    public IMutableTrigger Build()
    {
        CalendarIntervalTriggerImpl st = new CalendarIntervalTriggerImpl();
        st.RepeatInterval = interval;
        st.RepeatIntervalUnit = intervalUnit;
        st.MisfireInstruction = misfireInstruction;
        st.timeZone = timeZone;
        st.PreserveHourOfDayAcrossDaylightSavings = preserveHourOfDayAcrossDaylightSavings;
        st.SkipDayIfHourDoesNotExist = skipDayIfHourDoesNotExist;

        return st;
    }

    /// <summary>
    /// Specify the time unit and interval for the Trigger to be produced.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="interval">the interval at which the trigger should repeat.</param>
    /// <param name="unit"> the time unit (IntervalUnit) of the interval.</param>
    /// <returns>the updated CalendarIntervalScheduleBuilder</returns>
    /// <seealso cref="ICalendarIntervalTrigger.RepeatInterval" />
    /// <seealso cref="ICalendarIntervalTrigger.RepeatIntervalUnit" />
    public CalendarIntervalScheduleBuilder WithInterval(int interval, IntervalUnit unit)
    {
        ValidateInterval(interval);
        this.interval = interval;
        intervalUnit = unit;
        return this;
    }

    /// <summary>
    /// Say what the trigger should do when it misses a firing.
    /// </summary>
    /// <param name="instruction">the policy to apply; defaults to
    /// <see cref="CalendarIntervalTriggerMisfireInstruction.SmartPolicy" />.</param>
    /// <returns>the updated CalendarIntervalScheduleBuilder</returns>
    /// <seealso cref="CalendarIntervalTriggerMisfireInstruction" />
    public CalendarIntervalScheduleBuilder WithMisfireInstruction(CalendarIntervalTriggerMisfireInstruction instruction)
    {
        misfireInstruction = (int) instruction;
        return this;
    }

    /// <summary>
    /// TimeZone in which to base the schedule.
    /// </summary>
    /// <param name="timeZone">the time-zone for the schedule; <see langword="null" /> means the
    /// system's local time zone.</param>
    /// <returns>the updated CalendarIntervalScheduleBuilder</returns>
    /// <seealso cref="ICalendarIntervalTrigger.TimeZone" />
    public CalendarIntervalScheduleBuilder InTimeZone(TimeZoneInfo? timeZone)
    {
        this.timeZone = timeZone;
        return this;
    }

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
    /// <seealso cref="InTimeZone"/>
    /// <seealso cref="TriggerBuilder{TJob}.StartAt"/>
    public CalendarIntervalScheduleBuilder PreserveHourOfDayAcrossDaylightSavings(bool preserveHourOfDay = true)
    {
        preserveHourOfDayAcrossDaylightSavings = preserveHourOfDay;
        return this;
    }

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
    public CalendarIntervalScheduleBuilder SkipDayIfHourDoesNotExist(bool skipDay = true)
    {
        skipDayIfHourDoesNotExist = skipDay;
        return this;
    }

    // ReSharper disable once UnusedParameter.Local
    private static void ValidateInterval(int interval)
    {
        if (interval <= 0)
        {
            Throw.ArgumentException("Interval must be a positive value.");
        }
    }
}
