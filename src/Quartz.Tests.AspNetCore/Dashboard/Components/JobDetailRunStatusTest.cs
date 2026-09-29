using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The Job Detail page's run status panel, and its link to the job's own history.
/// </summary>
public class JobDetailRunStatusTest
{
    private static readonly DateTimeOffset LastRun = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
        context.WithScheduler();

        A.CallTo(() => context.Api.GetJobDetail(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .Returns(new JobDetailDto(
                "release-stale",
                "billing",
                "Quartz.Tests.AspNetCore.Support.DummyJob",
                "Releases stale reservations",
                Durable: true,
                RequestsRecovery: false,
                ConcurrentExecutionDisallowed: false,
                PersistJobDataAfterExecution: false,
                JobDataMap: new JobDataMap()));
        A.CallTo(() => context.Api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .Returns(new List<TriggerHeaderDto>());
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    [Test]
    public void ThePanelSaysHowTheJobsRunsHaveGone()
    {
        A.CallTo(() => context.Api.GetJobRunStatus(TestData.SchedulerName, new JobKeyDto("billing", "release-stale"), A<CancellationToken>._))
            .Returns(new JobRunStatus(TestData.SchedulerName, new JobKey("release-stale", "billing"), LastRun, JobRunResult.Failed)
            {
                LastEntryId = "entry-9",
                LastSchedulerInstanceId = "node-a",
                LastSucceededAtUtc = LastRun.AddDays(-1),
                LastFailedAtUtc = LastRun,
                LastFailureMessage = "the upstream system is down",
                ConsecutiveFailures = 2,
                RunCount = 40,
                FailureCount = 5,
                FirstFiredAtUtc = LastRun.AddDays(-30)
            });

        IRenderedComponent<JobDetail> page = Render();

        page.WaitForAssertion(() =>
        {
            page.Find(".qz-job-status-last-run .qz-state-label").TextContent.Should().Be("Failed");
            page.Find(".qz-job-status-last-run a").GetAttribute("href").Should().EndWith("history/entry-9",
                "the last run opens its own execution page, log and all, while the history keeps it");
            page.Find(".qz-job-status-last-success").TextContent.Should().Be("2029-12-31 12:00:00 +00:00");
            page.Find(".qz-job-status-last-failure").TextContent.Should().Contain("the upstream system is down");
            page.Find(".qz-job-status-failing").TextContent.Trim().Should().Be("failing ×2");
            page.Find(".qz-job-status-counts").TextContent.Should().StartWith("40 run(s), 5 failed for good");
        });
    }

    [Test]
    public void AJobWithNoRecordedRunSaysSo()
    {
        A.CallTo(() => context.Api.GetJobRunStatus(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .Returns((JobRunStatus?) null);

        IRenderedComponent<JobDetail> page = Render();

        page.WaitForAssertion(() =>
            page.Find("[data-testid=job-run-status-none]").TextContent.Should().Contain("No run of this job has been recorded"));
    }

    [Test]
    public void ASourceThatKeepsNoStatusLeavesThePanelOut()
    {
        IRenderedComponent<JobDetail> page = Render();

        page.WaitForAssertion(() =>
        {
            page.Markup.Should().Contain("Releases stale reservations", "the rest of the page does not depend on the panel");
            page.FindAll("[data-testid=job-run-status]").Should().BeEmpty();
            page.FindAll("[data-testid=job-run-status-none]").Should().BeEmpty(
                "a source that cannot say must not be read as a job that never ran");
            page.TextOfAll("h2").Should().NotContain("Runs");
        });
    }

    [Test]
    public void TheHistoryLinkNarrowsToThisJobExactly()
    {
        IRenderedComponent<JobDetail> page = Render();

        page.WaitForAssertion(() =>
            page.FindAll("a").Single(link => link.TextContent.Contains("View execution history", StringComparison.Ordinal))
                .GetAttribute("href").Should().EndWith("history?jobGroup=billing&jobName=release-stale",
                    "a text filter would also list release-stale-archive"));
    }

    [Test]
    public void ReadOnlyModeStillShowsThePanel()
    {
        context.Options.ReadOnly = true;
        A.CallTo(() => context.Api.GetJobRunStatus(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .Returns(new JobRunStatus(TestData.SchedulerName, new JobKey("release-stale", "billing"), LastRun, JobRunResult.Skipped));

        IRenderedComponent<JobDetail> page = Render();

        page.WaitForAssertion(() =>
        {
            page.Find(".qz-job-status-last-run .qz-state-label").TextContent.Should().Be("Skipped", "the panel only reads");
            page.HasButton("Trigger now").Should().BeFalse();
        });
    }

    /// <summary>
    /// A data source an application wrote against 4.3 answers both reads by saying it cannot, which is what
    /// the pages turn into a panel and columns left out.
    /// </summary>
    [Test]
    public async Task TheDefaultStatusReadsReportTheDatumAsUnavailable()
    {
        IQuartzApiClient client = A.Fake<IQuartzApiClient>(options => options.CallsBaseMethods());

        Func<Task> one = async () => await client.GetJobRunStatus(TestData.SchedulerName, new JobKeyDto("billing", "release-stale"));
        Func<Task> many = async () => await client.GetJobRunStatuses(TestData.SchedulerName, [new JobKeyDto("billing", "release-stale")]);

        await one.Should().ThrowAsync<NotSupportedException>().WithMessage("*GetJobRunStatus*");
        await many.Should().ThrowAsync<NotSupportedException>().WithMessage("*GetJobRunStatuses*");
    }

    private IRenderedComponent<JobDetail> Render()
    {
        return context.Render<JobDetail>(parameters => parameters
            .Add(x => x.Group, "billing")
            .Add(x => x.Name, "release-stale"));
    }
}
