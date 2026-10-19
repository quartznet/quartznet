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

using Quartz.Extensibility;
using Quartz.Impl.Triggers;

namespace Quartz.Core;

/// <summary>
/// Maps a scheduler's <see cref="DefaultMisfireHandling" /> onto a trigger's own family, and gives it to
/// a trigger whose instruction nobody stated.
/// </summary>
/// <remarks>
/// The table is <see cref="DefaultMisfireHandling" />'s, and the reasons for the two simple-trigger
/// rows are there.
/// </remarks>
internal static class MisfireDefaults
{
    /// <summary>
    /// The instruction code <paramref name="handling" /> means to <paramref name="trigger" />'s family, or
    /// <see langword="null" /> for a trigger in none of the five.
    /// </summary>
    internal static int? CodeFor(ITrigger trigger, DefaultMisfireHandling handling)
    {
        if (handling == DefaultMisfireHandling.SmartPolicy)
        {
            return MisfireInstruction.SmartPolicy;
        }

        if (handling == DefaultMisfireHandling.IgnoreMisfires)
        {
            return IsBuiltIn(trigger) ? MisfireInstruction.IgnoreMisfirePolicy : null;
        }

        return (trigger, handling) switch
        {
            (SimpleTriggerImpl, DefaultMisfireHandling.FireAndProceed) => MisfireInstruction.SimpleTrigger.FireNow,
            (SimpleTriggerImpl, DefaultMisfireHandling.DoNothing) => MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount,
            (CronTriggerImpl, DefaultMisfireHandling.FireAndProceed) => MisfireInstruction.CronTrigger.FireOnceNow,
            (CronTriggerImpl, DefaultMisfireHandling.DoNothing) => MisfireInstruction.CronTrigger.DoNothing,
            (CalendarIntervalTriggerImpl, DefaultMisfireHandling.FireAndProceed) => MisfireInstruction.CalendarIntervalTrigger.FireOnceNow,
            (CalendarIntervalTriggerImpl, DefaultMisfireHandling.DoNothing) => MisfireInstruction.CalendarIntervalTrigger.DoNothing,
            (DailyTimeIntervalTriggerImpl, DefaultMisfireHandling.FireAndProceed) => MisfireInstruction.DailyTimeIntervalTrigger.FireOnceNow,
            (DailyTimeIntervalTriggerImpl, DefaultMisfireHandling.DoNothing) => MisfireInstruction.DailyTimeIntervalTrigger.DoNothing,
            (RecurrenceTriggerImpl, DefaultMisfireHandling.FireAndProceed) => MisfireInstruction.RecurrenceTrigger.FireOnceNow,
            (RecurrenceTriggerImpl, DefaultMisfireHandling.DoNothing) => MisfireInstruction.RecurrenceTrigger.DoNothing,
            _ => null
        };
    }

    /// <summary>
    /// Gives <paramref name="trigger" /> the mapping of <paramref name="handling" /> when its instruction
    /// was never stated, which states it.
    /// </summary>
    /// <remarks>
    /// <see cref="DefaultMisfireHandling.SmartPolicy" /> changes nothing: an unstated instruction already
    /// is the smart policy. A trigger outside <see cref="TriggerBase" /> cannot say whether its instruction
    /// was stated, and one in none of the five families has no mapping, so both are left alone.
    /// </remarks>
    internal static void Apply(IMutableTrigger trigger, DefaultMisfireHandling handling)
    {
        if (handling == DefaultMisfireHandling.SmartPolicy
            || trigger is not TriggerBase { MisfireInstructionStated: false }
            || CodeFor(trigger, handling) is not { } code)
        {
            return;
        }

        trigger.MisfireInstructionCode = code;
    }

    private static bool IsBuiltIn(ITrigger trigger)
    {
        return trigger is SimpleTriggerImpl
            or CronTriggerImpl
            or CalendarIntervalTriggerImpl
            or DailyTimeIntervalTriggerImpl
            or RecurrenceTriggerImpl;
    }
}
