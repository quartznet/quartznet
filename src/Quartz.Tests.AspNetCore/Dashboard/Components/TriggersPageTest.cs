using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The Triggers page's paging, group filtering and state filtering.
/// </summary>
public class TriggersPageTest
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
    public void TriggersAreListedUnderTheirGroupWithTheirState()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2, TriggerState.Paused));

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.TextOfAll("h2").Should().Equal(["nightly"]);
        page.TextOfAll("td.qz-col-state").Should().Equal(["Paused", "Paused"],
            "the state comes off the listing itself rather than from a call per trigger");
    }

    [Test]
    public void ThePagerAsksForTheSliceThePageNumberNames()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 60));

        IRenderedComponent<Triggers> page = context.Render<Triggers>();
        page.TextOfAll(".qz-key-badge-value").Should().HaveCount(25, "the page size is 25");

        page.FindAll(".qz-pagination button").First(button => button.TextContent.Trim() == "2").Click();

        A.CallTo(() => context.Api.GetTriggers(
                TestData.SchedulerName,
                A<DashboardTriggerQuery>.That.Matches(query => query.Skip == 25 && query.Take == 25),
                A<CancellationToken>._))
            .MustHaveHappened();
    }

    [Test]
    public void TheStateFilterIsPassedToTheQueryAndShownAsSelected()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 1, TriggerState.Error));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        IElement errorOnly = page.FindAll("button").First(button => button.TextContent.Trim() == "Error only");
        errorOnly.ClassList.Should().NotContain("qz-button-primary", "no state filter is applied yet");

        errorOnly.Click();

        A.CallTo(() => context.Api.GetTriggers(
                TestData.SchedulerName,
                A<DashboardTriggerQuery>.That.Matches(query => query.State == TriggerState.Error && query.Skip == 0),
                A<CancellationToken>._))
            .MustHaveHappened();
        page.FindAll("button").First(button => button.TextContent.Trim() == "Error only")
            .ClassList.Should().Contain("qz-button-primary",
                "which filter is applied has to be visible, or the listing looks like the whole truth");
    }

    [Test]
    public void ChangingTheStateFilterReturnsToTheFirstPage()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 60));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();
        page.FindAll(".qz-pagination button").First(button => button.TextContent.Trim() == "2").Click();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Executing only").Click();

        // A narrowed listing has fewer pages, so the page number the reader was on means nothing.
        A.CallTo(() => context.Api.GetTriggers(
                TestData.SchedulerName,
                A<DashboardTriggerQuery>.That.Matches(query => query.State == TriggerState.Executing && query.Skip == 0),
                A<CancellationToken>._))
            .MustHaveHappened();
    }

    [Test]
    public void ReadOnlyModeHidesEveryMutatingAction()
    {
        context.Options.ReadOnly = true;
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.HasButton("Pause").Should().BeFalse();
        page.HasButton("Unschedule").Should().BeFalse();
        page.HasButton("Pause group").Should().BeFalse();
        page.HasButton("Error only").Should().BeTrue("filtering is reading, not writing");
    }

    [Test]
    public void AGroupActionAppliesToTheGroupItNamesAndNotToTheOnesThatMerelyContainIt()
    {
        GivenTriggers([
            .. TestData.Dashboard.TriggerHeaders("nightly", 2),
            .. TestData.Dashboard.TriggerHeaders("nightly-archive", 3)
        ]);
        A.CallTo(() => context.Api.PauseTrigger(A<string>._, A<TriggerKeyDto>._, A<CancellationToken>._)).Returns(true);
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Pause group").Click();

        A.CallTo(() => context.Api.PauseTrigger(
                TestData.SchedulerName,
                A<TriggerKeyDto>.That.Matches(key => key.Group == "nightly-archive"),
                A<CancellationToken>._))
            .MustNotHaveHappened();
        context.Toasts.Messages.Should().ContainSingle()
            .Which.Message.Should().Be("Paused 2 of 2 trigger(s) in group nightly.");
    }

    [Test]
    public void ATriggerWithNoExecutionGroupSaysSoRatherThanShowingNothing()
    {
        GivenTriggers([new TriggerHeaderDto("nightly", "trigger-1", "Cron", "0/5 * * * * ?", TriggerState.Normal, null)]);

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.Markup.Should().Contain("qz-muted",
            "an empty cell reads as a rendering fault; an em dash reads as 'no execution group'");
    }

    private void GivenTriggers(IReadOnlyList<TriggerHeaderDto> triggers)
    {
        A.CallTo(() => context.Api.GetTriggers(A<string>._, A<DashboardTriggerQuery>._, A<CancellationToken>._))
            .ReturnsLazily((string _, DashboardTriggerQuery query, CancellationToken _) =>
            {
                List<TriggerHeaderDto> matched = triggers
                    .Where(trigger => (query.GroupContains is null
                            || trigger.Group.Contains(query.GroupContains, StringComparison.OrdinalIgnoreCase))
                        && (query.State is null || trigger.State == query.State))
                    .ToList();

                return TestData.Dashboard.Page<TriggerHeaderDto>(
                    matched.Skip(query.Skip).Take(query.Take).ToList(),
                    matched.Count);
            });
    }
}
