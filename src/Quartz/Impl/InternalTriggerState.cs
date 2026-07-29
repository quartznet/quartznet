namespace Quartz.Impl;

/// <summary>
/// The stored trigger states, normalized across job stores. The in-memory store holds one of these
/// directly; the ADO store maps its persisted state strings onto them, so that both can resolve a
/// reported <see cref="TriggerState" /> through <see cref="TriggerStateResolver" />.
/// </summary>
/// <remarks>
/// There is deliberately no "executing" state: this drives scheduling decisions, and a trigger stays
/// schedulable while its job runs. Executions are tracked separately by each store.
/// </remarks>
internal enum InternalTriggerState
{
    /// <summary>
    /// Waiting 
    /// </summary>
    Waiting,

    /// <summary>
    /// Acquired
    /// </summary>
    Acquired,

    /// <summary>
    /// Complete
    /// </summary>
    Complete,

    /// <summary>
    /// Paused
    /// </summary>
    Paused,

    /// <summary>
    /// Blocked
    /// </summary>
    Blocked,

    /// <summary>
    /// Paused and Blocked
    /// </summary>
    PausedAndBlocked,

    /// <summary>
    /// Error
    /// </summary>
    Error
}