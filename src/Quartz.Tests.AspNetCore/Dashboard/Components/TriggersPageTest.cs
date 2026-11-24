using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Microsoft.AspNetCore.Components;

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

    /// <summary>
    /// Each row says when its trigger fires, in words and in its own zone, with the expression it is written in
    /// on hover (#4243).
    /// </summary>
    [Test]
    public void EachRowShowsItsScheduleInWordsWithItsExpressionOnHover()
    {
        GivenTriggers([
            TestData.Dashboard.TriggerHeaders("nightly", 1)[0] with
            {
                ScheduleDescription = "Every weekday at 07:00 (America/Chicago)",
                ScheduleExpression = "0 0 7 ? * MON-FRI"
            }
        ]);

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.FindAll("th").Select(th => th.TextContent.Trim()).Should().ContainInOrder("Trigger", "Schedule", "Next fire", "Previous fire", "State");
        IElement schedule = page.Find("td.qz-col-schedule .qz-schedule");
        schedule.TextContent.Should().Be("Every weekday at 07:00 (America/Chicago)");
        schedule.GetAttribute("title").Should().Be("0 0 7 ? * MON-FRI", "the expression the words were made from is a hover away");
    }

    /// <summary>
    /// The markup a narrow listing is laid out by: the table sits in its own scroll container, whose width the
    /// columns answer to; the columns that give way carry the classes they are hidden by; and the schedule is under
    /// the key too, shown there once its own column is hidden. The layout itself is checked in a browser.
    /// </summary>
    [Test]
    public void TheListingCarriesTheMarkupItsNarrowLayoutsAreBuiltOn()
    {
        GivenTriggers([
            TestData.Dashboard.TriggerHeaders("nightly", 1)[0] with
            {
                ScheduleDescription = "Every weekday at 07:00 (America/Chicago)",
                ScheduleExpression = "0 0 7 ? * MON-FRI"
            }
        ]);

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        IElement listing = page.Find("section.qz-section [data-testid=triggers-listing]");
        listing.ClassList.Should().Contain("qz-triggers-listing");
        listing.QuerySelectorAll("table.qz-triggers-table").Should().ContainSingle("the table is the scroll container's, not the page's");
        foreach (string column in new[] { "qz-col-schedule", "qz-col-previous-fire", "qz-col-execution-group", "qz-col-details" })
        {
            page.FindAll("th." + column).Should().ContainSingle($"the {column} header is hidden with its cells");
            page.FindAll("td." + column).Should().ContainSingle($"each row has a {column} cell");
        }

        IElement inline = page.Find("td.qz-col-key .qz-schedule-inline .qz-schedule");
        inline.TextContent.Should().Be("Every weekday at 07:00 (America/Chicago)");
        inline.GetAttribute("title").Should().Be("0 0 7 ? * MON-FRI");
    }

    /// <summary>
    /// A scheduler, HTTP API or agent older than 4.8 lists its triggers without descriptions.
    /// </summary>
    [Test]
    public void ARowWithNoDescriptionShowsADash()
    {
        GivenTriggers([TestData.Dashboard.TriggerHeaders("nightly", 1)[0] with { ScheduleSummary = null, ScheduleDescription = null }]);

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.Find("td.qz-col-schedule").TextContent.Trim().Should().Be("—");
        page.FindAll("td.qz-col-schedule .qz-schedule").Should().BeEmpty();
    }

    /// <summary>
    /// The fire times show in the dashboard's zone, each with how far it is from when the page was read: the
    /// clock reads 14:23 UTC, which is 09:23 in Chicago.
    /// </summary>
    [Test]
    public void TheFireTimesShowInTheDashboardsZoneWithHowFarOffTheyAre()
    {
        context.SchedulerState.SelectedTimeZoneId = "America/Chicago";
        DateTimeOffset now = context.Clock.GetUtcNow();
        GivenTriggers([
            TestData.Dashboard.TriggerHeaders("nightly", 1)[0] with
            {
                NextFireTimeUtc = now.AddMinutes(3),
                PreviousFireTimeUtc = now.AddHours(-2)
            },
            TestData.Dashboard.TriggerHeaders("nightly", 1, firstIndex: 2)[0] with { NextFireTimeUtc = now.AddDays(-1) }
        ]);

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.FindAll("td.qz-col-next-fire").Select(Text).Should().Equal(
            "2026-10-05 09:26:00 -05:00 in 3m",
            "2026-10-04 09:23:00 -05:00 due 1d ago");
        page.FindAll("td.qz-col-previous-fire").Select(Text).Should().Equal(
            "2026-10-05 07:23:00 -05:00 2h ago",
            "—");
    }

    [Test]
    public void ThePagerAsksForTheSliceThePageNumberNames()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 60));

        IRenderedComponent<Triggers> page = context.Render<Triggers>();
        page.TextOfAll(".qz-key-badge-value").Should().HaveCount(25, "the page size is 25");

        page.FindAll(".qz-pagination button").First(button => button.TextContent.Trim() == "2").Click();

        A.CallTo(() => context.Api.QueryTriggers(
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

        A.CallTo(() => context.Api.QueryTriggers(
                TestData.SchedulerName,
                A<DashboardTriggerQuery>.That.Matches(query => query.State == TriggerState.Error && query.Skip == 0),
                A<CancellationToken>._))
            .MustHaveHappened();
        page.FindAll("button").First(button => button.TextContent.Trim() == "Error only")
            .ClassList.Should().Contain("qz-button-primary",
                "which filter is applied has to be visible, or the listing looks like the whole truth");
    }

    /// <summary>
    /// A read that fails once the listing has been read keeps it, marked stale beside the time it was read,
    /// rather than putting the error in its place.
    /// </summary>
    [Test]
    public void AFailedReloadKeepsTheListingAndMarksItStale()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        A.CallTo(() => context.Api.ResumeTrigger(A<string>._, A<TriggerKeyDto>._, A<CancellationToken>._)).Returns(true);
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        // The resume answers; the read of the listing after it does not.
        A.CallTo(() => context.Api.QueryTriggers(A<string>._, A<DashboardTriggerQuery>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("the store is not answering"));
        page.FindAll("button").First(button => button.TextContent.Trim() == "Resume").Click();

        page.WaitForAssertion(() =>
        {
            context.Toasts.Messages.Should().ContainSingle()
                .Which.Message.Should().Be("Resumed trigger nightly.trigger-1.");
            page.TextOfAll(".qz-key-badge-value").Should().Equal(["nightly.trigger-1", "nightly.trigger-2"],
                "the listing stays until a new one arrives");
            page.FindAll(".qz-error-alert").Should().BeEmpty("a page with a listing to show is not a page that failed to load");
            page.Find("[data-testid=stale-marker]").GetAttribute("title").Should().Be(
                "The last refresh failed: the store is not answering");
        });
    }

    /// <summary>
    /// A stale listing's fire times still count from when it was read, the time the page shows beside it, not
    /// from the refresh that failed.
    /// </summary>
    [Test]
    public void AStaleListingsFireTimesCountFromWhenItWasRead()
    {
        DateTimeOffset readAt = context.Clock.GetUtcNow();
        GivenTriggers([
            TestData.Dashboard.TriggerHeaders("nightly", 1)[0] with
            {
                NextFireTimeUtc = readAt.AddMinutes(3),
                PreviousFireTimeUtc = readAt.AddHours(-2)
            }
        ]);
        A.CallTo(() => context.Api.ResumeTrigger(A<string>._, A<TriggerKeyDto>._, A<CancellationToken>._)).Returns(true);
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        context.Clock.Advance(TimeSpan.FromMinutes(10));
        A.CallTo(() => context.Api.QueryTriggers(A<string>._, A<DashboardTriggerQuery>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("the store is not answering"));
        page.FindAll("button").First(button => button.TextContent.Trim() == "Resume").Click();

        page.WaitForAssertion(() =>
        {
            page.FindAll("[data-testid=stale-marker]").Should().ContainSingle();
            page.Find("td.qz-col-next-fire .qz-fire-time-relative").TextContent.Should().Be("in 3m",
                "the row is the one read ten minutes ago, and says what it said then");
            page.Find("td.qz-col-previous-fire .qz-fire-time-relative").TextContent.Should().Be("2h ago");
        });
    }

    [Test]
    public void ChangingTheStateFilterReturnsToTheFirstPage()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 60));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();
        page.FindAll(".qz-pagination button").First(button => button.TextContent.Trim() == "2").Click();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Executing only").Click();

        // A narrowed listing has fewer pages, so the page number the reader was on means nothing.
        A.CallTo(() => context.Api.QueryTriggers(
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

    /// <summary>
    /// <em>Pause group</em> and <em>Resume group</em> act on the group in one call each, not a call per
    /// trigger, and each click is one Action Log entry naming the group (#4319).
    /// </summary>
    [Test]
    public void PauseGroupAndResumeGroupActOnTheGroupInOneCallEach()
    {
        GivenTriggers([
            .. TestData.Dashboard.TriggerHeaders("nightly", 2),
            .. TestData.Dashboard.TriggerHeaders("nightly-archive", 3)
        ]);
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Pause group").Click();
        page.ConfirmPause();

        page.WaitForAssertion(() => context.Toasts.Messages.Should().ContainSingle()
            .Which.Message.Should().Be("Paused trigger group nightly."));
        A.CallTo(() => context.Api.PauseTriggerGroupWith(A<string>._, A<string>._, A<PauseDetails?>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => context.Api.PauseTriggerGroupWith(TestData.SchedulerName, "nightly", A<PauseDetails?>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => context.Api.PauseTriggerWith(A<string>._, A<TriggerKeyDto>._, A<PauseDetails?>._, A<CancellationToken>._))
            .MustNotHaveHappened();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Resume group").Click();

        page.WaitForAssertion(() => context.Toasts.Messages[^1].Message.Should().Be("Resumed trigger group nightly."));
        A.CallTo(() => context.Api.ResumeTriggerGroup(TestData.SchedulerName, "nightly", A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => context.Api.ResumeTrigger(A<string>._, A<TriggerKeyDto>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        context.ActionLog.GetLatest(10).Select(entry => (entry.Action, entry.Target)).Should().Equal(
            [("ResumedTriggerGroup", "nightly"), ("PausedTriggerGroup", "nightly")],
            "one entry per click, newest first, with the group as its target");
    }

    [Test]
    public void UnschedulingFromTheListingSaysWhetherTheTriggerWasStillThere()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 1));
        A.CallTo(() => context.Api.UnscheduleJob(A<string>._, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(true);
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Unschedule").Click();
        page.Find(".qz-confirm-dialog button.qz-button-danger").Click();

        context.Toasts.Messages.Should().ContainSingle()
            .Which.Message.Should().Be("Unscheduled trigger nightly.trigger-1.");

        A.CallTo(() => context.Api.UnscheduleJob(A<string>._, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(false);

        page.FindAll("button").First(button => button.TextContent.Trim() == "Unschedule").Click();
        page.Find(".qz-confirm-dialog button.qz-button-danger").Click();

        context.Toasts.Messages[^1].Message.Should().Be(
            "Trigger nightly.trigger-1 was not unscheduled - it no longer exists.",
            "a listing is a snapshot, so the answer to 'was it still there' is the one thing the page "
            + "cannot work out for itself");
    }

    [Test]
    public void TheAwaitingFilterAsksForTheTriggersThatAreWaiting()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 1, TriggerState.Awaiting));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Awaiting only").Click();

        A.CallTo(() => context.Api.QueryTriggers(
                TestData.SchedulerName,
                A<DashboardTriggerQuery>.That.Matches(query => query.State == TriggerState.Awaiting && query.Skip == 0),
                A<CancellationToken>._))
            .MustHaveHappened();
        page.FindAll("button").First(button => button.TextContent.Trim() == "Awaiting only")
            .ClassList.Should().Contain("qz-button-primary");
        page.Markup.Should().NotContain("Showing Awaiting triggers only",
            "the filter has a button of its own now, and the spelled-out line is for the states that "
            + "have none");
    }

    /// <summary>
    /// A listing narrowed to Awaiting is a page of triggers none of which will ever fire on their own,
    /// so each row has to say what it is waiting for.
    /// </summary>
    [Test]
    public void AWaitingTriggerNamesTheTriggerItIsWaitingFor()
    {
        GivenTriggers([
            new TriggerHeaderDto("nightly", "reconcile", "Cron", null, TriggerState.Awaiting, null)
            {
                ContinuesAfter = new TriggerKeyDto("nightly", "import"),
                ContinuationCondition = ContinuationCondition.OnFailure | ContinuationCondition.OnCancellation
            }
        ]);

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.Markup.Should().Contain("after nightly.import",
            "a row in Awaiting with no parent on it says only that the trigger is not running");
        page.Find(".qz-continues-after").GetAttribute("title").Should().Be("Released on: Failure or cancellation",
            "the condition is the other half of the answer, spelled out rather than shown as the flags "
            + "value the enum prints");
        page.TextOfAll("td.qz-col-state").Should().Equal(["Awaiting"]);
        page.Find("td.qz-col-state .qz-state-indicator").ClassList.Should().Contain("qz-state-awaiting",
            "waiting is its own colour: the fallback blue means 'a state the dashboard does not "
            + "recognise', which is what Awaiting would have looked like");
    }

    [Test]
    public void ATriggerThatWaitsForNothingShowsNoParent()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 1));

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.FindAll(".qz-continues-after").Should().BeEmpty(
            "an ordinary trigger fires on its own schedule, and a line saying it waits for nothing would "
            + "be one more thing to read on every row of every listing");
    }

    [Test]
    public void ATriggerWithNoExecutionGroupSaysSoRatherThanShowingNothing()
    {
        GivenTriggers([new TriggerHeaderDto("nightly", "trigger-1", "Cron", "0/5 * * * * ?", TriggerState.Normal, null)]);

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.Markup.Should().Contain("qz-muted",
            "an empty cell reads as a rendering fault; an em dash reads as 'no execution group'");
    }

    /// <summary>
    /// The overview's trigger-state histogram links here, so a state named in the query string has to
    /// open the listing already narrowed to it.
    /// </summary>
    [Test]
    public void AStateInTheQueryStringOpensTheListingNarrowedToIt()
    {
        GivenTriggers([
            .. TestData.Dashboard.TriggerHeaders("nightly", 2, TriggerState.Paused),
            .. TestData.Dashboard.TriggerHeaders("reports", 3, TriggerState.Normal, firstIndex: 10)
        ]);
        context.Navigate("/quartz/triggers?state=Paused");

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        A.CallTo(() => context.Api.QueryTriggers(
                TestData.SchedulerName,
                A<DashboardTriggerQuery>.That.Matches(query => query.State == TriggerState.Paused),
                A<CancellationToken>._))
            .MustHaveHappened();
        page.TextOfAll("h2").Should().Equal(["nightly"], "only the paused triggers were asked for");
        page.Markup.Should().Contain("Showing Paused triggers only",
            "the buttons offer three of the states and the histogram links to five, so a filter none of "
            + "them can show as selected has to be spelled out");
    }

    [Test]
    public void AQueryStringNamingNoStateOpensTheUnfilteredListing()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        context.Navigate("/quartz/triggers?state=not-a-state");

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        A.CallTo(() => context.Api.QueryTriggers(
                TestData.SchedulerName,
                A<DashboardTriggerQuery>.That.Matches(query => query.State == null),
                A<CancellationToken>._))
            .MustHaveHappened();
        page.TextOfAll("td.qz-col-state").Should().HaveCount(2,
            "a query string is whatever the address bar holds, and a spelling nothing recognises is not "
            + "a reason to show an error where a listing belongs");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Filters
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public void EveryFilterIsPassedToTheQueryAndWrittenToTheAddress()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.Find("#trigger-filter-group").Change("night");
        page.Find("#trigger-filter-name").Change("trigger");
        page.Find("#trigger-filter-job-group").Change("reports");
        page.Find("#trigger-filter-job-name").Change("export");
        page.Find("#trigger-filter-calendar").Change("holidays");
        page.Find("#trigger-filter-state").Change("Paused");
        page.Find("#trigger-filter-before").Change("2026-09-27T12:30");
        page.Find(".qz-filter-apply").Click();

        DateTimeOffset before = new(2026, 9, 27, 12, 30, 0, TimeSpan.Zero);
        page.WaitForAssertion(() => A.CallTo(() => context.Api.QueryTriggers(
                TestData.SchedulerName,
                A<DashboardTriggerQuery>.That.Matches(query =>
                    query.GroupContains == "night"
                    && query.NameContains == "trigger"
                    && query.Job == new JobKeyDto("reports", "export")
                    && query.CalendarName == "holidays"
                    && query.State == TriggerState.Paused
                    && query.NextFireTimeBefore == before
                    && query.Skip == 0),
                A<CancellationToken>._))
            .MustHaveHappened());
        context.CurrentUri.Should().EndWith(
            "/quartz/triggers?group=night&name=trigger&jobGroup=reports&jobName=export&calendar=holidays&state=Paused&before=2026-09-27T12%3A30%3A00.0000000%2B00%3A00",
            "every filter is a query parameter, so a narrowed listing is a link someone can send or bookmark");
    }

    [Test]
    public void AFilteredAddressOpensTheListingNarrowedByItWithTheFieldsFilledIn()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        context.Navigate("/quartz/triggers?name=trigger-2&jobGroup=reports&jobName=export&calendar=holidays&before=2026-09-27T12%3A30%3A00.0000000%2B00%3A00");

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        A.CallTo(() => context.Api.QueryTriggers(
                TestData.SchedulerName,
                A<DashboardTriggerQuery>.That.Matches(query =>
                    query.NameContains == "trigger-2"
                    && query.Job == new JobKeyDto("reports", "export")
                    && query.CalendarName == "holidays"
                    && query.NextFireTimeBefore == new DateTimeOffset(2026, 9, 27, 12, 30, 0, TimeSpan.Zero)),
                A<CancellationToken>._))
            .MustHaveHappened();
        page.Find("#trigger-filter-name").GetAttribute("value").Should().Be("trigger-2",
            "the fields show what the listing is narrowed by, or it looks like the whole truth");
        page.Find("#trigger-filter-before").GetAttribute("value").Should().Be("2026-09-27T12:30",
            "the bound is shown on the clock the dashboard shows, which the context pins to UTC");
    }

    [Test]
    public void HalfAJobKeyIsRefusedBeforeAnythingIsAsked()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 1));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();
        Fake.ClearRecordedCalls(context.Api);

        page.Find("#trigger-filter-job-group").Change("reports");
        page.Find(".qz-filter-apply").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=trigger-filter-error]").TextContent
            .Should().Contain("give both to filter by job", "a job is a key, and half of one names no job"));
        A.CallTo(() => context.Api.QueryTriggers(A<string>._, A<DashboardTriggerQuery>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public void ClearingTheFiltersListsEverythingAgain()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        context.Navigate("/quartz/triggers?name=trigger-2");
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.Find(".qz-filter-clear").Click();

        page.WaitForAssertion(() => A.CallTo(() => context.Api.QueryTriggers(
                TestData.SchedulerName,
                A<DashboardTriggerQuery>.That.Matches(query => query.NameContains == null && query.Job == null && query.State == null),
                A<CancellationToken>._))
            .MustHaveHappened());
        context.CurrentUri.Should().EndWith("/quartz/triggers");
        page.Find("#trigger-filter-name").GetAttribute("value").Should().BeEmpty();
    }

    [Test]
    public void TheStateSelectOffersEveryStateATriggerCanBeListedIn()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 1));

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.FindAll("#trigger-filter-state option").Select(option => option.TextContent)
            .Should().Equal(["All states", "Normal", "Paused", "Complete", "Error", "Blocked", "Executing", "Awaiting"],
                "None is what a key that resolves to nothing answers, and no listed trigger is in it");
    }

    /// <summary>
    /// A target whose HTTP API predates the next-fire filter ignores it. The client proves that and
    /// refuses; the page drops that one filter, says why beside its field, and lists the rest.
    /// </summary>
    [Test]
    public void ANextFireFilterTheTargetCannotApplyIsDisabledWithTheReason()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        A.CallTo(() => context.Api.QueryTriggers(
                A<string>._, A<DashboardTriggerQuery>.That.Matches(query => query.NextFireTimeBefore != null), A<CancellationToken>._))
            .Throws(new NotSupportedException("This scheduler does not filter by next fire time."));
        context.Navigate("/quartz/triggers?name=trigger&before=2026-09-27T12%3A30%3A00.0000000%2B00%3A00");

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.WaitForAssertion(() =>
        {
            page.Find("[data-testid=trigger-filter-before-unavailable]").TextContent.Should().Contain("does not filter by next fire time");
            page.Find("#trigger-filter-before").HasAttribute("disabled").Should().BeTrue();
            page.TextOfAll("td.qz-col-state").Should().HaveCount(2, "the listing is still shown, narrowed by what the target can apply");
            page.FindAll(".qz-error-alert").Should().BeEmpty("a filter a target cannot apply is not an error page");
        });
        context.CurrentUri.Should().EndWith("/quartz/triggers?name=trigger",
            "the address stops claiming a filter the listing is not narrowed by");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Acting on a selection
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public void PausingASelectionIsOneCallAndSaysWhichRowsItDidNotReach()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 3));
        TriggerKeyDto first = new("nightly", "trigger-1");
        TriggerKeyDto second = new("nightly", "trigger-2");
        A.CallTo(() => context.Api.PauseTriggers(TestData.SchedulerName, A<IReadOnlyCollection<TriggerKeyDto>>._, A<CancellationToken>._))
            .Returns(new List<TriggerKeyDto> { first });
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        Select(page, "nightly.trigger-1");
        Select(page, "nightly.trigger-2");
        page.WaitForAssertion(() => page.Find(".qz-bulk-count").TextContent.Should().Be("2 selected"));
        page.Find(".qz-bulk-pause").Click();
        page.WaitForAssertion(() => page.Markup.Should().Contain("Pause 2 selected triggers?",
            "the prompt is asked once, for the whole batch"));
        page.ConfirmPause();

        page.WaitForAssertion(() => A.CallTo(() => context.Api.PauseTriggers(
                TestData.SchedulerName,
                A<IReadOnlyCollection<TriggerKeyDto>>.That.Matches(keys => keys.Count == 2 && keys.Contains(first) && keys.Contains(second)),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());
        page.WaitForAssertion(() =>
        {
            IElement result = page.Find("[data-testid=trigger-bulk-result]");
            result.TextContent.Should().Contain("Paused 1 of 2 selected triggers.");
            result.TextContent.Should().Contain("nightly.trigger-2",
                "the row the batch did not reach is the one somebody has to go and look at");
            result.TextContent.Should().Contain("no longer exists or was already paused");
        });
        A.CallTo(() => context.Api.PauseTriggerWith(A<string>._, A<TriggerKeyDto>._, A<PauseDetails>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        context.Toasts.Messages[^1].Message.Should().Be("Paused 1 of 2 selected triggers.");
        context.ActionLog.GetLatest(2).Should().BeEquivalentTo(
            [
                new { Action = "PauseTrigger", Target = "nightly.trigger-2", Succeeded = false },
                new { Action = "PauseTrigger", Target = "nightly.trigger-1", Succeeded = true }
            ],
            options => options.ExcludingMissingMembers(),
            "a bulk pause is recorded under each trigger's own key, where 'who paused this' looks");
        page.FindAll(".qz-bulk-count").Should().BeEmpty("the selection is spent once it has been acted on");
    }

    /// <summary>
    /// A pause that says something is one call for the whole selection, through the key-set member that
    /// records it, rather than a call per row (#3964).
    /// </summary>
    [Test]
    public void APauseWithAReasonIsOneCallForTheWholeSelection()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 3));
        TriggerKeyDto first = new("nightly", "trigger-1");
        TriggerKeyDto second = new("nightly", "trigger-2");
        A.CallTo(() => context.Api.PauseTriggersWith(
                TestData.SchedulerName, A<IReadOnlyCollection<TriggerKeyDto>>._, A<PauseDetails>._, A<CancellationToken>._))
            .ReturnsLazily((string _, IReadOnlyCollection<TriggerKeyDto> keys, PauseDetails? _, CancellationToken _) => keys.ToList());
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        Select(page, "nightly.trigger-1");
        Select(page, "nightly.trigger-2");
        page.WaitForAssertion(() => page.Find(".qz-bulk-count").TextContent.Should().Be("2 selected"));
        page.Find(".qz-bulk-pause").Click();
        page.ConfirmPause("disk full on the export host");

        page.WaitForAssertion(() => A.CallTo(() => context.Api.PauseTriggersWith(
                TestData.SchedulerName,
                A<IReadOnlyCollection<TriggerKeyDto>>.That.Matches(keys => keys.Count == 2 && keys.Contains(first) && keys.Contains(second)),
                A<PauseDetails>.That.Matches(details => details.Reason == "disk full on the export host"),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());
        // Over HTTP a call per row is a request and a store transaction per row.
        A.CallTo(() => context.Api.PauseTriggerWith(A<string>._, A<TriggerKeyDto>._, A<PauseDetails>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        page.WaitForAssertion(() => context.Toasts.Messages[^1].Message.Should().Be("Paused 2 of 2 selected triggers."));
        context.ActionLog.GetLatest(2).Should().BeEquivalentTo(
            [
                new { Action = "PauseTrigger", Target = "nightly.trigger-2", Succeeded = true, Message = "one of 2 selected; reason: disk full on the export host" },
                new { Action = "PauseTrigger", Target = "nightly.trigger-1", Succeeded = true, Message = "one of 2 selected; reason: disk full on the export host" }
            ],
            options => options.ExcludingMissingMembers(),
            "one call still records each trigger under its own key, with the reason it was paused for");
    }

    [Test]
    public void ARefusedPauseWithAReasonLogsTheReasonBesideWhatTheSchedulerSaid()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 1));
        A.CallTo(() => context.Api.PauseTriggersWith(A<string>._, A<IReadOnlyCollection<TriggerKeyDto>>._, A<PauseDetails>._, A<CancellationToken>._))
            .Throws(new SchedulerException("the store refused"));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.Find("input.qz-select-group").Change(true);
        page.Find(".qz-bulk-pause").Click();
        page.ConfirmPause("maintenance");

        page.WaitForAssertion(() => context.ActionLog.GetLatest(1).Should().ContainSingle()
            .Which.Message.Should().Be("one of 1 selected; reason: maintenance; the store refused"));
    }

    /// <summary>
    /// A data source written against 4.3 has no key-set member that records a reason. The interface's
    /// default makes the pause a key at a time through the member that does, so the reason is still kept.
    /// </summary>
    [Test]
    public void APauseWithAReasonIsMadeThroughTheMemberThatRecordsItForEachSelectedRow()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        A.CallTo(() => context.Api.PauseTrigger(A<string>._, A<TriggerKeyDto>._, A<CancellationToken>._)).Returns(true);
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.Find("input.qz-select-group").Change(true);
        page.WaitForAssertion(() => page.Find(".qz-bulk-count").TextContent.Should().Be("2 selected",
            "the group's box selects every row of that group on the page"));
        page.Find(".qz-bulk-pause").Click();
        page.ConfirmPause("disk full on the export host");

        page.WaitForAssertion(() => A.CallTo(() => context.Api.PauseTriggerWith(
                TestData.SchedulerName,
                A<TriggerKeyDto>._,
                A<PauseDetails>.That.Matches(details => details.Reason == "disk full on the export host"),
                A<CancellationToken>._))
            .MustHaveHappenedTwiceExactly());
        A.CallTo(() => context.Api.PauseTriggers(A<string>._, A<IReadOnlyCollection<TriggerKeyDto>>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        page.WaitForAssertion(() => context.Toasts.Messages[^1].Message.Should().Be("Paused 2 of 2 selected triggers."));
        page.FindAll("[data-testid=trigger-bulk-result]").Should().BeEmpty("every row was reached, so there is nothing to follow up");
    }

    /// <summary>
    /// The pause is one call and one store transaction, so a refusal is the whole selection's: every row
    /// is listed with what the scheduler said.
    /// </summary>
    [Test]
    public void ARefusedPauseListsEveryRowWithWhatTheSchedulerSaid()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        A.CallTo(() => context.Api.PauseTriggersWith(A<string>._, A<IReadOnlyCollection<TriggerKeyDto>>._, A<PauseDetails>._, A<CancellationToken>._))
            .Throws(new SchedulerException("the store refused"));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.Find("input.qz-select-group").Change(true);
        page.Find(".qz-bulk-pause").Click();
        page.ConfirmPause("maintenance");

        page.WaitForAssertion(() =>
        {
            IElement result = page.Find("[data-testid=trigger-bulk-result]");
            result.TextContent.Should().Contain("Paused 0 of 2 selected triggers.");
            result.TextContent.Should().Contain("nightly.trigger-1");
            result.TextContent.Should().Contain("nightly.trigger-2");
            result.TextContent.Should().Contain("the store refused", "a failure says what the scheduler said");
        });
        context.Toasts.Messages[^1].Message.Should().Be("Paused 0 of 2 selected triggers. 2 failed.");
    }

    [Test]
    public void ResumingASelectionIsOneCall()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2, TriggerState.Paused));
        A.CallTo(() => context.Api.ResumeTriggers(TestData.SchedulerName, A<IReadOnlyCollection<TriggerKeyDto>>._, A<CancellationToken>._))
            .ReturnsLazily((string _, IReadOnlyCollection<TriggerKeyDto> keys, CancellationToken _) => keys.ToList());
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.Find("input.qz-select-group").Change(true);
        page.Find(".qz-bulk-resume").Click();

        page.WaitForAssertion(() => A.CallTo(() => context.Api.ResumeTriggers(
                TestData.SchedulerName, A<IReadOnlyCollection<TriggerKeyDto>>.That.Matches(keys => keys.Count == 2), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());
        page.WaitForAssertion(() => context.Toasts.Messages[^1].Message.Should().Be("Resumed 2 of 2 selected triggers."));
    }

    [Test]
    public void UnschedulingASelectionAsksFirst()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        A.CallTo(() => context.Api.UnscheduleJobs(TestData.SchedulerName, A<IReadOnlyCollection<TriggerKeyDto>>._, A<CancellationToken>._))
            .ReturnsLazily((string _, IReadOnlyCollection<TriggerKeyDto> keys, CancellationToken _) => keys.ToList());
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.Find("input.qz-select-group").Change(true);
        page.Find(".qz-bulk-unschedule").Click();

        page.WaitForAssertion(() => page.Markup.Should().Contain("Unschedule 2 selected triggers?"));
        A.CallTo(() => context.Api.UnscheduleJobs(A<string>._, A<IReadOnlyCollection<TriggerKeyDto>>._, A<CancellationToken>._))
            .MustNotHaveHappened();

        page.Find(".qz-confirm-dialog button.qz-button-danger").Click();

        page.WaitForAssertion(() => A.CallTo(() => context.Api.UnscheduleJobs(
                TestData.SchedulerName, A<IReadOnlyCollection<TriggerKeyDto>>.That.Matches(keys => keys.Count == 2), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());
        page.WaitForAssertion(() => context.ActionLog.GetLatest(2).Should().OnlyContain(entry => entry.Action == "UnscheduleTrigger" && entry.Succeeded));
    }

    [Test]
    public void ABatchTheSchedulerRefusesReportsEveryRowAsNotReached()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        A.CallTo(() => context.Api.ResumeTriggers(A<string>._, A<IReadOnlyCollection<TriggerKeyDto>>._, A<CancellationToken>._))
            .Throws(new HttpClientException("Received response with status code Forbidden, error details: read-only"));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.Find("input.qz-select-group").Change(true);
        page.Find(".qz-bulk-resume").Click();

        page.WaitForAssertion(() =>
        {
            IElement result = page.Find("[data-testid=trigger-bulk-result]");
            result.TextContent.Should().Contain("Resumed 0 of 2 selected triggers.");
            result.QuerySelectorAll("li").Should().HaveCount(2, "one request, so a refusal is the whole batch's");
        });
        context.Toasts.Messages[^1].Message.Should().Be("Resumed 0 of 2 selected triggers. 2 failed.");
        context.ActionLog.GetLatest(2).Should().OnlyContain(entry => !entry.Succeeded && entry.Message!.Contains("read-only"));
    }

    /// <summary>
    /// Clear selection takes the bar it stands in away, so focus goes to the listing rather than fall to the body.
    /// </summary>
    [Test]
    public void ClearingTheSelectionMovesFocusToTheListing()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();
        string listing = ReferenceOf(page.Find("[data-testid=trigger-results]"));
        Select(page, "nightly.trigger-1");
        page.WaitForElement(".qz-bulk-clear");
        int focusedSoFar = Focused().Count;

        page.Find(".qz-bulk-clear").Click();

        page.WaitForAssertion(() => page.FindAll("[data-testid=trigger-bulk-actions]").Should().BeEmpty());
        page.WaitForAssertion(() => Focused().Should().HaveCount(focusedSoFar + 1));
        LastFocused().Should().Be(listing);
    }

    /// <summary>
    /// Dismiss removes the result it stands in, so focus goes to the listing rather than fall to the body.
    /// </summary>
    [Test]
    public void DismissingTheResultMovesFocusToTheListing()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        A.CallTo(() => context.Api.ResumeTriggers(A<string>._, A<IReadOnlyCollection<TriggerKeyDto>>._, A<CancellationToken>._))
            .Returns(new List<TriggerKeyDto>());
        IRenderedComponent<Triggers> page = context.Render<Triggers>();
        string listing = ReferenceOf(page.Find("[data-testid=trigger-results]"));
        page.Find("input.qz-select-group").Change(true);
        page.Find(".qz-bulk-resume").Click();
        page.WaitForElement("[data-testid=trigger-bulk-result]");
        int focusedSoFar = Focused().Count;

        page.Find(".qz-bulk-result-dismiss").Click();

        page.WaitForAssertion(() => page.FindAll("[data-testid=trigger-bulk-result]").Should().BeEmpty());
        page.WaitForAssertion(() => Focused().Should().HaveCount(focusedSoFar + 1));
        LastFocused().Should().Be(listing);
    }

    /// <summary>
    /// A bulk action spends the selection, which takes the bar and the button pressed with it, so focus goes to the
    /// listing as it is read again.
    /// </summary>
    [Test]
    public void ABulkActionMovesFocusToTheListingAsItsBarGoes()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2, TriggerState.Paused));
        A.CallTo(() => context.Api.ResumeTriggers(TestData.SchedulerName, A<IReadOnlyCollection<TriggerKeyDto>>._, A<CancellationToken>._))
            .ReturnsLazily((string _, IReadOnlyCollection<TriggerKeyDto> keys, CancellationToken _) => keys.ToList());
        IRenderedComponent<Triggers> page = context.Render<Triggers>();
        string listing = ReferenceOf(page.Find("[data-testid=trigger-results]"));
        page.Find("input.qz-select-group").Change(true);
        int focusedSoFar = Focused().Count;

        page.Find(".qz-bulk-resume").Click();

        page.WaitForAssertion(() => page.FindAll("[data-testid=trigger-bulk-actions]").Should().BeEmpty());
        page.WaitForAssertion(() => Focused().Should().HaveCount(focusedSoFar + 1));
        LastFocused().Should().Be(listing);
    }

    /// <summary>
    /// Each dialog is handed the control that opened it, which focus goes back to as it closes, and the listing, which
    /// focus goes to instead when that control has left the page by then. Without the control, focus goes back to
    /// whatever had it as the dialog opened: after a click that moves no focus, as Safari's on a button, that is the
    /// page's heading, and focusing the heading scrolls the page back to its top.
    /// </summary>
    [TestCase("Pause selected")]
    [TestCase("Unschedule selected")]
    [TestCase("a row's Pause")]
    [TestCase("a row's Unschedule")]
    [TestCase("Pause group")]
    public void EachDialogIsHandedTheControlThatOpenedIt(string opener)
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();
        string listing = ReferenceOf(page.Find("[data-testid=trigger-results]"));
        page.WaitForElement("input.qz-select-trigger");
        if (opener.EndsWith(" selected", StringComparison.Ordinal))
        {
            Select(page, "nightly.trigger-1");
            page.WaitForElement("[data-testid=trigger-bulk-actions]");
        }

        // Read off the control's first render, the only one that carries its reference.
        string? control = Opener(page, opener).GetAttribute("blazor:elementreference");

        Opener(page, opener).Click();

        page.WaitForAssertion(() => Invoked("quartzDashboardDialog.open").Should().ContainSingle());
        IReadOnlyList<object?> arguments = Invoked("quartzDashboardDialog.open")[0].Arguments;
        arguments.Should().HaveCount(3, "the dialog is handed the control focus goes back to, and where it goes should that have gone");
        ((ElementReference) arguments[1]!).Id.Should().Be(control);
        ((ElementReference) arguments[2]!).Id.Should().Be(listing);
    }

    /// <summary>
    /// A row's Unschedule closes its dialog before the call, so focus goes back to the row's button, and the read after
    /// it takes the row away: focus goes on to the listing rather than fall to the page's body, or stay on a button that
    /// now unschedules another trigger.
    /// </summary>
    [Test]
    public void ConfirmingARowsUnscheduleMovesFocusToTheListingOnceItsRowHasGone()
    {
        List<TriggerHeaderDto> listed = [.. TestData.Dashboard.TriggerHeaders("nightly", 3)];
        GivenTriggers(listed);
        TaskCompletionSource<bool> unscheduled = new();
        A.CallTo(() => context.Api.UnscheduleJob(TestData.SchedulerName, new TriggerKeyDto("nightly", "trigger-2"), A<CancellationToken>._))
            .Returns(new ValueTask<bool>(unscheduled.Task));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();
        string listing = ReferenceOf(page.Find("[data-testid=trigger-results]"));
        page.WaitForElement("input.qz-select-trigger[aria-label='Select nightly.trigger-2']")
            .Closest("tr")!.QuerySelectorAll("td.qz-col-actions button").First(button => button.TextContent.Trim() == "Unschedule").Click();
        page.Find(".qz-confirm-dialog button.qz-button-danger").Click();
        page.WaitForAssertion(() => page.FindAll(".qz-confirm-dialog").Should().BeEmpty("the dialog closes before the call, while the row is still listed"));
        int focusedSoFar = Focused().Count;

        listed.RemoveAt(1);
        unscheduled.SetResult(true);

        page.WaitForAssertion(() => page.FindAll("input.qz-select-trigger[aria-label='Select nightly.trigger-2']").Should().BeEmpty());
        page.WaitForAssertion(() => Focused().Should().HaveCount(focusedSoFar + 1));
        LastFocused().Should().Be(listing);
    }

    private static IElement Opener(IRenderedComponent<Triggers> page, string opener)
    {
        return opener switch
        {
            "Pause selected" => page.Find(".qz-bulk-pause"),
            "Unschedule selected" => page.Find(".qz-bulk-unschedule"),
            "a row's Pause" => page.FindAll("td.qz-col-actions button").First(button => button.TextContent.Trim() == "Pause"),
            "a row's Unschedule" => page.FindAll("td.qz-col-actions button").First(button => button.TextContent.Trim() == "Unschedule"),
            _ => page.FindAll(".qz-section > .qz-group-actions button").Single(button => button.TextContent.Trim() == "Pause group")
        };
    }

    [Test]
    public void ApplyingAFilterClearsTheSelection()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();
        Select(page, "nightly.trigger-1");
        page.WaitForElement(".qz-bulk-count");

        page.Find("#trigger-filter-name").Change("trigger-2");
        page.Find(".qz-filter-apply").Click();

        page.WaitForAssertion(() => page.FindAll(".qz-bulk-count").Should().BeEmpty(
            "a bulk action on rows the narrowed listing no longer shows would be a surprise"));
    }

    [Test]
    public void ChangingPageClearsTheSelection()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 30));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();
        Select(page, "nightly.trigger-1");
        page.WaitForElement(".qz-bulk-count");

        page.FindAll(".qz-pagination button").Single(button => button.TextContent.Trim() == "Next").Click();

        page.WaitForAssertion(() =>
        {
            page.FindAll("input.qz-select-trigger[aria-label='Select nightly.trigger-26']").Should().ContainSingle("page 2 is shown");
            page.FindAll(".qz-bulk-count").Should().BeEmpty(
                "a bulk action on rows the page no longer shows would be a surprise");
        });
    }

    [Test]
    public void ReadOnlyModeOffersNoSelection()
    {
        context.Options.ReadOnly = true;
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.FindAll("input[type=checkbox]").Should().BeEmpty("a selection is only for acting on, and read-only acts on nothing");
        page.FindAll(".qz-filter-apply").Should().ContainSingle("filtering is reading, not writing");
    }

    // ---------------------------------------------------------------------------------------------
    // Each trigger's last run (#4243)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A row says what its trigger's last run achieved, how long it took and how long ago it fired, with the fire
    /// time, the node and the summary on hover; and how many of its occurrences in a row have failed for good, linking
    /// to those failures.
    /// </summary>
    [Test]
    public void ARowShowsItsTriggersLastRunAndHowManyOccurrencesInARowFailed()
    {
        context.SchedulerState.SelectedTimeZoneId = "America/Chicago";
        DateTimeOffset now = context.Clock.GetUtcNow();
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        GivenRunStatuses(
            RunStatus("trigger-1", now.AddMinutes(-2), JobRunResult.Failed) with
            {
                LastDuration = TimeSpan.FromSeconds(1.5),
                LastSchedulerInstanceId = "node-a",
                LastSummary = "3 invoices did not reconcile",
                LastFailureMessage = "the feed is down",
                ConsecutiveFailures = 3
            },
            RunStatus("trigger-2", now.AddHours(-3), JobRunResult.Succeeded) with { LastDuration = TimeSpan.FromMilliseconds(40) });

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.FindAll("th").Select(th => th.TextContent.Trim()).Should().ContainInOrder("Previous fire", "Last run", "State");
        page.FindAll("td.qz-col-last-run .qz-trigger-last-run").Select(Text).Should().Equal(
            "Failed 1.5 s 2m ago",
            "Succeeded 40 ms 3h ago");
        page.FindAll("td.qz-col-last-run .qz-trigger-failing").Select(Text).Should().Equal("failing ×3");

        IElement summary = page.Find("td.qz-col-last-run .qz-trigger-last-run");
        summary.GetAttribute("title").Should().Be("Fired 2026-10-05 09:21:00 -05:00, on node-a: 3 invoices did not reconcile",
            "the fire time is in the dashboard's zone, as every time on the page is");

        IElement badge = page.Find("td.qz-col-last-run .qz-failing-badge");
        badge.GetAttribute("title").Should().Be("the feed is down", "the badge says what the last failure said");
        badge.GetAttribute("href").Should().EndWith("history?outcome=failed&triggerGroup=nightly&triggerName=trigger-1&result=failed",
            "the badge opens the trigger's final failures, not the trigger's own page");
        badge.GetAttribute("aria-label").Should().Be("failing ×3: the last 3 runs of nightly.trigger-1 failed. Open them in History",
            "the link's name starts with what it shows, then says whose runs and where it goes");
        page.FindAll("td.qz-col-last-run .qz-failing-badge").Should().ContainSingle("a trigger that has not failed has no badge");
    }

    /// <summary>
    /// The last run is under the state too, shown there once the listing is too narrow for its column, as the
    /// schedule is under the key.
    /// </summary>
    [Test]
    public void TheLastRunIsUnderTheStateForANarrowListing()
    {
        DateTimeOffset now = context.Clock.GetUtcNow();
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 1));
        GivenRunStatuses(RunStatus("trigger-1", now.AddMinutes(-2), JobRunResult.Failed) with { ConsecutiveFailures = 1 });

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.Find("table.qz-triggers-table").ClassList.Should().Contain("qz-with-last-run",
            "the listing's narrow steps come earlier with the column than without it");
        Text(page.Find("td.qz-col-state .qz-last-run-inline .qz-last-run-caption")).Should().Be("Last run",
            "under the state the last run is a second status, so it says which, as the column's header did");
        page.FindAll("td.qz-col-last-run .qz-last-run-caption").Should().BeEmpty("the column has its header");
        Text(page.Find("td.qz-col-state .qz-last-run-inline .qz-trigger-last-run")).Should().Be(Text(page.Find("td.qz-col-last-run .qz-trigger-last-run")));
        Text(page.Find("td.qz-col-state .qz-last-run-inline .qz-trigger-failing")).Should().Be(Text(page.Find("td.qz-col-last-run .qz-trigger-failing")));
    }

    /// <summary>
    /// A run shorter than a millisecond reads as under one, not as a fraction beside the seconds of the next row.
    /// </summary>
    [Test]
    public void ARunShorterThanAMillisecondReadsAsUnderOne()
    {
        DateTimeOffset now = context.Clock.GetUtcNow();
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        GivenRunStatuses(
            RunStatus("trigger-1", now.AddMinutes(-2), JobRunResult.Succeeded) with { LastDuration = TimeSpan.FromMilliseconds(0.043) },
            RunStatus("trigger-2", now.AddMinutes(-2), JobRunResult.Failed) with { LastDuration = TimeSpan.FromSeconds(4.07) });

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.FindAll("td.qz-col-last-run .qz-trigger-last-run").Select(Text).Should().Equal(
            "Succeeded < 1 ms 2m ago",
            "Failed 4.07 s 2m ago");
    }

    [Test]
    public void ATriggerWithNoRecordedRunShowsADashThatSaysSo()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 1));
        GivenRunStatuses();

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        IElement dash = page.Find("td.qz-col-last-run .qz-trigger-no-run");
        dash.TextContent.Trim().Should().Be("—");
        dash.GetAttribute("title").Should().Be("No run of this trigger has been recorded");
    }

    /// <summary>
    /// A data source that keeps no trigger status — a history of the application's own, a host older than 4.9 —
    /// leaves the column out, and the listing lays out as it did before it.
    /// </summary>
    [Test]
    public void ASourceThatKeepsNoTriggerStatusLeavesTheColumnOut()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.FindAll("th.qz-col-last-run").Should().BeEmpty();
        page.FindAll(".qz-last-run-inline").Should().BeEmpty();
        page.Find("table.qz-triggers-table").ClassList.Should().NotContain("qz-with-last-run");
        page.FindAll(".qz-error-alert").Should().BeEmpty("a source without the datum is not a page that failed to load");
    }

    /// <summary>
    /// The statuses are read once per load and once per refresh, for the page's triggers in one call: never one
    /// read per row.
    /// </summary>
    [Test]
    public void TheStatusesAreReadOncePerLoadAndPerRefreshForThePagesTriggers()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 3));
        List<TriggerRunStatusQuery> reads = GivenRunStatuses();

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.WaitForAssertion(() => reads.Should().ContainSingle());
        reads[0].Triggers.Should().BeEquivalentTo(
            [new TriggerKey("trigger-1", "nightly"), new TriggerKey("trigger-2", "nightly"), new TriggerKey("trigger-3", "nightly")],
            "the one read names every trigger on the page");
        reads[0].SchedulerName.Should().Be(context.SchedulerState.ActiveSchedulerName);

        page.Find("[data-testid=refresh]").Click();

        page.WaitForAssertion(() => reads.Should().HaveCount(2, "a refresh reads the page's statuses once more, and only once"));
    }

    /// <summary>
    /// A read of the statuses that fails, once the listing has been read, marks the listing stale as a failed read of
    /// the listing does, and keeps what was shown.
    /// </summary>
    [Test]
    public void AStatusReadThatFailsOnARefreshMarksTheListingStale()
    {
        DateTimeOffset now = context.Clock.GetUtcNow();
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 1));
        GivenRunStatuses(RunStatus("trigger-1", now.AddMinutes(-2), JobRunResult.Succeeded));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        A.CallTo(() => context.Api.QueryTriggerRunStatuses(A<TriggerRunStatusQuery>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("the history is not answering"));
        page.Find("[data-testid=refresh]").Click();

        page.WaitForAssertion(() =>
        {
            page.Find("[data-testid=stale-marker]").GetAttribute("title").Should().Be(
                "The last refresh failed: the history is not answering");
            page.FindAll("td.qz-col-last-run .qz-trigger-last-run").Should().ContainSingle("the statuses read before stay on screen");
        });
    }

    private List<TriggerRunStatusQuery> GivenRunStatuses(params TriggerRunStatus[] statuses)
    {
        List<TriggerRunStatusQuery> reads = [];
        A.CallTo(() => context.Api.QueryTriggerRunStatuses(A<TriggerRunStatusQuery>._, A<CancellationToken>._))
            .ReturnsLazily((TriggerRunStatusQuery query, CancellationToken _) =>
            {
                reads.Add(query);
                List<TriggerRunStatus> named = statuses.Where(status => query.Triggers is null || query.Triggers.Contains(status.Trigger)).ToList();
                return new ValueTask<PagedResult<TriggerRunStatus>>(new PagedResult<TriggerRunStatus>(named, HasMore: false));
            });

        return reads;
    }

    private TriggerRunStatus RunStatus(string triggerName, DateTimeOffset firedAt, JobRunResult result) =>
        new(context.SchedulerState.ActiveSchedulerName!, new TriggerKey(triggerName, "nightly"), firedAt, result);

    private static void Select(IRenderedComponent<Triggers> page, string key)
    {
        page.Find("input.qz-select-trigger[aria-label='Select " + key + "']").Change(true);
    }

    /// <summary>The ids of the elements focus was moved to, in order.</summary>
    private List<string> Focused()
    {
        return context.JSInterop.Invocations
            .Where(invocation => invocation.Identifier == "Blazor._internal.domWrapper.focus")
            .Select(invocation => ((ElementReference) invocation.Arguments[0]!).Id)
            .ToList();
    }

    private string LastFocused() => Focused().Should().NotBeEmpty().And.Subject.Last();

    /// <summary>The calls of one JavaScript function, in order.</summary>
    private List<JSRuntimeInvocation> Invoked(string identifier)
    {
        return context.JSInterop.Invocations.Where(invocation => invocation.Identifier == identifier).ToList();
    }

    /// <summary>
    /// The id an element's <c>@ref</c> is focused by, read off its first render: a later render of the element does not
    /// carry it again.
    /// </summary>
    private static string ReferenceOf(IElement element)
    {
        string? id = element.GetAttribute("blazor:elementreference");
        id.Should().NotBeNullOrEmpty();
        return id!;
    }

    /// <summary>
    /// A cell's text with its whitespace collapsed, as a reader sees it.
    /// </summary>
    private static string Text(IElement cell)
    {
        return string.Join(' ', cell.TextContent.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries));
    }

    private void GivenTriggers(IReadOnlyList<TriggerHeaderDto> triggers)
    {
        A.CallTo(() => context.Api.QueryTriggers(A<string>._, A<DashboardTriggerQuery>._, A<CancellationToken>._))
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
