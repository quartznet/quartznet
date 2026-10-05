using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Components.Shared;
using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The run chart, and the History and Job Detail pages that read it: what it draws, what it says when
/// there is nothing to draw, and which filters it is read with.
/// </summary>
public class RunStatisticsChartTest
{
    /// <summary>14:00 on the clock <see cref="DashboardComponentContext" /> holds the pages to, which reads 14:23.</summary>
    private static readonly DateTimeOffset TwoOClock = new(2026, 10, 5, 14, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Midnight = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

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
    public void EachBucketIsAColumnStackedByResultWithTheDurationsBeneath()
    {
        IRenderedComponent<RunStatisticsChart> chart = context.Render<RunStatisticsChart>(parameters => parameters
            .Add(x => x.Statistics, Hourly(
                Bucket(TwoOClock.AddHours(-3), succeeded: 40, failed: 2, p50: 120, p95: 450, max: 900),
                Bucket(TwoOClock.AddHours(-2), succeeded: 10, skipped: 5, cancelled: 1, p50: 80, p95: 200, max: 210),
                Bucket(TwoOClock, succeeded: 3, p50: 100, p95: 100, max: 100)))
            .Add(x => x.From, TwoOClock.AddHours(-3))
            .Add(x => x.Before, TwoOClock.AddMinutes(23))
            .Add(x => x.Scope, "last 24 hours"));

        IReadOnlyList<IElement> buckets = chart.FindAll("[data-testid=run-statistics-bucket]");
        buckets.Select(bucket => bucket.GetAttribute("data-start")).Should().Equal(
            ["2026-10-05T11:00:00.0000000+00:00", "2026-10-05T12:00:00.0000000+00:00", "2026-10-05T14:00:00.0000000+00:00"]);
        buckets.Select(bucket => bucket.GetAttribute("data-runs")).Should().Equal(["42", "16", "3"]);

        buckets[0].QuerySelectorAll("[data-testid=run-statistics-segment]").Select(segment => segment.GetAttribute("data-result"))
            .Should().Equal(["Failed", "Succeeded"], "a column stacks failures from the baseline up, and leaves out a result with no run");
        buckets[1].QuerySelectorAll("[data-testid=run-statistics-segment]").Select(segment => segment.GetAttribute("data-result"))
            .Should().Equal(["Cancelled", "Skipped", "Succeeded"]);
        buckets[0].QuerySelector("title")!.TextContent.Should().Be(
            "2026-10-05 11:00 · 42 runs: 40 succeeded, 2 failed · median 120 ms, 95th percentile 450 ms, longest 900 ms",
            "a hover leads with the values");

        chart.Find("[data-testid=run-statistics-runs]").GetAttribute("role").Should().Be("img");
        chart.Find("[data-testid=run-statistics-runs] desc").TextContent.Should().StartWith("61 runs from 2026-10-05 11:00: 53 succeeded, 5 skipped, 1 cancelled, 2 failed.");
        chart.Find("[data-testid=run-statistics-durations] desc").TextContent.Should().Contain("The 95th percentile was highest, 450 ms");

        chart.FindAll("path[data-testid=run-statistics-p95]").Should().ContainSingle(
            "11:00 and 12:00 are neighbours and are joined by a line");
        chart.FindAll("circle[data-testid=run-statistics-p95]").Should().ContainSingle(
            "13:00 holds no run, so 14:00 stands alone as a dot rather than being joined across the gap");

        chart.FindAll("[data-testid=run-statistics-table] tbody tr").Should().HaveCount(3, "every bucket is in the table too");
        chart.Find("[data-testid=run-statistics-summary]").TextContent.Should().Be("61 runs · 95.1 % succeeded · 2 failed · last 24 hours");
        chart.FindAll("[data-testid=run-statistics-truncated]").Should().BeEmpty();
    }

    [Test]
    public void AWindowWithNoRunSaysSoRatherThanDrawingEmptyAxes()
    {
        IRenderedComponent<RunStatisticsChart> chart = context.Render<RunStatisticsChart>(parameters => parameters
            .Add(x => x.Statistics, Hourly())
            .Add(x => x.Before, TwoOClock)
            .Add(x => x.Scope, "last hour"));

        chart.Find("[data-testid=run-statistics-empty]").TextContent.Should().Be("No runs recorded (last hour).");
        chart.FindAll("svg").Should().BeEmpty();
    }

    [Test]
    public void NothingIsDrawnWithoutStatistics()
    {
        IRenderedComponent<RunStatisticsChart> chart = context.Render<RunStatisticsChart>(parameters => parameters
            .Add(x => x.Statistics, null));

        chart.Markup.Trim().Should().BeEmpty();
    }

    [Test]
    public void AShortCountSaysTheOldestBucketsMayBeShort()
    {
        ExecutionStatistics statistics = Hourly(Bucket(TwoOClock, succeeded: 1, p50: 10, p95: 10, max: 10)) with { Truncated = true };

        IRenderedComponent<RunStatisticsChart> chart = context.Render<RunStatisticsChart>(parameters => parameters
            .Add(x => x.Statistics, statistics)
            .Add(x => x.Before, TwoOClock.AddMinutes(30))
            .Add(x => x.Scope, "all retained"));

        chart.Find("[data-testid=run-statistics-truncated]").TextContent.Should().Contain("newest 10,000 runs only");
    }

    /// <summary>
    /// The History page counts with every filter it lists with, over the window it lists, and its stat cards
    /// say which window they cover.
    /// </summary>
    [Test]
    public void TheHistoryPageChartsWhatItsFiltersList()
    {
        GivenOneListedRow();
        A.CallTo(() => context.Api.QueryExecutionStatistics(A<ExecutionStatisticsQuery>._, A<CancellationToken>._))
            .Returns(Hourly(Bucket(TwoOClock, succeeded: 3, failed: 1, p50: 100, p95: 200, max: 250)));

        context.Navigate("/quartz/history?job=night&trigger=midnight&node=node-b&outcome=failed&jobGroup=billing&jobName=nightly&result=failed&window=24h");
        IRenderedComponent<History> page = context.Render<History>();

        DateTimeOffset from = TwoOClock.AddHours(-24);
        page.WaitForAssertion(() =>
        {
            A.CallTo(() => context.Api.QueryExecutionStatistics(
                    A<ExecutionStatisticsQuery>.That.Matches(query =>
                        query.SchedulerName == TestData.SchedulerName
                        && query.SchedulerInstanceId == "node-b"
                        && query.JobContains == "night"
                        && query.TriggerContains == "midnight"
                        && query.FailedFinally == true
                        && new JobKey("nightly", "billing").Equals(query.Job)
                        && query.Results!.SequenceEqual(new[] { JobRunResult.Failed })
                        && query.FiredFrom == from
                        && query.BucketSize == TimeSpan.FromHours(1)),
                    A<CancellationToken>._))
                .MustHaveHappened();

            A.CallTo(() => context.Api.QueryExecutions(
                    A<DashboardHistoryQuery>.That.Matches(query => query.FiredFrom == from),
                    A<CancellationToken>._))
                .MustHaveHappened();

            page.Find("[data-testid=run-statistics]").Should().NotBeNull();
            page.StatCardValue("Runs (last 24 hours, node-b)").Should().Be("4");
            page.StatCardValue("Success rate (last 24 hours, node-b)").Should().Be("75.0 %");
            page.StatCardValue("Failed (last 24 hours, node-b)").Should().Be("1");
            page.StatCardValue("Longest run (last 24 hours, node-b)").Should().Be("250 ms");
            page.Markup.Should().Contain("Window: last 24 hours");
        });
    }

    [Test]
    public void ChoosingAWindowPutsItInTheUrl()
    {
        GivenOneListedRow();

        IRenderedComponent<History> page = context.Render<History>();
        page.Find("[data-testid=history-window-filter]").Change("7d");

        page.WaitForAssertion(() => context.CurrentUri.Should().EndWith("history?window=7d"));
    }

    /// <summary>
    /// The whole history is charted by the day, unless every run is from the last three days, when it is read
    /// again by the hour.
    /// </summary>
    [Test]
    public void TheWholeHistoryIsChartedByTheHourWhileItIsRecent()
    {
        GivenOneListedRow();
        A.CallTo(() => context.Api.QueryExecutionStatistics(
                A<ExecutionStatisticsQuery>.That.Matches(query => query.BucketSize == TimeSpan.FromDays(1)), A<CancellationToken>._))
            .Returns(new ExecutionStatistics { BucketSize = TimeSpan.FromDays(1), Buckets = [Bucket(Midnight, succeeded: 2, p50: 1, p95: 1, max: 1)] });
        A.CallTo(() => context.Api.QueryExecutionStatistics(
                A<ExecutionStatisticsQuery>.That.Matches(query => query.BucketSize == TimeSpan.FromHours(1)), A<CancellationToken>._))
            .Returns(Hourly(Bucket(TwoOClock.AddHours(-1), succeeded: 1, p50: 1, p95: 1, max: 1), Bucket(TwoOClock, succeeded: 1, p50: 1, p95: 1, max: 1)));

        IRenderedComponent<History> page = context.Render<History>();

        page.WaitForAssertion(() =>
        {
            page.FindAll("[data-testid=run-statistics-bucket]").Should().HaveCount(2, "a day's runs are charted hour by hour");
            page.StatCardValue("Runs (all retained)").Should().Be("2");
        });
        A.CallTo(() => context.Api.QueryExecutionStatistics(
                A<ExecutionStatisticsQuery>.That.Matches(query => query.FiredFrom == null), A<CancellationToken>._))
            .MustHaveHappenedTwiceOrMore();
    }

    [Test]
    public void ASourceThatCannotCountLeavesTheChartOutAndTheCardsOverThePage()
    {
        GivenOneListedRow();

        IRenderedComponent<History> page = context.Render<History>();

        page.FindAll("[data-testid=run-statistics]").Should().BeEmpty();
        page.StatCardValue("Success rate (page)").Should().Be("100.0 %",
            "with nothing to count the window with, the cards are over the rows on the page and say so");
    }

    [Test]
    public void TheJobDetailPageChartsThatJobAndItsWindow()
    {
        GivenJob();
        A.CallTo(() => context.Api.QueryExecutionStatistics(A<ExecutionStatisticsQuery>._, A<CancellationToken>._))
            .Returns(new ExecutionStatistics { BucketSize = TimeSpan.FromDays(1), Buckets = [Bucket(Midnight, succeeded: 2, p50: 1, p95: 1, max: 1)] });

        IRenderedComponent<JobDetail> page = context.Render<JobDetail>(parameters => parameters
            .Add(x => x.Group, "billing")
            .Add(x => x.Name, "release-stale"));

        page.WaitForAssertion(() => page.Find("#job-run-statistics").TextContent.Should().Be("Runs over time"));
        A.CallTo(() => context.Api.QueryExecutionStatistics(
                A<ExecutionStatisticsQuery>.That.Matches(query => new JobKey("release-stale", "billing").Equals(query.Job) && query.FiredFrom == null),
                A<CancellationToken>._))
            .MustHaveHappened();

        page.Find("[data-testid=job-run-window]").Change("30d");

        page.WaitForAssertion(() => A.CallTo(() => context.Api.QueryExecutionStatistics(
                A<ExecutionStatisticsQuery>.That.Matches(query =>
                    new JobKey("release-stale", "billing").Equals(query.Job)
                    && query.FiredFrom == new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero)
                    && query.BucketSize == TimeSpan.FromDays(1)),
                A<CancellationToken>._))
            .MustHaveHappened());
    }

    [Test]
    public void TheJobDetailPageLeavesTheChartOutWhereTheSourceCannotCount()
    {
        GivenJob();

        IRenderedComponent<JobDetail> page = context.Render<JobDetail>(parameters => parameters
            .Add(x => x.Group, "billing")
            .Add(x => x.Name, "release-stale"));

        page.WaitForAssertion(() => page.Markup.Should().Contain("JobDataMap"));
        page.FindAll("[data-testid=run-statistics]").Should().BeEmpty();
    }

    private void GivenOneListedRow()
    {
        A.CallTo(() => context.Api.QueryExecutions(A<DashboardHistoryQuery>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.Page<DashboardHistoryEntry>(
                [TestData.Dashboard.HistoryEntry(TimeSpan.FromMilliseconds(100), succeeded: true)], 1));
    }

    private void GivenJob()
    {
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

    private static ExecutionStatistics Hourly(params ExecutionStatisticsBucket[] buckets) => new()
    {
        BucketSize = TimeSpan.FromHours(1),
        Buckets = [.. buckets]
    };

    private static ExecutionStatisticsBucket Bucket(
        DateTimeOffset start,
        long succeeded = 0,
        long failed = 0,
        long cancelled = 0,
        long skipped = 0,
        int p50 = 0,
        int p95 = 0,
        int max = 0) => new(start)
    {
        SucceededCount = succeeded,
        FailedCount = failed,
        CancelledCount = cancelled,
        SkippedCount = skipped,
        P50Duration = TimeSpan.FromMilliseconds(p50),
        P95Duration = TimeSpan.FromMilliseconds(p95),
        MaxDuration = TimeSpan.FromMilliseconds(max)
    };
}
