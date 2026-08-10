using FakeItEasy;

using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Extensibility;

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
            SchedulerType = typeof(IScheduler),
            IsRemote = false,
            Started = true,
            InStandbyMode = false,
            Shutdown = false,
            RunningSince = DateTimeOffset.Now.AddDays(-1),
            JobsExecuted = 1_000_000,
            JobStoreType = typeof(RAMJobStore),
            JobStoreSupportsPersistence = false,
            JobStoreClustered = false,
            ThreadPoolType = typeof(DefaultThreadPool),
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
            firedBundle: new TriggerFiredBundle(
                job: JobDetail,
                trigger: (IOperableTrigger) CronTrigger,
                calendar: CronCalendar,
                jobIsRecovering: false,
                fireTimeUtc: DateTimeOffset.Now.AddSeconds(-1),
                scheduledFireTimeUtc: DateTimeOffset.Now.AddSeconds(-1),
                previousFireTimeUtc: DateTimeOffset.Now.AddMinutes(-10),
                nextFireTimeUtc: DateTimeOffset.Now.AddMinutes(10)
            ),
            job: new DummyJob()
        );

        ExecutingJobTwo = new JobExecutionContextImpl(
            scheduler: A.Fake<IScheduler>(),
            firedBundle: new TriggerFiredBundle(
                job: JobDetail2,
                trigger: (IOperableTrigger) SimpleTrigger,
                calendar: null,
                jobIsRecovering: true,
                fireTimeUtc: DateTimeOffset.Now.AddSeconds(-5),
                scheduledFireTimeUtc: null,
                previousFireTimeUtc: null,
                nextFireTimeUtc: null
            ),
            job: new DummyJob()
        );
    }
}