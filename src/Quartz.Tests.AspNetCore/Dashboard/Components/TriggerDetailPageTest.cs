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
    public void ACronTriggerOffersItsExpressionForEditing()
    {
        GivenTrigger(CronTrigger("0/25 * * * * ?"));

        IRenderedComponent<TriggerDetail> page = Render();

        page.Find("#trigger-cron-expression").GetAttribute("value").Should().Be("0/25 * * * * ?",
            "the editor starts from the schedule the trigger is running with");
        page.Markup.Should().Contain("Upcoming cron fires",
            "the page previews the expression so a reader can see what it means before saving");
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
    public void ATriggerWithNoCronExpressionIsNotOfferedACronEditor()
    {
        GivenTrigger(TestData.SimpleTrigger);

        IRenderedComponent<TriggerDetail> page = Render();

        page.Markup.Should().NotContain("Edit cron schedule",
            "there is no cron expression to edit, and a simple trigger's interval is not one");
        page.Markup.Should().Contain("Every 120.02:30:59.9990000",
            "the schedule is summarised in the trigger's own terms instead");
    }

    [Test]
    public void ReadOnlyModeHidesEveryMutatingAction()
    {
        context.Options.ReadOnly = true;
        GivenTrigger(CronTrigger("0/25 * * * * ?"));

        IRenderedComponent<TriggerDetail> page = Render();

        page.HasButton("Pause").Should().BeFalse();
        page.HasButton("Resume").Should().BeFalse();
        page.HasButton("Unschedule").Should().BeFalse();
        page.Markup.Should().NotContain("Edit cron schedule",
            "a read-only dashboard shows the schedule but does not offer to change it");
        page.Markup.Should().Contain("Upcoming cron fires",
            "reading the schedule is exactly what a read-only dashboard is for");
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

    private static ITrigger CronTrigger(string cronExpression)
    {
        return TriggerBuilder.Create()
            .WithIdentity(TriggerName, TriggerGroup)
            .ForJob("CronJobKey", "CronJobGroup")
            .WithCalendarName("SomeCalendar")
            .WithCronSchedule(cronExpression)
            .Build();
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
