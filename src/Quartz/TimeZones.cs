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

public static class TimeZones
{
    /// <summary>
    /// Id spellings that some platform has used and some other platform cannot resolve. The BCL does
    /// not make this table redundant: on Windows with ICU, "Coordinated Universal Time" and "CET"
    /// fail both <see cref="TimeZoneInfo.FindSystemTimeZoneById" /> and the
    /// <see cref="TimeZoneInfo.TryConvertIanaIdToWindowsId(string, out string)" /> conversions, and
    /// resolve through this table alone.
    /// </summary>
    private static readonly Dictionary<string, string> timeZoneIdAliases = new Dictionary<string, string>
    {
        // Azure has had issues with having both formats
        ["UTC"] = "Coordinated Universal Time",
        ["Coordinated Universal Time"] = "UTC",

        // Mono differs in naming too...
        ["Central European Standard Time"] = "CET",
        ["CET"] = "Central European Standard Time",

        ["Eastern Standard Time"] = "US/Eastern",
        ["US/Eastern"] = "Eastern Standard Time",

        ["Central Standard Time"] = "US/Central",
        ["US/Central"] = "Central Standard Time",

        ["Mountain Standard Time"] = "US/Mountain",
        ["US/Mountain"] = "Mountain Standard Time",

        ["US Mountain Standard Time"] = "US/Arizona",
        ["US/Arizona"] = "US Mountain Standard Time",

        ["Pacific Standard Time"] = "US/Pacific",
        ["US/Pacific"] = "Pacific Standard Time",

        ["Alaskan Standard Time"] = "US/Alaska",
        ["US/Alaska"] = "Alaskan Standard Time",

        ["Hawaiian Standard Time"] = "US/Hawaii",
        ["US/Hawaii"] = "Hawaiian Standard Time",

        ["China Standard Time"] = "Asia/Shanghai",
        ["Asia/Shanghai"] = "China Standard Time",

        ["Pakistan Standard Time"] = "Asia/Karachi",
        ["Asia/Karachi"] = "Pakistan Standard Time"
    };

    private static readonly Lock resolverLock = new();

    /// <summary>
    /// The registered resolvers, most recently added first — the order they are consulted in.
    /// Copy-on-write: mutated only under <see cref="resolverLock" />, read as a snapshot without it.
    /// </summary>
    private static ResolverRegistration[] resolvers = [];

    /// <summary>
    /// Registers a last-resort resolver, consulted when a time zone id is neither a system id nor one
    /// of the aliases above. Disposing the returned registration removes the resolver again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is process-wide, and deliberately so: <see cref="FindById" /> is reached from
    /// places that have no scheduler in scope — parsing a <see cref="CronExpression" />, deserializing
    /// a trigger or calendar out of a job store blob — so there is nothing scheduler-scoped to hang a
    /// resolver on. Adding a resolver from one scheduler changes id resolution for every scheduler in
    /// the process, which is what installing <c>Quartz.Plugins.TimeZoneConverter</c> does; each such
    /// plugin disposes its own registration when its scheduler shuts down.
    /// </para>
    /// <para>
    /// Resolvers are consulted most recently added first, so a later registration shadows an earlier
    /// one for the ids it resolves. A resolver declines an id by returning <see langword="null" /> or
    /// by throwing <see cref="TimeZoneNotFoundException" />; either way the search continues with the
    /// next resolver, and <see cref="FindById" /> throws only when every fallback has failed.
    /// </para>
    /// </remarks>
    /// <param name="resolver">Maps a time zone id to a <see cref="TimeZoneInfo" />, or to
    /// <see langword="null" /> for an id it does not know.</param>
    /// <returns>A registration whose disposal removes the resolver. Disposing twice is a no-op.</returns>
    public static IDisposable AddResolver(Func<string, TimeZoneInfo?> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        ResolverRegistration registration = new ResolverRegistration(resolver);
        lock (resolverLock)
        {
            ResolverRegistration[] next = new ResolverRegistration[resolvers.Length + 1];
            next[0] = registration;
            Array.Copy(resolvers, sourceIndex: 0, next, destinationIndex: 1, resolvers.Length);
            resolvers = next;
        }

        return registration;
    }

    private static void RemoveResolver(ResolverRegistration registration)
    {
        lock (resolverLock)
        {
            int index = Array.IndexOf(resolvers, registration);
            if (index < 0)
            {
                return;
            }

            ResolverRegistration[] next = new ResolverRegistration[resolvers.Length - 1];
            Array.Copy(resolvers, sourceIndex: 0, next, destinationIndex: 0, index);
            Array.Copy(resolvers, sourceIndex: index + 1, next, destinationIndex: index, resolvers.Length - index - 1);
            resolvers = next;
        }
    }

    private sealed class ResolverRegistration : IDisposable
    {
        internal readonly Func<string, TimeZoneInfo?> resolver;

        internal ResolverRegistration(Func<string, TimeZoneInfo?> resolver)
        {
            this.resolver = resolver;
        }

        public void Dispose()
        {
            RemoveResolver(this);
        }
    }

    /// <summary>
    /// TimeZoneInfo.ConvertTime is not supported under mono
    /// </summary>
    /// <param name="dateTimeOffset"></param>
    /// <param name="timeZoneInfo"></param>
    /// <returns></returns>
    internal static DateTimeOffset ConvertTime(DateTimeOffset dateTimeOffset, TimeZoneInfo timeZoneInfo)
    {
        return TimeZoneInfo.ConvertTime(dateTimeOffset, timeZoneInfo);
    }

    /// <summary>
    /// TimeZoneInfo.GetUtcOffset(DateTimeOffset) is not supported under mono
    /// </summary>
    /// <param name="dateTimeOffset"></param>
    /// <param name="timeZoneInfo"></param>
    /// <returns></returns>
    internal static TimeSpan GetUtcOffset(DateTimeOffset dateTimeOffset, TimeZoneInfo timeZoneInfo)
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
    public static TimeZoneInfo FindById(string id)
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
                    var logger = LogProvider.CreateLogger(nameof(TimeZones));
                    logger.TimeZoneAliasNotFound(aliasedId);
                }
            }

            // The BCL conversion runs only here, after the direct lookup has failed: run first, it
            // would turn an id like "US/Eastern" into a TimeZoneInfo whose Id is "Eastern Standard
            // Time", and that rewritten Id is what a job store writes back to TIME_ZONE_ID. On ICU
            // builds FindSystemTimeZoneById already attempts this conversion internally, so this is
            // a guard for environments where that internal fallback is unavailable.
            if (info is null && TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out string? windowsId))
            {
                try
                {
                    info = TimeZoneInfo.FindSystemTimeZoneById(windowsId);
                }
                catch (TimeZoneNotFoundException)
                {
                    // the converted id is not present on this system either; continue with the resolvers
                }
            }

            if (info is null)
            {
                // snapshot read; registrations added most recently are consulted first, so a later
                // registration shadows an earlier one for the ids it resolves
                foreach (ResolverRegistration registration in resolvers)
                {
                    try
                    {
                        info = registration.resolver(id);
                    }
                    catch (TimeZoneNotFoundException)
                    {
                        // the resolver declined loudly; continue with the next one
                    }

                    if (info is not null)
                    {
                        break;
                    }
                }
            }

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