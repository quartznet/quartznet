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
/// Why a firing recorded in the misfire history did not happen.
/// </summary>
/// <remarks>
/// Persisted as the integer, in the <c>REASON</c> column of the misfire history table, so the values
/// are a storage contract: members are never renumbered and a new one is appended.
/// </remarks>
/// <seealso cref="MisfireHistoryEntry.Reason" />
public enum MisfireReason
{
    /// <summary>
    /// A misfire: the scheduler could not fire the trigger in time, and applied its misfire
    /// instruction.
    /// </summary>
    Missed = 0,

    /// <summary>
    /// The trigger's <see cref="OverlapPolicy.Skip" /> dropped the firing, because the previous firing
    /// of the same trigger was still running.
    /// </summary>
    Overlap = 1,
}
