using System;

using Quartz.Impl.Triggers;
using Quartz.Spi;

namespace Quartz
{
    /**
     * <code>CalendarIntervalScheduleBuilder</code> is a {@link ScheduleBuilder} 
     * that defines calendar time (day, week, month, year) interval-based 
     * schedules for <code>Trigger</code>s.
     *  
     * <p>Quartz provides a builder-style API for constructing scheduling-related
     * entities via a Domain-Specific Language (DSL).  The DSL can best be
     * utilized through the usage of static imports of the methods on the classes
     * <code>TriggerBuilder</code>, <code>JobBuilder</code>, 
     * <code>DateBuilder</code>, <code>JobKey</code>, <code>TriggerKey</code> 
     * and the various <code>ScheduleBuilder</code> implementations.</p>
     * 
     * <p>Client code can then use the DSL to write code such as this:</p>
     * <pre>
     *         JobDetail job = newJob(MyJob.class)
     *             .withIdentity("myJob")
     *             .build();
     *             
     *         Trigger trigger = newTrigger() 
     *             .withIdentity(triggerKey("myTrigger", "myTriggerGroup"))
     *             .withSchedule(simpleSchedule()
     *                 .withIntervalInHours(1)
     *                 .repeatForever())
     *             .startAt(futureDate(10, MINUTES))
     *             .build();
     *         
     *         scheduler.scheduleJob(job, trigger);
     * <pre>
     *
     * @see CalenderIntervalTrigger
     * @see CronScheduleBuilder
     * @see ScheduleBuilder
     * @see SimpleScheduleBuilder 
     * @see TriggerBuilder
     */

    public class CalendarIntervalScheduleBuilder : ScheduleBuilder
    {
        private int interval = 1;
        private IntervalUnit intervalUnit = IntervalUnit.Day;

        private int misfireInstruction = MisfireInstruction.SmartPolicy;

        private CalendarIntervalScheduleBuilder()
        {
        }

        /**
     * Create a CalendarIntervalScheduleBuilder.
     * 
     * @return the new CalendarIntervalScheduleBuilder
     */

        public static CalendarIntervalScheduleBuilder CalendarIntervalSchedule()
        {
            return new CalendarIntervalScheduleBuilder();
        }

        /**
     * Build the actual Trigger -- NOT intended to be invoked by end users,
     * but will rather be invoked by a TriggerBuilder which this 
     * ScheduleBuilder is given to.
     * 
     * @see TriggerBuilder#withSchedule(ScheduleBuilder)
     */

        public override IMutableTrigger Build()
        {
            CalendarIntervalTriggerImpl st = new CalendarIntervalTriggerImpl();
            st.RepeatInterval = interval;
            st.RepeatIntervalUnit = intervalUnit;

            return st;
        }

        /**
     * Specify the time unit and interval for the Trigger to be produced.
     * 
     * @param interval the interval at which the trigger should repeat.
     * @param unit  the time unit (IntervalUnit) of the interval.
     * @return the updated CalendarIntervalScheduleBuilder
     * @see CalendarIntervalTrigger#getRepeatInterval()
     * @see CalendarIntervalTrigger#getRepeatIntervalUnit()
     */

        public CalendarIntervalScheduleBuilder withInterval(int interval, IntervalUnit unit)
        {
            validateInterval(interval);
            this.interval = interval;
            this.intervalUnit = unit;
            return this;
        }

        /**
     * Specify an interval in the IntervalUnit.SECOND that the produced 
     * Trigger will repeat at.
     * 
     * @param intervalInSeconds the number of seconds at which the trigger should repeat.
     * @return the updated CalendarIntervalScheduleBuilder
     * @see CalendarIntervalTrigger#getRepeatInterval()
     * @see CalendarIntervalTrigger#getRepeatIntervalUnit()
     */

        public CalendarIntervalScheduleBuilder withIntervalInSeconds(int intervalInSeconds)
        {
            validateInterval(intervalInSeconds);
            this.interval = intervalInSeconds;
            this.intervalUnit = IntervalUnit.Second;
            return this;
        }

        /**
     * Specify an interval in the IntervalUnit.MINUTE that the produced 
     * Trigger will repeat at.
     * 
     * @param intervalInMinutes the number of minutes at which the trigger should repeat.
     * @return the updated CalendarIntervalScheduleBuilder
     * @see CalendarIntervalTrigger#getRepeatInterval()
     * @see CalendarIntervalTrigger#getRepeatIntervalUnit()
     */

        public CalendarIntervalScheduleBuilder withIntervalInMinutes(int intervalInMinutes)
        {
            validateInterval(intervalInMinutes);
            this.interval = intervalInMinutes;
            this.intervalUnit = IntervalUnit.Minute;
            return this;
        }

        /**
     * Specify an interval in the IntervalUnit.HOUR that the produced 
     * Trigger will repeat at.
     * 
     * @param intervalInHours the number of hours at which the trigger should repeat.
     * @return the updated CalendarIntervalScheduleBuilder
     * @see CalendarIntervalTrigger#getRepeatInterval()
     * @see CalendarIntervalTrigger#getRepeatIntervalUnit()
     */

        public CalendarIntervalScheduleBuilder withIntervalInHours(int intervalInHours)
        {
            validateInterval(intervalInHours);
            this.interval = intervalInHours;
            this.intervalUnit = IntervalUnit.Hour;
            return this;
        }

        /**
     * Specify an interval in the IntervalUnit.DAY that the produced 
     * Trigger will repeat at.
     * 
     * @param intervalInDays the number of days at which the trigger should repeat.
     * @return the updated CalendarIntervalScheduleBuilder
     * @see CalendarIntervalTrigger#getRepeatInterval()
     * @see CalendarIntervalTrigger#getRepeatIntervalUnit()
     */

        public CalendarIntervalScheduleBuilder withIntervalInDays(int intervalInDays)
        {
            validateInterval(intervalInDays);
            this.interval = intervalInDays;
            this.intervalUnit = IntervalUnit.Day;
            return this;
        }

        /**
     * Specify an interval in the IntervalUnit.WEEK that the produced 
     * Trigger will repeat at.
     * 
     * @param intervalInWeeks the number of weeks at which the trigger should repeat.
     * @return the updated CalendarIntervalScheduleBuilder
     * @see CalendarIntervalTrigger#getRepeatInterval()
     * @see CalendarIntervalTrigger#getRepeatIntervalUnit()
     */

        public CalendarIntervalScheduleBuilder withIntervalInWeeks(int intervalInWeeks)
        {
            validateInterval(intervalInWeeks);
            this.interval = intervalInWeeks;
            this.intervalUnit = IntervalUnit.Week;
            return this;
        }

        /**
     * Specify an interval in the IntervalUnit.MONTH that the produced 
     * Trigger will repeat at.
     * 
     * @param intervalInMonths the number of months at which the trigger should repeat.
     * @return the updated CalendarIntervalScheduleBuilder
     * @see CalendarIntervalTrigger#getRepeatInterval()
     * @see CalendarIntervalTrigger#getRepeatIntervalUnit()
     */

        public CalendarIntervalScheduleBuilder withIntervalInMonths(int intervalInMonths)
        {
            validateInterval(intervalInMonths);
            this.interval = intervalInMonths;
            this.intervalUnit = IntervalUnit.Month;
            return this;
        }

        /**
     * Specify an interval in the IntervalUnit.YEAR that the produced 
     * Trigger will repeat at.
     * 
     * @param intervalInYears the number of years at which the trigger should repeat.
     * @return the updated CalendarIntervalScheduleBuilder
     * @see CalendarIntervalTrigger#getRepeatInterval()
     * @see CalendarIntervalTrigger#getRepeatIntervalUnit()
     */

        public CalendarIntervalScheduleBuilder withIntervalInYears(int intervalInYears)
        {
            validateInterval(intervalInYears);
            this.interval = intervalInYears;
            this.intervalUnit = IntervalUnit.Year;
            return this;
        }

        /**
 * If the Trigger misfires, use the 
 * {@link Trigger#MISFIRE_INSTRUCTION_IGNORE_MISFIRE_POLICY} instruction.
 * 
 * @return the updated CronScheduleBuilder
 * @see Trigger#MISFIRE_INSTRUCTION_IGNORE_MISFIRE_POLICY
 */
        public CalendarIntervalScheduleBuilder withMisfireHandlingInstructionIgnoreMisfires()
        {
            misfireInstruction = MisfireInstruction.IgnoreMisfirePolicy;
            return this;
        }
    

        /**
     * If the Trigger misfires, use the 
     * {@link CalendarIntervalTrigger#MISFIRE_INSTRUCTION_DO_NOTHING} instruction.
     * 
     * @return the updated CalendarIntervalScheduleBuilder
     * @see CalendarIntervalTrigger#MISFIRE_INSTRUCTION_DO_NOTHING
     */

        public CalendarIntervalScheduleBuilder withMisfireHandlingInstructionDoNothing()
        {
            misfireInstruction = MisfireInstruction.CalendarIntervalTrigger.DoNothing;
            return this;
        }

        /**
     * If the Trigger misfires, use the 
     * {@link CalendarIntervalTrigger#MISFIRE_INSTRUCTION_FIRE_ONCE_NOW} instruction.
     * 
     * @return the updated CalendarIntervalScheduleBuilder
     * @see CalendarIntervalTrigger#MISFIRE_INSTRUCTION_FIRE_ONCE_NOW
     */

        public CalendarIntervalScheduleBuilder withMisfireHandlingInstructionFireAndProceed()
        {
            misfireInstruction = MisfireInstruction.CalendarIntervalTrigger.FireOnceNow;
            return this;
        }

        private void validateInterval(int interval)
        {
            if (interval <= 0)
            {
                throw new ArgumentException("Interval must be a positive value.");
            }
        }

        internal CalendarIntervalScheduleBuilder withMisfireHandlingInstruction(int readMisfireInstructionFromString)
        {
            misfireInstruction = readMisfireInstructionFromString;
            return this;
        }
    }
}