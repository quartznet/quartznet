using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Fallout.Common;
using Fallout.Common.IO;

using Serilog;

/// <summary>
/// Generates the log event catalogue page from the snapshots the log catalogue tests keep.
/// </summary>
/// <remarks>
/// <para>
/// An event id is what an operator filters and alerts on, and the documentation sells it as stable —
/// so every one of them has to be reachable by somebody reading a log line. The catalogue that answers
/// that already exists: <c>LogEventCatalogTest</c> reflects over every <c>[LoggerMessage]</c> method a
/// packable assembly declares and snapshots the result. Those snapshots are the source here, rather
/// than a second reflection pass, because they are already the reviewed artefact — an event that
/// arrives, is renumbered or is reworded is a diff in a <c>.verified.txt</c> before it is a diff on
/// the page.
/// </para>
/// <para>
/// The generated block sits between markers in an otherwise hand-written page, the way
/// <see cref="DocsSnippets" /> fills a snippet marker, and
/// <see cref="VerifyDocsLogEvents" /> fails a pull request whose page no longer matches the snapshots.
/// The page's own newline is preserved, so the check gives the same answer on an LF checkout and a
/// CRLF one.
/// </para>
/// </remarks>
public partial class Build
{
    AbsolutePath LogEventsPage => DocsDirectory / "documentation" / "quartz-4.x" / "log-events.md";

    /// <summary>
    /// The snapshots the log catalogue tests keep, one per packable assembly that logs. Both test
    /// projects that hold one are covered: <c>Quartz.AspNetCore</c>'s lives with the project whose
    /// dependencies it needs.
    /// </summary>
    IEnumerable<AbsolutePath> LogEventCatalogFiles =>
        SourceDirectory.GlobFiles("*/Verify/LogEventCatalogTest_*.verified.txt");

    const string LogEventsBeginMarker = "<!-- logEvents -->";
    const string LogEventsEndMarker = "<!-- endLogEvents -->";

    static readonly Regex LogEventHeaderLine = new(
        @"^(?<id>\d+)\s\s(?<level>\w+)\s\s(?<source>\S+)$",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    Target DocsLogEvents => _ => _
        .Description("Generates the log event catalogue page from the snapshotted log catalogues")
        .Executes(() => ProcessLogEventCatalogue(verifyOnly: false));

    Target VerifyDocsLogEvents => _ => _
        .Description("Fails when the log event catalogue page is out of step with the snapshotted catalogues")
        .Executes(() => ProcessLogEventCatalogue(verifyOnly: true));

    void ProcessLogEventCatalogue(bool verifyOnly)
    {
        List<LogEventRow> rows = ReadLogEventCatalogues();
        string before = File.ReadAllText(LogEventsPage);
        string after = ReplaceLogEventTable(before, rows);

        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            Log.Information("The log event catalogue page is up to date ({Count} events)", rows.Count);
            return;
        }

        if (verifyOnly)
        {
            throw new InvalidOperationException(
                $"""
                 {RootDirectory.GetRelativePathTo(LogEventsPage)} no longer matches the snapshotted log catalogues.

                 Run 'dotnet fallout DocsLogEvents' and commit the result.
                 """);
        }

        File.WriteAllText(LogEventsPage, after);
        Log.Information("Updated {File} ({Count} events)", RootDirectory.GetRelativePathTo(LogEventsPage), rows.Count);
    }

    List<LogEventRow> ReadLogEventCatalogues()
    {
        List<AbsolutePath> files = LogEventCatalogFiles.ToList();

        if (files.Count == 0)
        {
            throw new InvalidOperationException(
                "No LogEventCatalogTest_*.verified.txt was found under src/*/Verify. The catalogue page is "
                + "generated from those snapshots, so an empty result means the search stopped working "
                + "rather than that nothing logs.");
        }

        List<LogEventRow> rows = [];

        foreach (AbsolutePath file in files)
        {
            string package = Path.GetFileName(file)
                .Replace("LogEventCatalogTest_", "", StringComparison.Ordinal)
                .Replace(".verified.txt", "", StringComparison.Ordinal);

            rows.AddRange(ReadLogEventCatalogue(file, package));
        }

        List<IGrouping<int, LogEventRow>> collisions = rows.GroupBy(x => x.Id).Where(x => x.Count() > 1).ToList();

        if (collisions.Count != 0)
        {
            throw new InvalidOperationException(
                "One event id has to mean one thing to whoever filters on it, and these do not:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, collisions.Select(x => $"  {x.Key}: {string.Join(", ", x.Select(y => $"{y.Package} {y.Source}"))}")));
        }

        rows.Sort((x, y) => x.Id.CompareTo(y.Id));
        return rows;
    }

    /// <summary>
    /// Reads one snapshot. Each event is two lines — <c>&lt;id&gt;  &lt;Level&gt;  &lt;Class&gt;.&lt;Member&gt;</c>
    /// and then the quoted message template — which is the shape <c>LogEventCatalogTest.Format</c> writes.
    /// </summary>
    static IEnumerable<LogEventRow> ReadLogEventCatalogue(AbsolutePath file, string package)
    {
        // The snapshots carry a byte-order mark, because that is what Verify writes.
        string[] lines = File.ReadAllText(file).TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length == 0)
            {
                continue;
            }

            Match header = LogEventHeaderLine.Match(lines[i]);

            if (!header.Success)
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(file)}:{i + 1} is not an event header, so the snapshot's format has "
                    + $"changed and this target has not: '{lines[i]}'");
            }

            if (i + 1 >= lines.Length || !lines[i + 1].StartsWith("    \"", StringComparison.Ordinal) || !lines[i + 1].EndsWith('"'))
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(file)}:{i + 1} has no message template on the line after it, so the "
                    + "snapshot's format has changed and this target has not.");
            }

            string message = lines[i + 1][5..^1];
            i++;

            yield return new LogEventRow(
                int.Parse(header.Groups["id"].Value, CultureInfo.InvariantCulture),
                header.Groups["level"].Value,
                package,
                header.Groups["source"].Value,
                message);
        }
    }

    static string ReplaceLogEventTable(string page, List<LogEventRow> rows)
    {
        int begin = page.IndexOf(LogEventsBeginMarker, StringComparison.Ordinal);
        int end = page.IndexOf(LogEventsEndMarker, StringComparison.Ordinal);

        if (begin < 0 || end < begin)
        {
            throw new InvalidOperationException(
                $"The log event catalogue page must carry '{LogEventsBeginMarker}' and '{LogEventsEndMarker}', "
                + "in that order, around the block this target writes.");
        }

        string newLine = page.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        StringBuilder table = new();
        table.Append(LogEventsBeginMarker).Append(newLine);
        table.Append(newLine);
        table.Append("| Id | Level | Package | Message template |").Append(newLine);
        table.Append("|---|---|---|---|").Append(newLine);

        foreach (LogEventRow row in rows)
        {
            table
                .Append("| ").Append(row.Id.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(row.Level)
                .Append(" | `").Append(row.Package)
                .Append("` | `\"").Append(EscapeForTableCell(row.Message))
                .Append("\"` |").Append(newLine);
        }

        table.Append(newLine);

        return page[..begin] + table + page[end..];
    }

    /// <summary>
    /// A template is rendered as inline code, so the two characters that would end the cell or the code
    /// span early are the ones to spell out. Neither appears in any template today; the escape is here so
    /// that the day one does is not the day the table silently loses a column.
    /// </summary>
    /// <remarks>
    /// The quotation marks the caller puts around the result are the snapshot's own convention and are
    /// kept for the same reason it has them: leading and trailing whitespace is part of a template, and a
    /// code span that begins or ends with a space has that space eaten by the markdown reader — as well
    /// as being invisible to whoever reviews the diff.
    /// </remarks>
    static string EscapeForTableCell(string message) =>
        message.Replace("|", "\\|", StringComparison.Ordinal).Replace("`", "'", StringComparison.Ordinal);

    sealed record LogEventRow(int Id, string Level, string Package, string Source, string Message);
}
