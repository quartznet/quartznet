using System.Globalization;
using System.Text.RegularExpressions;

namespace Quartz.Tests.Unit;

/// <summary>
/// The check behind <see cref="WolverineHowToTest" /> and <see cref="FSharpHowToTest" />: a page whose
/// fenced samples are hand-written, held to the example project each one says it was copied from.
/// </summary>
/// <remarks>
/// <para>
/// Every other page's samples are generated: they live as <c>#region sample_*</c> blocks in
/// <c>Quartz.Documentation.Samples</c> and <c>VerifyDocsSnippets</c> fails a page that has drifted from
/// them. Two pages cannot use that machinery — <c>DocsSnippets</c> reads that one project, and it may
/// neither take a <c>WolverineFx</c> dependency for the sake of a sample nor hold a line of F#, it being
/// a C# project. This is what <c>VerifyDocsSnippets</c> would otherwise have been for them: each fence
/// names the example file and line it was copied from, and the two are compared verbatim.
/// </para>
/// <para>
/// It is deliberately not the anti-pattern <see cref="Configuration.DocumentedConfigurationTest" />'s
/// remarks describe — that was a test holding its own transcription of a page, so that the page and the
/// test were two copies with nothing comparing them. Here the compiled example is the only copy, and
/// the page is checked against it.
/// </para>
/// </remarks>
internal static class AttributedSamples
{
    /// <summary>
    /// The provenance comment and the fence it introduces — <c>Copied from &lt;path&gt;:&lt;line&gt;</c>
    /// anywhere inside an HTML comment, then a fence in the page's sample language.
    /// </summary>
    private static Regex AttributedFence(string exampleDirectory, string language) => new(
        $@"<!--(?:(?!-->).)*?Copied from (?<path>{Regex.Escape(exampleDirectory)}/[^\s:]+):(?<line>\d+)(?:(?!-->).)*?-->\s*\r?\n```{Regex.Escape(language)}\r?\n(?<code>.*?)\r?\n```",
        RegexOptions.Singleline,
        TimeSpan.FromSeconds(10));

    /// <summary>
    /// Every fenced block on the page in that language, attributed or not.
    /// </summary>
    private static Regex AnyFence(string language) => new(
        $@"^```{Regex.Escape(language)}\r?$",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(10));

    /// <summary>
    /// Asserts that each attributed fence on the page is, line for line, what the example file holds at
    /// the line the page names.
    /// </summary>
    /// <param name="pagePath">The page, relative to the repository root, with forward slashes.</param>
    /// <param name="exampleDirectory">The example project the page quotes, in the same form.</param>
    /// <param name="language">The fence's language tag, which is the example project's language.</param>
    /// <param name="leastExpectedFences">
    /// How many fences the page is expected to carry, so that deleting them all cannot make the check
    /// pass by having nothing left to compare.
    /// </param>
    public static void EachFenceAppearsVerbatimInTheExample(
        string pagePath,
        string exampleDirectory,
        string language,
        int leastExpectedFences)
    {
        DirectoryInfo root = RepositoryRoot.Find();
        string page = ReadPage(root, pagePath);

        MatchCollection matches = AttributedFence(exampleDirectory, language).Matches(page);
        matches.Count.Should().BeGreaterThanOrEqualTo(leastExpectedFences,
            $"{pagePath} teaches {exampleDirectory}, and a page that has stopped quoting it is a page nobody is checking");

        foreach (Match match in matches)
        {
            string relative = match.Groups["path"].Value;
            int firstLine = int.Parse(match.Groups["line"].Value, CultureInfo.InvariantCulture);

            FileInfo source = new(Path.Combine(root.FullName, relative.Replace('/', Path.DirectorySeparatorChar)));
            source.Exists.Should().BeTrue($"{pagePath} says it copied a sample from {relative}");

            string[] sourceLines = File.ReadAllLines(source.FullName);
            string[] pageLines = Dedent(match.Groups["code"].Value.Replace("\r\n", "\n").Split('\n'));

            (firstLine + pageLines.Length - 1).Should().BeLessThanOrEqualTo(sourceLines.Length,
                $"{relative} is shorter than the block {pagePath} says starts at line {firstLine}");

            string[] candidate = Dedent(sourceLines.Skip(firstLine - 1).Take(pageLines.Length).ToArray());

            for (int i = 0; i < pageLines.Length; i++)
            {
                candidate[i].Should().Be(pageLines[i],
                    $"{pagePath} quotes {relative}:{firstLine + i}. Copy the block again from the example, and correct the line number in the page's provenance comment if it moved: {Locate(sourceLines, pageLines)}");
            }
        }
    }

    /// <summary>
    /// Asserts that the page carries no fence in that language without a provenance comment, since an
    /// unattributed one is a sample nothing compares against anything.
    /// </summary>
    public static void EachFenceSaysWhereItCameFrom(string pagePath, string exampleDirectory, string language)
    {
        DirectoryInfo root = RepositoryRoot.Find();
        string page = ReadPage(root, pagePath);

        AnyFence(language).Matches(page).Count.Should().Be(AttributedFence(exampleDirectory, language).Matches(page).Count,
            $"every {language} fence on {pagePath} is hand-written, so each one needs the comment naming the {exampleDirectory} file and line it was copied from — otherwise nothing keeps it honest");
    }

    private static string ReadPage(DirectoryInfo root, string pagePath)
    {
        FileInfo file = new(Path.Combine(root.FullName, pagePath.Replace('/', Path.DirectorySeparatorChar)));
        file.Exists.Should().BeTrue($"{pagePath} is the page this test exists for");

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
