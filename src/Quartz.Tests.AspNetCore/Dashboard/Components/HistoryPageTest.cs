using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The Execution History page: what a failed refresh leaves on it.
/// </summary>
public class HistoryPageTest
{
    /// <summary>The rows of the history listing, one per run.</summary>
    private const string HistoryRows = ".qz-table tbody tr";

    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
    }

    [TearDown]
    public async Task TearDown()
    {
        await context.DisposeAsync();
    }

    /// <summary>
    /// A refresh that fails keeps the rows on screen and marks them stale beside the time they were read, and
    /// the next one that answers replaces them and clears the mark.
    /// </summary>
    /// <remarks>
    /// The rows used to be emptied before each read and the error shown when it failed, so over a flaky
    /// connection the page alternated between the two every second.
    /// </remarks>
    [Test]
    public void AFailedRefreshKeepsTheRowsAndMarksThemStale()
    {
        GivenHistory(Entry(100), Entry(200));
        IRenderedComponent<History> page = context.Render<History>();
        page.WaitForAssertion(() => page.FindAll(HistoryRows).Should().HaveCount(2));

        A.CallTo(() => context.Api.GetHistory(A<JobHistoryQueryDto>._))
            .Throws(new InvalidOperationException("the history store is not answering"));
        context.Clock.Advance(TimeSpan.FromSeconds(1));

        page.WaitForAssertion(() =>
        {
            page.FindAll(HistoryRows).Should().HaveCount(2, "the rows stay until new ones arrive");
            page.FindAll(".qz-error-alert").Should().BeEmpty("one failed refresh is not a page that failed to load");
            page.Find("[data-testid=stale-marker]").GetAttribute("title").Should().Be(
                "The last refresh failed: the history store is not answering");
        });

        GivenHistory(Entry(100), Entry(200), Entry(300));
        context.Clock.Advance(TimeSpan.FromSeconds(1));

        page.WaitForAssertion(() =>
        {
            page.FindAll("[data-testid=stale-marker]").Should().BeEmpty("the next refresh that answers clears the mark");
            page.FindAll(HistoryRows).Should().HaveCount(3);
        });
    }

    private static HistoryEntry Entry(int durationMilliseconds)
    {
        return new HistoryEntry(
            DashboardComponentContext.SchedulerName,
            "reports",
            "job-1",
            "nightly",
            "trigger-1",
            DashboardComponentContext.FiredAt,
            durationMilliseconds,
            Succeeded: true,
            ExceptionMessage: null);
    }

    private void GivenHistory(params HistoryEntry[] entries)
    {
        A.CallTo(() => context.Api.GetHistory(A<JobHistoryQueryDto>._))
            .Returns(DashboardComponentContext.HistoryPage(entries));
    }
}
