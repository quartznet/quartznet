using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The Jobs page's paging and group filtering, and what read-only mode takes away.
/// </summary>
public class JobsPageTest
{
    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
        context.WithScheduler();
        GivenGroups(("reports", false));
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    [Test]
    public void JobsAreListedUnderTheirGroupWithThatGroupsState()
    {
        GivenJobs(TestData.Dashboard.JobKeys("reports", 2));
        GivenGroups(("reports", true));

        IRenderedComponent<Jobs> page = context.Render<Jobs>();

        page.TextOfAll("h2").Should().Equal(["reports"], "one heading per group, and there is one group");
        page.TextOfAll(".qz-key-badge-value").Should().Equal(["reports.job-1", "reports.job-2"]);
        page.Find(".qz-state-indicator").TextContent.Should().Contain("Paused",
            "a paused group is the first thing to know about it, and only GetJobGroups reports it");
    }

    [Test]
    public void ThePagerAsksForTheSliceThePageNumberNames()
    {
        GivenJobs(TestData.Dashboard.JobKeys("reports", 60));

        IRenderedComponent<Jobs> page = context.Render<Jobs>();

        page.TextOfAll(".qz-key-badge-value").Should().HaveCount(25, "the page size is 25");

        page.FindAll(".qz-pagination button").First(button => button.TextContent.Trim() == "2").Click();

        A.CallTo(() => context.Api.GetJobs(
                TestData.SchedulerName,
                A<DashboardJobQuery>.That.Matches(query => query.Skip == 25 && query.Take == 25),
                A<CancellationToken>._))
            .MustHaveHappened();
        page.TextOfAll(".qz-key-badge-value").Should().Contain("reports.job-26",
            "the second page starts where the first left off");
    }

    [Test]
    public void APageBeyondTheEndIsClampedToTheLastOne()
    {
        GivenJobs(TestData.Dashboard.JobKeys("reports", 30));
        IRenderedComponent<Jobs> page = context.Render<Jobs>();
        page.FindAll(".qz-pagination button").First(button => button.TextContent.Trim() == "2").Click();

        // The listing shrinks under the reader — someone deleted the jobs the second page was showing.
        GivenJobs(TestData.Dashboard.JobKeys("reports", 3));
        page.FindAll("button").First(button => button.TextContent.Trim() == "Pause").Click();

        page.TextOfAll(".qz-key-badge-value").Should().Equal(["reports.job-1", "reports.job-2", "reports.job-3"],
            "a page past the end shows the last page rather than an empty one that looks like a deleted group");
    }

    [Test]
    public void AGroupFilterNarrowsTheListing()
    {
        GivenJobs([.. TestData.Dashboard.JobKeys("reports", 2), .. TestData.Dashboard.JobKeys("imports", 2)]);
        GivenGroups(("reports", false), ("imports", false));
        IRenderedComponent<Jobs> page = context.Render<Jobs>();
        page.TextOfAll("h2").Should().HaveCount(2);

        page.Find("input.qz-search-filter").Input("imp");

        // The filter is debounced, so the listing reloads a moment after the last keystroke.
        page.WaitForAssertion(() => page.TextOfAll("h2").Should().Equal(["imports"]));
        A.CallTo(() => context.Api.GetJobs(
                TestData.SchedulerName,
                A<DashboardJobQuery>.That.Matches(query => query.GroupContains == "imp" && query.Skip == 0),
                A<CancellationToken>._))
            .MustHaveHappened();
    }

    [Test]
    public void ReadOnlyModeHidesEveryMutatingAction()
    {
        context.Options.ReadOnly = true;
        GivenJobs(TestData.Dashboard.JobKeys("reports", 2));

        IRenderedComponent<Jobs> page = context.Render<Jobs>();

        page.HasButton("Pause").Should().BeFalse();
        page.HasButton("Trigger now").Should().BeFalse();
        page.HasButton("Delete").Should().BeFalse();
        page.HasButton("Pause group").Should().BeFalse();
        page.TextOfAll("th").Should().NotContain("Actions",
            "the column would be an empty one, which reads as a rendering fault rather than as a policy");
        page.TextOfAll(".qz-details-link").Should().HaveCount(2,
            "a read-only dashboard still navigates");
    }

    [Test]
    public void AGroupActionAppliesToTheGroupItNamesAndNotToTheOnesThatMerelyContainIt()
    {
        GivenJobs([.. TestData.Dashboard.JobKeys("reports", 2), .. TestData.Dashboard.JobKeys("reports-archive", 3)]);
        GivenGroups(("reports", false), ("reports-archive", false));
        A.CallTo(() => context.Api.PauseJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._)).Returns(true);
        IRenderedComponent<Jobs> page = context.Render<Jobs>();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Pause group").Click();

        A.CallTo(() => context.Api.PauseJob(
                TestData.SchedulerName,
                A<JobKeyDto>.That.Matches(key => key.Group == "reports"),
                A<CancellationToken>._))
            .MustHaveHappened(2, Times.Exactly);
        A.CallTo(() => context.Api.PauseJob(
                TestData.SchedulerName,
                A<JobKeyDto>.That.Matches(key => key.Group == "reports-archive"),
                A<CancellationToken>._))
            .MustNotHaveHappened();
        context.Toasts.Messages.Should().ContainSingle()
            .Which.Message.Should().Be("Paused 2 of 2 job(s) in group reports.",
                "the group filter matches groups that contain the name, so the count says which ones it acted on");
    }

    [Test]
    public void DeletingAJobAsksFirstAndRecordsTheAction()
    {
        GivenJobs(TestData.Dashboard.JobKeys("reports", 1));
        IRenderedComponent<Jobs> page = context.Render<Jobs>();

        page.FindAll("button").First(button => button.TextContent.Trim() == "Delete").Click();
        page.Markup.Should().Contain("Delete reports.job-1? This cannot be undone.");
        A.CallTo(() => context.Api.DeleteJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._)).MustNotHaveHappened();

        page.Find(".qz-confirm-dialog button.qz-button-danger").Click();

        A.CallTo(() => context.Api.DeleteJob(
                TestData.SchedulerName,
                new JobKeyDto("reports", "job-1"),
                A<CancellationToken>._))
            .MustHaveHappened();
        context.ActionLog.GetLatest(1).Should().ContainSingle()
            .Which.Target.Should().Be("reports.job-1");
    }

    [Test]
    public void AnApiThatRefusesIsReportedInsteadOfLeavingAStaleListing()
    {
        A.CallTo(() => context.Api.GetJobs(A<string>._, A<DashboardJobQuery>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("the scheduler is not answering"));

        IRenderedComponent<Jobs> page = context.Render<Jobs>();

        page.Markup.Should().Contain("the scheduler is not answering",
            "the reader can act on what went wrong; an empty listing looks like a scheduler with no jobs");
        page.HasButton("Retry").Should().BeTrue();
    }

    private void GivenJobs(IReadOnlyList<JobKeyDto> jobs)
    {
        A.CallTo(() => context.Api.GetJobs(A<string>._, A<DashboardJobQuery>._, A<CancellationToken>._))
            .ReturnsLazily((string _, DashboardJobQuery query, CancellationToken _) =>
            {
                List<JobKeyDto> matched = jobs
                    .Where(job => query.GroupContains is null
                        || job.Group.Contains(query.GroupContains, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                return TestData.Dashboard.Page<JobKeyDto>(
                    matched.Skip(query.Skip).Take(query.Take).ToList(),
                    matched.Count);
            });
    }

    private void GivenGroups(params (string Name, bool Paused)[] groups)
    {
        List<JobGroupDto> dtos = [];
        foreach ((string name, bool paused) in groups)
        {
            dtos.Add(new JobGroupDto(name, paused));
        }

        A.CallTo(() => context.Api.GetJobGroups(A<string>._, A<CancellationToken>._)).Returns(dtos);
    }
}
