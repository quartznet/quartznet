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

#pragma warning disable CS1591

using System.Globalization;
using System.Runtime.InteropServices;

namespace Quartz.Dashboard.Models;

/// <summary>Identity key for a trigger (or job row): (Group, Name) with ordinal string equality.</summary>
internal readonly record struct TimelineEntityKey(string Group, string Name);

/// <summary>The drawn axis: the window the visitor asked for, and the right edge which may look past now.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct TimelineBounds(DateTimeOffset Start, DateTimeOffset End, DateTimeOffset AxisEnd)
{
    public double SpanTicks => Math.Max(1, (AxisEnd - Start).Ticks);

    public double NowFraction => Math.Clamp((End - Start).Ticks / SpanTicks, 0, 1);

    /// <summary>
    /// CSS vars so trigger tracks paint one vertical rule per clock minute across the drawn axis.
    /// </summary>
    public string MinuteGridStyle
    {
        get
        {
            double axisMs = Math.Max(1, (AxisEnd - Start).TotalMilliseconds);
            double minutePct = TimeSpan.FromMinutes(1).TotalMilliseconds / axisMs * 100.0;

            DateTimeOffset truncated = new(
                Start.Year,
                Start.Month,
                Start.Day,
                Start.Hour,
                Start.Minute,
                0,
                Start.Offset);
            DateTimeOffset firstMinute = truncated <= Start ? truncated.AddMinutes(1) : truncated;
            double offsetPct = firstMinute >= AxisEnd
                ? 0
                : (firstMinute - Start).TotalMilliseconds / axisMs * 100.0;

            return "--qz-timeline-minute-pct: "
                   + minutePct.ToString("0.####", CultureInfo.InvariantCulture) + "%; "
                   + "--qz-timeline-minute-offset: "
                   + offsetPct.ToString("0.####", CultureInfo.InvariantCulture) + "%;";
        }
    }
}

internal sealed record TimelineWindow(string Key, string Label, string ShortLabel, TimeSpan Span, int TickCount, string TickFormat)
{
    public static readonly TimelineWindow Last10Minutes = new("10m", "Last 10 minutes", "10 min", TimeSpan.FromMinutes(10), 5, "HH:mm");
    public static readonly TimelineWindow Last15Minutes = new("15m", "Last 15 minutes", "15 min", TimeSpan.FromMinutes(15), 5, "HH:mm");
    public static readonly TimelineWindow Last30Minutes = new("30m", "Last 30 minutes", "30 min", TimeSpan.FromMinutes(30), 6, "HH:mm");
    public static readonly TimelineWindow LastHour = new("1h", "Last hour", "1 hour", TimeSpan.FromHours(1), 6, "HH:mm");
    public static readonly TimelineWindow LastSixHours = new("6h", "Last 6 hours", "6 hours", TimeSpan.FromHours(6), 6, "HH:mm");
    public static readonly TimelineWindow LastDay = new("24h", "Last 24 hours", "24 hours", TimeSpan.FromHours(24), 8, "HH:mm");
    public static readonly TimelineWindow LastWeek = new("7d", "Last 7 days", "7 days", TimeSpan.FromDays(7), 7, "MM-dd");

    public static readonly TimelineWindow[] Offered =
    [
        Last10Minutes,
        Last15Minutes,
        Last30Minutes,
        LastHour,
        LastSixHours,
        LastDay,
        LastWeek
    ];

    public static TimelineWindow Parse(string? key)
    {
        string trimmed = key?.Trim() ?? string.Empty;
        foreach (TimelineWindow window in Offered)
        {
            if (string.Equals(window.Key, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return window;
            }
        }

        return Last15Minutes;
    }
}

internal sealed record TimelineTick(double Fraction, string Label);

internal sealed record TimelineBar(double Left, double Width, string CssClass, string Title, string? EntryId);

internal sealed record TimelineMark(DateTimeOffset Instant, string Title);

internal sealed record TimelineTriggerRow(
    string Group,
    string Name,
    TriggerState? State,
    string? ScheduleSummary,
    DateTimeOffset? NextFireTimeUtc,
    DateTimeOffset? PreviousFireTimeUtc,
    DateTimeOffset? StartTimeUtc,
    DateTimeOffset? EndTimeUtc,
    bool IsExecuting,
    List<TimelineBar> Bars,
    List<TimelineMark> Marks);

internal sealed record TimelineRow(
    TimelineEntityKey Key,
    string JobGroup,
    string JobName,
    Dictionary<TimelineEntityKey, PendingTriggerBars> BarsByTrigger,
    List<TimelineTriggerRow> Triggers)
{
    public bool TriggersLoaded { get; set; }

    /// <summary>
    /// The scheduler no longer holds this job; the row is history bars only (checked when hydrate
    /// finds no live triggers).
    /// </summary>
    public bool JobMissing { get; set; }
}

internal sealed record TimelineSection(string JobGroup, List<TimelineRow> Rows);

internal sealed record PendingTriggerBars(string Group, string Name, List<TimelineBar> Bars);

internal sealed class TimelineRowBuilder(TimelineEntityKey key, string jobGroup, string jobName)
{
    public TimelineEntityKey Key { get; } = key;

    public string JobGroup { get; } = jobGroup;

    public string JobName { get; } = jobName;

    public Dictionary<TimelineEntityKey, PendingTriggerBars> BarsByTrigger { get; } = new();
}

/// <summary>One trigger on the chart, so a menu action knows which row it belongs to.</summary>
internal sealed record TimelineTriggerRef(TimelineRow Row, TimelineTriggerRow Trigger);

/// <summary>One job row on the chart, so a menu action knows which job it belongs to.</summary>
internal sealed record TimelineJobRef(TimelineRow Row);

/// <summary>
/// Job-level status for the square mark (distinct from circular trigger dots): missing, paused when
/// every live trigger is paused, executing when any is firing, otherwise normal when live triggers exist.
/// </summary>
internal enum TimelineJobStatus
{
    None,
    Missing,
    Paused,
    Normal,
    Executing,
    Error,
}

internal static class TimelineJobStatusResolver
{
    public static TimelineJobStatus Resolve(TimelineRow row)
    {
        if (row.JobMissing)
        {
            return TimelineJobStatus.Missing;
        }

        if (!row.TriggersLoaded)
        {
            return TimelineJobStatus.None;
        }

        bool hasLive = false;
        bool anyScheduled = false;
        bool anyPaused = false;
        bool anyExecuting = false;
        bool anyError = false;
        foreach (TimelineTriggerRow trigger in row.Triggers)
        {
            if (trigger.IsExecuting || trigger.State == TriggerState.Executing)
            {
                hasLive = true;
                anyExecuting = true;
                continue;
            }

            if (trigger.State is null)
            {
                continue;
            }

            hasLive = true;
            switch (trigger.State)
            {
                case TriggerState.Normal:
                    anyScheduled = true;
                    break;
                case TriggerState.Paused:
                    anyPaused = true;
                    break;
                case TriggerState.Error:
                case TriggerState.Blocked:
                    anyError = true;
                    break;
                // Complete / None: still live headers, but not scheduled and not paused.
            }
        }

        if (!hasLive)
        {
            return TimelineJobStatus.None;
        }

        if (anyExecuting)
        {
            return TimelineJobStatus.Executing;
        }

        if (anyError)
        {
            return TimelineJobStatus.Error;
        }

        if (anyScheduled)
        {
            return TimelineJobStatus.Normal;
        }

        return anyPaused ? TimelineJobStatus.Paused : TimelineJobStatus.None;
    }

    public static string Title(TimelineJobStatus status) => status switch
    {
        TimelineJobStatus.Missing => "Deleted — history only",
        TimelineJobStatus.Paused => "Paused",
        TimelineJobStatus.Normal => "Normal",
        TimelineJobStatus.Executing => "Executing",
        TimelineJobStatus.Error => "Error",
        _ => string.Empty,
    };

    public static string CssModifier(TimelineJobStatus status) => status switch
    {
        TimelineJobStatus.Missing => "missing",
        TimelineJobStatus.Paused => "paused",
        TimelineJobStatus.Normal => "normal",
        TimelineJobStatus.Executing => "executing",
        TimelineJobStatus.Error => "error",
        _ => string.Empty,
    };
}

/// <summary>Which trigger commands the visitor may use. The row draws a menu only when <see cref="Any"/> is set.</summary>
internal sealed record TimelineTriggerMenu(bool Run, bool Pause, bool Resume, bool Reset, bool Unschedule)
{
    public bool Any => Run || Pause || Resume || Reset || Unschedule;
}

/// <summary>Which job commands the visitor may use. Pause XOR Resume follows live trigger states on the row.</summary>
internal sealed record TimelineJobMenu(bool Run, bool Pause, bool Resume, bool Delete)
{
    public bool Any => Run || Pause || Resume || Delete;

    /// <summary>
    /// Builds the menu: none when the job is missing; Pause when any live trigger is not paused,
    /// Resume when every live trigger is paused, neither when the row has only orphan bars.
    /// Run/Delete follow authorization alone for jobs that still exist.
    /// </summary>
    public static TimelineJobMenu For(
        IEnumerable<TimelineTriggerRow> triggers,
        bool canRun,
        bool canPause,
        bool canResume,
        bool canDelete,
        bool jobMissing = false)
    {
        if (jobMissing)
        {
            return new TimelineJobMenu(Run: false, Pause: false, Resume: false, Delete: false);
        }

        // Pause / Resume only follow Normal and Paused: Error / Blocked / Complete are not "unpaused
        // schedule" and must not flip the menu to Pause.
        bool hasPauseable = false;
        bool anyUnpaused = false;
        foreach (TimelineTriggerRow trigger in triggers)
        {
            if (trigger.State is not (TriggerState.Normal or TriggerState.Paused or TriggerState.Executing))
            {
                continue;
            }

            hasPauseable = true;
            if (trigger.State != TriggerState.Paused)
            {
                anyUnpaused = true;
            }
        }

        bool pause = canPause && hasPauseable && anyUnpaused;
        bool resume = canResume && hasPauseable && !anyUnpaused;
        return new TimelineJobMenu(canRun, pause, resume, canDelete);
    }
}

/// <summary>What <see cref="Services.Timeline.TimelineService"/> returns after assembling rows from the API.</summary>
internal sealed record TimelineSnapshot(
    IReadOnlyList<TimelineRow> Rows,
    HashSet<TimelineEntityKey> ExecutingTriggerKeys,
    bool HistoryUnavailable,
    bool HistoryHasMore = false);
