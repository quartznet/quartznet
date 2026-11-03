using System.Text.RegularExpressions;

using Quartz.HttpApiContract;

namespace Quartz.Tests.Unit.HttpApi;

/// <summary>
/// The access tables under "Operator roles" on the HTTP API and dashboard pages are the route table's: every
/// operation a row names needs the role the row states, and the HTTP API's table names every operation that
/// needs more than View.
/// </summary>
/// <remarks>
/// A row's role is the one backticked level it names, and its operations are the backticked words that are
/// route names; a row that says <c>with job data</c> asks <see cref="SchedulerRoutes.RequiredAccess" /> with
/// data. So a route moved to another level, or added at one, fails here until the page says so.
/// </remarks>
public partial class OperationAccessDocsTest
{
    private const string HttpApiPage = "docs/documentation/quartz-4.x/packages/http-api.md";
    private const string DashboardPage = "docs/documentation/quartz-4.x/packages/dashboard.md";

    [TestCase(HttpApiPage)]
    [TestCase(DashboardPage)]
    public void EveryOperationARowNamesNeedsTheRowsRole(string page)
    {
        List<Row> rows = Rows(page);
        rows.Should().NotBeEmpty($"{page} has an access table under its Operator roles heading");

        List<string> wrong = [];
        foreach (Row row in rows)
        {
            foreach (WireRoute route in row.Operations)
            {
                SchedulerAccess required = SchedulerRoutes.RequiredAccess(route, row.WithJobData);
                if (required != row.Role)
                {
                    wrong.Add($"{route.Name} is in the {row.Role} row and needs {required}");
                }
            }
        }

        wrong.Should().BeEmpty($"the table on {page} is the route table's");
    }

    [Test]
    public void TheHttpApiTableNamesEveryOperationAboveView()
    {
        List<Row> rows = Rows(HttpApiPage);
        HashSet<string> named = rows.SelectMany(row => row.Operations).Select(route => route.Name).ToHashSet(StringComparer.Ordinal);

        List<string> missing = SchedulerRoutes.All
            .Where(route => route.Access > SchedulerAccess.View || (route.Method != "GET" && route.Access == SchedulerAccess.View))
            .Select(route => route.Name)
            .Where(name => !named.Contains(name))
            .ToList();

        missing.Should().BeEmpty("every change, and every read that is not a GET, is named in the HTTP API's table");

        rows.Should().Contain(row => row.Role == SchedulerAccess.Administer && row.WithJobData
                                     && row.Operations.Contains(SchedulerRoutes.TriggerJob),
            "triggering with job data is Administer, and the table says so");
    }

    private static List<Row> Rows(string page)
    {
        string[] lines = File.ReadAllLines(Path.Combine(RepositoryRoot.Find().FullName, page));
        int start = Array.FindIndex(lines, line => line.TrimStart('#', ' ') == "Operator roles" && line.StartsWith('#'));
        start.Should().BeGreaterThanOrEqualTo(0, $"{page} has an Operator roles heading");

        List<Row> rows = [];
        for (int i = start + 1; i < lines.Length && !lines[i].StartsWith('#'); i++)
        {
            string line = lines[i];
            if (!line.StartsWith('|') || line.StartsWith("|---", StringComparison.Ordinal))
            {
                continue;
            }

            List<string> words = Backticked().Matches(line).Select(match => match.Groups[1].Value).ToList();
            List<SchedulerAccess> roles = words
                .Select(word => Enum.TryParse(word, out SchedulerAccess access) && Enum.IsDefined(access) ? (SchedulerAccess?) access : null)
                .OfType<SchedulerAccess>()
                .ToList();
            if (roles.Count != 1)
            {
                continue;
            }

            List<WireRoute> operations = words.Select(word => SchedulerRoutes.Find(word)).OfType<WireRoute>().ToList();
            rows.Add(new Row(roles[0], operations, line.Contains("with job data", StringComparison.Ordinal)));
        }

        return rows;
    }

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex Backticked();

    private sealed record Row(SchedulerAccess Role, List<WireRoute> Operations, bool WithJobData);
}
