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
/// The <c>FailedJobOriginalTrigger*</c> entries of a recovery trigger's data map, which name the firing it
/// replays, as startup recovery and cluster recovery both write them.
/// </summary>
internal static class RecoveryMarkers
{
    /// <summary>
    /// Whether the firing being recovered was itself a replay whose data map already names the firing it
    /// stands in for.
    /// </summary>
    /// <remarks>
    /// Such a map keeps its markers, so a replay of a replay names the original firing: the recovery
    /// trigger's own key names a spent trigger that the same recovery removes.
    /// </remarks>
    /// <param name="firedTriggerKey">The key of the trigger whose firing is recovered.</param>
    /// <param name="jobDataMap">That trigger's data map.</param>
    internal static bool ReplaysARecovery(TriggerKey firedTriggerKey, JobDataMap jobDataMap)
    {
        return firedTriggerKey.Group == SchedulerConstants.DefaultRecoveryGroup
               && jobDataMap.ContainsKey(SchedulerConstants.FailedJobOriginalTriggerName);
    }
}
