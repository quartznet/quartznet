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

    /// <summary>
    /// One group per target, this process first, and the option's value is the key while its label is the
    /// bare name: the group already says where the scheduler was found.
    /// </summary>
    [Test]
    public void SchedulersAreGroupedByTargetWithThisProcessFirst()
    {
        GivenSchedulers(
            TestData.Dashboard.SchedulerHeader("QuartzScheduler", origin: SchedulerOrigin.Remote, target: "w1"),
            TestData.Dashboard.SchedulerHeader("core"),
            TestData.Dashboard.SchedulerHeader("QuartzScheduler", origin: SchedulerOrigin.Remote, target: "w2"));

        IRenderedComponent<SchedulerSelector> selector = context.Render<SchedulerSelector>();

        selector.FindAll("optgroup").Select(group => group.GetAttribute("label")).Should().Equal(["This process", "w1", "w2"]);
        selector.FindAll("option").Select(option => option.GetAttribute("value")).Should().Equal(
            ["core", "w1/QuartzScheduler", "w2/QuartzScheduler"],
            "the value is the key, which is what every page addresses the scheduler by");
        selector.TextOfAll("option").Should().Equal(["core", "QuartzScheduler (remote)", "QuartzScheduler (remote)"],
            "the target is the group's label, so the option does not repeat it");
    }

    [Test]
    public void AClusterIsOfferedOnceWithItsNodeCountAndNothingIsAskedForIt()
    {
        GivenSchedulers(TestData.Dashboard.SchedulerHeader("QuartzScheduler", origin: SchedulerOrigin.Cluster, target: "a+b+c", members: ["a", "b", "c"]));

        IRenderedComponent<SchedulerSelector> selector = context.Render<SchedulerSelector>();

        selector.FindAll("optgroup").Select(group => group.GetAttribute("label")).Should().Equal(["cluster a+b+c"]);
        selector.TextOfAll("option").Should().Equal(["QuartzScheduler (cluster, 3 nodes)"]);
        context.SchedulerState.ActiveSchedulerName.Should().Be("a+b+c/QuartzScheduler");
        selector.WaitForAssertion(() => selector.Markup.Should().Contain("(3 nodes)",
            "the members are the nodes, which the row already carries"));
        A.CallTo(() => context.Api.QueryClusterNodes(A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public void TheRememberedSchedulerIsSelectedWhenTheListingStillCarriesIt()
    {
        context.Dispose();
        context = new DashboardComponentContext(rememberedScheduler: "w1/QuartzScheduler");
        GivenSchedulers(
            TestData.Dashboard.SchedulerHeader("core"),
            TestData.Dashboard.SchedulerHeader("QuartzScheduler", origin: SchedulerOrigin.Remote, target: "w1"));

        context.Render<SchedulerSelector>();

        context.SchedulerState.ActiveSchedulerName.Should().Be("w1/QuartzScheduler",
            "the browser remembered it, and the listing still carries it");
    }

    [Test]
    public void AStaleRememberedSchedulerFallsBackToTheFirstThatExists()
    {
        context.Dispose();
        context = new DashboardComponentContext(rememberedScheduler: "gone/QuartzScheduler");
        GivenSchedulers(
            TestData.Dashboard.RegisteredSchedulerHeader("acme"),
            TestData.Dashboard.SchedulerHeader("core"));

        context.Render<SchedulerSelector>();

        context.SchedulerState.ActiveSchedulerName.Should().Be("core",
            "a key the listing does not carry - a target that went away, a cluster whose membership changed - "
            + "is not a scheduler to open on");
    }

    [Test]
    public void SelectingASchedulerRemembersItsKeyInTheBrowser()
    {
        GivenSchedulers(
            TestData.Dashboard.SchedulerHeader("core"),
            TestData.Dashboard.SchedulerHeader("QuartzScheduler", origin: SchedulerOrigin.Remote, target: "w1"));
        IRenderedComponent<SchedulerSelector> selector = context.Render<SchedulerSelector>();

        selector.Find("select").Change("w1/QuartzScheduler");

        selector.WaitForAssertion(() => context.JSInterop.Invocations.Should().Contain(
            invocation => invocation.Identifier == "quartzDashboardPrefs.set"
                && invocation.Arguments.SequenceEqual(new object?[] { "qz_scheduler", "w1/QuartzScheduler" }),
            "the selection is remembered the way the theme is, so the next visit opens on it"));
    }

    /// <summary>
    /// A header whose target is empty rather than null is one reached through no target, as its display
    /// name already says: the key is the bare name.
    /// </summary>
    [Test]
    public void AnEmptyTargetOnAHeaderIsNoTarget()
    {
        SchedulerHeaderDto header = TestData.Dashboard.SchedulerHeader("core") with { Target = "" };

        header.Key.Should().Be("core");
        header.DisplayName.Should().Be("core");
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
                A.CallTo(() => context.Api.GetScheduler(scheduler.Key, A<CancellationToken>._))
                    .Returns(TestData.Dashboard.SchedulerDetail(status, scheduler.SchedulerName));
            }
        }

        A.CallTo(() => context.Api.GetSchedulers(A<CancellationToken>._)).Returns(schedulers.ToList());
    }
}
