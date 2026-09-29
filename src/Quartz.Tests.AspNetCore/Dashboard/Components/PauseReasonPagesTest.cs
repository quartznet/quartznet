using System.Security.Claims;

using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The dashboard's side of a pause that says why: every pause button asks for an optional reason, and
/// every paused trigger or group on screen says why it is paused, who asked and when.
/// </summary>
public sealed class PauseReasonPagesTest
{
    private static readonly PauseInfo record = new(
        "database maintenance", "alice", new DateTimeOffset(2031, 6, 17, 10, 0, 0, TimeSpan.Zero));

    private const string RenderedRecord = "Paused: database maintenance (by alice, 2031-06-17 10:00:00 +00:00)";

    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
        context.WithScheduler();

        // The operator the requester is read from.
        context.AuthenticationState.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "operator@example.com")], authenticationType: "test"));
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Trigger detail
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public void APausedTriggerSaysWhyWhoAskedAndWhen()
    {
        GivenTrigger(TriggerState.Paused);
        A.CallTo(() => context.Api.GetTriggerPause(TestData.SchedulerName, TriggerKey, A<CancellationToken>._))
            .Returns(record);

        IRenderedComponent<TriggerDetail> page = RenderTrigger();

        page.WaitForAssertion(() => page.Find(".qz-pause-note").TextContent.Trim().Should().Be(RenderedRecord,
            "the note sits beside the paused state and says what the pause recorded"));
    }

    [Test]
    public void ATriggerThatIsNotPausedIsNotAskedWhy()
    {
        GivenTrigger(TriggerState.Normal);

        IRenderedComponent<TriggerDetail> page = RenderTrigger();

        page.WaitForAssertion(() => page.FindAll(".qz-pause-note").Should().BeEmpty());
        A.CallTo(() => context.Api.GetTriggerPause(A<string>._, A<TriggerKeyDto>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public void APauseRecordedWithoutAReasonOrRequesterStillSaysWhen()
    {
        GivenTrigger(TriggerState.Paused);
        A.CallTo(() => context.Api.GetTriggerPause(TestData.SchedulerName, TriggerKey, A<CancellationToken>._))
            .Returns(record with { Reason = null, RequestedBy = null });

        IRenderedComponent<TriggerDetail> page = RenderTrigger();

        page.WaitForAssertion(() => page.Find(".qz-pause-note").TextContent.Trim()
            .Should().Be("Paused (2031-06-17 10:00:00 +00:00)"));
    }

    [Test]
    public void PausingATriggerAsksForAReasonAndSendsItWithTheOperator()
    {
        GivenTrigger(TriggerState.Normal);
        A.CallTo(() => context.Api.PauseTrigger(A<string>._, A<TriggerKeyDto>._, A<CancellationToken>._)).Returns(true);
        IRenderedComponent<TriggerDetail> page = RenderTrigger();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Pause").Click();

        page.WaitForAssertion(() => page.Markup.Should().Contain("Pause CronTriggerGroup.CronTriggerKey?",
            "the prompt names what it is about to pause"));
        A.CallTo(() => context.Api.PauseTrigger(A<string>._, A<TriggerKeyDto>._, A<CancellationToken>._))
            .MustNotHaveHappened();

        page.ConfirmPause("  vendor outage  ");

        page.WaitForAssertion(() => A.CallTo(() => context.Api.PauseTriggerWith(
                TestData.SchedulerName,
                TriggerKey,
                A<PauseDetails>.That.Matches(d => d.Reason == "vendor outage" && d.RequestedBy == "operator@example.com"),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());
        page.WaitForAssertion(() => page.FindAll(".qz-pause-dialog").Should().BeEmpty("the prompt closes once it is answered"));
        context.ActionLog.GetLatest(1).Should().ContainSingle().Which.Message.Should().Be("reason: vendor outage",
            "the action log answers why as well as who, as a selection's pause does");
    }

    [Test]
    public void AReasonLeftBlankPausesWithoutOne()
    {
        GivenTrigger(TriggerState.Normal);
        IRenderedComponent<TriggerDetail> page = RenderTrigger();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Pause").Click();
        page.ConfirmPause("   ");

        page.WaitForAssertion(() => A.CallTo(() => context.Api.PauseTriggerWith(
                TestData.SchedulerName,
                TriggerKey,
                A<PauseDetails>.That.Matches(d => d.Reason == null),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());
        page.WaitForAssertion(() => context.ActionLog.GetLatest(1).Should().ContainSingle()
            .Which.Message.Should().BeNull("a pause without a reason has nothing to add to the entry"));
    }

    [Test]
    public void CancellingThePromptPausesNothing()
    {
        GivenTrigger(TriggerState.Normal);
        IRenderedComponent<TriggerDetail> page = RenderTrigger();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Pause").Click();
        page.WaitForElement(".qz-pause-dialog");
        page.FindAll(".qz-pause-dialog button").First(button => button.TextContent.Trim() == "Cancel").Click();

        page.WaitForAssertion(() => page.FindAll(".qz-pause-dialog").Should().BeEmpty());
        A.CallTo(() => context.Api.PauseTriggerWith(A<string>._, A<TriggerKeyDto>._, A<PauseDetails>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Listings
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public void TheTriggerListingShowsEachPausedTriggersRecord()
    {
        List<TriggerHeaderDto> headers = TestData.Dashboard.TriggerHeaders("nightly", 2, TriggerState.Paused);
        headers[0] = headers[0] with { Pause = record };
        GivenTriggers(headers);

        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.WaitForAssertion(() => page.TextOfAll(".qz-pause-note").Should().Equal([RenderedRecord],
            "the record travels with the listing, and a trigger whose pause recorded nothing shows its state alone"));
        page.Find(".qz-pause-note").GetAttribute("title").Should().Be(RenderedRecord,
            "the listing's state column cuts a long note to fit, so its title holds the whole of it");
    }

    [Test]
    public void PausingFromTheTriggerListingAsksForAReason()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 1));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Pause").Click();
        page.ConfirmPause("deploy");

        page.WaitForAssertion(() => A.CallTo(() => context.Api.PauseTriggerWith(
                TestData.SchedulerName,
                new TriggerKeyDto("nightly", "trigger-1"),
                A<PauseDetails>.That.Matches(d => d.Reason == "deploy" && d.RequestedBy == "operator@example.com"),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());
        page.WaitForAssertion(() => context.ActionLog.GetLatest(1).Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Action = "PauseTrigger", Target = "nightly.trigger-1", Message = "reason: deploy" },
                options => options.ExcludingMissingMembers()));
    }

    [Test]
    public void PausingATriggerGroupSendsTheReasonWithEachTrigger()
    {
        GivenTriggers(TestData.Dashboard.TriggerHeaders("nightly", 2));
        IRenderedComponent<Triggers> page = context.Render<Triggers>();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Pause group").Click();
        page.ConfirmPause("deploy");

        page.WaitForAssertion(() => A.CallTo(() => context.Api.PauseTriggerWith(
                TestData.SchedulerName,
                A<TriggerKeyDto>.That.Matches(key => key.Group == "nightly"),
                A<PauseDetails>.That.Matches(d => d.Reason == "deploy"),
                A<CancellationToken>._))
            .MustHaveHappened(2, Times.Exactly));
        page.WaitForAssertion(() => context.ActionLog.GetLatest(1).Should().ContainSingle()
            .Which.Message.Should().Be("0 of 2 trigger(s); reason: deploy"));
    }

    [Test]
    public void APausedJobGroupSaysWhyInTheJobListing()
    {
        GivenJobs(TestData.Dashboard.JobKeys("reports", 1));
        A.CallTo(() => context.Api.QueryJobGroups(A<string>._, A<DashboardGroupQuery>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.Page<JobGroupDto>([new JobGroupDto("reports", Paused: true)]));
        A.CallTo(() => context.Api.GetJobGroupPause(TestData.SchedulerName, "reports", A<CancellationToken>._))
            .Returns(record);

        IRenderedComponent<Jobs> page = context.Render<Jobs>();

        page.WaitForAssertion(() => page.TextOfAll(".qz-pause-note").Should().Equal([RenderedRecord]));
    }

    [Test]
    public void AJobGroupThatIsNotPausedIsNotAskedWhy()
    {
        GivenJobs(TestData.Dashboard.JobKeys("reports", 1));

        IRenderedComponent<Jobs> page = context.Render<Jobs>();

        page.WaitForAssertion(() => page.FindAll(".qz-pause-note").Should().BeEmpty());
        A.CallTo(() => context.Api.GetJobGroupPause(A<string>._, A<string>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public void PausingFromTheJobListingAsksForAReason()
    {
        GivenJobs(TestData.Dashboard.JobKeys("reports", 1));
        A.CallTo(() => context.Api.PauseJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._)).Returns(true);
        IRenderedComponent<Jobs> page = context.Render<Jobs>();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Pause").Click();
        page.ConfirmPause("quarter close");

        page.WaitForAssertion(() => A.CallTo(() => context.Api.PauseJobWith(
                TestData.SchedulerName,
                new JobKeyDto("reports", "job-1"),
                A<PauseDetails>.That.Matches(d => d.Reason == "quarter close" && d.RequestedBy == "operator@example.com"),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());
        page.WaitForAssertion(() => context.ActionLog.GetLatest(1).Should().ContainSingle()
            .Which.Message.Should().Be("reason: quarter close"));
    }

    [Test]
    public void PausingAJobGroupSendsTheReasonWithEachJob()
    {
        GivenJobs(TestData.Dashboard.JobKeys("reports", 2));
        IRenderedComponent<Jobs> page = context.Render<Jobs>();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Pause group").Click();
        page.ConfirmPause("quarter close");

        page.WaitForAssertion(() => A.CallTo(() => context.Api.PauseJobWith(
                TestData.SchedulerName,
                A<JobKeyDto>.That.Matches(key => key.Group == "reports"),
                A<PauseDetails>.That.Matches(d => d.Reason == "quarter close"),
                A<CancellationToken>._))
            .MustHaveHappened(2, Times.Exactly));
    }

    [Test]
    public void CancellingAListingsPromptPausesNothing()
    {
        GivenJobs(TestData.Dashboard.JobKeys("reports", 1));
        IRenderedComponent<Jobs> page = context.Render<Jobs>();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Pause").Click();
        page.WaitForElement(".qz-pause-dialog");
        page.FindAll(".qz-pause-dialog button").First(button => button.TextContent.Trim() == "Cancel").Click();

        page.WaitForAssertion(() => page.FindAll(".qz-pause-dialog").Should().BeEmpty());
        A.CallTo(() => context.Api.PauseJobWith(A<string>._, A<JobKeyDto>._, A<PauseDetails>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Job detail and the overview
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public void AJobsPageSaysWhyItsGroupAndEachOfItsTriggersArePaused()
    {
        GivenJob();
        List<TriggerHeaderDto> triggers = TestData.Dashboard.TriggerHeaders("nightly", 1, TriggerState.Paused);
        triggers[0] = triggers[0] with { Pause = record with { Reason = "vendor outage" } };
        A.CallTo(() => context.Api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._)).Returns(triggers);
        A.CallTo(() => context.Api.GetJobGroupPause(TestData.SchedulerName, "reports", A<CancellationToken>._)).Returns(record);

        IRenderedComponent<JobDetail> page = RenderJob();

        page.WaitForAssertion(() => page.TextOfAll(".qz-pause-note").Should().Equal(
            [RenderedRecord, "Paused: vendor outage (by alice, 2031-06-17 10:00:00 +00:00)"],
            "the group's pause heads the page and each trigger's sits beside its state"));
    }

    [Test]
    public void PausingAJobFromItsPageAsksForAReason()
    {
        GivenJob();
        IRenderedComponent<JobDetail> page = RenderJob();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Pause").Click();
        page.ConfirmPause("quarter close");

        page.WaitForAssertion(() => A.CallTo(() => context.Api.PauseJobWith(
                TestData.SchedulerName,
                new JobKeyDto("reports", "job-1"),
                A<PauseDetails>.That.Matches(d => d.Reason == "quarter close"),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());
        page.WaitForAssertion(() => context.ActionLog.GetLatest(1).Should().ContainSingle()
            .Which.Message.Should().Be("reason: quarter close"));
    }

    [Test]
    public void CancellingAJobsPromptPausesNothing()
    {
        GivenJob();
        IRenderedComponent<JobDetail> page = RenderJob();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Pause").Click();
        page.WaitForElement(".qz-pause-dialog");
        page.FindAll(".qz-pause-dialog button").First(button => button.TextContent.Trim() == "Cancel").Click();

        page.WaitForAssertion(() => page.FindAll(".qz-pause-dialog").Should().BeEmpty());
        A.CallTo(() => context.Api.PauseJobWith(A<string>._, A<JobKeyDto>._, A<PauseDetails>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Test]
    public void PausingEverythingAsksForAReason()
    {
        IRenderedComponent<Quartz.Dashboard.Components.Pages.Dashboard> page = context.Render<Quartz.Dashboard.Components.Pages.Dashboard>();

        page.WaitForAssertion(() => page.HasButton("Pause all").Should().BeTrue());
        page.FindAll("button").First(button => button.TextContent.Trim() == "Pause all").Click();

        page.WaitForAssertion(() => page.Markup.Should().Contain("Pause every trigger group?"));
        page.ConfirmPause("datacentre move");

        page.WaitForAssertion(() => A.CallTo(() => context.Api.PauseAllWith(
                TestData.SchedulerName,
                A<PauseDetails>.That.Matches(d => d.Reason == "datacentre move" && d.RequestedBy == "operator@example.com"),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly());
        page.WaitForAssertion(() => context.ActionLog.GetLatest(1).Should().ContainSingle()
            .Which.Message.Should().Be("reason: datacentre move"));
    }

    [Test]
    public void CancellingThePauseAllPromptPausesNothing()
    {
        IRenderedComponent<Quartz.Dashboard.Components.Pages.Dashboard> page = context.Render<Quartz.Dashboard.Components.Pages.Dashboard>();

        page.WaitForAssertion(() => page.HasButton("Pause all").Should().BeTrue());
        page.FindAll("button").First(button => button.TextContent.Trim() == "Pause all").Click();
        page.WaitForElement(".qz-pause-dialog");
        page.FindAll(".qz-pause-dialog button").First(button => button.TextContent.Trim() == "Cancel").Click();

        page.WaitForAssertion(() => page.FindAll(".qz-pause-dialog").Should().BeEmpty());
        A.CallTo(() => context.Api.PauseAllWith(A<string>._, A<PauseDetails>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Scaffolding
    //////////////////////////////////////////////////////////////////////////////////////////////

    private static readonly TriggerKeyDto TriggerKey = new("CronTriggerGroup", "CronTriggerKey");

    private void GivenTrigger(TriggerState state)
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity(TriggerKey.Name, TriggerKey.Group)
            .ForJob("CronJobKey", "CronJobGroup")
            .WithCronSchedule("0 0 12 * * ?")
            .Build();

        A.CallTo(() => context.Api.GetTrigger(TestData.SchedulerName, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(trigger);
        A.CallTo(() => context.Api.GetTriggerState(TestData.SchedulerName, A<TriggerKeyDto>._, A<CancellationToken>._))
            .Returns(state);
    }

    private IRenderedComponent<TriggerDetail> RenderTrigger()
    {
        return context.Render<TriggerDetail>(parameters => parameters
            .Add(x => x.Group, TriggerKey.Group)
            .Add(x => x.Name, TriggerKey.Name));
    }

    private void GivenTriggers(List<TriggerHeaderDto> triggers)
    {
        A.CallTo(() => context.Api.QueryTriggers(A<string>._, A<DashboardTriggerQuery>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.Page<TriggerHeaderDto>([.. triggers], triggers.Count));
    }

    private void GivenJobs(List<JobKeyDto> jobs)
    {
        A.CallTo(() => context.Api.QueryJobs(A<string>._, A<DashboardJobQuery>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.Page<JobKeyDto>([.. jobs], jobs.Count));
    }

    private void GivenJob()
    {
        A.CallTo(() => context.Api.GetJobDetail(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .Returns(new JobDetailDto(
                "job-1",
                "reports",
                "Quartz.Tests.AspNetCore.Support.DummyJob",
                "Dummy job description",
                Durable: true,
                RequestsRecovery: false,
                ConcurrentExecutionDisallowed: false,
                PersistJobDataAfterExecution: false,
                JobDataMap: new JobDataMap()));
        A.CallTo(() => context.Api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.TriggerHeaders("nightly", 1));
    }

    private IRenderedComponent<JobDetail> RenderJob()
    {
        return context.Render<JobDetail>(parameters => parameters
            .Add(x => x.Group, "reports")
            .Add(x => x.Name, "job-1"));
    }
}
