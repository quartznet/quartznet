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

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Quartz.Impl.Triggers;

namespace Quartz.Mcp.Impl;

/// <summary>
/// How the tools write what a scheduler's users wrote: cut to a length, saying so, and never more than that.
/// </summary>
internal static class McpText
{
    /// <summary>
    /// <paramref name="text" /> cut to <paramref name="max" /> characters, and whether it was.
    /// </summary>
    public static (string? Text, bool? Truncated) Cut(string? text, int max)
    {
        if (text is null || text.Length <= max)
        {
            return (text, null);
        }

        return (text[..max], true);
    }

    /// <summary>
    /// The keys of a data map, sorted: what a tool shows of job and trigger data, and all it shows.
    /// </summary>
    public static string[] DataKeys(JobDataMap? map)
    {
        if (map is null || map.Count == 0)
        {
            return [];
        }

        string[] keys = [.. map.Keys];
        Array.Sort(keys, StringComparer.Ordinal);
        return keys;
    }

    public static PauseRow? Pause(PauseInfo? pause)
    {
        if (pause is null)
        {
            return null;
        }

        (string? reason, bool? truncated) = Cut(pause.Reason, McpLimits.MaxReasonLength);
        return new PauseRow(pause.RequestedBy, reason, pause.PausedAtUtc) { ReasonTruncated = truncated };
    }

    /// <summary>
    /// The kind of a trigger, in the words a schedule is spoken of in.
    /// </summary>
    public static string Kind(ITrigger trigger) => trigger switch
    {
        ICronTrigger => "cron",
        ISimpleTrigger => "simple",
        IDailyTimeIntervalTrigger => "dailyTimeInterval",
        ICalendarIntervalTrigger => "calendarInterval",
        IRecurrenceTrigger => "recurrence",
        _ => "custom",
    };

    /// <summary>The time zone a trigger's schedule is read in, for the kinds that have one.</summary>
    public static string? TimeZone(ITrigger trigger) => trigger switch
    {
        ICronTrigger cron => cron.TimeZone.Id,
        IDailyTimeIntervalTrigger daily => daily.TimeZone.Id,
        ICalendarIntervalTrigger calendar => calendar.TimeZone.Id,
        _ => null,
    };

    /// <summary>
    /// The trigger's schedule in words, or <see langword="null" /> for a kind that is not described; never throws.
    /// </summary>
    public static string? Schedule(ITrigger trigger)
    {
        try
        {
            return ScheduleDescriber.Describe(trigger);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    public static string? Expression(ITrigger trigger)
    {
        try
        {
            return ScheduleDescriber.Expression(trigger);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// The trigger's misfire instruction by its name in <see cref="MisfireInstruction" />.
    /// </summary>
    public static string MisfireInstructionName(ITrigger trigger)
    {
        int code = trigger.MisfireInstructionCode;
        if (code == MisfireInstruction.IgnoreMisfirePolicy)
        {
            return "IgnoreMisfirePolicy";
        }

        if (code == MisfireInstruction.SmartPolicy)
        {
            return "SmartPolicy";
        }

        return trigger switch
        {
            ISimpleTrigger => code switch
            {
                MisfireInstruction.SimpleTrigger.FireNow => "FireNow",
                MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount => "RescheduleNowWithExistingRepeatCount",
                MisfireInstruction.SimpleTrigger.RescheduleNowWithRemainingRepeatCount => "RescheduleNowWithRemainingRepeatCount",
                MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount => "RescheduleNextWithRemainingCount",
                MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount => "RescheduleNextWithExistingCount",
                _ => code.ToString(CultureInfo.InvariantCulture),
            },
            _ => code switch
            {
                MisfireInstruction.CronTrigger.FireOnceNow => "FireOnceNow",
                MisfireInstruction.CronTrigger.DoNothing => "DoNothing",
                _ => code.ToString(CultureInfo.InvariantCulture),
            },
        };
    }

    /// <summary>
    /// What resuming does about the fire times a trigger missed while it was paused, in words.
    /// </summary>
    public static string MisfireOnResume(ITrigger trigger)
    {
        string name = MisfireInstructionName(trigger);
        string effect = name switch
        {
            "IgnoreMisfirePolicy" => "every fire time missed while paused fires now, one after another",
            "FireOnceNow" or "FireNow" => "it fires once now if it missed a fire time while paused, then follows its schedule",
            "DoNothing" => "fire times missed while paused are skipped, and it fires next at its next scheduled time",
            "SmartPolicy" when trigger is ISimpleTrigger => "a fire time missed while paused is handled by the simple trigger's smart policy, which depends on its repeat count",
            "SmartPolicy" => "it fires once now if it missed a fire time while paused, then follows its schedule",
            _ => "fire times missed while paused are handled by its misfire instruction",
        };

        return $"Resuming applies its misfire instruction, {name}: {effect}, once the time missed exceeds the scheduler's misfire threshold.";
    }

    /// <summary>
    /// A one-way digest of the parts of a trigger a confirmed change was previewed against: its kind, schedule,
    /// time zone, state, next fire time, job and calendar.
    /// </summary>
    public static string StateDigest(ITrigger trigger, TriggerState state)
    {
        string expression = trigger switch
        {
            ISimpleTrigger simple => simple.RepeatInterval.ToString("c", CultureInfo.InvariantCulture) + "x" + simple.RepeatCount.ToString(CultureInfo.InvariantCulture),
            _ => Expression(trigger) ?? "",
        };

        string text = string.Join(
            '\n',
            Kind(trigger),
            expression,
            Schedule(trigger) ?? "",
            TimeZone(trigger) ?? "",
            state.ToString(),
            trigger.NextFireTimeUtc?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? "",
            trigger.JobKey.ToString(),
            trigger.CalendarName ?? "",
            trigger.EndTimeUtc?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? "");

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    /// <summary>An instant as the previews write it.</summary>
    public static string Utc(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }
}
