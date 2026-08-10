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

using Quartz.Util;

namespace Quartz;

/// <summary>
/// The schedule families a trigger can belong to. A misfire instruction is only meaningful
/// within one of them: the same number means a different policy in each.
/// </summary>
internal enum TriggerFamily
{
    Simple,
    Cron,
    CalendarInterval,
    DailyTimeInterval,
    Recurrence,
}

internal static class TriggerFamilyExtensions
{
    /// <summary>
    /// The family the given trigger belongs to, or <see langword="null" /> when it belongs to
    /// none of the built-in ones.
    /// </summary>
    internal static TriggerFamily? Family(this ITrigger trigger)
    {
        return trigger switch
        {
            ISimpleTrigger => TriggerFamily.Simple,
            ICronTrigger => TriggerFamily.Cron,
            ICalendarIntervalTrigger => TriggerFamily.CalendarInterval,
            IDailyTimeIntervalTrigger => TriggerFamily.DailyTimeInterval,
            IRecurrenceTrigger => TriggerFamily.Recurrence,
            _ => null
        };
    }

    /// <summary>
    /// Rejects an update whose misfire instruction was given in a family other than the stored
    /// trigger's. A code that is in range for two families means a different policy in each, so
    /// <see cref="Quartz.Impl.Triggers.AbstractTrigger" />'s range check lets the wrong one through
    /// silently; only the update object knows which family the caller meant.
    /// </summary>
    /// <exception cref="JobPersistenceException">The families disagree.</exception>
    internal static void EnsureMisfireInstructionMatchesFamily(this TriggerDetailsUpdate update, ITrigger trigger, TriggerKey triggerKey)
    {
        if (!update.HasMisfireInstruction || update.MisfireInstructionFamily is not TriggerFamily requested)
        {
            return;
        }

        TriggerFamily? actual = trigger.Family();
        if (actual == requested)
        {
            return;
        }

        string actualName = actual is TriggerFamily family ? family.DisplayName() : trigger.GetType().Name;
        Throw.JobPersistenceException(
            $"Misfire instruction {update.MisfireInstructionCode} was given for a {requested.DisplayName()} trigger, but '{triggerKey}' is a {actualName} trigger. "
            + $"The same code means a different policy in each family; use the {actualName} overload of WithMisfireInstruction, or WithMisfireInstructionCode if the code is already the stored one.");
    }

    /// <summary>
    /// The name of the family as it reads in an error message: the interface a caller would cast to.
    /// </summary>
    internal static string DisplayName(this TriggerFamily family)
    {
        return family switch
        {
            TriggerFamily.Simple => "simple",
            TriggerFamily.Cron => "cron",
            TriggerFamily.CalendarInterval => "calendar interval",
            TriggerFamily.DailyTimeInterval => "daily time interval",
            TriggerFamily.Recurrence => "recurrence",
            _ => family.ToString()
        };
    }
}
