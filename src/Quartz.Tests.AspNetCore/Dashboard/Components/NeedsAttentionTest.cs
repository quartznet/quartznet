#region License
/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */
#endregion

using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Shared;
using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

using DashboardPage = Quartz.Dashboard.Components.Pages.Dashboard;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The Overview's <em>Needs attention</em> (#4411): the jobs failing in a row, the waiting triggers past their
/// fire time, and the day's runs, each one read and a part left out where its source cannot answer.
/// </summary>
public class NeedsAttentionTest
{
    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
        context.WithScheduler();
        A.CallTo(() => context.Api.QueryTriggers(A<string>._, A<DashboardTriggerQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeaderDto>([], HasMore: false, TotalCount: 0));
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    [Test]
    public void TheFailingJobsAreRankedByHowManyTimesInARowTheyFailed()
    {
        GivenFailing(Enumerable.Range(1, 12).Select(index => Status("job-" + index.ToString("00", System.Globalization.CultureInfo.InvariantCulture), consecutiveFailures: index)).ToArray());

        IRenderedComponent<NeedsAttention> panel = Render();

        panel.WaitForAssertion(() => panel.Find("[data-testid=needs-attention-failing] p").TextContent.Should()
            .Be("12 jobs are failing; these are the 10 failing the most times in a row."));
        List<string> rows = panel.FindAll("[data-testid=needs-attention-failing] tbody tr td:first-child").Select(cell => cell.TextContent.Trim()).ToList();
        rows.Should().HaveCount(10);
        rows[0].Should().Be("reports.job-12", "the job failing the most times in a row comes first");
        rows.Should().NotContain(["reports.job-01", "reports.job-02"]);
        panel.Find("[data-testid=needs-attention-failing] .qz-failing-badge").GetAttribute("href").Should()
            .Be("quartz/history?outcome=failed&jobGroup=reports&jobName=job-12&result=failed");

        A.CallTo(() => context.Api.QueryJobRunStatuses(
                A<JobRunStatusQuery>.That.Matches(query => query.SchedulerName == TestData.SchedulerName && query.Failing == true && query.IncludeTotalCount),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// More failing jobs than one page holds: the ten are ranked from the page read, the first 250 by name, and
    /// the sentence says so rather than claiming the ten worst of all.
    /// </summary>
    [Test]
    public void AFailingListLongerThanAPageSaysItsTenAreFromTheFirstPage()
    {
        JobRunStatus[] page = Enumerable.Range(1, 12).Select(index => Status("job-" + index.ToString("00", System.Globalization.CultureInfo.InvariantCulture), consecutiveFailures: index)).ToArray();
        A.CallTo(() => context.Api.QueryJobRunStatuses(A<JobRunStatusQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<JobRunStatus>([.. page], HasMore: true, TotalCount: 300));

        IRenderedComponent<NeedsAttention> panel = Render();

        panel.WaitForAssertion(() => panel.Find("[data-testid=needs-attention-failing] p").TextContent.Should()
            .Be("300 jobs are failing; these are the 10 failing the most times in a row, of the first 250 by name."));
    }

    [Test]
    public void NoFailingJobIsSaid()
    {
        GivenFailing();

        IRenderedComponent<NeedsAttention> panel = Render();

        panel.WaitForAssertion(() => panel.Find("[data-testid=needs-attention-failing]").TextContent.Trim().Should().Be("No job is failing."));
    }

    /// <summary>
    /// A source that keeps no per-job run status leaves the part out, as the Jobs page leaves out its columns.
    /// </summary>
    [Test]
    public void ASourceWithoutRunStatusLeavesTheFailingJobsOut()
    {
        IRenderedComponent<NeedsAttention> panel = Render();

        panel.WaitForAssertion(() => panel.Find("[data-testid=needs-attention-overdue]"));
        panel.FindAll("[data-testid=needs-attention-failing]").Should().BeEmpty();
        panel.FindAll("[data-testid=run-statistics]").Should().BeEmpty("the source counts no runs either");
    }

    /// <summary>
    /// "Overdue" is what the due-backlog gauges count: a waiting trigger more than five minutes past its fire
    /// time, counted with <c>Take = 0</c>, and the count links to the Triggers page narrowed to the same.
    /// </summary>
    [Test]
    public void TheOverdueTriggersAreCountedAndLinked()
    {
        DateTimeOffset cutoff = context.Clock.GetUtcNow() - TimeSpan.FromMinutes(5);
        A.CallTo(() => context.Api.QueryTriggers(TestData.SchedulerName, A<DashboardTriggerQuery>.That.Matches(query =>
                query.State == TriggerState.Normal && query.NextFireTimeBefore == cutoff && query.Take == 0), A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeaderDto>([], HasMore: true, TotalCount: 3));

        IRenderedComponent<NeedsAttention> panel = Render();

        panel.WaitForAssertion(() => panel.Find("[data-testid=needs-attention-overdue]").TextContent.Trim().Should()
            .Be("3 waiting triggers are more than 5 minutes past their fire time."));
        panel.Find("[data-testid=needs-attention-overdue] a").GetAttribute("href").Should()
            .Be("quartz/triggers?state=Normal&before=" + Uri.EscapeDataString(cutoff.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Test]
    public void TheLastDaysRunsAreChartedFromOneRead()
    {
        DateTimeOffset now = context.Clock.GetUtcNow();
        A.CallTo(() => context.Api.QueryExecutionStatistics(A<ExecutionStatisticsQuery>._, A<CancellationToken>._))
            .Returns(new ExecutionStatistics
            {
                BucketSize = TimeSpan.FromHours(1),
                Buckets = [new ExecutionStatisticsBucket(RunStatisticsWindow.AlignDown(now, TimeSpan.FromHours(1))) { SucceededCount = 9, FailedCount = 1 }],
            });

        IRenderedComponent<NeedsAttention> panel = Render();

        panel.WaitForAssertion(() => panel.Find("[data-testid=run-statistics-summary]").TextContent.Should().Contain("10 runs").And.Contain("1 failed"));
        A.CallTo(() => context.Api.QueryExecutionStatistics(
                A<ExecutionStatisticsQuery>.That.Matches(query => query.SchedulerName == TestData.SchedulerName && query.BucketSize == TimeSpan.FromHours(1)),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// The overview renders every second for its executing count; the panel reads only when the scheduler
    /// changes and on its own timers.
    /// </summary>
    [Test]
    public void TheOverviewsRendersDoNotReadThePanelAgain()
    {
        GivenFailing();
        A.CallTo(() => context.Api.QueryClusterNodes(A<string>._, A<CancellationToken>._)).Returns(new List<ClusterNodeDto>());

        IRenderedComponent<DashboardPage> page = context.Render<DashboardPage>();
        page.WaitForAssertion(() => page.Find("[data-testid=needs-attention]"));
        page.Render();
        page.Render();

        A.CallTo(() => context.Api.QueryJobRunStatuses(A<JobRunStatusQuery>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    /// <summary>
    /// A data source an application wrote against 4.8 says it cannot list run statuses, which the panel turns
    /// into the part left out.
    /// </summary>
    [Test]
    public async Task TheDefaultListingReportsTheDatumAsUnavailable()
    {
        IQuartzApiClient client = A.Fake<IQuartzApiClient>(options => options.CallsBaseMethods());

        Func<Task> query = async () => await client.QueryJobRunStatuses(new JobRunStatusQuery { SchedulerName = TestData.SchedulerName, Failing = true });

        await query.Should().ThrowAsync<NotSupportedException>().WithMessage("*QueryJobRunStatuses*");
    }

    private void GivenFailing(params JobRunStatus[] statuses)
    {
        A.CallTo(() => context.Api.QueryJobRunStatuses(A<JobRunStatusQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<JobRunStatus>([.. statuses], HasMore: false, TotalCount: statuses.Length));
    }

    private static JobRunStatus Status(string name, int consecutiveFailures)
    {
        DateTimeOffset failedAt = TestData.Dashboard.FiredAt;
        return new JobRunStatus(TestData.SchedulerName, new JobKey(name, "reports"), failedAt, JobRunResult.Failed)
        {
            ConsecutiveFailures = consecutiveFailures,
            LastFailedAtUtc = failedAt,
            LastFailureMessage = "connection refused",
        };
    }

    private IRenderedComponent<NeedsAttention> Render()
    {
        return context.Render<NeedsAttention>(parameters => parameters.Add(x => x.SchedulerName, TestData.SchedulerName));
    }
}
