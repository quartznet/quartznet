using Quartz.Impl.Recurrence;
using Quartz.Util;

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
    private static readonly int YearToGiveupSchedulingAt = DateTime.Now.AddYears(100).Year;

    private DateTimeOffset startTime;
    private DateTimeOffset? endTime;
    private DateTimeOffset? nextFireTimeUtc;
    private DateTimeOffset? previousFireTimeUtc;
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
    public override void UpdateAfterMisfire(ICalendar? cal)
    {
        int instr = MisfireInstruction;

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
            while (newFireTime != null && cal != null && !cal.IsTimeIncluded(newFireTime.Value))
            {
                newFireTime = GetFireTimeAfter(newFireTime);
            }
            SetNextFireTimeUtc(newFireTime);
        }
        else if (instr == Quartz.MisfireInstruction.RecurrenceTrigger.FireOnceNow)
        {
            SetNextFireTimeUtc(TimeProvider.GetUtcNow());
        }
    }

    /// <inheritdoc/>
    public override void UpdateAfterMisfire(ICalendar? cal, TimeSpan misfireThreshold)
    {
        int instr = MisfireInstruction;

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
            DateTimeOffset? newFireTime = GetFireTimeAfter(TimeProvider.GetUtcNow() - misfireThreshold);
            while (newFireTime != null && cal != null && !cal.IsTimeIncluded(newFireTime.Value))
            {
                newFireTime = GetFireTimeAfter(newFireTime);
            }
            SetNextFireTimeUtc(newFireTime);
        }
        else if (instr == Quartz.MisfireInstruction.RecurrenceTrigger.FireOnceNow)
        {
            SetNextFireTimeUtc(TimeProvider.GetUtcNow());
        }
    }

    /// <inheritdoc/>
    public override void Triggered(ICalendar? calendar)
    {
        TimesTriggered++;
        previousFireTimeUtc = nextFireTimeUtc;
        nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);

        while (nextFireTimeUtc != null && calendar != null
                                       && !calendar.IsTimeIncluded(nextFireTimeUtc.Value))
        {
            nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);

            if (nextFireTimeUtc == null)
            {
                break;
            }

            if (nextFireTimeUtc.Value.Year > YearToGiveupSchedulingAt)
            {
                nextFireTimeUtc = null;
            }
        }
    }

    /// <inheritdoc/>
    public override void UpdateWithNewCalendar(ICalendar calendar, TimeSpan misfireThreshold)
    {
        nextFireTimeUtc = GetFireTimeAfter(previousFireTimeUtc);

        if (nextFireTimeUtc == null || calendar == null)
        {
            return;
        }

        DateTimeOffset now = TimeProvider.GetUtcNow();
        while (nextFireTimeUtc != null && !calendar.IsTimeIncluded(nextFireTimeUtc.Value))
        {
            nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);

            if (nextFireTimeUtc == null)
            {
                break;
            }

            if (nextFireTimeUtc.Value.Year > YearToGiveupSchedulingAt)
            {
                nextFireTimeUtc = null;
            }

            if (nextFireTimeUtc != null && nextFireTimeUtc < now)
            {
                TimeSpan diff = now - nextFireTimeUtc.Value;
                if (diff >= misfireThreshold)
                {
                    nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);
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
        nextFireTimeUtc = rule.GetNextOccurrence(StartTimeUtc, StartTimeUtc.AddSeconds(-1), TimeZone, EndTimeUtc, skipCount: true);

        if (nextFireTimeUtc == null)
        {
            return null;
        }

        while (nextFireTimeUtc != null && calendar != null
                                       && !calendar.IsTimeIncluded(nextFireTimeUtc.Value))
        {
            nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);

            if (nextFireTimeUtc == null)
            {
                break;
            }

            if (nextFireTimeUtc.Value.Year > YearToGiveupSchedulingAt)
            {
                return null;
            }
        }

        return nextFireTimeUtc;
    }

    /// <inheritdoc/>
    public override DateTimeOffset? GetNextFireTimeUtc()
    {
        return nextFireTimeUtc;
    }

    /// <inheritdoc/>
    public override DateTimeOffset? GetPreviousFireTimeUtc()
    {
        return previousFireTimeUtc;
    }

    /// <inheritdoc/>
    public override void SetNextFireTimeUtc(DateTimeOffset? value)
    {
        nextFireTimeUtc = value;
    }

    /// <inheritdoc/>
    public override void SetPreviousFireTimeUtc(DateTimeOffset? previousFireTimeUtc)
    {
        this.previousFireTimeUtc = previousFireTimeUtc;
    }

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
    public override bool GetMayFireAgain()
    {
        return GetNextFireTimeUtc() != null;
    }

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

        switch (MisfireInstruction)
        {
            case Quartz.MisfireInstruction.RecurrenceTrigger.DoNothing:
                sb.WithMisfireHandlingInstructionDoNothing();
                break;
            case Quartz.MisfireInstruction.RecurrenceTrigger.FireOnceNow:
                sb.WithMisfireHandlingInstructionFireAndProceed();
                break;
            case Quartz.MisfireInstruction.IgnoreMisfirePolicy:
                sb.WithMisfireHandlingInstructionIgnoreMisfires();
                break;
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
