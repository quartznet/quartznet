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

using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;
using Quartz.Util;

namespace Quartz;

/// <summary>
/// Resolves the misfire instruction names that appear in XML and JSON scheduling data into the
/// codes a trigger stores, one map per trigger family.
/// </summary>
/// <remarks>
/// <para>
/// This replaces a reflection sweep over <see cref="MisfireInstruction" /> and every one of its
/// nested types, which resolved a name from <em>any</em> family for a trigger of <em>any</em>
/// family: a cron trigger asking for <c>RescheduleNowWithExistingRepeatCount</c> silently became
/// <c>DoNothing</c>, because both are 2. The reflection also never saw the calendar-interval names
/// in the XML processor, which passed only two of the nested types.
/// </para>
/// <para>
/// Each family accepts its own names, the family-agnostic root names, and the names of the enum
/// members a caller would write in C#. A name belonging to another family still resolves when its
/// value is legal for this one — that is what used to happen, and rejecting it outright would break
/// working configuration — but it is logged as the wrong spelling it is, naming the policy the value
/// actually selects and the name to write instead.
/// </para>
/// </remarks>
internal static class MisfireInstructionNames
{
    private const int SmartPolicy = MisfireInstruction.SmartPolicy;
    private const int IgnoreMisfires = MisfireInstruction.IgnoreMisfirePolicy;

    /// <summary>Names every family understands, whatever it is called in.</summary>
    private static readonly (string Name, int Value)[] rootNames =
    [
        ("SmartPolicy", SmartPolicy),
        ("InstructionNotSet", SmartPolicy),
        ("IgnoreMisfirePolicy", IgnoreMisfires),
        ("IgnoreMisfires", IgnoreMisfires)
    ];

    private static readonly Dictionary<string, int> simpleNames = Build(
        ("FireNow", MisfireInstruction.SimpleTrigger.FireNow),
        ("RescheduleNowWithExistingRepeatCount", MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount),
        ("RescheduleNowWithRemainingRepeatCount", MisfireInstruction.SimpleTrigger.RescheduleNowWithRemainingRepeatCount),
        ("RescheduleNextWithRemainingCount", MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount),
        ("RescheduleNextWithExistingCount", MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount),
        // the enum spellings too, so C# and configuration can be read side by side
        ("NowWithExistingCount", MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount),
        ("NowWithRemainingCount", MisfireInstruction.SimpleTrigger.RescheduleNowWithRemainingRepeatCount),
        ("NextWithRemainingCount", MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount),
        ("NextWithExistingCount", MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount));

    private static readonly Dictionary<string, int> cronNames = Build(
        ("FireOnceNow", MisfireInstruction.CronTrigger.FireOnceNow),
        ("DoNothing", MisfireInstruction.CronTrigger.DoNothing),
        ("FireAndProceed", MisfireInstruction.CronTrigger.FireOnceNow));

    private static readonly Dictionary<string, int> calendarIntervalNames = Build(
        ("FireOnceNow", MisfireInstruction.CalendarIntervalTrigger.FireOnceNow),
        ("DoNothing", MisfireInstruction.CalendarIntervalTrigger.DoNothing),
        ("FireAndProceed", MisfireInstruction.CalendarIntervalTrigger.FireOnceNow));

    private static readonly Dictionary<string, int> dailyTimeIntervalNames = Build(
        ("FireOnceNow", MisfireInstruction.DailyTimeIntervalTrigger.FireOnceNow),
        ("DoNothing", MisfireInstruction.DailyTimeIntervalTrigger.DoNothing),
        ("FireAndProceed", MisfireInstruction.DailyTimeIntervalTrigger.FireOnceNow));

    private static readonly Dictionary<string, int> recurrenceNames = Build(
        ("FireOnceNow", MisfireInstruction.RecurrenceTrigger.FireOnceNow),
        ("DoNothing", MisfireInstruction.RecurrenceTrigger.DoNothing),
        ("FireAndProceed", MisfireInstruction.RecurrenceTrigger.FireOnceNow));

    /// <summary>
    /// Every name any family knows, used to tell "spelled for the wrong family" apart from "not a
    /// misfire instruction at all".
    /// </summary>
    private static readonly Dictionary<string, int> allNames = BuildAll();

    private static Dictionary<string, int> Build(params (string Name, int Value)[] familyNames)
    {
        Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, int value) in rootNames)
        {
            map[name] = value;
        }

        foreach ((string name, int value) in familyNames)
        {
            map[name] = value;
        }

        return map;
    }

    private static Dictionary<string, int> BuildAll()
    {
        Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Dictionary<string, int> family in new[] { simpleNames, cronNames, calendarIntervalNames, dailyTimeIntervalNames, recurrenceNames })
        {
            foreach (KeyValuePair<string, int> entry in family)
            {
                map[entry.Key] = entry.Value;
            }
        }

        return map;
    }

    private static Dictionary<string, int> NamesFor(TriggerFamily family)
    {
        return family switch
        {
            TriggerFamily.Simple => simpleNames,
            TriggerFamily.Cron => cronNames,
            TriggerFamily.CalendarInterval => calendarIntervalNames,
            TriggerFamily.DailyTimeInterval => dailyTimeIntervalNames,
            _ => recurrenceNames
        };
    }

    /// <summary>
    /// Resolves the misfire instruction <paramref name="name" /> the way the given family spells it.
    /// </summary>
    /// <exception cref="SchedulerConfigException">
    /// The name is not a misfire instruction, or names a policy this family has no code for.
    /// </exception>
    internal static int Resolve(TriggerFamily family, string name, ILogger? logger = null)
    {
        string trimmed = name.Trim();
        Dictionary<string, int> names = NamesFor(family);

        if (names.TryGetValue(trimmed, out int value))
        {
            return value;
        }

        // A name from another family. It resolved before these maps existed, so it still resolves -
        // but say what it became, because that is the part nobody could see.
        if (allNames.TryGetValue(trimmed, out int otherFamilyValue))
        {
            if (IsValidFor(family, otherFamilyValue))
            {
                ILogger log = logger ?? LogProvider.CreateLogger(typeof(MisfireInstructionNames).FullName!);
                log.LogWarning(
                    "Misfire instruction '{MisfireInstruction}' is not one of the {Family} trigger names. It resolves to code {Code}, which for this trigger means {Policy}; spell it '{Canonical}'",
                    trimmed,
                    family.DisplayName(),
                    otherFamilyValue,
                    PolicyName(family, otherFamilyValue),
                    CanonicalName(family, otherFamilyValue));

                return otherFamilyValue;
            }

            Throw.SchedulerConfigException(
                $"Misfire instruction '{trimmed}' belongs to another trigger family, and its code {otherFamilyValue} is not valid for a {family.DisplayName()} trigger. Valid names: {ValidNames(family)}.");
        }

        Throw.SchedulerConfigException(
            $"Unknown misfire instruction: '{trimmed}'. Valid names for a {family.DisplayName()} trigger: {ValidNames(family)}.");
        return 0;
    }

    private static bool IsValidFor(TriggerFamily family, int value)
    {
        if (value is SmartPolicy or IgnoreMisfires)
        {
            return true;
        }

        int max = family == TriggerFamily.Simple
            ? MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount
            : MisfireInstruction.CronTrigger.DoNothing;

        return value >= MisfireInstruction.SimpleTrigger.FireNow && value <= max;
    }

    /// <summary>What this family calls the code in C#.</summary>
    private static string PolicyName(TriggerFamily family, int value)
    {
        return family switch
        {
            TriggerFamily.Simple => nameof(SimpleTriggerMisfireInstruction) + "." + (SimpleTriggerMisfireInstruction) value,
            TriggerFamily.Cron => nameof(CronTriggerMisfireInstruction) + "." + (CronTriggerMisfireInstruction) value,
            TriggerFamily.CalendarInterval => nameof(CalendarIntervalTriggerMisfireInstruction) + "." + (CalendarIntervalTriggerMisfireInstruction) value,
            TriggerFamily.DailyTimeInterval => nameof(DailyTimeIntervalTriggerMisfireInstruction) + "." + (DailyTimeIntervalTriggerMisfireInstruction) value,
            _ => nameof(RecurrenceTriggerMisfireInstruction) + "." + (RecurrenceTriggerMisfireInstruction) value
        };
    }

    /// <summary>What this family calls the code in XML and JSON scheduling data.</summary>
    private static string CanonicalName(TriggerFamily family, int value)
    {
        if (value == IgnoreMisfires)
        {
            return "IgnoreMisfirePolicy";
        }

        if (value == SmartPolicy)
        {
            return "SmartPolicy";
        }

        if (family != TriggerFamily.Simple)
        {
            return value == MisfireInstruction.CronTrigger.DoNothing ? "DoNothing" : "FireOnceNow";
        }

        return value switch
        {
            MisfireInstruction.SimpleTrigger.FireNow => "FireNow",
            MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount => "RescheduleNowWithExistingRepeatCount",
            MisfireInstruction.SimpleTrigger.RescheduleNowWithRemainingRepeatCount => "RescheduleNowWithRemainingRepeatCount",
            MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount => "RescheduleNextWithRemainingCount",
            _ => "RescheduleNextWithExistingCount"
        };
    }

    private static string ValidNames(TriggerFamily family) => string.Join(", ", NamesFor(family).Keys.Order(StringComparer.Ordinal));
}
