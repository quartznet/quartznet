using FakeItEasy;

using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Tests.AspNetCore.Support;

public static class TestData
{
    public const string SchedulerName = "TestScheduler";
    public const string SchedulerInstanceId = "TEST_NON_CLUSTERED";

    /// <summary>
    /// A job type name that nothing in the test process resolves. The API carries job types as names, so
    /// a request naming this one is accepted and only fails when something has to run the job.
    /// </summary>
    public const string UnresolvableJobTypeName = "Quartz.Tests.AspNetCore.Support.DummyJob2, Quartz.Tests.AspNetCore";

    public static readonly SchedulerMetadata Metadata;

    public static readonly BaseCalendar BaseCalendar;
    public static readonly AnnualCalendar AnnualCalendar;
    public static readonly CronCalendar CronCalendar;
    public static readonly DailyCalendar DailyCalendar;
    public static readonly HolidayCalendar HolidayCalendar;
    public static readonly MonthlyCalendar MonthlyCalendar;
    public static readonly WeeklyCalendar WeeklyCalendar;

    public static readonly IJobDetail JobDetail;
    public static readonly IJobDetail JobDetail2;

    public static readonly ITrigger CalendarIntervalTrigger;
    public static readonly ITrigger CronTrigger;
    public static readonly ITrigger DailyTimeIntervalTrigger;
    public static readonly ITrigger SimpleTrigger;

    public static readonly IJobExecutionContext ExecutingJobOne;
    public static readonly IJobExecutionContext ExecutingJobTwo;

    static TestData()
    {
        Metadata = new SchedulerMetadata
        {
            SchedulerName = SchedulerName,
            SchedulerInstanceId = SchedulerInstanceId,
            SchedulerTypeName = typeof(IScheduler).AssemblyQualifiedNameWithoutVersion(),
            IsProxy = false,
            Started = true,
            InStandbyMode = false,
            Shutdown = false,
            RunningSince = DateTimeOffset.Now.AddDays(-1),
            JobsExecuted = 1_000_000,
            JobStoreTypeName = typeof(RAMJobStore).AssemblyQualifiedNameWithoutVersion(),
            JobStorePersistent = false,
            JobStoreClustered = false,
            ThreadPoolTypeName = typeof(DefaultThreadPool).AssemblyQualifiedNameWithoutVersion(),
            ThreadPoolSize = 10,
            Version = "1.2.3",
        };

        BaseCalendar = new BaseCalendar
        {
            TimeZone = TimeZoneInfo.Utc,
            Description = "Test BaseCalendar"
        };

        AnnualCalendar = new AnnualCalendar
        {
            TimeZone = TimeZoneInfo.Utc,
            Description = "Test AnnualCalendar",
            CalendarBase = BaseCalendar
        };
        AnnualCalendar.AddExcludedDay(MonthDay.From(DateOnly.FromDateTime(DateTime.Today)));

        CronCalendar = new CronCalendar("0 0 * * * ?")
        {
            TimeZone = TimeZoneInfo.Local,
            Description = "Test CronCalendar",
            CalendarBase = null
        };

        DailyCalendar = new DailyCalendar(new TimeOnly(10, 0, 0), new TimeOnly(12, 30, 0))
        {
            TimeZone = TimeZoneInfo.Utc,
            Description = null,
            CalendarBase = BaseCalendar,
            InvertTimeRange = true
        };

        HolidayCalendar = new HolidayCalendar
        {
            TimeZone = TimeZoneInfo.Utc,
            Description = "Test HolidayCalendar",
            CalendarBase = BaseCalendar
        };
        HolidayCalendar.AddExcludedDay(DateOnly.FromDateTime(DateTime.Today));

        MonthlyCalendar = new MonthlyCalendar
        {
            TimeZone = TimeZoneInfo.Utc,
            Description = "Test MonthlyCalendar",
            CalendarBase = BaseCalendar
        };
        MonthlyCalendar.AddExcludedDay(10);
        MonthlyCalendar.AddExcludedDay(20);
        MonthlyCalendar.AddExcludedDay(30);

        WeeklyCalendar = new WeeklyCalendar
        {
            TimeZone = TimeZoneInfo.Utc,
            Description = "Test WeeklyCalendar",
            CalendarBase = BaseCalendar
        };
        WeeklyCalendar.AddExcludedDay(DayOfWeek.Wednesday);
        WeeklyCalendar.AddExcludedDay(DayOfWeek.Thursday);
        WeeklyCalendar.AddExcludedDay(DayOfWeek.Friday);

        JobDetail = JobBuilder.Create<DummyJob>()
            .WithIdentity("DummyJob", "DummyGroup")
            .WithDescription("Dummy job description")
            .StoreDurably(true)
            .RequestRecovery(true)
            .DisallowConcurrentExecution(true)
            .PersistJobDataAfterExecution(true)
            .UsingJobData("TestKey", "TestValue")
            .Build();

        JobDetail2 = JobBuilder.Create<DummyJob>()
            .WithIdentity("DummyJob2", "DummyGroup2")
            .WithDescription("Dummy job 2 description")
            .StoreDurably(true)
            .RequestRecovery(false)
            .DisallowConcurrentExecution(true)
            .PersistJobDataAfterExecution(false)
            .UsingJobData("TestKey", "180")
            .Build();

        CalendarIntervalTrigger = TriggerBuilder.Create()
            .WithCalendarIntervalSchedule(builder => builder
                .WithInterval(10, IntervalUnit.Minute)
                .InTimeZone(TimeZoneInfo.Utc)
                .PreserveHourOfDayAcrossDaylightSavings(true)
                .SkipDayIfHourDoesNotExist(false)
            )
            .WithIdentity("CalendarIntervalTriggerKey", "CalendarIntervalTriggerGroup")
            .ForJob("CalendarIntervalJobKey", "CalendarIntervalJobGroup")
            .WithDescription("CalendarIntervalTrigger description")
            .WithCalendarName("SomeCalendar")
            .UsingJobData("TestKey", "TestValue")
            .EndAt(null)
            .StartAt(DateTimeOffset.Now)
            .WithPriority(10)
            .Build();

        CronTrigger = TriggerBuilder.Create()
            .WithCronSchedule("0/25 * * * * ?", builder => builder
                .InTimeZone(TimeZoneInfo.Local)
            )
            .WithIdentity("CronTriggerKey", "CronTriggerGroup")
            .ForJob("CronJobKey", "CronJobGroup")
            .WithDescription(null)
            .WithCalendarName(null)
            .EndAt(DateTimeOffset.Now.AddDays(5))
            .StartAt(DateTimeOffset.Now.AddDays(-5))
            .WithPriority(1)
            .Build();

        DailyTimeIntervalTrigger = TriggerBuilder.Create()
            .WithDailyTimeIntervalSchedule(builder => builder
                .WithRepeatCount(1_000)
                .WithInterval(5, IntervalUnit.Hour)
                .StartingDailyAt(new TimeOnly(10, 0, 0))
                .EndingDailyAt(new TimeOnly(20, 0, 0))
                .OnDaysOfTheWeek(DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday)
                .InTimeZone(TimeZoneInfo.Utc)
            )
            .WithIdentity("DailyTimeIntervalTriggerKey", "DailyTimeIntervalTriggerGroup")
            .WithDescription("DailyTimeIntervalTrigger description")
            .WithCalendarName(null)
            .EndAt(null)
            .StartAt(DateTimeOffset.Now.AddDays(-5))
            .Build();

        SimpleTrigger = TriggerBuilder.Create()
            .WithSimpleSchedule(builder => builder
                .WithInterval(new TimeSpan(120, 2, 30, 59, 999))
                .WithRepeatCount(1_000)
            )
            .WithIdentity("SimpleTriggerKey", "SimpleTriggerGroup")
            .ForJob("SimpleJobKey", "SimpleJobGroup")
            .WithDescription("SimpleTrigger description")
            .WithCalendarName("SomeOtherCalendar")
            .UsingJobData("TestKey", "150")
            .EndAt(DateTimeOffset.Now.AddYears(1_000))
            .StartAt(DateTimeOffset.Now)
            .WithPriority(150_000)
            .Build();

        ExecutingJobOne = new JobExecutionContextImpl(
            scheduler: A.Fake<IScheduler>(),
            firedBundle: new TriggerFiredBundle
            {
                JobDetail = JobDetail,
                Trigger = (IOperableTrigger) CronTrigger,
                Calendar = CronCalendar,
                Recovering = false,
                FireTimeUtc = DateTimeOffset.Now.AddSeconds(-1),
                ScheduledFireTimeUtc = DateTimeOffset.Now.AddSeconds(-1),
                PreviousFireTimeUtc = DateTimeOffset.Now.AddMinutes(-10),
                NextFireTimeUtc = DateTimeOffset.Now.AddMinutes(10)
            },
            job: new DummyJob()
        );

        ExecutingJobTwo = new JobExecutionContextImpl(
            scheduler: A.Fake<IScheduler>(),
            firedBundle: new TriggerFiredBundle
            {
                JobDetail = JobDetail2,
                Trigger = (IOperableTrigger) SimpleTrigger,
                Calendar = null,
                Recovering = true,
                FireTimeUtc = DateTimeOffset.Now.AddSeconds(-5),
                ScheduledFireTimeUtc = null,
                PreviousFireTimeUtc = null,
                NextFireTimeUtc = null
            },
            job: new DummyJob()
        );
    }

    /// <summary>
    /// Test data for the tests that snapshot the bytes the HTTP API puts on the wire.
    /// </summary>
    /// <remarks>
    /// The rest of <see cref="TestData" /> is built off the wall clock and the machine's time zone, which
    /// costs those tests nothing because they only round-trip it. A wire snapshot has to compare equal
    /// tomorrow, and on a machine in another time zone, so everything here is a constant instead: fixed
    /// instants, UTC throughout, and fire times assigned rather than computed. Anything left to move
    /// would have to be scrubbed, and a scrubbed field is a field the snapshot no longer pins.
    /// </remarks>
    public static class Wire
    {
        public static readonly DateTimeOffset StartTime = new(2024, 7, 1, 12, 0, 0, TimeSpan.Zero);
        public static readonly DateTimeOffset EndTime = new(2025, 7, 1, 12, 0, 0, TimeSpan.Zero);
        public static readonly DateTimeOffset PreviousFireTime = new(2024, 8, 1, 6, 30, 0, TimeSpan.Zero);
        public static readonly DateTimeOffset NextFireTime = new(2024, 8, 1, 12, 30, 0, TimeSpan.Zero);

        public static readonly SchedulerMetadata Metadata;

        public static readonly HolidayCalendar HolidayCalendar;

        public static readonly ITrigger SimpleTrigger;
        public static readonly ITrigger CronTrigger;
        public static readonly ITrigger CalendarIntervalTrigger;
        public static readonly ITrigger DailyTimeIntervalTrigger;
        public static readonly ITrigger RecurrenceTrigger;

        static Wire()
        {
            Metadata = new SchedulerMetadata
            {
                SchedulerName = SchedulerName,
                SchedulerInstanceId = SchedulerInstanceId,
                SchedulerTypeName = typeof(IScheduler).AssemblyQualifiedNameWithoutVersion(),
                IsProxy = false,
                Started = true,
                InStandbyMode = false,
                Shutdown = false,
                RunningSince = StartTime,
                JobsExecuted = 1_000_000,
                JobStoreTypeName = typeof(RAMJobStore).AssemblyQualifiedNameWithoutVersion(),
                JobStorePersistent = false,
                JobStoreClustered = false,
                ThreadPoolTypeName = typeof(DefaultThreadPool).AssemblyQualifiedNameWithoutVersion(),
                ThreadPoolSize = 10,
                Version = "1.2.3",
            };

            HolidayCalendar = new HolidayCalendar
            {
                TimeZone = TimeZoneInfo.Utc,
                Description = "Test HolidayCalendar",
                // chained, so the snapshot also pins how a base calendar nests on the wire
                CalendarBase = new BaseCalendar
                {
                    TimeZone = TimeZoneInfo.Utc,
                    Description = "Test BaseCalendar"
                }
            };
            HolidayCalendar.AddExcludedDay(new DateOnly(2024, 12, 24));
            HolidayCalendar.AddExcludedDay(new DateOnly(2024, 12, 25));

            SimpleTrigger = WithFireTimes(TriggerBuilder.Create()
                .WithSimpleSchedule(builder => builder
                    .WithInterval(new TimeSpan(120, 2, 30, 59, 999))
                    .WithRepeatCount(1_000)
                )
                .WithIdentity("SimpleTriggerKey", "SimpleTriggerGroup")
                .ForJob("SimpleJobKey", "SimpleJobGroup")
                .WithDescription("SimpleTrigger description")
                .WithCalendarName("SomeOtherCalendar")
                .UsingJobData("TestKey", "150")
                .StartAt(StartTime)
                .EndAt(EndTime)
                .WithPriority(150_000)
                .Build());

            CronTrigger = WithFireTimes(TriggerBuilder.Create()
                .WithCronSchedule("0/25 * * * * ?", builder => builder
                    .InTimeZone(TimeZoneInfo.Utc)
                )
                .WithIdentity("CronTriggerKey", "CronTriggerGroup")
                .ForJob("CronJobKey", "CronJobGroup")
                .WithDescription("CronTrigger description")
                .WithCalendarName("SomeCalendar")
                .StartAt(StartTime)
                .EndAt(EndTime)
                .WithPriority(1)
                .Build());

            CalendarIntervalTrigger = WithFireTimes(TriggerBuilder.Create()
                .WithCalendarIntervalSchedule(builder => builder
                    .WithInterval(10, IntervalUnit.Minute)
                    .InTimeZone(TimeZoneInfo.Utc)
                    .PreserveHourOfDayAcrossDaylightSavings(true)
                    .SkipDayIfHourDoesNotExist(false)
                )
                .WithIdentity("CalendarIntervalTriggerKey", "CalendarIntervalTriggerGroup")
                .ForJob("CalendarIntervalJobKey", "CalendarIntervalJobGroup")
                .WithDescription("CalendarIntervalTrigger description")
                .WithCalendarName("SomeCalendar")
                .UsingJobData("TestKey", "TestValue")
                .StartAt(StartTime)
                .EndAt(null)
                .WithPriority(10)
                .Build());

            DailyTimeIntervalTrigger = WithFireTimes(TriggerBuilder.Create()
                .WithDailyTimeIntervalSchedule(builder => builder
                    .WithRepeatCount(1_000)
                    .WithInterval(5, IntervalUnit.Hour)
                    .StartingDailyAt(new TimeOnly(10, 0, 0))
                    .EndingDailyAt(new TimeOnly(20, 0, 0))
                    .OnDaysOfTheWeek(DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday)
                    .InTimeZone(TimeZoneInfo.Utc)
                )
                .WithIdentity("DailyTimeIntervalTriggerKey", "DailyTimeIntervalTriggerGroup")
                .ForJob("DailyTimeIntervalJobKey", "DailyTimeIntervalJobGroup")
                .WithDescription("DailyTimeIntervalTrigger description")
                .WithCalendarName(null)
                .StartAt(StartTime)
                .EndAt(null)
                .Build());

            RecurrenceTrigger = WithFireTimes(TriggerBuilder.Create()
                .WithRecurrenceSchedule("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR", builder => builder
                    .InTimeZone(TimeZoneInfo.Utc)
                )
                .WithIdentity("RecurrenceTriggerKey", "RecurrenceTriggerGroup")
                .ForJob("RecurrenceJobKey", "RecurrenceJobGroup")
                .WithDescription("RecurrenceTrigger description")
                .WithCalendarName(null)
                .StartAt(StartTime)
                .EndAt(EndTime)
                .WithPriority(5)
                .Build());
        }

        /// <summary>
        /// Assigns the fire times the scheduler would otherwise have advanced, so that the snapshot pins
        /// them as values rather than as nulls.
        /// </summary>
        private static ITrigger WithFireTimes(ITrigger trigger)
        {
            IMutableTrigger mutableTrigger = (IMutableTrigger) trigger;
            mutableTrigger.PreviousFireTimeUtc = PreviousFireTime;
            mutableTrigger.NextFireTimeUtc = NextFireTime;
            return trigger;
        }
    }
}