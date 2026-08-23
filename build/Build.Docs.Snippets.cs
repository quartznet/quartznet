using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Fallout.Common;
using Fallout.Common.IO;

using MarkdownSnippets;

using Serilog;

/// <summary>
/// Injects the compiled documentation samples into the markdown that references them.
/// </summary>
/// <remarks>
/// <para>
/// Samples live as <c>#region sample_*</c> blocks in <c>src/Quartz.Documentation.Samples</c>, which is an
/// ordinary project in the solution — so a sample that stops compiling fails the normal build. A docs page
/// carries a <c>&lt;!-- snippet: name --&gt;</c> / <c>&lt;!-- endSnippet --&gt;</c> marker pair, and this
/// target fills it in.
/// </para>
/// <para>
/// The engine is <see href="https://github.com/SimonCropp/MarkdownSnippets">MarkdownSnippets</see>, used
/// through its library rather than its <c>mdsnippets</c> command line tool. The tool would be the obvious
/// choice, but its directory filter hard-codes a list of names that are never scanned, and
/// <c>packages</c> — the name of the directory holding the fifteen package pages this convention exists
/// for — is on it (<c>DefaultDirectoryExclusions.ShouldExcludeDirectory</c>). The tool skips the whole
/// directory and exits 0, which is the silent degradation the convention is meant to remove. The library
/// takes the directory predicates as parameters, so they are spelled out below instead.
/// </para>
/// <para>
/// The per-package <c>src/&lt;Package&gt;/README.md</c> files nuget.org shows are processed alongside the
/// documentation. A sample on a package page is the first code a new user meets, so it is the last place
/// that should be allowed to rot.
/// </para>
/// <para>
/// Three things fail rather than warn: a marker naming a snippet that does not exist, a marker that came
/// out empty (which is how a skipped directory would show up), and markdown that does not match what the
/// samples currently say. The last one is why <see cref="VerifyDocsSnippets" /> exists — the upstream tool
/// has no check mode, and the pattern most of its users adopt regenerates and auto-commits, which would let
/// a pull request merge green while the reviewed diff and the published page disagreed.
/// </para>
/// </remarks>
public partial class Build
{
    AbsolutePath DocsDirectory => RootDirectory / "docs";

    AbsolutePath DocumentationSamplesDirectory => SourceDirectory / "Quartz.Documentation.Samples";

    /// <summary>
    /// The readmes nuget.org renders on the package pages. One per packable project, beside its csproj.
    /// </summary>
    IEnumerable<AbsolutePath> PackageReadmeFiles => SourceDirectory.GlobFiles("*/README.md");

    /// <summary>
    /// Markdown trees that are frozen at the names their release shipped with, or are not documentation.
    /// </summary>
    static readonly string[] MarkdownDirectoryExclusions =
    [
        "docs/documentation/quartz-1.x",
        "docs/documentation/quartz-2.x",
        "docs/documentation/quartz-3.x",
        "docs/_posts",
        "docs/.vuepress"
    ];

    static readonly string[] TraversalExclusions =
    [
        ".git", ".vs", ".vscode", ".idea", "node_modules", "bin", "obj", "artifacts", "dist"
    ];

    static readonly Regex EmptySnippetMarker = new(
        @"<!--\s*snippet:\s*(?<key>[^\s>]+)\s*-->\s*(\r?\n)\s*<!--\s*endSnippet\s*-->",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    static readonly Regex SnippetMarker = new(
        @"<!--\s*snippet:\s*(?<key>[^\s>]+)\s*-->",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    Target DocsSnippets => _ => _
        .Description("Injects the compiled documentation samples into the markdown that references them")
        .Executes(() => ProcessDocumentationSnippets(verifyOnly: false));

    Target VerifyDocsSnippets => _ => _
        .Description("Fails when a documented snippet is missing, empty, or out of step with the samples")
        .Executes(() => ProcessDocumentationSnippets(verifyOnly: true));

    void ProcessDocumentationSnippets(bool verifyOnly)
    {
        var markdownFiles = DocsDirectory
            .GlobFiles("**/*.md")
            .Where(x => !IsExcludedMarkdown(x))
            .Concat(PackageReadmeFiles)
            .ToList();

        var before = markdownFiles.ToDictionary(x => x.ToString(), x => File.ReadAllText(x), StringComparer.Ordinal);

        var processor = new DirectoryMarkdownProcessor(
            RootDirectory,
            appendSnippets: AppendSnippet,
            directoryIncludes: ShouldTraverse,
            markdownDirectoryIncludes: HoldsProcessedMarkdown,
            snippetDirectoryIncludes: IsSampleDirectory,
            convention: DocumentConvention.InPlaceOverwrite,
            log: x => Log.Debug("{Message}", x),
            treatMissingAsWarning: false);

        AssertSnippetKeysAreUnique(processor.Snippets);

        List<AbsolutePath> changed;

        try
        {
            processor.Run();

            changed = markdownFiles
                .Where(x => !string.Equals(before[x], File.ReadAllText(x), StringComparison.Ordinal))
                .ToList();
        }
        finally
        {
            // Even a failing run may have written the pages it got through before the one that threw, and
            // a verification must leave the working tree exactly as it found it.
            if (verifyOnly)
            {
                foreach (var file in markdownFiles)
                {
                    File.WriteAllText(file, before[file]);
                }
            }
        }

        AssertNoEmptySnippets(markdownFiles);
        ReportUnreferencedSamples(processor.Snippets, markdownFiles);

        if (changed.Count == 0)
        {
            Log.Information("Documentation snippets are up to date ({Count} markdown files checked)", markdownFiles.Count);
            return;
        }

        if (!verifyOnly)
        {
            foreach (var file in changed)
            {
                Log.Information("Updated {File}", RootDirectory.GetRelativePathTo(file));
            }

            return;
        }

        throw new InvalidOperationException(
            $"""
             {changed.Count} documentation page(s) no longer match the samples they show:
             {string.Join(Environment.NewLine, changed.Select(x => "  " + RootDirectory.GetRelativePathTo(x)))}

             Run 'dotnet fallout DocsSnippets' (or 'npm run docs:snippets') and commit the result.
             """);
    }

    /// <summary>
    /// Writes the snippet as a plain fenced block, with no source link.
    /// </summary>
    /// <remarks>
    /// MarkdownSnippets' own writer emits an <c>&lt;a id&gt;</c> anchor and a <c>&lt;sup&gt;</c> line
    /// carrying a line-numbered permalink. Both are dropped here. The raw HTML would need the repository's
    /// markdownlint MD033 allow-list widened, and the permalink carries the sample's line numbers — so
    /// moving a sample within its own file would dirty every page that shows it, and a pull request that
    /// touched no documentation would fail the staleness gate. Without the link the generated markdown
    /// depends only on the sample's text. The fence language is spelled the way the rest of the
    /// documentation spells it.
    /// </remarks>
    static void AppendSnippet(string key, IEnumerable<Snippet> snippets, Action<string> appendLine)
    {
        foreach (var snippet in snippets)
        {
            appendLine($"```{FenceLanguage(snippet.Language)}");
            appendLine(snippet.Value);
            appendLine("```");
        }
    }

    static string FenceLanguage(string language) => language switch
    {
        "cs" => "csharp",
        _ => language
    };

    static bool ShouldTraverse(string path) =>
        !TraversalExclusions.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);

    bool HoldsProcessedMarkdown(string path) =>
        IsDocumentationDirectory(path) || IsPackageDirectory(path);

    bool IsDocumentationDirectory(string path) =>
        IsUnder(path, DocsDirectory) && !IsExcludedMarkdown(path);

    /// <summary>
    /// A project directory directly under <c>src</c>, which is where a package's own README.md sits.
    /// Deeper directories are left out on purpose: nothing else under <c>src</c> is published markdown.
    /// </summary>
    bool IsPackageDirectory(string path) =>
        NormalizePath(Path.GetDirectoryName(path) ?? "").Equals(NormalizePath(SourceDirectory), StringComparison.OrdinalIgnoreCase);

    bool IsSampleDirectory(string path) => IsUnder(path, DocumentationSamplesDirectory);

    bool IsExcludedMarkdown(string path) =>
        MarkdownDirectoryExclusions.Any(x => IsUnder(path, RootDirectory / x));

    /// <summary>
    /// Whether <paramref name="path" /> is <paramref name="root" /> or sits below it. The comparison
    /// stops at a separator, so a sibling whose name merely starts the same way is not swept in.
    /// </summary>
    static bool IsUnder(string path, AbsolutePath root)
    {
        var normalized = NormalizePath(path);
        var prefix = NormalizePath(root);

        return normalized.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(prefix + '/', StringComparison.OrdinalIgnoreCase);
    }

    static string NormalizePath(string path) => path.Replace('\\', '/').TrimEnd('/');

    /// <summary>
    /// Two regions sharing a name are not an error upstream — both are emitted, stacked, one after the
    /// other. That is never what a page wants, so it is an error here.
    /// </summary>
    static void AssertSnippetKeysAreUnique(IReadOnlyList<Snippet> snippets)
    {
        var duplicates = snippets
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .Where(x => x.Count() > 1)
            .ToList();

        if (duplicates.Count == 0)
        {
            return;
        }

        var detail = duplicates.Select(x => $"  {x.Key}: {string.Join(", ", x.Select(y => $"{Path.GetFileName(y.Path)}:{y.StartLine}"))}");
        throw new InvalidOperationException(
            "Documentation sample names must be unique, and these are not:" + Environment.NewLine + string.Join(Environment.NewLine, detail));
    }

    /// <summary>
    /// A marker with nothing between it and its <c>endSnippet</c> means the page was never processed —
    /// which is how a directory silently dropped from the scan announces itself.
    /// </summary>
    static void AssertNoEmptySnippets(IEnumerable<AbsolutePath> markdownFiles)
    {
        var empty = new List<string>();

        foreach (var file in markdownFiles)
        {
            foreach (Match match in EmptySnippetMarker.Matches(File.ReadAllText(file)))
            {
                empty.Add($"  {file}: {match.Groups["key"].Value}");
            }
        }

        if (empty.Count != 0)
        {
            throw new InvalidOperationException(
                "These snippet markers were left empty, so the page was never processed:" + Environment.NewLine + string.Join(Environment.NewLine, empty));
        }
    }

    static void ReportUnreferencedSamples(IReadOnlyList<Snippet> snippets, IEnumerable<AbsolutePath> markdownFiles)
    {
        var referenced = markdownFiles
            .SelectMany(x => SnippetMarker.Matches(File.ReadAllText(x)).Select(y => y.Groups["key"].Value))
            .ToHashSet(StringComparer.Ordinal);

        var unreferenced = snippets
            .Select(x => x.Key)
            .Where(x => !referenced.Contains(x))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        if (unreferenced.Count != 0)
        {
            // A warning rather than an error: a sample may legitimately land before the page that shows it.
            Log.Warning("Documentation samples nothing references: {Samples}", string.Join(", ", unreferenced));
        }
    }
}
