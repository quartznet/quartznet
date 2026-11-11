using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// Timeline page behaviour: refresh split, refused menu items, history gap, Back links, null fire times.
/// </summary>
public class TimelinePageTest
{
    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
        context.WithScheduler();
        context.Navigate("/quartz/timeline?window=1h");
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    [Test]
    public void ATriggersRefreshUpdatesBarsWithoutRereadingHeadersOrUpcomingFires()
    {
        DateTimeOffset now = context.Clock.GetUtcNow();
        GivenHistory(now.AddMinutes(-10));
        GivenTriggersOfJob("demo", "demo-job", CreateTrigger("demo", "demo-trigger", nextFire: now.AddMinutes(5)));

        IRenderedComponent<Timeline> page = context.Render<Timeline>();
        page.WaitForAssertion(() => page.Find("[data-testid=timeline-gantt]").Should().NotBeNull());

        A.CallTo(() => context.Api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .MustHaveHappened();
        A.CallTo(() => context.Api.GetUpcomingFireTimes(A<string>._, A<TriggerKeyDto>._, A<int>._, A<CancellationToken>._))
            .MustHaveHappened();
        Fake.ClearRecordedCalls(context.Api);

        // Cheap Triggers tick: bars and live fires only. Headers wait until a fire leaves executing.
        page.Find("#timeline-triggers-refresh").Change("10s");
        page.WaitForAssertion(() =>
        {
            A.CallTo(() => context.Api.QueryExecutions(A<DashboardHistoryQuery>._, A<CancellationToken>._))
                .MustHaveHappened();
            A.CallTo(() => context.Api.QueryFireInstances(A<string>._, A<DashboardFireInstanceQuery>._, A<CancellationToken>._))
                .MustHaveHappened();
            A.CallTo(() => context.Api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
                .MustNotHaveHappened();
            A.CallTo(() => context.Api.GetUpcomingFireTimes(A<string>._, A<TriggerKeyDto>._, A<int>._, A<CancellationToken>._))
                .MustNotHaveHappened();
            // Trigger row, schedule text, and actions button must all survive a Triggers-only refresh.
            page.Find("[data-testid=timeline-trigger-row]").Should().NotBeNull();
            page.Find("[data-testid=timeline-trigger-row]").TextContent.Should().Contain("every 30s");
            page.FindAll(".qz-timeline-actions-btn").Should().NotBeEmpty(
                "operator access allows mutations so the actions button must remain visible after a Triggers refresh");
        });
    }

    [Test]
    public void ATriggersRefreshClearsStateOnAFinishedManualTrigger()
    {
        DateTimeOffset now = context.Clock.GetUtcNow();
        TriggerHeaderDto scheduled = CreateTrigger("demo", "demo-trigger", nextFire: now.AddMinutes(5));
        TriggerHeaderDto manual = CreateTrigger("DEFAULT", "MT_finished", nextFire: null, state: TriggerState.Normal);
        GivenHistory(
            now.AddMinutes(-1),
            entryId: "entry-manual",
            triggerGroup: "DEFAULT",
            triggerName: "MT_finished");
        GivenTriggersOfJob("demo", "demo-job", scheduled, manual);
        // A live fire puts the manual trigger in the executing set so the next tick can detect it leaving.
        A.CallTo(() => context.Api.QueryFireInstances(A<string>._, A<DashboardFireInstanceQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<FireInstanceDto>(
                [
                    new FireInstanceDto(
                        FireInstanceId: "fire-manual",
                        TriggerKey: new TriggerKeyDto("DEFAULT", "MT_finished"),
                        JobKey: new JobKeyDto("demo", "demo-job"),
                        SchedulerInstanceId: "instance-1",
                        State: FireInstanceState.Executing,
                        FireTimeUtc: now.AddMinutes(-1),
                        ScheduledFireTimeUtc: now.AddMinutes(-1),
                        ExecutionGroup: null)
                ],
                HasMore: false,
                TotalCount: 1));

        IRenderedComponent<Timeline> page = context.Render<Timeline>();
        page.WaitForAssertion(() =>
        {
            IReadOnlyList<IElement> rows = page.FindAll("[data-testid=timeline-trigger-row]");
            rows.Count.Should().Be(2);
            IElement manualRow = rows.Single(row => row.TextContent.Contains("MT_finished", StringComparison.Ordinal));
            manualRow.QuerySelector(".qz-state-indicator")!.GetAttribute("title").Should().Be("Executing");
        });

        A.CallTo(() => context.Api.GetTriggersOfJob(
                A<string>._,
                A<JobKeyDto>.That.Matches(key => key.Group == "demo" && key.Name == "demo-job"),
                A<CancellationToken>._))
            .Returns(new List<TriggerHeaderDto> { scheduled });
        A.CallTo(() => context.Api.QueryFireInstances(A<string>._, A<DashboardFireInstanceQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<FireInstanceDto>([], HasMore: false, TotalCount: 0));
        Fake.ClearRecordedCalls(context.Api);

        page.Find("#timeline-triggers-refresh").Change("10s");

        // History bar stays as an orphan row; the state indicator must not (Run now finished).
        page.WaitForAssertion(() =>
        {
            // Leaving the executing set must re-read headers so a finished manual trigger can drop off.
            A.CallTo(() => context.Api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
                .MustHaveHappened();
            IReadOnlyList<IElement> rows = page.FindAll("[data-testid=timeline-trigger-row]");
            rows.Count.Should().Be(2);
            IElement manualRow = rows.Single(row => row.TextContent.Contains("MT_finished", StringComparison.Ordinal));
            manualRow.QuerySelectorAll(".qz-state-indicator").Should().BeEmpty();
        });
    }

    [TestCase(SchedulerAccess.Operate, true)]
    [TestCase(SchedulerAccess.View, false)]
    public void ARefusedOperationHidesItsMenuItem(SchedulerAccess role, bool actionsVisible)
    {
        // Render under the policy with the role already set: Allows refuses until LoadOperations
        // has answered, so flipping the role on an existing page can hide the button for any role.
        context.WithOperatorRole(role);

        DateTimeOffset now = context.Clock.GetUtcNow();
        GivenHistory(now.AddMinutes(-10));
        GivenTriggersOfJob(
            "demo",
            "demo-job",
            CreateTrigger("demo", "demo-trigger", nextFire: now.AddMinutes(5), state: TriggerState.Normal));

        IRenderedComponent<Timeline> page = context.Render<Timeline>();
        page.WaitForAssertion(() =>
        {
            page.Find("[data-testid=timeline-trigger-row]").Should().NotBeNull(
                "the trigger row is drawn for both Operate and View");
            if (actionsVisible)
            {
                page.FindAll(".qz-timeline-actions-btn").Should().NotBeEmpty(
                    "Operate may mutate, so the actions button is drawn");
            }
            else
            {
                page.FindAll(".qz-timeline-actions-btn").Should().BeEmpty(
                    "View may not mutate, so the actions control is not drawn");
            }
        });
    }

    [Test]
    public void HistoryUnavailableShowsTheMessage()
    {
        A.CallTo(() => context.Api.QueryExecutions(A<DashboardHistoryQuery>._, A<CancellationToken>._))
            .Throws(new NotSupportedException("history not served"));

        IRenderedComponent<Timeline> page = context.Render<Timeline>();

        page.WaitForAssertion(() =>
        {
            string text = page.Find("[data-testid=timeline-history-unavailable]").TextContent;
            string collapsed = string.Join(' ', text.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries));
            collapsed.Should().Contain("does not serve execution history");
        });
    }

    [Test]
    public void JobTriggerAndExecutionLinksCarryFromTimeline()
    {
        DateTimeOffset now = context.Clock.GetUtcNow();
        GivenHistory(now.AddMinutes(-10), entryId: "entry-1");
        GivenTriggersOfJob("demo", "demo-job", CreateTrigger("demo", "demo-trigger", nextFire: now.AddMinutes(5)));

        IRenderedComponent<Timeline> page = context.Render<Timeline>();
        page.WaitForAssertion(() => page.Find("[data-testid=timeline-bar-link]").Should().NotBeNull());

        page.WaitForAssertion(() =>
        {
            page.Find(".qz-timeline-row-name").GetAttribute("href").Should().Contain("from=timeline");
            page.Find(".qz-timeline-row-trigger-name").GetAttribute("href").Should().Contain("from=timeline");
            page.Find("[data-testid=timeline-bar-link]").GetAttribute("href").Should().Contain("from=timeline")
                .And.Contain("history/entry-1");
        });
    }

    [Test]
    public void ANullFireTimeRendersAsAnEmDash()
    {
        DateTimeOffset now = context.Clock.GetUtcNow();
        GivenHistory(now.AddMinutes(-10));
        GivenTriggersOfJob(
            "demo",
            "demo-job",
            CreateTrigger("demo", "demo-trigger", nextFire: null, previousFire: null, start: null, end: null));

        IRenderedComponent<Timeline> page = context.Render<Timeline>();
        page.WaitForAssertion(() =>
            page.Find("[data-testid=timeline-trigger-row]").TextContent.Should().Contain("—"));
    }

    [Test]
    public void NameFilterWaitsForDebounceBeforeQueryingTriggers()
    {
        DateTimeOffset now = context.Clock.GetUtcNow();
        GivenHistory(now.AddMinutes(-10));
        GivenTriggersOfJob("demo", "demo-job", CreateTrigger("demo", "demo-trigger", nextFire: now.AddMinutes(5)));

        IRenderedComponent<Timeline> page = context.Render<Timeline>();
        page.WaitForAssertion(() => page.Find("#timeline-name").Should().NotBeNull());
        Fake.ClearRecordedCalls(context.Api);

        page.Find("#timeline-name").Input("slo");

        A.CallTo(() => context.Api.QueryTriggers(
                A<string>._,
                A<DashboardTriggerQuery>.That.Matches(query => query.NameContains == "slo"),
                A<CancellationToken>._))
            .MustNotHaveHappened();

        page.WaitForAssertion(() =>
            A.CallTo(() => context.Api.QueryTriggers(
                    A<string>._,
                    A<DashboardTriggerQuery>.That.Matches(query => query.NameContains == "slo"),
                    A<CancellationToken>._))
                .MustHaveHappenedOnceExactly());
    }

    [Test]
    public void TriggerRowsRemainVisibleAfterHydrateAndTriggersRefresh()
    {
        // Circuit-safety: row.Triggers must not be cleared before the await completes, so a
        // renderer running in the gap would not see an empty list.
        DateTimeOffset now = context.Clock.GetUtcNow();
        GivenHistory(now.AddMinutes(-10));
        GivenTriggersOfJob("demo", "demo-job", CreateTrigger("demo", "demo-trigger", nextFire: now.AddMinutes(5)));

        IRenderedComponent<Timeline> page = context.Render<Timeline>();
        // After hydration the trigger row must be visible.
        page.WaitForAssertion(() =>
            page.Find("[data-testid=timeline-trigger-row]").Should().NotBeNull(
                "trigger rows must appear after hydration completes"));

        // A Triggers-only refresh must keep the rows visible.
        page.Find("#timeline-triggers-refresh").Change("10s");
        page.WaitForAssertion(() =>
            page.FindAll("[data-testid=timeline-trigger-row]").Should().NotBeEmpty(
                "trigger rows must survive a Triggers-only refresh"));
    }

    [Test]
    public void TurningTriggersRefreshOffUpdatesTheLiveBadge()
    {
        DateTimeOffset now = context.Clock.GetUtcNow();
        GivenHistory(now.AddMinutes(-10));
        GivenTriggersOfJob("demo", "demo-job", CreateTrigger("demo", "demo-trigger", nextFire: now.AddMinutes(5)));

        IRenderedComponent<Timeline> page = context.Render<Timeline>();
        page.WaitForAssertion(() => page.Find(".qz-timeline-live").TextContent.Should().Contain("5s"));

        page.Find("#timeline-triggers-refresh").Change("Off");

        page.WaitForAssertion(() => page.Find(".qz-timeline-live").TextContent.Trim().Should().Be("Live · Off"));
    }

    [Test]
    public void AStoredOneSecondTriggersRefreshShowsFiveSecondsInTheSelect()
    {
        // ParseTriggersRefreshInterval coerces "1s" to 5s; the select binds _triggersRefreshKey, which
        // ApplyRefreshPreferences must coerce too — otherwise the option list has no "1s" to show.
        DateTimeOffset now = context.Clock.GetUtcNow();
        GivenHistory(now.AddMinutes(-10));
        GivenTriggersOfJob("demo", "demo-job", CreateTrigger("demo", "demo-trigger", nextFire: now.AddMinutes(5)));
        context.JSInterop.Setup<string?>("quartzDashboardPrefs.get", "qz-timeline-triggers-refresh")
            .SetResult("1s");

        IRenderedComponent<Timeline> page = context.Render<Timeline>();

        page.WaitForAssertion(() =>
        {
            page.Find("#timeline-triggers-refresh").GetAttribute("value").Should().Be("5s",
                "a stored 1s preference must show as the 5s floor in the Triggers refresh select");
            page.Find(".qz-timeline-live").TextContent.Should().Contain("5s");
        });
    }

    private void GivenHistory(
        DateTimeOffset firedAt,
        string entryId = "entry-1",
        string triggerGroup = "demo",
        string triggerName = "demo-trigger")
    {
        DashboardHistoryEntry entry = new(
            TestData.SchedulerName,
            "instance-1",
            JobGroup: "demo",
            JobName: "demo-job",
            TriggerGroup: triggerGroup,
            TriggerName: triggerName,
            FiredAtUtc: firedAt,
            Duration: TimeSpan.FromMinutes(1),
            Succeeded: true,
            ExceptionMessage: null)
        {
            EntryId = entryId,
            Result = JobRunResult.Succeeded
        };

        A.CallTo(() => context.Api.QueryExecutions(A<DashboardHistoryQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<DashboardHistoryEntry>([entry], HasMore: false, TotalCount: 1));
    }

    private void GivenTriggersOfJob(string group, string name, params TriggerHeaderDto[] triggers)
    {
        A.CallTo(() => context.Api.GetTriggersOfJob(
                A<string>._,
                A<JobKeyDto>.That.Matches(key => key.Group == group && key.Name == name),
                A<CancellationToken>._))
            .Returns(triggers.ToList());
        A.CallTo(() => context.Api.GetUpcomingFireTimes(A<string>._, A<TriggerKeyDto>._, A<int>._, A<CancellationToken>._))
            .Returns([]);
        A.CallTo(() => context.Api.QueryTriggers(A<string>._, A<DashboardTriggerQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeaderDto>([], HasMore: false, TotalCount: 0));
    }

    private static TriggerHeaderDto CreateTrigger(
        string group,
        string name,
        DateTimeOffset? nextFire,
        DateTimeOffset? previousFire = null,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        TriggerState state = TriggerState.Normal) =>
        new(group, name, "simple", "every 30s", state, ExecutionGroup: null)
        {
            NextFireTimeUtc = nextFire,
            PreviousFireTimeUtc = previousFire,
            StartTimeUtc = start,
            EndTimeUtc = end
        };
}
