using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The Jobs page: what a failed action and a failed reload leave on it, and what a second click sends.
/// </summary>
public class JobsPageTest
{
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
    /// A failed action leaves the page it was taken from as it was: the toast and the Action Log say why.
    /// </summary>
    /// <remarks>
    /// The listing was read and is still true; only the pause failed. Replacing it with the error and its
    /// Retry button took away the page the reader was acting on, to repeat what the toast already said.
    /// </remarks>
    [Test]
    public void AFailedPauseLeavesTheListingAndRaisesAToast()
    {
        GivenJobs(DashboardComponentContext.JobKeys("reports", 2));
        A.CallTo(() => context.Api.PauseJob(A<string>._, A<string>._, A<string>._))
            .Throws(new InvalidOperationException("the store is not answering"));

        IRenderedComponent<Jobs> page = context.Render<Jobs>();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Pause").Click();

        page.WaitForAssertion(() =>
        {
            context.Toasts.Messages.Should().ContainSingle()
                .Which.Message.Should().Be("the store is not answering");
            page.TextOfAll(".qz-key-badge-value").Should().Equal(["reports.job-1", "reports.job-2"],
                "the listing was read and is still true; only the pause failed");
            page.FindAll(".qz-error-alert").Should().BeEmpty("the toast is the report, not an error in place of the page");
        });
    }

    /// <summary>
    /// A read that fails once the listing has been read keeps it, marked stale beside the time it was read,
    /// rather than putting the error in its place.
    /// </summary>
    [Test]
    public void AFailedReloadKeepsTheListingAndMarksItStale()
    {
        GivenJobs(DashboardComponentContext.JobKeys("reports", 2));
        A.CallTo(() => context.Api.ResumeJob(A<string>._, A<string>._, A<string>._)).Returns(Task.CompletedTask);
        IRenderedComponent<Jobs> page = context.Render<Jobs>();

        // The resume answers; the read of the listing after it does not.
        A.CallTo(() => context.Api.GetJobKeys(A<string>._, A<string?>._))
            .Throws(new InvalidOperationException("the store is not answering"));
        page.FindAll("button").First(button => button.TextContent.Trim() == "Resume").Click();

        page.WaitForAssertion(() =>
        {
            context.Toasts.Messages.Should().ContainSingle()
                .Which.Message.Should().Be("Resumed job reports.job-1.");
            page.TextOfAll(".qz-key-badge-value").Should().Equal(["reports.job-1", "reports.job-2"],
                "the listing stays until a new one arrives");
            page.FindAll(".qz-error-alert").Should().BeEmpty("a page with a listing to show is not a page that failed to load");
            page.Find("[data-testid=stale-marker]").GetAttribute("title").Should().Be(
                "The last refresh failed: the store is not answering");
        });
    }

    private void GivenJobs(IReadOnlyList<JobKeyDto> jobs)
    {
        A.CallTo(() => context.Api.GetJobKeys(A<string>._, A<string?>._)).Returns([.. jobs]);
    }
}
