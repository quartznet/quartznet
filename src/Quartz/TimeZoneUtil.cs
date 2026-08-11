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

public static class TimeZoneUtil
{
    private static readonly Dictionary<string, string> timeZoneIdAliases = new Dictionary<string, string>();

    static TimeZoneUtil()
    {
        // Azure has had issues with having both formats
        timeZoneIdAliases["UTC"] = "Coordinated Universal Time";
        timeZoneIdAliases["Coordinated Universal Time"] = "UTC";

        // Mono differs in naming too...
        timeZoneIdAliases["Central European Standard Time"] = "CET";
        timeZoneIdAliases["CET"] = "Central European Standard Time";

        timeZoneIdAliases["Eastern Standard Time"] = "US/Eastern";
        timeZoneIdAliases["US/Eastern"] = "Eastern Standard Time";

        timeZoneIdAliases["Central Standard Time"] = "US/Central";
        timeZoneIdAliases["US/Central"] = "Central Standard Time";

        timeZoneIdAliases["US Central Standard Time"] = "US/Indiana-Stark";
        timeZoneIdAliases["US/Indiana-Stark"] = "US Central Standard Time";

        timeZoneIdAliases["Mountain Standard Time"] = "US/Mountain";
        timeZoneIdAliases["US/Mountain"] = "Mountain Standard Time";

        timeZoneIdAliases["US Mountain Standard Time"] = "US/Arizona";
        timeZoneIdAliases["US/Arizona"] = "US Mountain Standard Time";

        timeZoneIdAliases["Pacific Standard Time"] = "US/Pacific";
        timeZoneIdAliases["US/Pacific"] = "Pacific Standard Time";

        timeZoneIdAliases["Alaskan Standard Time"] = "US/Alaska";
        timeZoneIdAliases["US/Alaska"] = "Alaskan Standard Time";

        timeZoneIdAliases["Hawaiian Standard Time"] = "US/Hawaii";
        timeZoneIdAliases["US/Hawaii"] = "Hawaiian Standard Time";

        timeZoneIdAliases["China Standard Time"] = "Asia/Shanghai";
        timeZoneIdAliases["Asia/Shanghai"] = "China Standard Time";

        timeZoneIdAliases["Pakistan Standard Time"] = "Asia/Karachi";
        timeZoneIdAliases["Asia/Karachi"] = "Pakistan Standard Time";
    }

    /// <summary>
    /// A last-resort resolver consulted when a time zone id is neither a system id nor one of the
    /// aliases above. <see langword="null" /> — the default — means there is none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is process-wide, and deliberately so: <see cref="FindTimeZoneById" /> is reached from
    /// places that have no scheduler in scope — parsing a <see cref="CronExpression" />, deserializing
    /// a trigger or calendar out of a job store blob — so there is nothing scheduler-scoped to hang a
    /// resolver on. Setting it from one scheduler changes id resolution for every scheduler in the
    /// process, which is what installing <c>Quartz.Plugins.TimeZoneConverter</c> does.
    /// </para>
    /// <para>
    /// Assign <see langword="null" /> to remove a resolver again; a resolver returning
    /// <see langword="null" /> for an id it does not know is how it declines a single id.
    /// </para>
    /// </remarks>
    public static Func<string, TimeZoneInfo?>? CustomResolver { get; set; }

    /// <summary>
    /// TimeZoneInfo.ConvertTime is not supported under mono
    /// </summary>
    /// <param name="dateTimeOffset"></param>
    /// <param name="timeZoneInfo"></param>
    /// <returns></returns>
    public static DateTimeOffset ConvertTime(DateTimeOffset dateTimeOffset, TimeZoneInfo timeZoneInfo)
    {
        return TimeZoneInfo.ConvertTime(dateTimeOffset, timeZoneInfo);
    }

    /// <summary>
    /// TimeZoneInfo.GetUtcOffset(DateTimeOffset) is not supported under mono
    /// </summary>
    /// <param name="dateTimeOffset"></param>
    /// <param name="timeZoneInfo"></param>
    /// <returns></returns>
    public static TimeSpan GetUtcOffset(DateTimeOffset dateTimeOffset, TimeZoneInfo timeZoneInfo)
    {
        if (QuartzEnvironment.IsRunningOnMono)
        {
            return timeZoneInfo.GetUtcOffset(dateTimeOffset.UtcDateTime);
        }

        return timeZoneInfo.GetUtcOffset(dateTimeOffset);
    }

    public static TimeSpan GetUtcOffset(DateTime dateTime, TimeZoneInfo timeZoneInfo)
    {
        // Unlike the default behavior of TimeZoneInfo.GetUtcOffset, it is prefered to choose
        // the DAYLIGHT time when the input is ambiguous, because the daylight instance is the
        // FIRST instance, and time moves in a forward direction.

        TimeSpan offset = timeZoneInfo.IsAmbiguousTime(dateTime)
            ? timeZoneInfo.GetAmbiguousTimeOffsets(dateTime).Max()
            : timeZoneInfo.GetUtcOffset(dateTime);

        return offset;
    }

    /// <summary>
    /// Transition scans are bounded so that a pathological time zone cannot loop forever;
    /// no real transition gap or overlap is anywhere near this wide.
    /// </summary>
    private const int MaxTransitionScanMinutes = 48 * 60;

    /// <summary>
    /// Resolves a wall-clock time in the given zone to a <see cref="DateTimeOffset"/> using the
    /// scheduler-wide daylight saving policy:
    /// an ambiguous local time (fall-back overlap) resolves to the daylight offset — the first of
    /// the two occurrences; a local time that does not exist (spring-forward gap) is paired with
    /// the offset in effect just before the gap, which places the instant at the first wall-clock
    /// time after the gap, shifted forward by the transition delta.
    /// </summary>
    /// <remarks>
    /// For an in-gap time this differs from pairing with <see cref="TimeZoneInfo.GetUtcOffset(DateTime)"/>
    /// only in zones whose daylight delta is negative (for example Europe/Dublin as modeled by TZif
    /// data, where winter time is the daylight-flagged period): there the base offset is the
    /// post-gap offset and pairing with it would produce an instant before the gap, moving time
    /// backwards. The offset just before the gap is found by scanning backwards a minute at a time,
    /// which also handles transition deltas that are not whole hours.
    /// </remarks>
    internal static DateTimeOffset ResolveLocal(DateTime dateTime, TimeZoneInfo timeZoneInfo)
    {
        if (timeZoneInfo.IsInvalidTime(dateTime))
        {
            DateTime probe = dateTime;
            int guard = 0;
            do
            {
                probe = probe.AddMinutes(-1);
            } while (timeZoneInfo.IsInvalidTime(probe) && ++guard < MaxTransitionScanMinutes);

            return new DateTimeOffset(dateTime, timeZoneInfo.GetUtcOffset(probe));
        }

        return new DateTimeOffset(dateTime, GetUtcOffset(dateTime, timeZoneInfo));
    }

    /// <summary>
    /// Determines the wall-clock window that occurs twice around a fall-back transition. Returns
    /// false when the given time is not ambiguous. On success <paramref name="windowStart"/> is the
    /// first ambiguous wall-clock minute (pairing it with the standard offset yields the transition
    /// instant, i.e. the first instant of the second pass) and <paramref name="windowEnd"/> is the
    /// first wall-clock minute after the window. Boundaries are found by scanning a minute at a
    /// time, which handles transition deltas that are not whole hours.
    /// </summary>
    internal static bool TryGetAmbiguousWindow(DateTime dateTime, TimeZoneInfo timeZoneInfo, out DateTime windowStart, out DateTime windowEnd)
    {
        if (!timeZoneInfo.IsAmbiguousTime(dateTime))
        {
            windowStart = default;
            windowEnd = default;
            return false;
        }

        DateTime minute = new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, dateTime.Minute, 0, dateTime.Kind);

        DateTime start = minute;
        int guard = 0;
        while (timeZoneInfo.IsAmbiguousTime(start.AddMinutes(-1)) && guard++ < MaxTransitionScanMinutes)
        {
            start = start.AddMinutes(-1);
        }

        DateTime end = minute;
        guard = 0;
        while (timeZoneInfo.IsAmbiguousTime(end) && guard++ < MaxTransitionScanMinutes)
        {
            end = end.AddMinutes(1);
        }

        windowStart = start;
        windowEnd = end;
        return true;
    }

    /// <summary>
    /// Walks forward from a wall-clock time inside a spring-forward gap to the first wall-clock
    /// time that exists in the given zone (the end of the gap). Returns the input unchanged when it
    /// is already valid.
    /// </summary>
    internal static DateTime WalkToGapEnd(DateTime dateTime, TimeZoneInfo timeZoneInfo)
    {
        DateTime probe = dateTime;
        int guard = 0;
        while (timeZoneInfo.IsInvalidTime(probe) && guard++ < MaxTransitionScanMinutes)
        {
            probe = probe.AddMinutes(1);
        }

        return probe;
    }

    /// <summary>
    /// Tries to find time zone with given id, has ability do some fallbacks when necessary.
    /// </summary>
    /// <param name="id">System id of the time zone.</param>
    /// <returns></returns>
    public static TimeZoneInfo FindTimeZoneById(string id)
    {
        TimeZoneInfo? info = null;
        try
        {
            info = TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException ex)
        {
            if (timeZoneIdAliases.TryGetValue(id, out var aliasedId))
            {
                try
                {
                    info = TimeZoneInfo.FindSystemTimeZoneById(aliasedId);
                }
                catch
                {
                    var logger = LogProvider.CreateLogger(nameof(TimeZoneUtil));
                    logger.LogError("Could not find time zone using alias id {AliasId}", aliasedId);
                }
            }

            info ??= CustomResolver?.Invoke(id);

            if (info is null)
            {
                // we tried our best
                throw new TimeZoneNotFoundException(
                    $"Could not find time zone with id {id}, consider using Quartz.Plugins.TimeZoneConverter for resolving more time zones ids",
                    ex);
            }
        }

        return info;
    }
}