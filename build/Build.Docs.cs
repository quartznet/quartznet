using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Fallout.Common;
using Fallout.Common.IO;
using Fallout.Common.Tooling;
using Fallout.Common.Tools.Npm;

using Serilog;

using static Fallout.Common.Tools.Npm.NpmTasks;

public partial class Build
{
    Target DocsBuild => _ => _
        .DependsOn(DocsSnippets)
        // Not DependsOn: the VuePress CLI empties the output directory before it builds, so a
        // reference generated first would be deleted rather than published. ApiDoc is ordered after
        // this target and triggered by it, which is the order the docs workflow's steps are in too.
        .Triggers(ApiDoc)
        .Executes(() =>
        {
            if (IsServerBuild)
            {
                NpmCi();
            }
            else
            {
                NpmInstall();
            }

            // https://stackoverflow.com/a/69699772/111604
            var nodeVersion = ProcessTasks.StartProcess("node", "--version").AssertWaitForExit().Output.FirstOrDefault().Text.Trim();
            var major = Convert.ToInt32(Regex.Match(nodeVersion, "^v(\\d+)").Groups[1].Captures[0].Value);

            Log.Information("Detected Node.js major version {Version}", major);

            NpmRun(_ => _
                .SetCommand("docs:build")
            );
        });

    /// <summary>
    /// The docfx project the API reference is generated from. It lives at the repository root rather
    /// than under <c>docs/</c>, which is VuePress's source directory — every <c>.md</c> in there
    /// becomes a site page, is link-checked and is linted, and a docfx landing page is none of those.
    /// </summary>
    AbsolutePath ApiDocDirectory => RootDirectory / "apidoc";

    /// <summary>
    /// Where the generated reference lands: inside the VuePress output, so that one <c>dist</c> is the
    /// whole published site and the docs workflow's deploy step carries the reference with it.
    /// </summary>
    /// <remarks>
    /// <c>4.x</c> rather than a full version: the set rolls forward with every 4.x minor release, and
    /// the sidebar, the readme and the landing page all link to it by that name.
    /// </remarks>
    AbsolutePath ApiDocOutputDirectory => DocsDirectory / ".vuepress" / "dist" / "apidoc" / "4.x";

    /// <summary>
    /// Anything docfx prints as a warning. Three shapes occur: a bare <c>warning: …</c> from the tool
    /// itself, a <c>File.cs(1,1): warning: …</c> from a doc comment it read, and a
    /// <c>page.md: warning UidNotFound: …</c> from a cross reference it could not resolve. Lower case,
    /// so that the MSBuild summary docfx's own restore prints — <c>0 Warning(s)</c> — is not one.
    /// </summary>
    static readonly Regex DocfxWarning = new(@"\bwarning(?:\s+\w+)?:\s", RegexOptions.None, TimeSpan.FromSeconds(5));

    /// <summary>
    /// docfx's closing count, which is what makes the pattern above checkable rather than hopeful: a
    /// warning shape it does not match is caught by the two numbers disagreeing.
    /// </summary>
    static readonly Regex DocfxWarningCount = new(@"^\s*(\d+) warning\(s\)\s*$", RegexOptions.None, TimeSpan.FromSeconds(5));

    /// <summary>
    /// The one warning this build tolerates, and it is about the machine rather than about Quartz.
    /// </summary>
    /// <remarks>
    /// docfx carries its own Roslyn, and <c>Quartz.Dashboard</c> is a Razor project whose source
    /// generator ships with the .NET SDK and refuses to load into a compiler older than the one it was
    /// built against. Nothing here can move either version. What it costs is the generated class per
    /// <c>.razor</c> file — which <c>apidoc/filterConfig.yml</c> excludes anyway, for the same reason
    /// <c>PublicApiTest</c> keeps them out of the <c>Quartz.Dashboard</c> baseline: they are the
    /// dashboard's UI, not API anyone calls. Everything else in the assembly is documented normally.
    /// </remarks>
    static readonly Regex ToleratedDocfxWarning =
        new(@"warning: FailedToLoadAnalyzer: .*Microsoft\.CodeAnalysis\.Razor\.Compiler", RegexOptions.None, TimeSpan.FromSeconds(5));

    /// <summary>
    /// Generates the 4.x API reference from the XML documentation comments of the ten shipped packages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two docfx passes: <c>metadata</c> reads the projects with Roslyn and writes one YAML file per
    /// namespace and type, <c>build</c> turns those into the site. docfx runs its own restore, so this
    /// target needs no compiled output and depends on nothing. It is ordered <em>after</em>
    /// <see cref="DocsBuild" /> rather than before it, because the VuePress CLI empties the output
    /// directory the two of them share.
    /// </para>
    /// <para>
    /// A malformed doc comment or an <c>xref</c> that resolves nowhere fails the build rather than
    /// warning, which is what makes the reference worth publishing — the docs pull request leg runs
    /// this target for exactly that reason. docfx's own <c>--warningsAsErrors</c> is not used, because
    /// it cannot make the one exception <see cref="ToleratedDocfxWarning" /> describes.
    /// </para>
    /// </remarks>
    Target ApiDoc => _ => _
        .After(DocsBuild)
        .Executes(() =>
        {
            // The metadata pass is incremental over whatever is already in api/, so a type that was
            // deleted since the last run would otherwise stay in the reference forever.
            (ApiDocDirectory / "api").DeleteDirectory();
            ApiDocOutputDirectory.CreateOrCleanDirectory();

            RunDocfx("metadata");
            RunDocfx("build");

            // The docfx template ships the source maps of its own minified JavaScript, which is half
            // the bytes of the published set and of no use to anyone reading an API reference.
            var sourceMaps = ApiDocOutputDirectory.GlobFiles("**/*.map");
            sourceMaps.DeleteFiles();

            var pages = ApiDocOutputDirectory.GlobFiles("**/*.html").Count;
            Log.Information("Generated {Pages} pages into {Directory} (dropped {SourceMaps} source maps)",
                pages, ApiDocOutputDirectory, sourceMaps.Count);
        });

    void RunDocfx(string command)
    {
        var process = ProcessTasks.StartProcess(
            "dotnet",
            $"docfx {command} \"{ApiDocDirectory / "docfx.json"}\"",
            workingDirectory: ApiDocDirectory,
            logOutput: true);

        process.AssertZeroExitCode();

        List<string> output = process.Output.Select(x => x.Text).Where(x => x is not null).ToList();
        List<string> recognized = output.Where(x => DocfxWarning.IsMatch(x)).ToList();
        List<string> unexpected = recognized.Where(x => !ToleratedDocfxWarning.IsMatch(x)).ToList();

        if (unexpected.Count > 0)
        {
            throw new InvalidOperationException(
                $"docfx {command} reported {unexpected.Count} warning(s). The API reference is generated from the XML " +
                "documentation comments, so a warning here is a doc comment to fix rather than a threshold to raise:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, unexpected.Select(x => "  " + x)));
        }

        var reported = output
            .Select(x => DocfxWarningCount.Match(x))
            .Where(x => x.Success)
            .Select(x => int.Parse(x.Groups[1].Value))
            .DefaultIfEmpty(0)
            .Max();

        if (reported > recognized.Count)
        {
            throw new InvalidOperationException(
                $"docfx {command} counted {reported} warning(s) but only {recognized.Count} of them look like a " +
                $"warning to this build, so {reported - recognized.Count} would have passed unread. Widen " +
                $"{nameof(DocfxWarning)} in build/Build.Docs.cs to whatever shape docfx has started printing.");
        }
    }
}
