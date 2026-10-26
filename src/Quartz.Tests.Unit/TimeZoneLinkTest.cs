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

using System.Collections.Concurrent;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using TimeZoneConverter;

namespace Quartz.Tests.Unit;

/// <summary>
/// Ids that name a tzdata legacy link, or a Windows id CLDR maps to one, on a system without
/// <c>tzdata-legacy</c> (#4172): Ubuntu 26.04, which GitHub's <c>ubuntu-latest</c> runs, is one.
/// </summary>
/// <remarks>
/// The tests that call <see cref="TimeZones.FindById" /> run on every system and fail only on one
/// without <c>tzdata-legacy</c>, where the lookup threw. The ones that call
/// <see cref="TimeZones.FindByIdUsing" /> stand in for such a system with a lookup that knows only the
/// canonical zones, so that Windows and macOS prove the fallback too.
/// </remarks>
public class TimeZoneLinkTest
{
    /// <summary>
    /// The seven Windows ids whose CLDR name is a legacy link, and two legacy names asked for directly.
    /// </summary>
    private static readonly object[] legacyIds =
    [
        new object[] { "FLE Standard Time", "Europe/Kyiv" },
        new object[] { "India Standard Time", "Asia/Kolkata" },
        new object[] { "Argentina Standard Time", "America/Argentina/Buenos_Aires" },
        new object[] { "Greenland Standard Time", "America/Nuuk" },
        new object[] { "Myanmar Standard Time", "Asia/Yangon" },
        new object[] { "Nepal Standard Time", "Asia/Kathmandu" },
        new object[] { "US Eastern Standard Time", "America/Indiana/Indianapolis" },
        new object[] { "Asia/Calcutta", "Asia/Kolkata" },
        new object[] { "Europe/Kiev", "Europe/Kyiv" },
    ];

    [TestCaseSource(nameof(legacyIds))]
    [TestCase("US/Eastern", "America/New_York")]
    public void FindById_LegacyId_ResolvesWithTheCanonicalZonesOffsets(string id, string canonicalId)
    {
        TimeZoneInfo zone = TimeZones.FindById(id);

        OffsetsThatDiffer(zone, TZConvert.GetTimeZoneInfo(canonicalId)).Should().BeEmpty(
            $"{id} is a name for {canonicalId}, and a schedule read in one has to fire when it would in the other");
    }

    [TestCaseSource(nameof(legacyIds))]
    public void FindById_LegacyId_KeepsTheIdItWasAskedFor(string id, string canonicalId)
    {
        TimeZones.FindById(id).Id.Should().Be(id,
            $"a job store writes the zone's id to TIME_ZONE_ID, and a node with tzdata-legacy answers with {id} rather "
            + $"than {canonicalId}, so a node without it has to as well for a cluster to write one id for one zone");
    }

    [TestCase("FLE Standard Time")]
    [TestCase("Asia/Calcutta")]
    public async Task AnAdoStore_WritesAndReadsBackTheIdAskedFor(string id)
    {
        using SqliteTestDatabase database = new("time-zone-link");
        TimeZoneInfo zone = TimeZones.FindById(id);

        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "time-zone-link";
                options.InstanceId = "one";
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
            });
        });

        await using ServiceProvider container = services.BuildServiceProvider();

        // Never started: the triggers are stored and read, not fired.
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job").StoreDurably().Build();
            await scheduler.AddJob(job);

            await scheduler.ScheduleJob(TriggerBuilder.Create().WithIdentity("cron").ForJob(job)
                .WithCronSchedule("0 0 3 * * ?", x => x.InTimeZone(zone)).Build());
            await scheduler.ScheduleJob(TriggerBuilder.Create().WithIdentity("calendar").ForJob(job)
                .WithCalendarIntervalSchedule(x => x.WithInterval(1, IntervalUnit.Day).InTimeZone(zone)).Build());
            await scheduler.ScheduleJob(TriggerBuilder.Create().WithIdentity("daily").ForJob(job)
                .WithDailyTimeIntervalSchedule(x => x.WithInterval(1, IntervalUnit.Hour).InTimeZone(zone)).Build());

            (await ColumnValues(database, CronTimeZoneIds)).Should().Equal([id], "the cron trigger's row names the id asked for");
            (await ColumnValues(database, SimplePropertiesTimeZoneIds)).Should().Equal([id, id],
                "the calendar-interval and daily time-interval triggers' rows name it too");

            ((ICronTrigger) (await scheduler.GetTrigger(new TriggerKey("cron")))!).TimeZone.Id.Should().Be(id);
            ((ICalendarIntervalTrigger) (await scheduler.GetTrigger(new TriggerKey("calendar")))!).TimeZone.Id.Should().Be(id);
            ((IDailyTimeIntervalTrigger) (await scheduler.GetTrigger(new TriggerKey("daily")))!).TimeZone.Id.Should().Be(id,
                "reading the row back resolves the same id again, so writing it back changes nothing");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [TestCaseSource(nameof(legacyIds))]
    public void FindByIdUsing_SystemWithoutTheLegacyName_FindsTheCanonicalZoneUnderTheIdAsked(string id, string canonicalId)
    {
        AssumeCldrNamesALegacyLinkFor(id);
        TimeZoneInfo canonical = Zone(canonicalId, TimeSpan.FromMinutes(345));

        TimeZoneInfo zone = TimeZones.FindByIdUsing(id, SystemWith(canonical), new ConcurrentDictionary<string, TimeZoneInfo>());

        zone.Id.Should().Be(id, "the zone keeps the id it was asked for, which is what a job store writes");
        zone.HasSameRules(canonical).Should().BeTrue($"{id} is a name for {canonicalId}");
    }

    [Test]
    public void FindByIdUsing_SystemWithoutTheLegacyName_KeepsTheCanonicalZonesDaylightRules()
    {
        TimeZoneInfo kyiv = ZoneWithEuropeanSummerTime("Europe/Kyiv", TimeSpan.FromHours(2));

        TimeZoneInfo zone = TimeZones.FindByIdUsing("Europe/Kiev", SystemWith(kyiv), new ConcurrentDictionary<string, TimeZoneInfo>());

        zone.HasSameRules(kyiv).Should().BeTrue("the zone is the canonical one under another name");
        zone.SupportsDaylightSavingTime.Should().BeTrue();
        zone.GetUtcOffset(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero)).Should().Be(TimeSpan.FromHours(2));
        zone.GetUtcOffset(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero)).Should().Be(TimeSpan.FromHours(3));
        OffsetsThatDiffer(zone, kyiv).Should().BeEmpty("a schedule fires when it would in the canonical zone");
    }

    [Test]
    public void FindByIdUsing_AliasThatIsALegacyLink_FindsTheCanonicalZoneUnderTheIdAsked()
    {
        // Where the system cannot convert Windows ids, "Eastern Standard Time" fails and so does its alias,
        // US/Eastern, without tzdata-legacy. The alias is a link to America/New_York.
        TimeZoneInfo newYork = Zone("America/New_York", TimeSpan.FromHours(-5));

        TimeZoneInfo zone = TimeZones.FindByIdUsing("Eastern Standard Time", SystemWith(newYork), new ConcurrentDictionary<string, TimeZoneInfo>());

        zone.Id.Should().Be("Eastern Standard Time");
        zone.HasSameRules(newYork).Should().BeTrue("US/Eastern, the id's alias, is a link to America/New_York");
    }

    [Test]
    public void FindByIdUsing_SystemThatHasTheLegacyName_AnswersWithItsOwnZone()
    {
        TimeZoneInfo kiev = Zone("Europe/Kiev", TimeSpan.FromHours(2));
        TimeZoneInfo kyiv = Zone("Europe/Kyiv", TimeSpan.FromHours(2));
        ConcurrentDictionary<string, TimeZoneInfo> found = new();

        TimeZones.FindByIdUsing("Europe/Kiev", SystemWith(kiev, kyiv), found).Should().BeSameAs(kiev,
            "the table is a fallback: a system with tzdata-legacy answers for the legacy name itself");
        found.Should().BeEmpty("nothing was found through a link");
    }

    [Test]
    public void FindByIdUsing_SecondLookup_AnswersFromTheCacheWithoutAskingTheSystem()
    {
        TimeZoneInfo kolkata = Zone("Asia/Kolkata", TimeSpan.FromMinutes(330));
        List<string> asked = [];
        ConcurrentDictionary<string, TimeZoneInfo> found = new();
        Func<string, TimeZoneInfo> system = SystemWith(kolkata);
        Func<string, TimeZoneInfo> counting = id =>
        {
            asked.Add(id);
            return system(id);
        };

        TimeZoneInfo first = TimeZones.FindByIdUsing("Asia/Calcutta", counting, found);
        asked.Clear();
        TimeZoneInfo second = TimeZones.FindByIdUsing("Asia/Calcutta", counting, found);

        second.Should().BeSameAs(first, "an id found through a link is found once");
        asked.Should().BeEmpty("the system lookup throws for the legacy name, and a trigger read from a store looks its zone up every time");
    }

    [Test]
    public void FindByIdUsing_CanonicalZoneMissingToo_FallsThroughToTheResolvers()
    {
        // Asia/Saigon is a link to Asia/Ho_Chi_Minh, which this system does not have either.
        TimeZoneInfo fromResolver = Zone("Asia/Saigon", TimeSpan.FromHours(7));
        using IDisposable resolver = TimeZones.AddResolver(x => x == "Asia/Saigon" ? fromResolver : null);

        TimeZones.FindByIdUsing("Asia/Saigon", SystemWith(), new ConcurrentDictionary<string, TimeZoneInfo>())
            .Should().BeSameAs(fromResolver, "a link the system cannot follow either leaves the id to the resolvers");
    }

    [Test]
    public void FindByIdUsing_LinkTheSystemCanFollow_ComesBeforeTheResolvers()
    {
        TimeZoneInfo hoChiMinh = Zone("Asia/Ho_Chi_Minh", TimeSpan.FromHours(7));
        TimeZoneInfo fromResolver = Zone("Asia/Saigon", TimeSpan.FromHours(8));
        using IDisposable resolver = TimeZones.AddResolver(x => x == "Asia/Saigon" ? fromResolver : null);

        TimeZoneInfo zone = TimeZones.FindByIdUsing("Asia/Saigon", SystemWith(hoChiMinh), new ConcurrentDictionary<string, TimeZoneInfo>());

        zone.HasSameRules(hoChiMinh).Should().BeTrue(
            "resolvers are the last resort and keep that place, after every fallback Quartz has of its own");
        zone.Id.Should().Be("Asia/Saigon");
    }

    [Test]
    public void FindByIdUsing_IdNoLinkExplains_StillThrowsWithGuidance()
    {
        Func<TimeZoneInfo> act = () => TimeZones.FindByIdUsing("Quartz/Test-No-Such-Link", SystemWith(), new ConcurrentDictionary<string, TimeZoneInfo>());

        act.Should().Throw<TimeZoneNotFoundException>().WithMessage("*Quartz/Test-No-Such-Link*Quartz.Plugins.TimeZoneConverter*");
    }

    [Test]
    public void TheLinkTable_NeverLinksToALink()
    {
        TimeZoneLinks.CanonicalIds.Should().NotBeEmpty();
        TimeZoneLinks.CanonicalIds.Values.Where(TimeZoneLinks.CanonicalIds.ContainsKey).Should().BeEmpty(
            "a lookup follows one link, as tzdata's own data lines do, so every target has to be a zone");
    }

    private const string CronTimeZoneIds = "SELECT TIME_ZONE_ID FROM QRTZ_CRON_TRIGGERS ORDER BY TRIGGER_NAME";

    private const string SimplePropertiesTimeZoneIds = "SELECT TIME_ZONE_ID FROM QRTZ_SIMPROP_TRIGGERS ORDER BY TRIGGER_NAME";

    private static async Task<List<string>> ColumnValues(SqliteTestDatabase database, string query)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = new(query, connection);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();

        List<string> values = [];
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    /// <summary>
    /// The premise of the stand-in tests: CLDR's name for a Windows id is a legacy link. A later CLDR that
    /// gave the canonical name instead would have nothing left to fall back from.
    /// </summary>
    private static void AssumeCldrNamesALegacyLinkFor(string id)
    {
        string name = TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out string? ianaId) ? ianaId : id;
        Assume.That(TimeZoneLinks.CanonicalIds.ContainsKey(name), $"test premise: {id} reaches the legacy link {name}");
    }

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

        return TimeZoneInfo.CreateCustomTimeZone(id, offset, id, id, id + " summer", [rule]);
    }

    /// <summary>
    /// The instants, six hours apart from 2000 to 2040, at which the two zones disagree on the offset.
    /// Earlier years are left out: a distribution may carry a link's own pre-1970 history from
    /// tzdata's <c>backzone</c>.
    /// </summary>
    private static List<string> OffsetsThatDiffer(TimeZoneInfo zone, TimeZoneInfo canonical)
    {
        List<string> differences = [];
        for (DateTimeOffset at = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero); at.Year < 2040; at = at.AddHours(6))
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

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
