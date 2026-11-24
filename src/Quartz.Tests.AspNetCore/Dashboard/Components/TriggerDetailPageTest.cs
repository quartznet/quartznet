using Bunit;

using FakeItEasy;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Impl.Triggers;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The Trigger Detail page's one decision — whether this trigger can be rescheduled from the dashboard —
/// and the payload it posts when it can.
/// </summary>
/// <remarks>
/// Rescheduling replaces the trigger, so a payload assembled from the fields on screen quietly drops
/// whatever the page does not show. The page therefore refuses any trigger it cannot rebuild faithfully,
/// which is the behaviour these tests pin.
/// </remarks>
public class TriggerDetailPageTest
{
    private const string TriggerGroup = "CronTriggerGroup";
    private const string TriggerName = "CronTriggerKey";

    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
        context.WithScheduler();
        A.CallTo(() => context.Api.GetTriggerState(TestData.SchedulerName, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(TriggerState.Normal);
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    [Test]
    public void AVisitFromTheTimelineReturnsToThatTimeline()
    {
        context.Navigate("/quartz/triggers/CronTriggerGroup/CronTriggerKey?from=timeline&window=1h&group=housekeeping&pan=15");

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() =>
        {
            AngleSharp.Dom.IElement link = page.Find("[data-testid=detail-back]");
            link.TextContent.Should().Be("Back to timeline");
            link.GetAttribute("href").Should().EndWith("timeline?window=1h&group=housekeeping&pan=15",
                "the timeline's window, group and pan travel with the link so Back restores the view");
        });
    }

    /// <summary>
    /// The page says the schedule in words, as the listing does, and keeps the expression beside it (#4243).
    /// </summary>
    [Test]
    public void TheScheduleIsInWordsWithTheExpressionBesideIt()
    {
        GivenTrigger(TriggerBuilder.Create()
            .WithIdentity(TriggerName, TriggerGroup)
            .ForJob("CronJobKey", "CronJobGroup")
            .WithCronSchedule("0 0 7 ? * MON-FRI", x => x.InTimeZone(TimeZoneInfo.Utc))
            .Build());

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() =>
        {
            page.Find("[data-testid=trigger-schedule] .qz-schedule-description").TextContent.Should().Be("Every weekday at 07:00 (UTC)");
            page.Find("[data-testid=trigger-schedule] .qz-schedule-expression").TextContent.Should().Be("0 0 7 ? * MON-FRI");
        });
    }

    /// <summary>
    /// A shape the describer shows as written is not written twice.
    /// </summary>
    [Test]
    public void AnExpressionWithNoWordsOfItsOwnIsShownOnce()
    {
        GivenTrigger(TriggerBuilder.Create()
            .WithIdentity(TriggerName, TriggerGroup)
            .ForJob("CronJobKey", "CronJobGroup")
            .WithCronSchedule("0 0-5 9-17 * * ?", x => x.InTimeZone(TimeZoneInfo.Utc))
            .Build());

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() =>
        {
            page.Find("[data-testid=trigger-schedule] .qz-schedule-description").TextContent.Should().Be("0 0-5 9-17 * * ? (UTC)");
            page.FindAll("[data-testid=trigger-schedule] .qz-schedule-expression").Should().BeEmpty();
        });
    }

    [Test]
    public void ACronTriggerOffersItsExpressionForEditing()
    {
        GivenTrigger(CronTrigger("0/25 * * * * ?"));

        IRenderedComponent<TriggerDetail> page = Render();

        page.Find("#trigger-cron-expression").GetAttribute("value").Should().Be("0/25 * * * * ?",
            "the editor starts from the schedule the trigger is running with");
        page.Markup.Should().NotContain("Upcoming cron fires",
            "the preview is of an edit, and until there is one the saved schedule's fires are Upcoming fires");
    }

    [Test]
    public void SavingACronScheduleClonesTheTriggerAndClearsItsNextFireTime()
    {
        GivenTrigger(CronTrigger("0/25 * * * * ?"));
        IRenderedComponent<TriggerDetail> page = Render();

        page.Find("#trigger-cron-expression").Change("0 0 12 * * ?");
        page.Find("button.qz-button-primary").Click();

        A.CallTo(() => context.Api.RescheduleJob(
                TestData.SchedulerName,
                new TriggerKeyDto(TriggerGroup, TriggerName),
                A<RescheduleRequest>.That.Matches(request =>
                    ((ICronTrigger) request.NewTrigger).CronExpressionString == "0 0 12 * * ?"
                    && request.NewTrigger.Key.Name == TriggerName
                    && request.NewTrigger.CalendarName == "SomeCalendar"
                    && request.NewTrigger.NextFireTimeUtc == null),
                A<CancellationToken>._))
            .MustHaveHappened();
    }

    [Test]
    public void ATriggerTheDashboardCannotRebuildIsRefusedRatherThanGuessedAt()
    {
        // A cron trigger of a type the page cannot clone: it has a cron expression to show, so the
        // editor is offered, but posting a CronTriggerImpl back would replace it with a different
        // trigger than the one on screen.
        ICronTrigger foreignCronTrigger = A.Fake<ICronTrigger>();
        A.CallTo(() => foreignCronTrigger.CronExpressionString).Returns("0/25 * * * * ?");
        A.CallTo(() => foreignCronTrigger.Key).Returns(new TriggerKey(TriggerName, TriggerGroup));
        GivenTrigger(foreignCronTrigger);

        IRenderedComponent<TriggerDetail> page = Render();
        page.Find("#trigger-cron-expression").Change("0 0 12 * * ?");
        page.Find("button.qz-button-primary").Click();

        A.CallTo(() => context.Api.RescheduleJob(A<string>._, A<TriggerKeyDto>._, A<RescheduleRequest>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        context.Toasts.Messages.Should().ContainSingle()
            .Which.Message.Should().Be("This trigger cannot be rescheduled from the dashboard.",
                "refusing out loud beats rescheduling something the reader did not ask for");
    }

    [Test]
    public void AnEmptyExpressionIsRefusedBeforeAnythingIsPosted()
    {
        GivenTrigger(CronTrigger("0/25 * * * * ?"));
        IRenderedComponent<TriggerDetail> page = Render();

        page.Find("#trigger-cron-expression").Change("   ");
        page.Find("button.qz-button-primary").Click();

        A.CallTo(() => context.Api.RescheduleJob(A<string>._, A<TriggerKeyDto>._, A<RescheduleRequest>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        context.Toasts.Messages.Should().ContainSingle()
            .Which.Message.Should().Be("Cron expression is required.");
    }

    [Test]
    public void TheHistoryLinkNarrowsToThisTriggerExactly()
    {
        GivenTrigger(CronTrigger("0/25 * * * * ?"));

        IRenderedComponent<TriggerDetail> page = Render();

        page.FindAll("a").Single(link => link.TextContent.Contains("View execution history", StringComparison.Ordinal))
            .GetAttribute("href").Should().EndWith($"history?triggerGroup={TriggerGroup}&triggerName={TriggerName}",
                "a text filter would also list a trigger whose name merely contains this one's");
    }

    [Test]
    public void ThePageShowsTheTriggersOverlapPolicy()
    {
        GivenTrigger(TriggerBuilder.Create()
            .WithIdentity(TriggerName, TriggerGroup)
            .ForJob("CronJobKey", "CronJobGroup")
            .WithCronSchedule("0/25 * * * * ?")
            .WithOverlapPolicy(OverlapPolicy.BufferOne)
            .Build());

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => page.Markup.Should().Contain("<th>Overlap Policy</th><td>BufferOne</td>",
            "what a trigger does when its last firing is still running is part of what it is"));
    }

    [Test]
    public void ThePageShowsTheTriggersMisfireThresholdOrTheStores()
    {
        GivenTrigger(TriggerBuilder.Create()
            .WithIdentity(TriggerName, TriggerGroup)
            .ForJob("CronJobKey", "CronJobGroup")
            .WithCronSchedule("0/25 * * * * ?")
            .WithMisfireThreshold(TimeSpan.FromMinutes(5))
            .Build());

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => page.Markup.Should().Contain("<th>Misfire Threshold</th><td>00:05:00</td>",
            "how late a trigger may fire before it misfires is part of what it is"));
    }

    [Test]
    public void ThePageSaysASimpleTriggerMeasuresFromCompletion()
    {
        GivenTrigger(TriggerBuilder.Create()
            .WithIdentity(TriggerName, TriggerGroup)
            .ForJob("CronJobKey", "CronJobGroup")
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(5)).RepeatForever().MeasureIntervalFromCompletion())
            .Build());

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => page.Markup.Should().Contain("<th>Interval</th><td>Measured from completion</td>",
            "whether the next run waits for this one to end is part of what the trigger is"));
        page.Markup.Should().Contain("5 minutes after each run completes", "and the schedule in words says so too");
    }

    [Test]
    public void ATriggerWithNoCronExpressionIsNotOfferedACronEditor()
    {
        GivenTrigger(TestData.SimpleTrigger);

        IRenderedComponent<TriggerDetail> page = Render();

        page.Markup.Should().NotContain("Edit cron schedule",
            "there is no cron expression to edit, and a simple trigger's interval is not one");
        page.Markup.Should().Contain("Every 10377059999 milliseconds, 1001 times",
            "the schedule is said in the trigger's own terms instead: an interval of 120 days and change, to the millisecond");
    }

    /// <summary>
    /// A trigger its scheduler's start-up declares is stored again at the next start, over whatever an editor
    /// saves, and each editor says so (#4412).
    /// </summary>
    [Test]
    public void TheEditorsOfADeclaredTriggerSayTheChangeLastsUntilTheNextStart()
    {
        GivenTrigger(CronTrigger("0/25 * * * * ?"));
        GivenDeclaredAtStartup(true);

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => page.Find("[data-testid=declared-at-startup-note]").TextContent.Should().Be(
            "Declared at start-up: this change lasts until the next start.",
            "the cron editor saves a reschedule the next start undoes"));

        page.Find("button.qz-trigger-edit").Click();

        page.WaitForAssertion(() => page.FindAll("[data-testid=trigger-editor] [data-testid=declared-at-startup-note]").Should().ContainSingle(
            "Edit details is undone the same way"));
    }

    [TestCase(false)]
    [TestCase(null)]
    public void AnEditorSaysNothingOfATriggerTheNextStartDoesNotStoreAgain(bool? declared)
    {
        GivenTrigger(CronTrigger("0/25 * * * * ?"));
        GivenDeclaredAtStartup(declared);

        IRenderedComponent<TriggerDetail> page = Render();
        page.Find("button.qz-trigger-edit").Click();

        page.WaitForAssertion(() =>
        {
            page.FindAll("[data-testid=trigger-editor]").Should().ContainSingle();
            page.FindAll("[data-testid=declared-at-startup-note]").Should().BeEmpty(
                declared is null ? "a source that cannot say warns of nothing" : "a trigger added at run time keeps an edit");
        });
    }

    [Test]
    public void ADeclarationThatCannotBeReadCostsTheWarningAndNotThePage()
    {
        GivenTrigger(CronTrigger("0/25 * * * * ?"));
        A.CallTo(() => context.Api.IsTriggerDeclaredAtStartup(TestData.SchedulerName, A<TriggerKeyDto>._, A<CancellationToken>._))
            .ThrowsAsync(new HttpRequestException("the host went away"));

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() =>
        {
            page.Find("#trigger-cron-expression").GetAttribute("value").Should().Be("0/25 * * * * ?");
            page.FindAll("[data-testid=declared-at-startup-note]").Should().BeEmpty();
        });
    }

    [Test]
    public void ReadOnlyModeHidesEveryMutatingAction()
    {
        context.Options.ReadOnly = true;
        GivenTrigger(CronTrigger("0/25 * * * * ?"));
        GivenDeclaredAtStartup(true);

        IRenderedComponent<TriggerDetail> page = Render();

        // A visitor who may edit nothing needs no warning about an edit.
        A.CallTo(() => context.Api.IsTriggerDeclaredAtStartup(A<string>._, A<TriggerKeyDto>._, A<CancellationToken>._))
            .MustNotHaveHappened();

        page.HasButton("Pause").Should().BeFalse();
        page.HasButton("Resume").Should().BeFalse();
        page.HasButton("Unschedule").Should().BeFalse();
        page.Markup.Should().NotContain("Edit cron schedule",
            "a read-only dashboard shows the schedule but does not offer to change it");
        page.Markup.Should().Contain("Upcoming fires",
            "reading the schedule is exactly what a read-only dashboard is for");
        page.Markup.Should().NotContain("Upcoming cron fires", "with no editor there is no edit to preview");
    }

    [Test]
    public void UnschedulingAsksFirstAndThenReturnsToTheListing()
    {
        GivenTrigger(CronTrigger("0/25 * * * * ?"));
        A.CallTo(() => context.Api.UnscheduleJob(A<string>._, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(true);
        IRenderedComponent<TriggerDetail> page = Render();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Unschedule").Click();
        page.Markup.Should().Contain("Unschedule CronTriggerGroup.CronTriggerKey?",
            "an irreversible action names what it is about to remove");

        A.CallTo(() => context.Api.UnscheduleJob(A<string>._, A<TriggerKeyDto>._, A<CancellationToken>._))
            .MustNotHaveHappened();

        page.Find(".qz-confirm-dialog button.qz-button-danger").Click();

        A.CallTo(() => context.Api.UnscheduleJob(
                TestData.SchedulerName,
                new TriggerKeyDto(TriggerGroup, TriggerName),
                A<CancellationToken>._))
            .MustHaveHappened();
        context.Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/quartz/triggers",
            "the trigger the page was showing no longer exists, so staying on it would show a not-found page");
        context.ActionLog.GetLatest(1).Should().ContainSingle()
            .Which.Action.Should().Be("UnscheduleTrigger",
                "an action taken from the dashboard is recorded whether or not anyone was watching");
    }

    [Test]
    public void UnschedulingATriggerSomebodyElseAlreadyRemovedSaysSoAndStays()
    {
        GivenTrigger(CronTrigger("0/25 * * * * ?"));
        A.CallTo(() => context.Api.UnscheduleJob(A<string>._, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(false);
        IRenderedComponent<TriggerDetail> page = Render();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Unschedule").Click();
        page.Find(".qz-confirm-dialog button.qz-button-danger").Click();

        context.Toasts.Messages.Should().ContainSingle()
            .Which.Message.Should().Be(
                "Trigger CronTriggerGroup.CronTriggerKey was not unscheduled - it no longer exists.",
                "the scheduler answers whether the trigger was there, and a page that reported success "
                + "either way would tell an operator a cluster peer's removal was their own");
        context.Services.GetRequiredService<NavigationManager>().Uri.Should().NotEndWith("/quartz/triggers",
            "nothing was removed, so there is nothing to return from");
        context.ActionLog.GetLatest(1).Should().ContainSingle()
            .Which.Succeeded.Should().BeFalse(
                "the action log records what happened, and nothing happened");
    }

    /// <summary>
    /// A waiting trigger's two rows: the parent, as a link to the page that shows it, and the outcomes
    /// that release the wait.
    /// </summary>
    [Test]
    public void AWaitingTriggerLinksToTheTriggerItIsWaitingFor()
    {
        GivenTrigger(ContinuationTrigger(ContinuationCondition.OnFailure | ContinuationCondition.OnCancellation));
        A.CallTo(() => context.Api.GetTriggerState(TestData.SchedulerName, new TriggerKeyDto("nightly", "import"), A<CancellationToken>._))
            .Returns(TriggerState.Normal);

        IRenderedComponent<TriggerDetail> page = Render();

        page.Markup.Should().Contain("Continues after");
        page.Find("td a.qz-details-link[href*='triggers/nightly/import']").TextContent.Should().Be("nightly.import",
            "the parent is a trigger with a page of its own, and 'what is this waiting for' is a "
            + "question whose answer is one click away");
        page.Markup.Should().Contain("Failure or cancellation",
            "the condition is spelled out rather than shown as the flags value the enum prints");
    }

    /// <summary>
    /// A parent somebody removed leaves the continuation naming a trigger that is not there, and parked
    /// in <see cref="TriggerState.Error" />. The page says both rather than linking to a page that would
    /// report the trigger as not found.
    /// </summary>
    [Test]
    public void AWaitingTriggerWhoseParentIsGoneNamesItWithoutLinkingToIt()
    {
        GivenTrigger(ContinuationTrigger(ContinuationCondition.OnSuccess));
        A.CallTo(() => context.Api.GetTriggerState(TestData.SchedulerName, new TriggerKeyDto("nightly", "import"), A<CancellationToken>._))
            .Returns(TriggerState.None);

        IRenderedComponent<TriggerDetail> page = Render();

        page.FindAll("td a.qz-details-link[href*='triggers/nightly/import']").Should().BeEmpty();
        page.Markup.Should().Contain("nightly.import");
        page.Markup.Should().Contain("no longer scheduled",
            "a key that resolves to nothing is what an operator has to know, and a dead link says it "
            + "only to whoever clicks it");
    }

    [Test]
    public void AnOrdinaryTriggerHasNoContinuationRows()
    {
        GivenTrigger(CronTrigger("0/25 * * * * ?"));

        IRenderedComponent<TriggerDetail> page = Render();

        page.Markup.Should().NotContain("Continues after",
            "a trigger that waits for nothing has nothing to say about waiting");
    }

    /// <summary>
    /// "Why isn't it firing?" (#4411): a blocked trigger names the firing that holds its job, the node and
    /// since when, and <em>Interrupt</em> asks that firing to stop.
    /// </summary>
    [Test]
    public void ABlockedTriggerNamesTheFiringThatHoldsItsJobAndInterruptsIt()
    {
        GivenTrigger(Scheduled(CronTrigger("0 0 12 * * ?")));
        A.CallTo(() => context.Api.GetTriggerState(TestData.SchedulerName, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(TriggerState.Blocked);
        A.CallTo(() => context.Api.QueryFireInstances(TestData.SchedulerName, A<DashboardFireInstanceQuery>.That.Matches(query => query.Job != null), A<CancellationToken>._))
            .Returns(new PagedResult<FireInstanceDto>([HoldingFiring()], HasMore: false, TotalCount: 1));
        A.CallTo(() => context.Api.InterruptFireInstance(TestData.SchedulerName, "fire-7", A<CancellationToken>._)).Returns(true);

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => page.Find("tr[data-check=\"Its job's firings\"]").TextContent.Should()
            .Contain("One running: on node node-b since 2026-10-05 13:00:00"));
        page.Find("[data-testid=trigger-diagnosis-summary]").TextContent.Trim().Should().Be("2 findings may stop this trigger firing.",
            "the trigger is blocked, and the firing is what blocks it");
        page.FindAll("[data-testid=trigger-diagnosis-findings] tr").Select(row => row.GetAttribute("data-check")).Should()
            .Equal(["Trigger", "Its job's firings"], "the findings open the section, ahead of the checks that passed");
        page.FindAll("[data-testid=trigger-diagnosis-checks]").Should().BeEmpty("the checks that passed are behind the toggle");
        page.Find("[data-testid=trigger-diagnosis-toggle]").TextContent.Should().Be("Show the 7 other checks");

        page.Find("[data-testid=trigger-diagnosis-toggle]").Click();

        page.WaitForAssertion(() => page.FindAll("[data-testid=trigger-diagnosis-checks] tr").Should().HaveCount(7));
        page.Find("[data-testid=trigger-diagnosis-toggle]").TextContent.Should().Be("Hide the other checks");
        A.CallTo(() => context.Api.QueryFireInstances(TestData.SchedulerName, A<DashboardFireInstanceQuery>.That.Matches(query =>
                query.Job == new JobKeyDto("CronJobGroup", "CronJobKey")), A<CancellationToken>._))
            .MustHaveHappened();
        page.Find(".qz-diagnosis-interrupt").GetAttribute("aria-label").Should().Be("Interrupt firing fire-7",
            "a screen reader says which firing the button stops");

        page.Find(".qz-diagnosis-interrupt").Click();

        page.WaitForAssertion(() => A.CallTo(() => context.Api.InterruptFireInstance(TestData.SchedulerName, "fire-7", A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());
    }

    /// <summary>
    /// A serial job's trigger whose own firing runs reads Executing, never Blocked (#1416), and is held all the
    /// same once its next fire time passes: the page names the firing that holds it, with Interrupt, and Interrupt
    /// is acknowledged where the button was as well as in a toast.
    /// </summary>
    [Test]
    public void AnExecutingTriggerHeldByItsJobsRunningFiringSaysSoAndInterruptsIt()
    {
        ITrigger trigger = Scheduled(CronTrigger("0 0 12 * * ?"));
        ((Quartz.Extensibility.IMutableTrigger) trigger).NextFireTimeUtc = new DateTimeOffset(2026, 10, 5, 14, 0, 0, TimeSpan.Zero);
        GivenTrigger(trigger);
        A.CallTo(() => context.Api.GetTriggerState(TestData.SchedulerName, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(TriggerState.Executing);
        A.CallTo(() => context.Api.QueryFireInstances(TestData.SchedulerName, A<DashboardFireInstanceQuery>.That.Matches(query => query.Job != null), A<CancellationToken>._))
            .Returns(new PagedResult<FireInstanceDto>([HoldingFiring()], HasMore: false, TotalCount: 1));
        A.CallTo(() => context.Api.GetJobDetail(TestData.SchedulerName, new JobKeyDto("CronJobGroup", "CronJobKey"), A<CancellationToken>._))
            .Returns(new JobDetailDto("CronJobKey", "CronJobGroup", "Example.SerialJob", null, Durable: true, RequestsRecovery: false,
                ConcurrentExecutionDisallowed: true, PersistJobDataAfterExecution: null, new JobDataMap()));
        A.CallTo(() => context.Api.InterruptFireInstance(TestData.SchedulerName, "fire-7", A<CancellationToken>._)).Returns(true);

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => page.Find("tr[data-check=\"Its job's firings\"]").TextContent.Should()
            .Contain("Held by its job's running firing fire-7 on node node-b since 2026-10-05 13:00:00"));
        page.Find("[data-testid=trigger-diagnosis-summary]").TextContent.Trim().Should().Be("One finding may stop this trigger firing.");

        page.Find(".qz-diagnosis-interrupt").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=diagnosis-interrupt-requested]").TextContent.Should().Be("Interrupt requested"));
        page.FindAll(".qz-diagnosis-interrupt").Should().BeEmpty("the request was accepted, and the button would only send it again");
        A.CallTo(() => context.Api.InterruptFireInstance(TestData.SchedulerName, "fire-7", A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        context.Toasts.Messages.Should().ContainSingle().Which.Message.Should().StartWith("Interrupt requested for firing fire-7");
        A.CallTo(() => context.Api.GetTrigger(TestData.SchedulerName, A<TriggerKeyDto>._, A<CancellationToken>._)).MustHaveHappenedTwiceExactly();
    }

    [Test]
    public void ATriggerWithNothingWrongSaysSo()
    {
        GivenTrigger(Scheduled(CronTrigger("0 0 12 * * ?")));

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => page.Find("[data-testid=trigger-diagnosis-summary]").TextContent.Trim()
            .Should().Be("Nothing found that stops this trigger firing."));

        // One line, and every check behind the toggle beside it.
        page.FindAll("[data-testid=trigger-diagnosis-checks]").Should().BeEmpty("with nothing found the section is one line until it is opened");
        page.FindAll("[data-testid=trigger-diagnosis-findings]").Should().BeEmpty();
        page.Find("[data-testid=trigger-diagnosis-toggle]").TextContent.Should().Be("Show the 9 checks");
        page.Find("[data-testid=trigger-diagnosis-toggle]").GetAttribute("aria-expanded").Should().Be("false");
    }

    /// <summary>
    /// The toggle says what it will do: "Show the 9 checks" while they are folded away, "Hide the checks" once
    /// they show, and it says which with aria-expanded.
    /// </summary>
    [Test]
    public void TheDiagnosisToggleSaysWhetherItWillShowOrHide()
    {
        GivenTrigger(Scheduled(CronTrigger("0 0 12 * * ?")));
        IRenderedComponent<TriggerDetail> page = Render();
        page.WaitForAssertion(() => page.Find("[data-testid=trigger-diagnosis-toggle]"));

        page.Find("[data-testid=trigger-diagnosis-toggle]").Click();

        page.WaitForAssertion(() => page.FindAll("[data-testid=trigger-diagnosis-checks] tbody tr").Should().HaveCount(9));
        AngleSharp.Dom.IElement toggle = page.Find("[data-testid=trigger-diagnosis-toggle]");
        toggle.TextContent.Should().Be("Hide the checks");
        toggle.GetAttribute("aria-expanded").Should().Be("true");
        toggle.GetAttribute("aria-controls").Should().Be(page.Find("[data-testid=trigger-diagnosis-checks]").Id);

        toggle.Click();

        page.WaitForAssertion(() => page.FindAll("[data-testid=trigger-diagnosis-checks]").Should().BeEmpty());
        page.Find("[data-testid=trigger-diagnosis-toggle]").TextContent.Should().Be("Show the 9 checks");
    }

    /// <summary>
    /// A paused trigger's upcoming fires are its schedule's times, which it does not fire: the page says so above
    /// the list.
    /// </summary>
    [TestCase(TriggerState.Paused, true)]
    [TestCase(TriggerState.Normal, false)]
    public void UpcomingFiresSayWhenThePausedTriggerFiresNone(TriggerState state, bool said)
    {
        GivenTrigger(Scheduled(CronTrigger("0 0 12 * * ?")));
        A.CallTo(() => context.Api.GetTriggerState(TestData.SchedulerName, A<TriggerKeyDto>._, A<CancellationToken>._)).Returns(state);

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => page.Find("[data-testid=trigger-diagnosis]"));
        page.FindAll("[data-testid=upcoming-fires-paused]").Should().HaveCount(said ? 1 : 0);
        if (said)
        {
            page.Find("[data-testid=upcoming-fires-paused]").TextContent.Should().Contain("fires none of them until it is resumed");
        }
    }

    /// <summary>
    /// A trigger group's pause shows who paused it, when and why, as the trigger's own and the job group's do.
    /// </summary>
    [Test]
    public void APausedTriggerGroupsReasonShowsInItsFinding()
    {
        GivenTrigger(Scheduled(CronTrigger("0 0 12 * * ?")));
        A.CallTo(() => context.Api.GetTriggerState(TestData.SchedulerName, A<TriggerKeyDto>._, A<CancellationToken>._)).Returns(TriggerState.Paused);
        A.CallTo(() => context.Api.QueryTriggerGroups(TestData.SchedulerName, A<DashboardGroupQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerGroupDto>([new TriggerGroupDto(TriggerGroup, Paused: true)], HasMore: false, TotalCount: 1));
        A.CallTo(() => context.Api.GetTriggerGroupPause(TestData.SchedulerName, TriggerGroup, A<CancellationToken>._))
            .Returns(new PauseInfo("vendor outage", "alice", new DateTimeOffset(2026, 10, 5, 13, 0, 0, TimeSpan.Zero)));

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => page.Find("tr[data-check=\"Trigger group\"]").TextContent.Should()
            .Contain("'CronTriggerGroup' is paused by alice at 2026-10-05 13:00:00").And.Contain("(vendor outage)"));
    }

    /// <summary>
    /// The first render reads the trigger once, and its diagnosis with it: the page read everything twice, once
    /// as it was initialized and once as its parameters were set.
    /// </summary>
    [Test]
    public void TheFirstRenderReadsTheTriggerOnce()
    {
        GivenTrigger(Scheduled(CronTrigger("0 0 12 * * ?")));

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => page.Find("[data-testid=trigger-diagnosis]"));
        A.CallTo(() => context.Api.GetTrigger(TestData.SchedulerName, A<TriggerKeyDto>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => context.Api.GetExecutionLimits(TestData.SchedulerName, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// A render of the same trigger reads nothing; a navigation to another trigger's page reads that one.
    /// </summary>
    [Test]
    public void AnotherTriggersPageReadsThatTrigger()
    {
        GivenTrigger(Scheduled(CronTrigger("0 0 12 * * ?")));
        IRenderedComponent<TriggerDetail> page = Render();
        page.WaitForAssertion(() => page.Find("[data-testid=trigger-diagnosis]"));

        page.Render(parameters => parameters.Add(x => x.Group, TriggerGroup).Add(x => x.Name, TriggerName));
        page.Render(parameters => parameters.Add(x => x.Group, TriggerGroup).Add(x => x.Name, "OtherTrigger"));

        page.WaitForAssertion(() => A.CallTo(() => context.Api.GetTrigger(TestData.SchedulerName, new TriggerKeyDto(TriggerGroup, "OtherTrigger"), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());
        A.CallTo(() => context.Api.GetTrigger(TestData.SchedulerName, new TriggerKeyDto(TriggerGroup, TriggerName), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// The actions the diagnosis offers keep their own operations: read-only mode offers neither.
    /// </summary>
    [Test]
    public void ReadOnlyModeOffersNoActionInTheDiagnosis()
    {
        context.Options.ReadOnly = true;
        GivenTrigger(Scheduled(CronTrigger("0 0 12 * * ?")));
        A.CallTo(() => context.Api.GetTriggerState(TestData.SchedulerName, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(TriggerState.Blocked);
        A.CallTo(() => context.Api.QueryFireInstances(TestData.SchedulerName, A<DashboardFireInstanceQuery>.That.Matches(query => query.Job != null), A<CancellationToken>._))
            .Returns(new PagedResult<FireInstanceDto>([HoldingFiring()], HasMore: false, TotalCount: 1));

        IRenderedComponent<TriggerDetail> page = Render();

        page.WaitForAssertion(() => page.Find("[data-testid=trigger-diagnosis]"));
        page.FindAll(".qz-diagnosis-interrupt, .qz-diagnosis-reset").Should().BeEmpty();
    }

    private static FireInstanceDto HoldingFiring()
    {
        DateTimeOffset firedAt = new(2026, 10, 5, 13, 0, 0, TimeSpan.Zero);
        return new FireInstanceDto("fire-7", new TriggerKeyDto(TriggerGroup, "other"), new JobKeyDto("CronJobGroup", "CronJobKey"), "node-b",
            FireInstanceState.Executing, firedAt, firedAt, ExecutionGroup: null);
    }

    /// <summary>A trigger as a store holds it: with its first fire time computed.</summary>
    private static ITrigger Scheduled(ITrigger trigger)
    {
        ((Quartz.Extensibility.IOperableTrigger) trigger).ComputeFirstFireTimeUtc(null);
        return trigger;
    }

    private static ITrigger ContinuationTrigger(ContinuationCondition condition)
    {
        return TriggerBuilder.Create()
            .WithIdentity(TriggerName, TriggerGroup)
            .ForJob("CronJobKey", "CronJobGroup")
            .StartAfter(new TriggerKey("import", "nightly"), condition)
            .WithCronSchedule("0 0 12 * * ?")
            .Build();
    }

    private static ITrigger CronTrigger(string cronExpression)
    {
        return TriggerBuilder.Create()
            .WithIdentity(TriggerName, TriggerGroup)
            .ForJob("CronJobKey", "CronJobGroup")
            .WithCalendarName("SomeCalendar")
            .WithCronSchedule(cronExpression)
            .Build();
    }

    private void GivenDeclaredAtStartup(bool? declared)
    {
        A.CallTo(() => context.Api.IsTriggerDeclaredAtStartup(TestData.SchedulerName, new TriggerKeyDto(TriggerGroup, TriggerName), A<CancellationToken>._))
            .Returns(declared);
    }

    private void GivenTrigger(ITrigger trigger)
    {
        A.CallTo(() => context.Api.GetTrigger(TestData.SchedulerName, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(trigger);
    }

    private IRenderedComponent<TriggerDetail> Render()
    {
        return context.Render<TriggerDetail>(parameters => parameters
            .Add(x => x.Group, TriggerGroup)
            .Add(x => x.Name, TriggerName));
    }
}
