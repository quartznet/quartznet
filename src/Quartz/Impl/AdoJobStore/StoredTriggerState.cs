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
/// The state the ADO job store keeps a trigger in, as the TRIGGER_STATE and STATE columns hold it.
/// </summary>
/// <remarks>
/// <para>
/// This is storage's own vocabulary, not the one callers see: <see cref="TriggerState" /> is what a
/// scheduler reports, and one is derived from the other (plus whether an execution is in flight) rather
/// than mapped one to one. Only <see cref="IDriverDelegate" /> and its callers speak this enum.
/// </para>
/// <para>
/// The database keeps storing the same strings it always has — see
/// <see cref="StoredTriggerStates.ToStoredValue" /> and <see cref="StoredTriggerStates.FromStoredValue" />,
/// which are the only place the two representations meet. A delegate written against this enum therefore
/// reads and writes rows a 3.x scheduler wrote, and vice versa.
/// </para>
/// </remarks>
public enum StoredTriggerState
{
    /// <summary>
    /// Schedulable: the trigger is waiting for its next fire time to arrive. The default, and the state
    /// a stored value this version does not recognise is treated as.
    /// </summary>
    Waiting,

    /// <summary>
    /// A scheduler instance has reserved the trigger and intends to fire it.
    /// </summary>
    Acquired,

    /// <summary>
    /// The trigger is firing. Written to FIRED_TRIGGERS rather than to TRIGGERS, where it only turns up
    /// through migrated or hand-repaired data.
    /// </summary>
    Executing,

    /// <summary>
    /// The trigger has no further fire times and is awaiting removal.
    /// </summary>
    Complete,

    /// <summary>
    /// Held back because its job disallows concurrent execution and is already running.
    /// </summary>
    Blocked,

    /// <summary>
    /// The trigger could not be fired — typically its job type would not load — and will not be
    /// retried until it is reset.
    /// </summary>
    Error,

    /// <summary>
    /// Paused, either individually or as part of its group.
    /// </summary>
    Paused,

    /// <summary>
    /// Paused while also blocked, so that resuming returns it to <see cref="Blocked" /> rather than to
    /// <see cref="Waiting" />.
    /// </summary>
    PausedBlocked,

    /// <summary>
    /// The trigger does not exist. A sentinel a read reports rather than a value the store writes.
    /// </summary>
    Deleted
}

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
