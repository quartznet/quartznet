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
        protected RecurrenceScheduleBuilder(Recurrence recurrence)
        {
            Recurrence = recurrence  ?? throw new ArgumentNullException(nameof(recurrence), "recurrence cannot be null");
        }
        public override IMutableTrigger Build()
        {
            var trigger = new RecurrenceTriggerImpl();
            trigger.Recurrence = Recurrence;
            trigger.MisfireInstruction = misfireInstruction;
            return trigger;
        }

        public static RecurrenceScheduleBuilder RecurrenceSchedule(Recurrence recurrence)
        {
            return new RecurrenceScheduleBuilder(recurrence: recurrence);
        }

        public RecurrenceScheduleBuilder WithRecurrenceRuleString(string rule)
        {
            return RecurrenceSchedule(new Recurrence(rule));
        }

        public RecurrenceScheduleBuilder WithRecurrence(Recurrence recur)
        {
            return RecurrenceSchedule(recur);
        }

        public RecurrenceScheduleBuilder Hourly(int interval)
        {
            var recur = new Recurrence();
            recur.Frequency = RecurFrequency.Hourly;
            recur.Interval = interval;
            return RecurrenceSchedule(recur);
        }
        
        public RecurrenceScheduleBuilder Daily(int interval)
        {
            var recur = new Recurrence();
            recur.RecurDaily(interval);
            return RecurrenceSchedule(recur);
        }

        public RecurrenceScheduleBuilder WeeklyAtDays(DayOfWeek[] daysOfWeek, int interval)
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

        public RecurrenceScheduleBuilder MonthlyAtDays(int[] daysOfMonth, int interval)
        {
            var recur = new Recurrence();
            recur.Frequency = RecurFrequency.Monthly;
            recur.ByMonthDay.AddRange(daysOfMonth);
            return RecurrenceSchedule(recur);
        }

        public RecurrenceScheduleBuilder YearlyAtDays(int[] daysOfYear, int interval)
        {
            var recur = new Recurrence();
            recur.Frequency = RecurFrequency.Yearly;
            recur.ByYearDay.AddRange(daysOfYear);
            return RecurrenceSchedule(recur);
        }
        
        public RecurrenceScheduleBuilder WithMisfireHandlingInstructionIgnoreMisfires()
        {
            misfireInstruction = MisfireInstruction.IgnoreMisfirePolicy;
            return this;
        }
        
        public RecurrenceScheduleBuilder WithMisfireHandlingInstructionDoNothing()
        {
            misfireInstruction = MisfireInstruction.RecurrenceTrigger.DoNothing;
            return this;
        }
        
        public RecurrenceScheduleBuilder WithMisfireHandlingInstructionFireAndProceed()
        {
            misfireInstruction = MisfireInstruction.RecurrenceTrigger.FireOnceNow;
            return this;
        }
        
    }
}