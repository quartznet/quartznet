using System.Text.RegularExpressions;

namespace Quartz.Tests.Unit;

/// <summary>
/// The C# on the Wolverine how-to page is the C# in <c>src/Quartz.Examples.Wolverine</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every other page's samples are generated: they live as <c>#region sample_*</c> blocks in
/// <c>Quartz.Documentation.Samples</c> and <c>VerifyDocsSnippets</c> fails a page that has drifted from
/// them. That machinery is not available here, because <c>DocsSnippets</c> reads that one project and
/// that project may not take a <c>WolverineFx</c> dependency for the sake of a sample — the rule
/// <c>CONTRIBUTING.md</c> states under "Code samples in the documentation", and the reason the Aspire
/// how-to's AppHost blocks are hand-written too.
/// </para>
/// <para>
/// So the page carries plain fences, and this test is what <c>VerifyDocsSnippets</c> would otherwise
/// have been: each fence names the example file and line it was copied from, and the two are compared
/// verbatim. It is deliberately not the anti-pattern
/// <see cref="Configuration.DocumentedConfigurationTest" />'s remarks describe — that was a test
/// holding its own transcription of a page, so that the page and the test were two copies with nothing
/// comparing them. Here the compiled example is the only copy, and the page is checked against it.
/// </para>
/// </remarks>
public class WolverineHowToTest
{
    private const string PagePath = "docs/documentation/quartz-4.x/how-tos/wolverine.md";

    private const string ExampleDirectory = "src/Quartz.Examples.Wolverine";

    /// <summary>
    /// How many fences the page is expected to carry, so that deleting them all cannot make this test
    /// pass by having nothing to check.
    /// </summary>
    private const int LeastExpectedFences = 6;

    /// <summary>
    /// The provenance comment and the fence it introduces — <c>Copied from &lt;path&gt;:&lt;line&gt;</c>
    /// anywhere inside an HTML comment, then a <c>csharp</c> fence.
    /// </summary>
    private static readonly Regex AttributedFence = new(
        @"<!--(?<comment>(?:(?!-->).)*?)Copied from (?<path>src/Quartz\.Examples\.Wolverine/[^\s:]+):(?<line>\d+)(?:(?!-->).)*?-->\s*\r?\n```csharp\r?\n(?<code>.*?)\r?\n```",
        RegexOptions.Singleline,
        TimeSpan.FromSeconds(10));

    /// <summary>
    /// Every fenced C# block on the page, attributed or not.
    /// </summary>
    private static readonly Regex CsharpFence = new(
        @"^```csharp\r?$",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(10));

    [Test]
    public void EveryFencedSampleAppearsVerbatimInTheExample()
    {
        DirectoryInfo root = RepositoryRoot.Find();
        string page = ReadPage(root);

        MatchCollection matches = AttributedFence.Matches(page);
        matches.Count.Should().BeGreaterThanOrEqualTo(LeastExpectedFences,
            $"{PagePath} teaches the six parts of {ExampleDirectory}, and a page that has stopped quoting them is a page nobody is checking");

        foreach (Match match in matches)
        {
            string relative = match.Groups["path"].Value;
            int firstLine = int.Parse(match.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture);

            FileInfo source = new(Path.Combine(root.FullName, relative.Replace('/', Path.DirectorySeparatorChar)));
            source.Exists.Should().BeTrue($"{PagePath} says it copied a sample from {relative}");

            string[] sourceLines = File.ReadAllLines(source.FullName);
            string[] pageLines = Dedent(match.Groups["code"].Value.Replace("\r\n", "\n").Split('\n'));

            (firstLine + pageLines.Length - 1).Should().BeLessThanOrEqualTo(sourceLines.Length,
                $"{relative} is shorter than the block {PagePath} says starts at line {firstLine}");

            string[] candidate = Dedent(sourceLines.Skip(firstLine - 1).Take(pageLines.Length).ToArray());

            for (int i = 0; i < pageLines.Length; i++)
            {
                candidate[i].Should().Be(pageLines[i],
                    $"{PagePath} quotes {relative}:{firstLine + i}. Copy the block again from the example, and correct the line number in the page's provenance comment if it moved: {Locate(sourceLines, pageLines)}");
            }
        }
    }

    [Test]
    public void EveryFencedSampleSaysWhereItCameFrom()
    {
        DirectoryInfo root = RepositoryRoot.Find();
        string page = ReadPage(root);

        CsharpFence.Matches(page).Count.Should().Be(AttributedFence.Matches(page).Count,
            $"every csharp fence on {PagePath} is hand-written, so each one needs the comment naming the {ExampleDirectory} file and line it was copied from — otherwise nothing keeps it honest");
    }

    private static string ReadPage(DirectoryInfo root)
    {
        FileInfo file = new(Path.Combine(root.FullName, PagePath.Replace('/', Path.DirectorySeparatorChar)));
        file.Exists.Should().BeTrue($"{PagePath} is the page this test exists for");

        return File.ReadAllText(file.FullName);
    }

    /// <summary>
    /// Removes the indentation the block carries as a whole, so a method body reads on the page the way
    /// a generated snippet would.
    /// </summary>
    private static string[] Dedent(string[] lines)
    {
        string[] trimmed = lines.Select(x => x.TrimEnd()).ToArray();

        int indent = trimmed
            .Where(x => x.Length > 0)
            .Select(x => x.Length - x.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();

        return trimmed.Select(x => x.Length == 0 ? x : x[indent..]).ToArray();
    }

    /// <summary>
    /// Where the quoted block really begins, so a failure names the line to write rather than only the
    /// line that is wrong.
    /// </summary>
    private static string Locate(string[] sourceLines, string[] pageLines)
    {
        for (int start = 0; start + pageLines.Length <= sourceLines.Length; start++)
        {
            string[] candidate = Dedent(sourceLines.Skip(start).Take(pageLines.Length).ToArray());
            if (candidate.SequenceEqual(pageLines, StringComparer.Ordinal))
            {
                return $"the block now starts at line {start + 1}";
            }
        }

        return "the block is no longer in that file at all";
    }
}
