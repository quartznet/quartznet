using Bunit;

using FakeItEasy;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.Impl.Triggers;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// Job Detail's <em>Add trigger</em> and Trigger Detail's <em>Duplicate…</em> (#4412): a new trigger of a stored job, of
/// any of the five kinds, posted through <c>ScheduleJob</c> with no job.
/// </summary>
/// <remarks>The pages' clock reads 14:23 UTC on Monday 5 October 2026, and the dashboard shows UTC.</remarks>
public sealed class AddTriggerFormTest
{
    private const string JobGroup = "billing";
    private const string JobName = "release-stale";

    private DashboardComponentContext context = null!;
    private ScheduleJobRequest? sent;

    [SetUp]
    public void SetUp()
    {
        sent = null;
        context = new DashboardComponentContext();
        context.WithScheduler();
        A.CallTo(() => context.Api.GetJobDetail(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .Returns(new JobDetailDto(
                JobName,
                JobGroup,
                "Quartz.Tests.AspNetCore.Support.DummyJob",
                Description: null,
                Durable: true,
                RequestsRecovery: false,
                ConcurrentExecutionDisallowed: false,
                PersistJobDataAfterExecution: false,
                JobDataMap: new JobDataMap()));
        A.CallTo(() => context.Api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .Returns(new List<TriggerHeaderDto>());
        A.CallTo(() => context.Api.GetCalendarNames(A<string>._, A<CancellationToken>._))
            .Returns(new List<string> { "holidays" });
        A.CallTo(() => context.Api.GetTriggerState(TestData.SchedulerName, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(TriggerState.Normal);
        A.CallTo(() => context.Api.ScheduleJob(TestData.SchedulerName, A<ScheduleJobRequest>._, A<CancellationToken>._))
            .Invokes((string _, ScheduleJobRequest request, CancellationToken _) => sent = request);
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    [Test]
    public void ACronTriggerIsAddedToTheStoredJobWithoutAJob()
    {
        IRenderedComponent<JobDetail> page = RenderJob();
        Open(page);

        page.Find("#add-trigger-name").Input("month-end");
        page.Find("#add-trigger-cron-expression").Input("0 0 23 L * ?");
        page.Find("#add-trigger-time-zone").Change("UTC");
        page.Find("#add-trigger-priority").Change("7");
        page.Find("#add-trigger-misfire").Change("2");
        page.Find("button.qz-add-trigger-save").Click();

        page.WaitForAssertion(() => sent.Should().NotBeNull());
        sent!.Job.Should().BeNull("the job is stored already, and the dashboard never creates one from a type name");
        CronTriggerImpl trigger = sent.Trigger.Should().BeOfType<CronTriggerImpl>().Subject;
        trigger.Key.Should().Be(new TriggerKey("month-end", JobGroup), "the group is the job's until another is typed");
        trigger.JobKey.Should().Be(new JobKey(JobName, JobGroup));
        trigger.CronExpressionString.Should().Be("0 0 23 L * ?");
        trigger.TimeZone.Should().Be(TimeZoneInfo.Utc);
        trigger.Priority.Should().Be(7);
        trigger.MisfireInstruction.Should().Be(CronTriggerMisfireInstruction.DoNothing);
        trigger.CalendarName.Should().BeNull();
        trigger.NextFireTimeUtc.Should().Be(new DateTimeOffset(2026, 10, 31, 23, 0, 0, TimeSpan.Zero), "the last day of October, at 23:00");
        context.Toasts.Messages.Should().Contain(toast => toast.Message == "Added trigger billing.month-end to job billing.release-stale.");
        context.ActionLog.GetLatest(1).Should().ContainSingle().Which.Action.Should().Be("ScheduleTrigger");
    }

    [TestCase("Simple", "#add-trigger-simple-interval")]
    [TestCase("CalendarInterval", "#add-trigger-calendar-interval")]
    [TestCase("DailyTimeInterval", "#add-trigger-daily-start")]
    [TestCase("Recurrence", "#add-trigger-recurrence-rule")]
    public void EveryKindCanBeAdded(string kind, string field)
    {
        IRenderedComponent<JobDetail> page = RenderJob();
        Open(page);

        page.Find("#add-trigger-name").Input("t-" + kind);
        page.Find("#add-trigger-kind").Change(kind);
        page.WaitForAssertion(() => page.FindAll(field).Should().ContainSingle("the kind's own fields replace cron's"));
        page.FindAll("#add-trigger-cron-expression").Should().BeEmpty();
        page.Find("button.qz-add-trigger-save").Click();

        page.WaitForAssertion(() => sent.Should().NotBeNull());
        ScheduleForm.KindOf(sent!.Trigger)!.Value.ToString().Should().Be(kind);
        sent.Trigger.NextFireTimeUtc.Should().NotBeNull().And.BeOnOrAfter(context.Clock.GetUtcNow());
        sent.Trigger.StartTimeUtc.Should().Be(sent.Trigger.NextFireTimeUtc!.Value,
            "ScheduleJob counts the first fire from the start, so a new trigger starts at it");
    }

    [Test]
    public void AStartBehindNowKeepsItsPhaseAndTheFirstFireIsAfterNow()
    {
        IRenderedComponent<JobDetail> page = RenderJob();
        Open(page);

        page.Find("#add-trigger-name").Input("every-other-day");
        page.Find("#add-trigger-kind").Change("CalendarInterval");
        page.Find("#add-trigger-calendar-interval").Input("2");
        page.Find("#add-trigger-time-zone").Change("UTC");
        page.Find("#add-trigger-starts").Change("2026-10-01T06:00");
        page.Find("button.qz-add-trigger-save").Click();

        page.WaitForAssertion(() => sent.Should().NotBeNull());
        sent!.Trigger.NextFireTimeUtc.Should().Be(new DateTimeOffset(2026, 10, 7, 6, 0, 0, TimeSpan.Zero),
            "every other day at 06:00 from the 1st: the 3rd and the 5th are behind 14:23 on the 5th, so the 7th");
    }

    [Test]
    public void ANameIsNeededAndNothingIsPostedWithoutOne()
    {
        IRenderedComponent<JobDetail> page = RenderJob();
        Open(page);

        page.Find("button.qz-add-trigger-save").Click();

        page.WaitForAssertion(() =>
        {
            AngleSharp.Dom.IElement error = page.Find("[data-testid=add-trigger-name-error]");
            error.TextContent.Should().Be("A trigger needs a name: with its group, it is the trigger's key.");
            page.Find("#add-trigger-name").GetAttribute("aria-describedby").Should().Be(error.Id);
        });
        sent.Should().BeNull();
    }

    [Test]
    public void ARefusalIsShownInTheFormWhichStaysOpen()
    {
        A.CallTo(() => context.Api.ScheduleJob(TestData.SchedulerName, A<ScheduleJobRequest>._, A<CancellationToken>._))
            .Throws(new ObjectAlreadyExistsException("Unable to store Trigger with name: 'nightly' and group: 'billing', because one already exists with this identification."));
        IRenderedComponent<JobDetail> page = RenderJob();
        Open(page);

        page.Find("#add-trigger-name").Input("nightly");
        page.Find("button.qz-add-trigger-save").Click();

        page.WaitForAssertion(() => page.Find("[data-testid=add-trigger-errors]").TextContent.Should().Contain("already exists"));
        page.FindAll("[data-testid=add-trigger]").Should().ContainSingle("the reader corrects the name rather than starting again");
    }

    [Test]
    public void ReadOnlyModeOffersNoAddTrigger()
    {
        context.Options.ReadOnly = true;

        IRenderedComponent<JobDetail> page = RenderJob();

        page.WaitForAssertion(() => page.Markup.Should().Contain("Associated triggers"));
        page.FindAll("button.qz-add-trigger-open").Should().BeEmpty();
    }

    [Test]
    public void DuplicateStartsFromTheTriggerAndMovesToTheCopy()
    {
        ITrigger original = TriggerBuilder.Create()
            .WithIdentity("nightly", JobGroup)
            .ForJob(JobName, JobGroup)
            .WithDescription("the nightly release")
            .WithCalendarName("holidays")
            .WithPriority(8)
            .WithExecutionGroup("imports")
            .WithPreferredNode(PreferredNode.For("node-a"))
            .UsingJobData("colour", "green")
            .StartAt(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero))
            .WithCronSchedule("0 0 2 * * ?", x => x.InTimeZone(TimeZoneInfo.Utc).WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing))
            .Build();
        A.CallTo(() => context.Api.GetTrigger(TestData.SchedulerName, A<TriggerKeyDto>._, A<CancellationToken>._)).Returns(original);
        A.CallTo(() => context.Api.GetCalendar(TestData.SchedulerName, "holidays", A<CancellationToken>._)).Returns(new Quartz.Impl.Calendar.HolidayCalendar());
        IRenderedComponent<TriggerDetail> page = context.Render<TriggerDetail>(parameters => parameters
            .Add(x => x.Group, JobGroup)
            .Add(x => x.Name, "nightly"));

        page.WaitForElement("button.qz-trigger-duplicate").Click();

        page.WaitForAssertion(() =>
        {
            page.Find("#add-trigger-name").GetAttribute("value").Should().Be("nightly-copy");
            page.Find("#add-trigger-cron-expression").GetAttribute("value").Should().Be("0 0 2 * * ?");
            page.Find("#add-trigger-calendar").GetAttribute("value").Should().Be("holidays");
        });
        page.Find("button.qz-add-trigger-save").Click();

        page.WaitForAssertion(() => sent.Should().NotBeNull());
        ITrigger copy = sent!.Trigger;
        copy.Should().BeOfType<CronTriggerImpl>().And.NotBeSameAs(original);
        copy.Key.Should().Be(new TriggerKey("nightly-copy", JobGroup));
        copy.JobKey.Should().Be(original.JobKey);
        copy.Description.Should().Be("the nightly release");
        copy.CalendarName.Should().Be("holidays");
        copy.Priority.Should().Be(8);
        copy.MisfireInstructionCode.Should().Be(original.MisfireInstructionCode);
        copy.ExecutionGroup.Should().Be("imports", "while the kind is kept, the copy carries everything the trigger does");
        copy.PreferredNode.Node.Should().Be("node-a");
        copy.JobDataMap["colour"].Should().Be("green");
        original.Key.Name.Should().Be("nightly", "the trigger on the page is not touched");
        context.Services.GetRequiredService<NavigationManager>().Uri.Should().EndWith("/quartz/triggers/billing/nightly-copy");
    }

    private IRenderedComponent<JobDetail> RenderJob()
    {
        return context.Render<JobDetail>(parameters => parameters
            .Add(x => x.Group, JobGroup)
            .Add(x => x.Name, JobName));
    }

    private static void Open(IRenderedComponent<JobDetail> page)
    {
        page.WaitForElement("button.qz-add-trigger-open").Click();
        page.WaitForElement("[data-testid=add-trigger]");
    }
}
