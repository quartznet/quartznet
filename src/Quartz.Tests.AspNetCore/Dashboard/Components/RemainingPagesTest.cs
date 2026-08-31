using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Layout;
using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Impl.Calendar;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The pages whose own logic is small enough not to earn a file of their own, rendered and pinned where
/// they say something.
/// </summary>
/// <remarks>
/// Together with the other files in this folder, every component the dashboard routes to is rendered by
/// something — which is what lets the Sonar coverage exclusion for <c>*.razor</c> go away.
/// </remarks>
public class RemainingPagesTest
{
    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
        context.WithScheduler();
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    [Test]
    public void TheActionLogShowsOnlyTheActiveSchedulersActions()
    {
        context.ActionLog.Record(TestData.SchedulerName, "PauseJob", "reports.job-1", succeeded: true);
        context.ActionLog.Record("other", "PauseJob", "elsewhere.job-1", succeeded: true);

        IRenderedComponent<ActionLog> page = context.Render<ActionLog>();

        page.TextOfAll("tbody td").Should().Contain("reports.job-1");
        page.Markup.Should().NotContain("elsewhere.job-1");
    }

    [Test]
    public void TheActionLogSaysWhenNothingHasHappenedYet()
    {
        IRenderedComponent<ActionLog> page = context.Render<ActionLog>();

        page.Markup.Should().Contain("No recorded admin actions yet.",
            "an empty table reads as a rendering fault");
    }

    [Test]
    public void CurrentlyExecutingNamesTheNodeThatOwnsEachFiring()
    {
        A.CallTo(() => context.Api.QueryFireInstances(A<string>._, A<DashboardFireInstanceQuery>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.Page<FireInstanceDto>([
                new FireInstanceDto(
                    "fire-1",
                    new TriggerKeyDto("nightly", "trigger-1"),
                    new JobKeyDto("reports", "job-1"),
                    "node-b",
                    FireInstanceState.Executing,
                    TestData.Dashboard.FiredAt,
                    TestData.Dashboard.FiredAt,
                    "batch")
            ]));

        IRenderedComponent<CurrentlyExecuting> page = context.Render<CurrentlyExecuting>();

        page.Markup.Should().Contain("node-b",
            "with a persistent job store the listing is cluster-wide, so which node owns a firing is the "
            + "difference between interrupting it here and asking another node to");
        page.Markup.Should().Contain("reports.job-1");
        page.Markup.Should().Contain("batch");
    }

    [Test]
    public void CurrentlyExecutingSaysSoWhenNothingIsRunning()
    {
        A.CallTo(() => context.Api.QueryFireInstances(A<string>._, A<DashboardFireInstanceQuery>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.Page<FireInstanceDto>([]));

        IRenderedComponent<CurrentlyExecuting> page = context.Render<CurrentlyExecuting>();

        page.Markup.Should().Contain("No jobs currently executing.");
    }

    [Test]
    public void TheJobDetailPageShowsTheJobAndItsTriggers()
    {
        GivenJob();

        IRenderedComponent<JobDetail> page = RenderJobDetail();

        page.Markup.Should().Contain("Dummy job description");
        page.Markup.Should().Contain("nightly.trigger-1", "the triggers that fire the job belong on its page");
        page.TextOfAll("td").Should().Contain("TestValue", "the job data map is what a job is configured with");
    }

    [Test]
    public void AJobThatIsGoneSaysSoRatherThanRenderingBlanks()
    {
        A.CallTo(() => context.Api.GetJobDetail(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("no such job"));

        IRenderedComponent<JobDetail> page = RenderJobDetail();

        page.Markup.Should().Contain("no such job");
    }

    [Test]
    public void ReadOnlyModeHidesTheJobDetailPagesActions()
    {
        context.Options.ReadOnly = true;
        GivenJob();

        IRenderedComponent<JobDetail> page = RenderJobDetail();

        page.HasButton("Trigger now").Should().BeFalse();
        page.HasButton("Delete").Should().BeFalse();
        page.Markup.Should().NotContain("Trigger with JobDataMap overrides",
            "an override editor with no button to submit it is worse than no editor");
    }

    [Test]
    public void TheCalendarsPageListsWhatTheSchedulerKnows()
    {
        A.CallTo(() => context.Api.GetCalendarNames(A<string>._, A<CancellationToken>._))
            .Returns(new List<string> { "holidays", "maintenance" });

        IRenderedComponent<Calendars> page = context.Render<Calendars>();

        page.Markup.Should().Contain("holidays").And.Contain("maintenance");
        page.Markup.Should().Contain("Create or replace cron calendar");
    }

    [Test]
    public void ReadOnlyModeHidesTheCalendarEditor()
    {
        context.Options.ReadOnly = true;
        A.CallTo(() => context.Api.GetCalendarNames(A<string>._, A<CancellationToken>._))
            .Returns(new List<string> { "holidays" });

        IRenderedComponent<Calendars> page = context.Render<Calendars>();

        page.Markup.Should().NotContain("Create or replace cron calendar");
        page.Markup.Should().Contain("holidays", "reading the calendars is still allowed");
    }

    [Test]
    public void TheCalendarDetailPageDescribesTheCalendarItself()
    {
        A.CallTo(() => context.Api.GetCalendar(A<string>._, "holidays", A<CancellationToken>._))
            .Returns(TestData.HolidayCalendar);

        IRenderedComponent<CalendarDetail> page = context.Render<CalendarDetail>(parameters => parameters
            .Add(x => x.CalendarName, "holidays"));

        page.Markup.Should().Contain("holidays");
        page.Markup.Should().Contain("Test HolidayCalendar",
            "the calendar arrives as itself, so its description is readable without parsing JSON");
    }

    [Test]
    public void TheNavigationMenuLinksUnderTheDashboardPath()
    {
        IRenderedComponent<NavMenu> menu = context.Render<NavMenu>();

        menu.FindAll("a").Select(link => link.GetAttribute("href")).Should()
            .Equal(["quartz", "quartz/jobs", "quartz/triggers", "quartz/calendars", "quartz/executing", "quartz/schedulers", "quartz/cluster", "quartz/history", "quartz/live", "quartz/actions"],
                "the links are base-relative, so they survive an application path base as well as a "
                + "custom dashboard path");
    }

    [Test]
    public void ADetailPageKeepsItsSectionHighlighted()
    {
        context.Navigate("/quartz/jobs/reports/job-1");

        IRenderedComponent<NavMenu> menu = context.Render<NavMenu>();

        ActiveLinks(menu).Should().Equal(["Jobs"],
            "a job's detail page is still the Jobs section, and exactly one section is the current one");
    }

    [Test]
    public void TheOverviewIsHighlightedOnlyOnTheDashboardRoot()
    {
        context.Navigate("/quartz");
        IRenderedComponent<NavMenu> menu = context.Render<NavMenu>();
        ActiveLinks(menu).Should().Equal(["Dashboard"]);

        context.Navigate("/quartz/triggers");

        ActiveLinks(menu).Should().Equal(["Triggers"],
            "the root's link prefixes every other one, so an unguarded prefix match would light it up everywhere");
    }

    private static List<string> ActiveLinks(IRenderedComponent<NavMenu> menu)
    {
        List<string> active = [];
        foreach (IElement link in menu.FindAll("a.qz-nav-active"))
        {
            active.Add(link.QuerySelector(".qz-nav-link-text")?.TextContent.Trim() ?? string.Empty);
        }

        return active;
    }

    private void GivenJob()
    {
        A.CallTo(() => context.Api.GetJobDetail(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .Returns(new JobDetailDto(
                "job-1",
                "reports",
                "Quartz.Tests.AspNetCore.Support.DummyJob",
                "Dummy job description",
                Durable: true,
                RequestsRecovery: false,
                ConcurrentExecutionDisallowed: true,
                PersistJobDataAfterExecution: false,
                JobDataMap: new JobDataMap { ["TestKey"] = "TestValue" }));
        A.CallTo(() => context.Api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.TriggerHeaders("nightly", 1));
    }

    private IRenderedComponent<JobDetail> RenderJobDetail()
    {
        return context.Render<JobDetail>(parameters => parameters
            .Add(x => x.Group, "reports")
            .Add(x => x.Name, "job-1"));
    }
}
