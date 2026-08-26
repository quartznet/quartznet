using System;
using System.Collections.Generic;
using System.Linq;

using Quartz.Logging;

namespace Quartz.Util;

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

    public static Func<string, TimeZoneInfo?> CustomResolver = id => null;

    /// <summary>
    /// TimeZoneInfo.ConvertTime is not supported under mono
    /// </summary>
    /// <param name="dateTimeOffset"></param>
    /// <param name="timeZoneInfo"></param>
    /// <returns></returns>
    public static DateTimeOffset ConvertTime(DateTimeOffset dateTimeOffset, TimeZoneInfo timeZoneInfo)
    {
        if (QuartzEnvironment.IsRunningOnMono)
        {
            return TimeZoneInfo.ConvertTime(dateTimeOffset.UtcDateTime, TimeZoneInfo.Utc, timeZoneInfo);
        }

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
    /// The first instant of the given local date in the given zone, carrying that zone's offset.
    /// Only the date part of <paramref name="date"/> is used. Local midnight is resolved with
    /// <see cref="ResolveLocal"/>, so a date whose own midnight falls in a spring-forward gap begins
    /// at the end of that gap, and a date whose midnight happens twice begins at the first of the two.
    /// </summary>
    /// <remarks>
    /// Building a day boundary as <c>new DateTimeOffset(local.Date, local.Offset)</c> — midnight at
    /// the offset that some other instant of the day happens to carry — lands on the wrong instant
    /// whenever the offset the date starts at is not the offset of the instant that named it, which
    /// is every transition day: an hour out in a zone that moves its clocks at midnight, and on the
    /// day before or the day after in a zone that moves them later.
    /// </remarks>
    internal static DateTimeOffset StartOfLocalDay(DateTime date, TimeZoneInfo timeZoneInfo)
    {
        DateTime midnight = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified);
        return ConvertTime(ResolveLocal(midnight, timeZoneInfo), timeZoneInfo);
    }

    /// <summary>
    /// The first instant at which the zone's clock reads <paramref name="dateTime"/> or later.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A wall clock that exists resolves through <see cref="ResolveLocal"/>, so one that happens
    /// twice answers with the first of the two. One that does not exist — it fell in a spring-forward
    /// gap — is answered with the instant the clocks moved, that being the first instant the clock
    /// reads past it. This is deliberately not what <see cref="ResolveLocal"/> alone says of an
    /// in-gap time: a trigger asking when a wall clock happens is answered with the gap's end shifted
    /// forward by the transition delta, whereas a boundary is crossed the moment the clock passes it.
    /// </para>
    /// <para>
    /// The gap's end is walked to from the whole minute the given time falls in, because a zone
    /// changes offset on a minute and walking from the time's own second would carry that second past
    /// the transition.
    /// </para>
    /// </remarks>
    internal static DateTimeOffset FirstInstantAtOrAfterLocal(DateTime dateTime, TimeZoneInfo timeZoneInfo)
    {
        if (!timeZoneInfo.IsInvalidTime(dateTime))
        {
            return ResolveLocal(dateTime, timeZoneInfo);
        }

        DateTime minute = new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, dateTime.Minute, 0, dateTime.Kind);
        return ResolveLocal(WalkToGapEnd(minute, timeZoneInfo), timeZoneInfo);
    }

    /// <summary>
    /// The second of the two instants an ambiguous wall-clock time names — the one after the
    /// fall-back transition. Returns false when the time is not ambiguous, so it also answers
    /// "does this wall clock happen twice, and when is the second time".
    /// </summary>
    /// <remarks>
    /// The second pass always carries the smaller of the two offsets: a clock that goes back is a
    /// clock whose offset shrinks, whichever of the two periods the zone labels as its daylight one.
    /// <see cref="ResolveLocal"/> resolves the same wall clock to the first of the two, which is
    /// what a trigger wants; a caller that has already gone past the first pass wants this one.
    /// </remarks>
    internal static bool TryResolveSecondPass(DateTime dateTime, TimeZoneInfo timeZoneInfo, out DateTimeOffset instant)
    {
        if (!timeZoneInfo.IsAmbiguousTime(dateTime))
        {
            instant = default;
            return false;
        }

        instant = new DateTimeOffset(dateTime, timeZoneInfo.GetAmbiguousTimeOffsets(dateTime).Min());
        return true;
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
                    var logger = LogProvider.GetLogger(typeof(TimeZoneUtil));
                    logger.ErrorFormat("Could not find time zone using alias id " + aliasedId);
                }
            }

            if (info == null)
            {
                info = CustomResolver(id);
            }

            if (info == null)
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