using System.Collections.Specialized;

using Bunit;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The Trigger Detail page's <em>Upcoming cron fires</em>: the next five fires of the expression in the
/// editor, as the trigger would have them once rescheduled to it.
/// </summary>
/// <remarks>
/// The page reads an in-memory scheduler through the in-process API client, so the trigger and its
/// calendar make the same JSON round trips they make in an application. The dashboard's clock reads
/// 14:23 UTC on Monday 5 October 2026 and it shows times in UTC, so every time written here is a UTC
/// instant. Each trigger starts in August, behind that clock.
/// </remarks>
public sealed class TriggerCronPreviewTest
{
    private const string TriggerGroup = "reports";
    private const string TriggerName = "nightly";

    private static readonly JobKey ExportJob = new("export", "reports");
    private static readonly TriggerKey NightlyTrigger = new(TriggerName, TriggerGroup);

    private BunitContext context = null!;
    private IScheduler scheduler = null!;
    private FixedClock clock = null!;

    [SetUp]
    public async Task SetUp()
    {
        NameValueCollection properties = new()
        {
            ["quartz.scheduler.instanceName"] = "TriggerCronPreviewTest_" + Guid.NewGuid().ToString("N"),
            ["quartz.threadPool.threadCount"] = "1",
            ["quartz.serializer.type"] = "stj"
        };
        scheduler = await new StdSchedulerFactory(properties).GetScheduler();
        await scheduler.AddJob(JobBuilder.Create<NoOpJob>().WithIdentity(ExportJob).StoreDurably().Build(), replace: false);

        SchedulerRepository repository = new();
        repository.Bind(scheduler);
        IOptions<QuartzDashboardOptions> options = Options.Create(new QuartzDashboardOptions());

        clock = new FixedClock(new DateTimeOffset(2026, 10, 5, 14, 23, 0, TimeSpan.Zero));
        context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IQuartzApiClient>(new InProcessQuartzApiClient(repository, options, new DashboardHistoryStore()));
        context.Services.AddSingleton(options);
        context.Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
        context.Services.AddSingleton<SchedulerState>();
        context.Services.AddSingleton<ToastService>();
        context.Services.AddSingleton<DashboardActionLogService>();
        context.Services.AddSingleton<TimeProvider>(clock);

        SchedulerState state = context.Services.GetRequiredService<SchedulerState>();
        state.SelectedTimeZoneId = TimeZoneInfo.Utc.Id;
        state.ActiveSchedulerName = scheduler.SchedulerName;
    }

    [TearDown]
    public async Task TearDown()
    {
        await context.DisposeAsync();
        await scheduler.Shutdown(waitForJobsToComplete: false);
    }

    /// <summary>
    /// Kathmandu is UTC+05:45 all year, which is neither a CI host's zone nor any zone whose offset is a
    /// whole hour, so the dashboard host's zone cannot produce these times by accident.
    /// </summary>
    [Test]
    public async Task ATriggerInAnotherZonePreviewsItsFiresInItsOwnZone()
    {
        TimeZoneInfo kathmandu = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kathmandu");
        await Schedule(Cron("0 0 9 * * ?", kathmandu));

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => FireTimes(page).Should().Equal(
            [
                "2026-10-06 03:15:00 +00:00",
                "2026-10-07 03:15:00 +00:00",
                "2026-10-08 03:15:00 +00:00",
                "2026-10-09 03:15:00 +00:00",
                "2026-10-10 03:15:00 +00:00",
            ],
            "09:00 in Kathmandu is 03:15 UTC, and the trigger fires at 09:00 in its own zone, not the dashboard host's"));
        page.Find(".qz-cron-next-fires li .qz-muted").TextContent.Should().Be("(in 12h)",
            "an upcoming fire is ahead of now, so it is said as how long until it rather than how long ago");
        page.Find("[data-testid=cron-next-fires-basis]").TextContent.Should().Be(
            $"Read in {kathmandu.Id}, the trigger's time zone.",
            "the expression's hours are the trigger's zone's, which the times in the list are not");
    }

    [Test]
    public async Task ACalendarExcludingTheNextFireSkipsItInThePreview()
    {
        HolidayCalendar holidays = new() { TimeZone = TimeZoneInfo.Utc };
        holidays.AddExcludedDate(new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc));
        await scheduler.AddCalendar("holidays", holidays, replace: false, updateTriggers: false);
        await Schedule(Cron("0 0 12 * * ?", TimeZoneInfo.Utc, calendarName: "holidays"));

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => FireTimes(page).Should().Equal(
            [
                "2026-10-07 12:00:00 +00:00",
                "2026-10-08 12:00:00 +00:00",
                "2026-10-09 12:00:00 +00:00",
                "2026-10-10 12:00:00 +00:00",
                "2026-10-11 12:00:00 +00:00",
            ],
            "the trigger's calendar excludes 6 October, so the scheduler will not fire it then"));
        page.Find("[data-testid=cron-next-fires-basis]").TextContent.Should().Be(
            "Read in UTC, the trigger's time zone, less what calendar 'holidays' excludes.");
    }

    /// <summary>
    /// A calendar of a type the Quartz converters do not know comes back from the API client as reflection
    /// sees it, which cannot be read back as a calendar. The preview is then the expression alone, said
    /// under the list, rather than a failed page.
    /// </summary>
    [Test]
    public async Task ACalendarThatCannotBeReadIsLeftOutAndTheNoteSaysSo()
    {
        await scheduler.AddCalendar("no-tuesdays", new NoTuesdaysCalendar(), replace: false, updateTriggers: false);
        await Schedule(Cron("0 0 12 * * ?", TimeZoneInfo.Utc, calendarName: "no-tuesdays"));

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => FireTimes(page).Should().HaveCount(5)
            .And.StartWith("2026-10-06 12:00:00 +00:00", "6 October is a Tuesday the unread calendar would have excluded"));
        page.Find("[data-testid=cron-next-fires-calendar]").TextContent.Should()
            .StartWith("Calendar 'no-tuesdays' could not be read, so these times include any it excludes.");
        page.FindAll(".qz-error-alert").Should().BeEmpty("the trigger was read, and the preview without its calendar still says something true");
    }

    [Test]
    public async Task EditingTheExpressionChangesThePreviewBeforeItIsSaved()
    {
        await Schedule(Cron("0 0 12 * * ?", TimeZoneInfo.Utc));
        IRenderedComponent<TriggerDetail> page = Render();
        page.WaitForAssertion(() => FireTimes(page).Should().StartWith("2026-10-06 12:00:00 +00:00"));
        page.FindAll("[data-testid=cron-preview-unsaved]").Should().BeEmpty("nothing has been edited yet");

        page.Find("#trigger-cron-expression").Change("0 30 6 * * ?");

        page.WaitForAssertion(() => FireTimes(page).Should().Equal(
            [
                "2026-10-06 06:30:00 +00:00",
                "2026-10-07 06:30:00 +00:00",
                "2026-10-08 06:30:00 +00:00",
                "2026-10-09 06:30:00 +00:00",
                "2026-10-10 06:30:00 +00:00",
            ],
            "the preview is there to show what the edited expression means before anybody saves it"));
        page.Find("[data-testid=cron-preview-unsaved]").TextContent.Should().Be("Of the edited expression, which is not saved yet.");
        ICronTrigger stored = (ICronTrigger) (await scheduler.GetTrigger(NightlyTrigger))!;
        stored.CronExpressionString.Should().Be("0 0 12 * * ?", "previewing an edit reschedules nothing");

        page.FindAll("button").Single(button => button.TextContent.Trim() == "Reset").Click();

        page.WaitForAssertion(() => FireTimes(page).Should().StartWith("2026-10-06 12:00:00 +00:00",
            "resetting the editor previews the saved expression again"));
        page.FindAll("[data-testid=cron-preview-unsaved]").Should().BeEmpty();
    }

    [Test]
    public async Task AnEditedExpressionThatDoesNotParseShowsTheErrorInPlaceOfTheList()
    {
        await Schedule(Cron("0 0 12 * * ?", TimeZoneInfo.Utc));
        IRenderedComponent<TriggerDetail> page = Render();

        page.Find("#trigger-cron-expression").Change("0 0 25 * * ?");

        page.WaitForAssertion(() =>
        {
            page.Find(".qz-cron-next-fires .qz-error-alert").TextContent.Should().StartWith("Invalid cron expression: ",
                "an expression that does not parse has no fire times, and the reader needs to know why");
            FireTimes(page).Should().BeEmpty("the saved expression's fires would read as the edited one's");
        });
    }

    [Test]
    public async Task ATriggerThatHasEndedHasNoUpcomingFires()
    {
        await Schedule(Cron("0 0 12 * * ?", TimeZoneInfo.Utc, endAt: new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        clock.UtcNow = new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.Zero);

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => page.Find(".qz-cron-next-fires").TextContent.Should().Contain("No upcoming fire times.",
            "the trigger's end time is part of its schedule, and it has passed"));
        FireTimes(page).Should().BeEmpty();
    }

    [Test]
    public async Task ATriggerThatStartsLaterPreviewsFromItsStart()
    {
        await Schedule(Cron("0 0 12 * * ?", TimeZoneInfo.Utc, startAt: new DateTimeOffset(2026, 12, 24, 0, 0, 0, TimeSpan.Zero)));

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => FireTimes(page).Should().StartWith("2026-12-24 12:00:00 +00:00",
            "a trigger does not fire before its start time"));
    }

    /// <summary>
    /// A trigger type the Quartz converters do not know comes back from the API client without the
    /// discriminator a save needs, so the page can neither reschedule it nor say what a reschedule would do.
    /// </summary>
    [Test]
    public async Task ATriggerThePageCannotReadBackIsNotPreviewed()
    {
        await Schedule(new UnknownCronTrigger
        {
            Key = NightlyTrigger,
            JobKey = ExportJob,
            CronExpressionString = "0 0 12 * * ?",
            StartTimeUtc = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
        });

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => page.Find(".qz-cron-next-fires").TextContent.Trim().Should()
            .Be("The dashboard cannot preview this trigger's fire times."));
    }

    private static ITrigger Cron(
        string cronExpression,
        TimeZoneInfo timeZone,
        string? calendarName = null,
        DateTimeOffset? startAt = null,
        DateTimeOffset? endAt = null)
    {
        return TriggerBuilder.Create()
            .WithIdentity(NightlyTrigger)
            .ForJob(ExportJob)
            .StartAt(startAt ?? new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero))
            .EndAt(endAt)
            .ModifiedByCalendar(calendarName)
            .WithCronSchedule(cronExpression, cron => cron.InTimeZone(timeZone))
            .Build();
    }

    private static List<string> FireTimes(IRenderedComponent<TriggerDetail> page)
    {
        return [.. page.FindAll(".qz-cron-next-fires li > span:first-child").Select(time => time.TextContent)];
    }

    private Task Schedule(ITrigger trigger)
    {
        return scheduler.ScheduleJob(trigger);
    }

    private IRenderedComponent<TriggerDetail> Render()
    {
        return context.Render<TriggerDetail>(parameters => parameters
            .Add(x => x.Group, TriggerGroup)
            .Add(x => x.Name, TriggerName));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return UtcNow;
        }
    }

    /// <remarks>
    /// Clones as itself: <see cref="BaseCalendar.Clone" /> answers a plain <see cref="BaseCalendar" />, which
    /// is what the in-memory store would then hold.
    /// </remarks>
    private sealed class NoTuesdaysCalendar : BaseCalendar
    {
        public override bool IsTimeIncluded(DateTimeOffset timeStampUtc)
        {
            return timeStampUtc.UtcDateTime.DayOfWeek != DayOfWeek.Tuesday && base.IsTimeIncluded(timeStampUtc);
        }

        public override ICalendar Clone()
        {
            return CloneFields(new NoTuesdaysCalendar());
        }
    }

    private sealed class UnknownCronTrigger : CronTriggerImpl;

    private sealed class NoOpJob : IJob
    {
        public Task Execute(IJobExecutionContext context)
        {
            return Task.CompletedTask;
        }
    }
}
