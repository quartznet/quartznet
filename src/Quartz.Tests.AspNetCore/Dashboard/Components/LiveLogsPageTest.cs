using Bunit;

using Quartz.Dashboard.Components.Pages;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The Live Logs page, driven through the connection seam rather than a SignalR circuit.
/// </summary>
/// <remarks>
/// The page used to build its own <c>HubConnection</c>, which made rendering it indistinguishable from
/// opening a socket. It is handed one now, so what the page does with the events it receives — and with
/// a hub it cannot reach — is something a test can say.
/// </remarks>
public class LiveLogsPageTest
{
    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
        context.WithScheduler();
        context.Navigate("/quartz/live");
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    [Test]
    public void ThePageConnectsToTheHubUnderTheDashboardPathAndJoinsTheActiveScheduler()
    {
        IRenderedComponent<LiveLogs> page = context.Render<LiveLogs>();

        context.LiveConnections.LastHubUri.Should().Be(new Uri("http://localhost/quartz/hub"),
            "the hub travels with the dashboard path, so a dashboard behind a prefix reaches its own hub");
        context.LiveConnections.Current.Invocations.Should().Equal([("JoinScheduler", TestData.SchedulerName)],
            "a connection that joined nothing receives nothing");
        page.Markup.Should().Contain("● Connected");
        page.Markup.Should().Contain("Listening to: " + TestData.SchedulerName);
    }

    [Test]
    public void AnEventFromTheHubIsListedWithItsTypeAndPayload()
    {
        IRenderedComponent<LiveLogs> page = context.Render<LiveLogs>();

        page.InvokeAsync(() => context.LiveConnections.Current.Push("JobExecuted", "reports.nightly")).Wait();

        page.TextOfAll(".qz-live-type").Should().Equal(["JobExecuted"]);
        page.TextOfAll(".qz-live-description").Should().Equal(["JobExecuted: reports.nightly"]);
        page.Find(".qz-live-event").ClassList.Should().Contain("qz-live-success",
            "the category is what makes a wall of events readable at a glance");
    }

    [Test]
    public void TheNewestEventIsListedFirst()
    {
        IRenderedComponent<LiveLogs> page = context.Render<LiveLogs>();

        page.InvokeAsync(() => context.LiveConnections.Current.Push("TriggerFired", "first")).Wait();
        page.InvokeAsync(() => context.LiveConnections.Current.Push("TriggerMisfired", "second")).Wait();

        page.TextOfAll(".qz-live-type").Should().Equal(["TriggerMisfired", "TriggerFired"],
            "a live view is read from the top");
    }

    [Test]
    public void ALongPayloadIsTruncatedRatherThanFloodingTheRow()
    {
        IRenderedComponent<LiveLogs> page = context.Render<LiveLogs>();
        string payload = new('x', 400);

        page.InvokeAsync(() => context.LiveConnections.Current.Push("SchedulerError", payload)).Wait();

        string description = page.Find(".qz-live-description").TextContent;
        description.Should().Be("SchedulerError: " + new string('x', 180) + "…",
            "one event must not push every other one off the screen");
    }

    [Test]
    public void TheEventTypeFilterHidesWhatItDeselects()
    {
        IRenderedComponent<LiveLogs> page = context.Render<LiveLogs>();
        page.InvokeAsync(() => context.LiveConnections.Current.Push("JobExecuted", "one")).Wait();

        page.FindAll("button").First(button => button.TextContent.Trim() == "None").Click();

        page.FindAll(".qz-live-event").Should().BeEmpty();
        page.Markup.Should().Contain("No events match the selected event types",
            "the events are still there, which is a different thing from having received none");

        page.FindAll("button").First(button => button.TextContent.Trim() == "All").Click();

        page.TextOfAll(".qz-live-type").Should().Equal(["JobExecuted"]);
    }

    [Test]
    public void OneEventTypeCanBeTurnedOffOnItsOwn()
    {
        IRenderedComponent<LiveLogs> page = context.Render<LiveLogs>();
        page.InvokeAsync(() => context.LiveConnections.Current.Push("JobExecuted", "one")).Wait();
        page.InvokeAsync(() => context.LiveConnections.Current.Push("TriggerFired", "two")).Wait();

        page.Find("#qz-live-filter-JobExecuted").Change(false);

        page.TextOfAll(".qz-live-type").Should().Equal(["TriggerFired"]);
    }

    [Test]
    public void AHubThatCannotBeReachedIsReportedRatherThanShownAsAnEmptyFeed()
    {
        context.LiveConnections.StartFailure = new InvalidOperationException("the hub refused the connection");

        IRenderedComponent<LiveLogs> page = context.Render<LiveLogs>();

        page.Markup.Should().Contain("the hub refused the connection");
        page.Markup.Should().Contain("● Disconnected",
            "a page that says nothing about its connection looks like a scheduler doing nothing");
    }

    [Test]
    public void SwitchingSchedulerLeavesTheOldGroupBeforeJoiningTheNewOne()
    {
        context.Render<LiveLogs>();

        context.SchedulerState.ActiveSchedulerName = "reporting";

        context.LiveConnections.Current.Invocations.Should().Equal([
            ("JoinScheduler", TestData.SchedulerName),
            ("LeaveScheduler", TestData.SchedulerName),
            ("JoinScheduler", "reporting")
        ], "a connection still in the old group keeps streaming the scheduler the reader navigated away from");
    }

    [Test]
    public void ADroppedConnectionThatComesBackRejoinsTheScheduler()
    {
        IRenderedComponent<LiveLogs> page = context.Render<LiveLogs>();
        FakeDashboardLiveConnection connection = context.LiveConnections.Current;

        page.InvokeAsync(() => connection.Drop()).Wait();
        page.Markup.Should().Contain("● Disconnected");

        page.InvokeAsync(() => connection.Reconnect()).Wait();

        connection.Invocations.Should().Equal([
            ("JoinScheduler", TestData.SchedulerName),
            ("LeaveScheduler", TestData.SchedulerName),
            ("JoinScheduler", TestData.SchedulerName)
        ], "the hub forgets its groups when the connection goes, so a reconnected page must join again");
        page.Markup.Should().Contain("● Connected");
    }
}
