using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Layout;
using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The header's scheduler picker: which schedulers it offers, which one it starts on, and how it
/// reports the one it is on.
/// </summary>
public class SchedulerSelectorTest
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

    [Test]
    public void EverySchedulerInTheContainerIsOfferedAndTheFirstIsSelected()
    {
        GivenSchedulers(("core", SchedulerStatus.Running), ("reporting", SchedulerStatus.Standby));

        IRenderedComponent<SchedulerSelector> selector = context.Render<SchedulerSelector>();

        selector.TextOfAll("option").Should().Equal(["core", "reporting"]);
        context.SchedulerState.ActiveSchedulerName.Should().Be("core",
            "a dashboard that opens on no scheduler shows nothing and says nothing about why");
    }

    [TestCase(SchedulerStatus.Running, "qz-state-running")]
    [TestCase(SchedulerStatus.Standby, "qz-state-standby")]
    [TestCase(SchedulerStatus.Shutdown, "qz-state-shutdown")]
    [TestCase(SchedulerStatus.ShuttingDown, "qz-state-shutting-down")]
    [TestCase(SchedulerStatus.Created, "qz-state-created")]
    public void TheStatusIsShownWithTheOneMapping(SchedulerStatus status, string expectedModifier)
    {
        GivenSchedulers(("core", status));

        IRenderedComponent<SchedulerSelector> selector = context.Render<SchedulerSelector>();

        selector.SchedulerStatusModifier().Should().Be(expectedModifier);
        selector.Find(".qz-state-dot").ClassList.Should().Contain("qz-state-dot-lg",
            "the header draws the larger dot");
    }

    [Test]
    public void SelectingAnotherSchedulerSwitchesTheDashboardToIt()
    {
        GivenSchedulers(("core", SchedulerStatus.Running), ("reporting", SchedulerStatus.Standby));
        IRenderedComponent<SchedulerSelector> selector = context.Render<SchedulerSelector>();

        selector.Find("select").Change("reporting");

        context.SchedulerState.ActiveSchedulerName.Should().Be("reporting");
        selector.SchedulerStatusModifier().Should().Be("qz-state-standby",
            "the status shown is the selected scheduler's, not the one the page opened on");
    }

    [Test]
    public void AnApiThatCannotBeReachedSaysSoRatherThanShowingAStatus()
    {
        A.CallTo(() => context.Api.GetSchedulers(A<CancellationToken>._))
            .Throws(new InvalidOperationException("no answer"));

        IRenderedComponent<SchedulerSelector> selector = context.Render<SchedulerSelector>();

        selector.Markup.Should().Contain("Unavailable",
            "'Unavailable' is not a SchedulerStatus, which is why it is a label of its own");
        selector.FindAll(".qz-state-label").Should().BeEmpty(
            "there is no status to show, and Unknown would claim there is a scheduler in some state");
    }

    [Test]
    public void WhileTheSchedulersAreBeingFetchedThePickerSaysSo()
    {
        A.CallTo(() => context.Api.GetSchedulers(A<CancellationToken>._)).Returns(new List<SchedulerHeaderDto>());

        IRenderedComponent<SchedulerSelector> selector = context.Render<SchedulerSelector>();

        selector.TextOfAll("option").Should().Equal(["Loading…"],
            "an empty picker reads as a process with no schedulers in it");
    }

    private void GivenSchedulers(params (string Name, SchedulerStatus Status)[] schedulers)
    {
        List<SchedulerHeaderDto> headers = [];
        foreach ((string name, SchedulerStatus status) in schedulers)
        {
            headers.Add(TestData.Dashboard.SchedulerHeader(name, status));
            A.CallTo(() => context.Api.GetScheduler(name, A<CancellationToken>._))
                .Returns(TestData.Dashboard.SchedulerDetail(status, name));
        }

        A.CallTo(() => context.Api.GetSchedulers(A<CancellationToken>._)).Returns(headers);
    }
}
