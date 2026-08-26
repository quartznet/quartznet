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

using System;
using System.Collections.Generic;
using System.Globalization;

using TimeZoneConverter;

namespace Quartz.Tests.Unit;

/// <summary>
/// Shared time zones and helpers for daylight saving time (DST) tests. Zones are resolved via
/// TimeZoneConverter from Windows ids, which works on every supported OS. Each zone is chosen for a
/// specific DST corner it exercises; tests should state their transition premise with the Assume
/// helpers so that a changed time zone database skips the test instead of failing it.
/// </summary>
internal static class TestTimeZones
{
    /// <summary>
    /// America/New_York: the classic US transition at 02:00 with a one hour delta.
    /// Spring forward 2024-03-10 (02:30 invalid), fall back 2024-11-03 (01:30 ambiguous).
    /// </summary>
    public static TimeZoneInfo Eastern { get; } = TZConvert.GetTimeZoneInfo("Eastern Standard Time");

    /// <summary>
    /// Europe/Warsaw: EU transition at 02:00/03:00 local with a one hour delta.
    /// Spring forward 2018-03-25 (02:30 invalid), fall back 2018-10-28 (02:30 ambiguous).
    /// </summary>
    public static TimeZoneInfo CentralEuropean { get; } = TZConvert.GetTimeZoneInfo("Central European Standard Time");

    /// <summary>
    /// Europe/Helsinki: EET/EEST, +02:00 in winter and +03:00 in summer, with the EU transition at
    /// 01:00 UTC - which is 03:00 local in spring (03:00 becomes 04:00, so 03:30 is invalid) and
    /// 04:00 local in autumn (04:00 becomes 03:00, so 03:30 is ambiguous). Spring forward
    /// 2024-03-31, fall back 2024-10-27.
    /// </summary>
    /// <remarks>
    /// Windows names the whole EET/EEST group "FLE Standard Time", which resolves to Europe/Kyiv on
    /// a system carrying IANA data. Helsinki and Kyiv share the EU rules, so the transitions
    /// asserted against this zone are the same instants either way - and the Assume helpers say so
    /// out loud, so a zone database that ever disagreed would skip the test rather than fail it.
    /// </remarks>
    public static TimeZoneInfo Helsinki { get; } = TZConvert.GetTimeZoneInfo("FLE Standard Time");

    /// <summary>
    /// America/Santiago: southern hemisphere and the transition happens at midnight, so on
    /// spring-forward day the date's own 00:00 does not exist (2019-09-08, 00:30 invalid) and on
    /// fall-back day the repeated hour crosses backwards over the date boundary
    /// (Saturday 2019-04-06 23:30 is ambiguous).
    /// </summary>
    public static TimeZoneInfo Santiago { get; } = TZConvert.GetTimeZoneInfo("Pacific SA Standard Time");

    /// <summary>
    /// Australia/Sydney: southern hemisphere with the usual 02:00/03:00 transitions.
    /// Fall back 2024-04-07 (02:30 ambiguous), spring forward 2024-10-06 (02:30 invalid).
    /// </summary>
    public static TimeZoneInfo Sydney { get; } = TZConvert.GetTimeZoneInfo("AUS Eastern Standard Time");

    /// <summary>
    /// Australia/Lord_Howe: DST delta is only 30 minutes (+10:30 to +11:00), which catches any
    /// code that assumes a one hour correction. Spring forward 2019-10-06 (02:15 invalid),
    /// fall back 2019-04-07 (01:45 ambiguous). May be missing from old OS installations, in which
    /// case tests using it are ignored.
    /// </summary>
    public static TimeZoneInfo LordHowe => GetOrIgnore("Lord Howe Standard Time");

    /// <summary>
    /// Asia/Amman: historically the spring-forward gap started at midnight, so the transition
    /// day started at 01:00 (2017-03-31, 00:30 invalid) and on fall-back day the first hour of the
    /// day repeated (2017-10-27, 00:30 ambiguous). Jordan abolished DST in 2022, so this history is
    /// frozen. May be missing from old OS installations, in which case tests using it are ignored.
    /// </summary>
    public static TimeZoneInfo Amman => GetOrIgnore("Jordan Standard Time");

    private static TimeZoneInfo GetOrIgnore(string windowsId)
    {
        try
        {
            return TZConvert.GetTimeZoneInfo(windowsId);
        }
        catch (TimeZoneNotFoundException)
        {
            NUnit.Framework.Assert.Ignore($"time zone {windowsId} is not available on this system");
            throw; // unreachable, Assert.Ignore throws
        }
    }

    /// <summary>
    /// States a test's premise that the given wall-clock time does not exist in the zone (it falls
    /// into a spring-forward gap). If the time zone database no longer agrees, the test is skipped
    /// as inconclusive instead of failing.
    /// </summary>
    public static void AssumeInvalidLocalTime(TimeZoneInfo zone, DateTime localTime)
    {
        Assume.That(zone.IsInvalidTime(localTime), $"test premise: {localTime:yyyy-MM-dd HH:mm} should not exist in zone {zone.Id}");
    }

    /// <summary>
    /// States a test's premise that the given wall-clock time is ambiguous in the zone (it occurs
    /// twice around a fall-back transition). If the time zone database no longer agrees, the test
    /// is skipped as inconclusive instead of failing.
    /// </summary>
    public static void AssumeAmbiguousLocalTime(TimeZoneInfo zone, DateTime localTime)
    {
        Assume.That(zone.IsAmbiguousTime(localTime), $"test premise: {localTime:yyyy-MM-dd HH:mm} should be ambiguous in zone {zone.Id}");
    }

    /// <summary>
    /// Parses strings like "2024-11-03 01:30 -04:00" so that test case grids can assert the fire
    /// instant and its offset in a single value, mirroring how the two occurrences of an ambiguous
    /// local time differ only by offset.
    /// </summary>
    public static DateTimeOffset Local(string value)
    {
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.None);
    }

    /// <summary>
    /// Collects fire times by repeatedly calling <paramref name="getNextAfter"/>, starting after
    /// <paramref name="after"/> and stopping at the first result at or beyond
    /// <paramref name="untilExclusive"/> (which is not included). Fails the test if the produced
    /// times are not strictly increasing or the safety limit is exceeded, so any fire-count test
    /// also guards against the trigger wedging or looping around a DST transition.
    /// </summary>
    public static List<DateTimeOffset> Walk(
        Func<DateTimeOffset, DateTimeOffset?> getNextAfter,
        DateTimeOffset after,
        DateTimeOffset untilExclusive,
        int safetyLimit = 10_000)
    {
        List<DateTimeOffset> fireTimes = new List<DateTimeOffset>();
        DateTimeOffset current = after;
        while (true)
        {
            DateTimeOffset? next = getNextAfter(current);
            if (next is null || next.Value >= untilExclusive)
            {
                return fireTimes;
            }

            if (next.Value <= current)
            {
                NUnit.Framework.Assert.Fail($"fire times must strictly increase: got {next.Value:O} after {current:O}");
            }

            if (fireTimes.Count >= safetyLimit)
            {
                NUnit.Framework.Assert.Fail($"more than {safetyLimit} fire times produced before {untilExclusive:O}; trigger is likely looping");
            }

            fireTimes.Add(next.Value);
            current = next.Value;
        }
    }
}
