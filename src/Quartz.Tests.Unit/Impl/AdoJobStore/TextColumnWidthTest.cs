#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The width in bytes each dialect cuts a text to is the width its schema scripts declare, and a dialect
/// that cuts by characters only declares columns that hold them (#3905).
/// </summary>
/// <remarks>
/// The widths are the delegate's own knowledge, written beside the scripts rather than read from the
/// database, so this is what stops the two drifting. Both scripts of a dialect are read: the fresh
/// install people run, and the embedded one <c>ProvisionSchema()</c> runs.
/// </remarks>
public class TextColumnWidthTest
{
    /// <summary>Every text column the store cuts to fit, with its length in characters.</summary>
    private static readonly (string Column, int MaxLength)[] CutColumns =
    [
        (AdoConstants.ColumnPauseReason, PauseDetails.MaxReasonLength),
        (AdoConstants.ColumnPausedBy, PauseDetails.MaxRequestedByLength),
        (AdoConstants.ColumnProgressMessage, FireInstanceProgress.MaxMessageLength),
        (AdoConstants.ColumnErrorMessage, StdAdoDelegate.MaxErrorMessageLength),
    ];

    private static IEnumerable<TestCaseData> Dialects()
    {
        yield return new TestCaseData("sqlServer", new SqlServerDelegate());
        yield return new TestCaseData("postgres", new PostgreSQLDelegate());
        yield return new TestCaseData("mysql_innodb", new MySQLDelegate());
        yield return new TestCaseData("oracle", new OracleDelegate());
        yield return new TestCaseData("sqlite", new SQLiteDelegate());
        yield return new TestCaseData("firebird", new FirebirdDelegate());
    }

    [TestCaseSource(nameof(Dialects))]
    public void EachCutTextFitsTheColumnItsSchemaDeclares(string dialect, StdAdoDelegate driverDelegate)
    {
        foreach (string script in new[] { FreshInstallScript(dialect), GeneratedScript(dialect) })
        {
            foreach ((string column, int maxLength) in CutColumns)
            {
                List<int?> declared = DeclaredWidths(script, column);
                declared.Should().NotBeEmpty("{0} is declared in the {1} schema", column, dialect);

                if (driverDelegate.TextColumnByteWidth(column) is { } bytes)
                {
                    declared.Should().AllSatisfy(width => width.Should().Be(bytes,
                        "a dialect that counts {0} in bytes cuts it to the width its schema declares", column));
                }
                else
                {
                    declared.Should().AllSatisfy(width => (width ?? int.MaxValue).Should().BeGreaterThanOrEqualTo(maxLength,
                        "a dialect that counts {0} in characters must declare room for all {1} of them", column, maxLength));
                }
            }
        }
    }

    [Test]
    public void AColumnTheStoreDoesNotCutHasNoWidthInBytes()
    {
        new FirebirdDelegate().TextColumnByteWidth(AdoConstants.ColumnDescription).Should().BeNull(
            "a description is refused when it is too long, not cut");
        new OracleDelegate().TextColumnByteWidth(AdoConstants.ColumnDescription).Should().BeNull();
    }

    /// <summary>
    /// Every width <paramref name="column" /> is declared with in <paramref name="script" />: a number, or
    /// <see langword="null" /> for a type with none, such as <c>TEXT</c>.
    /// </summary>
    private static List<int?> DeclaredWidths(string script, string column)
    {
        // Comments name the columns too, and say why Oracle's are wider.
        string code = Regex.Replace(script, @"--[^\n]*|^\s*#[^\n]*", "", RegexOptions.Multiline);

        return Regex.Matches(code,
                $@"\[?\b{column}\b\]?\s+\[?(?:n?varchar2?|text|clob)\]?(?:\s*\(\s*(?<width>\d+)\s*\))?",
                RegexOptions.IgnoreCase)
            .Select(match => match.Groups["width"].Success
                ? int.Parse(match.Groups["width"].Value, CultureInfo.InvariantCulture)
                : (int?) null)
            .ToList();
    }

    private static string GeneratedScript(string dialect)
    {
        string name = $"Quartz.Impl.AdoJobStore.Schema.create_{dialect}.sql";

        using Stream stream = typeof(IScheduler).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"'{name}' is not embedded in Quartz.dll.");

        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string FreshInstallScript(string dialect)
    {
        return File.ReadAllText(Path.Combine(RepositoryRoot.Find().FullName, "database", "tables", $"tables_{dialect}.sql"));
    }
}
