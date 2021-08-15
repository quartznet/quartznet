using EWSoftware.PDI;
using System;
using System.Linq;

using Quartz.Impl.Triggers;
using Quartz.Spi;

namespace Quartz
{
    public class RecurrenceScheduleBuilder : ScheduleBuilder<IRecurrenceTrigger>
    {
        private Recurrence? Recurrence;
        private int misfireInstruction = MisfireInstruction.SmartPolicy;
        private int? maxCount = null;
        protected RecurrenceScheduleBuilder(Recurrence recurrence)
        {
            Recurrence = recurrence  ?? throw new ArgumentNullException(nameof(recurrence), "recurrence cannot be null");
        }
        public override IMutableTrigger Build()
        {
            var trigger = new RecurrenceTriggerImpl();
            Recurrence!.MaximumOccurrences = maxCount ?? 0;
            
            trigger.Recurrence = Recurrence;
            trigger.MisfireInstruction = misfireInstruction;
            return trigger;
        }
        
        public static RecurrenceScheduleBuilder RecurrenceSchedule(Recurrence recurrence)
        {
            return new RecurrenceScheduleBuilder(recurrence: recurrence);
        }

        /// <summary>
        /// Creates a RecurrenceScheduleBuilder with the given RecurrenceRule.
        /// </summary>
        /// <param name="rule">The RecurrenceRule to based the RecurrenceSchedule on.</param>
        /// <returns>The new RecurrenceScheduleBuilder</returns>
        public static RecurrenceScheduleBuilder WithRecurrenceRuleString(string rule)
        {
            return RecurrenceSchedule(new Recurrence(rule));
        }

        /// <summary>
        /// Creates a RecurrenceScheduleBuilder with the given Recurrence.
        /// </summary>
        /// <param name="recurrence">The Recurrence to based the RecurrenceSchedule on.</param>
        /// <returns>the new RecurrenceScheduleBuilder</returns>
        public static RecurrenceScheduleBuilder WithRecurrence(Recurrence recurrence)
        {
            return RecurrenceSchedule(recurrence);
        }

        /// <summary>
        /// Creates a RecurrenceScheduleBuilder with an hourly pattern and the given interval.
        /// </summary>
        /// <param name="interval">The amount of hours between every occurence.</param>
        /// <returns>the new RecurrenceScheduleBuilder</returns>
        public static RecurrenceScheduleBuilder Hourly(int interval)
        {
            var recur = new Recurrence();
            recur.Frequency = RecurFrequency.Hourly;
            recur.Interval = interval;
            return RecurrenceSchedule(recur);
        }
        
        /// <summary>
        /// Creates a RecurrenceScheduleBuilder with an daily pattern and the given interval.
        /// </summary>
        /// <param name="interval">The amount of days between every occurence.</param>
        /// <returns>the new RecurrenceScheduleBuilder</returns>
        public static RecurrenceScheduleBuilder Daily(int interval)
        {
            var recur = new Recurrence();
            recur.RecurDaily(interval);
            return RecurrenceSchedule(recur);
        }

        /// <summary>
        /// Creates a RecurrenceScheduleBuilder with an Weekly pattern on the given days of the week.
        /// </summary>
        /// <param name="daysOfWeek">The days of the Week the Recurrence should occur on.</param>
        /// <param name="interval">The amount of Week between every occurence.</param>
        /// <returns>the new RecurrenceScheduleBuilder</returns>
        public static RecurrenceScheduleBuilder WeeklyAtDays(DayOfWeek[] daysOfWeek, int interval)
        {
            if (!daysOfWeek.Any())
                throw new ArgumentException("daysOfWeek cannot be empty", nameof(daysOfWeek));
            var recur = new Recurrence();
            DaysOfWeek daysOfWeekForRecurrence = DaysOfWeek.None;
            foreach (DayOfWeek dayOfWeek in daysOfWeek)
            {
                switch (dayOfWeek)
                {
                    case DayOfWeek.Sunday:
                        daysOfWeekForRecurrence += 0x00000001;
                        break;
                    case DayOfWeek.Monday:
                        daysOfWeekForRecurrence += 0x00000002;
                        break;
                    case DayOfWeek.Tuesday:
                        daysOfWeekForRecurrence += 0x00000004;
                        break;
                    case DayOfWeek.Wednesday:
                        daysOfWeekForRecurrence += 0x00000008;
                        break;
                    case DayOfWeek.Thursday:
                        daysOfWeekForRecurrence += 0x00000010;
                        break;
                    case DayOfWeek.Friday:
                        daysOfWeekForRecurrence += 0x00000020;
                        break;
                    case DayOfWeek.Saturday:
                        daysOfWeekForRecurrence += 0x00000040;
                        break;
                }
            }
            recur.RecurWeekly(interval, daysOfWeekForRecurrence);
            return RecurrenceSchedule(recurrence: recur);
        }

        /// <summary>
        /// Creates a RecurrenceScheduleBuilder with an Monthly pattern on the given days of the month.
        /// </summary>
        /// <param name="daysOfMonth">The days of the Month the Recurrence should occur on.</param>
        /// <param name="interval">The amount of Months between every occurence.</param>
        /// <returns>the new RecurrenceScheduleBuilder</returns>
        public static RecurrenceScheduleBuilder MonthlyAtDays(int[] daysOfMonth, int interval)
        {
            var recur = new Recurrence();
            recur.Frequency = RecurFrequency.Monthly;
            recur.ByMonthDay.AddRange(daysOfMonth);
            return RecurrenceSchedule(recur);
        }
        
        /// <summary>
        /// Creates a RecurrenceScheduleBuilder with a Yearly pattern on the given days of the month.
        /// </summary>
        /// <param name="daysOfYear">The days of the Year the Recurrence should occur on.</param>
        /// <param name="interval">The amount of Years between every occurence.</param>
        /// <returns>the new RecurrenceScheduleBuilder</returns>
        public static RecurrenceScheduleBuilder YearlyAtDays(int[] daysOfYear, int interval)
        {
            var recur = new Recurrence();
            recur.Frequency = RecurFrequency.Yearly;
            recur.ByYearDay.AddRange(daysOfYear);
            return RecurrenceSchedule(recur);
        }
        
        /// <summary>
        /// Sets the maximum amount of times the recurrence can occur. 
        /// </summary>
        /// <remarks>
        /// If this property is set, the EndDate will get ignored and will be calculated
        /// based on the amount of times the recurrence can occur.
        /// </remarks>
        /// <param name="maxOcur">The start time.</param>
        /// <returns>The RecurrenceScheduleBuilder with the start time set accordingly</returns>
        public RecurrenceScheduleBuilder WithMaximumOccurrences(int maxOcur)
        {
            maxCount = maxOcur;
            return this;
        }
        
        /// <summary>
        /// Sets the MisfireInstruction to IgnoreMisfirePolicy
        /// </summary>
        /// <returns>The RecurrenceScheduleBuilder with the MisfireInstruction set appropriately</returns>
        public RecurrenceScheduleBuilder WithMisfireHandlingInstructionIgnoreMisfires()
        {
            misfireInstruction = MisfireInstruction.IgnoreMisfirePolicy;
            return this;
        }
        /// <summary>
        /// Sets the MisfireInstruction to DoNothing
        /// </summary>
        /// <returns>The RecurrenceScheduleBuilder with the MisfireInstruction set appropriately</returns>
        public RecurrenceScheduleBuilder WithMisfireHandlingInstructionDoNothing()
        {
            misfireInstruction = MisfireInstruction.RecurrenceTrigger.DoNothing;
            return this;
        }
        
        /// <summary>
        /// Sets the MisfireInstruction to IgnoreMisfirePolicy
        /// </summary>
        /// <returns>The RecurrenceScheduleBuilder with the MisfireInstruction set appropriately</returns>
        public RecurrenceScheduleBuilder WithMisfireHandlingInstructionFireAndProceed()
        {
            misfireInstruction = MisfireInstruction.RecurrenceTrigger.FireOnceNow;
            return this;
        }
    }
}