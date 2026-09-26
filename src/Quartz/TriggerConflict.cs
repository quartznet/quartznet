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

using System.Runtime.InteropServices;

namespace Quartz;

/// <summary>
/// What becomes of a trigger already stored under the key of one being scheduled.
/// </summary>
/// <remarks>
/// <para>
/// The store decides under its own lock, so two callers scheduling the same key at once cannot both
/// win: exactly one trigger is stored, and each caller is told which.
/// </para>
/// <para>
/// <see cref="Keep" /> and <see cref="KeepEarlier" /> keep only a <em>pending</em> trigger — one with a
/// fire time still ahead of it, paused or not. A one-shot trigger whose firing has already begun has
/// none, so the new trigger is stored over it, as <see cref="Replace" /> would.
/// </para>
/// </remarks>
/// <seealso cref="IScheduler.ScheduleTrigger" />
/// <seealso cref="OneOffJobOptions.OnConflict" />
public enum TriggerConflict
{
    /// <summary>
    /// Refuse: throw <see cref="ObjectAlreadyExistsException" /> and store nothing. The default, and
    /// what scheduling has always done.
    /// </summary>
    Throw = 0,

    /// <summary>
    /// Store the new trigger over the existing one. Scheduling the same key again with a later time is
    /// a debounce: only the last call fires.
    /// </summary>
    Replace = 1,

    /// <summary>
    /// Leave a pending trigger alone and store nothing: an idempotent enqueue, "make sure this is
    /// scheduled".
    /// </summary>
    Keep = 2,

    /// <summary>
    /// Keep whichever of the two fires first: the new trigger replaces a pending one only when it fires
    /// strictly earlier. A debounce with a deadline that can only move closer.
    /// </summary>
    KeepEarlier = 3,
}

/// <summary>
/// What a scheduling call did with the trigger it was given.
/// </summary>
public enum ScheduleOutcome
{
    /// <summary>
    /// Nothing was stored under the key, and the trigger now is.
    /// </summary>
    Created = 0,

    /// <summary>
    /// The trigger was stored over one already there.
    /// </summary>
    Replaced = 1,

    /// <summary>
    /// A pending trigger was already there and was left alone; the given trigger was not stored.
    /// </summary>
    Kept = 2,
}

/// <summary>
/// What <see cref="IScheduler.ScheduleTrigger" /> did, and when the trigger it left stored fires next.
/// </summary>
/// <param name="NextFireTimeUtc">
/// When the stored trigger fires next: the given trigger's first fire time, or, when
/// <paramref name="Outcome" /> is <see cref="ScheduleOutcome.Kept" />, the kept trigger's next one.
/// </param>
/// <param name="Outcome">Whether the trigger was created, stored over another, or not stored at all.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ScheduleTriggerResult(DateTimeOffset NextFireTimeUtc, ScheduleOutcome Outcome);
