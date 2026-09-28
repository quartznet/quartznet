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

namespace Quartz;

/// <summary>
/// What one backfill found in its range and what it scheduled.
/// </summary>
/// <remarks>
/// Every slot found is either scheduled by this call or was already scheduled, so
/// <see cref="SlotsFound" /> is <see cref="Scheduled" /> plus <see cref="AlreadyScheduled" />.
/// </remarks>
/// <seealso cref="SchedulerBackfillExtensions" />
public sealed record BackfillResult
{
    /// <summary>
    /// The slots the range holds: the original trigger's fire times in it that its calendar does not
    /// exclude.
    /// </summary>
    public int SlotsFound { get; init; }

    /// <summary>
    /// How many slots this call scheduled, one trigger each: the count of <see cref="ScheduledTriggers" />.
    /// </summary>
    public int Scheduled => ScheduledTriggers.Count;

    /// <summary>
    /// The slots skipped because a trigger for them was already stored, by an earlier backfill of an
    /// overlapping range that has not fired it yet.
    /// </summary>
    public int AlreadyScheduled { get; init; }

    /// <summary>
    /// The earliest slot found, or <see langword="null" /> when the range holds none.
    /// </summary>
    public DateTimeOffset? FirstSlot { get; init; }

    /// <summary>
    /// The latest slot found, or <see langword="null" /> when the range holds none.
    /// </summary>
    public DateTimeOffset? LastSlot { get; init; }

    /// <summary>
    /// The keys of the triggers this call stored, earliest slot first.
    /// </summary>
    public List<TriggerKey> ScheduledTriggers { get; init; } = [];
}
