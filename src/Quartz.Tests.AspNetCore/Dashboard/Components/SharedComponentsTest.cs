using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Microsoft.AspNetCore.Components;

using Quartz.Dashboard.Components.Layout;
using Quartz.Dashboard.Components.Shared;
using Quartz.Dashboard.Services;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The pieces every page is built out of, and the shell they sit in.
/// </summary>
public class SharedComponentsTest
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
    public void AToastAppearsWhenOneIsRaisedAndGoesWhenItIsDismissed()
    {
        IRenderedComponent<ToastHost> host = context.Render<ToastHost>();
        host.Markup.Should().BeEmpty("nothing has happened yet");

        host.InvokeAsync(() => context.Toasts.Error("the scheduler refused")).Wait();

        host.Find(".qz-toast").ClassList.Should().Contain("qz-toast-error",
            "a failure and a success must not look alike");
        host.Find(".qz-toast span").TextContent.Should().Be("the scheduler refused");

        host.Find(".qz-toast-close").Click();

        host.FindAll(".qz-toast").Should().BeEmpty();
    }

    [TestCase(0, "1s ago")]
    [TestCase(45, "45s ago")]
    [TestCase(90, "1m ago")]
    [TestCase(3 * 60 * 60, "3h ago")]
    [TestCase(50 * 60 * 60, "2d ago")]
    public void TimeAgoNamesTheLargestUnitThatStillMeansSomething(int secondsAgo, string expected)
    {
        IRenderedComponent<TimeAgo> timeAgo = context.Render<TimeAgo>(parameters => parameters
            .Add(x => x.Timestamp, DateTimeOffset.UtcNow.AddSeconds(-secondsAgo)));

        timeAgo.Find(".qz-time-ago").TextContent.Should().Be(expected);
    }

    [Test]
    public void ATimestampInTheFutureDoesNotCountBackwards()
    {
        IRenderedComponent<TimeAgo> timeAgo = context.Render<TimeAgo>(parameters => parameters
            .Add(x => x.Timestamp, DateTimeOffset.UtcNow.AddMinutes(10)));

        timeAgo.Find(".qz-time-ago").TextContent.Should().Be("1s ago",
            "clock skew between a scheduler and the dashboard must not render as '-9m ago'");
    }

    [Test]
    public void AMissingTimestampSaysNever()
    {
        IRenderedComponent<TimeAgo> timeAgo = context.Render<TimeAgo>();

        timeAgo.Find(".qz-time-ago").TextContent.Should().Be("never");
    }

    [Test]
    public void AnErrorAlertOffersARetryOnlyWhenThereIsSomethingToRetry()
    {
        IRenderedComponent<ErrorAlert> withoutRetry = context.Render<ErrorAlert>(parameters => parameters
            .Add(x => x.Message, "it failed"));
        withoutRetry.FindAll("button").Should().BeEmpty();

        int retries = 0;
        IRenderedComponent<ErrorAlert> withRetry = context.Render<ErrorAlert>(parameters => parameters
            .Add(x => x.Message, "it failed")
            .Add(x => x.OnRetry, EventCallback.Factory.Create(this, () => retries++)));

        withRetry.Find("button").Click();
        retries.Should().Be(1);
    }

    [Test]
    public void AnErrorAlertWithNothingToSayRendersNothing()
    {
        IRenderedComponent<ErrorAlert> alert = context.Render<ErrorAlert>();

        alert.Markup.Should().BeEmpty("an empty alert box is a rendering fault a reader cannot act on");
    }

    [Test]
    public void ThePagerOffersTwoPagesEitherSideOfTheCurrentOne()
    {
        IRenderedComponent<Pagination> pager = context.Render<Pagination>(parameters => parameters
            .Add(x => x.TotalItems, 250)
            .Add(x => x.PageSize, 25)
            .Add(x => x.CurrentPage, 5));

        pager.TextOfAll(".qz-pagination button").Should().Equal(["Prev", "3", "4", "5", "6", "7", "Next"],
            "ten pages of buttons is a scrollbar, not navigation");
    }

    [Test]
    public void ThePagerDisappearsWhenEverythingFitsOnOnePage()
    {
        IRenderedComponent<Pagination> pager = context.Render<Pagination>(parameters => parameters
            .Add(x => x.TotalItems, 3)
            .Add(x => x.PageSize, 25));

        pager.Markup.Should().BeEmpty();
    }

    [Test]
    public void ThePagerRefusesToLeaveTheRange()
    {
        List<int> requested = [];
        IRenderedComponent<Pagination> pager = context.Render<Pagination>(parameters => parameters
            .Add(x => x.TotalItems, 30)
            .Add(x => x.PageSize, 25)
            .Add(x => x.CurrentPage, 1)
            .Add(x => x.OnPageChanged, EventCallback.Factory.Create<int>(this, requested.Add)));

        pager.FindAll("button").First(button => button.TextContent.Trim() == "Prev").Click();
        pager.FindAll("button").First(button => button.TextContent.Trim() == "1").Click();
        requested.Should().BeEmpty("neither goes anywhere, so neither should reload the listing");

        pager.FindAll("button").First(button => button.TextContent.Trim() == "Next").Click();
        requested.Should().Equal([2]);
    }

    [Test]
    public void TheKeyBadgeShowsTheKeyTheWayQuartzWritesIt()
    {
        IRenderedComponent<KeyBadge> badge = context.Render<KeyBadge>(parameters => parameters
            .Add(x => x.GroupName, "reports")
            .Add(x => x.ItemName, "nightly"));

        badge.Find(".qz-key-badge-value").TextContent.Should().Be("reports.nightly");
        badge.Find(".qz-key-badge").GetAttribute("title").Should().Be("reports.nightly",
            "a key too long for its column is still readable on hover");
    }

    [Test]
    public void TheShellRendersTheNavigationTheHeaderAndThePage()
    {
        IRenderedComponent<DashboardLayout> layout = context.Render<DashboardLayout>(parameters => parameters
            .Add(x => x.Body, builder => builder.AddMarkupContent(0, "<p id=\"page-body\">the page</p>")));

        layout.Find("#page-body").TextContent.Should().Be("the page");
        layout.FindAll(".qz-sidebar-nav a").Should().NotBeEmpty("the navigation is part of the shell");
        layout.Find(".qz-dashboard").GetAttribute("data-theme").Should().Be("system",
            "the theme is applied to the shell, so every page follows it");
    }

    [Test]
    public void ChoosingAThemeAppliesItAndRemembersIt()
    {
        IRenderedComponent<DashboardLayout> layout = context.Render<DashboardLayout>(parameters => parameters
            .Add(x => x.Body, builder => builder.AddMarkupContent(0, "<p>the page</p>")));

        layout.Find("#qz-theme-select").Change("dark");

        layout.Find(".qz-dashboard").GetAttribute("data-theme").Should().Be("dark");
        context.SchedulerState.SelectedTheme.Should().Be("dark");
        context.JSInterop.VerifyInvoke("quartzDashboardPrefs.set")
            .Arguments.Should().Equal(["qz_theme", "dark"],
                "the preference has to survive the next page load, which is what the cookie is for");
    }

    [Test]
    public void ChoosingATimeZoneRefreshesEveryTimestampOnThePage()
    {
        // The fixture pins the zone to UTC, so any other installed zone is a change.
        string otherZoneId = TimeZoneInfo.GetSystemTimeZones()
            .First(zone => !string.Equals(zone.Id, TimeZoneInfo.Utc.Id, StringComparison.Ordinal))
            .Id;
        int notifications = 0;
        context.SchedulerState.OnSchedulerChanged += (_, _) => notifications++;
        IRenderedComponent<DashboardLayout> layout = context.Render<DashboardLayout>(parameters => parameters
            .Add(x => x.Body, builder => builder.AddMarkupContent(0, "<p>the page</p>")));

        layout.Find("#qz-timezone-select").Change(otherZoneId);

        context.SchedulerState.SelectedTimeZoneId.Should().Be(otherZoneId);
        notifications.Should().Be(1,
            "every page formats its timestamps in the selected zone, and none of them re-renders on its own");
    }

    [Test]
    public void AnUnknownTimeZoneFallsBackToTheMachinesRatherThanThrowing()
    {
        IRenderedComponent<DashboardLayout> layout = context.Render<DashboardLayout>(parameters => parameters
            .Add(x => x.Body, builder => builder.AddMarkupContent(0, "<p>the page</p>")));

        layout.Find("#qz-timezone-select").Change("Mars/Olympus_Mons");

        context.SchedulerState.SelectedTimeZoneId.Should().Be(TimeZoneInfo.Local.Id,
            "a stale cookie naming a zone this machine does not have must not take the dashboard down");
    }

    [Test]
    public void TheSpinnerSaysWhatItIsDoing()
    {
        IRenderedComponent<LoadingSpinner> spinner = context.Render<LoadingSpinner>();

        spinner.Find(".qz-loading-spinner").GetAttribute("role").Should().Be("status",
            "a spinner a screen reader cannot see is a page that never finishes loading");
        spinner.Markup.Should().Contain("Loading...");
    }

    [Test]
    public void TheFilterDebouncesSoOneReloadFollowsAWordRatherThanEachLetter()
    {
        List<string> filters = [];
        IRenderedComponent<SearchFilter> filter = context.Render<SearchFilter>(parameters => parameters
            .Add(x => x.Debounce, TimeSpan.FromMilliseconds(50))
            .Add(x => x.OnFilterChanged, EventCallback.Factory.Create<string>(this, filters.Add)));

        IElement input = filter.Find("input");
        input.Input("r");
        input.Input("re");
        input.Input("rep");

        filter.WaitForAssertion(() => filters.Should().Equal(["rep"],
            "one query per word typed, not one per keystroke"));
    }
}
