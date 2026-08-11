#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

namespace Quartz.Extensibility;

/// <summary>
/// The single definition of how a stored trigger state plus "is it executing" becomes the
/// <see cref="TriggerState" /> callers see. Every job store resolves through this — a custom
/// <see cref="IJobStore" /> included — so the stores cannot report different states for the same
/// situation.
/// </summary>
public static class TriggerStateResolver
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
    /// <param name="stored">
    /// The trigger's stored state. <see cref="StoredTriggerState.Deleted" /> reports
    /// <see cref="TriggerState.None" />, the same answer a missing trigger gives.
    /// </param>
    /// <param name="isExecuting">Whether at least one execution started by the trigger is still running.</param>
    public static TriggerState Resolve(StoredTriggerState stored, bool isExecuting)
    {
        if (stored == StoredTriggerState.Deleted)
        {
            return TriggerState.None;
        }

        if (stored == StoredTriggerState.Error)
        {
            return TriggerState.Error;
        }

        if (stored is StoredTriggerState.Paused or StoredTriggerState.PausedBlocked)
        {
            return TriggerState.Paused;
        }

        if (isExecuting)
        {
            return TriggerState.Executing;
        }

        return stored switch
        {
            StoredTriggerState.Blocked => TriggerState.Blocked,
            StoredTriggerState.Complete => TriggerState.Complete,

            // Waiting, Acquired and a TRIGGERS row carrying the Executing value some other writer left
            // there: all schedulable, all report as normal.
            _ => TriggerState.Normal
        };
    }
}
