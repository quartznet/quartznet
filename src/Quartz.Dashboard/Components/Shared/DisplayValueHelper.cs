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

namespace Quartz.Dashboard.Components.Shared;

/// <summary>
/// Formatting the dashboard's pages share.
/// </summary>
/// <remarks>
/// This used to read values out of whatever the API client returned by walking JSON properties and
/// reflecting over properties, because the client's trigger, calendar and job-data members were
/// untyped. They are <see cref="ITrigger" />, <see cref="ICalendar" /> and <see cref="JobDataMap" />
/// now, so the pages read them as properties and none of that is needed.
/// </remarks>
internal static class DisplayValueHelper
{
    public static string FormatKey(string? group, string? name)
    {
        string safeGroup = string.IsNullOrWhiteSpace(group) ? "DEFAULT" : group;
        string safeName = string.IsNullOrWhiteSpace(name) ? "(unknown)" : name;
        return safeGroup + "." + safeName;
    }

    /// <summary>
    /// A key cut into the pieces a listing may break it between, which together are the key exactly: after a
    /// separator (<c>.</c>, <c>_</c>, <c>-</c>, <c>/</c> or a space), so it stays on the line it ends; before an
    /// upper-case letter that follows a lower-case letter or a digit; and before the last capital of an upper-case
    /// run that a lower-case letter follows, so <c>XMLParserJob</c> is <c>XML</c>, <c>Parser</c>, <c>Job</c>.
    /// </summary>
    /// <remarks>
    /// A word longer than its column still breaks, wherever the line runs out: the stylesheet's
    /// <c>overflow-wrap: anywhere</c> is the last resort, after these.
    /// </remarks>
    public static List<string> KeyLineSegments(string key)
    {
        List<string> segments = [];
        int start = 0;
        for (int i = 1; i < key.Length; i++)
        {
            char previous = key[i - 1];
            char current = key[i];
            bool afterSeparator = previous is '.' or '_' or '-' or '/' or ' ';
            bool wordStart = char.IsUpper(current)
                             && (char.IsLower(previous)
                                 || char.IsDigit(previous)
                                 || (char.IsUpper(previous) && i + 1 < key.Length && char.IsLower(key[i + 1])));

            if (afterSeparator || wordStart)
            {
                segments.Add(key[start..i]);
                start = i;
            }
        }

        segments.Add(key[start..]);
        return segments;
    }

    /// <summary>
    /// How many occurrences in a row have failed for good, as the Jobs and Triggers listings badge it:
    /// <c>failing ×3</c>.
    /// </summary>
    public static string FormatFailing(int consecutiveFailures)
    {
        return "failing ×" + consecutiveFailures.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The accessible name of a failing badge that links to the failures: the badge's own text first, as a
    /// speech user says it, then whose runs failed and where the link goes.
    /// </summary>
    /// <param name="consecutiveFailures">How many occurrences in a row have failed for good.</param>
    /// <param name="key">The job's or trigger's key, <c>group.name</c>.</param>
    public static string FormatFailingLinkLabel(int consecutiveFailures, string key)
    {
        string runs = consecutiveFailures == 1
            ? "the last run of " + key + " failed. Open it in History"
            : "the last " + consecutiveFailures.ToString(CultureInfo.InvariantCulture) + " runs of " + key + " failed. Open them in History";
        return FormatFailing(consecutiveFailures) + ": " + runs;
    }

    /// <summary>
    /// How long a run took, as the Triggers listing shows it beside its result: <see cref="FormatDuration" />, except
    /// that a run shorter than a millisecond is <c>&lt; 1 ms</c>, whose fraction says nothing to read at a glance.
    /// </summary>
    /// <remarks>
    /// <see cref="FormatDuration" /> keeps the fraction: the run statistics chart labels its axis and its
    /// percentiles with it, and the History page's rows and cards agree with the chart beside them.
    /// </remarks>
    public static string FormatRunDuration(TimeSpan duration)
    {
        return duration > TimeSpan.Zero && duration < TimeSpan.FromMilliseconds(1) ? "< 1 ms" : FormatDuration(duration);
    }

    /// <summary>
    /// A duration in the largest unit that keeps it readable: sub-second runs in milliseconds, anything up to
    /// a minute in seconds, anything longer spelled out; <c>n/a</c> for none.
    /// </summary>
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return "n/a";
        }

        if (duration < TimeSpan.FromSeconds(1))
        {
            return duration.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture) + " ms";
        }

        if (duration < TimeSpan.FromMinutes(1))
        {
            return duration.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture) + " s";
        }

        return duration.ToString("g", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A duration as a clock, <c>hh:mm:ss</c>, with its days in front from a day up:
    /// <c>1.02:00:00</c> for 26 hours.
    /// </summary>
    /// <remarks>
    /// The format <c>hh\:mm\:ss</c> alone writes the hours component, so a 26-hour run read as
    /// <c>02:00:00</c>.
    /// </remarks>
    public static string FormatClock(TimeSpan duration)
    {
        string format = duration.Duration() < TimeSpan.FromDays(1) ? @"hh\:mm\:ss" : @"d\.hh\:mm\:ss";
        return duration.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// How long from <paramref name="now" /> until <paramref name="fireTime" />, to the largest whole unit:
    /// <c>in 12h</c>, and <c>in under a minute</c> for anything closer, a fire time already due included.
    /// </summary>
    public static string FormatUntil(DateTimeOffset fireTime, DateTimeOffset now)
    {
        TimeSpan until = fireTime - now;
        if (until < TimeSpan.FromMinutes(1))
        {
            return "in under a minute";
        }

        if (until < TimeSpan.FromHours(1))
        {
            return string.Create(CultureInfo.InvariantCulture, $"in {until.Minutes}m");
        }

        if (until < TimeSpan.FromDays(1))
        {
            return string.Create(CultureInfo.InvariantCulture, $"in {until.Hours}h");
        }

        return string.Create(CultureInfo.InvariantCulture, $"in {until.Days}d");
    }

    /// <summary>
    /// How long before <paramref name="now" /> <paramref name="time" /> was, to the largest whole unit:
    /// <c>12h ago</c>, and <c>under a minute ago</c> for anything closer, a time still ahead included.
    /// </summary>
    public static string FormatSince(DateTimeOffset time, DateTimeOffset now)
    {
        TimeSpan since = now - time;
        if (since < TimeSpan.FromMinutes(1))
        {
            return "under a minute ago";
        }

        if (since < TimeSpan.FromHours(1))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{since.Minutes}m ago");
        }

        if (since < TimeSpan.FromDays(1))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{since.Hours}h ago");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{since.Days}d ago");
    }

    /// <summary>
    /// The outcomes a continuation waits for, in words: <c>Success</c>, <c>Failure or cancellation</c>,
    /// <c>Any outcome</c>.
    /// </summary>
    /// <remarks>
    /// A flags enum rendered by <see cref="Enum.ToString()" /> reads as <c>OnFailure, OnCancellation</c>,
    /// which is a value rather than a sentence. A condition carrying a flag this build does not know —
    /// a row written by a newer node — is shown as it came rather than as an empty cell.
    /// </remarks>
    public static string FormatContinuationCondition(ContinuationCondition condition)
    {
        if (condition == ContinuationCondition.OnAnyOutcome)
        {
            return "Any outcome";
        }

        List<string> outcomes = [];
        if (condition.HasFlag(ContinuationCondition.OnSuccess))
        {
            outcomes.Add("success");
        }

        if (condition.HasFlag(ContinuationCondition.OnFailure))
        {
            outcomes.Add("failure");
        }

        if (condition.HasFlag(ContinuationCondition.OnCancellation))
        {
            outcomes.Add("cancellation");
        }

        if (condition.HasFlag(ContinuationCondition.OnVeto))
        {
            outcomes.Add("veto");
        }

        if (outcomes.Count == 0)
        {
            return condition.ToString();
        }

        string spelled = string.Join(" or ", outcomes);
        return char.ToUpperInvariant(spelled[0]) + spelled[1..];
    }
}
