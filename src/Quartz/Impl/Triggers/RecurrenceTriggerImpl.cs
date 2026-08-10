using RRule = Quartz.Impl.Recurrence.RecurrenceRule;

namespace Quartz.Impl.Triggers;

/// <summary>
/// A concrete <see cref="ITrigger"/> that fires based on an iCalendar RFC 5545
/// recurrence rule (RRULE).
/// </summary>
/// <remarks>
/// This trigger supports complex scheduling patterns that cannot be expressed with
/// CRON expressions, such as "every 2nd Monday of the month", "every other week
/// on Monday, Wednesday, and Friday", or "the last weekday of March each year".
/// </remarks>
/// <seealso cref="IRecurrenceTrigger"/>
/// <seealso cref="RecurrenceScheduleBuilder"/>
public sealed class RecurrenceTriggerImpl : AbstractTrigger, IRecurrenceTrigger
{
    private DateTimeOffset startTime;
    private DateTimeOffset? endTime;
    private string recurrenceRuleString = "";
    internal TimeZoneInfo? triggerTimeZone;

    private volatile RRule? parsedRule;

    /// <summary>
    /// Create a <see cref="RecurrenceTriggerImpl"/> with no settings.
    /// </summary>
    public RecurrenceTriggerImpl() : base(TimeProvider.System)
    {
    }

    /// <summary>
    /// Create a <see cref="RecurrenceTriggerImpl"/> with the given name, group, and RRULE.
    /// </summary>
    public RecurrenceTriggerImpl(string name, string group, string recurrenceRule,
        TimeProvider? timeProvider = null)
        : base(name, group, timeProvider ?? TimeProvider.System)
    {
        RecurrenceRule = recurrenceRule;
    }

    /// <summary>
    /// The RFC 5545 RRULE string.
    /// </summary>
    public string RecurrenceRule
    {
        get => recurrenceRuleString;
        set
        {
            recurrenceRuleString = value ?? throw new ArgumentNullException(nameof(value));
            parsedRule = null; // Invalidate cache
        }
    }

    /// <summary>
    /// The time zone for recurrence calculations.
    /// </summary>
    public TimeZoneInfo TimeZone
    {
        get
        {
            if (triggerTimeZone == null)
            {
                triggerTimeZone = TimeZoneInfo.Local;
            }
            return triggerTimeZone;
        }
        set => triggerTimeZone = value;
    }

    /// <summary>
    /// The number of times this trigger has fired.
    /// </summary>
    public int TimesTriggered { get; set; }

    /// <inheritdoc/>
    public override DateTimeOffset StartTimeUtc
    {
        get
        {
            if (startTime == DateTimeOffset.MinValue)
            {
                startTime = TimeProvider.GetUtcNow();
            }
            return startTime;
        }
        set
        {
            if (value == DateTimeOffset.MinValue)
            {
                Throw.ArgumentException("Start time cannot be DateTimeOffset.MinValue");
            }

            DateTimeOffset? eTime = EndTimeUtc;
            if (eTime != null && eTime < value)
            {
                Throw.ArgumentException("End time cannot be before start time");
            }

            startTime = value;
        }
    }

    /// <inheritdoc/>
    public override bool HasMillisecondPrecision => false;

    /// <inheritdoc/>
    public override DateTimeOffset? EndTimeUtc
    {
        get => endTime;
        set
        {
            DateTimeOffset sTime = StartTimeUtc;
            if (value != null && sTime > value)
            {
                Throw.ArgumentException("End time cannot be before start time");
            }

            endTime = value;
        }
    }

    /// <inheritdoc/>
    protected override bool ValidateMisfireInstruction(int misfireInstruction)
    {
        if (misfireInstruction < Quartz.MisfireInstruction.IgnoreMisfirePolicy)
        {
            return false;
        }

        if (misfireInstruction > Quartz.MisfireInstruction.RecurrenceTrigger.DoNothing)
        {
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public RecurrenceTriggerMisfireInstruction MisfireInstruction => (RecurrenceTriggerMisfireInstruction) MisfireInstructionCode;

    /// <inheritdoc/>
    public override void UpdateAfterMisfire(ICalendar? calendar)
    {
        int instr = MisfireInstructionCode;

        if (instr == Quartz.MisfireInstruction.IgnoreMisfirePolicy)
        {
            return;
        }

        if (instr == Quartz.MisfireInstruction.SmartPolicy)
        {
            instr = Quartz.MisfireInstruction.RecurrenceTrigger.FireOnceNow;
        }

        if (instr == Quartz.MisfireInstruction.RecurrenceTrigger.DoNothing)
        {
            DateTimeOffset? newFireTime = GetFireTimeAfter(TimeProvider.GetUtcNow());
            while (newFireTime != null && calendar != null && !calendar.IsTimeIncluded(newFireTime.Value))
            {
                newFireTime = GetFireTimeAfter(newFireTime);

                if (newFireTime == null)
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
        else if (instr == Quartz.MisfireInstruction.RecurrenceTrigger.FireOnceNow)
        {
            NextFireTimeUtc = TimeProvider.GetUtcNow();
        }
    }

    /// <inheritdoc/>
    public override void Triggered(ICalendar? calendar)
    {
        TimesTriggered++;
        PreviousFireTimeUtc = NextFireTimeUtc;
        NextFireTimeUtc = GetFireTimeAfter(NextFireTimeUtc);

        while (NextFireTimeUtc != null && calendar != null
                                       && !calendar.IsTimeIncluded(NextFireTimeUtc.Value))
        {
            NextFireTimeUtc = GetFireTimeAfter(NextFireTimeUtc);

            if (NextFireTimeUtc == null)
            {
                break;
            }

            if (NextFireTimeUtc.Value.Year > TriggerConstants.YearToGiveUpSchedulingAt)
            {
                NextFireTimeUtc = null;
            }
        }
    }

    /// <inheritdoc/>
    public override void UpdateWithNewCalendar(ICalendar calendar, TimeSpan misfireThreshold)
    {
        NextFireTimeUtc = GetFireTimeAfter(PreviousFireTimeUtc);

        if (NextFireTimeUtc == null || calendar == null)
        {
            return;
        }

        DateTimeOffset now = TimeProvider.GetUtcNow();
        while (NextFireTimeUtc != null && !calendar.IsTimeIncluded(NextFireTimeUtc.Value))
        {
            NextFireTimeUtc = GetFireTimeAfter(NextFireTimeUtc);

            if (NextFireTimeUtc == null)
            {
                break;
            }

            if (NextFireTimeUtc.Value.Year > TriggerConstants.YearToGiveUpSchedulingAt)
            {
                NextFireTimeUtc = null;
            }

            if (NextFireTimeUtc != null && NextFireTimeUtc < now)
            {
                TimeSpan diff = now - NextFireTimeUtc.Value;
                if (diff >= misfireThreshold)
                {
                    NextFireTimeUtc = GetFireTimeAfter(NextFireTimeUtc);
                }
            }
        }
    }

    /// <inheritdoc/>
    public override DateTimeOffset? ComputeFirstFireTimeUtc(ICalendar? calendar)
    {
        // If the end time is already in the past, the trigger should never fire
        if (EndTimeUtc.HasValue && EndTimeUtc.Value < TimeProvider.GetUtcNow())
        {
            return null;
        }

        // Find the first occurrence on or after StartTimeUtc.
        // Uses skipCount: true so COUNT is enforced by TimesTriggered (which is 0 here),
        // and the sub-daily fast-forward optimizations in FindNextOccurrenceNonCount are
        // used, avoiding MaxIterations exhaustion for sparse rules like FREQ=SECONDLY;BYMONTH=12.
        RRule rule = GetParsedRule();
        NextFireTimeUtc = rule.GetNextOccurrence(StartTimeUtc, StartTimeUtc.AddSeconds(-1), TimeZone, EndTimeUtc, skipCount: true);

        if (NextFireTimeUtc == null)
        {
            return null;
        }

        while (NextFireTimeUtc != null && calendar != null
                                       && !calendar.IsTimeIncluded(NextFireTimeUtc.Value))
        {
            NextFireTimeUtc = GetFireTimeAfter(NextFireTimeUtc);

            if (NextFireTimeUtc == null)
            {
                break;
            }

            if (NextFireTimeUtc.Value.Year > TriggerConstants.YearToGiveUpSchedulingAt)
            {
                return null;
            }
        }

        return NextFireTimeUtc;
    }

    /// <inheritdoc/>
    public override DateTimeOffset? NextFireTimeUtc { get; set; }

    /// <inheritdoc/>
    public override DateTimeOffset? PreviousFireTimeUtc { get; set; }

    /// <inheritdoc/>
    public override DateTimeOffset? GetFireTimeAfter(DateTimeOffset? afterTime)
    {
        // For COUNT-based rules, check if we've already exhausted the count.
        // TimesTriggered is the single source of truth for COUNT tracking,
        // avoiding expensive walk-from-start counting in the RRULE engine.
        RRule rule = GetParsedRule();
        if (rule.Count != null && TimesTriggered >= rule.Count.Value)
        {
            return null;
        }

        return rule.GetNextOccurrence(StartTimeUtc, afterTime ?? TimeProvider.GetUtcNow(), TimeZone, EndTimeUtc, skipCount: true);
    }

    /// <inheritdoc/>
    public override DateTimeOffset? FinalFireTimeUtc
    {
        get
        {
            RRule rule = GetParsedRule();

            // For COUNT-based rules, walk to the final occurrence
            if (rule.Count != null)
            {
                return rule.GetNthOccurrence(StartTimeUtc, rule.Count.Value, TimeZone, EndTimeUtc);
            }

            if (EndTimeUtc == null && rule.Until == null)
            {
                return null;
            }

            // Find the last actual occurrence before the boundary.
            // We can't just return EndTimeUtc/UNTIL because they may not align
            // with an actual fire time (e.g., daily at 9:00 with EndTime at 8:00).
            return rule.GetLastOccurrenceBefore(StartTimeUtc, TimeZone, EndTimeUtc);
        }
    }

    /// <inheritdoc/>
    public override bool MayFireAgain => NextFireTimeUtc != null;

    /// <inheritdoc/>
    public override void Validate()
    {
        base.Validate();

        if (string.IsNullOrWhiteSpace(recurrenceRuleString))
        {
            throw new SchedulerException("RecurrenceRule must be set.");
        }

        // Validate that the RRULE string is parseable
        try
        {
            RRule.Parse(recurrenceRuleString);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            throw new SchedulerException($"Invalid RecurrenceRule: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public override IScheduleBuilder GetScheduleBuilder()
    {
        RecurrenceScheduleBuilder sb = RecurrenceScheduleBuilder.Create(recurrenceRuleString)
            .InTimeZone(TimeZone);

        RecurrenceTriggerMisfireInstruction instruction = MisfireInstruction;
        if (Enum.IsDefined(instruction))
        {
            sb.WithMisfireInstruction(instruction);
        }

        return sb;
    }

    private RRule GetParsedRule()
    {
        RRule? rule = parsedRule;
        if (rule == null)
        {
            rule = RRule.Parse(recurrenceRuleString);
            parsedRule = rule;
        }
        return rule;
    }
}
