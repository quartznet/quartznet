﻿#region License

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
using Quartz.Collection;


namespace Quartz
{
    /// <summary>
    /// A <see cref="IScheduleBuilder"/> implementation that build schedule for DailyTimeIntervalTrigger.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This builder provide an extra convenient method for you to set the trigger's EndTimeOfDay. You may
    /// use either endingDailyAt() or EndingDailyAfterCount() to set the value. The later will auto calculate
    /// your EndTimeOfDay by using the interval, IntervalUnit and StartTimeOfDay to perform the calculation.
    /// </para>
    /// <para>
    /// When using EndingDailyAfterCount(), you should note that it is used to calculating EndTimeOfDay. So
    /// if your startTime on the first day is already pass by a time that would not add up to the count you
    /// expected, until the next day comes. Remember that DailyTimeIntervalTrigger will use StartTimeOfDay
    /// and endTimeOfDay as fresh per each day!
    /// </para>
    /// </remarks>
    /// <author>James House</author>
    /// <author>Zemian Deng saltnlight5@gmail.com</author>
    /// <author>Nuno Maia (.NET)</author>
    public class DailyTimeIntervalScheduleBuilder : ScheduleBuilder<IDailyTimeIntervalTrigger>
    {
        private int interval = 1;
        private IntervalUnit intervalUnit = IntervalUnit.Minute;
        private ISet<DayOfWeek> daysOfWeek;
        private TimeOfDay startTimeOfDayUtc;
        private TimeOfDay endTimeOfDayUtc;
        private int repeatCount = DailyTimeIntervalTriggerImpl.RepeatIndefinitely;

        private int misfireInstruction = MisfireInstruction.SmartPolicy;

        /// <summary>
        /// A set of all days of the week.
        /// </summary>
        /// <remarks>
        /// The set contains all values between <see cref="DayOfWeek.Sunday"/> and <see cref="DayOfWeek.Saturday"/>
        /// </remarks>
        public static readonly ISet<DayOfWeek> AllDaysOfTheWeek;

        /// <summary>
        /// A set of the business days of the week (for locales similar to the USA).
        /// </summary>
        /// <remarks>
        /// The set contains all values between <see cref="DayOfWeek.Monday"/> and <see cref="DayOfWeek.Friday"/> 
        /// </remarks>
        public static readonly ISet<DayOfWeek> MondayThroughFriday;

        /// <summary>
        /// A set of the weekend days of the week (for locales similar to the USA).
        /// </summary>
        /// <remarks>
        /// The set contains <see cref="DayOfWeek.Saturday"/> and <see cref="DayOfWeek.Sunday"/>
        /// </remarks>
        public static readonly ISet<DayOfWeek> SaturdayAndSunday;

        static DailyTimeIntervalScheduleBuilder()
        {
            AllDaysOfTheWeek = new HashSet<DayOfWeek>();
            MondayThroughFriday = new HashSet<DayOfWeek>();
            foreach (DayOfWeek d in Enum.GetValues(typeof(DayOfWeek)))
            {
                AllDaysOfTheWeek.Add(d);

                if ((d >= DayOfWeek.Monday) && (d <= DayOfWeek.Friday))
                {
                    MondayThroughFriday.Add(d);
                }
            }

            SaturdayAndSunday = new HashSet<DayOfWeek>();
            SaturdayAndSunday.Add(DayOfWeek.Sunday);
            SaturdayAndSunday.Add(DayOfWeek.Saturday);
        }

        private DailyTimeIntervalScheduleBuilder()
        {
        }
        
        
        /// <summary>
        /// Create a DailyTimeIntervalScheduleBuilder
        /// </summary>
        /// <returns>The new DailyTimeIntervalScheduleBuilder</returns>
        public static DailyTimeIntervalScheduleBuilder Create()
        {
            return new DailyTimeIntervalScheduleBuilder();
        }


        /// <summary>
        /// Build the actual Trigger -- NOT intended to be invoked by end users,
        /// but will rather be invoked by a TriggerBuilder which this 
        /// ScheduleBuilder is given to.
        /// </summary>
        /// <returns></returns>
        public override IMutableTrigger Build()
        {

            DailyTimeIntervalTriggerImpl st = new DailyTimeIntervalTriggerImpl();
            st.RepeatInterval = interval;
            st.RepeatIntervalUnit = intervalUnit;
            st.MisfireInstruction = misfireInstruction;
            st.RepeatCount = repeatCount;

            if (daysOfWeek != null)
            {
                st.DaysOfWeek = daysOfWeek;
            }
            else
            {
                st.DaysOfWeek = AllDaysOfTheWeek;
            }

            if (startTimeOfDayUtc != null)
            {
                st.StartTimeOfDayUtc = startTimeOfDayUtc;
            }
            else
            {
                st.StartTimeOfDayUtc = TimeOfDay.HourAndMinuteOfDay(0, 0);
            }

            if (endTimeOfDayUtc != null)
            {
                st.EndTimeOfDayUtc = endTimeOfDayUtc;
            }
            else
            {
                st.EndTimeOfDayUtc = TimeOfDay.HourMinuteAndSecondOfDay(23, 59, 59);
            }

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
        public DailyTimeIntervalScheduleBuilder WithInterval(int interval, IntervalUnit unit)
        {
            if (!((unit == IntervalUnit.Second) ||
                    (unit == IntervalUnit.Minute) || (unit == IntervalUnit.Hour)))
            {
                throw new ArgumentException("Invalid repeat IntervalUnit (must be Second, Minute or Hour).");
            }

            ValidateInterval(interval);
            this.interval = interval;
            this.intervalUnit = unit;
            return this;
        }

        /// <summary>
        /// Specify an interval in the IntervalUnit.Second that the produced
        /// Trigger will repeat at.
        /// </summary>
        /// <param name="intervalInSeconds">The number of seconds at which the trigger should repeat.</param>
        /// <returns>the updated DailyTimeIntervalScheduleBuilder></returns>
        /// <see cref="IDailyTimeIntervalTrigger.RepeatInterval"/>
        /// <see cref="IDailyTimeIntervalTrigger.RepeatIntervalUnit"/>
        public DailyTimeIntervalScheduleBuilder WithIntervalInSeconds(int intervalInSeconds)
        {
            WithInterval(intervalInSeconds, IntervalUnit.Second);
            return this;
        }

        /// <summary>
        /// Specify an interval in the IntervalUnit.Minute that the produced
        /// Trigger will repeat at.
        /// </summary>
        /// <param name="intervalInMinutes">The number of minutes at which the trigger should repeat.</param>
        /// <returns>the updated DailyTimeIntervalScheduleBuilder></returns>
        /// <see cref="IDailyTimeIntervalTrigger.RepeatInterval"/>
        /// <see cref="IDailyTimeIntervalTrigger.RepeatIntervalUnit"/>
        public DailyTimeIntervalScheduleBuilder WithIntervalInMinutes(int intervalInMinutes)
        {
            WithInterval(intervalInMinutes, IntervalUnit.Minute);
            return this;
        }

        /// <summary>
        /// Specify an interval in the IntervalUnit.Hour that the produced
        /// Trigger will repeat at.
        /// </summary>
        /// <param name="intervalInHours">The number of hours at which the trigger should repeat.</param>
        /// <returns>the updated DailyTimeIntervalScheduleBuilder></returns>
        /// <see cref="IDailyTimeIntervalTrigger.RepeatInterval"/>
        /// <see cref="IDailyTimeIntervalTrigger.RepeatIntervalUnit"/>
        public DailyTimeIntervalScheduleBuilder WithIntervalInHours(int intervalInHours)
        {
            WithInterval(intervalInHours, IntervalUnit.Hour);
            return this;
        }

        /// <summary>
        /// Set the trigger to fire on the given days of the week.
        /// </summary>
        /// <param name="daysOfWeek">a Set containing the integers representing the days of the week, defined by <see cref="DayOfWeek.Sunday"/> - <see cref="DayOfWeek.Saturday"/>. 
        /// </param>
        /// <returns>the updated DailyTimeIntervalScheduleBuilder</returns>
        public DailyTimeIntervalScheduleBuilder OnDaysOfTheWeek(ISet<DayOfWeek> daysOfWeek)
        {
            if (daysOfWeek == null || daysOfWeek.Count == 0)
            {
                throw new ArgumentException("Days of week must be an non-empty set.");
            }

            foreach (DayOfWeek day in daysOfWeek)
            {
                if (!AllDaysOfTheWeek.Contains(day))
                {
                    throw new ArgumentException("Invalid value for day of week: " + day);
                }
            }

            this.daysOfWeek = daysOfWeek;
            return this;
        }


        /// <summary>
        /// Set the trigger to fire on the days from Monday through Friday.
        /// </summary>
        /// <returns>the updated DailyTimeIntervalScheduleBuilder</returns>
        public DailyTimeIntervalScheduleBuilder OnMondayThroughFriday()
        {
            this.daysOfWeek = MondayThroughFriday;
            return this;
        }

        /// <summary>
        /// Set the trigger to fire on the days Saturday and Sunday.
        /// </summary>
        /// <returns>the updated DailyTimeIntervalScheduleBuilder</returns>
        public DailyTimeIntervalScheduleBuilder OnSaturdayAndSunday()
        {
            this.daysOfWeek = SaturdayAndSunday;
            return this;
        }

        /// <summary>
        /// Set the trigger to fire on all days of the week.
        /// </summary>
        /// <returns>the updated DailyTimeIntervalScheduleBuilder</returns>
        public DailyTimeIntervalScheduleBuilder OnEveryDay()
        {
            this.daysOfWeek = AllDaysOfTheWeek;
            return this;
        }

        /// <summary>
        /// Set the trigger to begin firing each day at the given time.
        /// </summary>
        /// <param name="timeOfDayUtc"></param>
        /// <returns>the updated DailyTimeIntervalScheduleBuilder</returns>
        public DailyTimeIntervalScheduleBuilder StartingDailyAt(TimeOfDay timeOfDayUtc)
        {

            if (timeOfDayUtc == null)
            {
                throw new ArgumentException("Start time of day cannot be null!");
            }

            this.startTimeOfDayUtc = timeOfDayUtc;
            return this;
        }

        /// <summary>
        /// Set the startTimeOfDay for this trigger to end firing each day at the given time.
        /// </summary>
        /// <param name="timeOfDayUtc"></param>
        /// <returns>the updated DailyTimeIntervalScheduleBuilder</returns>
        public DailyTimeIntervalScheduleBuilder EndingDailyAt(TimeOfDay timeOfDayUtc)
        {
            this.endTimeOfDayUtc = timeOfDayUtc;
            return this;
        }

            
        /// <summary>
        /// Calculate and set the EndTimeOfDay using count, interval and StarTimeOfDay. This means
        /// that these must be set before this method is call.
        /// </summary>
        /// <param name="count"></param>
        /// <returns>the updated DailyTimeIntervalScheduleBuilder</returns>
        public DailyTimeIntervalScheduleBuilder EndingDailyAfterCount(int count)
        {
            if (count <= 0)
            {
                throw new ArgumentException("Ending daily after count must be a positive number!");
            }

            if (startTimeOfDayUtc == null)
            {
                throw new ArgumentException("You must set the StartDailyAt() before calling this EndingDailyAfterCount()!");
            }

            DateTimeOffset today = DateTime.UtcNow;
            DateTimeOffset startTimeOfDayDate = startTimeOfDayUtc.GetTimeOfDayForDate(today).Value;
            DateTimeOffset maxEndTimeOfDayDate = TimeOfDay.HourMinuteAndSecondOfDay(23, 59, 59).GetTimeOfDayForDate(today).Value;
            TimeSpan remainingMillisInDay = maxEndTimeOfDayDate - startTimeOfDayDate;
            TimeSpan intervalInMillis = TimeSpan.Zero;
            if (intervalUnit == IntervalUnit.Second)
            {
                intervalInMillis = TimeSpan.FromSeconds(interval);
            }
            else if (intervalUnit == IntervalUnit.Minute)
            {
                intervalInMillis = TimeSpan.FromSeconds(interval * 60);
            }
            else if (intervalUnit == IntervalUnit.Hour)
            {
                intervalInMillis = TimeSpan.FromSeconds(interval * 60 * 24);
            }
            else
            {
                throw new ArgumentException("The IntervalUnit: " + intervalUnit + " is invalid for this trigger.");
            }

            if (remainingMillisInDay < intervalInMillis)
            {
                throw new ArgumentException("The startTimeOfDay is too late with given Interval and IntervalUnit values.");
            }

            long maxNumOfCount = (remainingMillisInDay.Ticks / intervalInMillis.Ticks);
            if (count > maxNumOfCount)
            {
                throw new ArgumentException("The given count " + count + " is too large! The max you can set is " + maxNumOfCount);
            }

            TimeSpan incrementInMillis = TimeSpan.FromTicks((count - 1) * intervalInMillis.Ticks) ;
            DateTimeOffset endTimeOfDayDate = startTimeOfDayDate.Add(incrementInMillis);

            if (endTimeOfDayDate > maxEndTimeOfDayDate)
            {
                throw new ArgumentException("The given count " + count + " is too large! The max you can set is " + maxNumOfCount);
            }

            DateTime cal = DateTime.UtcNow.Date;
            cal = cal.Add(endTimeOfDayDate.TimeOfDay);
            endTimeOfDayUtc = TimeOfDay.HourMinuteAndSecondOfDay(cal.Hour, cal.Minute, cal.Second);
            return this;
        }

        /// <summary>
        /// If the Trigger misfires, use the 
        /// <see cref="MisfireInstruction.IgnoreMisfirePolicy"/> instruction.
        /// </summary>
        /// <returns>the updated DailyTimeIntervalScheduleBuilder</returns>
        /// <seealso cref="MisfireInstruction.IgnoreMisfirePolicy"/>
        public DailyTimeIntervalScheduleBuilder WithMisfireHandlingInstructionIgnoreMisfires()
        {
            misfireInstruction = MisfireInstruction.IgnoreMisfirePolicy;
            return this;
        }

        /// <summary>
        /// If the Trigger misfires, use the 
        /// <see cref="MisfireInstruction.DailyTimeIntervalTrigger.DoNothing"/> instruction.
        /// </summary>
        /// <returns>the updated DailyTimeIntervalScheduleBuilder</returns>
        /// <seealso cref="MisfireInstruction.DailyTimeIntervalTrigger.DoNothing"/>
        public DailyTimeIntervalScheduleBuilder WithMisfireHandlingInstructionDoNothing()
        {
            misfireInstruction = MisfireInstruction.DailyTimeIntervalTrigger.DoNothing;
            return this;
        }

        /// <summary>
        /// If the Trigger misfires, use the 
        /// <see cref="MisfireInstruction.DailyTimeIntervalTrigger.FireOnceNow"/> instruction.
        /// </summary>
        /// <returns>the updated DailyTimeIntervalScheduleBuilder</returns>
        /// <seealso cref="MisfireInstruction.DailyTimeIntervalTrigger.FireOnceNow"/>
        public DailyTimeIntervalScheduleBuilder WithMisfireHandlingInstructionFireAndProceed()
        {
            misfireInstruction = MisfireInstruction.DailyTimeIntervalTrigger.FireOnceNow;
            return this;
        }

        /// <summary>
        /// Set number of times for interval to repeat.
        /// </summary>
        /// <remarks>
        /// Note: if you want total count = 1 (at start time) + repeatCount
        /// </remarks>
        /// <param name="repeatCount"></param>
        /// <returns></returns>
        public DailyTimeIntervalScheduleBuilder WithRepeatCount(int repeatCount)
        {
            this.repeatCount = repeatCount;
            return this;
        }

        private void ValidateInterval(int interval)
        {
            if (interval <= 0)
            {
                throw new ArgumentException("Interval must be a positive value.");
            }
        }
    }

    /// <summary>
    /// Extension methods to aid fluent trigger syntax.
    /// </summary>
    public static class DailyTimeIntervalScheduleBuilderExtensions
    {
        public static TriggerBuilder WithDailyTimeIntervalSchedule(this TriggerBuilder triggerBuilder)
        {
            DailyTimeIntervalScheduleBuilder builder = DailyTimeIntervalScheduleBuilder.Create();
            return triggerBuilder.WithSchedule(builder);
        }

        public static TriggerBuilder WithDailyTimeIntervalSchedule(this TriggerBuilder triggerBuilder, Action<DailyTimeIntervalScheduleBuilder> action)
        {
            DailyTimeIntervalScheduleBuilder builder = DailyTimeIntervalScheduleBuilder.Create();
            action(builder);
            return triggerBuilder.WithSchedule(builder);
        }
    }
}
    
