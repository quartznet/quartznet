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

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using Quartz.Util;

using TimeZoneConverter;

namespace Quartz.Tests.Unit.Utils;

/// <summary>
/// Ids that name a tzdata legacy link, or a Windows id CLDR maps to one, on a system without
/// <c>tzdata-legacy</c> (#4172): Ubuntu 26.04, which GitHub's <c>ubuntu-latest</c> runs, is one.
/// </summary>
/// <remarks>
/// The tests that call <see cref="TimeZoneUtil.FindTimeZoneById" /> fail only on such a system, where
/// the lookup threw. The ones that call <see cref="TimeZoneUtil.FindTimeZoneByIdUsing" /> stand in for
/// it with a lookup that knows only the canonical zones, so that Windows and macOS prove the fallback
/// too. .NET Framework knows no IANA name and cannot turn a Windows id into one, so the IANA cases and
/// the Windows-id stand-ins run on .NET only.
/// </remarks>
public class TimeZoneLinkTest
{
    /// <summary>
    /// The seven Windows ids whose CLDR name is a legacy link.
    /// </summary>
    private static readonly object[] windowsIds =
    [
        new object[] { "FLE Standard Time", "Europe/Kyiv" },
        new object[] { "India Standard Time", "Asia/Kolkata" },
        new object[] { "Argentina Standard Time", "America/Argentina/Buenos_Aires" },
        new object[] { "Greenland Standard Time", "America/Nuuk" },
        new object[] { "Myanmar Standard Time", "Asia/Yangon" },
        new object[] { "Nepal Standard Time", "Asia/Kathmandu" },
        new object[] { "US Eastern Standard Time", "America/Indiana/Indianapolis" },
    ];

    /// <summary>
    /// Two legacy names asked for directly.
    /// </summary>
    private static readonly object[] legacyNames =
    [
        new object[] { "Asia/Calcutta", "Asia/Kolkata" },
        new object[] { "Europe/Kiev", "Europe/Kyiv" },
    ];

    [TestCaseSource(nameof(windowsIds))]
    public void FindTimeZoneById_WindowsIdOfALegacyLink_ResolvesWithTheCanonicalZonesOffsetsUnderItsOwnId(string id, string canonicalId)
    {
        TimeZoneInfo zone = TimeZoneUtil.FindTimeZoneById(id);

        zone.Id.Should().Be(id, "a job store writes the zone's id to TIME_ZONE_ID, and every node of a cluster has to write the same one");
        OffsetsThatDiffer(zone, TZConvert.GetTimeZoneInfo(canonicalId)).Should().BeEmpty($"{id} is a name for {canonicalId}");
    }

#if NETCORE
    [TestCaseSource(nameof(legacyNames))]
    [TestCase("US/Eastern", "America/New_York")]
    public void FindTimeZoneById_LegacyName_ResolvesWithTheCanonicalZonesOffsets(string id, string canonicalId)
    {
        TimeZoneInfo zone = TimeZoneUtil.FindTimeZoneById(id);

        OffsetsThatDiffer(zone, TZConvert.GetTimeZoneInfo(canonicalId)).Should().BeEmpty(
            $"{id} is a name for {canonicalId}, and a schedule read in one has to fire when it would in the other");
    }

    [TestCaseSource(nameof(legacyNames))]
    public void FindTimeZoneById_LegacyName_KeepsTheIdItWasAskedFor(string id, string canonicalId)
    {
        TimeZoneUtil.FindTimeZoneById(id).Id.Should().Be(id,
            $"a job store writes the zone's id to TIME_ZONE_ID, and a node with tzdata-legacy answers with {id} rather "
            + $"than {canonicalId}, so a node without it has to as well for a cluster to write one id for one zone");
    }

    [TestCaseSource(nameof(windowsIds))]
    public void FindTimeZoneByIdUsing_WindowsIdOnASystemWithoutTheLegacyName_FindsTheCanonicalZoneUnderTheIdAsked(string id, string canonicalId)
    {
        string? ianaId = TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out string? converted) ? converted : null;
        Assume.That(ianaId is not null && TimeZoneLinks.CanonicalIds.ContainsKey(ianaId), $"test premise: CLDR names a legacy link for {id}");
        TimeZoneInfo canonical = Zone(canonicalId, TimeSpan.FromMinutes(345));

        TimeZoneInfo zone = TimeZoneUtil.FindTimeZoneByIdUsing(id, SystemWith(canonical), NoResolver, new ConcurrentDictionary<string, TimeZoneInfo>());

        zone.Id.Should().Be(id, "the zone keeps the id it was asked for, which is what a job store writes");
        zone.HasSameRules(canonical).Should().BeTrue($"{id} is a name for {canonicalId}");
    }
#endif

    [TestCaseSource(nameof(legacyNames))]
    public void FindTimeZoneByIdUsing_SystemWithoutTheLegacyName_FindsTheCanonicalZoneUnderTheIdAsked(string id, string canonicalId)
    {
        TimeZoneInfo canonical = Zone(canonicalId, TimeSpan.FromMinutes(345));

        TimeZoneInfo zone = TimeZoneUtil.FindTimeZoneByIdUsing(id, SystemWith(canonical), NoResolver, new ConcurrentDictionary<string, TimeZoneInfo>());

        zone.Id.Should().Be(id, "the zone keeps the id it was asked for, which is what a job store writes");
        zone.HasSameRules(canonical).Should().BeTrue($"{id} is a name for {canonicalId}");
    }

    [Test]
    public void FindTimeZoneByIdUsing_SystemWithoutTheLegacyName_KeepsTheCanonicalZonesDaylightRules()
    {
        TimeZoneInfo kyiv = ZoneWithEuropeanSummerTime("Europe/Kyiv", TimeSpan.FromHours(2));

        TimeZoneInfo zone = TimeZoneUtil.FindTimeZoneByIdUsing("Europe/Kiev", SystemWith(kyiv), NoResolver, new ConcurrentDictionary<string, TimeZoneInfo>());

        zone.HasSameRules(kyiv).Should().BeTrue("the zone is the canonical one under another name");
        zone.SupportsDaylightSavingTime.Should().BeTrue();
        zone.GetUtcOffset(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero)).Should().Be(TimeSpan.FromHours(2));
        zone.GetUtcOffset(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero)).Should().Be(TimeSpan.FromHours(3));
        OffsetsThatDiffer(zone, kyiv).Should().BeEmpty("a schedule fires when it would in the canonical zone");
    }

    [Test]
    public void FindTimeZoneByIdUsing_AliasThatIsALegacyLink_FindsTheCanonicalZoneUnderTheIdAsked()
    {
        // Where the system cannot convert Windows ids, "Eastern Standard Time" fails and so does its alias,
        // US/Eastern, without tzdata-legacy. The alias is a link to America/New_York.
        TimeZoneInfo newYork = Zone("America/New_York", TimeSpan.FromHours(-5));

        TimeZoneInfo zone = TimeZoneUtil.FindTimeZoneByIdUsing("Eastern Standard Time", SystemWith(newYork), NoResolver, new ConcurrentDictionary<string, TimeZoneInfo>());

        zone.Id.Should().Be("Eastern Standard Time");
        zone.HasSameRules(newYork).Should().BeTrue("US/Eastern, the id's alias, is a link to America/New_York");
    }

    [Test]
    public void FindTimeZoneByIdUsing_SystemThatHasTheLegacyName_AnswersWithItsOwnZone()
    {
        TimeZoneInfo kiev = Zone("Europe/Kiev", TimeSpan.FromHours(2));
        TimeZoneInfo kyiv = Zone("Europe/Kyiv", TimeSpan.FromHours(2));
        ConcurrentDictionary<string, TimeZoneInfo> found = new ConcurrentDictionary<string, TimeZoneInfo>();

        TimeZoneUtil.FindTimeZoneByIdUsing("Europe/Kiev", SystemWith(kiev, kyiv), NoResolver, found).Should().BeSameAs(kiev,
            "the table is a fallback: a system with tzdata-legacy answers for the legacy name itself");
        found.Should().BeEmpty("nothing was found through a link");
    }

    [Test]
    public void FindTimeZoneByIdUsing_SecondLookup_AnswersFromTheCacheWithoutAskingTheSystem()
    {
        TimeZoneInfo kolkata = Zone("Asia/Kolkata", TimeSpan.FromMinutes(330));
        List<string> asked = new List<string>();
        ConcurrentDictionary<string, TimeZoneInfo> found = new ConcurrentDictionary<string, TimeZoneInfo>();
        Func<string, TimeZoneInfo> system = SystemWith(kolkata);
        Func<string, TimeZoneInfo> counting = id =>
        {
            asked.Add(id);
            return system(id);
        };

        TimeZoneInfo first = TimeZoneUtil.FindTimeZoneByIdUsing("Asia/Calcutta", counting, NoResolver, found);
        asked.Clear();
        TimeZoneInfo second = TimeZoneUtil.FindTimeZoneByIdUsing("Asia/Calcutta", counting, NoResolver, found);

        second.Should().BeSameAs(first, "an id found through a link is found once");
        asked.Should().BeEmpty("the system lookup throws for the legacy name, and a trigger read from a store looks its zone up every time");
    }

    [Test]
    public void FindTimeZoneByIdUsing_CanonicalZoneMissingToo_FallsThroughToTheCustomResolver()
    {
        // Asia/Saigon is a link to Asia/Ho_Chi_Minh, which this system does not have either.
        TimeZoneInfo fromResolver = Zone("Asia/Saigon", TimeSpan.FromHours(7));

        TimeZoneUtil.FindTimeZoneByIdUsing("Asia/Saigon", SystemWith(), x => x == "Asia/Saigon" ? fromResolver : null, new ConcurrentDictionary<string, TimeZoneInfo>())
            .Should().BeSameAs(fromResolver, "a link the system cannot follow either leaves the id to the custom resolver");
    }

    [Test]
    public void FindTimeZoneByIdUsing_LinkTheSystemCanFollow_ComesBeforeTheCustomResolver()
    {
        TimeZoneInfo hoChiMinh = Zone("Asia/Ho_Chi_Minh", TimeSpan.FromHours(7));
        TimeZoneInfo fromResolver = Zone("Asia/Saigon", TimeSpan.FromHours(8));

        TimeZoneInfo zone = TimeZoneUtil.FindTimeZoneByIdUsing("Asia/Saigon", SystemWith(hoChiMinh), x => x == "Asia/Saigon" ? fromResolver : null, new ConcurrentDictionary<string, TimeZoneInfo>());

        zone.HasSameRules(hoChiMinh).Should().BeTrue("the custom resolver is the last resort and keeps that place, after every fallback Quartz has of its own");
        zone.Id.Should().Be("Asia/Saigon");
    }

    [Test]
    public void FindTimeZoneByIdUsing_IdNoLinkExplains_StillThrowsWithGuidance()
    {
        Func<TimeZoneInfo> act = () => TimeZoneUtil.FindTimeZoneByIdUsing("Quartz/Test-No-Such-Link", SystemWith(), NoResolver, new ConcurrentDictionary<string, TimeZoneInfo>());

        act.Should().Throw<TimeZoneNotFoundException>().WithMessage("*Quartz/Test-No-Such-Link*Quartz.Plugins.TimeZoneConverter*");
    }

    [Test]
    public void TheLinkTable_NeverLinksToALink()
    {
        TimeZoneLinks.CanonicalIds.Should().NotBeEmpty();
        TimeZoneLinks.CanonicalIds.Values.Where(TimeZoneLinks.CanonicalIds.ContainsKey).Should().BeEmpty(
            "a lookup follows one link, as tzdata's own data lines do, so every target has to be a zone");
    }

    private static TimeZoneInfo? NoResolver(string id) => null;

    /// <summary>
    /// A system lookup that knows only the given zones, the way one without <c>tzdata-legacy</c> knows only
    /// the canonical names.
    /// </summary>
    private static Func<string, TimeZoneInfo> SystemWith(params TimeZoneInfo[] zones)
    {
        return id => zones.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal))
                     ?? throw new TimeZoneNotFoundException($"The time zone ID '{id}' was not found on the local computer.");
    }

    private static TimeZoneInfo Zone(string id, TimeSpan offset)
    {
        return TimeZoneInfo.CreateCustomTimeZone(id, offset, id, id);
    }

    /// <summary>
    /// A zone that keeps summer time as the European Union does: from 01:00 UTC on the last Sunday of March to
    /// 01:00 UTC on the last Sunday of October.
    /// </summary>
    private static TimeZoneInfo ZoneWithEuropeanSummerTime(string id, TimeSpan offset)
    {
        DateTime oneUtc = new DateTime(1, 1, 1).Add(TimeSpan.FromHours(1) + offset);
        TimeZoneInfo.AdjustmentRule rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date,
            DateTime.MaxValue.Date,
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(oneUtc, 3, 5, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(oneUtc.AddHours(1), 10, 5, DayOfWeek.Sunday));

        return TimeZoneInfo.CreateCustomTimeZone(id, offset, id, id, id + " summer", new[] { rule });
    }

    /// <summary>
    /// The instants, six hours apart from 2000 to 2040, at which the two zones disagree on the offset.
    /// Earlier years are left out: a distribution may carry a link's own pre-1970 history from
    /// tzdata's <c>backzone</c>.
    /// </summary>
    private static List<string> OffsetsThatDiffer(TimeZoneInfo zone, TimeZoneInfo canonical)
    {
        List<string> differences = new List<string>();
        for (DateTimeOffset at = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero); at.Year < 2040; at = at.AddHours(6))
        {
            TimeSpan expected = canonical.GetUtcOffset(at);
            TimeSpan actual = zone.GetUtcOffset(at);
            if (actual != expected)
            {
                differences.Add($"{at:u}: {actual} rather than {expected}");
            }
        }

        return differences;
    }
}
