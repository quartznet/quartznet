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

using System.Collections.Specialized;
using System.Data.Common;
using System.Globalization;

using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Impl.Matchers;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Integration.Seeder;

/// <summary>
/// Fills a 3.20 schema with the rows the 4.0 upgrade has to carry across, using a released
/// Quartz 3.20.0 and its own ADO job store.
/// </summary>
internal sealed class LegacySeeder
{
    internal const string JobGroup = "seed";
    internal const string TriggerGroup = "seed";
    internal const string PausedTriggerGroup = "pausedtriggers";
    internal const string PausedJobGroup = "pausedjobs";
    internal const string PausedJobTriggerGroup = "jobpaused";

    internal const string WorkerJobName = "worker";
    internal const string ExoticJobName = "exotic";
    internal const string OrphanJobName = "orphan";
    internal const string OrphanTriggerName = "orphan";

    private static readonly TimeSpan BlockedFiringTimeout = TimeSpan.FromSeconds(60);

    private readonly SeedOptions options;
    private readonly List<SeededTrigger> triggers = [];
    private readonly List<SeededCalendar> calendars = [];

    public LegacySeeder(SeedOptions options)
    {
        this.options = options;
    }

    public async Task<SeedManifest> RunAsync()
    {
        using DbConnection connection = LegacyDialect.OpenConnection(options.Dialect, options.ConnectionString);

        if (options.SchemaScript is not null)
        {
            SchemaScript.Apply(connection, options.Dialect, File.ReadAllText(options.SchemaScript));
        }

        IScheduler scheduler = await BuildScheduler().ConfigureAwait(false);

        SeededFiredTrigger orphan = await AbandonOneFiringAsync(scheduler, connection).ConfigureAwait(false);

        await AddCalendarsAsync(scheduler).ConfigureAwait(false);
        await AddJobsAndTriggersAsync(scheduler).ConfigureAwait(false);
        await AddPausedGroupsAsync(scheduler).ConfigureAwait(false);

        SeedManifest manifest = new SeedManifest
        {
            QuartzVersion = "3.20.0",
            Dialect = options.Dialect,
            Serializer = options.Serializer,
            TablePrefix = options.TablePrefix,
            SchedulerName = options.SchedulerName,
            InstanceId = options.InstanceId,
            JobTypeName = ReadStoredJobTypeName(connection),
            CapturedUtc = DateTimeOffset.UtcNow,
            Jobs = await ReadJobsAsync(scheduler).ConfigureAwait(false),
            Triggers = ReadTriggerRows(connection),
            Calendars = calendars,
            PausedTriggerGroups = ReadPausedTriggerGroups(connection),
            PausedJobGroups = [PausedJobGroup],
            OrphanedFiredTrigger = orphan
        };

        if (options.FixtureDirectory is not null)
        {
            manifest.BlobFixtures = BlobDump.Write(connection, options);
        }

        return manifest;
    }

    private async Task<IScheduler> BuildScheduler()
    {
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = options.SchedulerName,
            ["quartz.scheduler.instanceId"] = options.InstanceId,
            ["quartz.threadPool.type"] = "Quartz.Simpl.DefaultThreadPool, Quartz",
            ["quartz.threadPool.maxConcurrency"] = "4",
            ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz",
            ["quartz.jobStore.driverDelegateType"] = LegacyDialect.DriverDelegateType(options.Dialect),
            ["quartz.jobStore.dataSource"] = "default",
            ["quartz.jobStore.tablePrefix"] = options.TablePrefix,
            ["quartz.jobStore.useProperties"] = "false",
            ["quartz.jobStore.clustered"] = "false",
            ["quartz.dataSource.default.connectionString"] = options.ConnectionString,
            ["quartz.dataSource.default.provider"] = LegacyDialect.ProviderName(options.Dialect),
            ["quartz.serializer.type"] = options.Serializer
        };

        StdSchedulerFactory factory = new StdSchedulerFactory(properties);
        return await factory.GetScheduler().ConfigureAwait(false);
    }

    /// <summary>
    /// Leaves a <c>QRTZ_FIRED_TRIGGERS</c> row behind the way a crash does: one firing is started, the
    /// job blocks in it, and the process is killed with the row still there and the trigger still
    /// asking for recovery.
    /// </summary>
    /// <remarks>
    /// This runs first and on its own, so the only trigger the scheduler can acquire in the window
    /// between <c>Start</c> and <c>Standby</c> is this one. Everything else is stored afterwards,
    /// while the scheduler is in standby and acquiring nothing — which is what keeps the seeded state
    /// exactly what the manifest says it is.
    /// </remarks>
    private async Task<SeededFiredTrigger> AbandonOneFiringAsync(IScheduler scheduler, DbConnection connection)
    {
        IJobDetail job = JobBuilder.Create<LegacyWorkerJob>()
            .WithIdentity(OrphanJobName, JobGroup)
            .WithDescription("the firing this seeding run abandons")
            .StoreDurably()
            .RequestRecovery()
            .UsingJobData(LegacyWorkerJob.BlockKey, true)
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity(OrphanTriggerName, TriggerGroup)
            .ForJob(job)
            .StartNow()
            .WithSimpleSchedule(x => x.WithRepeatCount(0).WithInterval(TimeSpan.Zero))
            .Build();

        await scheduler.ScheduleJob(job, trigger).ConfigureAwait(false);
        await scheduler.Start().ConfigureAwait(false);

        Task blocked = LegacyWorkerJob.Blocked;
        Task completed = await Task.WhenAny(blocked, Task.Delay(BlockedFiringTimeout)).ConfigureAwait(false);
        if (completed != blocked)
        {
            throw new InvalidOperationException(
                $"The firing to abandon never reached the job within {BlockedFiringTimeout.TotalSeconds:0} seconds.");
        }

        await scheduler.Standby().ConfigureAwait(false);

        return ReadOrphanedFiredTrigger(connection);
    }

    private async Task AddCalendarsAsync(IScheduler scheduler)
    {
        AnnualCalendar annual = new AnnualCalendar { Description = "Seeded AnnualCalendar", TimeZone = TimeZoneInfo.Utc };
        annual.SetDayExcluded(new DateTime(2024, 7, 1, 0, 0, 0, DateTimeKind.Utc), true);
        annual.SetDayExcluded(new DateTime(2024, 12, 25, 0, 0, 0, DateTimeKind.Utc), true);

        HolidayCalendar holiday = new HolidayCalendar { Description = "Seeded HolidayCalendar", TimeZone = TimeZoneInfo.Utc };
        holiday.AddExcludedDate(new DateTime(2024, 7, 1, 0, 0, 0, DateTimeKind.Utc));
        holiday.AddExcludedDate(new DateTime(2024, 12, 25, 0, 0, 0, DateTimeKind.Utc));

        MonthlyCalendar monthly = new MonthlyCalendar { Description = "Seeded MonthlyCalendar", TimeZone = TimeZoneInfo.Utc };
        monthly.SetDayExcluded(10, true);
        monthly.SetDayExcluded(20, true);
        monthly.SetDayExcluded(23, true);
        monthly.SetDayExcluded(30, true);

        WeeklyCalendar weekly = new WeeklyCalendar { Description = "Seeded WeeklyCalendar", TimeZone = TimeZoneInfo.Utc };
        weekly.SetDayExcluded(DayOfWeek.Saturday, true);
        weekly.SetDayExcluded(DayOfWeek.Sunday, true);
        weekly.SetDayExcluded(DayOfWeek.Wednesday, true);

        DailyCalendar daily = new DailyCalendar("01:01:01:001", "02:02:02:002")
        {
            Description = "Seeded DailyCalendar",
            TimeZone = TimeZoneInfo.Utc
        };

        CronCalendar cron = new CronCalendar("0/5 * * * * ?")
        {
            Description = "Seeded CronCalendar",
            TimeZone = TimeZoneInfo.Utc
        };

        // The chained pair: a holiday calendar answering on top of a cron one. Both halves have to
        // come out of the single blob the CALENDAR column holds.
        CronCalendar chainedBase = new CronCalendar("0/5 * * * * ?")
        {
            Description = "Seeded chained base",
            TimeZone = TimeZoneInfo.Utc
        };

        HolidayCalendar chained = new HolidayCalendar(chainedBase)
        {
            Description = "Seeded chained HolidayCalendar",
            TimeZone = TimeZoneInfo.Utc
        };
        chained.AddExcludedDate(new DateTime(2024, 7, 1, 0, 0, 0, DateTimeKind.Utc));

        await AddCalendarAsync(scheduler, "annual", annual,
            Utc(2024, 7, 1, 12, 0, 0), Utc(2024, 8, 5, 12, 0, 0), Utc(2025, 12, 25, 9, 0, 0));

        await AddCalendarAsync(scheduler, "holiday", holiday,
            Utc(2024, 7, 1, 12, 0, 0), Utc(2024, 7, 2, 12, 0, 0), Utc(2024, 12, 25, 6, 0, 0));

        await AddCalendarAsync(scheduler, "monthly", monthly,
            Utc(2024, 3, 10, 12, 0, 0), Utc(2024, 3, 11, 12, 0, 0), Utc(2024, 3, 30, 12, 0, 0));

        await AddCalendarAsync(scheduler, "weekly", weekly,
            Utc(2024, 7, 3, 12, 0, 0), Utc(2024, 7, 4, 12, 0, 0), Utc(2024, 7, 6, 12, 0, 0));

        await AddCalendarAsync(scheduler, "daily", daily,
            Utc(2024, 7, 1, 1, 30, 0), Utc(2024, 7, 1, 3, 0, 0), Utc(2024, 7, 1, 2, 30, 0));

        await AddCalendarAsync(scheduler, "cron", cron,
            Utc(2024, 7, 1, 12, 0, 0), Utc(2024, 7, 1, 12, 0, 1), Utc(2024, 7, 1, 12, 0, 5));

        await AddCalendarAsync(scheduler, "chained", chained,
            Utc(2024, 7, 1, 12, 0, 1), Utc(2025, 7, 2, 12, 0, 3), Utc(2025, 7, 2, 12, 0, 5));
    }

    private async Task AddCalendarAsync(IScheduler scheduler, string name, BaseCalendar calendar, params DateTimeOffset[] probes)
    {
        await scheduler.AddCalendar(name, calendar, replace: true, updateTriggers: false).ConfigureAwait(false);

        SeededCalendar seeded = new SeededCalendar
        {
            Name = name,
            Kind = calendar.GetType().Name,
            Description = calendar.Description,
            HasBaseCalendar = calendar.CalendarBase is not null,
            BaseCalendarKind = calendar.CalendarBase?.GetType().Name,
            Probes = probes
                .Select(p => new SeededCalendarProbe { Instant = p, Included = calendar.IsTimeIncluded(p) })
                .ToList()
        };

        // A probe set that answers the same everywhere proves nothing about the blob, so refuse to
        // record one: a calendar that deserialized into "include everything" would pass it.
        if (seeded.Probes.TrueForAll(p => p.Included) || seeded.Probes.TrueForAll(p => !p.Included))
        {
            throw new InvalidOperationException($"The probes for calendar '{name}' all answer the same, so they cannot detect a calendar that lost its exclusions.");
        }

        calendars.Add(seeded);
    }

    private async Task AddJobsAndTriggersAsync(IScheduler scheduler)
    {
        IJobDetail worker = JobBuilder.Create<LegacyWorkerJob>()
            .WithIdentity(WorkerJobName, JobGroup)
            .WithDescription("the job every seeded trigger points at")
            .StoreDurably()
            .UsingJobData(SeedValues.Build())
            .Build();

        await scheduler.AddJob(worker, replace: true).ConfigureAwait(false);

        // The value 4.0's write gate would refuse, on a job with no trigger of its own so that one
        // unreadable entry cannot take the rest of the rehearsal down with it.
        IJobDetail exotic = JobBuilder.Create<LegacyWorkerJob>()
            .WithIdentity(ExoticJobName, JobGroup)
            .WithDescription("job data 4.0 would not write, but a 3.x database can hold")
            .StoreDurably()
            .UsingJobData(SeedValues.BuildOutsideTheWriteGate())
            .Build();

        await scheduler.AddJob(exotic, replace: true).ConfigureAwait(false);

        DateTimeOffset startAt = DateTimeOffset.UtcNow;

        // One trigger of each family the ADO store has a persistence delegate for, and then the
        // families BlobStorageOverride can blob-store on this serializer, so QRTZ_BLOB_TRIGGERS holds
        // a payload of every shape too.
        foreach (string family in AllFamilies)
        {
            await ScheduleAsync(scheduler, BuildTrigger(family, TriggerGroup, worker, startAt)).ConfigureAwait(false);
        }

        foreach (string family in BlobStorageOverride.Families(options.Serializer))
        {
            await ScheduleAsync(scheduler, BuildTrigger(family, BlobStorageOverride.Group, worker, startAt)).ConfigureAwait(false);
        }

        // EXECUTION_GROUP (3.18) and PREFERRED_NODE (3.19): both columns exist on 3.20 and both have to
        // survive the upgrade with what 3.20 put in them.
        await ScheduleAsync(scheduler, TriggerBuilder.Create()
            .WithIdentity("pinned", TriggerGroup)
            .ForJob(worker)
            .WithDescription("pinned trigger")
            .StartAt(startAt)
            .WithExecutionGroup("seeded-execution-group")
            .WithPreferredNode(options.InstanceId)
            .WithSimpleSchedule(x => x
                .WithInterval(TimeSpan.FromSeconds(1))
                .RepeatForever()
                .WithMisfireHandlingInstructionFireNow())
            .Build()).ConfigureAwait(false);
    }

    /// <summary>Every family 3.20's ADO store has a persistence delegate for.</summary>
    private static readonly string[] AllFamilies =
        ["simple", "cron", "calendar-interval", "daily-time-interval", "recurrence"];

    private static ITrigger BuildTrigger(string family, string group, IJobDetail job, DateTimeOffset startAt)
    {
        TriggerBuilder builder = TriggerBuilder.Create()
            .WithIdentity(family, group)
            .ForJob(job)
            .StartAt(startAt);

        switch (family)
        {
            case "simple":
                return builder
                    .WithDescription("simple trigger")
                    .ModifiedByCalendar("holiday")
                    .WithPriority(3)
                    .UsingJobData("triggerMarker", "on the trigger")
                    .UsingJobData("triggerNumber", 7)
                    .WithSimpleSchedule(x => x
                        .WithInterval(TimeSpan.FromSeconds(1))
                        .RepeatForever()
                        .WithMisfireHandlingInstructionFireNow())
                    .Build();

            case "cron":
                return builder
                    .WithDescription("cron trigger")
                    .WithPriority(7)
                    .WithCronSchedule("0/1 * * * * ?", x => x
                        .InTimeZone(TimeZoneInfo.Utc)
                        .WithMisfireHandlingInstructionFireAndProceed())
                    .Build();

            case "calendar-interval":
                return builder
                    .WithDescription("calendar interval trigger")
                    .WithCalendarIntervalSchedule(x => x
                        .WithInterval(1, IntervalUnit.Second)
                        .InTimeZone(TimeZoneInfo.Utc)
                        .PreserveHourOfDayAcrossDaylightSavings(true)
                        .SkipDayIfHourDoesNotExist(false)
                        .WithMisfireHandlingInstructionFireAndProceed())
                    .Build();

            case "daily-time-interval":
                return builder
                    .WithDescription("daily time interval trigger")
                    .WithDailyTimeIntervalSchedule(x => x
                        .WithInterval(1, IntervalUnit.Second)
                        .OnEveryDay()
                        .StartingDailyAt(TimeOfDay.HourMinuteAndSecondOfDay(0, 0, 0))
                        .EndingDailyAt(TimeOfDay.HourMinuteAndSecondOfDay(23, 59, 59))
                        .InTimeZone(TimeZoneInfo.Utc)
                        .WithMisfireHandlingInstructionFireAndProceed())
                    .Build();

            case "recurrence":
                return builder
                    .WithDescription("recurrence trigger")
                    .WithRecurrenceSchedule("FREQ=SECONDLY;INTERVAL=1", x => x
                        .InTimeZone(TimeZoneInfo.Utc)
                        .WithMisfireHandlingInstructionFireAndProceed())
                    .Build();

            default:
                throw new ArgumentOutOfRangeException(nameof(family), family, "no trigger of this family");
        }
    }

    private async Task AddPausedGroupsAsync(IScheduler scheduler)
    {
        IJobDetail worker = (await scheduler.GetJobDetail(new JobKey(WorkerJobName, JobGroup)).ConfigureAwait(false))!;
        DateTimeOffset startAt = DateTimeOffset.UtcNow;

        // A trigger stored before the group was paused, and one stored after it: the second is the one
        // that can only be paused because the group is, which is what QRTZ_PAUSED_TRIGGER_GRPS is for.
        await ScheduleAsync(scheduler, PausedTrigger("before", PausedTriggerGroup, worker, startAt)).ConfigureAwait(false);

        await scheduler.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals(PausedTriggerGroup)).ConfigureAwait(false);

        await ScheduleAsync(scheduler, PausedTrigger("after", PausedTriggerGroup, worker, startAt)).ConfigureAwait(false);

        // The same shape for a job group. 3.x records nothing when it pauses one, so the trigger stored
        // afterwards is *not* paused — the rehearsal pins that, because it is the upgrade fact an
        // operator is most likely to be surprised by.
        IJobDetail before = JobBuilder.Create<LegacyWorkerJob>()
            .WithIdentity("before", PausedJobGroup)
            .StoreDurably()
            .Build();

        await scheduler.AddJob(before, replace: true).ConfigureAwait(false);
        await ScheduleAsync(scheduler, PausedTrigger("job-before", PausedJobTriggerGroup, before, startAt)).ConfigureAwait(false);

        await scheduler.PauseJobs(GroupMatcher<JobKey>.GroupEquals(PausedJobGroup)).ConfigureAwait(false);

        IJobDetail after = JobBuilder.Create<LegacyWorkerJob>()
            .WithIdentity("after", PausedJobGroup)
            .StoreDurably()
            .Build();

        await scheduler.AddJob(after, replace: true).ConfigureAwait(false);
        await ScheduleAsync(scheduler, PausedTrigger("job-after", PausedJobTriggerGroup, after, startAt)).ConfigureAwait(false);
    }

    private static ITrigger PausedTrigger(string name, string group, IJobDetail job, DateTimeOffset startAt)
    {
        return TriggerBuilder.Create()
            .WithIdentity(name, group)
            .ForJob(job)
            .StartAt(startAt)
            .WithSimpleSchedule(x => x
                .WithInterval(TimeSpan.FromSeconds(1))
                .RepeatForever()
                .WithMisfireHandlingInstructionFireNow())
            .Build();
    }

    /// <summary>
    /// Stores a trigger and records what 3.20 read back for it, family fields included.
    /// </summary>
    private async Task ScheduleAsync(IScheduler scheduler, ITrigger trigger)
    {
        await scheduler.ScheduleJob(trigger).ConfigureAwait(false);

        ITrigger stored = (await scheduler.GetTrigger(trigger.Key).ConfigureAwait(false))!;
        AbstractTrigger concrete = (AbstractTrigger) stored;

        triggers.Add(new SeededTrigger
        {
            Name = stored.Key.Name,
            Group = stored.Key.Group,
            JobName = stored.JobKey.Name,
            JobGroup = stored.JobKey.Group,
            Description = stored.Description,
            CalendarName = stored.CalendarName,
            Priority = stored.Priority,
            MisfireInstruction = stored.MisfireInstruction,
            ExecutionGroup = concrete.ExecutionGroup,
            PreferredNode = concrete.PreferredNode,
            Schedule = DescribeSchedule(stored)
        });
    }

    private static SeededSchedule DescribeSchedule(ITrigger trigger)
    {
        switch (trigger)
        {
            case ISimpleTrigger simple:
                return new SeededSchedule
                {
                    Kind = "simple",
                    RepeatCount = simple.RepeatCount,
                    RepeatIntervalMilliseconds = (long) simple.RepeatInterval.TotalMilliseconds
                };
            case ICronTrigger cron:
                return new SeededSchedule
                {
                    Kind = "cron",
                    CronExpression = cron.CronExpressionString,
                    TimeZoneId = cron.TimeZone.Id
                };
            case IDailyTimeIntervalTrigger daily:
                return new SeededSchedule
                {
                    Kind = "dailyTimeInterval",
                    RepeatCount = daily.RepeatCount,
                    RepeatInterval = daily.RepeatInterval,
                    RepeatIntervalUnit = daily.RepeatIntervalUnit.ToString(),
                    StartTimeOfDay = Format(daily.StartTimeOfDay),
                    EndTimeOfDay = Format(daily.EndTimeOfDay),
                    DaysOfWeek = daily.DaysOfWeek.Select(d => d.ToString()).OrderBy(d => d, StringComparer.Ordinal).ToList(),
                    TimeZoneId = daily.TimeZone.Id
                };
            case ICalendarIntervalTrigger calendarInterval:
                return new SeededSchedule
                {
                    Kind = "calendarInterval",
                    RepeatInterval = calendarInterval.RepeatInterval,
                    RepeatIntervalUnit = calendarInterval.RepeatIntervalUnit.ToString(),
                    TimeZoneId = calendarInterval.TimeZone.Id,
                    PreserveHourOfDayAcrossDaylightSavings = calendarInterval.PreserveHourOfDayAcrossDaylightSavings,
                    SkipDayIfHourDoesNotExist = calendarInterval.SkipDayIfHourDoesNotExist
                };
            case IRecurrenceTrigger recurrence:
                return new SeededSchedule
                {
                    Kind = "recurrence",
                    RecurrenceRule = recurrence.RecurrenceRule,
                    TimeZoneId = recurrence.TimeZone.Id
                };
            default:
                throw new ArgumentOutOfRangeException(nameof(trigger), trigger.GetType().FullName, "unrecognised trigger family");
        }
    }

    private static string Format(TimeOfDay timeOfDay)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{timeOfDay.Hour:00}:{timeOfDay.Minute:00}:{timeOfDay.Second:00}");
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute, int second)
    {
        return new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero);
    }

    // ---------------------------------------------------------------------------------------------
    // Ground truth, read out of the tables rather than out of the seeder's intentions.
    // ---------------------------------------------------------------------------------------------

    private static async Task<List<SeededJob>> ReadJobsAsync(IScheduler scheduler)
    {
        return
        [
            await ReadJobAsync(scheduler, WorkerJobName, JobGroup, SeedValues.Describe()).ConfigureAwait(false),
            await ReadJobAsync(scheduler, ExoticJobName, JobGroup, SeedValues.DescribeOutsideTheWriteGate()).ConfigureAwait(false)
        ];
    }

    private static async Task<SeededJob> ReadJobAsync(IScheduler scheduler, string name, string group, List<SeededDataValue> values)
    {
        IJobDetail job = (await scheduler.GetJobDetail(new JobKey(name, group)).ConfigureAwait(false))!;

        return new SeededJob
        {
            Name = job.Key.Name,
            Group = job.Key.Group,
            Description = job.Description,
            Durable = job.Durable,
            RequestsRecovery = job.RequestsRecovery,
            ConcurrentExecutionDisallowed = job.ConcurrentExecutionDisallowed,
            JobDataMap = values
        };
    }

    /// <summary>
    /// The stored spelling of the job type, verbatim, so the rehearsal aliases exactly what 3.20 wrote
    /// rather than a name reconstructed from this assembly's metadata.
    /// </summary>
    private string ReadStoredJobTypeName(DbConnection connection)
    {
        return ReadScalar(connection,
            $"SELECT JOB_CLASS_NAME FROM {options.TablePrefix}JOB_DETAILS WHERE SCHED_NAME = {Literal(options.SchedulerName)} "
            + $"AND JOB_NAME = {Literal(WorkerJobName)} AND JOB_GROUP = {Literal(JobGroup)}")
            ?? throw new InvalidOperationException("The worker job has no JOB_CLASS_NAME row.");
    }

    private List<SeededTrigger> ReadTriggerRows(DbConnection connection)
    {
        foreach (SeededTrigger trigger in triggers)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText =
                $"SELECT TRIGGER_TYPE, TRIGGER_STATE FROM {options.TablePrefix}TRIGGERS "
                + $"WHERE SCHED_NAME = {Literal(options.SchedulerName)} AND TRIGGER_NAME = {Literal(trigger.Name)} "
                + $"AND TRIGGER_GROUP = {Literal(trigger.Group)}";

            using DbDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidOperationException($"Trigger {trigger.Group}.{trigger.Name} was stored but has no row.");
            }

            trigger.TriggerType = reader.GetString(0).Trim();
            trigger.TriggerState = reader.GetString(1).Trim();
            trigger.ExpectFires = trigger.TriggerState == "WAITING";
        }

        return triggers;
    }

    private List<string> ReadPausedTriggerGroups(DbConnection connection)
    {
        List<string> groups = [];

        using DbCommand command = connection.CreateCommand();
        command.CommandText =
            $"SELECT TRIGGER_GROUP FROM {options.TablePrefix}PAUSED_TRIGGER_GRPS WHERE SCHED_NAME = {Literal(options.SchedulerName)}";

        using DbDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            groups.Add(reader.GetString(0).Trim());
        }

        groups.Sort(StringComparer.Ordinal);
        return groups;
    }

    private SeededFiredTrigger ReadOrphanedFiredTrigger(DbConnection connection)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText =
            $"SELECT ENTRY_ID, INSTANCE_NAME, TRIGGER_NAME, TRIGGER_GROUP, JOB_NAME, JOB_GROUP, STATE, REQUESTS_RECOVERY "
            + $"FROM {options.TablePrefix}FIRED_TRIGGERS WHERE SCHED_NAME = {Literal(options.SchedulerName)}";

        using DbDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("The abandoned firing left no QRTZ_FIRED_TRIGGERS row behind.");
        }

        SeededFiredTrigger fired = new SeededFiredTrigger
        {
            FireInstanceId = reader.GetString(0).Trim(),
            InstanceName = reader.GetString(1).Trim(),
            TriggerName = reader.GetString(2).Trim(),
            TriggerGroup = reader.GetString(3).Trim(),
            JobName = reader.IsDBNull(4) ? "" : reader.GetString(4).Trim(),
            JobGroup = reader.IsDBNull(5) ? "" : reader.GetString(5).Trim(),
            State = reader.GetString(6).Trim(),
            RequestsRecovery = IsTrue(reader.GetValue(7))
        };

        if (reader.Read())
        {
            throw new InvalidOperationException("More than one firing was left behind; the seeded state would not be deterministic.");
        }

        return fired;
    }

    private static bool IsTrue(object value)
    {
        // Every dialect spells this column differently: a bit, a boolean, a one-character string.
        return value switch
        {
            bool flag => flag,
            string text => text is "1" or "t" or "T" or "true" or "TRUE",
            _ => Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0
        };
    }

    private static string? ReadScalar(DbConnection connection, string sql)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;

        object? value = command.ExecuteScalar();
        return value is null or DBNull ? null : ((string) value).Trim();
    }

    /// <summary>
    /// A SQL string literal. The seeder's own names are the only things that reach it, and quoting
    /// them beats binding a parameter whose marker is a different character in every dialect.
    /// </summary>
    internal static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
