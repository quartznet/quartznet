using AngleSharp.Dom;

using Bunit;

using FakeItEasy;

using Quartz.Dashboard.Components.Pages;
using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The Schedulers page: the fleet, built or not, and what each one that exists is made of.
/// </summary>
/// <remarks>
/// This is the page that answers "what does this process run" without starting anything, so the two
/// things it must get right are that a registration nothing has built is <em>listed</em> rather than
/// silently missing, and that the reads it makes per scheduler are the ones that have an answer — a
/// node query against an in-memory store costs a round trip to be told there is one node.
/// </remarks>
public class SchedulersPageTest
{
    private DashboardComponentContext context = null!;

    [SetUp]
    public void SetUp()
    {
        context = new DashboardComponentContext();
    }

    [TearDown]
    public void TearDown()
    {
        context.Dispose();
    }

    [Test]
    public void ARegistrationNothingHasBuiltIsListedWithNoMetadataRatherThanOmitted()
    {
        GivenSchedulers(TestData.Dashboard.RegisteredSchedulerHeader("acme"));

        IRenderedComponent<Schedulers> page = context.Render<Schedulers>();

        List<string> cells = RowCells(page, rowIndex: 0);
        cells[0].Should().Be("acme",
            "a tenant the container knows about is a tenant an operator came here to see, whether or not "
            + "anything has built it");
        cells[2].Should().Contain("Not created");
        cells.Skip(3).Should().AllBe("—",
            "there is no scheduler to read metadata from, and a blank cell reads as a rendering fault");

        A.CallTo(() => context.Api.GetScheduler("acme", A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public void ABuiltSchedulerShowsWhatItIsMadeOf()
    {
        GivenSchedulers(TestData.Dashboard.SchedulerHeader("core"));
        GivenDetail("core", TestData.Dashboard.SchedulerDetail(SchedulerStatus.Running, "core"));

        IRenderedComponent<Schedulers> page = context.Render<Schedulers>();

        List<string> cells = RowCells(page, rowIndex: 0);
        cells[0].Should().Contain("core").And.Contain(TestData.SchedulerInstanceId);
        cells[1].Should().Be("Container");
        cells[2].Should().Contain("Running");
        cells[3].Should().Contain("RAMJobStore").And.Contain("in-memory");
        cells[4].Should().Be("10", "the pool size is the number an operator sizes a scheduler by");
        cells[5].Should().Be("2024-05-06 07:08:09 +00:00", "times are rendered in the zone the user picked");
        cells[6].Should().Be("42");
        cells[8].Should().Be("4.0.0.0");
    }

    [Test]
    public void OnlyAPersistentClusteredSchedulerIsAskedForItsNodes()
    {
        GivenSchedulers(
            TestData.Dashboard.SchedulerHeader("core"),
            TestData.Dashboard.SchedulerHeader("reporting"));
        GivenDetail("core", TestData.Dashboard.SchedulerDetail(SchedulerStatus.Running, "core", clustered: true, persistent: true));
        GivenDetail("reporting", TestData.Dashboard.SchedulerDetail(SchedulerStatus.Running, "reporting"));
        A.CallTo(() => context.Api.QueryClusterNodes("core", A<CancellationToken>._))
            .Returns(new List<ClusterNodeDto> { Node("node-a", isCurrent: true), Node("node-b", isCurrent: false) });

        IRenderedComponent<Schedulers> page = context.Render<Schedulers>();

        RowCells(page, rowIndex: 0)[7].Should().Be("2");
        RowCells(page, rowIndex: 1)[7].Should().Be("—",
            "a store with no node table answers with the one node it is, so asking is a round trip per "
            + "scheduler for a number that is always one");

        A.CallTo(() => context.Api.QueryClusterNodes("reporting", A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public void FollowingASchedulersLinkMakesItTheActiveOne()
    {
        GivenSchedulers(
            TestData.Dashboard.SchedulerHeader("core"),
            TestData.Dashboard.SchedulerHeader("reporting"));
        GivenDetail("core", TestData.Dashboard.SchedulerDetail(SchedulerStatus.Running, "core"));
        GivenDetail("reporting", TestData.Dashboard.SchedulerDetail(SchedulerStatus.Standby, "reporting"));

        IRenderedComponent<Schedulers> page = context.Render<Schedulers>();
        page.FindAll("tbody tr")[1].QuerySelector("a")!.Click();

        context.SchedulerState.ActiveSchedulerName.Should().Be("reporting",
            "the row is how an operator switches the dashboard to a scheduler, which is the only reason "
            + "to list them all in one place");
    }

    [Test]
    public void ARegistrationNothingHasBuiltIsNotALink()
    {
        GivenSchedulers(TestData.Dashboard.RegisteredSchedulerHeader("acme"));

        IRenderedComponent<Schedulers> page = context.Render<Schedulers>();

        page.FindAll("tbody tr")[0].QuerySelectorAll("a").Should().BeEmpty(
            "there is no scheduler behind the name, so every page the link leads to would report a "
            + "scheduler it could not find");
    }

    /// <summary>
    /// One scheduler that cannot answer must not blank the fleet.
    /// </summary>
    [Test]
    public void ASchedulerThatCannotBeReadIsReportedInItsOwnRow()
    {
        GivenSchedulers(
            TestData.Dashboard.SchedulerHeader("core"),
            TestData.Dashboard.SchedulerHeader("remote"));
        GivenDetail("core", TestData.Dashboard.SchedulerDetail(SchedulerStatus.Running, "core"));
        A.CallTo(() => context.Api.GetScheduler("remote", A<CancellationToken>._))
            .Throws(new InvalidOperationException("the other process is not answering"));

        IRenderedComponent<Schedulers> page = context.Render<Schedulers>();

        RowCells(page, rowIndex: 1)[2].Should().Contain("the other process is not answering");
        RowCells(page, rowIndex: 0)[6].Should().Be("42",
            "the scheduler that did answer is still shown, which is the whole point of a fleet view");
    }

    [Test]
    public void AProcessWithNoSchedulersSaysSoRatherThanShowingAnEmptyTable()
    {
        GivenSchedulers();

        IRenderedComponent<Schedulers> page = context.Render<Schedulers>();

        page.Markup.Should().Contain("No schedulers registered.");
    }

    private static ClusterNodeDto Node(string instanceId, bool isCurrent)
    {
        return new ClusterNodeDto(
            instanceId,
            TestData.Dashboard.FiredAt,
            TimeSpan.FromSeconds(15),
            ClusterNodeState.Alive,
            isCurrent);
    }

    private static List<string> RowCells(IRenderedComponent<Schedulers> page, int rowIndex)
    {
        List<string> cells = [];
        foreach (IElement cell in page.FindAll("tbody tr")[rowIndex].QuerySelectorAll("td"))
        {
            cells.Add(cell.TextContent.Trim());
        }

        return cells;
    }

    private void GivenSchedulers(params SchedulerHeaderDto[] schedulers)
    {
        A.CallTo(() => context.Api.GetSchedulers(A<CancellationToken>._)).Returns(schedulers.ToList());
    }

    private void GivenDetail(string schedulerName, SchedulerDetailDto detail)
    {
        A.CallTo(() => context.Api.GetScheduler(schedulerName, A<CancellationToken>._)).Returns(detail);
    }
}
