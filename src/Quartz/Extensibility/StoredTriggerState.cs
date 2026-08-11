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
/// The state a job store keeps a trigger in.
/// </summary>
/// <remarks>
/// <para>
/// This is storage's own vocabulary, not the one callers see: <see cref="TriggerState" /> is what a
/// scheduler reports, and one is derived from the other (plus whether an execution is in flight) by
/// <see cref="TriggerStateResolver.Resolve" /> rather than mapped one to one. Every job store — the
/// in-memory one, the ADO.NET one and a custom <see cref="IJobStore" /> alike — keeps its triggers in
/// this vocabulary and resolves through the same precedence, so two stores cannot report different
/// states for the same situation.
/// </para>
/// <para>
/// The ADO.NET job store persists these as the same strings the TRIGGER_STATE and STATE columns have
/// always held — <c>Quartz.Impl.AdoJobStore.StoredTriggerStates</c> is the only place the two
/// representations meet, and the strings themselves stay on <c>AdoConstants</c>, which is that store's
/// schema contract.
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
    /// The trigger is firing. The ADO.NET store writes this to FIRED_TRIGGERS rather than to TRIGGERS,
    /// where it only turns up through migrated or hand-repaired data.
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
    /// The trigger does not exist. A sentinel a read reports rather than a value a store writes.
    /// </summary>
    Deleted
}
