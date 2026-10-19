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

using System.Collections.Specialized;

using FakeItEasy;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Core;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit;

/// <summary>
/// The scheduler-wide default time zone: which triggers take it — a zone nobody stated, never an explicit
/// <see cref="TimeZoneInfo.Local" /> — where it is resolved, and that the first fire time is read in it
/// (#4135).
/// </summary>
[TestFixture]
public class DefaultTimeZoneTest
{
    /// <summary>The way a caller hands a trigger to the scheduler.</summary>
    public enum SchedulePath
    {
        ScheduleJobWithTrigger,
        ScheduleJobsBatch,
        ScheduleTriggerAlone,
        ScheduleTriggerOnConflict,
        RescheduleJob
    }

    private static readonly string[] zonedFamilies = ["cron", "calendar", "daily", "recurrence"];

    /// <summary>
    /// The three families whose schedule names a time of day, which each build to fire at 03:00 in their
    /// zone. A calendar-interval trigger repeats from its start time, so its first fire time is the same
    /// instant in every zone.
    /// </summary>
    private static readonly string[] timeOfDayFamilies = ["cron", "daily", "recurrence"];

    private static readonly TimeSpan threeInTheMorning = TimeSpan.FromHours(3);

    /// <summary>
    /// A zone whose clock is hours away from this machine's and keeps no daylight saving, so a time of day
    /// read in it cannot pass for the same time of day read in <see cref="TimeZoneInfo.Local" />.
    /// </summary>
    private static readonly TimeZoneInfo far = TimeZoneInfo.Local.BaseUtcOffset == TimeSpan.FromHours(9)
        ? TimeZones.FindById("Pacific/Honolulu")
        : TimeZones.FindById("Asia/Tokyo");

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Which triggers take it
    //////////////////////////////////////////////////////////////////////////////////////////////

    [TestCaseSource(nameof(zonedFamilies))]
    public void TheDefaultGivesAnUnstatedZoneItsZoneAndStatesIt(string family)
    {
        TriggerBase trigger = (TriggerBase) Build(family, zone: null);

        TimeZoneDefaults.Apply(trigger, far).Should().BeTrue("the trigger read as Local and now reads as another zone");

        ZoneOf(trigger).Should().Be(far);
        trigger.TimeZoneStated.Should().BeTrue("the stored zone is one somebody chose, so a second scheduler's default leaves it alone");
    }

    [Test]
    public void ATriggerWithoutAZoneIsLeftAlone()
    {
        TimeZoneDefaults.Apply((IMutableTrigger) Build("simple", zone: null), far).Should().BeFalse(
            "a simple trigger counts an interval from an instant and has no zone to give");

        IMutableTrigger stranger = A.Fake<IMutableTrigger>();
        TimeZoneDefaults.Apply(stranger, far).Should().BeFalse(
            "a trigger outside TriggerBase cannot say whether its zone was stated, and Quartz cannot know how to set one");
    }

    [Test]
    public void ANullDefaultChangesNothing()
    {
        TriggerBase trigger = (TriggerBase) Build("cron", zone: null);

        TimeZoneDefaults.Apply(trigger, defaultTimeZone: null).Should().BeFalse();

        trigger.TimeZoneStated.Should().BeFalse("no default is today's behaviour, which states nothing");
        ZoneOf(trigger).Should().Be(TimeZoneInfo.Local);
    }

    [TestCaseSource(nameof(zonedFamilies))]
    public void AnExplicitLocalZoneIsLeftAlone(string family)
    {
        ITrigger trigger = Build(family, TimeZoneInfo.Local);

        TimeZoneDefaults.Apply((IMutableTrigger) trigger, far).Should().BeFalse();

        ZoneOf(trigger).Should().Be(TimeZoneInfo.Local, "Local named on purpose reads the same as Local by default, and is stated");
    }

    [Test]
    public void TheDefaultReportsNoChangeWhenItIsTheZoneTheTriggerAlreadyReadAs()
    {
        TriggerBase trigger = (TriggerBase) Build("cron", zone: null);

        TimeZoneDefaults.Apply(trigger, TimeZoneInfo.Local).Should().BeFalse(
            "a fire time the trigger already carries was worked out in the zone it still has");
        trigger.TimeZoneStated.Should().BeTrue();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Stated or not
    //////////////////////////////////////////////////////////////////////////////////////////////

    [TestCaseSource(nameof(zonedFamilies))]
    public void ABuilderNeverGivenAZoneLeavesItUnstated(string family)
    {
        TriggerBase unstated = (TriggerBase) Build(family, zone: null);
        TriggerBase local = (TriggerBase) Build(family, TimeZoneInfo.Local);
        TriggerBase named = (TriggerBase) Build(family, far);

        unstated.TimeZoneStated.Should().BeFalse();
        ZoneOf(unstated).Should().Be(TimeZoneInfo.Local, "an unstated zone reads as Local until a scheduler stores the trigger");
        local.TimeZoneStated.Should().BeTrue("an explicit Local is stated, though it reads the same as an unstated zone");
        named.TimeZoneStated.Should().BeTrue();
        ZoneOf(named).Should().Be(far);
    }

    [Test]
    public void ANullZoneNamesNone()
    {
        ((TriggerBase) TriggerBuilder.Create().WithCronSchedule("0 0 3 * * ?", x => x.InTimeZone(null)).Build()).TimeZoneStated.Should().BeFalse();
        ((TriggerBase) TriggerBuilder.Create().WithCalendarIntervalSchedule(x => x.InTimeZone(null)).Build()).TimeZoneStated.Should().BeFalse();
        ((TriggerBase) TriggerBuilder.Create().WithDailyTimeIntervalSchedule(x => x.InTimeZone(null)).Build()).TimeZoneStated.Should().BeFalse();
        ((TriggerBase) TriggerBuilder.Create().WithRecurrenceSchedule("FREQ=DAILY", x => x.InTimeZone(null)).Build()).TimeZoneStated.Should().BeFalse();

        CronTriggerImpl cleared = new() { TimeZone = far };
        cleared.TimeZone = null!;
        cleared.TimeZoneStated.Should().BeFalse("null names no zone, so the trigger reads as Local again");
        cleared.TimeZone.Should().Be(TimeZoneInfo.Local);
    }

    [TestCaseSource(nameof(zonedFamilies))]
    public void ARebuildKeepsWhetherTheZoneWasStated(string family)
    {
        ((TriggerBase) Build(family, zone: null).GetTriggerBuilder().Build()).TimeZoneStated.Should().BeFalse(
            $"a rebuilt {family} trigger leaves unstated what the original left unstated");

        ITrigger rebuilt = Build(family, TimeZoneInfo.Local).GetTriggerBuilder().Build();
        ((TriggerBase) rebuilt).TimeZoneStated.Should().BeTrue($"a rebuilt {family} trigger keeps a Local somebody stated");
        ZoneOf(rebuilt).Should().Be(TimeZoneInfo.Local);

        ((TriggerBase) Build(family, zone: null).Clone()).TimeZoneStated.Should().BeFalse("a clone is the same trigger");
        ((TriggerBase) Build(family, far).Clone()).TimeZoneStated.Should().BeTrue();
    }

    [Test]
    public void ACronExpressionStatesTheZoneItCarries()
    {
        ((TriggerBase) TriggerBuilder.Create().WithCronSchedule(new CronExpression("0 0 3 * * ?")).Build()).TimeZoneStated.Should().BeFalse(
            "an expression given no zone reads as Local by default, as the trigger does");

        ICronTrigger zoned = (ICronTrigger) TriggerBuilder.Create().WithCronSchedule(new CronExpression("0 0 3 * * ?", far)).Build();
        ((TriggerBase) zoned).TimeZoneStated.Should().BeTrue("an expression given a zone names it for the trigger");
        zoned.TimeZone.Should().Be(far);

        CronTriggerImpl constructed = new("n", "g", "0 0 3 * * ?");
        constructed.TimeZoneStated.Should().BeFalse("the constructors no longer state Local on the caller's behalf");
        constructed.CronExpression!.TimeZoneStated.Should().BeFalse();

        new CronTriggerImpl { TimeZone = TimeZoneInfo.Local }.TimeZoneStated.Should().BeTrue("any assignment of a zone states it, Local included");
    }

    [Test]
    public void ANewExpressionStringKeepsTheZoneAndWhetherItWasStated()
    {
        CronTriggerImpl unstated = new() { CronExpressionString = "0 0 3 * * ?" };
        unstated.CronExpressionString = "0 0 4 * * ?";
        unstated.TimeZoneStated.Should().BeFalse();
        unstated.CronExpression!.TimeZoneStated.Should().BeFalse("an unstated zone stays out of the expression, so a rebuild leaves it unstated too");

        CronTriggerImpl stated = new() { TimeZone = far, CronExpressionString = "0 0 3 * * ?" };
        stated.CronExpressionString = "0 0 4 * * ?";
        stated.TimeZoneStated.Should().BeTrue();
        stated.TimeZone.Should().Be(far, "a string expression carries no zone, so the trigger's stays in effect");
    }

    [Test]
    public void AZoneStatedWithoutTheExpressionCarryingItSurvivesARebuild()
    {
        // What a job store hands back off a row whose zone column is empty: the zone it reads as, stated.
        CronTriggerImpl read = (CronTriggerImpl) Build("cron", zone: null);
        read.TimeZoneStated = true;

        ITrigger rebuilt = read.GetTriggerBuilder().Build();
        ((TriggerBase) rebuilt).TimeZoneStated.Should().BeTrue("the rebuilt trigger keeps the zone the stored one had");
        ((ICronTrigger) rebuilt).TimeZone.Should().Be(TimeZoneInfo.Local);

        ((TriggerBase) read.Clone()).TimeZoneStated.Should().BeTrue("the clone keeps the trigger's statedness, not the expression's");
    }

    [Test]
    public void ADeserializedZoneIsStated()
    {
        SystemTextJsonObjectSerializer serializer = new();
        byte[] payload = serializer.Serialize((IOperableTrigger) Build("cron", zone: null));

        ((TriggerBase) serializer.Deserialize<IOperableTrigger>(payload)!).TimeZoneStated.Should().BeTrue(
            "a deserialized trigger's zone was decided when it was written, and an HttpScheduler's trigger arrives this way");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Where the scheduler resolves it
    //////////////////////////////////////////////////////////////////////////////////////////////

    [TestCase(SchedulePath.ScheduleJobWithTrigger)]
    [TestCase(SchedulePath.ScheduleJobsBatch)]
    [TestCase(SchedulePath.ScheduleTriggerAlone)]
    [TestCase(SchedulePath.ScheduleTriggerOnConflict)]
    [TestCase(SchedulePath.RescheduleJob)]
    public async Task AnUnstatedZoneTakesTheDefaultBeforeTheFirstFireTimeIsComputed(SchedulePath path)
    {
        IScheduler scheduler = await BuildScheduler(far);
        try
        {
            ICronTrigger stored = (ICronTrigger) await StoreThrough(scheduler, path, Build("cron", zone: null, key: "unstated"));

            stored.TimeZone.Should().Be(far, $"{path} resolves an unstated zone to the scheduler's default, so the store holds an explicit one");
            AssertFiresAtThreeIn(stored, far, $"{path} computes the first fire time after the default gave the trigger its zone");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [TestCase(SchedulePath.ScheduleJobWithTrigger)]
    [TestCase(SchedulePath.ScheduleJobsBatch)]
    [TestCase(SchedulePath.ScheduleTriggerAlone)]
    [TestCase(SchedulePath.ScheduleTriggerOnConflict)]
    [TestCase(SchedulePath.RescheduleJob)]
    public async Task AnExplicitLocalZoneKeepsLocal(SchedulePath path)
    {
        IScheduler scheduler = await BuildScheduler(far);
        try
        {
            ICronTrigger stored = (ICronTrigger) await StoreThrough(scheduler, path, Build("cron", TimeZoneInfo.Local, key: "local"));

            stored.TimeZone.Should().Be(TimeZoneInfo.Local, "an explicit Local is stated, and the default leaves a stated zone alone");
            TimeZoneInfo.ConvertTime(stored.NextFireTimeUtc!.Value, TimeZoneInfo.Local).TimeOfDay.Should().Be(threeInTheMorning);
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [TestCaseSource(nameof(zonedFamilies))]
    public async Task EveryZonedFamilyTakesTheDefault(string family)
    {
        IScheduler scheduler = await BuildScheduler(far);
        try
        {
            ITrigger stored = await StoreThrough(scheduler, SchedulePath.ScheduleJobWithTrigger, Build(family, zone: null, key: family));

            ZoneOf(stored).Should().Be(far);
            ((TriggerBase) stored).TimeZoneStated.Should().BeTrue();
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [TestCaseSource(nameof(timeOfDayFamilies))]
    public async Task EveryFamilyNamingATimeOfDayFiresAtItInTheDefault(string family)
    {
        IScheduler scheduler = await BuildScheduler(far);
        try
        {
            ITrigger stored = await StoreThrough(scheduler, SchedulePath.ScheduleJobWithTrigger, Build(family, zone: null, key: family));

            AssertFiresAtThreeIn(stored, far, $"a {family} trigger's time of day is read in its zone, which the default decided first");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public async Task ARescheduledTriggerCarryingAFireTimeIsComputedAgainInItsNewZone()
    {
        IScheduler scheduler = await BuildScheduler(far);
        try
        {
            // A caller that worked out the next fire time itself, while the zone still read as Local.
            IOperableTrigger trigger = (IOperableTrigger) Build("cron", zone: null, key: "carried");
            trigger.ComputeFirstFireTimeUtc(calendar: null);

            ICronTrigger stored = (ICronTrigger) await StoreThrough(scheduler, SchedulePath.RescheduleJob, trigger);

            stored.TimeZone.Should().Be(far);
            AssertFiresAtThreeIn(stored, far, "a fire time worked out in Local is not the trigger's once the default has given it another zone");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public async Task WithNoDefaultAnUnstatedZoneIsLocal()
    {
        IScheduler scheduler = await BuildScheduler(defaultTimeZone: null);
        try
        {
            ICronTrigger stored = (ICronTrigger) await StoreThrough(scheduler, SchedulePath.ScheduleJobWithTrigger, Build("cron", zone: null));

            stored.TimeZone.Should().Be(TimeZoneInfo.Local, "no default is what every trigger did before there was one");
            TimeZoneInfo.ConvertTime(stored.NextFireTimeUtc!.Value, TimeZoneInfo.Local).TimeOfDay.Should().Be(threeInTheMorning);
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // A stored trigger keeps its zone
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task ATriggerStoredInMemoryKeepsItsZoneUnderAnotherDefault()
    {
        IScheduler first = await BuildScheduler(far);
        IScheduler second = await BuildScheduler(TimeZoneInfo.Utc);
        try
        {
            ITrigger stored = await StoreThrough(first, SchedulePath.ScheduleJobWithTrigger, Build("cron", zone: null));

            ITrigger moved = await StoreThrough(second, SchedulePath.ScheduleJobWithTrigger, stored.GetTriggerBuilder().Build());

            ZoneOf(moved).Should().Be(far, "the zone the first scheduler stored is stated, so the second's default leaves it alone");
            AssertFiresAtThreeIn(moved, far, "the moved trigger keeps the schedule it had");
        }
        finally
        {
            await first.Shutdown();
            await second.Shutdown();
        }
    }

    [Test]
    public async Task ATriggerStoredInADatabaseKeepsItsZoneUnderAnotherDefault()
    {
        using SqliteTestDatabase database = new("default-time-zone");

        await using (ServiceProvider writer = PersistentNode(database, far))
        {
            IScheduler scheduler = await writer.GetRequiredService<ISchedulerFactory>().GetScheduler();
            await StoreThrough(scheduler, SchedulePath.ScheduleJobWithTrigger, Build("cron", zone: null, key: "cron"));
            await StoreThrough(scheduler, SchedulePath.ScheduleTriggerAlone, Build("calendar", zone: null, key: "calendar"));
            await scheduler.Shutdown();
        }

        await using ServiceProvider reader = PersistentNode(database, TimeZoneInfo.Utc);
        IScheduler other = await reader.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            ITrigger cron = (await other.GetTrigger(new TriggerKey("cron", "g")))!;
            ZoneOf(cron).Should().Be(far, "the row holds the zone the writing node's default gave the trigger");
            AssertFiresAtThreeIn(cron, far, "the stored next fire time was computed in that zone");

            ITrigger rescheduled = await StoreThrough(other, SchedulePath.RescheduleJob, cron);
            ZoneOf(rescheduled).Should().Be(far, "a trigger read from its row is stated, so this node's default leaves it alone");
            ZoneOf((await other.GetTrigger(new TriggerKey("calendar", "g")))!).Should().Be(far);
        }
        finally
        {
            await other.Shutdown();
        }
    }

    [Test]
    public async Task ARowWithNoZoneIsReadAsLocalAndStated()
    {
        using SqliteTestDatabase database = new("default-time-zone-empty-row");

        await using (ServiceProvider writer = PersistentNode(database, defaultTimeZone: null))
        {
            IScheduler scheduler = await writer.GetRequiredService<ISchedulerFactory>().GetScheduler();
            await StoreThrough(scheduler, SchedulePath.ScheduleJobWithTrigger, Build("calendar", zone: null, key: "calendar"));
            await scheduler.Shutdown();
        }

        // A row an older version wrote without a zone, which the persistence delegate reads as no zone at all.
        await using (SqliteConnection connection = new(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using SqliteCommand clear = new("UPDATE QRTZ_SIMPROP_TRIGGERS SET TIME_ZONE_ID = NULL", connection);
            (await clear.ExecuteNonQueryAsync()).Should().Be(1);
        }

        await using ServiceProvider reader = PersistentNode(database, far);
        IScheduler other = await reader.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            ITrigger read = (await other.GetTrigger(new TriggerKey("calendar", "g")))!;
            ((TriggerBase) read).TimeZoneStated.Should().BeTrue("a trigger read from its row has had its zone decided, whatever the column held");
            ZoneOf(read).Should().Be(TimeZoneInfo.Local);

            ZoneOf(await StoreThrough(other, SchedulePath.RescheduleJob, read)).Should().Be(TimeZoneInfo.Local,
                "the stored trigger keeps the zone it has always read as, rather than taking this node's default");
        }
        finally
        {
            await other.Shutdown();
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Configuration
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task UseDefaultTimeZoneSetsTheOption()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.UseDefaultTimeZone(far));

        await using ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value.DefaultTimeZone.Should().Be(far);

        Action noZone = () => new ServiceCollection().AddQuartz(q => q.UseDefaultTimeZone(null!));
        noZone.Should().Throw<ArgumentNullException>("a null default is the unset option, which not calling this already says");
    }

    [TestCase("Asia/Tokyo")]
    [TestCase("Tokyo Standard Time")]
    public async Task TheFlatKeyReadsAnIanaOrAWindowsId(string id)
    {
        NameValueCollection properties = new()
        {
            ["quartz.scheduler.instanceName"] = "DefaultTimeZoneTest-flat-" + Guid.NewGuid().ToString("N"),
            ["quartz.scheduler.defaultTimeZone"] = id,
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType
        };

        using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();
        IScheduler scheduler = await factory.GetScheduler();
        try
        {
            ITrigger stored = await StoreThrough(scheduler, SchedulePath.ScheduleJobWithTrigger, Build("cron", zone: null));

            ZoneOf(stored).Should().Be(TimeZones.FindById(id), "the key takes any id TimeZones.FindById resolves, as InTimeZone's callers do");
            ZoneOf(stored).BaseUtcOffset.Should().Be(TimeSpan.FromHours(9));
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public async Task AFlatKeyNamingNoZoneIsRefused()
    {
        NameValueCollection properties = new()
        {
            ["quartz.scheduler.instanceName"] = "DefaultTimeZoneTest-refused",
            ["quartz.scheduler.defaultTimeZone"] = "Mars/Olympus_Mons"
        };

        using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();
        Func<Task> act = async () => await factory.GetScheduler();

        await act.Should().ThrowAsync<SchedulerConfigException>("a misspelled zone would leave every unstated trigger on Local unannounced")
            .WithMessage("*Mars/Olympus_Mons*quartz.scheduler.defaultTimeZone*IANA*Windows*");
    }

    [Test]
    public async Task TheSchedulerSectionReadsTheDefaultTimeZoneAsAnId()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Quartz:Scheduler:DefaultTimeZone"] = "Asia/Tokyo" })
            .Build();

        ServiceCollection services = new();
        services.AddQuartz(configuration.GetSection("Quartz"));

        await using ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value.DefaultTimeZone.Should().Be(TimeZones.FindById("Asia/Tokyo"),
            "the generator cannot bind a TimeZoneInfo, so the section's id is read beside it");
    }

    [Test]
    public async Task ASchedulerSectionNamingNoZoneIsRefused()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Quartz:Scheduler:DefaultTimeZone"] = "Mars/Olympus_Mons" })
            .Build();

        ServiceCollection services = new();
        services.AddQuartz(configuration.GetSection("Quartz"));

        await using ServiceProvider provider = services.BuildServiceProvider();
        Action act = () => _ = provider.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value;

        act.Should().Throw<SchedulerConfigException>()
            .WithMessage("*Mars/Olympus_Mons*Quartz:Scheduler:DefaultTimeZone*IANA*");
    }

    [Test]
    public async Task AJsonScheduleNamingNoZoneTakesTheDefault()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Scheduler:InstanceName"] = "DefaultTimeZoneTest-json-" + Guid.NewGuid().ToString("N"),
                ["Scheduler:DefaultTimeZone"] = far.Id,
                ["Schedule:Jobs:0:Name"] = "job",
                ["Schedule:Jobs:0:JobType"] = "Quartz.Jobs.NativeJob, Quartz.Jobs",
                ["Schedule:Jobs:0:Durable"] = "true",
                ["Schedule:Triggers:0:Name"] = "unzoned",
                ["Schedule:Triggers:0:JobName"] = "job",
                ["Schedule:Triggers:0:Cron:Expression"] = "0 0 3 * * ?",
                ["Schedule:Triggers:1:Name"] = "zoned",
                ["Schedule:Triggers:1:JobName"] = "job",
                ["Schedule:Triggers:1:Cron:Expression"] = "0 0 3 * * ?",
                ["Schedule:Triggers:1:Cron:TimeZone"] = "UTC",
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartz(configuration);

        await using ServiceProvider provider = services.BuildServiceProvider();
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            ITrigger unzoned = (await scheduler.GetTrigger(new TriggerKey("unzoned")))!;
            ZoneOf(unzoned).Should().Be(far, "a JSON schedule with no TimeZone states none, so the default applies");
            AssertFiresAtThreeIn(unzoned, far, "the first fire time is read in the default");

            ZoneOf((await scheduler.GetTrigger(new TriggerKey("zoned")))!).Should().Be(TimeZones.FindById("UTC"),
                "a JSON schedule naming a zone states it");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////

    private static void AssertFiresAtThreeIn(ITrigger trigger, TimeZoneInfo zone, string because)
    {
        DateTimeOffset next = trigger.NextFireTimeUtc!.Value;

        TimeZoneInfo.ConvertTime(next, zone).TimeOfDay.Should().Be(threeInTheMorning, because);
        TimeZoneInfo.ConvertTime(next, TimeZoneInfo.Local).TimeOfDay.Should().NotBe(threeInTheMorning,
            "03:00 in the far zone is another time of day here, which is what tells the two zones apart");
    }

    private static TimeZoneInfo ZoneOf(ITrigger trigger) => trigger switch
    {
        ICronTrigger cron => cron.TimeZone,
        ICalendarIntervalTrigger calendarInterval => calendarInterval.TimeZone,
        IDailyTimeIntervalTrigger dailyTimeInterval => dailyTimeInterval.TimeZone,
        IRecurrenceTrigger recurrence => recurrence.TimeZone,
        _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger.GetType(), "not a zoned trigger")
    };

    private static async Task<IScheduler> BuildScheduler(TimeZoneInfo? defaultTimeZone)
    {
        return await QuartzSchedulerBuilder.Create(q =>
            {
                q.ConfigureScheduler(options => options.InstanceName = "DefaultTimeZoneTest-" + Guid.NewGuid().ToString("N"));
                if (defaultTimeZone is not null)
                {
                    q.UseDefaultTimeZone(defaultTimeZone);
                }
            })
            .BuildScheduler();
    }

    /// <summary>
    /// A node on <paramref name="database" />, whose rows every node built on it shares.
    /// </summary>
    private static ServiceProvider PersistentNode(SqliteTestDatabase database, TimeZoneInfo? defaultTimeZone)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "DefaultTimeZoneTest");
            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
            });

            if (defaultTimeZone is not null)
            {
                q.UseDefaultTimeZone(defaultTimeZone);
            }
        });

        return services.BuildServiceProvider();
    }

    private static async Task<ITrigger> StoreThrough(IScheduler scheduler, SchedulePath path, ITrigger trigger)
    {
        IJobDetail job = JobBuilder.Create<DefaultTimeZoneJob>().WithIdentity("job", "g").StoreDurably().Build();

        switch (path)
        {
            case SchedulePath.ScheduleJobWithTrigger:
                await scheduler.ScheduleJob(job, trigger, new ScheduleJobOptions { Replace = true });
                break;
            case SchedulePath.ScheduleJobsBatch:
                await scheduler.ScheduleJobs(new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> { [job] = [trigger] });
                break;
            case SchedulePath.ScheduleTriggerAlone:
                await scheduler.AddJob(job, new AddJobOptions { Replace = true });
                await scheduler.ScheduleJob(trigger);
                break;
            case SchedulePath.ScheduleTriggerOnConflict:
                await scheduler.AddJob(job);
                await scheduler.ScheduleTrigger(trigger, TriggerConflict.Replace);
                break;
            case SchedulePath.RescheduleJob:
                if (!await scheduler.Exists(trigger.Key))
                {
                    ITrigger placeholder = TriggerBuilder.Create()
                        .WithIdentity(trigger.Key)
                        .ForJob(job)
                        .WithCronSchedule("0 0 1 * * ?", x => x.InTimeZone(TimeZoneInfo.Utc))
                        .Build();
                    await scheduler.ScheduleJob(job, placeholder);
                }

                await scheduler.RescheduleJob(trigger.Key, trigger);
                break;
        }

        return (await scheduler.GetTrigger(trigger.Key))!;
    }

    private static ITrigger Build(string family, TimeZoneInfo? zone, string key = "t")
    {
        TriggerBuilder<IJob> builder = TriggerBuilder.Create().WithIdentity(key, "g").ForJob("job", "g");

        return family switch
        {
            "simple" => builder.WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(1)).RepeatForever()).Build(),
            "cron" => builder.WithCronSchedule("0 0 3 * * ?", x =>
            {
                if (zone is not null)
                {
                    x.InTimeZone(zone);
                }
            }).Build(),
            "calendar" => builder.WithCalendarIntervalSchedule(x =>
            {
                x.WithInterval(1, IntervalUnit.Day);
                if (zone is not null)
                {
                    x.InTimeZone(zone);
                }
            }).Build(),
            "daily" => builder.WithDailyTimeIntervalSchedule(x =>
            {
                x.WithInterval(1, IntervalUnit.Hour).StartingDailyAt(new TimeOnly(3, 0)).EndingDailyAt(new TimeOnly(3, 0));
                if (zone is not null)
                {
                    x.InTimeZone(zone);
                }
            }).Build(),
            "recurrence" => builder.WithRecurrenceSchedule("FREQ=DAILY;BYHOUR=3;BYMINUTE=0;BYSECOND=0", x =>
            {
                if (zone is not null)
                {
                    x.InTimeZone(zone);
                }
            }).Build(),
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, "not a trigger family")
        };
    }

    /// <summary>Never executed: the triggers under test are stored, not fired.</summary>
    public sealed class DefaultTimeZoneJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
