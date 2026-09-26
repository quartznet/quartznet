using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// One execution's own page — outcome, timings, what it threw and what it logged — and the History row
/// that leads to it.
/// </summary>
public class ExecutionDetailPageTest
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
    public void TheCapturedLogIsShownBesideTheOutcome()
    {
        GivenExecution(Entry() with
        {
            Succeeded = false,
            ExceptionMessage = "the upstream system is down",
            RetryAttempt = 2,
            Log = "2030-01-01T12:00:00.000Z info Reports.Nightly: starting\n2030-01-01T12:00:01.000Z fail Reports.Nightly: gave up"
        });

        IRenderedComponent<ExecutionDetail> page = Render("entry-1");

        page.WaitForAssertion(() =>
        {
            page.Find("[data-testid=execution-log]").TextContent.Should().Be(
                "2030-01-01T12:00:00.000Z info Reports.Nightly: starting\n2030-01-01T12:00:01.000Z fail Reports.Nightly: gave up",
                "the log is shown as the job wrote it, line breaks and all");
            page.Markup.Should().Contain("the upstream system is down");
            page.Markup.Should().Contain("retry 2", "which attempt this was is part of the outcome");
            page.Markup.Should().Contain("DummyGroup.DummyJob");
        });

        A.CallTo(() => context.Api.GetExecution(TestData.SchedulerName, "entry-1", A<CancellationToken>._))
            .MustHaveHappened();
    }

    [Test]
    public void AnExecutionThatLoggedNothingSaysHowToCaptureIt()
    {
        GivenExecution(Entry());

        IRenderedComponent<ExecutionDetail> page = Render("entry-1");

        page.WaitForAssertion(() =>
        {
            page.Find("[data-testid=execution-no-log]").TextContent.Should().Contain("UseExecutionLogCapture()",
                "an empty panel reads as a fault, and the reader needs to know capture is a choice");
            page.Markup.Should().Contain("the regular fire");
            page.FindAll("[data-testid=execution-log]").Should().BeEmpty();
        });
    }

    [Test]
    public void AnExecutionThatIsGoneIsSaidToBeGone()
    {
        A.CallTo(() => context.Api.GetExecution(A<string>._, A<string>._, A<CancellationToken>._))
            .Returns((DashboardHistoryEntry?) null);

        IRenderedComponent<ExecutionDetail> page = Render("trimmed");

        page.WaitForAssertion(() =>
            page.Find("[data-testid=execution-not-found]").TextContent.Should().Contain("trimmed from the history",
                "a row the retention sweep took is the ordinary reason a link from an old listing finds nothing"));
    }

    [Test]
    public void ATargetThatServesNoSingleExecutionsSaysSoWithoutARetry()
    {
        A.CallTo(() => context.Api.GetExecution(A<string>._, A<string>._, A<CancellationToken>._))
            .Throws(new NotSupportedException("the target is older than 4.3"));

        IRenderedComponent<ExecutionDetail> page = Render("entry-1");

        page.WaitForAssertion(() =>
        {
            page.Find("[data-testid=execution-unavailable]").TextContent.Should().Contain("does not serve single executions");
            page.FindAll("button").Should().BeEmpty("asking again will not make an older host serve the route");
        });
    }

    [Test]
    public void AReadThatFailsIsShownAsAnError()
    {
        A.CallTo(() => context.Api.GetExecution(A<string>._, A<string>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("the database went away"));

        IRenderedComponent<ExecutionDetail> page = Render("entry-1");

        page.WaitForAssertion(() => page.Markup.Should().Contain("the database went away"));
    }

    [Test]
    public void TheReadOnlyDashboardShowsTheSamePage()
    {
        using DashboardComponentContext readOnly = new(options => options.ReadOnly = true);
        readOnly.WithScheduler();
        A.CallTo(() => readOnly.Api.GetExecution(A<string>._, A<string>._, A<CancellationToken>._))
            .Returns(Entry() with { Log = "a captured line" });

        IRenderedComponent<ExecutionDetail> page = readOnly.Render<ExecutionDetail>(parameters => parameters.Add(p => p.EntryId, "entry-1"));

        page.WaitForAssertion(() =>
        {
            page.Find("[data-testid=execution-log]").TextContent.Should().Be("a captured line",
                "the page only reads, so read-only mode takes nothing from it");
            page.FindAll("button:not(.qz-key-badge-copy)").Should().BeEmpty(
                "nothing on the page changes a scheduler; the key badges' copy buttons only touch the clipboard");
        });
    }

    [Test]
    public void AHistoryRowLinksToItsExecution()
    {
        A.CallTo(() => context.Api.QueryExecutions(A<DashboardHistoryQuery>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.Page<DashboardHistoryEntry>([
                Entry() with { EntryId = "entry-1" },
                Entry() with { JobName = "unnamed", EntryId = null }
            ]));

        IRenderedComponent<History> page = context.Render<History>();

        page.WaitForAssertion(() =>
        {
            var links = page.FindAll("[data-testid=history-execution-link]");
            links.Should().ContainSingle("a row a store left unnamed has no page to open, so it gets no link");
            links[0].GetAttribute("href").Should().EndWith("history/entry-1");
        });
    }

    [Test]
    public async Task TheDefaultSingleReadPicksTheRowOutOfTheListing()
    {
        IQuartzApiClient client = A.Fake<IQuartzApiClient>(options => options.CallsBaseMethods());
        DashboardHistoryEntry wanted = Entry() with { EntryId = "entry-2", Log = "lines" };
        A.CallTo(() => client.QueryExecutions(A<DashboardHistoryQuery>._, A<CancellationToken>._))
            .Returns(TestData.Dashboard.Page<DashboardHistoryEntry>([Entry(), wanted]));

        (await client.GetExecution(TestData.SchedulerName, "entry-2")).Should().BeSameAs(wanted,
            "a client an application wrote against 4.2 still opens a row, with whatever its listing carries");
        (await client.GetExecution(TestData.SchedulerName, "missing")).Should().BeNull();

        A.CallTo(() => client.QueryExecutions(
                A<DashboardHistoryQuery>.That.Matches(q => q.SchedulerName == TestData.SchedulerName && q.Take == PagedQuery.All),
                A<CancellationToken>._))
            .MustHaveHappened();
    }

    private IRenderedComponent<ExecutionDetail> Render(string entryId)
    {
        return context.Render<ExecutionDetail>(parameters => parameters.Add(p => p.EntryId, entryId));
    }

    private void GivenExecution(DashboardHistoryEntry entry)
    {
        A.CallTo(() => context.Api.GetExecution(A<string>._, A<string>._, A<CancellationToken>._))
            .Returns(entry);
    }

    private static DashboardHistoryEntry Entry()
    {
        return TestData.Dashboard.HistoryEntry(TimeSpan.FromMilliseconds(1500)) with { EntryId = "entry-1" };
    }
}
