using Quartz.Impl.Triggers;
using Quartz.Spi;

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
public sealed class RecurrenceScheduleBuilder : ScheduleBuilder<IRecurrenceTrigger>
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
    public override IMutableTrigger Build()
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
    public RecurrenceScheduleBuilder InTimeZone(TimeZoneInfo? tz)
    {
        timeZone = tz;
        return this;
    }

    /// <summary>
    /// If the trigger misfires, instruct the scheduler to ignore all misfire policies
    /// and fire the trigger immediately.
    /// </summary>
    public RecurrenceScheduleBuilder WithMisfireHandlingInstructionIgnoreMisfires()
    {
        misfireInstruction = MisfireInstruction.IgnoreMisfirePolicy;
        return this;
    }

    /// <summary>
    /// If the trigger misfires, instruct the scheduler to fire the trigger now.
    /// </summary>
    public RecurrenceScheduleBuilder WithMisfireHandlingInstructionFireAndProceed()
    {
        misfireInstruction = MisfireInstruction.RecurrenceTrigger.FireOnceNow;
        return this;
    }

    /// <summary>
    /// If the trigger misfires, instruct the scheduler to not fire the trigger now
    /// and instead wait for the next scheduled fire time.
    /// </summary>
    public RecurrenceScheduleBuilder WithMisfireHandlingInstructionDoNothing()
    {
        misfireInstruction = MisfireInstruction.RecurrenceTrigger.DoNothing;
        return this;
    }

    /// <summary>
    /// Set the misfire handling instruction directly.
    /// </summary>
    internal RecurrenceScheduleBuilder WithMisfireHandlingInstruction(int instruction)
    {
        misfireInstruction = instruction;
        return this;
    }
}

/// <summary>
/// Extension methods for building recurrence schedule triggers via <see cref="TriggerBuilder"/>.
/// </summary>
public static class RecurrenceTriggerBuilderExtensions
{
    /// <summary>
    /// Set the trigger to use an RFC 5545 RRULE-based schedule.
    /// </summary>
    /// <param name="triggerBuilder">The trigger builder.</param>
    /// <param name="recurrenceRule">
    /// An RFC 5545 RRULE string, e.g. "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR".
    /// </param>
    public static TriggerBuilder WithRecurrenceSchedule(
        this TriggerBuilder triggerBuilder,
        string recurrenceRule)
    {
        RecurrenceScheduleBuilder builder = RecurrenceScheduleBuilder.Create(recurrenceRule);
        return triggerBuilder.WithSchedule(builder);
    }

    /// <summary>
    /// Set the trigger to use an RFC 5545 RRULE-based schedule with additional configuration.
    /// </summary>
    /// <param name="triggerBuilder">The trigger builder.</param>
    /// <param name="recurrenceRule">
    /// An RFC 5545 RRULE string, e.g. "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR".
    /// </param>
    /// <param name="action">Action to further configure the schedule builder.</param>
    public static TriggerBuilder WithRecurrenceSchedule(
        this TriggerBuilder triggerBuilder,
        string recurrenceRule,
        Action<RecurrenceScheduleBuilder> action)
    {
        RecurrenceScheduleBuilder builder = RecurrenceScheduleBuilder.Create(recurrenceRule);
        action(builder);
        return triggerBuilder.WithSchedule(builder);
    }
}
