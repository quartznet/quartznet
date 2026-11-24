using System.Text.RegularExpressions;

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

    [Test]
    public void AWarningToastLooksLikeNeitherAFailureNorASuccess()
    {
        IRenderedComponent<ToastHost> host = context.Render<ToastHost>();

        host.InvokeAsync(() => context.Toasts.Warning("nothing was sent")).Wait();

        host.Find(".qz-toast").ClassList.Should().Contain("qz-toast-warning",
            "the page declined to send something, which is neither a failure nor a success");
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

    [TestCase(23, 1, "1 2 3 4 5 … 23")]
    [TestCase(23, 3, "1 2 3 4 5 … 23")]
    [TestCase(23, 4, "1 2 3 4 5 … 23")]
    [TestCase(23, 5, "1 … 4 5 6 … 23")]
    [TestCase(23, 12, "1 … 11 12 13 … 23")]
    [TestCase(23, 19, "1 … 18 19 20 … 23")]
    [TestCase(23, 20, "1 … 19 20 21 22 23")]
    [TestCase(23, 21, "1 … 19 20 21 22 23")]
    [TestCase(23, 23, "1 … 19 20 21 22 23")]
    [TestCase(2, 1, "1 2")]
    [TestCase(7, 4, "1 2 3 4 5 6 7")]
    [TestCase(8, 1, "1 2 3 4 5 … 8")]
    [TestCase(8, 4, "1 2 3 4 5 … 8")]
    [TestCase(8, 5, "1 … 4 5 6 7 8")]
    [TestCase(8, 8, "1 … 4 5 6 7 8")]
    public void ThePagerKeepsTheFirstAndLastPagesInAFixedRowOfSlots(int totalPages, int currentPage, string expected)
    {
        IRenderedComponent<Pagination> pager = RenderPager(totalPages, currentPage);

        List<string> slots = pager.TextOfAll(".qz-pagination-controls > *");
        slots.First().Should().Be("Prev");
        slots.Last().Should().Be("Next");
        string.Join(" ", slots.Skip(1).SkipLast(1)).Should().Be(expected,
            "the row keeps one width as the reader moves, so the buttons stay under the pointer");
    }

    [Test]
    public void ThePagerKeepsAWindowOfPagesWhenTheCurrentOneIsPastTheEnd()
    {
        List<int> requested = [];
        IRenderedComponent<Pagination> pager = context.Render<Pagination>(parameters => parameters
            .Add(x => x.TotalItems, 23 * 25)
            .Add(x => x.PageSize, 25)
            .Add(x => x.CurrentPage, 30)
            .Add(x => x.OnPageChanged, EventCallback.Factory.Create<int>(this, requested.Add)));

        pager.TextOfAll(".qz-pagination-controls button").Should().Equal(["Prev", "1", "19", "20", "21", "22", "23", "Next"],
            "a listing that shrank under the reader still needs a way back to the pages that are left");
        pager.Find(".qz-pagination-info").TextContent.Should().Be("Page 23 of 23", "there is no page 30 of 23 to be on");
        pager.Find("[aria-current=page]").TextContent.Trim().Should().Be("23");

        pager.FindAll("button").First(button => button.TextContent.Trim() == "Prev").Click();
        requested.Should().Equal([22], "Prev steps back from the last page there is, not from one past it");
    }

    [Test]
    public void ThePagerGroupsItsButtonsAndSaysWhichPageOfHowMany()
    {
        IRenderedComponent<Pagination> pager = RenderPager(totalPages: 23, currentPage: 5);

        pager.Find("nav.qz-pagination").GetAttribute("aria-label").Should().Be("Pagination");
        pager.FindAll("nav.qz-pagination > button").Should().BeEmpty(
            "a button straight under the bar is spread across the full width with the space between");
        pager.FindAll(".qz-pagination-controls button").Should().HaveCount(7, "Prev, five pages and Next");
        pager.Find(".qz-pagination-info").TextContent.Should().Be("Page 5 of 23");
    }

    [Test]
    public void OnlyTheCurrentPageIsMarkedCurrent()
    {
        IRenderedComponent<Pagination> pager = RenderPager(totalPages: 23, currentPage: 12);

        pager.FindAll("[aria-current]").Select(button => (button.TextContent.Trim(), button.GetAttribute("aria-current")))
            .Should().Equal([("12", "page")], "a screen reader names the page the reader is on, and no other");
    }

    [Test]
    public void TheEllipsisIsNotSomethingToPress()
    {
        IRenderedComponent<Pagination> pager = RenderPager(totalPages: 23, currentPage: 12);

        pager.FindAll(".qz-pagination-ellipsis").Should().HaveCount(2)
            .And.AllSatisfy(gap => gap.TagName.Should().Be("SPAN"));
        pager.TextOfAll(".qz-pagination-controls button").Should().NotContain("…",
            "the skipped pages are not one page, so there is nowhere for a press to go");
    }

    [TestCase(1, true, false)]
    [TestCase(12, false, false)]
    [TestCase(23, false, true)]
    public void PrevAndNextAreDisabledAtTheEnds(int currentPage, bool prevDisabled, bool nextDisabled)
    {
        IRenderedComponent<Pagination> pager = RenderPager(totalPages: 23, currentPage);

        IReadOnlyList<IElement> buttons = pager.FindAll(".qz-pagination-controls button");
        buttons[0].HasAttribute("disabled").Should().Be(prevDisabled);
        buttons[^1].HasAttribute("disabled").Should().Be(nextDisabled);
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

    private IRenderedComponent<Pagination> RenderPager(int totalPages, int currentPage)
    {
        return context.Render<Pagination>(parameters => parameters
            .Add(x => x.TotalItems, totalPages * 25)
            .Add(x => x.PageSize, 25)
            .Add(x => x.CurrentPage, currentPage));
    }

    [Test]
    public void TheKeyBadgeShowsTheKeyTheWayQuartzWritesIt()
    {
        IRenderedComponent<KeyBadge> badge = context.Render<KeyBadge>(parameters => parameters
            .Add(x => x.GroupName, "reports")
            .Add(x => x.ItemName, "nightly"));

        badge.Find(".qz-key-badge-value").TextContent.Should().Be("reports.nightly");
        badge.Find(".qz-key-badge").GetAttribute("title").Should().Be("reports.nightly",
            "the whole key is on hover as well");
    }

    /// <summary>
    /// A listing never cuts a key short: one too long for its column wraps, first after the group's dot, which the
    /// badge marks as a break opportunity, then inside the name. Two keys that differ only at their end stay apart.
    /// </summary>
    [Test]
    public void AListingWrapsAKeyItCannotFitRatherThanCuttingIt()
    {
        IRenderedComponent<KeyBadge> badge = context.Render<KeyBadge>(parameters => parameters
            .Add(x => x.GroupName, "demo")
            .Add(x => x.ItemName, "demoBlockingJobTrigger2"));

        badge.Find(".qz-key-badge-value").InnerHtml.Should().Be("demo.<wbr>demoBlockingJobTrigger2",
            "the line breaks after the group's dot before it breaks inside the name");
        badge.Find(".qz-key-badge-value").TextContent.Should().Be("demo.demoBlockingJobTrigger2", "the break adds no text");

        string listingKey = Rule(File.ReadAllText(HistoryPageTest.DashboardStylesheet()), ".qz-entity-table td.qz-col-key .qz-key-badge-value");
        listingKey.Should().Contain("white-space: normal;").And.Contain("overflow-wrap: anywhere;",
            "the Jobs and Triggers listings wrap a key, inside its name when it must, and never end it with an ellipsis");
        listingKey.Should().Contain("text-overflow: clip;");
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
    public void TheShellResetsContentScrollOnAPathChangeAndKeepsItOnAQueryChange()
    {
        // The shell scrolls .qz-content, not the window. Blazor's own top-of-page reset only reaches the
        // window, so a path change must ask for scrollTop = 0; paging and filters must not.
        context.Navigate("/quartz/jobs");
        IRenderedComponent<DashboardLayout> layout = context.Render<DashboardLayout>(parameters => parameters
            .Add(x => x.Body, builder => builder.AddMarkupContent(0, "<p>the page</p>")));

        int resetsBeforePathChange = CountContentScrollResets();
        context.Navigate("/quartz/jobs/reports/job-1");

        layout.WaitForAssertion(() =>
            CountContentScrollResets().Should().Be(resetsBeforePathChange + 1,
                "opening a detail page from a scrolled listing must start at the top of the content panel"));

        int resetsBeforeQueryChange = CountContentScrollResets();
        context.Navigate("/quartz/jobs/reports/job-1?from=timeline");

        CountContentScrollResets().Should().Be(resetsBeforeQueryChange,
            "a query-only change is paging or a filter, and those keep the scroll offset");
    }

    /// <summary>
    /// At a phone's width the header's controls wrap and shrink rather than widening the shell, the grid
    /// column holding the page stays at the viewport's width, and a group name with no break in it breaks
    /// anywhere in its heading. The fixed-layout listings keep their key columns and scroll, and a page's own
    /// header row wraps (#4345). A browser measures this; the rules that make it so are pinned here.
    /// </summary>
    [Test]
    public void TheHeaderTheHeadingsAndTheListingsGiveWayAtAPhonesWidth()
    {
        string css = File.ReadAllText(HistoryPageTest.DashboardStylesheet());

        Rule(css, ".qz-header").Should().Contain("flex-wrap: wrap;",
            "the time zone and theme pickers move under the scheduler when one row is too wide");
        Rule(css, ".qz-header-left").Should().Contain("min-width: 0;");
        Rule(css, ".qz-header-right").Should().Contain("min-width: 0;");
        Rule(css, ".qz-header-control").Should().Contain("min-width: 0;",
            "a picker shrinks to its select's minimum width rather than holding the header open");
        Rule(css, ".qz-header-control-label").Should().Contain("white-space: nowrap;",
            "a label keeps to one line and its select is what shrinks");
        Rule(css, ".qz-scheduler-selector").Should().Contain("flex-wrap: wrap;",
            "the scheduler's status moves under its picker");
        Rule(css, ".qz-main").Should().Contain("min-width: 0;",
            "the grid column is the viewport's width, not that of the widest thing in it");
        Rule(css, ".qz-section > h2").Should().Contain("overflow-wrap: anywhere;",
            "a long unbroken group name breaks rather than widening the page");
        Rule(css, ".qz-row > h2").Should().Contain("overflow-wrap: anywhere;",
            "the Jobs page's group heading shares a row with the group's state, and breaks the same way");

        // With the column no wider than the viewport, a fixed-layout table's key columns get what its fixed
        // columns leave, which on a phone is nothing: below 1280px Jobs fixes 484px and History 584px.
        NarrowMinWidth(css, 1024, ".qz-jobs-table").Should().BeGreaterThanOrEqualTo(484 + 276,
            "the job's key and its Copy button keep their width and the table scrolls instead");
        css.Should().MatchRegex(@"@media \(max-width: 1279px\) \{\s*\.qz-jobs-table \.qz-col-details \{\s*display: none;",
            "below 1280px Details gives the key its width, since the whole row opens the job");
        css.Should().MatchRegex(@"@media \(max-width: 1439px\) \{\s*\.qz-jobs-table \.qz-col-last-success \{\s*display: none;",
            "below 1440px Last success does, so the key keeps about 18rem");
        NarrowMinWidth(css, 1024, ".qz-history-table").Should().BeGreaterThanOrEqualTo(584 + 2 * 120,
            "the job's and the trigger's keys keep their width and the table scrolls instead");

        css.Should().MatchRegex(@"@media \(max-width: 640px\) \{\s*\.qz-page-header \{[^}]*flex-wrap: wrap;",
            "on a phone a page's title, description and refresh wrap rather than pushing Refresh off the screen");
        Rule(css, ".qz-page-header").Should().NotContain("flex-wrap",
            "on a wide screen a long description's own width would wrap a row that fits");
    }

    private static int NarrowMinWidth(string css, int breakpoint, string selector)
    {
        Match rule = Regex.Match(css, @"@media \(max-width: " + breakpoint + @"px\) \{\s*" + Regex.Escape(selector) + @" \{\s*min-width: (\d+)px;");
        rule.Success.Should().BeTrue("below " + breakpoint + "px " + selector + " has a minimum width");
        return int.Parse(rule.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Rule(string css, string selector)
    {
        Match rule = Regex.Match(css, "(?m)^" + Regex.Escape(selector) + @" \{([^}]*)\}");
        rule.Success.Should().BeTrue("the stylesheet defines " + selector);
        return rule.Groups[1].Value;
    }

    private int CountContentScrollResets()
    {
        return context.JSInterop.Invocations.Count(invocation =>
            invocation.Identifier == "quartzDashboardDom.resetScrollTop");
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
    public void CollapsingTheSidebarNarrowsTheRailAndRemembersIt()
    {
        IRenderedComponent<DashboardLayout> layout = context.Render<DashboardLayout>(parameters => parameters
            .Add(x => x.Body, builder => builder.AddMarkupContent(0, "<p>the page</p>")));

        layout.Find(".qz-layout").ClassList.Should().NotContain("qz-sidebar-collapsed");
        layout.Find(".qz-sidebar-toggle").GetAttribute("aria-expanded").Should().Be("true");

        layout.Find(".qz-sidebar-toggle").Click();

        layout.WaitForAssertion(() =>
        {
            layout.Find(".qz-layout").ClassList.Should().Contain("qz-sidebar-collapsed");
            layout.Find(".qz-sidebar-toggle").GetAttribute("aria-expanded").Should().Be("false");
            context.JSInterop.VerifyInvoke("quartzDashboardPrefs.set")
                .Arguments.Should().Equal(["qz_sidebar_collapsed", "1"],
                    "the rail width has to survive the next page load");
        });
    }

    [Test]
    public void AStoredCollapsedPreferenceRestoresTheNarrowRail()
    {
        context.JSInterop.Setup<string?>("quartzDashboardPrefs.get", "qz_sidebar_collapsed").SetResult("1");

        IRenderedComponent<DashboardLayout> layout = context.Render<DashboardLayout>(parameters => parameters
            .Add(x => x.Body, builder => builder.AddMarkupContent(0, "<p>the page</p>")));

        layout.WaitForAssertion(() =>
        {
            layout.Find(".qz-layout").ClassList.Should().Contain("qz-sidebar-collapsed",
                "a remembered collapse has to come back after the preference is read");
            layout.Find(".qz-sidebar-toggle").GetAttribute("aria-expanded").Should().Be("false");
        });
    }

    [Test]
    public void ChoosingATimeZoneRefreshesEveryTimestampOnThePage()
    {
        // The fixture pins the zone to UTC, so any other installed zone is a change.
        string otherZoneId = TimeZoneInfo.GetSystemTimeZones()
            .First(zone => !string.Equals(zone.Id, TimeZoneInfo.Utc.Id, StringComparison.Ordinal))
            .Id;
        int displayChanges = 0;
        int schedulerChanges = 0;
        context.SchedulerState.OnDisplayChanged += (_, _) => displayChanges++;
        context.SchedulerState.OnSchedulerChanged += (_, _) => schedulerChanges++;
        IRenderedComponent<DashboardLayout> layout = context.Render<DashboardLayout>(parameters => parameters
            .Add(x => x.Body, builder => builder.AddMarkupContent(0, "<p>the page</p>")));

        layout.Find("#qz-timezone-select").Change(otherZoneId);

        context.SchedulerState.SelectedTimeZoneId.Should().Be(otherZoneId);
        displayChanges.Should().Be(1,
            "every page formats its timestamps in the selected zone, and none of them re-renders on its own");
        schedulerChanges.Should().Be(0,
            "only the formatting changed: a page told the scheduler changed reads everything again and starts over");
    }

    /// <summary>
    /// A page's timer asks the browser to report its tab, and stops listening when the page goes.
    /// </summary>
    /// <remarks>
    /// A listener left on the document would call into a page that is gone at every tab switch, and there would
    /// be one more for every page visited.
    /// </remarks>
    [Test]
    public async Task LeavingAPageRemovesItsTabListener()
    {
        context.JSInterop.Setup<int>("quartzDashboardVisibility.observe", _ => true).SetResult(7);
        context.Render<AutoRefresh>(parameters => parameters
            .Add(x => x.Every, TimeSpan.FromSeconds(1))
            .Add(x => x.Refresh, () => Task.CompletedTask));

        await context.DisposeComponentsAsync();

        context.JSInterop.VerifyInvoke("quartzDashboardVisibility.unobserve").Arguments.Should().ContainSingle()
            .Which.Should().Be(7, "the listener removed is the one the browser registered for this page");
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
