namespace Quartz;

/// <summary>
/// Specifies which trigger properties to update without rescheduling.
/// Only properties explicitly set via the builder methods will be changed.
/// </summary>
/// <remarks>
/// Most properties here are pure metadata, but <see cref="CalendarName"/> and
/// <see cref="MisfireInstruction"/> can affect firing behavior. Changing them via this
/// API does not recompute fire times — the new values take effect starting from the
/// next scheduling evaluation.
/// </remarks>
/// <seealso cref="IScheduler.UpdateTriggerDetails"/>
public sealed class TriggerDetailsUpdate
{
    internal bool HasDescription { get; private set; }
    internal string? Description { get; private set; }

    internal bool HasPriority { get; private set; }
    internal int Priority { get; private set; }

    internal bool HasJobDataMap { get; private set; }
    internal JobDataMap? JobDataMap { get; private set; }

    internal bool HasCalendarName { get; private set; }
    internal string? CalendarName { get; private set; }

    internal bool HasMisfireInstruction { get; private set; }
    internal int MisfireInstruction { get; private set; }

    internal bool HasPreferredNode { get; private set; }
    internal PreferredNode PreferredNode { get; private set; }

    /// <summary>
    /// Set the trigger's description.
    /// </summary>
    public TriggerDetailsUpdate WithDescription(string? description)
    {
        HasDescription = true;
        Description = description;
        return this;
    }

    /// <summary>
    /// Set the trigger's priority.
    /// </summary>
    public TriggerDetailsUpdate WithPriority(int priority)
    {
        HasPriority = true;
        Priority = priority;
        return this;
    }

    /// <summary>
    /// Set the trigger's <see cref="Quartz.JobDataMap"/>.
    /// </summary>
    public TriggerDetailsUpdate WithJobDataMap(JobDataMap jobDataMap)
    {
        HasJobDataMap = true;
        JobDataMap = jobDataMap;
        return this;
    }

    /// <summary>
    /// Set the trigger's associated calendar name, or <c>null</c> to disassociate.
    /// </summary>
    public TriggerDetailsUpdate WithCalendarName(string? calendarName)
    {
        HasCalendarName = true;
        CalendarName = calendarName;
        return this;
    }

    /// <summary>
    /// Set the trigger's misfire instruction.
    /// </summary>
    public TriggerDetailsUpdate WithMisfireInstruction(int misfireInstruction)
    {
        HasMisfireInstruction = true;
        MisfireInstruction = misfireInstruction;
        return this;
    }

    /// <summary>
    /// Set the trigger's preferred node for cluster node affinity.
    /// </summary>
    /// <param name="preferredNode">
    /// The pin: <see cref="Quartz.PreferredNode.None" /> to clear,
    /// <see cref="Quartz.PreferredNode.Auto" /> for auto-pin on first fire, or
    /// <see cref="Quartz.PreferredNode.For" /> to name a node.
    /// </param>
    /// <seealso cref="Quartz.PreferredNode" />
    public TriggerDetailsUpdate WithPreferredNode(PreferredNode preferredNode)
    {
        HasPreferredNode = true;
        PreferredNode = preferredNode;
        return this;
    }
}
