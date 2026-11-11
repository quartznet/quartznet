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

/// <summary>Which trigger commands the visitor may use. The row draws a menu only when <see cref="Any"/> is set.</summary>
internal sealed record TimelineTriggerMenu(bool Run, bool Pause, bool Resume, bool Reset, bool Unschedule)
{
    public bool Any => Run || Pause || Resume || Reset || Unschedule;
}

/// <summary>What <see cref="Services.Timeline.TimelineService"/> returns after assembling rows from the API.</summary>
internal sealed record TimelineSnapshot(
    IReadOnlyList<TimelineRow> Rows,
    HashSet<TimelineEntityKey> ExecutingTriggerKeys,
    bool HistoryUnavailable,
    bool HistoryHasMore = false);
