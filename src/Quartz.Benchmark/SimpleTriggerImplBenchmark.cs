using BenchmarkDotNet.Attributes;
using Quartz.Impl.Triggers;

namespace Quartz.Benchmark;

[MemoryDiagnoser]
public class SimpleTriggerImplBenchmark
{
    private SimpleTriggerImpl? _trigger1;
    private SimpleTriggerImplLegacy? _trigger1Legacy;
    private SimpleTriggerImpl? _trigger2;
    private SimpleTriggerImplLegacy? _trigger2Legacy;
    private SimpleTriggerImpl? _trigger3;
    private SimpleTriggerImplLegacy? _trigger3Legacy;
    private SimpleTriggerImpl? _trigger4;
    private SimpleTriggerImplLegacy? _trigger4Legacy;
    private SimpleTriggerImpl? _trigger5;
    private SimpleTriggerImplLegacy? _trigger5Legacy;
    private SimpleTriggerImpl? _trigger6;
    private SimpleTriggerImplLegacy? _trigger6Legacy;
    private DateTimeOffset _today;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _trigger1 = new SimpleTriggerImpl("1",
            new DateTimeOffset(2, 1, 1, 0, 0, 0, 0, TimeSpan.Zero),
            null,
            SimpleTriggerImpl.RepeatIndefinitely,
            TimeSpan.FromTicks(1000));
        _trigger1.MisfireInstructionCode = MisfireInstruction.SmartPolicy;
        _trigger1.NextFireTimeUtc = DateTimeOffset.UtcNow;

        _trigger1Legacy = new SimpleTriggerImplLegacy("1",
            new DateTimeOffset(2, 1, 1, 0, 0, 0, 0, TimeSpan.Zero),
            null,
            SimpleTriggerImpl.RepeatIndefinitely,
            TimeSpan.FromTicks(1000));
        _trigger1Legacy.MisfireInstructionCode = MisfireInstruction.SmartPolicy;
        _trigger1Legacy.NextFireTimeUtc = DateTimeOffset.UtcNow;

        _trigger2 = new SimpleTriggerImpl("1",
            DateTimeOffset.MinValue,
            DateTimeOffset.MaxValue,
            SimpleTriggerImpl.RepeatIndefinitely,
            TimeSpan.FromTicks(1000));
        _trigger2.MisfireInstructionCode = MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount;

        _trigger2Legacy = new SimpleTriggerImplLegacy("1",
            DateTimeOffset.MinValue,
            DateTimeOffset.MaxValue,
            SimpleTriggerImpl.RepeatIndefinitely,
            TimeSpan.FromTicks(1000));
        _trigger2Legacy.MisfireInstructionCode = MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount;

        _trigger3 = new SimpleTriggerImpl("1",
            DateTimeOffset.MinValue,
            DateTimeOffset.MaxValue,
            0,
            TimeSpan.FromTicks(1000));
        _trigger3.MisfireInstructionCode = MisfireInstruction.SmartPolicy;

        _trigger3Legacy = new SimpleTriggerImplLegacy("1",
            DateTimeOffset.MinValue,
            DateTimeOffset.MaxValue,
            0,
            TimeSpan.FromTicks(1000));
        _trigger3Legacy.MisfireInstructionCode = MisfireInstruction.SmartPolicy;

        _trigger4 = new SimpleTriggerImpl("1",
            DateTimeOffset.MinValue,
            DateTimeOffset.MinValue,
            SimpleTriggerImpl.RepeatIndefinitely,
            TimeSpan.FromTicks(1000));
        _trigger4.MisfireInstructionCode = MisfireInstruction.SimpleTrigger.FireNow;
        _trigger4.NextFireTimeUtc = DateTimeOffset.UtcNow;

        _trigger4Legacy = new SimpleTriggerImplLegacy("1",
            DateTimeOffset.MinValue,
            DateTimeOffset.MinValue,
            SimpleTriggerImpl.RepeatIndefinitely,
            TimeSpan.FromTicks(1000));
        _trigger4Legacy.MisfireInstructionCode = MisfireInstruction.SimpleTrigger.FireNow;
        _trigger4Legacy.NextFireTimeUtc = DateTimeOffset.UtcNow;

        _trigger5 = new SimpleTriggerImpl("1",
            DateTimeOffset.MinValue,
            DateTimeOffset.MaxValue,
            5,
            TimeSpan.FromTicks(1000));
        _trigger5.MisfireInstructionCode = MisfireInstruction.SmartPolicy;

        _trigger5Legacy = new SimpleTriggerImplLegacy("1",
            DateTimeOffset.MinValue,
            DateTimeOffset.MaxValue,
            5,
            TimeSpan.FromTicks(1000));
        _trigger5Legacy.MisfireInstructionCode = MisfireInstruction.SmartPolicy;

        _trigger6 = new SimpleTriggerImpl("1",
            DateTimeOffset.MinValue,
            DateTimeOffset.MaxValue,
            int.MaxValue,
            TimeSpan.FromDays(1));
        _trigger6.MisfireInstructionCode = MisfireInstruction.SimpleTrigger.FireNow;
        _trigger6.NextFireTimeUtc = DateTimeOffset.UtcNow;

        _trigger6Legacy = new SimpleTriggerImplLegacy("1",
            DateTimeOffset.MinValue,
            DateTimeOffset.MaxValue,
            int.MaxValue,
            TimeSpan.FromDays(1));
        _trigger6Legacy.MisfireInstructionCode = MisfireInstruction.SimpleTrigger.FireNow;
        _trigger6Legacy.NextFireTimeUtc = DateTimeOffset.UtcNow;

        _today = new DateTimeOffset(DateTime.Today.ToUniversalTime(), TimeSpan.Zero);
    }

    [Benchmark]
    public DateTimeOffset? GetFireTimeAfter_RepeatCountIsRepeatIndefinitely_AfterTimeUtcIsNotNullAndAfterStartTimeUtc_EndTimeUtcIsNull_New()
    {
        return _trigger1!.GetFireTimeAfter(_today);
    }

    [Benchmark]
    public DateTimeOffset? GetFireTimeAfter_RepeatCountIsRepeatIndefinitely_AfterTimeUtcIsNotNullAndAfterStartTimeUtc_EndTimeUtcIsNull_Legacy()
    {
        return _trigger1Legacy!.GetFireTimeAfter(_today);
    }

    [Benchmark]
    public DateTimeOffset? GetFireTimeAfter_RepeatCountIsGreaterThanZeroAndLessThanNumberOfTimesExecuted_AfterTimeUtcIsNotNullAndAfterStartTimeUtc_EndTimeUtcIsMaxValue_New()
    {
        return _trigger5!.GetFireTimeAfter(_today);
    }

    [Benchmark]
    public DateTimeOffset? GetFireTimeAfter_RepeatCountIsGreaterThanZeroAndLessThanNumberOfTimesExecuted_AfterTimeUtcIsNotNullAndAfterStartTimeUtc_EndTimeUtcIsMaxValue_Legacy()
    {
        return _trigger5Legacy!.GetFireTimeAfter(_today);
    }

    [Benchmark]
    public DateTimeOffset? GetFireTimeAfter_RepeatCountIsRepeatIndefinitely_AfterTimeUtcIsNull_EndTimeUtcIsNull_New()
    {
        return _trigger1!.GetFireTimeAfter(null);
    }

    [Benchmark]
    public DateTimeOffset? GetFireTimeAfter_RepeatCountIsRepeatIndefinitely_AfterTimeUtcIsNull_EndTimeUtcIsNull_Legacy()
    {
        return _trigger1Legacy!.GetFireTimeAfter(null);
    }

    [Benchmark]
    public DateTimeOffset? GetFireTimeAfter_RepeatCountIsRepeatIndefinitely_AfterTimeUtcIsNotNullAndAfterStartTimeUtc_EndTimeUtcIsMaxValue_New()
    {
        return _trigger2!.GetFireTimeAfter(_today);
    }

    [Benchmark]
    public DateTimeOffset? GetFireTimeAfter_RepeatCountIsRepeatIndefinitely_AfterTimeUtcIsNotNullAndAfterStartTimeUtc_EndTimeUtcIsMaxValue_Legacy()
    {
        return _trigger2Legacy!.GetFireTimeAfter(_today);
    }

    [Benchmark]
    public DateTimeOffset? GetFireTimeAfter_RepeatCountIsRepeatIndefinitely_AfterTimeUtcIsNotNullAndAfterStartTimeUtc_EndTimeUtcIsMinValue_New()
    {
        return _trigger4!.GetFireTimeAfter(_today);
    }

    [Benchmark]
    public DateTimeOffset? GetFireTimeAfter_RepeatCountIsRepeatIndefinitely_AfterTimeUtcIsNotNullAndAfterStartTimeUtc_EndTimeUtcIsMinValue_Legacy()
    {
        return _trigger4Legacy!.GetFireTimeAfter(_today);
    }

    [Benchmark]
    public DateTimeOffset? GetFireTimeAfter_RepeatCountIsZero_AfterTimeUtcIsNotNullAndAfterStartTimeUtc_New()
    {
        return _trigger3!.GetFireTimeAfter(_today);
    }

    [Benchmark]
    public DateTimeOffset? GetFireTimeAfter_RepeatCountIsZero_AfterTimeUtcIsNotNullAndAfterStartTimeUtc_Legacy()
    {
        return _trigger3Legacy!.GetFireTimeAfter(_today);
    }

    [Benchmark]
    public DateTimeOffset? GetFireTimeAfter_RepeatCountIsRepeatIndefinitely_AfterTimeUtcIsNull_EndTimeUtcIsMaxValue_New()
    {
        return _trigger2!.GetFireTimeAfter(null);
    }

    [Benchmark]
    public DateTimeOffset? GetFireTimeAfter_RepeatCountIsRepeatIndefinitely_AfterTimeUtcIsNull_EndTimeUtcIsMaxValue_Legacy()
    {
        return _trigger2Legacy!.GetFireTimeAfter(null);
    }

    [Benchmark]
    public DateTimeOffset? GetFireTimeAfter_RepeatCountIsRepeatIndefinitely_AfterTimeUtcIsNotNullAndBeforeStartTimeUtc_EndTimeUtcIsNull_New()
    {
        return _trigger1!.GetFireTimeAfter(DateTimeOffset.MinValue);
    }

    [Benchmark]
    public DateTimeOffset? GetFireTimeAfter_RepeatCountIsRepeatIndefinitely_AfterTimeUtcIsNotNullAndBeforeStartTimeUtc_EndTimeUtcIsNull_Legacy()
    {
        return _trigger1Legacy!.GetFireTimeAfter(DateTimeOffset.MinValue);
    }

    [Benchmark]
    public void Triggered_RepeatCountIsRepeatIndefinitely_EndTimeUtcIsNull_CalendarIsNull_New()
    {
        const ICalendar? calendar = null;

        _trigger1!.Triggered(calendar);
    }

    [Benchmark]
    public void Triggered_RepeatCountIsRepeatIndefinitely_EndTimeUtcIsNull_CalendarIsNull_Legacy()
    {
        const ICalendar? calendar = null;

        _trigger1Legacy!.Triggered(calendar);
    }

    [Benchmark]
    public void UpdateAfterMisfire_SmartPolicy_RepeatCountIsZero_New()
    {
        _trigger3!.UpdateAfterMisfire(null);
    }

    [Benchmark]
    public void UpdateAfterMisfire_SmartPolicy_RepeatCountIsZero_Legacy()
    {
        _trigger3Legacy!.UpdateAfterMisfire(null);
    }

    [Benchmark]
    public void UpdateAfterMisfire_SmartPolicy_RepeatCountIsRepeatIndefinitely_New()
    {
        _trigger1!.UpdateAfterMisfire(null);
    }

    [Benchmark]
    public void UpdateAfterMisfire_SmartPolicy_RepeatCountIsRepeatIndefinitely_Legacy()
    {
        _trigger1Legacy!.UpdateAfterMisfire(null);
    }

    [Benchmark]
    public void UpdateAfterMisfire_SmartPolicy_RepeatCountIsGreaterThanZero_New()
    {
        _trigger5!.UpdateAfterMisfire(null);
    }

    [Benchmark]
    public void UpdateAfterMisfire_SmartPolicy_RepeatCountIsGreaterThanZero_Legacy()
    {
        _trigger5Legacy!.UpdateAfterMisfire(null);
    }

    [Benchmark]
    public void UpdateAfterMisfire_FireNow_RepeatCountIsRepeatIndefinitely_EndTimeUtcIsMinValue_New()
    {
        _trigger4!.UpdateAfterMisfire(null);
        // This is only necessary for the legacy implementation, but we do this here to make
        // sure we benchmark the same
        _trigger4!.NextFireTimeUtc = _today;
    }

    [Benchmark]
    public void UpdateAfterMisfire_FireNow_RepeatCountIsRepeatIndefinitely_EndTimeUtcIsMinValue_Legacy()
    {
        _trigger4Legacy!.UpdateAfterMisfire(null);
        // UpdateAfterMisfire will set next fire time to null, so make sure we don't crash on
        // the next invocation
        _trigger4Legacy!.NextFireTimeUtc = _today;
    }

    [Benchmark]
    public void UpdateAfterMisfire_FireNow_RepeatCountIsMaxValue_EndTimeUtcIsMaxValue_New()
    {
        _trigger6!.UpdateAfterMisfire(null);
    }

    [Benchmark]
    public void UpdateAfterMisfire_FireNow_RepeatCountIsMaxValue_EndTimeUtcIsMaxValue_Legacy()
    {
        _trigger6Legacy!.UpdateAfterMisfire(null);
    }

    [Benchmark]
    public void UpdateAfterMisfire_RescheduleNextWithExistingCount_RepeatCountIsRepeatIndefinitely_EndTimeUtcIsMaxValue_New()
    {
        _trigger2!.UpdateAfterMisfire(null);
    }

    [Benchmark]
    public void UpdateAfterMisfire_RescheduleNextWithExistingCount_RepeatCountIsRepeatIndefinitely_EndTimeUtcIsMaxValue_Legacy()
    {
        _trigger2Legacy!.UpdateAfterMisfire(null);
    }

    /// <summary>
    /// A concrete <see cref="ITrigger" /> that is used to fire a <see cref="IJobDetail" />
    /// at a given moment in time, and optionally repeated at a specified interval.
    /// </summary>
    /// <seealso cref="ITrigger" />
    /// <seealso cref="ICronTrigger" />
    /// <author>James House</author>
    /// <author>Contributions by Lieven Govaerts of Ebitec Nv, Belgium.</author>
    /// <author>Marko Lahma (.NET)</author>
    [Serializable]
    public class SimpleTriggerImplLegacy : TriggerBase, ISimpleTrigger
    {
        /// <summary>
        /// Used to indicate the 'repeat count' of the trigger is indefinite. Or in
        /// other words, the trigger should repeat continually until the trigger's
        /// ending timestamp.
        /// </summary>
        public const int RepeatIndefinitely = -1;

        private int repeatCount;
        private TimeSpan repeatInterval = TimeSpan.Zero;
        private int timesTriggered;

        /// <summary>
        /// Create a <see cref="SimpleTriggerImpl" /> with no settings.
        /// </summary>
        public SimpleTriggerImplLegacy() : base(TimeProvider.System)
        {
        }

        /// <summary>
        /// Create a <see cref="SimpleTriggerImpl" /> that will occur immediately, and
        /// not repeat.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
        public SimpleTriggerImplLegacy(string name) : this(name, SchedulerConstants.DefaultGroup)
        {
        }

        /// <summary>
        /// Create a <see cref="SimpleTriggerImpl" /> that will occur immediately, and
        /// not repeat.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="group"/> are <see langword="null"/>.</exception>
        public SimpleTriggerImplLegacy(string name, string group)
            : this(name, group, TimeProvider.System.GetUtcNow(), null, 0, TimeSpan.Zero)
        {
        }

        /// <summary>
        /// Create a <see cref="SimpleTriggerImpl" /> that will occur immediately, and
        /// repeat at the given interval the given number of times.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
        public SimpleTriggerImplLegacy(string name, int repeatCount, TimeSpan repeatInterval)
            : this(name, SchedulerConstants.DefaultGroup, repeatCount, repeatInterval)
        {
        }

        /// <summary>
        /// Create a <see cref="SimpleTriggerImpl" /> that will occur immediately, and
        /// repeat at the given interval the given number of times.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="group"/> are <see langword="null"/>.</exception>
        public SimpleTriggerImplLegacy(string name, string group, int repeatCount, TimeSpan repeatInterval)
            : this(name, group, TimeProvider.System.GetUtcNow(), null, repeatCount, repeatInterval)
        {
        }

        /// <summary>
        /// Create a <see cref="SimpleTriggerImpl" /> that will occur at the given time,
        /// and not repeat.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
        public SimpleTriggerImplLegacy(string name, DateTimeOffset startTimeUtc)
            : this(name, SchedulerConstants.DefaultGroup, startTimeUtc)
        {
        }

        /// <summary>
        /// Create a <see cref="SimpleTriggerImpl" /> that will occur at the given time,
        /// and not repeat.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="group"/> are <see langword="null"/>.</exception>
        public SimpleTriggerImplLegacy(string name, string group, DateTimeOffset startTimeUtc)
            : this(name, group, startTimeUtc, null, 0, TimeSpan.Zero)
        {
        }

        /// <summary>
        /// Create a <see cref="SimpleTriggerImpl" /> that will occur at the given time,
        /// and repeat at the given interval the given number of times, or until
        /// the given end time.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="startTimeUtc">A UTC <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" /> to fire.</param>
        /// <param name="endTimeUtc">A UTC <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" />
        /// to quit repeat firing.</param>
        /// <param name="repeatCount">The number of times for the <see cref="ITrigger" /> to repeat
        /// firing, use <see cref="RepeatIndefinitely "/> for unlimited times.</param>
        /// <param name="repeatInterval">The time span to pause between the repeat firing.</param>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
        public SimpleTriggerImplLegacy(string name, DateTimeOffset startTimeUtc,
            DateTimeOffset? endTimeUtc, int repeatCount, TimeSpan repeatInterval)
            : this(name, SchedulerConstants.DefaultGroup, startTimeUtc, endTimeUtc, repeatCount, repeatInterval)
        {
        }

        /// <summary>
        /// Create a <see cref="SimpleTriggerImpl" /> that will occur at the given time,
        /// and repeat at the given interval the given number of times, or until
        /// the given end time.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="group">The group.</param>
        /// <param name="startTimeUtc">A UTC <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" /> to fire.</param>
        /// <param name="endTimeUtc">A UTC <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" />
        /// to quit repeat firing.</param>
        /// <param name="repeatCount">The number of times for the <see cref="ITrigger" /> to repeat
        /// firing, use <see cref="RepeatIndefinitely "/> for unlimited times.</param>
        /// <param name="repeatInterval">The time span to pause between the repeat firing.</param>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="group"/> are <see langword="null"/>.</exception>
        public SimpleTriggerImplLegacy(
            string name,
            string group,
            DateTimeOffset startTimeUtc,
            DateTimeOffset? endTimeUtc,
            int repeatCount,
            TimeSpan repeatInterval)
            : base(name, group, TimeProvider.System)
        {
            StartTimeUtc = startTimeUtc;
            EndTimeUtc = endTimeUtc;
            RepeatCount = repeatCount;
            RepeatInterval = repeatInterval;
        }

        /// <summary>
        /// Create a <see cref="SimpleTriggerImpl" /> that will occur at the given time,
        /// fire the identified <see cref="IJob" /> and repeat at the given
        /// interval the given number of times, or until the given end time.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="group">The group.</param>
        /// <param name="jobName">Name of the job.</param>
        /// <param name="jobGroup">The job group.</param>
        /// <param name="startTimeUtc">A <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" />
        /// to fire.</param>
        /// <param name="endTimeUtc">A <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" />
        /// to quit repeat firing.</param>
        /// <param name="repeatCount">The number of times for the <see cref="ITrigger" /> to repeat
        /// firing, use RepeatIndefinitely for unlimited times.</param>
        /// <param name="repeatInterval">The time span to pause between the repeat firing.</param>
        /// <exception cref="ArgumentNullException"><paramref name="name"/>, <paramref name="group"/>, <paramref name="jobName"/> or <paramref name="jobGroup"/> are <see langword="null"/>.</exception>
        public SimpleTriggerImplLegacy(string name, string group, string jobName, string jobGroup, DateTimeOffset startTimeUtc,
            DateTimeOffset? endTimeUtc,
            int repeatCount, TimeSpan repeatInterval)
            : base(name, group, jobName, jobGroup, TimeProvider.System)
        {
            StartTimeUtc = startTimeUtc;
            EndTimeUtc = endTimeUtc;
            RepeatCount = repeatCount;
            RepeatInterval = repeatInterval;
        }

        /// <summary>
        /// Get or set the number of times the <see cref="SimpleTriggerImpl" /> should
        /// repeat, after which it will be automatically deleted.
        /// </summary>
        /// <seealso cref="RepeatIndefinitely" />
        public int RepeatCount
        {
            get => repeatCount;

            set
            {
                if (value < 0 && value != RepeatIndefinitely)
                {
                    throw new ArgumentException("Repeat count must be >= 0, use the constant RepeatIndefinitely for infinite.");
                }

                repeatCount = value;
            }
        }

        /// <summary>
        /// Get or set the time interval at which the <see cref="ISimpleTrigger" /> should repeat.
        /// </summary>
        public TimeSpan RepeatInterval
        {
            get => repeatInterval;

            set
            {
                if (value < TimeSpan.Zero)
                {
                    throw new ArgumentException("Repeat interval must be >= 0");
                }

                repeatInterval = value;
            }
        }

        /// <summary>
        /// Get or set the number of times the <see cref="ISimpleTrigger" /> has already
        /// fired.
        /// </summary>
        public virtual int TimesTriggered
        {
            get => timesTriggered;
            set => timesTriggered = value;
        }

        /// <inheritdoc />
        public SimpleTriggerMisfireInstruction MisfireInstruction => (SimpleTriggerMisfireInstruction) MisfireInstructionCode;

        public override IScheduleBuilder GetScheduleBuilder()
        {
            SimpleScheduleBuilder sb = SimpleScheduleBuilder.Create()
                .WithInterval(RepeatInterval)
                .WithRepeatCount(RepeatCount);

            switch (MisfireInstructionCode)
            {
                case Quartz.MisfireInstruction.SimpleTrigger.FireNow:
                    sb.WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow);
                    break;
                case Quartz.MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount:
                    sb.WithMisfireInstruction(SimpleTriggerMisfireInstruction.NextWithExistingCount);
                    break;
                case Quartz.MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount:
                    sb.WithMisfireInstruction(SimpleTriggerMisfireInstruction.NextWithRemainingCount);
                    break;
                case Quartz.MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount:
                    sb.WithMisfireInstruction(SimpleTriggerMisfireInstruction.NowWithExistingCount);
                    break;
                case Quartz.MisfireInstruction.SimpleTrigger.RescheduleNowWithRemainingRepeatCount:
                    sb.WithMisfireInstruction(SimpleTriggerMisfireInstruction.NowWithRemainingCount);
                    break;
                case Quartz.MisfireInstruction.IgnoreMisfirePolicy:
                    sb.WithMisfireInstruction(SimpleTriggerMisfireInstruction.IgnoreMisfires);
                    break;
            }

            return sb;
        }

        /// <summary>
        /// Returns the final UTC time at which the <see cref="ISimpleTrigger" /> will
        /// fire, if repeatCount is RepeatIndefinitely, null will be returned.
        /// <para>
        /// Note that the return time may be in the past.
        /// </para>
        /// </summary>
        public override DateTimeOffset? FinalFireTimeUtc
        {
            get
            {
                if (repeatCount == 0)
                {
                    return StartTimeUtc;
                }
                if (repeatCount == RepeatIndefinitely && !EndTimeUtc.HasValue)
                {
                    return null;
                }
                if (repeatCount == RepeatIndefinitely)
                {
                    return GetFireTimeBefore(EndTimeUtc);
                }

                DateTimeOffset lastTrigger = StartTimeUtc.AddTicks(repeatCount * repeatInterval.Ticks);

                if (!EndTimeUtc.HasValue || lastTrigger < EndTimeUtc.Value)
                {
                    return lastTrigger;
                }
                return GetFireTimeBefore(EndTimeUtc);
            }
        }

        /// <summary>
        /// Tells whether this Trigger instance can handle events
        /// in millisecond precision.
        /// </summary>
        /// <value></value>
        protected override bool HasMillisecondPrecision => true;

        /// <summary>
        /// Validates the misfire instruction.
        /// </summary>
        /// <param name="misfireInstruction">The misfire instruction.</param>
        /// <returns></returns>
        protected override bool ValidateMisfireInstruction(int misfireInstruction)
        {
            if (misfireInstruction < Quartz.MisfireInstruction.IgnoreMisfirePolicy)
            {
                return false;
            }

            if (misfireInstruction > Quartz.MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount)
            {
                return false;
            }

            return true;

        }

        /// <summary>
        /// Updates the <see cref="ISimpleTrigger" />'s state based on the
        /// MisfireInstruction value that was selected when the <see cref="ISimpleTrigger" />
        /// was created.
        /// </summary>
        /// <remarks>
        /// If MisfireSmartPolicyEnabled is set to true,
        /// then the following scheme will be used: <br />
        /// <ul>
        /// <li>If the Repeat Count is 0, then the instruction will
        /// be interpreted as <see cref="MisfireInstruction.SimpleTrigger.FireNow" />.</li>
        /// <li>If the Repeat Count is <see cref="RepeatIndefinitely" />, then
        /// the instruction will be interpreted as <see cref="MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount" />.
        /// <b>WARNING:</b> using MisfirePolicy.SimpleTrigger.RescheduleNowWithRemainingRepeatCount
        /// with a trigger that has a non-null end-time may cause the trigger to
        /// never fire again if the end-time arrived during the misfire time span.
        /// </li>
        /// <li>If the Repeat Count is > 0, then the instruction
        /// will be interpreted as <see cref="MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount" />.
        /// </li>
        /// </ul>
        /// </remarks>
        public override void UpdateAfterMisfire(ICalendar? calendar)
        {
            int instr = MisfireInstructionCode;
            if (instr == Quartz.MisfireInstruction.SmartPolicy)
            {
                if (RepeatCount == 0)
                {
                    instr = Quartz.MisfireInstruction.SimpleTrigger.FireNow;
                }
                else if (RepeatCount == RepeatIndefinitely)
                {
                    instr = Quartz.MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount;

                }
                else
                {
                    instr = Quartz.MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount;
                }
            }
            else if (instr == Quartz.MisfireInstruction.SimpleTrigger.FireNow && RepeatCount != 0)
            {
                instr = Quartz.MisfireInstruction.SimpleTrigger.RescheduleNowWithRemainingRepeatCount;
            }

            if (instr == Quartz.MisfireInstruction.SimpleTrigger.FireNow)
            {
                NextFireTimeUtc = TimeProvider.System.GetUtcNow();
            }
            else if (instr == Quartz.MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount)
            {
                DateTimeOffset? newFireTime = GetFireTimeAfter(TimeProvider.System.GetUtcNow());

                while (newFireTime.HasValue && calendar is not null && !calendar.IsTimeIncluded(newFireTime.Value))
                {
                    newFireTime = GetFireTimeAfter(newFireTime);

                    if (!newFireTime.HasValue)
                    {
                        break;
                    }

                    //avoid infinite loop
                    if (newFireTime.Value.Year > TriggerConstants.YearToGiveUpSchedulingAt)
                    {
                        newFireTime = null;
                    }
                }
                NextFireTimeUtc = newFireTime;
            }
            else if (instr == Quartz.MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount)
            {
                DateTimeOffset? newFireTime = GetFireTimeAfter(TimeProvider.System.GetUtcNow());

                while (newFireTime.HasValue && calendar is not null && !calendar.IsTimeIncluded(newFireTime.Value))
                {
                    newFireTime = GetFireTimeAfter(newFireTime);

                    if (!newFireTime.HasValue)
                    {
                        break;
                    }

                    //avoid infinite loop
                    if (newFireTime.Value.Year > TriggerConstants.YearToGiveUpSchedulingAt)
                    if (newFireTime.Value.Year > TriggerConstants.YearToGiveUpSchedulingAt)
                    {
                        newFireTime = null;
                    }
                }

                if (newFireTime.HasValue)
                {
                    int timesMissed = ComputeNumberOfTimesFiredBetween(NextFireTimeUtc!.Value, newFireTime!.Value);
                    TimesTriggered = TimesTriggered + timesMissed;
                }

                NextFireTimeUtc = newFireTime;
            }
            else if (instr == Quartz.MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount)
            {
                DateTimeOffset newFireTime = TimeProvider.System.GetUtcNow();
                if (repeatCount != 0 && repeatCount != RepeatIndefinitely)
                {
                    RepeatCount = RepeatCount - TimesTriggered;
                    TimesTriggered = 0;
                }

                if (EndTimeUtc.HasValue && EndTimeUtc.Value < newFireTime)
                {
                    NextFireTimeUtc = null; // We are past the end time
                }
                else
                {
                    StartTimeUtc = newFireTime;
                    NextFireTimeUtc = newFireTime;
                }
            }
            else if (instr == Quartz.MisfireInstruction.SimpleTrigger.RescheduleNowWithRemainingRepeatCount)
            {
                DateTimeOffset newFireTime = TimeProvider.System.GetUtcNow();
                int timesMissed = ComputeNumberOfTimesFiredBetween(NextFireTimeUtc!.Value, newFireTime);

                if (repeatCount != 0 && repeatCount != RepeatIndefinitely)
                {
                    int remainingCount = RepeatCount - (TimesTriggered + timesMissed);
                    if (remainingCount <= 0)
                    {
                        remainingCount = 0;
                    }
                    RepeatCount = remainingCount;
                    TimesTriggered = 0;
                }


                if (EndTimeUtc.HasValue && EndTimeUtc.Value < newFireTime)
                {
                    NextFireTimeUtc = null; // We are past the end time
                }
                else
                {
                    StartTimeUtc = newFireTime;
                    NextFireTimeUtc = newFireTime;
                }
            }
        }

        /// <summary>
        /// Called when the <see cref="IScheduler" /> has decided to 'fire'
        /// the trigger (Execute the associated <see cref="IJob" />), in order to
        /// give the <see cref="ITrigger" /> a chance to update itself for its next
        /// triggering (if any).
        /// </summary>
        /// <seealso cref="JobExecutionException" />
        public override void Triggered(ICalendar? calendar)
        {
            timesTriggered++;
            PreviousFireTimeUtc = NextFireTimeUtc;
            NextFireTimeUtc = GetFireTimeAfter(NextFireTimeUtc);

            while (NextFireTimeUtc.HasValue && calendar is not null && !calendar.IsTimeIncluded(NextFireTimeUtc.Value))
            {
                NextFireTimeUtc = GetFireTimeAfter(NextFireTimeUtc);

                if (!NextFireTimeUtc.HasValue)
                {
                    break;
                }

                //avoid infinite loop
                if (NextFireTimeUtc.Value.Year > TriggerConstants.YearToGiveUpSchedulingAt)
                {
                    NextFireTimeUtc = null;
                }
            }
        }


        /// <summary>
        /// Updates the instance with new calendar.
        /// </summary>
        /// <param name="calendar">The calendar.</param>
        /// <param name="misfireThreshold">The misfire threshold.</param>
        public override void UpdateWithNewCalendar(ICalendar calendar, TimeSpan misfireThreshold)
        {
            NextFireTimeUtc = GetFireTimeAfter(PreviousFireTimeUtc);

            if (NextFireTimeUtc is null || calendar is null)
            {
                return;
            }

            DateTimeOffset now = TimeProvider.System.GetUtcNow();
            while (NextFireTimeUtc.HasValue && !calendar.IsTimeIncluded(NextFireTimeUtc.Value))
            {
                NextFireTimeUtc = GetFireTimeAfter(NextFireTimeUtc);

                if (!NextFireTimeUtc.HasValue)
                {
                    break;
                }

                //avoid infinite loop
                if (NextFireTimeUtc.Value.Year > TriggerConstants.YearToGiveUpSchedulingAt)
                {
                    NextFireTimeUtc = null;
                }

                if (NextFireTimeUtc is not null && NextFireTimeUtc.Value < now)
                {
                    TimeSpan diff = now - NextFireTimeUtc.Value;
                    if (diff >= misfireThreshold)
                    {
                        NextFireTimeUtc = GetFireTimeAfter(NextFireTimeUtc);
                    }
                }
            }
        }

        /// <summary>
        /// Called by the scheduler at the time a <see cref="ITrigger" /> is first
        /// added to the scheduler, in order to have the <see cref="ITrigger" />
        /// compute its first fire time, based on any associated calendar.
        /// <para>
        /// After this method has been called, <see cref="NextFireTimeUtc" />
        /// should return a valid answer.
        /// </para>
        /// </summary>
        /// <returns>
        /// The first time at which the <see cref="ITrigger" /> will be fired
        /// by the scheduler, which is also the same value <see cref="NextFireTimeUtc" />
        /// will return (until after the first firing of the <see cref="ITrigger" />).
        /// </returns>
        public override DateTimeOffset? ComputeFirstFireTimeUtc(ICalendar? calendar)
        {
            NextFireTimeUtc = StartTimeUtc;

            while (calendar is not null && !calendar.IsTimeIncluded(NextFireTimeUtc.Value))
            {
                NextFireTimeUtc = GetFireTimeAfter(NextFireTimeUtc);

                if (!NextFireTimeUtc.HasValue)
                {
                    break;
                }

                //avoid infinite loop
                if (NextFireTimeUtc.Value.Year > TriggerConstants.YearToGiveUpSchedulingAt)
                {
                    return null;
                }
            }

            return NextFireTimeUtc;
        }


        /// <summary>
        /// Returns the next time at which the <see cref="ISimpleTrigger" /> will
        /// fire. If the trigger will not fire again, <see langword="null" /> will be
        /// returned. The value returned is not guaranteed to be valid until after
        /// the <see cref="ITrigger" /> has been added to the scheduler.
        /// </summary>
        public override DateTimeOffset? NextFireTimeUtc { get; set; }

        /// <summary>
        /// Returns the previous time at which the <see cref="ISimpleTrigger" /> fired.
        /// If the trigger has not yet fired, <see langword="null" /> will be
        /// returned.
        /// </summary>
        public override DateTimeOffset? PreviousFireTimeUtc { get; set; }

        /// <summary>
        /// Returns the next UTC time at which the <see cref="ISimpleTrigger" /> will
        /// fire, after the given UTC time. If the trigger will not fire after the given
        /// time, <see langword="null" /> will be returned.
        /// </summary>
        public override DateTimeOffset? GetFireTimeAfter(DateTimeOffset? afterTimeUtc)
        {
            if (timesTriggered > repeatCount && repeatCount != RepeatIndefinitely)
            {
                return null;
            }

            if (!afterTimeUtc.HasValue)
            {
                afterTimeUtc = TimeProvider.System.GetUtcNow();
            }

            if (repeatCount == 0 && afterTimeUtc.Value.CompareTo(StartTimeUtc) >= 0)
            {
                return null;
            }

            DateTimeOffset startMillis = StartTimeUtc;
            DateTimeOffset afterMillis = afterTimeUtc.Value;
            DateTimeOffset endMillis = EndTimeUtc ?? DateTimeOffset.MaxValue;


            if (endMillis <= afterMillis)
            {
                return null;
            }

            if (afterMillis < startMillis)
            {
                return startMillis;
            }

            long numberOfTimesExecuted = ((afterMillis - startMillis).Ticks / repeatInterval.Ticks) + 1;

            if (numberOfTimesExecuted > repeatCount &&
                repeatCount != RepeatIndefinitely)
            {
                return null;
            }

            DateTimeOffset time = startMillis.AddTicks(numberOfTimesExecuted * repeatInterval.Ticks);

            if (endMillis <= time)
            {
                return null;
            }


            return time;
        }

        /// <summary>
        /// Returns the last UTC time at which the <see cref="ISimpleTrigger" /> will
        /// fire, before the given time. If the trigger will not fire before the
        /// given time, <see langword="null" /> will be returned.
        /// </summary>
        public virtual DateTimeOffset? GetFireTimeBefore(DateTimeOffset? endUtc)
        {
            if (endUtc is not null && endUtc.Value < StartTimeUtc)
            {
                return null;
            }

            int numFires = ComputeNumberOfTimesFiredBetween(StartTimeUtc, endUtc!.Value);
            return StartTimeUtc.AddTicks(numFires * repeatInterval.Ticks);
        }

        /// <summary>
        /// Computes the number of times fired between the two UTC date times.
        /// </summary>
        /// <param name="startTimeUtc">The UTC start date and time.</param>
        /// <param name="endTimeUtc">The UTC end date and time.</param>
        /// <returns></returns>
        public virtual int ComputeNumberOfTimesFiredBetween(DateTimeOffset startTimeUtc, DateTimeOffset endTimeUtc)
        {
            long time = (endTimeUtc - startTimeUtc).Ticks;
            return (int) (time / repeatInterval.Ticks);
        }

        /// <summary>
        /// Determines whether or not the <see cref="ISimpleTrigger" /> will occur
        /// again.
        /// </summary>
        public override bool MayFireAgain => NextFireTimeUtc.HasValue;

        /// <summary>
        /// Validates whether the properties of the <see cref="IJobDetail" /> are
        /// valid for submission into a <see cref="IScheduler" />.
        /// </summary>
        public override void Validate()
        {
            base.Validate();

            if (repeatCount != 0 && repeatInterval.Ticks < 1)
            {
                throw new SchedulerException("Repeat Interval cannot be zero.");
            }
        }
    }
}