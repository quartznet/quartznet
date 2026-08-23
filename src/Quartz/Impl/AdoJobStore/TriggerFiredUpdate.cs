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
/// Every row change one trigger fire makes, gathered into one value so that
/// <see cref="IDriverDelegate.ApplyTriggerFired" /> can issue them in as few round trips as the
/// provider allows.
/// </summary>
/// <remarks>
/// The store decides all of this before a single statement goes out: it has read the trigger's stored
/// state and type, read the job, and applied the trigger's own <c>Triggered</c> transition in memory.
/// What is left is a set of writes with no read between them, which is exactly what a
/// <see cref="System.Data.Common.DbBatch" /> is for.
/// </remarks>
public sealed record TriggerFiredUpdate
{
    /// <summary>
    /// The trigger that fired, already advanced past the fire by <see cref="IOperableTrigger.Triggered" />.
    /// </summary>
    public required IOperableTrigger Trigger { get; init; }

    /// <summary>
    /// The job the trigger fires. Its concurrency and recovery flags are copied onto the fired-trigger
    /// row, and it is what the trigger's persistence delegate is handed.
    /// </summary>
    public required IJobDetail JobDetail { get; init; }

    /// <summary>
    /// The state to store on the trigger's own row.
    /// </summary>
    public required StoredTriggerState NewState { get; init; }

    /// <summary>
    /// The type discriminator the trigger's row holds, read before the fire.
    /// </summary>
    /// <remarks>
    /// Passed rather than read again: knowing it is the only reason the write path used to issue a
    /// <c>SELECT TRIGGER_TYPE</c> of its own. It is compared with the discriminator of the delegate
    /// that handles the trigger now, and a mismatch — which the fire path cannot produce, but a
    /// subclass firing a rebuilt trigger could — falls back to moving the row between type tables.
    /// </remarks>
    public required string StoredTriggerType { get; init; }

    /// <summary>
    /// The time the fire was scheduled for, which is the trigger's next fire time as it stood
    /// <em>before</em> the fire advanced it.
    /// </summary>
    /// <remarks>
    /// Carried explicitly rather than read off <see cref="Trigger" />, because by the time this update
    /// is applied the trigger has already moved on to its following fire time. That used to be
    /// expressed as an ordering constraint — the fired-trigger row had to be written before
    /// <c>Triggered</c> ran — which is precisely the kind of constraint a batch cannot honour.
    /// </remarks>
    public required DateTimeOffset? ScheduledFireTimeUtc { get; init; }

    /// <summary>
    /// Whether the trigger's recorded original fire time has to be cleared, which it does when this
    /// fire is the recovery of a misfire and the recorded time has now been reported to the job.
    /// </summary>
    public required bool ClearMisfireOriginalFireTime { get; init; }

    /// <summary>
    /// Whether the job's other triggers have to be moved into their blocked states, which they do for a
    /// job that disallows concurrent execution.
    /// </summary>
    public required bool BlockJobTriggers { get; init; }
}
