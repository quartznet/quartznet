namespace Quartz.Extensibility;

/// <summary>
/// Should not be used by end users.
/// </summary>
public interface IMutableTrigger : ITrigger
{
    new TriggerKey Key { get; set; }

    new JobKey JobKey { get; set; }

    /// <summary>
    /// Set a description for the <see cref="ITrigger" /> instance - may be
    /// useful for remembering/displaying the purpose of the trigger, though the
    /// description has no meaning to Quartz.
    /// </summary>
    new string? Description { get; set; }

    /// <summary>
    /// Gets or sets the execution group for this trigger. Execution groups allow
    /// per-node thread limits to be configured so that resource-intensive jobs
    /// do not saturate all available threads.
    /// </summary>
    /// <remarks>
    /// A <see langword="null"/> value means the trigger has no execution group
    /// (the default, backward-compatible behavior).
    /// </remarks>
    new string? ExecutionGroup { get; set; }

    /// <summary>
    /// Get or set which cluster node this trigger prefers to run on. Only that node acquires the
    /// trigger, with automatic failover while it is down.
    /// </summary>
    /// <remarks>
    /// <see cref="Quartz.PreferredNode.None" /> means the trigger has no node preference. The
    /// assigned value is recorded as given, automatic-pin flag included.
    /// </remarks>
    new PreferredNode PreferredNode { get; set; }

    /// <summary>
    /// Associate the <see cref="ICalendar" /> with the given name with this Trigger.
    /// </summary>
    new string? CalendarName { get; set; }

    /// <summary>
    /// Set the <see cref="JobDataMap" /> to be associated with the
    /// <see cref="ITrigger" />.
    /// </summary>
    new JobDataMap JobDataMap { get; set; }

    /// <summary>
    /// The priority of a <see cref="ITrigger" /> acts as a tie breaker such that if
    /// two <see cref="ITrigger" />s have the same scheduled fire time, then Quartz
    /// will do its best to give the one with the higher priority first access
    /// to a worker thread.
    /// </summary>
    /// <remarks>
    /// If not explicitly set, the default value is 5.
    /// </remarks>
    /// <seealso cref="TriggerConstants.DefaultPriority" />
    new int Priority { get; set; }

    /// <summary>
    /// <para>
    /// The time at which the trigger's scheduling should start.  May or may not
    /// be the first actual fire time of the trigger, depending upon the type of
    /// trigger and the settings of the other properties of the trigger.  However
    /// the first actual first time will not be before this date.
    /// </para>
    /// <para>
    /// Setting a value in the past may cause a new trigger to compute a first
    /// fire time that is in the past, which may cause an immediate misfire
    /// of the trigger.
    /// </para>
    /// </summary>
    new DateTimeOffset StartTimeUtc { get; set; }

    /// <summary>
    /// <para>
    /// Set the time at which the <see cref="ITrigger" /> should quit repeating -
    /// regardless of any remaining repeats (based on the trigger's particular
    /// repeat settings).
    /// </para>
    /// </summary>
    /// <remarks>
    /// </remarks>
    new DateTimeOffset? EndTimeUtc { get; set; }

    /// <summary>
    /// The next time at which the <see cref="ITrigger" /> is scheduled to fire.
    /// </summary>
    /// <remarks>
    /// <b>Not for client code.</b> The scheduler and job store advance this as the trigger fires.
    /// </remarks>
    new DateTimeOffset? NextFireTimeUtc { get; set; }

    /// <summary>
    /// The previous time at which the <see cref="ITrigger" /> fired.
    /// </summary>
    /// <remarks>
    /// <b>Not for client code.</b> The scheduler and job store record this as the trigger fires.
    /// </remarks>
    new DateTimeOffset? PreviousFireTimeUtc { get; set; }

    /// <summary>
    /// The raw code of the instruction the <see cref="IScheduler" /> follows when this trigger
    /// misses a firing.
    /// </summary>
    /// <remarks>
    /// The value is validated against the concrete trigger's family, which rejects a code outside
    /// its own range — but not one that is in range for two families and means something different
    /// in each. Assign a value cast from the family's own misfire enum.
    /// </remarks>
    /// <seealso cref="ISimpleTrigger.MisfireInstruction" />
    /// <seealso cref="ICronTrigger.MisfireInstruction" />
    new int MisfireInstructionCode { get; set; }
}