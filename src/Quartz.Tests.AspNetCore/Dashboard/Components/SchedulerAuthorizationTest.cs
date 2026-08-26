using Bunit;

using FakeItEasy;

using Microsoft.AspNetCore.Components;

using Quartz.Dashboard.Components.Layout;
using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

using DashboardPage = Quartz.Dashboard.Components.Pages.Dashboard;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// What <see cref="QuartzDashboardOptions.SchedulerAuthorizationPolicy" /> does to the dashboard: which
/// schedulers are offered, and what a page frame pointed at one the visitor may not see renders instead.
/// </summary>
public class SchedulerAuthorizationTest
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
    /// The picker offers what the visitor may reach and nothing else — a name it offered would be a name
    /// they could select, and how many tenants a process runs is itself something a tenant should not
    /// learn.
    /// </summary>
    [Test]
    public void ThePickerOffersOnlyTheSchedulersTheVisitorPassesFor()
    {
        GivenSchedulers("acme", "globex", "initech");
        context.WithSchedulerPolicy("acme");

        IRenderedComponent<SchedulerSelector> selector = context.Render<SchedulerSelector>();

        selector.TextOfAll("option").Should().Equal(["acme"],
            "a scheduler the policy refuses is not in the picker at all");
        context.SchedulerState.ActiveSchedulerName.Should().Be("acme",
            "the dashboard opens on the first scheduler the visitor may see, which is now the first of the "
            + "filtered listing rather than of the whole process");
    }

    /// <summary>
    /// The policy is asked about each scheduler by name, through the resource the handler is written
    /// against. This is the contract the sample's <c>AuthorizationHandler&lt;…, SchedulerResource&gt;</c>
    /// relies on.
    /// </summary>
    [Test]
    public void EachSchedulerIsAskedAboutByNameAgainstTheConfiguredPolicy()
    {
        GivenSchedulers("acme", "globex");
        context.WithSchedulerPolicy("acme");

        context.Render<SchedulerSelector>();

        context.AuthorizationService.Asked.Should().Contain(
            (DashboardComponentContext.SchedulerPolicyName, new SchedulerResource("globex")),
            "the refused scheduler is refused by the policy the options name, evaluated against the scheduler itself");
    }

    /// <summary>
    /// With the option unset nothing is asked and nothing is filtered, which is what keeps every dashboard
    /// that never configured a per-scheduler policy exactly as it was.
    /// </summary>
    [Test]
    public void WithNoPolicyEverySchedulerIsOfferedAndNothingIsAsked()
    {
        GivenSchedulers("acme", "globex");

        IRenderedComponent<SchedulerSelector> selector = context.Render<SchedulerSelector>();

        selector.TextOfAll("option").Should().Equal(["acme", "globex"]);
        context.AuthorizationService.Asked.Should().BeEmpty(
            "an unset policy is not a policy that always succeeds - there is nothing to evaluate");
    }

    /// <summary>
    /// The one read the picker makes about a single scheduler is its status, and it is not made for a
    /// scheduler the visitor may not see.
    /// </summary>
    [Test]
    public void ASchedulerTheVisitorMayNotSeeIsNotAskedForItsStatus()
    {
        GivenSchedulers("acme", "globex");
        context.WithSchedulerPolicy("acme");
        context.SchedulerState.ActiveSchedulerName = "globex";

        IRenderedComponent<SchedulerSelector> selector = context.Render<SchedulerSelector>();

        selector.Markup.Should().Contain("Not authorized");
        A.CallTo(() => context.Api.GetScheduler("globex", A<CancellationToken>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// The page frame pointed at a foreign scheduler renders the refusal instead of the page, so the page
    /// is never created and nothing it would have read is read.
    /// </summary>
    [Test]
    public void APageFrameOnAForeignSchedulerRendersTheRefusalAndCreatesNoPage()
    {
        GivenSchedulers("acme", "globex");
        context.WithSchedulerPolicy("acme");
        context.SchedulerState.ActiveSchedulerName = "globex";

        IRenderedComponent<DashboardLayout> layout = RenderLayoutAround<Jobs>();

        layout.Markup.Should().Contain("Not authorized");
        layout.Markup.Should().Contain("globex", "the refusal names the scheduler the visitor was pointed at");
        layout.FindAll("h1").Should().NotContain(heading => heading.TextContent.Trim() == "Jobs",
            "the page is not rendered at all, which is what guarantees it read nothing");

        // The frame's own header still lists the schedulers the visitor may see — that listing is the
        // filtered one. What must not have happened is any of the reads the page itself makes.
        A.CallTo(() => context.Api.GetJobs(A<string>._, A<DashboardJobQuery>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => context.Api.GetJobGroups(A<string>._, A<DashboardGroupQuery>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    /// <summary>
    /// The same frame on a scheduler the visitor may see renders the page, and the page reads its data.
    /// </summary>
    [Test]
    public void APageFrameOnTheVisitorsOwnSchedulerRendersThePage()
    {
        GivenSchedulers("acme", "globex");
        context.WithSchedulerPolicy("acme");
        context.SchedulerState.ActiveSchedulerName = "acme";

        IRenderedComponent<DashboardLayout> layout = RenderLayoutAround<Jobs>();

        layout.Markup.Should().NotContain("Not authorized");
        layout.FindAll("h1").Should().Contain(heading => heading.TextContent.Trim() == "Jobs");
        A.CallTo(() => context.Api.GetJobs("acme", A<DashboardJobQuery>._, A<CancellationToken>._)).MustHaveHappened();
    }

    /// <summary>
    /// The fleet view is a listing like the picker's, and is filtered like it.
    /// </summary>
    [Test]
    public void TheSchedulersPageListsOnlyTheSchedulersTheVisitorPassesFor()
    {
        GivenSchedulers("acme", "globex");
        context.WithSchedulerPolicy("acme");

        IRenderedComponent<Schedulers> page = context.Render<Schedulers>();

        page.Markup.Should().Contain("acme");
        page.Markup.Should().NotContain("globex",
            "the fleet view is where an operator counts tenants, so it must not count somebody else's");
        A.CallTo(() => context.Api.GetScheduler("globex", A<CancellationToken>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// The overview re-reads the scheduler listing after an action and moves to another scheduler when the
    /// one it acted on is gone — a shutdown leaves its registration behind with nothing to read. The
    /// scheduler it moves to has to be one the visitor may see.
    /// </summary>
    /// <remarks>
    /// This listing is the third the dashboard writes into <c>SchedulerState</c>, and the only one that
    /// then picks the active scheduler itself. Unfiltered it would hand a visitor somebody else's tenant
    /// and read it on the next line, without the page frame ever getting a say.
    /// </remarks>
    [Test]
    public void AnActionThatMovesTheOverviewOffItsSchedulerMovesItToOneTheVisitorMaySee()
    {
        GivenSchedulers("acme", "globex");
        context.WithSchedulerPolicy("acme");
        context.SchedulerState.ActiveSchedulerName = "acme";

        IRenderedComponent<DashboardPage> page = context.Render<DashboardPage>();

        // What the listing says once the action has been carried out: the scheduler it acted on is a
        // registration with nothing behind it, and the only scheduler still running is the foreign one.
        A.CallTo(() => context.Api.GetSchedulers(A<CancellationToken>._)).Returns(new List<SchedulerHeaderDto>
        {
            TestData.Dashboard.RegisteredSchedulerHeader("acme"),
            TestData.Dashboard.SchedulerHeader("globex")
        });

        page.FindAll("button").First(button => button.TextContent.Trim() == "Standby").Click();

        context.SchedulerState.ActiveSchedulerName.Should().Be("acme",
            "the visitor passes for no other scheduler, so there is nowhere else for the page to go");
        context.SchedulerState.AvailableSchedulers.Select(scheduler => scheduler.SchedulerName).Should().Equal(["acme"],
            "the refreshed listing is filtered like every other one, so the picker is not repopulated with "
            + "schedulers the visitor may not see");
        A.CallTo(() => context.Api.GetScheduler("globex", A<CancellationToken>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// Renders <typeparamref name="TPage" /> the way the dashboard does: inside the frame that decides
    /// whether it is rendered at all.
    /// </summary>
    private IRenderedComponent<DashboardLayout> RenderLayoutAround<TPage>() where TPage : IComponent
    {
        RenderFragment page = builder =>
        {
            builder.OpenComponent<TPage>(0);
            builder.CloseComponent();
        };

        return context.Render<DashboardLayout>(parameters => parameters.Add(layout => layout.Body, page));
    }

    private void GivenSchedulers(params string[] names)
    {
        List<SchedulerHeaderDto> headers = [];
        foreach (string name in names)
        {
            headers.Add(TestData.Dashboard.SchedulerHeader(name));
            A.CallTo(() => context.Api.GetScheduler(name, A<CancellationToken>._))
                .Returns(TestData.Dashboard.SchedulerDetail(SchedulerStatus.Running, name));
        }

        A.CallTo(() => context.Api.GetSchedulers(A<CancellationToken>._)).Returns(headers);
    }
}
