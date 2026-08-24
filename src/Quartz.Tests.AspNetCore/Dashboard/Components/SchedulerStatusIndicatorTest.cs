using Bunit;

using Quartz.Dashboard.Components.Shared;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The one place in the dashboard that turns a <see cref="SchedulerStatus" /> into a colour.
/// </summary>
/// <remarks>
/// The two views that show a scheduler's status used to classify it independently — a switch over the
/// enum in the header, a substring match over its name in the page below — so the same scheduler could
/// be amber in one and green in the other. One mapping, pinned here per status.
/// </remarks>
public class SchedulerStatusIndicatorTest
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

    [TestCase(SchedulerStatus.Created, "qz-state-created")]
    [TestCase(SchedulerStatus.Running, "qz-state-running")]
    [TestCase(SchedulerStatus.Standby, "qz-state-standby")]
    [TestCase(SchedulerStatus.ShuttingDown, "qz-state-shutting-down")]
    [TestCase(SchedulerStatus.Shutdown, "qz-state-shutdown")]
    [TestCase(SchedulerStatus.Unknown, "qz-state-waiting")]
    public void EachStatusHasItsOwnModifier(SchedulerStatus status, string expectedModifier)
    {
        IRenderedComponent<SchedulerStatusIndicator> indicator =
            context.Render<SchedulerStatusIndicator>(parameters => parameters.Add(x => x.Status, status));

        indicator.SchedulerStatusModifier().Should().Be(expectedModifier);
        indicator.Find(".qz-state-indicator").GetAttribute("title").Should().Be(status.ToString(),
            "the colour is a summary, and the name is what a reader hovers to confirm");
        indicator.Find(".qz-state-label").TextContent.Should().Be(status.ToString());
    }

    [Test]
    public void EveryStatusIsAccountedFor()
    {
        foreach (SchedulerStatus status in Enum.GetValues<SchedulerStatus>())
        {
            IRenderedComponent<SchedulerStatusIndicator> indicator =
                context.Render<SchedulerStatusIndicator>(parameters => parameters.Add(x => x.Status, status));

            indicator.SchedulerStatusModifier().Should().NotBeNullOrWhiteSpace(
                $"a status without a modifier renders as an uncoloured dot, and {status} is one a scheduler reports");
        }
    }

    [Test]
    public void TheHeaderDrawsALargerDotWithoutChangingTheStatusItMeans()
    {
        IRenderedComponent<SchedulerStatusIndicator> indicator = context.Render<SchedulerStatusIndicator>(parameters => parameters
            .Add(x => x.Status, SchedulerStatus.Running)
            .Add(x => x.Large, true));

        indicator.Find(".qz-state-dot").ClassList.Should().Contain("qz-state-dot-lg")
            .And.Contain("qz-state-running");
    }

    [Test]
    public void TheLabelCanBeLeftOffWhereTheDotIsEnough()
    {
        IRenderedComponent<SchedulerStatusIndicator> indicator = context.Render<SchedulerStatusIndicator>(parameters => parameters
            .Add(x => x.Status, SchedulerStatus.Standby)
            .Add(x => x.ShowLabel, false));

        indicator.FindAll(".qz-state-label").Should().BeEmpty();
        indicator.Find(".qz-state-indicator").GetAttribute("title").Should().Be("Standby",
            "the status still has to be readable, which is what the title is for");
    }
}
