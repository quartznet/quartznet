using System;
using System.Linq;

using EWSoftware.PDI;
using Quartz.Logging;
namespace Quartz.Impl.Triggers
{
    public class RecurrenceTriggerImpl : AbstractTrigger, IRecurrenceTrigger
    {
        private static readonly ILog logger = LogProvider.GetLogger(typeof(CronTriggerImpl));
        public override bool HasMillisecondPrecision { get; }
        private static int YearToGiveUpSchedulingAt = 2299;

        public string? RecurrenceRule
        {
            get => Recurrence!.ToString();
            set => Recurrence = new Recurrence(value!);
        }

        public Recurrence? Recurrence { get; set; }

        public override DateTimeOffset StartTimeUtc
        {
            get => Recurrence!.StartDateTime;
            set => Recurrence!.StartDateTime = value.DateTime;
        }

        public override DateTimeOffset? EndTimeUtc
        {
            get => Recurrence!.RecurUntil;
            set => Recurrence!.RecurUntil = value!.Value.DateTime;
        }
        public DateTimeOffset? NextFireTimeUtc { get; set; }
        public DateTimeOffset? PreviousFireTime { get; set; }
        public RecurrenceTriggerImpl()
        {
            StartTimeUtc = SystemTime.UtcNow();
        }
        public RecurrenceTriggerImpl(string name, string group) : base(name, group)
        {
            StartTimeUtc = SystemTime.UtcNow();
        }

        public RecurrenceTriggerImpl(string name, string group, string recurrenceRule) : base(name, group)
        {
            StartTimeUtc = SystemTime.UtcNow();
            RecurrenceRule = recurrenceRule;
        }

        public RecurrenceTriggerImpl(string name, string group, Recurrence recurrence) : base(name, group)
        {
            StartTimeUtc = SystemTime.UtcNow();
            RecurrenceRule = recurrence.ToString();
            Recurrence = recurrence;
        }
        public RecurrenceTriggerImpl(string name, string group, string jobName, string jobGroup, string recurrence) : 
            this(name, group, jobName, jobGroup, new Recurrence(recurrence))
        {
        } 
        public RecurrenceTriggerImpl(string name, string group, string jobName, string jobGroup, Recurrence recurrence) : base(name, group, jobName, jobGroup)
        {
            StartTimeUtc = SystemTime.UtcNow();
            RecurrenceRule = recurrence.ToString();
        }
        public RecurrenceTriggerImpl(string name, string group, string jobName, string jobGroup, DateTimeOffset startTimeUtc,  string recurrence) : 
            this(name, group, jobName, jobGroup, startTimeUtc, null, recurrence)
        {
        } 
        public RecurrenceTriggerImpl(string name, string group, string jobName, string jobGroup, DateTimeOffset startTimeUtc, Recurrence recurrence) 
            : this(name, group, jobName, jobGroup, startTimeUtc, null, recurrence)
        {
        }
        public RecurrenceTriggerImpl(string name, string group, string jobName, string jobGroup, DateTimeOffset startTimeUtc, DateTimeOffset? endTimeUtc, string recurrence) : 
            this(name, group, jobName, jobGroup, startTimeUtc, endTimeUtc, new Recurrence(recurrence))
        {
        } 
        public RecurrenceTriggerImpl(string name, string group, string jobName, string jobGroup, DateTimeOffset startTimeUtc, DateTimeOffset? endTimeUtc, Recurrence recurrence) 
            : base(name, group, jobName, jobGroup)
        {
            StartTimeUtc = SystemTime.UtcNow();
            RecurrenceRule = recurrence.ToString();
            StartTimeUtc = (startTimeUtc == DateTimeOffset.MinValue) ? SystemTime.UtcNow() : startTimeUtc;
            if (endTimeUtc.HasValue)
            {
                EndTimeUtc = endTimeUtc;
            }
        }
        public override void SetNextFireTimeUtc(DateTimeOffset? nextFireTime)
        {
            NextFireTimeUtc = nextFireTime;
        }

        public override void SetPreviousFireTimeUtc(DateTimeOffset? previousFireTime)
        {
            PreviousFireTime = previousFireTime;
        }

        public override DateTimeOffset? GetPreviousFireTimeUtc()
        {
            return PreviousFireTime;
        }
        public override DateTimeOffset? GetNextFireTimeUtc()
        {
            return NextFireTimeUtc;
        }
        public override DateTimeOffset? GetFireTimeAfter(DateTimeOffset? afterTime)
        {
            return Recurrence!.AllInstances().Where(occurrence => occurrence > afterTime).First();
        }
        public override IScheduleBuilder GetScheduleBuilder()
        {
            RecurrenceScheduleBuilder rb = RecurrenceScheduleBuilder.RecurrenceSchedule(Recurrence!);
            switch (MisfireInstruction)
            {
                default:
                    throw new NotImplementedException("Misfire Instructions in RecurrenceTriggers haven't been implemented.");
            }
        }

        public override DateTimeOffset? FinalFireTimeUtc { get; }
        public override void Triggered(ICalendar? cal)
        {
            PreviousFireTime = NextFireTimeUtc;
            NextFireTimeUtc = GetFireTimeAfter(NextFireTimeUtc);
            
            // Make sure that NextFireTime is not included in the calendar
            while (NextFireTimeUtc.HasValue && cal != null && cal.IsTimeIncluded(NextFireTimeUtc.Value))
            {
                NextFireTimeUtc = GetFireTimeAfter(NextFireTimeUtc);
            }
        }

        public override DateTimeOffset? ComputeFirstFireTimeUtc(ICalendar? cal)
        {
            var nextFireTimeUtc = GetFireTimeAfter(StartTimeUtc);
            
            // Make sure that NextFireTime is not included in the calendar
            while (NextFireTimeUtc.HasValue && cal != null && cal.IsTimeIncluded(NextFireTimeUtc.Value))
            {
                nextFireTimeUtc = GetFireTimeAfter(NextFireTimeUtc);
            }

            return nextFireTimeUtc;
        }

        public override bool GetMayFireAgain()
        {
            foreach (DateTime instance in Recurrence!.AllInstances())
            {
                if (instance < EndTimeUtc)
                    return true;
            }

            return false;
        }
        protected override bool ValidateMisfireInstruction(int misfireInstruction)
        {
            if (misfireInstruction < Quartz.MisfireInstruction.IgnoreMisfirePolicy)
                return false;
            else if (misfireInstruction > Quartz.MisfireInstruction.RecurrenceTrigger.DoNothing)
                return false;
            return true;
        }

        public override void UpdateAfterMisfire(ICalendar? cal)
        {
            switch (MisfireInstruction)
            {
                case Quartz.MisfireInstruction.SmartPolicy:
                case Quartz.MisfireInstruction.RecurrenceTrigger.FireOnceNow:
                    SetNextFireTimeUtc(SystemTime.UtcNow());
                    break;
                case Quartz.MisfireInstruction.RecurrenceTrigger.DoNothing:
                    SetNextFireTimeUtc(SystemTime.UtcNow());
                    SetNextFireTimeUtc(ComputeFirstFireTimeUtc(cal));
                    break;
            }
        }

        public override void UpdateWithNewCalendar(ICalendar cal, TimeSpan misfireThreshold)
        {
            NextFireTimeUtc = GetFireTimeAfter(PreviousFireTime);

            if (!NextFireTimeUtc.HasValue || cal == null)
                return;

            while (NextFireTimeUtc.HasValue && !cal.IsTimeIncluded(NextFireTimeUtc.Value))
            {
                NextFireTimeUtc = GetFireTimeAfter(NextFireTimeUtc);

                if (!NextFireTimeUtc.HasValue || NextFireTimeUtc.Value.Year > YearToGiveUpSchedulingAt)
                    return;

                if (NextFireTimeUtc.HasValue && NextFireTimeUtc.Value < DateTime.Now)
                {
                    TimeSpan diff = DateTime.Now - NextFireTimeUtc.Value;
                    if (diff >= misfireThreshold)
                    {
                        NextFireTimeUtc = GetFireTimeAfter(NextFireTimeUtc);
                    }
                }
            }
        }

 
    }
}