using Quartz.HttpApiContract;

namespace Quartz.Tests.Unit.HttpApi;

/// <summary>
/// The route table: every route is one path a client fills in and a carrier routes back, and no path is
/// a call of two routes.
/// </summary>
public class SchedulerRoutesTest
{
    [Test]
    public void EveryRouteHasANameOfItsOwn()
    {
        SchedulerRoutes.All.Select(route => route.Name).Should().OnlyHaveUniqueItems(
            "a route's name is its endpoint's name and its OpenAPI operation id, and ASP.NET Core refuses two endpoints of one name");
    }

    /// <summary>
    /// Two routes of one method and one length could share a path unless some segment is a literal in
    /// both and differs. None do, which is why <see cref="SchedulerRoutes.Match" /> can answer with the
    /// first route that matches and decide no precedence.
    /// </summary>
    [Test]
    public void NoPathIsACallOfTwoRoutes()
    {
        List<string> ambiguous = [];
        foreach (WireRoute first in SchedulerRoutes.All)
        {
            foreach (WireRoute second in SchedulerRoutes.All.SkipWhile(route => route != first).Skip(1))
            {
                if (CanShareAPath(first, second))
                {
                    ambiguous.Add($"{first} / {second}");
                }
            }
        }

        ambiguous.Should().BeEmpty(
            "a path two routes match would be routed by precedence in ASP.NET Core and by table order in the catalogue; give the new route a literal of its own");

        static bool CanShareAPath(WireRoute first, WireRoute second)
        {
            string[] a = first.Template.Split('/');
            string[] b = second.Template.Split('/');
            if (first.Method != second.Method || a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (!a[i].StartsWith('{') && !b[i].StartsWith('{') && !string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
    }

    [Test]
    public void ARouteIsFilledInWithItsValuesInOrder()
    {
        WireRequest request = SchedulerRoutes.PauseJob.For("reporting", "exports", "nightly");

        request.Route.Should().BeSameAs(SchedulerRoutes.PauseJob);
        request.Path.Should().Be("schedulers/reporting/jobs/exports/nightly/pause");
        request.Body.Should().BeNull("a request made of a route carries no body until the client gives it one");
        SchedulerRoutes.PauseJob.Parameters.Should().Equal(["schedulerName", "jobGroup", "jobName"]);
    }

    /// <summary>
    /// A value is escaped into its segment, so a character that means something in a URL arrives as written
    /// once the server unescapes the path (#3917). A value that needs no escaping goes in unchanged.
    /// </summary>
    [Test]
    public void AValueIsEscapedIntoItsSegment()
    {
        WireRequest request = SchedulerRoutes.PauseJob.For("reporting", "night shift?#", "100%&a+b");

        request.Path.Should().Be("schedulers/reporting/jobs/night%20shift%3F%23/100%25%26a%2Bb/pause");
        SchedulerRoutes.Match("POST", request.Path)!.Value.Values.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["schedulerName"] = "reporting",
            ["jobGroup"] = "night shift?#",
            ["jobName"] = "100%&a+b"
        }, "unescaping the path gives back what was written");

        SchedulerRoutes.PauseJob.For("reporting", "v1.2~x", "a-b_c").Path.Should().Be("schedulers/reporting/jobs/v1.2~x/a-b_c/pause",
            "letters, digits and -._~ need no escaping, so a path made of them is the one the client always sent");
    }

    /// <summary>
    /// A value no escaping brings back is refused before anything is sent: an escaped <c>/</c> stays escaped
    /// through ASP.NET Core's routing, and a dot segment is removed from the path before it.
    /// </summary>
    [TestCase("reports/2026", "*contains '/'*")]
    [TestCase(".", "*a path segment of '.' is removed before routing*")]
    [TestCase("..", "*a path segment of '..' is removed before routing*")]
    public void AValueThePathCannotCarryIsRefused(string group, string reason)
    {
        Action act = () => SchedulerRoutes.PauseJob.For("reporting", group, "nightly");

        act.Should().Throw<ArgumentException>()
            .WithMessage($"The jobGroup '{group}' cannot be sent in the path of PauseJob*")
            .Which.ParamName.Should().Be("jobGroup", "the route's parameter says which part of the key it was");
        act.Should().Throw<ArgumentException>().WithMessage(reason);
    }

    [Test]
    public void ARouteRefusesTheWrongNumberOfValues()
    {
        Action act = () => SchedulerRoutes.PauseJob.For("reporting");

        act.Should().Throw<ArgumentException>().WithMessage("Route PauseJob takes 3 values (schedulerName, jobGroup, jobName), was given 1.*",
            "a value left out would put the next one in the wrong segment of the path");
    }

    [Test]
    public void AQueryIsAppendedAsGiven()
    {
        WireRequest request = SchedulerRoutes.PauseJobs.For("reporting").WithQuery("?");

        request.Path.Should().Be("schedulers/reporting/jobs/pause?",
            "a matcher that names no group has always gone out as an empty query, and the client keeps sending what it sent");
    }

    [Test]
    public void MatchReadsTheValuesOutOfThePath()
    {
        (WireRoute Route, Dictionary<string, string> Values)? match =
            SchedulerRoutes.Match("post", "schedulers/reporting/jobs/night%20shift/nightly/pause?reason=ignored");

        match.Should().NotBeNull();
        match!.Value.Route.Should().BeSameAs(SchedulerRoutes.PauseJob);
        match.Value.Values.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["schedulerName"] = "reporting",
            ["jobGroup"] = "night shift",
            ["jobName"] = "nightly"
        }, "the values are unescaped and the query string takes no part, as in ASP.NET Core");
    }

    [TestCase("POST", "SCHEDULERS/reporting/JOBS/fetch", "FetchJobs")]
    [TestCase("GET", "/schedulers/reporting/jobs/", "QueryJobs")]
    [TestCase("GET", "schedulers", "GetAllSchedulers")]
    [TestCase("GET", "schedulers/reporting/jobs/groups/exports/paused", "IsJobGroupPaused")]
    [TestCase("GET", "schedulers/reporting/jobs/exports/nightly", "GetJobDetails")]
    [TestCase("DELETE", "schedulers/reporting/jobs/exports/nightly", "DeleteJob")]
    [TestCase("GET", "schedulers/reporting/history/misfires/count", "CountMisfires")]
    [TestCase("GET", "schedulers/reporting/history/job-status", "QueryJobRunStatuses")]
    [TestCase("GET", "schedulers/reporting/history/job-status/exports/nightly", "GetJobRunStatus")]
    [TestCase("POST", "schedulers/reporting/history/job-status/fetch", "FetchJobRunStatuses")]
    [TestCase("GET", "schedulers/reporting/history/statistics?bucket=01:00:00", "QueryExecutionStatistics")]
    public void MatchRoutesAPathAsAspNetCoreDoes(string method, string path, string routeName)
    {
        string matched = SchedulerRoutes.Match(method, path)?.Route.Name;

        matched.Should().Be(routeName, "literals compare ignoring case and a leading or trailing slash takes no part");
    }

    [TestCase("GET", "schedulers/reporting/nothing-here")]
    [TestCase("DELETE", "schedulers/reporting/start")]
    [TestCase("GET", "schedulers/reporting/jobs/exports/nightly/exists/more")]
    [TestCase("GET", "schedulers//jobs")]
    [TestCase("GET", "")]
    [TestCase("GET", "schedulers/reporting/history/job-status/fetch")]
    public void MatchAnswersNullForAPathTheTableDoesNotHave(string method, string path)
    {
        SchedulerRoutes.Match(method, path).Should().BeNull($"{method} {path} is no route of the table");
    }

    [Test]
    public void ARouteNeedsANameAMethodAndATemplate()
    {
        Action nameless = () => _ = new WireRoute(" ", "GET", "schedulers");
        Action methodless = () => _ = new WireRoute("Name", "", "schedulers");

        nameless.Should().Throw<ArgumentException>();
        methodless.Should().Throw<ArgumentException>();
    }
}
