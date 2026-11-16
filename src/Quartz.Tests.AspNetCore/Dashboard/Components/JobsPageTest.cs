using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Components.Shared;
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

    /// <summary>
    /// The confirm dialog closes as soon as it is confirmed, before the call returns, so a second click on
    /// Confirm sends nothing.
    /// </summary>
    /// <remarks>
    /// It stayed open with Confirm live until the call returned, so a second click deleted again and logged a
    /// second entry. The second click is sent as the dialog's own confirmation: over a slow connection, a
    /// double click's second event reaches the page before the browser has drawn the dialog closed.
    /// </remarks>
    [Test]
    public async Task ASecondConfirmSendsNothing()
    {
        GivenJobs(DashboardComponentContext.JobKeys("reports", 1));
        TaskCompletionSource deleting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        A.CallTo(() => context.Api.DeleteJob(A<string>._, A<string>._, A<string>._))
            .Returns(deleting.Task);
        IRenderedComponent<Jobs> page = context.Render<Jobs>();

        Button(page, "Delete").Click();
        page.WaitForElement(".qz-confirm-dialog button.qz-button-danger").Click();
        Task secondClick = page.InvokeAsync(() => page.FindComponent<ConfirmDialog>().Instance.OnConfirm.InvokeAsync());

        page.WaitForAssertion(() =>
        {
            A.CallTo(() => context.Api.DeleteJob(A<string>._, A<string>._, A<string>._))
                .MustHaveHappenedOnceExactly();
            page.FindAll(".qz-confirm-dialog").Should().BeEmpty("the dialog closes as soon as it is confirmed, while the call is out");
        });

        deleting.SetResult();
        await secondClick;

        page.WaitForAssertion(() => context.ActionLog.GetLatest(10).Should().ContainSingle(
            "one delete was sent, so one is recorded"));
    }

    /// <summary>
    /// <em>Trigger now</em> and <em>Trigger group</em> are disabled while their request is out, so a double
    /// click fires the job, or every job in the group, once.
    /// </summary>
    [TestCase("Trigger now")]
    [TestCase("Trigger group")]
    public void ATriggerButtonIsDisabledWhileItsRequestIsOut(string label)
    {
        GivenJobs(DashboardComponentContext.JobKeys("reports", 1));
        TaskCompletionSource triggering = new(TaskCreationOptions.RunContinuationsAsynchronously);
        A.CallTo(() => context.Api.TriggerJob(A<string>._, A<string>._, A<string>._))
            .Returns(triggering.Task);
        IRenderedComponent<Jobs> page = context.Render<Jobs>();

        Button(page, label).Click();

        page.WaitForAssertion(() => Button(page, label).HasAttribute("disabled").Should().BeTrue(
            "its request is still out, and a second click would fire again"));

        triggering.SetResult();

        page.WaitForAssertion(() => Button(page, label).HasAttribute("disabled").Should().BeFalse("the request has answered"));
        A.CallTo(() => context.Api.TriggerJob(A<string>._, A<string>._, A<string>._))
            .MustHaveHappenedOnceExactly();
    }

    private static IElement Button(IRenderedComponent<Jobs> page, string label)
    {
        return page.FindAll("button").First(button => button.TextContent.Trim() == label);
    }

    private void GivenJobs(IReadOnlyList<JobKeyDto> jobs)
    {
        A.CallTo(() => context.Api.GetJobKeys(A<string>._, A<string?>._)).Returns([.. jobs]);
    }
}
