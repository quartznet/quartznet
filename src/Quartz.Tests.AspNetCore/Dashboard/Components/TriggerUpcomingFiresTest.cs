using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.Impl.Calendar;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The Trigger Detail page's <em>Upcoming fires</em>: the next ten fire times of whichever kind of trigger the
/// page shows, read through the data source, so a scheduler in another process answers for itself.
/// </summary>
/// <remarks>
/// The context's clock reads 14:23 UTC on Monday 5 October 2026, and the dashboard shows times in UTC.
/// </remarks>
public sealed class TriggerUpcomingFiresTest
{
    private const string TriggerGroup = "reports";
    private const string TriggerName = "nightly";

    private static readonly TriggerKeyDto key = new(TriggerGroup, TriggerName);
    private static readonly DateTimeOffset two = new(2026, 10, 6, 2, 0, 0, TimeSpan.Zero);

    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
        context.WithScheduler();
        A.CallTo(() => context.Api.GetTriggerState(TestData.SchedulerName, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(TriggerState.Normal);
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    [Test]
    public void ASimpleTriggersPageShowsItsUpcomingFires()
    {
        GivenTrigger(Simple());
        A.CallTo(() => context.Api.GetUpcomingFireTimes(TestData.SchedulerName, key, 10, A<CancellationToken>._))
            .Returns(new List<DateTimeOffset> { two, two.AddDays(1), two.AddDays(2) });

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => FireTimes(page).Should().Equal(
            [
                "2026-10-06 02:00:00 +00:00",
                "2026-10-07 02:00:00 +00:00",
                "2026-10-08 02:00:00 +00:00",
            ],
            "every trigger type has upcoming fires, not only a cron trigger"));
        page.Find(".qz-upcoming-fires li .qz-muted").TextContent.Should().Be("(in 11h)",
            "an upcoming fire is said as how long until it");
        page.Markup.Should().Contain("Upcoming fires");
        page.FindAll(".qz-cron-next-fires").Should().BeEmpty("the cron preview is the cron editor's, and a simple trigger has none");
        A.CallTo(() => context.Api.GetUpcomingFireTimes(TestData.SchedulerName, key, 10, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public void ATriggerThatDoesNotFireAgainSaysSo()
    {
        GivenTrigger(Simple());
        A.CallTo(() => context.Api.GetUpcomingFireTimes(TestData.SchedulerName, key, 10, A<CancellationToken>._))
            .Returns(new List<DateTimeOffset>());

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => page.Find("[data-testid=upcoming-fires-none]").TextContent.Should().Be("This trigger does not fire again.",
            "an empty list would read as a panel that has not loaded"));
        FireTimes(page).Should().BeEmpty();
    }

    /// <summary>
    /// A data source that cannot answer — an agent that refuses the operation, say — leaves the panel saying
    /// so, rather than failing a page whose every other part was read.
    /// </summary>
    [Test]
    public void FireTimesThatCannotBeReadAreSaidInThePanel()
    {
        GivenTrigger(Simple());
        A.CallTo(() => context.Api.GetUpcomingFireTimes(TestData.SchedulerName, key, 10, A<CancellationToken>._))
            .Throws(new HttpClientException("The agent does not accept GetTriggerFireTimes."));

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => page.Find("[data-testid=upcoming-fires-unreadable]").TextContent.Should().Be(
            "The upcoming fire times could not be read. The agent does not accept GetTriggerFireTimes."));
        page.FindAll(".qz-error-alert").Should().BeEmpty("the trigger was read, and the rest of its page still says something true");
        page.Find("table.qz-table").TextContent.Should().Contain("Simple");
    }

    /// <summary>
    /// An <see cref="IQuartzApiClient" /> of an application's own that predates the member answers through its
    /// default, which reads the trigger and the calendar it names and computes from them.
    /// </summary>
    [Test]
    public void ADataSourceOfItsOwnAnswersFromTheTriggerAndItsCalendar()
    {
        TimeZoneInfo kathmandu = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kathmandu");
        HolidayCalendar holidays = new() { TimeZone = kathmandu };
        holidays.AddExcludedDay(new DateOnly(2026, 10, 7));
        A.CallTo(() => context.Api.GetCalendar(TestData.SchedulerName, "holidays", A<CancellationToken>._)).Returns(holidays);

        // As a store holds it: its first fire time computed against the calendar.
        ITrigger daily = TriggerBuilder.Create(context.Clock)
            .WithIdentity(TriggerName, TriggerGroup)
            .ForJob("export", "reports")
            .StartAt(new DateTimeOffset(2026, 10, 6, 3, 15, 0, TimeSpan.Zero))
            .WithCalendarName("holidays")
            .WithCalendarIntervalSchedule(schedule => schedule.WithInterval(1, IntervalUnit.Day).InTimeZone(kathmandu))
            .Build();
        ((IOperableTrigger) daily).ComputeFirstFireTimeUtc(holidays);
        GivenTrigger(daily);
        A.CallTo(() => context.Api.GetUpcomingFireTimes(A<string>._, A<TriggerKeyDto>._, A<int>._, A<CancellationToken>._))
            .CallsBaseMethod();

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => FireTimes(page).Should().HaveCount(10).And.StartWith(
            [
                "2026-10-06 03:15:00 +00:00",
                "2026-10-08 03:15:00 +00:00",
                "2026-10-09 03:15:00 +00:00",
            ],
            "09:00 in Kathmandu is 03:15 UTC, and the calendar excludes the 7th there"));
    }

    private static ITrigger Simple()
    {
        return TriggerBuilder.Create()
            .WithIdentity(TriggerName, TriggerGroup)
            .ForJob("export", "reports")
            .StartAt(new DateTimeOffset(2026, 8, 1, 2, 0, 0, TimeSpan.Zero))
            .WithSimpleSchedule(TimeSpan.FromDays(1))
            .Build();
    }

    private static List<string> FireTimes(IRenderedComponent<TriggerDetail> page)
    {
        return [.. page.FindAll(".qz-upcoming-fires li > span:first-child").Select(time => time.TextContent)];
    }

    private void GivenTrigger(ITrigger trigger)
    {
        A.CallTo(() => context.Api.GetTrigger(TestData.SchedulerName, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(trigger);
    }

    private IRenderedComponent<TriggerDetail> Render()
    {
        return context.Render<TriggerDetail>(parameters => parameters
            .Add(x => x.Group, TriggerGroup)
            .Add(x => x.Name, TriggerName));
    }
}
