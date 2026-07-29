namespace Quartz.Impl;

/// <summary>
/// The single definition of how a stored trigger state plus "is it executing" becomes the
/// <see cref="TriggerState" /> callers see. Every job store resolves through this, so the stores cannot
/// report different states for the same situation.
/// </summary>
internal static class TriggerStateResolver
{
    /// <summary>
    /// Resolves the reported state, applying the precedence
    /// <c>None &gt; Error &gt; Paused &gt; Executing &gt; Blocked &gt; Complete &gt; Normal</c>.
    /// </summary>
    /// <remarks>
    /// Paused and error outrank executing because they are the facts an operator has to act on, and both
    /// remain true while a previously started execution finishes. Executing outranks blocked so that the
    /// trigger which actually started the running job stays distinguishable from the siblings that are
    /// merely gated behind it.
    /// </remarks>
    /// <param name="stored">The trigger's stored state. A trigger that does not exist is not a case here — callers report <see cref="TriggerState.None" /> before asking.</param>
    /// <param name="isExecuting">Whether at least one execution started by the trigger is still running.</param>
    internal static TriggerState Resolve(InternalTriggerState stored, bool isExecuting)
    {
        if (stored == InternalTriggerState.Error)
        {
            return TriggerState.Error;
        }

        if (stored is InternalTriggerState.Paused or InternalTriggerState.PausedAndBlocked)
        {
            return TriggerState.Paused;
        }

        if (isExecuting)
        {
            return TriggerState.Executing;
        }

        return stored switch
        {
            InternalTriggerState.Blocked => TriggerState.Blocked,
            InternalTriggerState.Complete => TriggerState.Complete,
            _ => TriggerState.Normal
        };
    }
}
