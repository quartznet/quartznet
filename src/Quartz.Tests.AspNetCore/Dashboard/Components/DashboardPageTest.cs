using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Services;

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

    /// <summary>
    /// A failed read of what is executing, which the page repeats every second, keeps the page and marks it
    /// stale beside the time it was read; the next read that answers clears the mark.
    /// </summary>
    /// <remarks>
    /// One failed read used to replace the whole overview with the error until Retry was pressed: a dropped
    /// read out of sixty a minute took every figure on the page with it.
    /// </remarks>
    [Test]
    public void AFailedRefreshKeepsTheOverviewAndMarksItStale()
    {
        GivenCounts(jobs: 137, triggers: 250, errorTriggers: 3, executing: 2);
        IRenderedComponent<DashboardPage> page = context.Render<DashboardPage>();
        page.WaitForAssertion(() => page.StatCardValue("Total Jobs").Should().Be("137"));

        // The once-a-second count: the page has read everything else, and until it reads the whole of it
        // again, only its refresh asks what is executing.
        A.CallTo(() => context.Api.GetCurrentlyExecutingJobs(A<string>._))
            .Throws(new InvalidOperationException("the store is not answering"));
        context.Clock.Advance(TimeSpan.FromSeconds(1));

        page.WaitForAssertion(() =>
        {
            page.FindAll(".qz-error-alert").Should().BeEmpty("one failed refresh is not a page that failed to load");
            page.StatCardValue("Total Jobs").Should().Be("137", "what the page read is still true enough to show");
            page.StatCardValue("Currently Executing").Should().Be("2", "the last count read stays until another is");
            page.Find("[data-testid=stale-marker]").GetAttribute("title").Should().Be(
                "The last refresh failed: the store is not answering");
        });

        GivenCounts(jobs: 137, triggers: 250, errorTriggers: 3, executing: 1);
        context.Clock.Advance(TimeSpan.FromSeconds(1));

        page.WaitForAssertion(() =>
        {
            page.FindAll("[data-testid=stale-marker]").Should().BeEmpty("the next refresh that answers clears the mark");
            page.StatCardValue("Currently Executing").Should().Be("1");
        });
    }

    /// <remarks>
    /// The page counts the triggers in error among the first 25 it lists, which is where these are.
    /// </remarks>
    private void GivenCounts(int jobs, int triggers, int errorTriggers, int executing)
    {
        List<TriggerKeyDto> triggerKeys = DashboardComponentContext.TriggerKeys("nightly", triggers);
        HashSet<string> inError = [.. triggerKeys.Take(errorTriggers).Select(trigger => trigger.Name)];

        A.CallTo(() => context.Api.GetJobKeys(A<string>._, A<string?>._))
            .Returns(DashboardComponentContext.JobKeys("reports", jobs));
        A.CallTo(() => context.Api.GetTriggerKeys(A<string>._, A<string?>._))
            .Returns(triggerKeys);
        A.CallTo(() => context.Api.GetTriggerState(A<string>._, A<string>._, A<string>._))
            .ReturnsLazily((string _, string _, string name) => inError.Contains(name) ? "Error" : "Normal");
        A.CallTo(() => context.Api.GetCurrentlyExecutingJobs(A<string>._))
            .Returns([.. Enumerable.Range(1, executing).Select(Executing)]);
    }

    private static CurrentlyExecutingJobDto Executing(int index)
    {
        return new CurrentlyExecutingJobDto(
            new JobKeyDto("reports", "job-" + index),
            new TriggerKeyDto("nightly", "trigger-" + index),
            DashboardComponentContext.FiredAt,
            "fire-" + index);
    }
}
