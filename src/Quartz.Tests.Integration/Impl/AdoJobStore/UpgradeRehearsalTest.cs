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

using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

using FirebirdSql.Data.FirebirdClient;

using Microsoft.Data.SqlClient;

using MySqlConnector;

using Npgsql;

using Oracle.ManagedDataAccess.Client;

using Quartz.Tests.Integration.Seeder;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Runs the 4.0 upgrade over a database a released Quartz 3.20.0 filled, and then reads it with 4.0.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MigrationScriptTest" /> migrates an <em>empty</em> 3.16 schema and asserts the result is
/// shaped like a fresh install. That is necessary and it is not the claim beta.1 most needs to make:
/// what an upgrading deployment wants to know is whether the rows it already has survive. Every row
/// here was written by the released 3.20 package, out of process — the only way it can be, since that
/// package's assembly identity is this repository's own.
/// </para>
/// <para>
/// So the shape of a leg is: build the 3.20 schema under <c>QRTZU_</c>, have
/// <c>Quartz.Tests.Integration.Seeder</c> fill it (once per serializer, under a scheduler name each,
/// because every Quartz table is keyed by <c>SCHED_NAME</c> and two schedulers share a schema
/// happily), run both halves of <c>database/migrations/4.0/</c> over it — the mandatory
/// <c>schema_30_to_40_upgrade_&lt;dialect&gt;.sql</c> and then <c>schema_30_to_40_indexes_…</c> —
/// assert the structural equivalence <see cref="MigrationScriptTest" /> already asserts, and only then
/// start a 4.0 scheduler and check every seeded row against the manifest the seeder wrote.
/// </para>
/// <para>
/// The seeded job type names an assembly this process does not have, which is exactly the position an
/// application whose job types were renamed is in. <c>UseTypeLoader(o =&gt; o.Map(…))</c> is the
/// migration guide's answer to that, and mapping the spelling 3.20 actually stored is how this
/// rehearsal runs the jobs at all — so the guide's advice is exercised rather than merely written down.
/// </para>
/// </remarks>
/// <remarks>
/// Not parallelizable: every leg rebuilds a schema with DDL on the same database the other migration
/// fixtures use, and Firebird refuses concurrent metadata updates with a deadlock (SQLSTATE 40001) --
/// which is exactly what the first CI run of this fixture hit when it overlapped <see cref="MigrationScriptTest" />.
/// </remarks>
[NonParallelizable]
[Category("migrations")]
public class UpgradeRehearsalTest
{
    /// <summary>The prefix the rehearsal builds its 3.20 schema under, beside the fresh <c>QRTZ_</c> one.</summary>
    internal const string RehearsalPrefix = "QRTZU_";

    /// <summary>
    /// Both store serializers, because 3.x wrote genuinely different bytes through each: with the
    /// defaults a 3.x deployment gets, Newtonsoft writes a trigger blob as a plain object graph
    /// carrying <c>$type</c> and System.Text.Json writes the discriminated form.
    /// </summary>
    private static readonly string[] Serializers = ["stj", "json"];

    private static readonly TimeSpan FireTimeout = TimeSpan.FromSeconds(90);

    private static readonly TimeSpan SeederTimeout = TimeSpan.FromMinutes(5);

    [Test]
    [Category("db-sqlite")]
    public Task SqliteUpgradeCarriesA320DatabaseAcross()
    {
        return MigrationScriptTest.WithSqliteAsync((connection, connectionString) =>
            RehearseAsync(connection, "sqlite", connectionString));
    }

    [Test]
    [Category("db-sqlserver")]
    public async Task SqlServerUpgradeCarriesA320DatabaseAcross()
    {
        string connectionString = MigrationScriptTest.RequireConnectionString("MSSQL_CONNECTION_STRING");

        await using SqlConnection connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await RehearseAsync(connection, "sqlServer", connectionString);
    }

    [Test]
    [Category("db-postgres")]
    public async Task PostgreSqlUpgradeCarriesA320DatabaseAcross()
    {
        string connectionString = MigrationScriptTest.RequireConnectionString("PG_CONNECTION_STRING");

        await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await RehearseAsync(connection, "postgres", connectionString);
    }

    [Test]
    [Category("db-mysql")]
    public async Task MySqlUpgradeCarriesA320DatabaseAcross()
    {
        string connectionString = MigrationScriptTest.RequireConnectionString("MYSQL_CONNECTION_STRING");

        await using MySqlConnection connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        await RehearseAsync(connection, "mysql_innodb", connectionString);
    }

    [Test]
    [Category("db-oracle")]
    public async Task OracleUpgradeCarriesA320DatabaseAcross()
    {
        string connectionString = MigrationScriptTest.RequireConnectionString("ORACLE_CONNECTION_STRING");

        await using OracleConnection connection = new OracleConnection(connectionString);
        await connection.OpenAsync();

        await RehearseAsync(connection, "oracle", connectionString);
    }

    [Test]
    [Category("db-firebird")]
    public async Task FirebirdUpgradeCarriesA320DatabaseAcross()
    {
        string connectionString = MigrationScriptTest.RequireConnectionString("FIREBIRD_CONNECTION_STRING");

        await using FbConnection connection = new FbConnection(connectionString);
        await connection.OpenAsync();

        await RehearseAsync(connection, "firebird", connectionString);
    }

    private static async Task RehearseAsync(DbConnection connection, string dialect, string connectionString)
    {
        await MigrationScriptTest.ExecuteScriptAsync(
            connection, MigrationScriptTest.BaselineScript("3.20", dialect, RehearsalPrefix), dialect);

        List<SeedManifest> manifests = [];
        foreach (string serializer in Serializers)
        {
            manifests.Add(await SeedAsync(dialect, connectionString, serializer));
        }

        // Both halves of the upgrade, in the order an operator runs them: the mandatory one while the
        // 3.x nodes are still up, and the index set once the last of them has gone.
        await MigrationScriptTest.ExecuteScriptAsync(
            connection, MigrationScriptTest.MigrationScript("4.0", "schema_30_to_40_upgrade", dialect, RehearsalPrefix), dialect);

        await MigrationScriptTest.ExecuteScriptAsync(
            connection, MigrationScriptTest.MigrationScript("4.0", "schema_30_to_40_indexes", dialect, RehearsalPrefix), dialect);

        await MigrationScriptTest.AssertSchemaMatchesAsync(connection, dialect, RehearsalPrefix);

        foreach (SeedManifest manifest in manifests)
        {
            await AssertUpgradedAsync(connection, dialect, connectionString, manifest);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Running the seeder
    // ---------------------------------------------------------------------------------------------

    private static async Task<SeedManifest> SeedAsync(string dialect, string connectionString, string serializer)
    {
        string output = Path.Combine(Path.GetTempPath(), $"quartz-rehearsal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(output);

        ProcessStartInfo start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        start.ArgumentList.Add(SeederAssembly());
        start.ArgumentList.Add("--dialect");
        start.ArgumentList.Add(dialect);
        start.ArgumentList.Add("--connection-string");
        start.ArgumentList.Add(connectionString);
        start.ArgumentList.Add("--serializer");
        start.ArgumentList.Add(serializer);
        start.ArgumentList.Add("--table-prefix");
        start.ArgumentList.Add(RehearsalPrefix);
        start.ArgumentList.Add("--scheduler-name");
        start.ArgumentList.Add($"Quartz320_{serializer}");
        start.ArgumentList.Add("--instance-id");
        start.ArgumentList.Add($"seed-node-{serializer}");
        start.ArgumentList.Add("--output");
        start.ArgumentList.Add(output);
        start.ArgumentList.Add("--fixture-output");
        start.ArgumentList.Add(Path.Combine(output, "blobs"));

        using Process process = Process.Start(start)!;

        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();

        using CancellationTokenSource timeout = new CancellationTokenSource(SeederTimeout);
        await process.WaitForExitAsync(timeout.Token);

        process.ExitCode.Should().Be(0,
            $"the 3.20 seeder has to fill the {dialect} schema before there is anything to upgrade. "
            + $"stdout: {await stdout}{Environment.NewLine}stderr: {await stderr}");

        string manifestPath = Path.Combine(output, "seed.json");
        File.Exists(manifestPath).Should().BeTrue("the seeder writes the manifest the assertions read their expectations from");

        SeedManifest manifest = JsonSerializer.Deserialize<SeedManifest>(File.ReadAllText(manifestPath), SeedManifest.SerializerOptions)!;

        AssertManifestIsWorthAsserting(manifest, serializer);
        return manifest;
    }

    /// <summary>
    /// Every assertion below walks a list out of the manifest, so a manifest that arrived empty would
    /// make the whole rehearsal pass without checking anything. This is what stops that being possible.
    /// </summary>
    private static void AssertManifestIsWorthAsserting(SeedManifest manifest, string serializer)
    {
        manifest.QuartzVersion.Should().Be("3.20.0", "the fixture is a named released version, never a floating one");
        manifest.Serializer.Should().Be(serializer);
        manifest.JobTypeName.Should().NotBeEmpty("the stored job type name is what the type loader alias is built from");
        manifest.SchedulerName.Should().NotBeEmpty();

        manifest.Jobs.Should().HaveCount(2, "3.20 seeded the worker job and the one holding a value 4.0's write gate would refuse");
        manifest.Jobs[0].JobDataMap.Should().HaveCountGreaterThan(10, "the worker's map holds every value type the write gate admits");

        // Five families in their own tables, plus the ones blob-stored, plus the pinned trigger and the
        // four that say what a pause survives.
        manifest.Triggers.Should().HaveCountGreaterThan(12);
        manifest.Triggers.Select(x => x.TriggerType).Should().Contain("BLOB", "the point of the blob group is a real QRTZ_BLOB_TRIGGERS row");
        manifest.Triggers.Where(x => x.ExpectFires).Should().NotBeEmpty();
        manifest.Triggers.Where(x => x.TriggerState == "PAUSED").Should().NotBeEmpty();

        manifest.Calendars.Should().HaveCount(7, "six kinds and a chained pair");
        manifest.Calendars.Should().OnlyContain(x => x.Probes.Count > 0);

        manifest.PausedTriggerGroups.Should().NotBeEmpty();
        manifest.PausedJobGroups.Should().NotBeEmpty();
        manifest.OrphanedFiredTrigger.Should().NotBeNull("the abandoned firing is what recovery has to find");
        manifest.BlobFixtures.Should().NotBeEmpty();
    }

    /// <summary>
    /// The seeder's assembly, in the same configuration this test was built in.
    /// </summary>
    /// <remarks>
    /// It is not a project reference, and cannot be: it builds against the released <c>Quartz</c>
    /// 3.20.0, whose assembly identity is this repository's own, so referencing it would put two
    /// different Quartz assemblies in one output directory. <c>Quartz.slnx</c> carries it, so
    /// <c>dotnet fallout Compile</c> — and any solution build — produces it.
    /// </remarks>
    private static string SeederAssembly()
    {
        const string project = "Quartz.Tests.Integration.Seeder";

        DirectoryInfo configuration = new DirectoryInfo(AppContext.BaseDirectory);
        DirectoryInfo bin = configuration.Parent?.Parent
            ?? throw new InvalidOperationException($"Cannot locate the artifacts directory from {AppContext.BaseDirectory}.");

        string path = Path.Combine(bin.FullName, project, configuration.Name, $"{project}.dll");

        File.Exists(path).Should().BeTrue(
            $"the rehearsal runs the 3.20 seeder out of process; build it first with "
            + $"'dotnet build src/{project}/{project}.csproj -c {configuration.Name}'");

        return path;
    }

    // ---------------------------------------------------------------------------------------------
    // What 4.0 makes of what 3.20 wrote
    // ---------------------------------------------------------------------------------------------

    private static async Task AssertUpgradedAsync(
        DbConnection connection,
        string dialect,
        string connectionString,
        SeedManifest manifest)
    {
        UpgradeRehearsalJob.Reset();

        IScheduler scheduler = await BuildScheduler(dialect, connectionString, manifest);

        try
        {
            await AssertCalendarsAsync(scheduler, manifest);
            await AssertJobsAsync(scheduler, manifest);
            await AssertTriggersAsync(scheduler, manifest);
            await AssertPausedGroupsAsync(scheduler, manifest);
            AssertRetryColumnsAreEmpty(connection, manifest);

            await scheduler.Start();

            await AssertTheAbandonedFiringIsRecoveredAsync(connection, scheduler, manifest);
            await AssertEveryTriggerFiresAsync(manifest);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    private static async Task AssertCalendarsAsync(IScheduler scheduler, SeedManifest manifest)
    {
        foreach (SeededCalendar seeded in manifest.Calendars)
        {
            ICalendar calendar = await scheduler.GetCalendar(seeded.Name);

            calendar.Should().NotBeNull(
                $"the {seeded.Kind} 3.20 stored under '{seeded.Name}' has to come back out of the migrated QRTZ_CALENDARS");
            calendar.Description.Should().Be(seeded.Description);

            if (seeded.HasBaseCalendar)
            {
                calendar.CalendarBase.Should().NotBeNull(
                    "a chained calendar is one blob holding both halves, so the base has to arrive with it");
                calendar.CalendarBase!.GetType().Name.Should().Be(seeded.BaseCalendarKind);
            }

            foreach (SeededCalendarProbe probe in seeded.Probes)
            {
                calendar.IsTimeIncluded(probe.Instant).Should().Be(probe.Included,
                    $"3.20 answered {probe.Included} for {probe.Instant:O} on calendar '{seeded.Name}', and a calendar "
                    + "that deserialized into something answering differently would silently reschedule every trigger using it");
            }
        }
    }

    private static async Task AssertJobsAsync(IScheduler scheduler, SeedManifest manifest)
    {
        foreach (SeededJob seeded in manifest.Jobs)
        {
            IJobDetail job = await scheduler.GetJobDetail(new JobKey(seeded.Name, seeded.Group));

            job.Should().NotBeNull($"the job 3.20 stored as {seeded.Group}.{seeded.Name} has to survive the upgrade");
            job.Description.Should().Be(seeded.Description);
            job.Durable.Should().Be(seeded.Durable);
            job.RequestsRecovery.Should().Be(seeded.RequestsRecovery);

            foreach (SeededDataValue value in seeded.JobDataMap)
            {
                AssertDataValue(job.JobDataMap, value, $"{seeded.Group}.{seeded.Name}");
            }
        }
    }

    /// <summary>
    /// One job data entry, read back through the accessor its kind names.
    /// </summary>
    /// <remarks>
    /// Several of these are not stored as themselves — a <see cref="char" /> goes in as a string, a
    /// <see cref="decimal" /> as a number — so the assertion is that 4.0's accessor coerces the stored
    /// shape back to the value 3.20 was handed, which is the promise a stored job data map makes.
    /// </remarks>
    private static void AssertDataValue(JobDataMap map, SeededDataValue value, string owner)
    {
        string because = $"3.20 stored {owner}'s '{value.Key}' as a {value.Kind}";

        switch (value.Kind)
        {
            case "string":
                map.GetString(value.Key).Should().Be(value.Text, because);
                break;
            case "bool":
                map.GetBoolean(value.Key).Should().Be(bool.Parse(value.Text!), because);
                break;
            case "int":
                map.GetInt(value.Key).Should().Be(int.Parse(value.Text!, CultureInfo.InvariantCulture), because);
                break;
            case "long":
                map.GetLong(value.Key).Should().Be(long.Parse(value.Text!, CultureInfo.InvariantCulture), because);
                break;
            case "double":
                map.GetDouble(value.Key).Should().Be(double.Parse(value.Text!, CultureInfo.InvariantCulture), because);
                break;
            case "float":
                map.GetFloat(value.Key).Should().Be(float.Parse(value.Text!, CultureInfo.InvariantCulture), because);
                break;
            case "decimal":
                map.Get<decimal>(value.Key).Should().Be(decimal.Parse(value.Text!, CultureInfo.InvariantCulture), because);
                break;
            case "char":
                map.Get<char>(value.Key).Should().Be(value.Text![0], because);
                break;
            case "dateTime":
                map.Get<DateTime>(value.Key).Should().Be(DateTime.Parse(value.Text!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), because);
                break;
            case "dateTimeOffset":
                map.GetDateTimeOffset(value.Key).Should().Be(DateTimeOffset.Parse(value.Text!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), because);
                break;
            case "timeSpan":
                map.Get<TimeSpan>(value.Key).Should().Be(TimeSpan.Parse(value.Text!, CultureInfo.InvariantCulture), because);
                break;
            case "guid":
                map.Get<Guid>(value.Key).Should().Be(Guid.Parse(value.Text!), because);
                break;
            case "dateOnly":
                map.Get<DateOnly>(value.Key).Should().Be(DateOnly.Parse(value.Text!, CultureInfo.InvariantCulture), because);
                break;
            case "timeOnly":
                map.Get<TimeOnly>(value.Key).Should().Be(TimeOnly.Parse(value.Text!, CultureInfo.InvariantCulture), because);
                break;
            case "enum":
                map.Get<DayOfWeek>(value.Key).ToString().Should().Be(value.Text, because);
                break;
            case "dictionary":
                map.Get<Dictionary<string, string>>(value.Key).Should().BeEquivalentTo(value.Entries,
                    "a string dictionary is the one object shape a job data map may hold, and 3.x's Newtonsoft writer "
                    + "decorated it with a $type marker 4.0 has to read past rather than hand back as data (#3582)");
                break;
            case "outsideTheWriteGate":
                map.ContainsKey(value.Key).Should().BeTrue(
                    "4.0 would refuse to write a JobKey into a job data map, but a 3.x database can hold one, "
                    + "and refusing to *read* the row would strand the job rather than the value");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value), value.Kind, "no assertion for this job data kind");
        }
    }

    private static async Task AssertTriggersAsync(IScheduler scheduler, SeedManifest manifest)
    {
        foreach (SeededTrigger seeded in manifest.Triggers)
        {
            TriggerKey key = new TriggerKey(seeded.Name, seeded.Group);
            ITrigger trigger = await scheduler.GetTrigger(key);

            trigger.Should().NotBeNull(
                $"3.20 stored {seeded.Group}.{seeded.Name} as a {seeded.TriggerType} row, and the upgrade has to leave it readable");

            trigger.JobKey.Should().Be(new JobKey(seeded.JobName, seeded.JobGroup));
            trigger.Description.Should().Be(seeded.Description);
            trigger.CalendarName.Should().Be(seeded.CalendarName);
            trigger.Priority.Should().Be(seeded.Priority);
            trigger.MisfireInstructionCode.Should().Be(seeded.MisfireInstruction,
                "the misfire instruction decides what happens to a trigger an upgrade window left behind, "
                + "so losing it in the migration would change when the job next runs");
            trigger.ExecutionGroup.Should().Be(seeded.ExecutionGroup);
            trigger.PreferredNode.Node.Should().Be(seeded.PreferredNode);

            trigger.RetryPolicy.Should().BeNull(
                "RETRY_POLICY is new in 4.0, so every row the migration brought across reads as no policy");
            trigger.RetryAttempt.Should().Be(0,
                "a migrated row's RETRY_ATTEMPT is null, and a null attempt means the occurrence has no retries behind it");

            AssertSchedule(trigger, seeded);

            TriggerState state = await scheduler.GetTriggerState(key);
            state.Should().Be(ExpectedState(seeded.TriggerState),
                $"the migrated row still says TRIGGER_STATE = {seeded.TriggerState}");
        }
    }

    private static TriggerState ExpectedState(string storedState) => storedState switch
    {
        "WAITING" => TriggerState.Normal,
        "PAUSED" => TriggerState.Paused,
        _ => throw new ArgumentOutOfRangeException(nameof(storedState), storedState, "the seeded set produces no other state")
    };

    private static void AssertSchedule(ITrigger trigger, SeededTrigger seeded)
    {
        SeededSchedule schedule = seeded.Schedule;
        string because = $"3.20 stored {seeded.Group}.{seeded.Name} as a {schedule.Kind} schedule";

        switch (schedule.Kind)
        {
            case "simple":
                ISimpleTrigger simple = trigger.Should().BeAssignableTo<ISimpleTrigger>(because).Subject;
                simple.RepeatCount.Should().Be(schedule.RepeatCount!.Value, because);
                simple.RepeatInterval.Should().Be(TimeSpan.FromMilliseconds(schedule.RepeatIntervalMilliseconds!.Value), because);
                break;

            case "cron":
                ICronTrigger cron = trigger.Should().BeAssignableTo<ICronTrigger>(because).Subject;
                cron.CronExpressionString.Should().Be(schedule.CronExpression, because);
                cron.TimeZone.Id.Should().Be(schedule.TimeZoneId, because);
                break;

            case "calendarInterval":
                ICalendarIntervalTrigger calendarInterval = trigger.Should().BeAssignableTo<ICalendarIntervalTrigger>(because).Subject;
                calendarInterval.RepeatInterval.Should().Be(schedule.RepeatInterval!.Value, because);
                calendarInterval.RepeatIntervalUnit.ToString().Should().Be(schedule.RepeatIntervalUnit, because);
                calendarInterval.TimeZone.Id.Should().Be(schedule.TimeZoneId, because);
                calendarInterval.PreserveHourOfDayAcrossDaylightSavings.Should().Be(schedule.PreserveHourOfDayAcrossDaylightSavings!.Value, because);
                calendarInterval.SkipDayIfHourDoesNotExist.Should().Be(schedule.SkipDayIfHourDoesNotExist!.Value, because);
                break;

            case "dailyTimeInterval":
                IDailyTimeIntervalTrigger daily = trigger.Should().BeAssignableTo<IDailyTimeIntervalTrigger>(because).Subject;
                daily.RepeatCount.Should().Be(schedule.RepeatCount!.Value, because);
                daily.RepeatInterval.Should().Be(schedule.RepeatInterval!.Value, because);
                daily.RepeatIntervalUnit.ToString().Should().Be(schedule.RepeatIntervalUnit, because);
                daily.StartTimeOfDay.ToString("HH:mm:ss", CultureInfo.InvariantCulture).Should().Be(schedule.StartTimeOfDay, because);
                daily.EndTimeOfDay.ToString("HH:mm:ss", CultureInfo.InvariantCulture).Should().Be(schedule.EndTimeOfDay, because);
                daily.DaysOfWeek.Select(x => x.ToString()).OrderBy(x => x, StringComparer.Ordinal)
                    .Should().BeEquivalentTo(schedule.DaysOfWeek, because);
                daily.TimeZone.Id.Should().Be(schedule.TimeZoneId, because);
                break;

            case "recurrence":
                IRecurrenceTrigger recurrence = trigger.Should().BeAssignableTo<IRecurrenceTrigger>(because).Subject;
                recurrence.RecurrenceRule.Should().Be(schedule.RecurrenceRule, because);
                recurrence.TimeZone.Id.Should().Be(schedule.TimeZoneId, because);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(seeded), schedule.Kind, "no assertion for this schedule kind");
        }
    }

    /// <summary>
    /// Paused groups across the upgrade, which the two kinds do not survive alike.
    /// </summary>
    /// <remarks>
    /// A paused <em>trigger</em> group is a <c>QRTZ_PAUSED_TRIGGER_GRPS</c> row on both versions, so it
    /// migrates with everything else and still governs what is stored into it afterwards. A paused
    /// <em>job</em> group is not stored anywhere on 3.x — its ADO store's <c>IsJobGroupPaused</c>
    /// returns a hard-coded false and <c>PauseJobs</c> only pauses the individual triggers — so 4.0's
    /// <c>QRTZ_PAUSED_JOB_GRPS</c> is empty after an upgrade and the group reads as not paused. The
    /// triggers that were paused stay paused; the *group* does not, so a trigger added to it after the
    /// upgrade is born running. That is asserted rather than wished away, because it is the upgrade
    /// fact an operator is most likely to be surprised by.
    /// </remarks>
    private static async Task AssertPausedGroupsAsync(IScheduler scheduler, SeedManifest manifest)
    {
        List<string> paused = await scheduler.GetPausedTriggerGroups();

        foreach (string group in manifest.PausedTriggerGroups)
        {
            paused.Should().Contain(group,
                "a paused trigger group is a row on both versions, so the upgrade carries it across");

            (await scheduler.IsTriggerGroupPaused(group)).Should().BeTrue();
        }

        foreach (string group in manifest.PausedJobGroups)
        {
            (await scheduler.IsJobGroupPaused(group)).Should().BeFalse(
                "3.x records nothing when it pauses a job group, so there is nothing for the migration to carry "
                + "into QRTZ_PAUSED_JOB_GRPS and the group reads as running again after the upgrade");
        }
    }

    /// <summary>
    /// What the two columns the 4.0 script adds hold on a row it brought across: nothing at all.
    /// </summary>
    /// <remarks>
    /// Naming both columns is also the existence check — the statement does not parse when one of them
    /// is missing. <c>RETRY_ATTEMPT</c> is null rather than zero here, which is the state only an
    /// upgraded row is ever in: every INSERT 4.0 writes names the attempt explicitly.
    /// </remarks>
    private static void AssertRetryColumnsAreEmpty(DbConnection connection, SeedManifest manifest)
    {
        long total = Count(connection,
            $"SELECT COUNT(*) FROM {RehearsalPrefix}TRIGGERS WHERE SCHED_NAME = '{manifest.SchedulerName}'");

        // Ties the rows to the manifest, so that a rehearsal reading an empty table cannot agree with
        // an empty manifest and report that everything survived.
        total.Should().Be(manifest.Triggers.Count + 1,
            "the migrated table holds every trigger the manifest lists, plus the one whose firing 3.20 abandoned");

        long empty = Count(connection,
            $"SELECT COUNT(*) FROM {RehearsalPrefix}TRIGGERS WHERE SCHED_NAME = '{manifest.SchedulerName}' "
            + "AND RETRY_POLICY IS NULL AND RETRY_ATTEMPT IS NULL");

        empty.Should().Be(total,
            "the 4.0 script adds RETRY_POLICY and RETRY_ATTEMPT as nullable with no default, so every row it "
            + "brought across from 3.x reads as no policy and no attempt behind it");
    }

    private static async Task AssertTheAbandonedFiringIsRecoveredAsync(
        DbConnection connection,
        IScheduler scheduler,
        SeedManifest manifest)
    {
        SeededFiredTrigger orphan = manifest.OrphanedFiredTrigger!;
        JobKey jobKey = new JobKey(orphan.JobName, orphan.JobGroup);

        await WaitUntil(() => UpgradeRehearsalJob.HasFired(jobKey),
            $"the firing 3.20 abandoned asks for recovery, so starting 4.0 has to run {jobKey} again rather than "
            + "leave a QRTZ_FIRED_TRIGGERS row nothing will ever clean up");

        long stranded = Count(connection,
            $"SELECT COUNT(*) FROM {RehearsalPrefix}FIRED_TRIGGERS WHERE SCHED_NAME = '{manifest.SchedulerName}' "
            + $"AND ENTRY_ID = '{orphan.FireInstanceId}'");

        stranded.Should().Be(0,
            "recovery takes the abandoned firing's row with it; a row left behind is one that would be recovered again on every start");

        _ = scheduler;
    }

    private static async Task AssertEveryTriggerFiresAsync(SeedManifest manifest)
    {
        List<SeededTrigger> due = manifest.Triggers.Where(x => x.ExpectFires).ToList();

        await WaitUntil(
            () => due.TrueForAll(x => UpgradeRehearsalJob.HasFired(new TriggerKey(x.Name, x.Group))),
            "every trigger the upgrade left runnable has to fire, which is the difference between a row that "
            + "reads back and a row that still schedules something");

        List<string> silent = due
            .Where(x => !UpgradeRehearsalJob.HasFired(new TriggerKey(x.Name, x.Group)))
            .Select(x => $"{x.Group}.{x.Name}")
            .ToList();

        silent.Should().BeEmpty($"every seeded trigger should fire on the upgraded schema within {FireTimeout.TotalSeconds:0} seconds");
    }

    private static async Task WaitUntil(Func<bool> condition, string because)
    {
        Stopwatch elapsed = Stopwatch.StartNew();

        while (elapsed.Elapsed < FireTimeout)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        condition().Should().BeTrue(because);
    }

    private static long Count(DbConnection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static async Task<IScheduler> BuildScheduler(string dialect, string connectionString, SeedManifest manifest)
    {
        return await QuartzSchedulerBuilder
            .Create(q => q
                .ConfigureScheduler(o =>
                {
                    o.InstanceName = manifest.SchedulerName;

                    // The same instance id the crashed 3.20 process used, which is what a single-node
                    // deployment restarting on 4.0 looks like -- and what lets RecoverJobs find the
                    // firing it abandoned.
                    o.InstanceId = manifest.InstanceId;
                })
                .UseDefaultThreadPool(x => x.MaxConcurrency = 5)
                .UseTypeLoader(o => o.Map(manifest.JobTypeName, typeof(UpgradeRehearsalJob)))
                .UsePersistentStore(store =>
                {
                    store.ConfigureStore(o =>
                    {
                        o.TablePrefix = RehearsalPrefix;
                        o.SchemaProvisioning = SchemaProvisioning.Validate;
                    });

                    MigratedSchemaWorkload.UseDialect(store, dialect, connectionString);

                    if (manifest.Serializer == "stj")
                    {
                        store.UseSystemTextJsonSerializer();
                    }
                    else
                    {
                        // The defaults a 3.x Newtonsoft deployment had: no trigger converters, so a
                        // blob is the plain object graph carrying $type, which is what 3.20 wrote.
                        store.UseNewtonsoftJsonSerializer();
                    }
                }))
            .BuildScheduler();
    }
}

/// <summary>
/// Stands in for the job type 3.20 stored, which named an assembly this process does not have.
/// </summary>
/// <remarks>
/// Public with a public constructor because the store hands the job factory nothing but the type the
/// type loader resolved out of <c>JOB_CLASS_NAME</c>.
/// </remarks>
public sealed class UpgradeRehearsalJob : IJob
{
    private static readonly ConcurrentDictionary<TriggerKey, int> triggerFires = new();
    private static readonly ConcurrentDictionary<JobKey, int> jobFires = new();

    public static void Reset()
    {
        triggerFires.Clear();
        jobFires.Clear();
    }

    public static bool HasFired(TriggerKey key) => triggerFires.ContainsKey(key);

    public static bool HasFired(JobKey key) => jobFires.ContainsKey(key);

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        triggerFires.AddOrUpdate(context.Trigger.Key, 1, static (_, count) => count + 1);
        jobFires.AddOrUpdate(context.JobDetail.Key, 1, static (_, count) => count + 1);

        return default;
    }
}
