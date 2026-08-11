using Quartz.Extensibility;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// A trigger's stored state together with whether it currently has an execution in flight.
/// </summary>
/// <remarks>
/// The two facts live in different tables — the state in TRIGGERS, the execution in FIRED_TRIGGERS — and
/// are read together so that reporting a trigger's state stays a single round trip.
/// </remarks>
public readonly record struct TriggerExecutionState
{
    // Nullable so that the default value reads as "no such trigger" rather than as Waiting, which is
    // what the enum's own default is.
    private readonly StoredTriggerState? state;

    /// <param name="state">The stored state. Pass <see cref="StoredTriggerState.Deleted" /> when no such trigger exists, or use <see cref="NotFound" />.</param>
    /// <param name="isExecuting">Whether a FIRED_TRIGGERS row for the trigger is in the executing state.</param>
    public TriggerExecutionState(StoredTriggerState state, bool isExecuting)
    {
        // Normalized so that spelling "no such trigger" out is equal to NotFound rather than merely
        // equivalent — this is a value type, and two values meaning the same thing should compare equal.
        this.state = state == StoredTriggerState.Deleted ? null : state;
        IsExecuting = isExecuting;
    }

    /// <summary>
    /// The stored state, or <see cref="StoredTriggerState.Deleted" /> when no such trigger exists.
    /// </summary>
    public StoredTriggerState State => state ?? StoredTriggerState.Deleted;

    /// <summary>
    /// Whether a FIRED_TRIGGERS row for the trigger is in the executing state.
    /// </summary>
    public bool IsExecuting { get; }

    /// <summary>
    /// The result for a trigger that does not exist.
    /// </summary>
    public static TriggerExecutionState NotFound => default;
}
