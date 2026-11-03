using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Dashboard.Support;
using Quartz.Tests.AspNetCore.Support;

using DashboardPage = Quartz.Dashboard.Components.Pages.Dashboard;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// What <see cref="QuartzDashboardOptions.OperationAuthorizationPolicy" /> does to the pages: a visitor is
/// offered the controls their operator role allows and no others, and an action refused anyway is a toast
/// and a refused entry in the Action Log.
/// </summary>
/// <remarks>
/// The visitor's role is <see cref="TestSchedulerAuthorizationService.Role" />, answered against the level
/// the dashboard asks with; the policy Quartz ships is <c>SchedulerRolesTest</c>'s subject. Without the
/// option every page is as it was, which the rest of the page tests hold.
/// </remarks>
public class OperatorRolesPagesTest
{
    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
        context.WithScheduler();

        A.CallTo(() => context.Api.QueryJobs(A<string>._, A<DashboardJobQuery>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.Page<JobKeyDto>(TestData.Dashboard.JobKeys("reports", 1)));
        A.CallTo(() => context.Api.QueryJobGroups(A<string>._, A<DashboardGroupQuery>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.Page<JobGroupDto>([new JobGroupDto("reports", false)]));
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    [TestCase(null, new string[0], new[] { "Pause", "Resume", "Trigger now", "Delete", "Pause group", "Resume group", "Trigger group" })]
    [TestCase(SchedulerAccess.View, new string[0], new[] { "Pause", "Resume", "Trigger now", "Delete", "Pause group", "Resume group", "Trigger group" })]
    [TestCase(SchedulerAccess.Operate, new[] { "Pause", "Resume", "Trigger now", "Pause group", "Resume group", "Trigger group" }, new[] { "Delete" })]
    [TestCase(SchedulerAccess.Administer, new[] { "Pause", "Resume", "Trigger now", "Delete", "Pause group", "Resume group", "Trigger group" }, new string[0])]
    public void TheJobsPageOffersWhatTheRoleAllows(SchedulerAccess? role, string[] offered, string[] withheld)
    {
        context.WithOperatorRole(role);

        IRenderedComponent<Jobs> page = context.Render<Jobs>();

        page.WaitForAssertion(() =>
        {
            page.TextOfAll(".qz-key-badge-value").Should().Equal(["reports.job-1"]);
            foreach (string label in offered)
            {
                page.HasButton(label).Should().BeTrue($"a visitor at {role?.ToString() ?? "no role"} may {label}");
            }
        });

        foreach (string label in withheld)
        {
            page.HasButton(label).Should().BeFalse($"a visitor at {role?.ToString() ?? "no role"} may not {label}, so it is not offered");
        }

        page.TextOfAll("th").Contains("Actions").Should().Be(offered.Length > 0,
            "a column with nothing in it is not drawn");
    }

    [TestCase(SchedulerAccess.View, new string[0], new[] { "Start", "Standby", "Pause all", "Resume all", "Shutdown" })]
    [TestCase(SchedulerAccess.Operate, new[] { "Start", "Standby", "Pause all", "Resume all" }, new[] { "Shutdown" })]
    [TestCase(SchedulerAccess.Administer, new[] { "Start", "Standby", "Pause all", "Resume all", "Shutdown" }, new string[0])]
    public void TheOverviewOffersTheLifecycleTheRoleAllows(SchedulerAccess role, string[] offered, string[] withheld)
    {
        context.WithOperatorRole(role);

        IRenderedComponent<DashboardPage> page = context.Render<DashboardPage>();

        page.WaitForAssertion(() =>
        {
            page.Markup.Should().Contain("Total Jobs", "the overview has loaded");
            foreach (string label in offered)
            {
                page.HasButton(label).Should().BeTrue($"a visitor at {role} may {label}");
            }
        });

        foreach (string label in withheld)
        {
            page.HasButton(label).Should().BeFalse($"shutting down is Administer, and a visitor at {role} is not offered it");
        }
    }

    /// <summary>
    /// Triggering with job data is Administer, so an operator may trigger the job and is not offered the
    /// override editor: a stored <c>NativeJob</c> runs the command its job data names.
    /// </summary>
    [TestCase(SchedulerAccess.Operate, false)]
    [TestCase(SchedulerAccess.Administer, true)]
    public void TheJobDataOverridesAreAnAdministratorsAlone(SchedulerAccess role, bool overridesOffered)
    {
        context.WithOperatorRole(role);
        GivenJob();

        IRenderedComponent<JobDetail> page = context.Render<JobDetail>(parameters => parameters
            .Add(x => x.Group, "reports")
            .Add(x => x.Name, "job-1"));

        page.WaitForAssertion(() => page.HasButton("Trigger now").Should().BeTrue("triggering without data is Operate"));
        page.HasButton("Trigger with overrides").Should().Be(overridesOffered);
        page.Markup.Contains("Trigger with JobDataMap overrides", StringComparison.Ordinal).Should().Be(overridesOffered);
        page.HasButton("Delete").Should().Be(role == SchedulerAccess.Administer);
    }

    [TestCase(SchedulerAccess.Operate, false)]
    [TestCase(SchedulerAccess.Administer, true)]
    public void CalendarsAreAnAdministratorsToChange(SchedulerAccess role, bool offered)
    {
        context.WithOperatorRole(role);
        A.CallTo(() => context.Api.GetCalendarNames(A<string>._, A<CancellationToken>._))
            .Returns(new List<string> { "holidays" });

        IRenderedComponent<Calendars> page = context.Render<Calendars>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("holidays"));
        page.HasButton("Delete").Should().Be(offered);
        page.Markup.Contains("Create or replace cron calendar", StringComparison.Ordinal).Should().Be(offered);
    }

    /// <summary>
    /// <see cref="QuartzDashboardOptions.ReadOnly" /> is the ceiling: an administrator of a read-only
    /// dashboard reads.
    /// </summary>
    [Test]
    public void ReadOnlyIsTheCeilingOverEveryRole()
    {
        context.Options.ReadOnly = true;
        context.WithOperatorRole(SchedulerAccess.Administer);

        IRenderedComponent<Jobs> page = context.Render<Jobs>();

        page.WaitForAssertion(() => page.TextOfAll(".qz-key-badge-value").Should().NotBeEmpty());
        page.HasButton("Pause").Should().BeFalse();
        page.HasButton("Delete").Should().BeFalse();
    }

    /// <summary>
    /// The role is asked once per scheduler and kept for the circuit: a page that renders again reads the
    /// answers rather than asking the policy for each control each time.
    /// </summary>
    [Test]
    public void TheRoleIsAskedOncePerScheduler()
    {
        context.WithOperatorRole(SchedulerAccess.Operate);

        IRenderedComponent<Jobs> first = context.Render<Jobs>();
        first.WaitForAssertion(() => first.HasButton("Pause").Should().BeTrue());
        int asked = context.AuthorizationService.Asked.Count;

        IRenderedComponent<Jobs> second = context.Render<Jobs>();
        second.WaitForAssertion(() => second.HasButton("Pause").Should().BeTrue());

        asked.Should().BePositive();
        context.AuthorizationService.Asked.Count.Should().Be(asked, "the second render reads what the first asked");
    }

    /// <summary>
    /// A control that was offered and is then refused — the visitor's role changed since the page asked —
    /// is a toast and a refused entry in the Action Log, not a failure.
    /// </summary>
    [Test]
    public void ARefusalIsAToastAndARefusedEntry()
    {
        context.WithOperatorRole(SchedulerAccess.Operate);
        A.CallTo(() => context.Api.TriggerJob(A<string>._, A<JobKeyDto>._, A<JobDataMap?>._, A<CancellationToken>._))
            .Throws(new OperationRefusedException("TriggerJob", TestData.SchedulerName, SchedulerAccess.Operate));

        IRenderedComponent<Jobs> page = context.Render<Jobs>();
        page.WaitForAssertion(() => page.HasButton("Trigger now").Should().BeTrue());
        page.FindAll("button").First(button => button.TextContent.Trim() == "Trigger now").Click();

        page.WaitForAssertion(() => context.Toasts.Messages.Should().ContainSingle()
            .Which.Message.Should().Contain("it needs Operate"));

        DashboardActionLogEntry entry = context.ActionLog.GetLatest(1).Should().ContainSingle().Which;
        entry.Refused.Should().BeTrue("a rule said no, and nothing reached the scheduler");
        entry.Succeeded.Should().BeFalse();
        entry.Action.Should().Be("TriggerJob");

        IRenderedComponent<ActionLog> log = context.Render<ActionLog>();
        log.Find(".qz-state-indicator").TextContent.Should().Contain("Refused");
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
                ConcurrentExecutionDisallowed: false,
                PersistJobDataAfterExecution: false,
                JobDataMap: new JobDataMap { ["TestKey"] = "TestValue" }));
        A.CallTo(() => context.Api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.TriggerHeaders("nightly", 1));
    }
}
