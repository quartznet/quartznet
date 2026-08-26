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

    /// <summary>
    /// A registration nothing has built is a tenant the container knows about, so the picker offers it —
    /// greyed out, because there is nothing behind it to show.
    /// </summary>
    /// <remarks>
    /// It used to be omitted, the listing being the repository's. An operator looking for a tenant that
    /// had failed to start found no trace of it and no way to tell that from "never registered".
    /// </remarks>
    [Test]
    public void ARegistrationNothingHasBuiltIsOfferedGreyedOutRatherThanOmitted()
    {
        GivenSchedulers(TestData.Dashboard.SchedulerHeader("core"), TestData.Dashboard.RegisteredSchedulerHeader("acme"));

        IRenderedComponent<SchedulerSelector> selector = context.Render<SchedulerSelector>();

        selector.TextOfAll("option").Should().Equal(["core", "acme (not created)"]);
        selector.FindAll("option")[1].HasAttribute("disabled").Should().BeTrue(
            "there is no scheduler behind the name, so selecting it would only produce an error");
        context.SchedulerState.ActiveSchedulerName.Should().Be("core",
            "the dashboard opens on a scheduler that exists when there is one");
    }

    [Test]
    public void ASchedulerThatHasNotBeenBuiltShowsTheNotCreatedStateRatherThanAnError()
    {
        GivenSchedulers(TestData.Dashboard.RegisteredSchedulerHeader("acme"));

        IRenderedComponent<SchedulerSelector> selector = context.Render<SchedulerSelector>();

        context.SchedulerState.ActiveSchedulerName.Should().Be("acme",
            "it is the only registration there is, so it is what the dashboard is about");
        selector.Markup.Should().Contain("Not created",
            "the listing already said no scheduler exists under this name, so asking for one and "
            + "reporting the refusal would dress a known state up as a fault");
        A.CallTo(() => context.Api.GetScheduler("acme", A<CancellationToken>._)).MustNotHaveHappened();
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
        }

        GivenSchedulers(headers.ToArray());
    }

    private void GivenSchedulers(params SchedulerHeaderDto[] schedulers)
    {
        foreach (SchedulerHeaderDto scheduler in schedulers)
        {
            if (scheduler.Status is { } status)
            {
                A.CallTo(() => context.Api.GetScheduler(scheduler.SchedulerName, A<CancellationToken>._))
                    .Returns(TestData.Dashboard.SchedulerDetail(status, scheduler.SchedulerName));
            }
        }

        A.CallTo(() => context.Api.GetSchedulers(A<CancellationToken>._)).Returns(schedulers.ToList());
    }
}
