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

using Quartz.Extensibility;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Translates between <see cref="StoredTriggerState" /> and the strings the trigger state columns hold.
/// </summary>
/// <remarks>
/// A custom <see cref="IDriverDelegate" /> binds these values into its own statements, so both directions
/// are public. The strings themselves stay on <see cref="AdoConstants" />, which is the schema contract.
/// </remarks>
public static class StoredTriggerStates
{
    /// <summary>
    /// The value the trigger state column holds for this state.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The state is not a defined enum member.</exception>
    public static string ToStoredValue(this StoredTriggerState state)
    {
        switch (state)
        {
            case StoredTriggerState.Waiting: return AdoConstants.StateWaiting;
            case StoredTriggerState.Acquired: return AdoConstants.StateAcquired;
            case StoredTriggerState.Executing: return AdoConstants.StateExecuting;
            case StoredTriggerState.Complete: return AdoConstants.StateComplete;
            case StoredTriggerState.Blocked: return AdoConstants.StateBlocked;
            case StoredTriggerState.Error: return AdoConstants.StateError;
            case StoredTriggerState.Paused: return AdoConstants.StatePaused;
            case StoredTriggerState.PausedBlocked: return AdoConstants.StatePausedBlocked;
            case StoredTriggerState.Deleted: return AdoConstants.StateDeleted;
            default:
                Throw.ArgumentOutOfRangeException(nameof(state), "Unknown stored trigger state: " + state);
                return default;
        }
    }

    /// <summary>
    /// The state a stored column value stands for.
    /// </summary>
    /// <param name="storedValue">
    /// The column value, or <see langword="null" /> for a row that does not exist — which reads as
    /// <see cref="StoredTriggerState.Deleted" />, the same sentinel a missing trigger reports.
    /// </param>
    /// <remarks>
    /// A value this version does not recognise — left by a third-party delegate, a migration or a
    /// hand-repaired row — reads as <see cref="StoredTriggerState.Waiting" />, which is how the store has
    /// always treated it: schedulable, and reported as a normal trigger.
    /// </remarks>
    public static StoredTriggerState FromStoredValue(string? storedValue)
    {
        return storedValue switch
        {
            null => StoredTriggerState.Deleted,
            AdoConstants.StateWaiting => StoredTriggerState.Waiting,
            AdoConstants.StateAcquired => StoredTriggerState.Acquired,
            AdoConstants.StateExecuting => StoredTriggerState.Executing,
            AdoConstants.StateComplete => StoredTriggerState.Complete,
            AdoConstants.StateBlocked => StoredTriggerState.Blocked,
            AdoConstants.StateError => StoredTriggerState.Error,
            AdoConstants.StatePaused => StoredTriggerState.Paused,
            AdoConstants.StatePausedBlocked => StoredTriggerState.PausedBlocked,
            AdoConstants.StateDeleted => StoredTriggerState.Deleted,
            _ => StoredTriggerState.Waiting
        };
    }
}
