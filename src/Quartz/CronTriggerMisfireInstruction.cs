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
/// What an <see cref="ICronTrigger" /> should do when it misses a firing.
/// </summary>
/// <remarks>
/// The values match the <see cref="MisfireInstruction" /> constants a trigger stores in
/// <see cref="ITrigger.MisfireInstructionCode" />, which is family-agnostic and therefore still an
/// <see cref="int" />.
/// </remarks>
/// <seealso cref="CronScheduleBuilder.WithMisfireInstruction" />
public enum CronTriggerMisfireInstruction
{
    /// <summary>
    /// Let the scheduler pick the policy. This is the default, and for a cron trigger it means
    /// <see cref="FireAndProceed" />.
    /// </summary>
    /// <remarks>
    /// Spelled <c>SmartPolicy</c> in XML and JSON scheduling data.
    /// </remarks>
    SmartPolicy = MisfireInstruction.SmartPolicy,

    /// <summary>
    /// Never treat a missed firing as a misfire: fire every missed firing as soon as possible.
    /// </summary>
    /// <remarks>
    /// A trigger that missed many firings will fire that many times in rapid succession while it
    /// catches up.
    /// <para>Spelled <c>IgnoreMisfirePolicy</c> in XML and JSON scheduling data.</para>
    /// </remarks>
    IgnoreMisfires = MisfireInstruction.IgnoreMisfirePolicy,

    /// <summary>
    /// Fire once now, then resume the schedule.
    /// </summary>
    /// <remarks>
    /// Spelled <c>FireOnceNow</c> in XML and JSON scheduling data.
    /// </remarks>
    FireAndProceed = MisfireInstruction.CronTrigger.FireOnceNow,

    /// <summary>
    /// Skip the missed firings and resume at the next scheduled time.
    /// </summary>
    /// <remarks>
    /// Spelled <c>DoNothing</c> in XML and JSON scheduling data.
    /// </remarks>
    DoNothing = MisfireInstruction.CronTrigger.DoNothing,
}
