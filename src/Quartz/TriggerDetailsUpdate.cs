namespace Quartz;

/// <summary>
/// Specifies which trigger properties to update without rescheduling.
/// Only properties explicitly set via the builder methods will be changed.
/// </summary>
/// <remarks>
/// <para>
/// Most properties here are pure metadata, but the calendar name and the misfire instruction can
/// affect firing behavior. Changing them via this API does not recompute fire times — the new
/// values take effect starting from the next scheduling evaluation.
/// </para>
/// <para>
/// The misfire instruction is set six ways, and all six earn their place. The five typed overloads
/// of <c>WithMisfireInstruction</c> are one per schedule family, and each is load-bearing: the same
/// number means a different policy in each family, so naming the family is what lets the store
/// reject an update aimed at a trigger of another one rather than silently apply the wrong policy.
/// <see cref="WithMisfireInstructionCode" /> is the sixth, and it is the only way to set a code on a
/// trigger outside the five built-in families — a custom <see cref="ITrigger" /> belongs to none of
/// them, so every typed overload would be rejected for it — as well as the way to pass a code that
/// arrived as a number.
/// </para>
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
    internal int MisfireInstructionCode { get; private set; }

    /// <summary>
    /// The family the misfire instruction was given in, or <see langword="null" /> when it arrived
    /// as a bare code through <see cref="WithMisfireInstructionCode" />. The store rejects an update
    /// whose family is not the stored trigger's.
    /// </summary>
    internal TriggerFamily? MisfireInstructionFamily { get; private set; }

    internal bool HasPreferredNode { get; private set; }
    internal PreferredNode PreferredNode { get; private set; }

    internal bool HasExecutionGroup { get; private set; }
    internal string? ExecutionGroup { get; private set; }

    internal bool HasRetryPolicy { get; private set; }
    internal RetryPolicy? RetryPolicy { get; private set; }

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
    /// <remarks>
    /// A blank name disassociates as well. The store checks that a non-null name exists before it
    /// assigns it, so without this a blank name would be rejected as a missing calendar rather than
    /// clearing the association.
    /// </remarks>
    public TriggerDetailsUpdate WithCalendarName(string? calendarName)
    {
        HasCalendarName = true;
        CalendarName = string.IsNullOrWhiteSpace(calendarName) ? null : calendarName;
        return this;
    }

    /// <summary>
    /// Set the misfire instruction of a simple trigger.
    /// </summary>
    /// <remarks>
    /// The update is rejected if the trigger the key resolves to is not a
    /// <see cref="ISimpleTrigger" />.
    /// </remarks>
    public TriggerDetailsUpdate WithMisfireInstruction(SimpleTriggerMisfireInstruction misfireInstruction)
        => SetMisfireInstruction((int) misfireInstruction, TriggerFamily.Simple);

    /// <summary>
    /// Set the misfire instruction of a cron trigger.
    /// </summary>
    /// <remarks>
    /// The update is rejected if the trigger the key resolves to is not an
    /// <see cref="ICronTrigger" />.
    /// </remarks>
    public TriggerDetailsUpdate WithMisfireInstruction(CronTriggerMisfireInstruction misfireInstruction)
        => SetMisfireInstruction((int) misfireInstruction, TriggerFamily.Cron);

    /// <summary>
    /// Set the misfire instruction of a calendar-interval trigger.
    /// </summary>
    /// <remarks>
    /// The update is rejected if the trigger the key resolves to is not an
    /// <see cref="ICalendarIntervalTrigger" />.
    /// </remarks>
    public TriggerDetailsUpdate WithMisfireInstruction(CalendarIntervalTriggerMisfireInstruction misfireInstruction)
        => SetMisfireInstruction((int) misfireInstruction, TriggerFamily.CalendarInterval);

    /// <summary>
    /// Set the misfire instruction of a daily-time-interval trigger.
    /// </summary>
    /// <remarks>
    /// The update is rejected if the trigger the key resolves to is not an
    /// <see cref="IDailyTimeIntervalTrigger" />.
    /// </remarks>
    public TriggerDetailsUpdate WithMisfireInstruction(DailyTimeIntervalTriggerMisfireInstruction misfireInstruction)
        => SetMisfireInstruction((int) misfireInstruction, TriggerFamily.DailyTimeInterval);

    /// <summary>
    /// Set the misfire instruction of a recurrence trigger.
    /// </summary>
    /// <remarks>
    /// The update is rejected if the trigger the key resolves to is not an
    /// <see cref="IRecurrenceTrigger" />.
    /// </remarks>
    public TriggerDetailsUpdate WithMisfireInstruction(RecurrenceTriggerMisfireInstruction misfireInstruction)
        => SetMisfireInstruction((int) misfireInstruction, TriggerFamily.Recurrence);

    /// <summary>
    /// Set the trigger's misfire instruction as the raw code a trigger stores, for callers that
    /// have a number rather than a family — a value read off the wire, from configuration, or from
    /// <see cref="ITrigger.MisfireInstructionCode" />.
    /// </summary>
    /// <remarks>
    /// Prefer the family-typed <c>WithMisfireInstruction</c> overloads: the same number means a
    /// different policy in each family, and only the typed form lets the store tell you that the
    /// key resolved to a trigger of another one. This one names no family and so skips that check
    /// entirely, which is also what makes it the only way to set a misfire instruction on a trigger
    /// implementation of your own: such a trigger is in none of the five families, so every typed
    /// overload would be rejected for it.
    /// </remarks>
    public TriggerDetailsUpdate WithMisfireInstructionCode(int misfireInstructionCode)
        => SetMisfireInstruction(misfireInstructionCode, family: null);

    private TriggerDetailsUpdate SetMisfireInstruction(int misfireInstructionCode, TriggerFamily? family)
    {
        HasMisfireInstruction = true;
        MisfireInstructionCode = misfireInstructionCode;
        MisfireInstructionFamily = family;
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

    /// <summary>
    /// Set the execution group whose thread limit this trigger's job counts against, or
    /// <see langword="null" /> to remove it from every group.
    /// </summary>
    /// <remarks>
    /// The new group applies from the next acquisition cycle; a job already running keeps counting
    /// against the group it was acquired under.
    /// </remarks>
    /// <seealso cref="ExecutionLimits" />
    public TriggerDetailsUpdate WithExecutionGroup(string? executionGroup)
    {
        HasExecutionGroup = true;
        ExecutionGroup = executionGroup;
        return this;
    }

    /// <summary>
    /// Set how the scheduler re-fires the trigger when its job fails, or <see langword="null" /> to
    /// stop retrying it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The policy is configuration and is the only half of the retry state an update touches. How
    /// many times the occurrence in flight has already been retried is the scheduler's to advance
    /// and clear, so there is deliberately no way to set it here: doing so would either grant a
    /// running job extra attempts or take away ones it has not used.
    /// </para>
    /// <para>
    /// A new policy applies from the next failure. An occurrence already waiting on a retry keeps
    /// the schedule it was given.
    /// </para>
    /// </remarks>
    /// <param name="retryPolicy">the retry policy, or <see langword="null" /> for no retries</param>
    /// <seealso cref="Quartz.RetryPolicy" />
    public TriggerDetailsUpdate WithRetryPolicy(RetryPolicy? retryPolicy)
    {
        HasRetryPolicy = true;
        RetryPolicy = retryPolicy;
        return this;
    }
}
