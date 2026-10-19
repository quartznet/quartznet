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

    /// <summary>
    /// A running firing has no history row yet — the recorder names one when the job returns — so the
    /// row links to its trigger's finished runs, each of which opens its own execution page.
    /// </summary>
    [Test]
    public void ARunningFiringLinksToItsTriggersFinishedRuns()
    {
        using DashboardComponentContext context = new(options => options.ReadOnly = true);
        context.WithScheduler();
        GivenFiring(context, progress: 10, message: null);

        IRenderedComponent<CurrentlyExecuting> page = context.Render<CurrentlyExecuting>();

        page.WaitForAssertion(() =>
        {
            IElement link = page.Find("[data-testid=executing-history-link]");
            link.GetAttribute("href").Should().Be("quartz/history?triggerGroup=nightly&triggerName=trigger-1",
                "the history page narrowed to this trigger exactly is where its execution pages are; the text "
                + "filter would also list a trigger-10 beside it");
            page.TextOfAll("thead th").Should().Contain("History", "reading history is not a mutation, so read-only keeps it");
        });
    }

    /// <summary>
    /// A firing another node was asked to stop is still running until that node's next check-in, so the row
    /// says the request is pending rather than leaving the operator to wonder whether the click took (#4141).
    /// </summary>
    [Test]
    public void AFiringAskedToStopFromAnotherNodeSaysAnInterruptIsRequested()
    {
        using DashboardComponentContext context = new(options => options.ReadOnly = true);
        context.WithScheduler();
        GivenFiring(context, progress: null, message: null, interruptRequestedUtc: TestData.Dashboard.FiredAt.AddSeconds(5));

        IRenderedComponent<CurrentlyExecuting> page = context.Render<CurrentlyExecuting>();

        page.WaitForAssertion(() =>
        {
            IElement pending = page.Find("[data-testid=interrupt-requested]");
            pending.TextContent.Trim().Should().Be("Interrupt requested",
                "reading that a stop is pending is not a mutation, so a read-only dashboard shows it too");
            pending.GetAttribute("title").Should().Contain("next check-in", "the hover says when the node acts on it");
        });
    }

    [Test]
    public void AFiringNobodyAskedToStopSaysNothingOfIt()
    {
        using DashboardComponentContext context = new();
        context.WithScheduler();
        GivenFiring(context, progress: null, message: null);

        IRenderedComponent<CurrentlyExecuting> page = context.Render<CurrentlyExecuting>();

        page.WaitForAssertion(() =>
        {
            page.Markup.Should().Contain("reports.job-1", "the firing is listed");
            page.FindAll("[data-testid=interrupt-requested]").Should().BeEmpty();
        });
    }

    /// <summary>
    /// A window onto an attached store offers Interrupt from 4.7: it asks the node running the firing through
    /// the firing's row.
    /// </summary>
    [Test]
    public void AWindowOffersInterrupt()
    {
        using DashboardComponentContext context = new();
        context.WithScheduler(origin: SchedulerOrigin.Window, target: "db");
        GivenFiring(context, progress: null, message: null);

        IRenderedComponent<CurrentlyExecuting> page = context.Render<CurrentlyExecuting>();

        page.WaitForAssertion(() =>
            page.FindAll("button").Should().Contain(button => button.TextContent.Trim() == "Interrupt",
                "the window records the request on the row, which the node running the firing reads"));
    }

    private static void GivenFiring(DashboardComponentContext context, int? progress, string? message, DateTimeOffset? interruptRequestedUtc = null)
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
                    ProgressMessage = message,
                    InterruptRequestedUtc = interruptRequestedUtc
                }
            ]));
    }
}
