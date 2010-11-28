#region License

/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
 * 
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not 
 * use this file except in compliance with the License. You may obtain a copy 
 * of the License at 
 * 
 *   http://www.apache.org/licenses/LICENSE-2.0 
 *   
 * Unless required by applicable law or agreed to in writing, software 
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT 
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the 
 * License for the specific language governing permissions and limitations 
 * under the License.
 * 
 */

#endregion

using System;

using Quartz.Impl.Triggers;
using Quartz.Spi;

namespace Quartz
{
/**
 * <code>CronScheduleBuilder</code> is a {@link ScheduleBuilder} that defines
 * {@link CronExpression}-based schedules for <code>Trigger</code>s.
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
 * @see CronExpression
 * @see CronTrigger
 * @see ScheduleBuilder
 * @see SimpleScheduleBuilder 
 * @see CalendarIntervalScheduleBuilder 
 * @see TriggerBuilder
 */

    public class CronScheduleBuilder : ScheduleBuilder<ICronTrigger>
    {
        private readonly string cronExpression;
        private TimeZoneInfo tz;
        private int misfireInstruction = MisfireInstruction.SmartPolicy;

        private CronScheduleBuilder(string cronExpression)
        {
            this.cronExpression = cronExpression;
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
            CronTriggerImpl ct = new CronTriggerImpl();

            try
            {
                ct.CronExpressionString = cronExpression;
            }
            catch (FormatException)
            {
                // all methods of construction ensure the expression is valid by this point...
                throw new Exception("CronExpression '" + cronExpression +
                                    "' is invalid, which should not be possible, please report bug to Quartz developers.");
            }
            ct.TimeZone = tz;
            ct.MisfireInstruction = misfireInstruction;

            return ct;
        }

        /**
     * Create a CronScheduleBuilder with the given cron-expression.
     * 
     * @param cronExpression the cron expression to base the schedule on.
     * @return the new CronScheduleBuilder
     * @throws ParseException
     * @see CronExpression
     */

        public static CronScheduleBuilder CronSchedule(string cronExpression)
        {
            CronExpression.ValidateExpression(cronExpression);
            return new CronScheduleBuilder(cronExpression);
        }

        /**
     * Create a CronScheduleBuilder with a cron-expression that sets the
     * schedule to fire every day at the given time (hour and minute).
     * 
     * @param hour the hour of day to fire
     * @param minute the minute of the given hour to fire
     * @return the new CronScheduleBuilder
     * @throws ParseException
     * @see CronExpression
     */

        public static CronScheduleBuilder DailyAtHourAndMinute(int hour, int minute)
        {
            DateBuilder.validateHour(hour);
            DateBuilder.validateMinute(minute);

            string cronExpression = String.Format("0 {0} {1} ? * *", minute, hour);

            return new CronScheduleBuilder(cronExpression);
        }

        /**
     * Create a CronScheduleBuilder with a cron-expression that sets the
     * schedule to fire one per week on the given day at the given time 
     * (hour and minute).
     * 
     * @param dayOfWeek the day of the week to fire
     * @param hour the hour of day to fire
     * @param minute the minute of the given hour to fire
     * @return the new CronScheduleBuilder
     * @throws ParseException
     * @see CronExpression
     * @see DateBuilder#MONDAY
     * @see DateBuilder#TUESDAY
     * @see DateBuilder#WEDNESDAY
     * @see DateBuilder#THURSDAY
     * @see DateBuilder#FRIDAY
     * @see DateBuilder#SATURDAY
     * @see DateBuilder#SUNDAY
     */

        public static CronScheduleBuilder WeeklyOnDayAndHourAndMinute(int dayOfWeek, int hour, int minute)
        {
            DateBuilder.validateDayOfWeek(dayOfWeek);
            DateBuilder.validateHour(hour);
            DateBuilder.validateMinute(minute);

            string cronExpression = String.Format("0 {0} {1} ? * {2}", minute, hour, dayOfWeek);

            return new CronScheduleBuilder(cronExpression);
        }

        /**
     * Create a CronScheduleBuilder with a cron-expression that sets the
     * schedule to fire one per month on the given day of month at the given 
     * time (hour and minute).
     * 
     * @param dayOfMonth the day of the month to fire
     * @param hour the hour of day to fire
     * @param minute the minute of the given hour to fire
     * @return the new CronScheduleBuilder
     * @throws ParseException
     * @see CronExpression
     */

        public static CronScheduleBuilder MonthlyOnDayAndHourAndMinute(int dayOfMonth, int hour, int minute)
        {
            DateBuilder.validateDayOfMonth(dayOfMonth);
            DateBuilder.validateHour(hour);
            DateBuilder.validateMinute(minute);

            string cronExpression = String.Format("0 {0} {1} {2} * ?", minute, hour, dayOfMonth);

            return new CronScheduleBuilder(cronExpression);
        }

        /**
     * The <code>TimeZone</code> in which to base the schedule.
     * 
     * @param tz the time-zone for the schedule.
     * @return the updated CronScheduleBuilder
     * @see CronExpression#getTimeZone()
     */

        public CronScheduleBuilder InTimeZone(TimeZoneInfo tz)
        {
            this.tz = tz;
            return this;
        }


        /**
         * If the Trigger misfires, use the 
         * {@link Trigger#MISFIRE_INSTRUCTION_IGNORE_MISFIRE_POLICY} instruction.
         * 
         * @return the updated CronScheduleBuilder
         * @see Trigger#MISFIRE_INSTRUCTION_IGNORE_MISFIRE_POLICY
         */

        public CronScheduleBuilder WithMisfireHandlingInstructionIgnoreMisfires()
        {
            misfireInstruction = MisfireInstruction.IgnoreMisfirePolicy;
            return this;
        }

        /**
     * If the Trigger misfires, use the 
     * {@link CronTrigger#MISFIRE_INSTRUCTION_DO_NOTHING} instruction.
     * 
     * @return the updated CronScheduleBuilder
     * @see CronTrigger#MISFIRE_INSTRUCTION_DO_NOTHING
     */

        public CronScheduleBuilder WithMisfireHandlingInstructionDoNothing()
        {
            misfireInstruction = MisfireInstruction.CronTrigger.DoNothing;
            return this;
        }

        /**
     * If the Trigger misfires, use the 
     * {@link CronTrigger#MISFIRE_INSTRUCTION_FIRE_ONCE_NOW} instruction.
     * 
     * @return the updated CronScheduleBuilder
     * @see CronTrigger#MISFIRE_INSTRUCTION_FIRE_ONCE_NOW
     */

        public CronScheduleBuilder WithMisfireHandlingInstructionFireAndProceed()
        {
            misfireInstruction = MisfireInstruction.CronTrigger.FireOnceNow;
            return this;
        }

        internal CronScheduleBuilder WithMisfireHandlingInstruction(int readMisfireInstructionFromString)
        {
            misfireInstruction = readMisfireInstructionFromString;
            return this;
        }
    }
}