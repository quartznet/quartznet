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
/// One row of the FIRED_TRIGGERS table: a firing a scheduler instance has reserved, is running, or has
/// been blocked from running.
/// </summary>
/// <remarks>
/// A row is written when a trigger is acquired and rewritten when it starts executing, so the members
/// describing the job are only populated once the job is known — see <see cref="JobKey" />.
/// </remarks>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public sealed record FiredTriggerRecord
{
    /// <summary>
    /// Identifies this firing, and only this one: a trigger that fires again gets a new value.
    /// </summary>
    public required string FireInstanceId { get; init; }

    /// <summary>
    /// The state of the firing, which is the state of the FIRED_TRIGGERS row rather than of the trigger
    /// itself.
    /// </summary>
    public required StoredTriggerState FireInstanceState { get; init; }

    /// <summary>
    /// The trigger that fired.
    /// </summary>
    public required TriggerKey TriggerKey { get; init; }

    /// <summary>
    /// The instance id of the scheduler that reserved the firing or is running it.
    /// </summary>
    public required string SchedulerInstanceId { get; init; }

    /// <summary>
    /// When the row was written.
    /// </summary>
    public DateTimeOffset FireTimestamp { get; init; }

    /// <summary>
    /// The time the trigger was scheduled to fire at.
    /// </summary>
    public DateTimeOffset ScheduleTimestamp { get; init; }

    /// <summary>
    /// The job the trigger fires, or <see langword="null" /> for a row still in
    /// <see cref="StoredTriggerState.Acquired" />: acquisition records the trigger before the job has
    /// been loaded, so the job columns are only filled in once the firing starts.
    /// </summary>
    public JobKey? JobKey { get; init; }

    /// <summary>
    /// Whether the job disallows concurrent execution. Always <see langword="false" /> while
    /// <see cref="JobKey" /> is <see langword="null" />.
    /// </summary>
    public bool JobDisallowsConcurrentExecution { get; init; }

    /// <summary>
    /// Whether the job asks to be recovered after a scheduler failure. Always <see langword="false" />
    /// while <see cref="JobKey" /> is <see langword="null" />.
    /// </summary>
    public bool JobRequestsRecovery { get; init; }

    /// <summary>
    /// The priority the trigger fired with.
    /// </summary>
    public int Priority { get; init; }
}
