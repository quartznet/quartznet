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

#nullable enable

using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;

using Quartz.Impl.AdoJobStore;
using Quartz.Tests.Unit.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Weasel;

/// <summary>
/// Every generated Weasel model has the tables and columns its dialect's <c>create_&lt;dialect&gt;.sql</c>
/// declares.
/// </summary>
/// <remarks>
/// <para>
/// Both are rendered from one model by <c>dotnet fallout GenerateSchema</c>, but through two renderers, and
/// the Weasel one leaves things out on purpose — so "generated from the same model" is a claim this checks
/// rather than assumes. A column only one of them has is a database that one route builds and the other
/// reads as changed.
/// </para>
/// <para>
/// The models are found in the tree, not listed here: every <c>src/Quartz.Weasel.*/Generated/QuartzTables.g.cs</c>
/// is a case, its dialect read from the header that names its script. A new dialect package is checked as
/// soon as its model is generated; the one thing it needs is a project reference from this test project,
/// and a model whose assembly is not referenced fails here saying so.
/// </para>
/// </remarks>
public sealed class WeaselModelColumnTest
{
    private static readonly Regex ScriptNamed = new(@"create_(?<dialect>\w+)\.sql", RegexOptions.CultureInvariant);

    /// <summary>Every generated model in the tree, as its project and the dialect its header names.</summary>
    private static IEnumerable<TestCaseData> GeneratedModels()
    {
        string source = Path.Combine(RepositoryRoot.Find().FullName, "src");

        foreach (string project in Directory.GetDirectories(source, "Quartz.Weasel.*").Order(StringComparer.Ordinal))
        {
            string model = Path.Combine(project, "Generated", "QuartzTables.g.cs");
            if (!File.Exists(model))
            {
                continue;
            }

            string dialect = ScriptNamed.Match(File.ReadAllText(model)).Groups["dialect"].Value;
            yield return new TestCaseData(Path.GetFileName(project), dialect);
        }
    }

    [Test]
    public void TheModelsAreFound()
    {
        GeneratedModels().Should().HaveCountGreaterThanOrEqualTo(3,
            "PostgreSQL, SQL Server and SQLite have a generated model, so finding fewer is a search that broke");
    }

    [TestCaseSource(nameof(GeneratedModels))]
    public void EveryWeaselTableHasTheColumnsTheGeneratedScriptDeclares(string project, string dialect)
    {
        dialect.Should().NotBeEmpty($"{project}'s generated header names the create_<dialect>.sql it mirrors");

        Dictionary<string, List<string>> weasel = WeaselTables(project);
        Dictionary<string, List<string>> script = SqlObjects.Parse(SchemaScriptTest.GeneratedScript(dialect), dialect).Tables
            .ToDictionary(x => x.Key, x => x.Value.Keys.Order(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);

        weasel.Should().HaveCount(AdoConstants.AllTableNames.Length + AdoConstants.OptionalTableNames.Length,
            "the model has every table the store knows, required and optional");

        weasel.Keys.Should().BeEquivalentTo(script.Keys,
            $"{project} and create_{dialect}.sql are one schema, and a table only one of them has is one "
            + "Weasel creates and ProvisionSchema() does not, or the reverse");

        foreach ((string table, List<string> columns) in weasel)
        {
            columns.Should().Equal(script[table],
                $"QRTZ_{table} in {project} has to have the columns create_{dialect}.sql declares, or a database "
                + "the script created reads as changed and Weasel adds or keeps a column the store never wrote");
        }
    }

    /// <summary>
    /// The model's tables, built the way the package builds them, as unprefixed table and column names.
    /// </summary>
    /// <remarks>
    /// By reflection, because each dialect's table is Weasel's own type for that dialect and a new
    /// package must not need a line here: the generated <c>QuartzTables.Build</c> and its
    /// <c>QuartzTableNaming</c> are the same shape in every package.
    /// </remarks>
    private static Dictionary<string, List<string>> WeaselTables(string project)
    {
        Assembly assembly;
        try
        {
            assembly = Assembly.Load(project);
        }
        catch (FileNotFoundException)
        {
            throw new InvalidOperationException(
                $"{project} has a generated model and this test project does not reference it. Add a "
                + $"ProjectReference to src/{project}/{project}.csproj so its model is checked.");
        }

        Type naming = assembly.GetType($"{project}.QuartzTableNaming", throwOnError: true)!;
        MethodInfo build = assembly.GetType($"{project}.QuartzTables", throwOnError: true)!
            .GetMethod("Build", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;

        object model = build.Invoke(null, [Activator.CreateInstance(naming, "quartz", "QRTZ_")])!;

        Dictionary<string, List<string>> tables = new(StringComparer.Ordinal);
        foreach (object item in (IEnumerable) model)
        {
            if (item.GetType().GetProperty("Columns")?.GetValue(item) is not IEnumerable columns)
            {
                continue;
            }

            object identifier = item.GetType().GetProperty("Identifier")!.GetValue(item)!;
            string table = Unprefixed((string) identifier.GetType().GetProperty("Name")!.GetValue(identifier)!);

            tables[table] = columns.Cast<object>()
                .Select(column => ((string) column.GetType().GetProperty("Name")!.GetValue(column)!).ToUpperInvariant())
                .Order(StringComparer.Ordinal)
                .ToList();
        }

        return tables;
    }

    private static string Unprefixed(string name)
    {
        string upper = name.ToUpperInvariant();
        return upper.StartsWith("QRTZ_", StringComparison.Ordinal) ? upper[5..] : upper;
    }
}
