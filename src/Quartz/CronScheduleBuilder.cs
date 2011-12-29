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

    /// <summary>
    /// CronScheduleBuilder is a <see cref="IScheduleBuilder" /> that defines
    /// <see cref="CronExpression" />-based schedules for <see cref="ITrigger" />s.
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
    /// <para>
    /// Client code can then use the DSL to write code such as this:
    /// </para>
    /// <code>
    /// IJobDetail job = JobBuilder.Create&lt;MyJob&gt;()
    ///   .WithIdentity("myJob")
    ///   .Build();
    /// ITrigger trigger = newTrigger()
    ///  .WithIdentity(triggerKey("myTrigger", "myTriggerGroup"))
    ///  .WithSimpleSchedule(x => x.WithIntervalInHours(1).RepeatForever())
    ///  .StartAt(DateBuilder.FutureDate(10, IntervalUnit.Minute))
    ///  .Build();
    /// scheduler.scheduleJob(job, trigger);
    /// </code>
    /// </remarks>
    /// <seealso cref="CronExpression" />
    /// <seealso cref="ICronTrigger" />
    /// <seealso cref="IScheduleBuilder" />
    /// <seealso cref="SimpleScheduleBuilder" />
    /// <seealso cref="CalendarIntervalScheduleBuilder" />
    /// <seealso cref="TriggerBuilder" />
    public class CronScheduleBuilder : ScheduleBuilder<ICronTrigger>
    {
        private readonly CronExpression cronExpression;
        private int misfireInstruction = MisfireInstruction.SmartPolicy;

        protected CronScheduleBuilder(CronExpression cronExpression)
        {
            if (cronExpression == null)
            {
                throw new ArgumentNullException("cronExpression", "cronExpression cannot be null");
            }            
            
            this.cronExpression = cronExpression;
        }

        /// <summary>
        /// Build the actual Trigger -- NOT intended to be invoked by end users,
        /// but will rather be invoked by a TriggerBuilder which this
        /// ScheduleBuilder is given to.
        /// </summary>
        /// <seealso cref="TriggerBuilder.WithSchedule" />
        public override IMutableTrigger Build()
        {
            CronTriggerImpl ct = new CronTriggerImpl();
            
            ct.CronExpression = cronExpression;
            ct.TimeZone = cronExpression.TimeZone;
            ct.MisfireInstruction = misfireInstruction;

            return ct;
        }

        /// <summary>
        /// Create a CronScheduleBuilder with the given cron-expression - which
        /// is presumed to b e valid cron expression (and hence only a RuntimeException
        /// will be thrown if it is not).
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="cronExpression">the cron expression to base the schedule on.</param>
        /// <returns>the new CronScheduleBuilder</returns>
        /// <seealso cref="CronExpression" />
        public static CronScheduleBuilder CronSchedule(string cronExpression)
        {
            CronExpression.ValidateExpression(cronExpression);
            return CronScheduleNoParseException(cronExpression);
        }

        /// <summary>
        /// Create a CronScheduleBuilder with the given cron-expression string - which
        /// may not be a valid cron expression (and hence a ParseException will be thrown
        /// f it is not).
        /// </summary>
        /// <param name="presumedValidCronExpression">the cron expression string to base the schedule on</param>
        /// <returns>the new CronScheduleBuilder</returns>
        /// <seealso cref="CronExpression" /> 
        private static CronScheduleBuilder CronScheduleNoParseException(String presumedValidCronExpression)
        {
            try
            {
                return CronSchedule(new CronExpression(presumedValidCronExpression));
            }
            catch (FormatException e)
            {
                // all methods of construction ensure the expression is valid by this point...
                throw new Exception("CronExpression '" + presumedValidCronExpression +
                        "' is invalid, which should not be possible, please report bug to Quartz developers.", e);
            }
        }

        /// <summary>
        /// Create a CronScheduleBuilder with the given cron-expression.
        /// </summary>
        /// <param name="cronExpression">the cron expression to base the schedule on.</param>
        /// <returns>the new CronScheduleBuilder</returns>
        /// <seealso cref="CronExpression" /> 
        public static CronScheduleBuilder CronSchedule(CronExpression cronExpression)
        {
            return new CronScheduleBuilder(cronExpression);
        }

        /// <summary>
        /// Create a CronScheduleBuilder with a cron-expression that sets the
        /// schedule to fire every day at the given time (hour and minute).
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="hour">the hour of day to fire</param>
        /// <param name="minute">the minute of the given hour to fire</param>
        /// <returns>the new CronScheduleBuilder</returns>
        /// <seealso cref="CronExpression" />
        public static CronScheduleBuilder DailyAtHourAndMinute(int hour, int minute)
        {
            DateBuilder.ValidateHour(hour);
            DateBuilder.ValidateMinute(minute);

            string cronExpression = String.Format("0 {0} {1} ? * *", minute, hour);

            return CronScheduleNoParseException(cronExpression);
        }

        /// <summary>
        /// Create a CronScheduleBuilder with a cron-expression that sets the
        /// schedule to fire at the given day at the given time (hour and minute) on the given days of the week.
        /// </summary>
        /// <param name="hour">the hour of day to fire</param>
        /// <param name="minute">the minute of the given hour to fire</param>
        /// <param name="daysOfWeek">the days of the week to fire</param>
        /// <returns>the new CronScheduleBuilder</returns>
        /// <seealso cref="CronExpression" />
        public static CronScheduleBuilder AtHourAndMinuteOnGivenDaysOfWeek(int hour, int minute, params DayOfWeek[] daysOfWeek)
        {
            if (daysOfWeek == null || daysOfWeek.Length == 0)
            {
                throw new ArgumentException("You must specify at least one day of week.");
            }

            DateBuilder.ValidateHour(hour);
            DateBuilder.ValidateMinute(minute);

            string cronExpression = string.Format("0 {0} {1} ? * {2}", minute, hour, ((int) daysOfWeek[0]) + 1);

            for (int i = 1; i < daysOfWeek.Length; i++)
            {
                cronExpression = cronExpression + "," + (((int) daysOfWeek[i]) + 1);
            }

            return CronScheduleNoParseException(cronExpression);
        }

        /// <summary>
        /// Create a CronScheduleBuilder with a cron-expression that sets the
        /// schedule to fire one per week on the given day at the given time
        /// (hour and minute).
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="dayOfWeek">the day of the week to fire</param>
        /// <param name="hour">the hour of day to fire</param>
        /// <param name="minute">the minute of the given hour to fire</param>
        /// <returns>the new CronScheduleBuilder</returns>
        /// <seealso cref="CronExpression" />
        public static CronScheduleBuilder WeeklyOnDayAndHourAndMinute(DayOfWeek dayOfWeek, int hour, int minute)
        {
            DateBuilder.ValidateHour(hour);
            DateBuilder.ValidateMinute(minute);

            string cronExpression = string.Format("0 {0} {1} ? * {2}", minute, hour, ((int) dayOfWeek) + 1);

            return CronScheduleNoParseException(cronExpression);
        }

        /// <summary>
        /// Create a CronScheduleBuilder with a cron-expression that sets the
        /// schedule to fire one per month on the given day of month at the given
        /// time (hour and minute).
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="dayOfMonth">the day of the month to fire</param>
        /// <param name="hour">the hour of day to fire</param>
        /// <param name="minute">the minute of the given hour to fire</param>
        /// <returns>the new CronScheduleBuilder</returns>
        /// <seealso cref="CronExpression" />
        public static CronScheduleBuilder MonthlyOnDayAndHourAndMinute(int dayOfMonth, int hour, int minute)
        {
            DateBuilder.ValidateDayOfMonth(dayOfMonth);
            DateBuilder.ValidateHour(hour);
            DateBuilder.ValidateMinute(minute);

            string cronExpression = String.Format("0 {0} {1} {2} * ?", minute, hour, dayOfMonth);

            return CronScheduleNoParseException(cronExpression);
        }

        /// <summary>
        /// The <see cref="TimeZoneInfo" /> in which to base the schedule.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="tz">the time-zone for the schedule.</param>
        /// <returns>the updated CronScheduleBuilder</returns>
        /// <seealso cref="CronExpression.TimeZone" />
        public CronScheduleBuilder InTimeZone(TimeZoneInfo tz)
        {
            cronExpression.TimeZone = tz;
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
        public CronScheduleBuilder WithMisfireHandlingInstructionIgnoreMisfires()
        {
            misfireInstruction = MisfireInstruction.IgnoreMisfirePolicy;
            return this;
        }

        /// <summary>
        /// If the Trigger misfires, use the <see cref="MisfireInstruction.CronTrigger.DoNothing" />
        /// instruction.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the updated CronScheduleBuilder</returns>
        /// <seealso cref="MisfireInstruction.CronTrigger.DoNothing" />
        public CronScheduleBuilder WithMisfireHandlingInstructionDoNothing()
        {
            misfireInstruction = MisfireInstruction.CronTrigger.DoNothing;
            return this;
        }

        /// <summary>
        /// If the Trigger misfires, use the <see cref="MisfireInstruction.CronTrigger.FireOnceNow" />
        /// instruction.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the updated CronScheduleBuilder</returns>
        /// <seealso cref="MisfireInstruction.CronTrigger.FireOnceNow" />
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

    /// <summary>
    /// Extension methods that attach <see cref="CronScheduleBuilder" /> to <see cref="TriggerBuilder" />.
    /// </summary>
    public static class CronScheduleTriggerBuilderExtensions
    {
        public static TriggerBuilder WithCronSchedule(this TriggerBuilder triggerBuilder, string cronExpression)
        {
            CronScheduleBuilder builder = CronScheduleBuilder.CronSchedule(cronExpression);
            return triggerBuilder.WithSchedule(builder);
        }

        public static TriggerBuilder WithCronSchedule(this TriggerBuilder triggerBuilder, string cronExpression, Action<CronScheduleBuilder> action)
        {
            CronScheduleBuilder builder = CronScheduleBuilder.CronSchedule(cronExpression);
            action(builder);
            return triggerBuilder.WithSchedule(builder);
        }
    }
}