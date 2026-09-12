using Bunit;

using Quartz.Dashboard.Components.Pages;
using Quartz.HttpApiContract;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The Live Logs page, driven by pushing events onto the stream it reads.
/// </summary>
/// <remarks>
/// <para>
/// The page used to open a SignalR connection back to the dashboard's own public hub URL, replaying the
/// visitor's cookie — which is the loopback that fails behind a reverse proxy, and which made rendering the
/// page the same thing as opening a socket. It reads Quartz's event stream now: the process's own for a
/// scheduler here, and the target's for one somewhere else, which is a distinction the page cannot make
/// and does not have to.
/// </para>
/// <para>
/// The events arrive on the stream's own task, so the assertions wait for the render rather than assuming
/// it: pushing an event and reading the markup on the next line is a race the test would sometimes win.
/// </para>
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
    public void ThePageWatchesTheActiveSchedulersStream()
    {
        IRenderedComponent<LiveLogs> page = context.Render<LiveLogs>();

        page.WaitForAssertion(() => context.Events.Subscribed.Should().Equal([TestData.SchedulerName],
            "a page that subscribed to nothing receives nothing"));

        page.Markup.Should().Contain("● Streaming");
        page.Markup.Should().Contain("Listening to: " + TestData.SchedulerName);
        page.Markup.Should().Contain("this node is " + TestData.SchedulerInstanceId,
            "the stream is fed by every node running that scheduler, so the page has to say which of them "
            + "its own process is");
    }

    [Test]
    public void AnEventSaysWhichNodeRaisedIt()
    {
        IRenderedComponent<LiveLogs> page = context.Render<LiveLogs>();

        Push(page, Event(SchedulerEventKind.JobExecuted, node: "node-b") with
        {
            JobKey = new KeyDto("nightly", "reports"),
            TriggerKey = new KeyDto("at-midnight", "reports"),
            RunTime = TimeSpan.FromSeconds(2)
        });

        page.WaitForAssertion(() => page.TextOfAll(".qz-live-node").Should().Equal(["node-b"],
            "one browser watching a clustered scheduler sees every node's events at once"));
    }

    [Test]
    public void AnEventThatNamesNoNodeIsStillListed()
    {
        IRenderedComponent<LiveLogs> page = context.Render<LiveLogs>();

        Push(page, Event(SchedulerEventKind.JobExecuted, node: "") with { JobKey = new KeyDto("nightly", "reports") });

        page.WaitForAssertion(() => page.TextOfAll(".qz-live-node").Should().Equal(["—"],
            "an event that says nothing about a node is a gap in the row, not a dropped event"));
    }

    /// <summary>
    /// A row says which job ran, how long it took and how it went, read off the event's own fields.
    /// </summary>
    /// <remarks>
    /// The page used to render whatever the hub's JSON stringified to, because eleven payload shapes
    /// arrived as <c>JsonElement</c>s and reading one member of each would have cost eleven
    /// deserializations. It holds the record now.
    /// </remarks>
    [Test]
    public void AJobThatRanIsListedWithItsJobItsTriggerAndItsDuration()
    {
        IRenderedComponent<LiveLogs> page = context.Render<LiveLogs>();

        Push(page, Event(SchedulerEventKind.JobExecuted) with
        {
            JobKey = new KeyDto("nightly", "reports"),
            TriggerKey = new KeyDto("at-midnight", "reports"),
            RunTime = TimeSpan.FromMilliseconds(1500)
        });

        page.WaitForAssertion(() =>
        {
            page.TextOfAll(".qz-live-type").Should().Equal(["JobExecuted"]);
            page.TextOfAll(".qz-live-description").Should().Equal(["reports.nightly · ran for 1.5 s"]);
            page.Find(".qz-live-event").ClassList.Should().Contain("qz-live-success",
                "the category is what makes a wall of events readable at a glance");
        });
    }

    [Test]
    public void AJobThatFailedIsListedAsAFailure()
    {
        IRenderedComponent<LiveLogs> page = context.Render<LiveLogs>();

        Push(page, Event(SchedulerEventKind.JobExecuted) with
        {
            JobKey = new KeyDto("nightly", "reports"),
            TriggerKey = new KeyDto("at-midnight", "reports"),
            RunTime = TimeSpan.FromSeconds(3),
            ExceptionMessage = "the job threw"
        });

        page.WaitForAssertion(() =>
        {
            page.Find(".qz-live-description").TextContent.Should().Contain("failed: the job threw");
            page.Find(".qz-live-event").ClassList.Should().Contain("qz-live-error",
                "an execution that threw is the row an operator is looking for");
        });
    }

    /// <summary>
    /// The two kinds the dashboard's hub never carried, which the stream does.
    /// </summary>
    [Test]
    public void AnInterruptedFiringAndATriggerInErrorAreListed()
    {
        IRenderedComponent<LiveLogs> page = context.Render<LiveLogs>();

        Push(page, Event(SchedulerEventKind.JobInterrupted) with
        {
            JobKey = new KeyDto("nightly", "reports"),
            FireInstanceId = "fire-1"
        });
        Push(page, Event(SchedulerEventKind.TriggerInError) with { TriggerKey = new KeyDto("at-midnight", "reports") });

        page.WaitForAssertion(() =>
        {
            page.TextOfAll(".qz-live-type").Should().Equal(["TriggerInError", "JobInterrupted"]);
            page.TextOfAll(".qz-live-description").Should().Equal(
            [
                "reports.at-midnight · will not fire until it is reset",
                "reports.nightly · firing fire-1"
            ]);
        });
    }

    [Test]
    public void TheNewestEventIsListedFirst()
    {
        IRenderedComponent<LiveLogs> page = context.Render<LiveLogs>();

        Push(page, Event(SchedulerEventKind.TriggerFired) with { TriggerKey = new KeyDto("first", "reports") });
        Push(page, Event(SchedulerEventKind.TriggerMisfired) with { TriggerKey = new KeyDto("second", "reports") });

        page.WaitForAssertion(() => page.TextOfAll(".qz-live-type").Should().Equal(
            ["TriggerMisfired", "TriggerFired"],
            "a live view is read from the top"));
    }

    [Test]
    public void TheEventTypeFilterHidesWhatItDeselects()
    {
        IRenderedComponent<LiveLogs> page = context.Render<LiveLogs>();
        Push(page, Event(SchedulerEventKind.JobExecuted) with { JobKey = new KeyDto("nightly", "reports") });
        page.WaitForAssertion(() => page.FindAll(".qz-live-event").Should().NotBeEmpty());

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
        Push(page, Event(SchedulerEventKind.JobExecuted) with { JobKey = new KeyDto("nightly", "reports") });
        Push(page, Event(SchedulerEventKind.TriggerFired) with { TriggerKey = new KeyDto("at-midnight", "reports") });
        page.WaitForAssertion(() => page.FindAll(".qz-live-event").Should().HaveCount(2));

        page.Find("#qz-live-filter-JobExecuted").Change(false);

        page.TextOfAll(".qz-live-type").Should().Equal(["TriggerFired"]);
    }

    /// <summary>
    /// A heartbeat never reaches the page: the transport consumes it, and a filter checkbox for it would be
    /// a checkbox for nothing.
    /// </summary>
    [Test]
    public void TheHeartbeatIsNotOneOfTheEventTypes()
    {
        IRenderedComponent<LiveLogs> page = context.Render<LiveLogs>();

        page.FindAll("#qz-live-filter-Heartbeat").Should().BeEmpty(
            "the heartbeat is the transport saying the connection is alive, not something a scheduler did");
        page.FindAll("#qz-live-filter-JobInterrupted").Should().NotBeEmpty(
            "every kind a scheduler raises is filterable, including the two the hub never carried");
    }

    /// <summary>
    /// A scheduler in another process is watched through its own target's stream, and the page cannot tell.
    /// </summary>
    /// <remarks>
    /// This is what #3387 left undone: the events were broadcast onto the hub of the process the scheduler
    /// ran in, so Live Logs for a remote target showed a notice instead of a feed.
    /// </remarks>
    [Test]
    public void ASchedulerInAnotherProcessIsWatchedThroughItsOwnTarget()
    {
        using DashboardComponentContext remote = new(remoteEventSourceFor: TestData.SchedulerName);
        remote.WithScheduler(origin: SchedulerOrigin.Remote);
        remote.Navigate("/quartz/live");

        IRenderedComponent<LiveLogs> page = remote.Render<LiveLogs>();

        page.WaitForAssertion(() => remote.RemoteEvents!.Subscribed.Should().Equal([TestData.SchedulerName],
            "the scheduler runs somewhere else, so its events are read from there"));
        remote.Events.Subscribed.Should().BeEmpty(
            "this process's stream carries this process's schedulers, and showing those would be showing somebody else's events");

        remote.RemoteEvents!.Push(Event(SchedulerEventKind.TriggerFired, node: "remote-node") with
        {
            TriggerKey = new KeyDto("at-midnight", "reports")
        });

        page.WaitForAssertion(() =>
        {
            page.TextOfAll(".qz-live-type").Should().Equal(["TriggerFired"]);
            page.TextOfAll(".qz-live-node").Should().Equal(["remote-node"]);
        });

        page.Markup.Should().NotContain("3387",
            "the notice that a remote scheduler has no events here goes with the feature that gives it some");
    }

    /// <summary>
    /// A target that serves no event stream says so, rather than showing a page that looks idle.
    /// </summary>
    [Test]
    public void ATargetThatStreamsNoEventsIsReportedRatherThanShownAsAnIdleFeed()
    {
        using DashboardComponentContext remote = new(remoteEventSourceFor: TestData.SchedulerName);
        remote.RemoteEvents!.Failure = new NotSupportedException("the target does not serve an event stream");
        remote.WithScheduler(origin: SchedulerOrigin.Remote);
        remote.Navigate("/quartz/live");

        IRenderedComponent<LiveLogs> page = remote.Render<LiveLogs>();

        page.WaitForAssertion(() =>
        {
            page.Markup.Should().Contain("does not serve an event stream");
            page.Markup.Should().Contain("● Not streaming",
                "a page that says nothing about its stream looks like a scheduler doing nothing");
        });
    }

    [Test]
    public void AContainerWithNoEventStreamSaysSo()
    {
        using DashboardComponentContext withoutEvents = new(registerEventSource: false);
        withoutEvents.WithScheduler();
        withoutEvents.Navigate("/quartz/live");

        IRenderedComponent<LiveLogs> page = withoutEvents.Render<LiveLogs>();

        page.WaitForAssertion(() => page.Markup.Should().Contain("registers no event stream",
            "an application that registered none is a different case from a scheduler with nothing to say"));
    }

    [Test]
    public void SwitchingSchedulerLeavesTheOldStreamBeforeWatchingTheNewOne()
    {
        IRenderedComponent<LiveLogs> page = context.Render<LiveLogs>();
        page.WaitForAssertion(() => context.Events.Subscribed.Should().Equal([TestData.SchedulerName]));

        context.SchedulerState.AvailableSchedulers =
        [
            TestData.Dashboard.SchedulerHeader(TestData.SchedulerName),
            TestData.Dashboard.SchedulerHeader("reporting")
        ];
        context.SchedulerState.ActiveSchedulerName = "reporting";

        page.WaitForAssertion(() =>
        {
            context.Events.Subscribed.Should().Equal([TestData.SchedulerName, "reporting"],
                "a page still reading the old stream keeps showing the scheduler the reader navigated away from");
            page.Markup.Should().Contain("Listening to: reporting");
        });

        context.Events.Push(Event(SchedulerEventKind.TriggerFired, schedulerName: TestData.SchedulerName) with
        {
            TriggerKey = new KeyDto("at-midnight", "reports")
        });

        page.FindAll(".qz-live-event").Should().BeEmpty(
            "the events of the scheduler that was left are not this page's any more");
    }

    /// <summary>
    /// A stream that ends stops the page saying it is streaming, so a reader can tell a live view from a
    /// stopped one.
    /// </summary>
    [Test]
    public void AStreamThatEndsIsNoLongerReportedAsStreaming()
    {
        IRenderedComponent<LiveLogs> page = context.Render<LiveLogs>();
        page.WaitForAssertion(() => page.Markup.Should().Contain("● Streaming"));

        context.Events.End(TestData.SchedulerName);

        page.WaitForAssertion(() => page.Markup.Should().Contain("● Not streaming"));
    }

    /// <summary>
    /// Pushes one event and lets the page's own reader pick it up.
    /// </summary>
    /// <remarks>
    /// The push has to happen after the page has subscribed, which is the first thing it does when it
    /// renders: a stream carries what happens after a subscription is made, here as everywhere.
    /// </remarks>
    private void Push(IRenderedComponent<LiveLogs> page, SchedulerEvent schedulerEvent)
    {
        page.WaitForAssertion(() => context.Events.Subscribed.Should().NotBeEmpty("the page subscribes when it renders"));
        context.Events.Push(schedulerEvent);
    }

    private static SchedulerEvent Event(
        SchedulerEventKind kind,
        string schedulerName = TestData.SchedulerName,
        string node = TestData.SchedulerInstanceId) => new()
    {
        Kind = kind,
        SchedulerName = schedulerName,
        SchedulerInstanceId = node,
        OccurredAtUtc = DateTimeOffset.UtcNow
    };
}
