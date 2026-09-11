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
/// One trigger that missed its scheduled firing, as the scheduler reported it.
/// </summary>
/// <remarks>
/// A misfire is not an execution — nothing ran — so it is recorded beside the executions rather than
/// among them, under the same bounds.
/// </remarks>
/// <param name="SchedulerName">The scheduler the trigger belongs to.</param>
/// <param name="SchedulerInstanceId">The node that noticed the misfire.</param>
/// <param name="TriggerGroup">The group of the trigger that missed its firing.</param>
/// <param name="TriggerName">The name of the trigger that missed its firing.</param>
/// <param name="JobKey">
/// The job the trigger points at, or <see langword="null" /> when the trigger names none.
/// </param>
/// <param name="MisfiredAtUtc">When the misfire was noticed, on the scheduler's clock.</param>
/// <param name="ScheduledFireTimeUtc">
/// The firing that was missed, or <see langword="null" /> when the trigger had no next firing left to
/// name. The scheduler reports a misfire before it applies the trigger's misfire instruction, so this
/// is the time the trigger was still due at.
/// </param>
public sealed record MisfireHistoryEntry(
    string SchedulerName,
    string SchedulerInstanceId,
    string TriggerGroup,
    string TriggerName,
    JobKey? JobKey,
    DateTimeOffset MisfiredAtUtc,
    DateTimeOffset? ScheduledFireTimeUtc);
