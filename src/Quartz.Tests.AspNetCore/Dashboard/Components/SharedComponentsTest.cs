using System.Text.RegularExpressions;

using AngleSharp.Dom;

using Bunit;

using Quartz.Dashboard.Components.Shared;

namespace Quartz.Tests.AspNetCore.Dashboard.Components;

/// <summary>
/// The pieces every page is built out of.
/// </summary>
public class SharedComponentsTest
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
    public void TheKeyBadgeShowsTheKeyTheWayQuartzWritesIt()
    {
        IRenderedComponent<KeyBadge> badge = context.Render<KeyBadge>(parameters => parameters
            .Add(x => x.GroupName, "reports")
            .Add(x => x.ItemName, "nightly"));

        badge.Find(".qz-key-badge-value").TextContent.Should().Be("reports.nightly");
        badge.Find(".qz-key-badge").GetAttribute("title").Should().Be("reports.nightly",
            "the whole key is on hover as well");
    }

    /// <summary>
    /// A listing never cuts a key short: one too long for its column wraps, between the key's words, which the badge
    /// marks as break opportunities, and only a word longer than the column breaks inside. Two keys that differ only
    /// at their end stay apart.
    /// </summary>
    [Test]
    public void AListingWrapsAKeyItCannotFitRatherThanCuttingIt()
    {
        IRenderedComponent<KeyBadge> badge = context.Render<KeyBadge>(parameters => parameters
            .Add(x => x.GroupName, "demo")
            .Add(x => x.ItemName, "demoBlockingJobTrigger2"));

        badge.Find(".qz-key-badge-value").InnerHtml.Should().Be("demo.<wbr>demo<wbr>Blocking<wbr>Job<wbr>Trigger2",
            "the line breaks after the group's dot and between the name's words, not wherever it runs out");
        badge.Find(".qz-key-badge-value").TextContent.Should().Be("demo.demoBlockingJobTrigger2", "the breaks add no text");

        string listingKey = Rule(File.ReadAllText(DashboardStylesheet()), ".qz-entity-table td.qz-col-key .qz-key-badge-value");
        listingKey.Should().Contain("white-space: normal;").And.Contain("overflow-wrap: anywhere;",
            "the Jobs and Triggers listings wrap a key, inside its name when it must, and never end it with an ellipsis");
        listingKey.Should().Contain("text-overflow: clip;");
    }

    /// <summary>
    /// Where a key may break: after a separator, which stays on the line it ends; before a capital that follows a
    /// lower-case letter or a digit; and before the last capital of an upper-case run, so an acronym stays whole.
    /// </summary>
    [TestCase("demo", "demoBlockingJobTrigger", "demo.|demo|Blocking|Job|Trigger", TestName = "AKeyBreaksBetweenTheWordsOfACamelCaseName")]
    [TestCase("reports", "FlakyReportHourly", "reports.|Flaky|Report|Hourly", TestName = "AKeyBreaksBetweenTheWordsOfAPascalCaseName")]
    [TestCase("batch", "nightly_report_job", "batch.|nightly_|report_|job", TestName = "AKeyBreaksAfterEachUnderscore")]
    [TestCase("batch", "nightly-report-job", "batch.|nightly-|report-|job", TestName = "AKeyBreaksAfterEachHyphen")]
    [TestCase("billing", "ledger.month.close", "billing.|ledger.|month.|close", TestName = "AKeyBreaksAfterEachDotOfADottedName")]
    [TestCase("imports", "XMLParserJob", "imports.|XML|Parser|Job", TestName = "AKeyKeepsAnAcronymWholeAndBreaksBeforeTheWordAfterIt")]
    [TestCase("imports", "job2Report2024Q4", "imports.|job2|Report2024|Q4", TestName = "AKeyBreaksBeforeACapitalAfterADigitButNotBeforeADigit")]
    [TestCase("DEFAULT", "Combined Configuration Trigger", "DEFAULT.|Combined |Configuration |Trigger", TestName = "AKeyBreaksAfterEachSpace")]
    [TestCase("ops", "exports/daily", "ops.|exports/|daily", TestName = "AKeyBreaksAfterASlash")]
    [TestCase("UPPER", "NIGHTLY", "UPPER.|NIGHTLY", TestName = "AnAllCapitalKeyBreaksOnlyAfterItsGroup")]
    public void AKeyMayBreakOnlyBetweenItsWords(string group, string name, string pieces)
    {
        IRenderedComponent<KeyBadge> badge = context.Render<KeyBadge>(parameters => parameters
            .Add(x => x.GroupName, group)
            .Add(x => x.ItemName, name));

        IElement value = badge.Find(".qz-key-badge-value");
        value.InnerHtml.Should().Be(pieces.Replace("|", "<wbr>", StringComparison.Ordinal));
        value.TextContent.Should().Be(group + "." + name, "the text is the key exactly");
        badge.Find(".qz-key-badge").GetAttribute("title").Should().Be(group + "." + name, "and so is the title");
    }

    private static string Rule(string css, string selector)
    {
        Match rule = Regex.Match(css, "(?m)^" + Regex.Escape(selector) + @" \{([^}]*)\}");
        rule.Success.Should().BeTrue("the stylesheet defines " + selector);
        return rule.Groups[1].Value;
    }

    private static string DashboardStylesheet()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "src", "Quartz.Dashboard", "wwwroot", "css", "quartz-dashboard.css");
            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("quartz-dashboard.css was not found above " + AppContext.BaseDirectory);
    }
}
