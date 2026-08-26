using System.Text.RegularExpressions;

namespace Quartz.Tests.Unit;

/// <summary>
/// How a call site logs: through a source-generated <c>[LoggerMessage]</c> method, never through
/// <c>ILogger.LogInformation</c> and its siblings.
/// </summary>
/// <remarks>
/// <para>
/// A plain <c>Log*</c> call formats and boxes its arguments whether or not the level is on, carries no
/// event id an operator can filter, and puts its message somewhere no snapshot can see. The conversion
/// away from them is worth nothing if it can come back one call at a time, which is what this test is
/// for — the same job <c>SourceEncodingTest</c> does for byte-order marks.
/// </para>
/// <para>
/// Every packable project under <c>src/</c> is covered, which
/// <see cref="EveryPackableProjectIsCovered" /> is what keeps true: a package that ships is a package
/// whose messages an operator has to be able to filter, so a new one joins <see cref="Converted" /> on
/// the day it is added rather than on the day somebody notices. <c>CA1848</c> guards the same boundary
/// from the compiler's side, which is what catches a call in a <c>.razor</c> file that this scan of
/// <c>.cs</c> files does not see.
/// </para>
/// <para>
/// Nothing that does not ship is covered, and that is deliberate rather than unfinished.
/// <c>Quartz.Examples*</c> and <c>Quartz.Documentation.Samples</c> are application code shown to a
/// reader, and application code logs with <c>logger.LogInformation(…)</c> — a sample that routed its
/// one message through a generated class would be teaching the wrong lesson about how to use Quartz.
/// <c>Quartz.Server</c>, <c>Quartz.Benchmark</c>, <c>Quartz.Trimming.Canary</c> and the test projects
/// have no operator to serve at all.
/// </para>
/// </remarks>
public class LogCallSiteTest
{
    /// <summary>
    /// The projects whose logging has been converted, relative to the repository root.
    /// </summary>
    private static readonly string[] Converted =
    [
        "src/Quartz",
        "src/Quartz.AspNetCore",
        "src/Quartz.Dashboard",
        "src/Quartz.Extensions.Redis",
        "src/Quartz.HttpClient",
        "src/Quartz.Jobs",
        "src/Quartz.Plugins",
        "src/Quartz.Plugins.TimeZoneConverter",
        "src/Quartz.Serialization.Newtonsoft",
    ];

    /// <summary>
    /// Build output and tool caches, which hold the generator's own output among other things.
    /// </summary>
    private static readonly string[] NotSearched = ["bin", "obj", "node_modules", "TestResults", ".vs"];

    /// <summary>
    /// Call sites that may keep a plain call, each with the reason it is allowed one. An entry here is
    /// a promise that the site has been looked at, not a place to put one that has not.
    /// </summary>
    private static readonly (string Path, string Reason)[] Allowed =
    [
        ("src/Quartz.Plugins/Plugins/History/StructuredLoggingJobHistoryPlugin.cs",
            "the message template is the user's, configured at run time, and its placeholders are named "
            + "- the whole point of this plugin is that a structured sink receives them as properties. A "
            + "[LoggerMessage] template is fixed at compile time, and rendering the template here first "
            + "would flatten exactly what the plugin exists to preserve. Every call is already behind an "
            + "IsEnabled check"),
        ("src/Quartz.Plugins/Plugins/History/StructuredLoggingTriggerHistoryPlugin.cs",
            "same as StructuredLoggingJobHistoryPlugin: the configured template's named placeholders are "
            + "the plugin's reason to exist, and they cannot survive a compile-time template"),
    ];

    /// <summary>
    /// The generated classes themselves, whose whole job is to hold the logging.
    /// </summary>
    private const string GeneratedLoggingSuffix = "Log.cs";

    private static readonly Regex PlainLogCall = new(
        @"\.Log(Trace|Debug|Information|Warning|Error|Critical)\(|\.Log\(LogLevel",
        RegexOptions.Compiled);

    [Test]
    public void NoConvertedProjectCallsILoggerDirectly()
    {
        DirectoryInfo root = RepositoryRoot.Find();

        List<string> offenders = [];

        foreach (string project in Converted)
        {
            DirectoryInfo directory = new(Path.Combine(root.FullName, project.Replace('/', Path.DirectorySeparatorChar)));

            directory.Exists.Should().BeTrue(
                $"{project} is listed as converted, so the tree this test walks must reach it");

            foreach (FileInfo file in Discover(directory))
            {
                string relative = Path.GetRelativePath(root.FullName, file.FullName).Replace('\\', '/');

                if (Allowed.Any(x => x.Path == relative))
                {
                    continue;
                }

                foreach (Match match in PlainLogCall.Matches(File.ReadAllText(file.FullName)))
                {
                    offenders.Add($"{relative}: {match.Value}");
                }
            }
        }

        offenders.Should().BeEmpty(
            "logging goes through a [LoggerMessage] method on the area's *Log class, which costs nothing "
            + "when the level is off and gives the message an event id that LogEventCatalogTest pins. These "
            + "call ILogger directly:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders.Order(StringComparer.Ordinal).Select(x => "    " + x))
            + Environment.NewLine
            + "Add the event to the *Log class beside the caller, taking the next id in that area's range, "
            + "and call it. A site that genuinely cannot go through one belongs in this test's allow-list, "
            + "with the reason written down");
    }

    /// <summary>
    /// An allow-list entry stops being a considered exception the moment its file stops making a plain
    /// call: from then on it is a hole nobody decided to leave open, and the next plain call written into
    /// that file walks straight through it.
    /// </summary>
    [Test]
    public void AllowListCarriesNoEntryThatHasServedItsPurpose()
    {
        DirectoryInfo root = RepositoryRoot.Find();

        foreach ((string path, string reason) in Allowed)
        {
            FileInfo file = new(Path.Combine(root.FullName, path.Replace('/', Path.DirectorySeparatorChar)));

            file.Exists.Should().BeTrue(
                $"the allow-list excuses {path}, so that has to be a file this repository still has");

            PlainLogCall.IsMatch(File.ReadAllText(file.FullName)).Should().BeTrue(
                $"{path} is on the allow-list because {reason}. It no longer calls ILogger directly, so the "
                + "entry excuses nothing and should be deleted before it starts excusing something else");
        }
    }

    /// <summary>
    /// A package that ships is a package whose log messages somebody operates on, so the list of covered
    /// projects is the list of packable ones and nothing less.
    /// </summary>
    /// <remarks>
    /// Without this, a package added after the conversion would ship unguarded — and it would look
    /// exactly like a package with nothing to convert, because both spend their whole life passing every
    /// other test in this file. A project that packs nothing is not covered and does not need to be.
    /// </remarks>
    [Test]
    public void EveryPackableProjectIsCovered()
    {
        DirectoryInfo root = RepositoryRoot.Find();

        List<string> packable = ShippedProjects.Find()
            .Select(x => Path.GetRelativePath(root.FullName, x.DirectoryName!).Replace('\\', '/'))
            .ToList();

        packable.Should().BeSubsetOf(Converted,
            "every package that ships logs to somebody who has to filter it, so its call sites are covered "
            + "from the day the package exists. A new package joins the Converted list with its own *Log "
            + "class and an event id range in LogEventCatalogTest; one that logs nothing joins with neither, "
            + "and stays honest the day it starts logging");

        Converted.Should().BeSubsetOf(packable,
            "a project that ships nothing has no operator to serve, and covering one would say that the "
            + "examples and the documentation samples should stop writing the plain call this repository "
            + "teaches readers to write");
    }

    private static IEnumerable<FileInfo> Discover(DirectoryInfo directory)
    {
        foreach (FileInfo file in directory.EnumerateFiles("*.cs"))
        {
            if (!file.Name.EndsWith(GeneratedLoggingSuffix, StringComparison.Ordinal))
            {
                yield return file;
            }
        }

        foreach (DirectoryInfo child in directory.EnumerateDirectories())
        {
            if (NotSearched.Contains(child.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (FileInfo file in Discover(child))
            {
                yield return file;
            }
        }
    }
}
