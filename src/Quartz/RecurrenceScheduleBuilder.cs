using Quartz.Impl.Triggers;
using Quartz.Extensibility;

namespace Quartz;

/// <summary>
/// <see cref="RecurrenceScheduleBuilder"/> is a <see cref="IScheduleBuilder"/>
/// that defines RFC 5545 RRULE-based schedules for triggers.
/// </summary>
/// <remarks>
/// <para>
/// Quartz provides a builder-style API for constructing scheduling-related
/// entities via a Domain-Specific Language (DSL). The DSL can best be
/// utilized through the usage of static imports of the methods on the classes
/// <see cref="TriggerBuilder"/>, <see cref="JobBuilder"/>,
/// <see cref="DateBuilder"/>, <see cref="JobKey"/>, <see cref="TriggerKey"/>
/// and the various <see cref="IScheduleBuilder"/> implementations.
/// </para>
/// <code>
/// ITrigger trigger = TriggerBuilder.Create()
///     .WithIdentity("myTrigger", "myGroup")
///     .WithRecurrenceSchedule("FREQ=MONTHLY;BYDAY=2MO")
///     .StartNow()
///     .Build();
/// </code>
/// </remarks>
/// <seealso cref="IRecurrenceTrigger"/>
/// <seealso cref="TriggerBuilder"/>
public sealed class RecurrenceScheduleBuilder : IScheduleBuilder
{
    private string recurrenceRule;
    private int misfireInstruction = MisfireInstruction.SmartPolicy;
    private TimeZoneInfo? timeZone;

    private RecurrenceScheduleBuilder(string recurrenceRule)
    {
        ArgumentNullException.ThrowIfNull(recurrenceRule);

        // Validate early, matching CronScheduleBuilder's fail-fast behavior
        Impl.Recurrence.RecurrenceRule.Parse(recurrenceRule);

        this.recurrenceRule = recurrenceRule;
    }

    /// <summary>
    /// Create a <see cref="RecurrenceScheduleBuilder"/> with the given RRULE string.
    /// </summary>
    /// <param name="recurrenceRule">
    /// An RFC 5545 RRULE string, e.g. "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR".
    /// </param>
    public static RecurrenceScheduleBuilder Create(string recurrenceRule)
    {
        return new RecurrenceScheduleBuilder(recurrenceRule);
    }

    /// <summary>
    /// Build the actual trigger -- NOT intended to be invoked by end users,
    /// but will rather be invoked by a <see cref="TriggerBuilder"/> which this
    /// <see cref="IScheduleBuilder"/> is given to.
    /// </summary>
    public IMutableTrigger Build()
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl();
        trigger.RecurrenceRule = recurrenceRule;
        trigger.MisfireInstruction = misfireInstruction;
        trigger.triggerTimeZone = timeZone;
        return trigger;
    }

    /// <summary>
    /// Set the time zone for recurrence calculations.
    /// </summary>
    /// <param name="timeZone">the time-zone for the schedule; <see langword="null" /> means the
    /// system's local time zone.</param>
    public RecurrenceScheduleBuilder InTimeZone(TimeZoneInfo? timeZone)
    {
        this.timeZone = timeZone;
        return this;
    }

    /// <summary>
    /// Say what the trigger should do when it misses a firing.
    /// </summary>
    /// <param name="instruction">the policy to apply; defaults to
    /// <see cref="RecurrenceTriggerMisfireInstruction.SmartPolicy" />.</param>
    /// <returns>the updated RecurrenceScheduleBuilder</returns>
    /// <seealso cref="RecurrenceTriggerMisfireInstruction" />
    public RecurrenceScheduleBuilder WithMisfireInstruction(RecurrenceTriggerMisfireInstruction instruction)
    {
        misfireInstruction = (int) instruction;
        return this;
    }
}
