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
/// Thrown inside a firing round that runs without <see cref="SchedulerLock.TriggerAccess" /> when it
/// finds something only the lock decides: a candidate whose job disallows concurrent execution or whose
/// overlap policy is decided at the fire, before anything is written, or — after the claim — a job or
/// trigger replaced meanwhile into one of those. The transaction is rolled back and the round runs again
/// under the lock (#4067).
/// </summary>
/// <remarks>
/// <para>
/// Rolling back rather than taking the lock in the open transaction keeps the lock order every locked
/// path has: <c>TRIGGER_ACCESS</c> first, rows after. A round that has claimed holds row locks, and
/// waiting on <c>TRIGGER_ACCESS</c> while holding them would deadlock against a locked path waiting on
/// one of those rows. Before the claim the order would be safe, and the rollback is kept there too so
/// that the round has one escalation path, which the hysteresis makes a per-episode cost rather than a
/// per-round one.
/// </para>
/// <para>
/// A <see cref="JobPersistenceException" />, as <see cref="CompletionNeedsLockException" /> is, so that
/// the store's guards and its transaction wrapper let it through as itself: neither wraps one, and the
/// wrapper rolls back and rethrows without retrying, since nothing about it is transient.
/// </para>
/// </remarks>
internal sealed class RoundNeedsLockException : JobPersistenceException
{
    public RoundNeedsLockException(RoundLockReason reason, TriggerKey? triggerKey)
        : base($"The firing round needs TRIGGER_ACCESS: {reason.Describe()}" + (triggerKey is null ? "." : $" ({triggerKey})."))
    {
        Reason = reason;
        TriggerKey = triggerKey;
    }

    /// <summary>Why the round needs the lock.</summary>
    public RoundLockReason Reason { get; }

    /// <summary>The candidate that needs it, when one does.</summary>
    public TriggerKey? TriggerKey { get; }
}
