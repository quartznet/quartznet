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
/// What an <see cref="IRecurrenceTrigger" /> should do when it misses a firing.
/// </summary>
/// <remarks>
/// Each value is the number a trigger stores in <see cref="ITrigger.MisfireInstructionCode" />, which
/// is family-agnostic and therefore still an <see cref="int" />. Casting between the two is
/// deliberate and safe.
/// </remarks>
/// <seealso cref="RecurrenceScheduleBuilder.WithMisfireInstruction" />
public enum RecurrenceTriggerMisfireInstruction
{
    /// <summary>
    /// Let the scheduler pick the policy. This is the default, and for a recurrence trigger it
    /// means <see cref="FireAndProceed" />.
    /// </summary>
    /// <remarks>
    /// Named <c>SmartPolicy</c> in the misfire vocabulary. Recurrence triggers have no XML or JSON
    /// scheduling-data form, so the name only ever appears in code.
    /// </remarks>
    SmartPolicy = MisfireInstruction.SmartPolicy,

    /// <summary>
    /// Never treat a missed firing as a misfire: fire every missed firing as soon as possible.
    /// </summary>
    /// <remarks>
    /// A trigger that missed many firings will fire that many times in rapid succession while it
    /// catches up.
    /// <para>Named <c>IgnoreMisfirePolicy</c> in the misfire vocabulary.</para>
    /// </remarks>
    IgnoreMisfires = MisfireInstruction.IgnoreMisfirePolicy,

    /// <summary>
    /// Fire once now, then resume the schedule.
    /// </summary>
    /// <remarks>
    /// Named <c>FireOnceNow</c> in the misfire vocabulary.
    /// </remarks>
    FireAndProceed = MisfireInstruction.RecurrenceTrigger.FireOnceNow,

    /// <summary>
    /// Skip the missed firings and resume at the next scheduled time.
    /// </summary>
    /// <remarks>
    /// Named <c>DoNothing</c> in the misfire vocabulary.
    /// </remarks>
    DoNothing = MisfireInstruction.RecurrenceTrigger.DoNothing,
}
