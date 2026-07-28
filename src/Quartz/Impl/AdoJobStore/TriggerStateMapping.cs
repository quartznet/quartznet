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

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Translates between the state strings the ADO job store persists in TRIGGER_STATE and the public
/// <see cref="TriggerState" />. Both directions live here so that a listing's state filter and the
/// state it reports back cannot disagree.
/// </summary>
internal static class TriggerStateMapping
{
    private static readonly string[] normalStates = [AdoConstants.StateWaiting, AdoConstants.StateAcquired, AdoConstants.StateExecuting];
    private static readonly string[] pausedStates = [AdoConstants.StatePaused, AdoConstants.StatePausedBlocked];
    private static readonly string[] completeStates = [AdoConstants.StateComplete];
    private static readonly string[] errorStates = [AdoConstants.StateError];
    private static readonly string[] blockedStates = [AdoConstants.StateBlocked];

    // DELETED is never written to TRIGGER_STATE — it is what a read of a missing row reports — so a
    // filter on it matches no row, which is exactly what "None" means for a stored trigger.
    private static readonly string[] noneStates = [AdoConstants.StateDeleted];

    /// <summary>
    /// Maps a stored state string to the state callers see.
    /// </summary>
    /// <remarks>
    /// <see cref="AdoConstants.StateComplete" /> maps to <see cref="TriggerState.Complete" /> here.
    /// <c>JobStoreSupport.GetTriggerState</c> refines that one case to <see cref="TriggerState.Blocked" />
    /// when the trigger is currently executing, which costs an extra query per trigger; a listing takes
    /// the unrefined answer rather than paying that per row.
    /// </remarks>
    internal static TriggerState ToTriggerState(string? state)
    {
        if (state is null || state == AdoConstants.StateDeleted)
        {
            return TriggerState.None;
        }

        if (state == AdoConstants.StateComplete)
        {
            return TriggerState.Complete;
        }

        if (state == AdoConstants.StatePaused || state == AdoConstants.StatePausedBlocked)
        {
            return TriggerState.Paused;
        }

        if (state == AdoConstants.StateError)
        {
            return TriggerState.Error;
        }

        if (state == AdoConstants.StateBlocked)
        {
            return TriggerState.Blocked;
        }

        return TriggerState.Normal;
    }

    /// <summary>
    /// The stored state strings a public state covers, for a <c>TRIGGER_STATE IN (...)</c> filter.
    /// </summary>
    internal static string[] ToStoredStates(TriggerState state)
    {
        switch (state)
        {
            case TriggerState.Normal:
                return normalStates;
            case TriggerState.Paused:
                return pausedStates;
            case TriggerState.Complete:
                return completeStates;
            case TriggerState.Error:
                return errorStates;
            case TriggerState.Blocked:
                return blockedStates;
            case TriggerState.None:
                return noneStates;
            default:
                Throw.ArgumentOutOfRangeException(nameof(state), "Unknown trigger state: " + state);
                return default;
        }
    }
}
