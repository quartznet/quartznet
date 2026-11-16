using Bunit;

using DashboardPage = Quartz.Dashboard.Components.Pages.Dashboard;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The overview page.
/// </summary>
public class DashboardPageTest
{
    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    /// <summary>
    /// Recent admin actions are the active scheduler's latest ten, however many actions another scheduler
    /// took since.
    /// </summary>
    /// <remarks>
    /// Taking the process-wide latest ten and then keeping the active scheduler's showed nothing once
    /// another scheduler had been busier.
    /// </remarks>
    [Test]
    public void RecentActionsFindTheActiveSchedulersActionsBehindABusierScheduler()
    {
        context.ActionLog.Record(DashboardComponentContext.SchedulerName, "PauseJob", "reports.job-1", succeeded: true);
        for (int index = 0; index < 10; index++)
        {
            context.ActionLog.Record("other", "PauseJob", "elsewhere.job-" + index, succeeded: true);
        }

        IRenderedComponent<DashboardPage> page = context.Render<DashboardPage>();

        page.Markup.Should().Contain("reports.job-1",
            "ten newer actions on another scheduler are not this scheduler's recent actions");
        page.Markup.Should().NotContain("elsewhere.job-");
    }
}
