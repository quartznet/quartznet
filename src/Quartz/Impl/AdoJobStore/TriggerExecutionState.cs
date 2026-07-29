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
    private readonly string? state;

    /// <param name="state">The stored state. Pass <see cref="AdoConstants.StateDeleted" /> when no such trigger exists, or use <see cref="NotFound" />.</param>
    /// <param name="isExecuting">Whether a FIRED_TRIGGERS row for the trigger is in the executing state.</param>
    public TriggerExecutionState(string state, bool isExecuting)
    {
        // Normalized so that spelling "no such trigger" out is equal to NotFound rather than merely
        // equivalent — this is a value type, and two values meaning the same thing should compare equal.
        this.state = state == AdoConstants.StateDeleted ? null : state;
        IsExecuting = isExecuting;
    }

    /// <summary>
    /// The stored state, or <see cref="AdoConstants.StateDeleted" /> when no such trigger exists.
    /// </summary>
    /// <remarks>
    /// A struct can always be default-constructed, which would otherwise leave this null despite being
    /// declared non-nullable; the default reads as a trigger that does not exist rather than as null.
    /// </remarks>
    public string State => state ?? AdoConstants.StateDeleted;

    /// <summary>
    /// Whether a FIRED_TRIGGERS row for the trigger is in the executing state.
    /// </summary>
    public bool IsExecuting { get; }

    /// <summary>
    /// The result for a trigger that does not exist.
    /// </summary>
    public static TriggerExecutionState NotFound => default;
}
