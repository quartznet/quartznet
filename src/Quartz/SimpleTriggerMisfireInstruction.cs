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
/// What an <see cref="ISimpleTrigger" /> should do when it misses a firing.
/// </summary>
/// <remarks>
/// The values match the <see cref="MisfireInstruction" /> constants a trigger stores in
/// <see cref="ITrigger.MisfireInstructionCode" />, which is family-agnostic and therefore still an
/// <see cref="int" />.
/// </remarks>
/// <seealso cref="SimpleScheduleBuilder.WithMisfireInstruction" />
public enum SimpleTriggerMisfireInstruction
{
    /// <summary>
    /// Let the scheduler pick the policy, based on the trigger's repeat count and interval.
    /// This is the default.
    /// </summary>
    /// <remarks>
    /// Spelled <c>SmartPolicy</c> in XML and JSON scheduling data.
    /// </remarks>
    SmartPolicy = MisfireInstruction.SmartPolicy,

    /// <summary>
    /// Never treat a missed firing as a misfire: fire as soon as possible and carry on as if the
    /// firing had happened on time.
    /// </summary>
    /// <remarks>
    /// A trigger that missed many firings will fire that many times in rapid succession while it
    /// catches up.
    /// <para>Spelled <c>IgnoreMisfirePolicy</c> in XML and JSON scheduling data.</para>
    /// </remarks>
    IgnoreMisfires = MisfireInstruction.IgnoreMisfirePolicy,

    /// <summary>
    /// Fire now.
    /// </summary>
    /// <remarks>
    /// Intended for one-shot (non-repeating) triggers. On a repeating trigger this behaves like
    /// <see cref="NowWithRemainingCount" />.
    /// <para>Spelled <c>FireNow</c> in XML and JSON scheduling data.</para>
    /// </remarks>
    FireNow = MisfireInstruction.SimpleTrigger.FireNow,

    /// <summary>
    /// Reschedule to now, keeping the repeat count as it stands.
    /// </summary>
    /// <remarks>
    /// The trigger forgets the start time and repeat count it was originally set up with. The
    /// trigger's end time is still honored, so a trigger whose end time has passed will not fire.
    /// <para>Spelled <c>RescheduleNowWithExistingRepeatCount</c> in XML and JSON scheduling data.</para>
    /// </remarks>
    NowWithExistingCount = MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount,

    /// <summary>
    /// Reschedule to now, with the repeat count set to what it would have been had nothing been
    /// missed.
    /// </summary>
    /// <remarks>
    /// The trigger forgets the start time and repeat count it was originally set up with. If every
    /// remaining firing was missed, the trigger completes after firing now.
    /// <para>Spelled <c>RescheduleNowWithRemainingRepeatCount</c> in XML and JSON scheduling data.</para>
    /// </remarks>
    NowWithRemainingCount = MisfireInstruction.SimpleTrigger.RescheduleNowWithRemainingRepeatCount,

    /// <summary>
    /// Reschedule to the next scheduled time after now, with the repeat count set to what it would
    /// have been had nothing been missed.
    /// </summary>
    /// <remarks>
    /// If every firing was missed, the trigger goes straight to completed.
    /// <para>Spelled <c>RescheduleNextWithRemainingCount</c> in XML and JSON scheduling data.</para>
    /// </remarks>
    NextWithRemainingCount = MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount,

    /// <summary>
    /// Reschedule to the next scheduled time after now, keeping the repeat count as it stands.
    /// </summary>
    /// <remarks>
    /// Spelled <c>RescheduleNextWithExistingCount</c> in XML and JSON scheduling data.
    /// </remarks>
    NextWithExistingCount = MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount,
}
