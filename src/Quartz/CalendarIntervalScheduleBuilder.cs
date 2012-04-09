using System;

using Quartz.Impl.Triggers;
using Quartz.Spi;

namespace Quartz
{
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
    /// JobDetail job = JobBuilder.Create&lt;MyJob&gt;()
    ///     .WithIdentity("myJob")
    ///     .Build();
    /// Trigger trigger = TriggerBuilder.Create()
    ///     .WithIdentity("myTrigger", "myTriggerGroup")
    ///     .WithSimpleSchedule(x => x
    ///         .WithIntervalInHours(1)
    ///         .RepeatForever())
    ///     .StartAt(DateBuilder.FutureDate(10, IntervalUnit.Minute))
    ///     .Build();
    /// scheduler.scheduleJob(job, trigger);
    /// </code>
    /// </remarks>
    /// <seealso cref="ICalendarIntervalTrigger" />
    /// <seealso cref="CronScheduleBuilder" />
    /// <seealso cref="IScheduleBuilder" />
    /// <seealso cref="SimpleScheduleBuilder" />
    /// <seealso cref="TriggerBuilder" />
    public class CalendarIntervalScheduleBuilder : ScheduleBuilder<ICalendarIntervalTrigger>
    {
        private int interval = 1;
        private IntervalUnit intervalUnit = IntervalUnit.Day;

        private int misfireInstruction = MisfireInstruction.SmartPolicy;
        private TimeZoneInfo timeZone;
        private bool preserveHourOfDayAcrossDaylightSavings;
        private bool skipDayIfHourDoesNotExist;

        protected CalendarIntervalScheduleBuilder()
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
        public override IMutableTrigger Build()
        {
            CalendarIntervalTriggerImpl st = new CalendarIntervalTriggerImpl();
            st.RepeatInterval = interval;
            st.RepeatIntervalUnit = intervalUnit;
            st.MisfireInstruction = misfireInstruction;
            st.TimeZone = timeZone;
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
            this.intervalUnit = unit;
            return this;
        }

        /// <summary>
        /// Specify an interval in the IntervalUnit.SECOND that the produced
        /// Trigger will repeat at.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="intervalInSeconds">the number of seconds at which the trigger should repeat.</param>
        /// <returns>the updated CalendarIntervalScheduleBuilder</returns>
        /// <seealso cref="ICalendarIntervalTrigger.RepeatInterval" />
        /// <seealso cref="ICalendarIntervalTrigger.RepeatIntervalUnit" />
        public CalendarIntervalScheduleBuilder WithIntervalInSeconds(int intervalInSeconds)
        {
            ValidateInterval(intervalInSeconds);
            this.interval = intervalInSeconds;
            this.intervalUnit = IntervalUnit.Second;
            return this;
        }

        /// <summary>
        /// Specify an interval in the IntervalUnit.MINUTE that the produced
        /// Trigger will repeat at.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="intervalInMinutes">the number of minutes at which the trigger should repeat.</param>
        /// <returns>the updated CalendarIntervalScheduleBuilder</returns>
        /// <seealso cref="ICalendarIntervalTrigger.RepeatInterval" />
        /// <seealso cref="ICalendarIntervalTrigger.RepeatIntervalUnit" />
        public CalendarIntervalScheduleBuilder WithIntervalInMinutes(int intervalInMinutes)
        {
            ValidateInterval(intervalInMinutes);
            this.interval = intervalInMinutes;
            this.intervalUnit = IntervalUnit.Minute;
            return this;
        }

        /// <summary>
        /// Specify an interval in the IntervalUnit.HOUR that the produced
        /// Trigger will repeat at.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="intervalInHours">the number of hours at which the trigger should repeat.</param>
        /// <returns>the updated CalendarIntervalScheduleBuilder</returns>
        /// <seealso cref="ICalendarIntervalTrigger.RepeatInterval" />
        /// <seealso cref="ICalendarIntervalTrigger.RepeatIntervalUnit" />
        public CalendarIntervalScheduleBuilder WithIntervalInHours(int intervalInHours)
        {
            ValidateInterval(intervalInHours);
            this.interval = intervalInHours;
            this.intervalUnit = IntervalUnit.Hour;
            return this;
        }

        /// <summary>
        /// Specify an interval in the IntervalUnit.DAY that the produced
        /// Trigger will repeat at.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="intervalInDays">the number of days at which the trigger should repeat.</param>
        /// <returns>the updated CalendarIntervalScheduleBuilder</returns>
        /// <seealso cref="ICalendarIntervalTrigger.RepeatInterval" />
        /// <seealso cref="ICalendarIntervalTrigger.RepeatIntervalUnit" />
        public CalendarIntervalScheduleBuilder WithIntervalInDays(int intervalInDays)
        {
            ValidateInterval(intervalInDays);
            this.interval = intervalInDays;
            this.intervalUnit = IntervalUnit.Day;
            return this;
        }

        /// <summary>
        /// Specify an interval in the IntervalUnit.WEEK that the produced
        /// Trigger will repeat at.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="intervalInWeeks">the number of weeks at which the trigger should repeat.</param>
        /// <returns>the updated CalendarIntervalScheduleBuilder</returns>
        /// <seealso cref="ICalendarIntervalTrigger.RepeatInterval" />
        /// <seealso cref="ICalendarIntervalTrigger.RepeatIntervalUnit" />
        public CalendarIntervalScheduleBuilder WithIntervalInWeeks(int intervalInWeeks)
        {
            ValidateInterval(intervalInWeeks);
            this.interval = intervalInWeeks;
            this.intervalUnit = IntervalUnit.Week;
            return this;
        }

        /// <summary>
        /// Specify an interval in the IntervalUnit.MONTH that the produced
        /// Trigger will repeat at.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="intervalInMonths">the number of months at which the trigger should repeat.</param>
        /// <returns>the updated CalendarIntervalScheduleBuilder</returns>
        /// <seealso cref="ICalendarIntervalTrigger.RepeatInterval" />
        /// <seealso cref="ICalendarIntervalTrigger.RepeatIntervalUnit" />
        public CalendarIntervalScheduleBuilder WithIntervalInMonths(int intervalInMonths)
        {
            ValidateInterval(intervalInMonths);
            this.interval = intervalInMonths;
            this.intervalUnit = IntervalUnit.Month;
            return this;
        }

        /// <summary>
        /// Specify an interval in the IntervalUnit.YEAR that the produced
        /// Trigger will repeat at.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="intervalInYears">the number of years at which the trigger should repeat.</param>
        /// <returns>the updated CalendarIntervalScheduleBuilder</returns>
        /// <seealso cref="ICalendarIntervalTrigger.RepeatInterval" />
        /// <seealso cref="ICalendarIntervalTrigger.RepeatIntervalUnit" />
        public CalendarIntervalScheduleBuilder WithIntervalInYears(int intervalInYears)
        {
            ValidateInterval(intervalInYears);
            this.interval = intervalInYears;
            this.intervalUnit = IntervalUnit.Year;
            return this;
        }

        /// <summary>
        /// If the Trigger misfires, use the
        /// <see cref="MisfireInstruction.IgnoreMisfirePolicy" /> instruction.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the updated CronScheduleBuilder</returns>
        /// <seealso cref="MisfireInstruction.IgnoreMisfirePolicy" />
        public CalendarIntervalScheduleBuilder WithMisfireHandlingInstructionIgnoreMisfires()
        {
            misfireInstruction = MisfireInstruction.IgnoreMisfirePolicy;
            return this;
        }


        /// <summary>
        /// If the Trigger misfires, use the
        /// <see cref="MisfireInstruction.CalendarIntervalTrigger.DoNothing" /> instruction.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the updated CalendarIntervalScheduleBuilder</returns>
        /// <seealso cref="MisfireInstruction.CalendarIntervalTrigger.DoNothing" />
        public CalendarIntervalScheduleBuilder WithMisfireHandlingInstructionDoNothing()
        {
            misfireInstruction = MisfireInstruction.CalendarIntervalTrigger.DoNothing;
            return this;
        }

        /// <summary>
        /// If the Trigger misfires, use the
        /// <see cref="MisfireInstruction.CalendarIntervalTrigger.FireOnceNow" /> instruction.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the updated CalendarIntervalScheduleBuilder</returns>
        /// <seealso cref="MisfireInstruction.CalendarIntervalTrigger.FireOnceNow" />
        public CalendarIntervalScheduleBuilder WithMisfireHandlingInstructionFireAndProceed()
        {
            misfireInstruction = MisfireInstruction.CalendarIntervalTrigger.FireOnceNow;
            return this;
        }

        /// <summary>
        /// TimeZone in which to base the schedule.
        /// </summary>
        /// <param name="timezone">the time-zone for the schedule</param>
        /// <returns>the updated CalendarIntervalScheduleBuilder</returns>
        /// <seealso cref="ICalendarIntervalTrigger.TimeZone" />
        public CalendarIntervalScheduleBuilder InTimeZone(TimeZoneInfo timezone)
        {
            this.timeZone = timezone;
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
        /// <seealso cref="TimeZone"/>
        /// <seealso cref="InTimeZone"/>
        /// <seealso cref="TriggerBuilder.StartAt"/>
        public CalendarIntervalScheduleBuilder PreserveHourOfDayAcrossDaylightSavings(bool preserveHourOfDay)
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
        public CalendarIntervalScheduleBuilder SkipDayIfHourDoesNotExist(bool skipDay)
        {
            skipDayIfHourDoesNotExist = skipDay;
            return this;
        }

        private static void ValidateInterval(int interval)
        {
            if (interval <= 0)
            {
                throw new ArgumentException("Interval must be a positive value.");
            }
        }

        internal CalendarIntervalScheduleBuilder WithMisfireHandlingInstruction(int readMisfireInstructionFromString)
        {
            misfireInstruction = readMisfireInstructionFromString;
            return this;
        }
    }

    /// <summary>
    /// Extension methods that attach <see cref="CalendarIntervalScheduleBuilder" /> to <see cref="TriggerBuilder" />.
    /// </summary>
    public static class CalendarIntervalTriggerBuilderExtensions
    {
        public static TriggerBuilder WithCalendarIntervalSchedule(this TriggerBuilder triggerBuilder)
        {
            CalendarIntervalScheduleBuilder builder = CalendarIntervalScheduleBuilder.Create();
            return triggerBuilder.WithSchedule(builder);
        }

        public static TriggerBuilder WithCalendarIntervalSchedule(this TriggerBuilder triggerBuilder, Action<CalendarIntervalScheduleBuilder> action)
        {
            CalendarIntervalScheduleBuilder builder = CalendarIntervalScheduleBuilder.Create();
            action(builder);
            return triggerBuilder.WithSchedule(builder);
        }
    }
}