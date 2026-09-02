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
/// What an <see cref="IDailyTimeIntervalTrigger" /> should do when it misses a firing.
/// </summary>
/// <remarks>
/// Each value is the number a trigger stores in <see cref="ITrigger.MisfireInstructionCode" />, which
/// is family-agnostic and therefore still an <see cref="int" />. Casting between the two is
/// deliberate and safe.
/// </remarks>
/// <seealso cref="DailyTimeIntervalScheduleBuilder.WithMisfireInstruction" />
public enum DailyTimeIntervalTriggerMisfireInstruction
{
    /// <summary>
    /// Let the scheduler pick the policy. This is the default, and for a daily-time-interval
    /// trigger it means <see cref="FireAndProceed" />.
    /// </summary>
    /// <remarks>
    /// Spelled <c>SmartPolicy</c> in JSON scheduling data. The XML scheduling-data schema has no
    /// daily-time-interval trigger.
    /// </remarks>
    SmartPolicy = MisfireInstruction.SmartPolicy,

    /// <summary>
    /// Never treat a missed firing as a misfire: fire every missed firing as soon as possible.
    /// </summary>
    /// <remarks>
    /// A trigger that missed many firings will fire that many times in rapid succession while it
    /// catches up.
    /// <para>Spelled <c>IgnoreMisfirePolicy</c> in JSON scheduling data.</para>
    /// </remarks>
    IgnoreMisfires = MisfireInstruction.IgnoreMisfirePolicy,

    /// <summary>
    /// Fire once now, then resume the schedule.
    /// </summary>
    /// <remarks>
    /// Spelled <c>FireOnceNow</c> in JSON scheduling data.
    /// </remarks>
    FireAndProceed = MisfireInstruction.DailyTimeIntervalTrigger.FireOnceNow,

    /// <summary>
    /// Skip the missed firings and resume at the next scheduled time.
    /// </summary>
    /// <remarks>
    /// Spelled <c>DoNothing</c> in JSON scheduling data.
    /// </remarks>
    DoNothing = MisfireInstruction.DailyTimeIntervalTrigger.DoNothing,
}
