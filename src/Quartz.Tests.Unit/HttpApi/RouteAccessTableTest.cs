using System.Text;

using Quartz.HttpApiContract;

namespace Quartz.Tests.Unit.HttpApi;

/// <summary>
/// The operator role each route needs: the table the HTTP API, the dashboard and the agent all check, held
/// as a snapshot so a change to it is a reviewed diff, and held to <see cref="WireRoute.Mutates" /> so a
/// route that changes something cannot be added without a level somebody chose.
/// </summary>
public class RouteAccessTableTest
{
    [Test]
    public async Task TheAccessTableIsSnapshotted()
    {
        StringBuilder rendered = new();
        foreach (WireRoute route in SchedulerRoutes.All)
        {
            rendered.Append(route.Access.ToString().PadRight(11))
                .Append(route.Name.PadRight(32))
                .Append(route.Method.PadRight(7))
                .AppendLine(route.Template);
        }

        await Verify(rendered.ToString(), extension: "txt")
            .UseDirectory("../Verify")
            .UseFileName("RouteAccessTableTest_Routes")
            .DisableRequireUniquePrefix();
    }

    /// <summary>
    /// A read is View and a change is Operate or Administer. A mutating route added with no level says View,
    /// which fails here.
    /// </summary>
    [Test]
    public void EveryRouteThatChangesSomethingNeedsMoreThanView()
    {
        List<string> wrong = SchedulerRoutes.All
            .Where(route => route.Mutates != route.Access > SchedulerAccess.View)
            .Select(route => $"{route.Name}: Mutates {route.Mutates}, Access {route.Access}")
            .ToList();

        wrong.Should().BeEmpty("a route that changes something states the role it needs, and a read needs View");
    }

    [Test]
    public void TheLevelsAreCountedAsTheIssueStatesThem()
    {
        SchedulerRoutes.All.Count(route => route.Access == SchedulerAccess.View).Should().Be(33);
        SchedulerRoutes.All.Count(route => route.Access == SchedulerAccess.Operate).Should().Be(22);
        SchedulerRoutes.All.Count(route => route.Access == SchedulerAccess.Administer).Should().Be(17);
    }

    /// <summary>
    /// Triggering a job now needs Administer with job data, because a stored <c>NativeJob</c> runs the command
    /// its job data names; data changes the level of no other route.
    /// </summary>
    [Test]
    public void JobDataRaisesTriggerJobAndNothingElse()
    {
        SchedulerRoutes.RequiredAccess(SchedulerRoutes.TriggerJob, carriesJobData: false).Should().Be(SchedulerAccess.Operate);
        SchedulerRoutes.RequiredAccess(SchedulerRoutes.TriggerJob, carriesJobData: true).Should().Be(SchedulerAccess.Administer);

        foreach (WireRoute route in SchedulerRoutes.All.Where(route => route != SchedulerRoutes.TriggerJob))
        {
            SchedulerRoutes.RequiredAccess(route, carriesJobData: true).Should().Be(route.Access, route.Name);
        }
    }

    [Test]
    public void AnEmptyMapIsNoJobData()
    {
        SchedulerRoutes.CarriesJobData(null).Should().BeFalse();
        SchedulerRoutes.CarriesJobData(new JobDataMap()).Should().BeFalse();
        SchedulerRoutes.CarriesJobData(new JobDataMap { ["command"] = "rm" }).Should().BeTrue();
    }

    [Test]
    public void ARouteIsFoundByItsNameIgnoringCase()
    {
        SchedulerRoutes.Find("deletejob").Should().BeSameAs(SchedulerRoutes.DeleteJob);
        SchedulerRoutes.Find("NoSuchRoute").Should().BeNull();
    }
}
