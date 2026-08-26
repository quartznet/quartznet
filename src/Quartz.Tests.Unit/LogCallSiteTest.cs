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
/// The four packages #3414 names are covered. The rest of what ships joins <see cref="Converted" />
/// with the sweep that finishes it; tests, examples and the documentation samples are deliberately
/// never covered, because a plain call is the right thing to write in all three.
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
        "src/Quartz.Extensions.Redis",
        "src/Quartz.Jobs",
        "src/Quartz.Plugins",
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
