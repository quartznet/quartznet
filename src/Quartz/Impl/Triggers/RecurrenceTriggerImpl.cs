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

        /// <summary>
        /// Create a <see cref="RecurrenceTriggerImpl"/> with no settings
        /// </summary>
        /// <remarks>
        /// The start-time will be set to the current time.
        /// </remarks>
        public RecurrenceTriggerImpl()
        {
            StartTimeUtc = SystemTime.UtcNow();
        }
        /// <summary>
        /// Create a <see cref="RecurrenceTriggerImpl"/> with the given name and default group.
        /// </summary>
        /// <remarks>
        /// The start time will be set to the current time
        /// </remarks>
        /// <param name="name">The name of the <see cref="ITrigger"/></param>
        public RecurrenceTriggerImpl(string name) : base(name, null)
        {
            StartTimeUtc = SystemTime.UtcNow();
        }
        /// <summary>
        /// Create a <see cref="RecurrenceTriggerImpl"/> with the given name and group.
        /// </summary>
        /// <remarks>
        /// The start time will be set to the current time
        /// </remarks>
        /// <param name="name">The Name of the <see cref="ITrigger"/></param>
        /// <param name="group">The name of the group of the <see cref="ITrigger"/></param>
        public RecurrenceTriggerImpl(string name, string group) : base(name, group)
        {
            StartTimeUtc = SystemTime.UtcNow();
        }
        /// <summary>
        /// Create a <see cref="RecurrenceTriggerImpl"/> with the given name, group and RecurrenceRule
        /// </summary>
        /// <remarks>
        /// A new Recurrence will be created using the recurrenceRule provided.
        /// </remarks>
        /// <param name="name">The Name of the <see cref="ITrigger"/></param>
        /// <param name="group">The name of the group of the <see cref="ITrigger"/></param>
        /// <param name="recurrenceRule">A RecurrenceRule describing the firing pattern of <see cref="ITrigger"/></param>
        public RecurrenceTriggerImpl(string name, string group, string recurrenceRule) : 
            this(name, group, new Recurrence(recurrenceRule)) { }
        /// <summary>
        /// Create a <see cref="RecurrenceTriggerImpl"/> with the given name, group and Recurrence
        /// </summary>
        /// <param name="name">The Name of the <see cref="ITrigger"/></param>
        /// <param name="group">The name of the group of the <see cref="ITrigger"/></param>
        /// <param name="recurrence">The Recurrence describing the firing pattern of the <see cref="ITrigger"/></param>
        public RecurrenceTriggerImpl(string name, string group, Recurrence recurrence) : base(name, group)
        {
            StartTimeUtc = SystemTime.UtcNow();
            Recurrence = recurrence;
        }
        /// <summary>
        /// Create a <see cref="ICronTrigger" /> with the given name and group,
        /// associated with the identified <see cref="IJobDetail" />,
        /// and with the given RecurrenceRule.
        /// </summary>
        /// <param name="name">The name of the <see cref="ITrigger" /></param>
        /// <param name="group">The group of the <see cref="ITrigger" /></param>
        /// <param name="jobName">name of the <see cref="IJobDetail" /> executed on firetime</param>
        /// <param name="jobGroup">Group of the <see cref="IJobDetail" /> executed on firetime</param>
        /// <param name="recurrenceRule">A RecurrenceRule describing the firing pattern of <see cref="ITrigger"/></param>
        public RecurrenceTriggerImpl(string name, string group, string jobName, string jobGroup, string recurrenceRule) : 
            this(name, group, jobName, jobGroup, new Recurrence(recurrenceRule))
        {
        } 
        
        /// <summary>
        /// Create a <see cref="ICronTrigger" /> with the given name and group,
        /// associated with the identified <see cref="IJobDetail" />,
        /// and with the given Recurrence.
        /// </summary>
        /// <param name="name">The name of the <see cref="ITrigger" /></param>
        /// <param name="group">The group of the <see cref="ITrigger" /></param>
        /// <param name="jobName">name of the <see cref="IJobDetail" /> executed on firetime</param>
        /// <param name="jobGroup">Group of the <see cref="IJobDetail" /> executed on firetime</param>
        /// <param name="recurrence">A Recurrence describing the firing pattern of <see cref="ITrigger"/></param>
        public RecurrenceTriggerImpl(string name, string group, string jobName, string jobGroup, Recurrence recurrence) : base(name, group, jobName, jobGroup)
        {
            StartTimeUtc = SystemTime.UtcNow();
            Recurrence = recurrence;
        }
        /// <summary>
        /// Create a <see cref="ICronTrigger" /> with the given name and group,
        /// associated with the identified <see cref="IJobDetail" />,
        /// and with the given RecurrenceRule.
        /// </summary>
        /// <param name="name">The name of the <see cref="ITrigger" /></param>
        /// <param name="group">The group of the <see cref="ITrigger" /></param>
        /// <param name="jobName">name of the <see cref="IJobDetail" /> executed on firetime</param>
        /// <param name="jobGroup">Group of the <see cref="IJobDetail" /> executed on firetime</param>
        /// <param name="startTimeUtc">The earliest time for hte <see cref="ITrigger"/> firing. </param>
        /// <param name="recurrenceRule">A RecurrenceRule describing the firing pattern of <see cref="ITrigger"/></param>
        public RecurrenceTriggerImpl(string name, string group, string jobName, string jobGroup, DateTimeOffset startTimeUtc,  string recurrenceRule) : 
            this(name, group, jobName, jobGroup, startTimeUtc, null, recurrenceRule)
        {
        } 
        /// <summary>
        /// Create a <see cref="ICronTrigger" /> with the given name and group,
        /// associated with the identified <see cref="IJobDetail" />,
        /// and with the given RecurrenceRule.
        /// </summary>
        /// <param name="name">The name of the <see cref="ITrigger" /></param>
        /// <param name="group">The group of the <see cref="ITrigger" /></param>
        /// <param name="jobName">name of the <see cref="IJobDetail" /> executed on firetime</param>
        /// <param name="jobGroup">Group of the <see cref="IJobDetail" /> executed on firetime</param>
        /// <param name="startTimeUtc">The earliest time for hte <see cref="ITrigger"/> firing. </param>
        /// <param name="recurrence">A Recurrence describing the firing pattern of <see cref="ITrigger"/></param>
        public RecurrenceTriggerImpl(string name, string group, string jobName, string jobGroup, DateTimeOffset startTimeUtc, Recurrence recurrence) 
            : this(name, group, jobName, jobGroup, startTimeUtc, null, recurrence)
        {
        }
        /// <summary>
        /// Create a <see cref="ICronTrigger" /> with the given name and group,
        /// associated with the identified <see cref="IJobDetail" />,
        /// and with the given RecurrenceRule.
        /// </summary>
        /// <param name="name">The name of the <see cref="ITrigger" /></param>
        /// <param name="group">The group of the <see cref="ITrigger" /></param>
        /// <param name="jobName">name of the <see cref="IJobDetail" /> executed on firetime</param>
        /// <param name="jobGroup">Group of the <see cref="IJobDetail" /> executed on firetime</param>
        /// <param name="endTimeUtc">The latest time for hte <see cref="ITrigger"/> firing. </param>
        /// <param name="startTimeUtc">The earliest time for hte <see cref="ITrigger"/> firing. </param>
        /// <param name="recurrenceRule">A RecurrenceRule describing the firing pattern of <see cref="ITrigger"/></param>
        public RecurrenceTriggerImpl(string name, string group, string jobName, string jobGroup, DateTimeOffset startTimeUtc, DateTimeOffset? endTimeUtc, string recurrenceRule) : 
            this(name, group, jobName, jobGroup, startTimeUtc, endTimeUtc, new Recurrence(recurrenceRule))
        {
        }
        /// <summary>
        /// Create a <see cref="ICronTrigger" /> with the given name and group,
        /// associated with the identified <see cref="IJobDetail" />,
        /// and with the given RecurrenceRule.
        /// </summary>
        /// <param name="name">The name of the <see cref="ITrigger" /></param>
        /// <param name="group">The group of the <see cref="ITrigger" /></param>
        /// <param name="jobName">name of the <see cref="IJobDetail" /> executed on firetime</param>
        /// <param name="jobGroup">Group of the <see cref="IJobDetail" /> executed on firetime</param>
        /// <param name="endTimeUtc">The latest time for hte <see cref="ITrigger"/> firing. </param>
        /// <param name="startTimeUtc">The earliest time for th-e <see cref="ITrigger"/> firing. </param>
        /// <param name="recurrence">A RecurrenceRule describing the firing pattern of <see cref="ITrigger"/></param>
        public RecurrenceTriggerImpl(string name, string group, string jobName, string jobGroup, DateTimeOffset startTimeUtc, DateTimeOffset? endTimeUtc, Recurrence recurrence) 
            : base(name, group, jobName, jobGroup)
        {
            StartTimeUtc = SystemTime.UtcNow();
            Recurrence = recurrence;
            StartTimeUtc = (startTimeUtc == DateTimeOffset.MinValue) ? SystemTime.UtcNow() : startTimeUtc;
            if (endTimeUtc.HasValue)
            {
                EndTimeUtc = endTimeUtc;
            }
        }
        
        /// <summary>
        /// Gets or sets the RecurrenceRule. When a new one is set, the <see cref="Recurrence"/> is updated accordingly.
        /// </summary>
        public string? RecurrenceRule
        {
            get => Recurrence!.ToString();
            set => Recurrence = new Recurrence(value!);
        }
        
        /// <summary>
        /// The Recurrence describing the firing of the <see cref="ITrigger"/>
        /// </summary>
        public Recurrence? Recurrence { get; set; }

        /// <summary>
        /// Sets or gets the StartTime of the <see cref="Recurrence"/>
        /// </summary>
        public override DateTimeOffset StartTimeUtc
        {
            get => Recurrence!.StartDateTime;
            set => Recurrence!.StartDateTime = value.DateTime;
        }
        /// <summary>
        /// Sets or gets the EndTime of the <see cref="Recurrence"/>
        /// </summary>
        public override DateTimeOffset? EndTimeUtc
        {
            get => Recurrence!.RecurUntil;
            set
            {
                if (value < StartTimeUtc)
                    Recurrence!.RecurUntil = value!.Value.DateTime;
                else
                    throw new ArgumentException("EndTime cannot be smaller than StartTime", nameof(EndTimeUtc));
            }
        }
        private DateTimeOffset? nextFireTimeUtc { get; set; }
        private DateTimeOffset? previousFireTimeUtc { get; set; }
        
        /// <summary>
        /// Set nextFireTime to the provided DateTime
        /// </summary>
        /// <param name="nextFireTime">The provided FireTime</param>
        public override void SetNextFireTimeUtc(DateTimeOffset? nextFireTime)
        {
            nextFireTimeUtc = nextFireTime;
        }
        
        /// <summary>
        /// Set previousFireTime to the provided DateTime
        /// </summary>
        /// <param name="previousFireTime">The provided FireTime</param>
        public override void SetPreviousFireTimeUtc(DateTimeOffset? previousFireTime)
        {
            this.previousFireTimeUtc = previousFireTime;
        }
        /// <summary>
        /// Gets previousFireTime
        /// </summary>
        /// <returns></returns>
        public override DateTimeOffset? GetPreviousFireTimeUtc()
        {
            return previousFireTimeUtc;
        }
        /// <summary>
        /// Gets nextFireTime
        /// </summary>
        /// <returns></returns>
        public override DateTimeOffset? GetNextFireTimeUtc()
        {
            return nextFireTimeUtc;
        }
        
        /// <summary>
        /// Gets Fire Time After provided DateTime, returns null if none could be found
        /// </summary>
        /// <param name="afterTime">The Time after which a time should be found</param>
        /// <returns></returns>
        public override DateTimeOffset? GetFireTimeAfter(DateTimeOffset? afterTime)
        {
            try
            {
                return Recurrence!.AllInstances().OrderBy(x => x).First(occurrence => occurrence > afterTime);
            }
            catch(InvalidOperationException)
            {
                return null;
            }
        }
        
        /// <summary>
        /// Gets the ScheduleBuilder for the Recurrence and the MisfireInstruction
        /// </summary>
        /// <returns></returns>
        public override IScheduleBuilder GetScheduleBuilder()
        {
            RecurrenceScheduleBuilder rb = RecurrenceScheduleBuilder.RecurrenceSchedule(Recurrence!);
            switch (MisfireInstruction)
            {
                case Quartz.MisfireInstruction.SmartPolicy:
                    break;
                case Quartz.MisfireInstruction.RecurrenceTrigger.DoNothing:
                    rb.WithMisfireHandlingInstructionDoNothing();
                    break;
                case Quartz.MisfireInstruction.RecurrenceTrigger.FireOnceNow:
                    rb.WithMisfireHandlingInstructionFireAndProceed();
                    break;
                case Quartz.MisfireInstruction.IgnoreMisfirePolicy:
                    rb.WithMisfireHandlingInstructionIgnoreMisfires();
                    break;
            }

            return rb;
        }

        public override DateTimeOffset? FinalFireTimeUtc { get; }
        /// <summary>
        /// Called when the <see cref="IScheduler" /> has decided to 'fire'
        /// the trigger (Execute the associated <see cref="IJob" />), in order to
        /// give the <see cref="ITrigger" /> a chance to update itself for its next
        /// triggering (if any).
        /// </summary>
        /// <param name="cal">The calendar which is used to update the <see cref="ITrigger"/></param>
        /// <seealso cref="JobExecutionException" />
        public override void Triggered(ICalendar? cal)
        {
            previousFireTimeUtc = nextFireTimeUtc;
            nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);
            
            // Make sure that NextFireTime is not included in the calendar
            while (nextFireTimeUtc.HasValue && cal != null && cal.IsTimeIncluded(nextFireTimeUtc.Value))
            {
                nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);
            }
        }
        
        /// <summary>
        /// Calculates the first FireTime based on the calendar provided.
        /// </summary>
        /// <param name="cal">The calendar with which to calculate the firstfiretime</param>
        /// <returns></returns>
        public override DateTimeOffset? ComputeFirstFireTimeUtc(ICalendar? cal)
        {
            var nextFireTime = GetFireTimeAfter(StartTimeUtc);
            
            // Make sure that NextFireTime is not included in the calendar
            while (nextFireTime.HasValue && cal != null && cal.IsTimeIncluded(nextFireTime.Value))
            {
                nextFireTime = GetFireTimeAfter(this.nextFireTimeUtc);
            }

            return nextFireTimeUtc;
        }
        
        /// <summary>
        /// Used by the <see cref="IScheduler" /> to determine whether or not
        /// it is possible for this <see cref="ITrigger" /> to fire again.
        /// </summary>
        /// <returns></returns>
        public override bool GetMayFireAgain()
        {
            return GetNextFireTimeUtc().HasValue;
        }
        /// <summary>
        /// Validates the misfire instruction.
        /// </summary>
        /// <param name="misfireInstruction">The misfire instruction</param>
        /// <returns></returns>
        protected override bool ValidateMisfireInstruction(int misfireInstruction)
        {
            if (misfireInstruction < Quartz.MisfireInstruction.IgnoreMisfirePolicy)
                return false;
            else if (misfireInstruction > Quartz.MisfireInstruction.RecurrenceTrigger.DoNothing)
                return false;
            return true;
        }
        
        /// <summary>
        /// Should not be used by the Quartz Client!
        /// Updates the <see cref="ITrigger"/> based on the MisfireInstruction set.
        /// </summary>
        /// <param name="cal"></param>
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
        
        /// <summary>
        /// Updates the <see cref="ITrigger"/> with the provided calendar and misfireThreshold
        /// </summary>
        /// <param name="cal">The calender to update with</param>
        /// <param name="misfireThreshold">The misfire threshold.</param>
        public override void UpdateWithNewCalendar(ICalendar cal, TimeSpan misfireThreshold)
        {
            nextFireTimeUtc = GetFireTimeAfter(previousFireTimeUtc);

            if (!nextFireTimeUtc.HasValue || cal == null)
                return;

            while (nextFireTimeUtc.HasValue && !cal.IsTimeIncluded(nextFireTimeUtc.Value))
            {
                nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);

                if (!nextFireTimeUtc.HasValue || nextFireTimeUtc.Value.Year > YearToGiveUpSchedulingAt)
                    return;

                if (nextFireTimeUtc.HasValue && nextFireTimeUtc.Value < DateTime.Now)
                {
                    TimeSpan diff = DateTime.Now - nextFireTimeUtc.Value;
                    if (diff >= misfireThreshold)
                    {
                        nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);
                    }
                }
            }
        }

 
    }
}