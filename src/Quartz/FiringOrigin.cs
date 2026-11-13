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
/// Why a firing runs: on its trigger's schedule, because somebody asked for it, or as a backfill of a
/// slot the schedule had.
/// </summary>
/// <remarks>
/// Every firing has one, so there is no "none": <see cref="Schedule" /> is the default. A retry, a misfire
/// catch-up and a recovery keep the origin of the firing they continue.
/// </remarks>
/// <seealso cref="FiringOccurrence" />
/// <seealso cref="JobExecutionContextOccurrenceExtensions.GetOccurrence" />
public enum FiringOrigin
{
    /// <summary>
    /// The trigger's schedule called for it. <see cref="FiringOccurrence.OccurrenceUtc" /> is the fire time
    /// the schedule named.
    /// </summary>
    Schedule,

    /// <summary>
    /// <see cref="IScheduler.TriggerJob" /> or <see cref="SchedulerJobExtensions.TriggerJob{TInput}" />
    /// asked for it. <see cref="FiringOccurrence.OccurrenceUtc" /> is when the run was asked for.
    /// </summary>
    Manual,

    /// <summary>
    /// <see cref="SchedulerBackfillExtensions.Backfill" /> scheduled it for a slot the trigger had.
    /// <see cref="FiringOccurrence.OccurrenceUtc" /> is the slot, not when the backfill ran it.
    /// </summary>
    Backfill
}
