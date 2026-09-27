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

using System.Runtime.InteropServices;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Quartz.Analyzers.Tests;

/// <summary>
/// The cron parser's files compile against the base class library, with nothing of Quartz's beside
/// them but <c>TriggerConstants</c>.
/// </summary>
/// <remarks>
/// <para>
/// That is what lets them move into an assembly of their own later (#3720, #3873). This analyzer's
/// build already fails when a file it links reaches past the others, but it links neither
/// <c>CronExpressionBuilder.cs</c> nor the fast path, so this compiles all of them.
/// </para>
/// <para>
/// No <c>NET7_0_OR_GREATER</c> is defined, so the <c>#else</c> branches the analyzer compiles are the
/// ones read here: the <c>[GeneratedRegex]</c> branch needs a source generator this compilation does
/// not run. Both branches name only BCL types.
/// </para>
/// </remarks>
public class CronClosureTest
{
    private static readonly string[] closure =
    [
        "CronExpression.cs",
        "CronExpression.FastPath.cs",
        "CronExpressionBuilder.cs",
        "CronExpressionConstants.cs",
        "CronFormat.cs",
        "CronMacros.cs",
        "CronThrow.cs",
        "TimeZones.cs",
        "UnixCronRewriter.cs",
        "ZoneClock.cs",
        "ZoneOffsetTable.cs",
        "Util/BitUtil.cs",
        "Util/SerializationInfoExtensions.cs",
        "Util/SpanSplitExtensions.cs",
    ];

    /// <summary>
    /// The global usings <c>ImplicitUsings</c> gives every project in the repository.
    /// </summary>
    private const string ImplicitUsings =
        """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    /// <summary>
    /// The two members of <c>TriggerConstants</c> the parser reads. The type stays in <c>Quartz</c>,
    /// because it is public for <c>DefaultPriority</c> and every trigger type reads it; a split-out
    /// parser would carry these two privately, as <c>Quartz.Analyzers</c> does.
    /// </summary>
    private const string TriggerConstantsStandIn =
        """
        namespace Quartz;

        internal static class TriggerConstants
        {
            internal const int EarliestYear = 1970;

            internal static readonly int YearToGiveUpSchedulingAt = DateTimeOffset.UtcNow.Year + 100;
        }
        """;

    [Test]
    public void TheCronParserCompilesAgainstTheBaseClassLibraryAlone()
    {
        DirectoryInfo source = QuartzSource();
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest);

        List<SyntaxTree> trees =
        [
            .. closure.Select(file =>
            {
                string path = Path.Combine(source.FullName, file);
                return CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path);
            }),
            CSharpSyntaxTree.ParseText(ImplicitUsings, parseOptions, "ImplicitUsings.cs"),
            CSharpSyntaxTree.ParseText(TriggerConstantsStandIn, parseOptions, "TriggerConstants.cs"),
        ];

        CSharpCompilation compilation = CSharpCompilation.Create(
            "CronClosure",
            trees,
            BaseClassLibrary(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        compilation.GetDiagnostics()
            .Where(x => x.Severity == DiagnosticSeverity.Error)
            .Select(x => x.ToString())
            .Should().BeEmpty("the cron parser's files may reach each other and the BCL, and nothing else of Quartz's, or they cannot become an assembly of their own");
    }

    /// <summary>
    /// The managed assemblies of the shared framework this test runs on, and nothing the test itself
    /// brought: no <c>Quartz.dll</c> and no <c>Microsoft.Extensions.*</c>.
    /// </summary>
    private static List<MetadataReference> BaseClassLibrary()
    {
        string runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        string platformAssemblies = (string) AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;

        return platformAssemblies
            .Split(Path.PathSeparator)
            .Where(x => x.StartsWith(runtimeDirectory, StringComparison.OrdinalIgnoreCase))
            .Select(x => (MetadataReference) MetadataReference.CreateFromFile(x))
            .ToList();
    }

    /// <summary>
    /// Walks up from the test assembly's directory to the one holding <c>Quartz.slnx</c>.
    /// </summary>
    private static DirectoryInfo QuartzSource()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Quartz.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the parser's files are read from the source tree, so the test has to run inside one");

        return new DirectoryInfo(Path.Combine(directory!.FullName, "src", "Quartz"));
    }
}
