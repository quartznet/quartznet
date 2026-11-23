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
/// Where a wait for a firing stood when it answered.
/// </summary>
/// <seealso cref="FiringResult" />
/// <seealso cref="SchedulerWaitExtensions" />
public enum FiringWaitStatus
{
    /// <summary>
    /// The firing ended, and <see cref="FiringResult.Outcome" /> says how.
    /// </summary>
    /// <remarks>
    /// A retried occurrence ends at its last attempt: an attempt the trigger answers with another one never
    /// ends a wait.
    /// </remarks>
    Ended = 0,

    /// <summary>
    /// The timeout ran out first. The firing may still be due or running; the wait never stops it.
    /// </summary>
    /// <remarks>
    /// Wait again with <see cref="SchedulerWaitExtensions.WaitForFiring" /> and <see cref="FiringResult.TriggerKey" />.
    /// </remarks>
    Pending = 1,

    /// <summary>
    /// The trigger is gone and nothing says how its firing ended: it was unscheduled or its job deleted, it
    /// ended before the wait began on a scheduler that keeps no execution history, or its row was trimmed.
    /// </summary>
    Unknown = 2
}
