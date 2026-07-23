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

using Quartz.Spi;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// One pass worth of misfired triggers, as returned by
/// <see cref="IDriverDelegate.SelectMisfiredTriggersToRecover" />.
/// </summary>
/// <param name="Triggers">The misfired triggers, ordered by next fire time then descending priority.</param>
/// <param name="HasMore">
/// Whether more misfired triggers were left behind by the row limit, meaning recovery should run again
/// promptly rather than waiting out the misfire handler's normal interval.
/// </param>
public readonly record struct MisfiredTriggerBatch(
    List<IOperableTrigger> Triggers,
    bool HasMore);

/// <summary>
/// A single trigger's pending misfire update, as applied in a batch by
/// <see cref="IDriverDelegate.UpdateMisfiredTriggers" />.
/// </summary>
/// <param name="Trigger">The trigger, after <c>UpdateAfterMisfire</c> has been applied in-memory.</param>
/// <param name="NewState">The new trigger state to persist (e.g. WAITING, COMPLETE, BLOCKED).</param>
/// <param name="MisfireOriginalFireTime">
/// The original scheduled fire time for "fire now" misfire policies. When non-<c>null</c>, the value is
/// written to the MISFIRE_ORIG_FIRE_TIME column. <c>null</c> leaves the column unchanged, preserving any
/// previously stored original fire time.
/// </param>
public readonly record struct MisfiredTriggerUpdate(
    IOperableTrigger Trigger,
    string NewState,
    DateTimeOffset? MisfireOriginalFireTime);
