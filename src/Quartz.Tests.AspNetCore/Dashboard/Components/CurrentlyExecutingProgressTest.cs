using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// Currently Executing draws what a running job last reported: a bar, its percentage and the job's own
/// words, for every node of a cluster the listing covers.
/// </summary>
public class CurrentlyExecutingProgressTest
{
    [Test]
    public void AFiringThatReportedIsDrawnAsABarWithItsMessage()
    {
        using DashboardComponentContext context = new();
        context.WithScheduler();
        GivenFiring(context, progress: 40, message: "page 4 of 10");

        IRenderedComponent<CurrentlyExecuting> page = context.Render<CurrentlyExecuting>();

        page.WaitForAssertion(() =>
        {
            IElement bar = page.Find("[role=progressbar]");
            bar.GetAttribute("aria-valuenow").Should().Be("40",
                "the value is on the element a screen reader announces, not only in the width of a div");
            page.Find(".qz-progress-fill").GetAttribute("style").Should().Contain("width: 40%");
            page.Find(".qz-progress-value").TextContent.Should().Be("40%");
            page.Find(".qz-progress-message").TextContent.Should().Be("page 4 of 10",
                "the job's own words are what say which part of the work the percentage is of");
        });
    }

    [Test]
    public void AFiringThatHasNotReportedShowsNoBar()
    {
        using DashboardComponentContext context = new();
        context.WithScheduler();
        GivenFiring(context, progress: null, message: null);

        IRenderedComponent<CurrentlyExecuting> page = context.Render<CurrentlyExecuting>();

        page.WaitForAssertion(() =>
        {
            page.Markup.Should().Contain("reports.job-1", "the firing is listed");
            page.FindAll("[role=progressbar]").Should().BeEmpty(
                "a bar at zero would claim the job reported nothing done, when it reported nothing at all");
        });
    }

    [Test]
    public void ProgressIsShownOnAReadOnlyDashboardToo()
    {
        using DashboardComponentContext context = new(options => options.ReadOnly = true);
        context.WithScheduler();
        GivenFiring(context, progress: 75, message: null);

        IRenderedComponent<CurrentlyExecuting> page = context.Render<CurrentlyExecuting>();

        page.WaitForAssertion(() =>
        {
            page.Find("[role=progressbar]").GetAttribute("aria-valuenow").Should().Be("75",
                "progress is something to read, and read-only takes away what changes a scheduler");
            page.FindAll(".qz-progress-message").Should().BeEmpty("the job said nothing beside the percentage");
            page.FindAll("button").Should().NotContain(button => button.TextContent.Trim() == "Interrupt");
        });
    }

    private static void GivenFiring(DashboardComponentContext context, int? progress, string? message)
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
                {
                    Progress = progress,
                    ProgressMessage = message
                }
            ]));
    }
}
