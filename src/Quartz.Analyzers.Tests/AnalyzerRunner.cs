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

using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Quartz.Analyzers.Tests;

/// <summary>
/// Compiles a snippet against the real <c>Quartz.dll</c> and runs one analyzer over it.
/// </summary>
/// <remarks>
/// <para>
/// The references are the ones this test process is running on, which is what makes the compilation
/// a net10.0 one without a reference assembly pack to download. That is the reason this is
/// hand-rolled rather than <c>Microsoft.CodeAnalysis.CSharp.Analyzer.Testing</c>: the newest
/// reference assemblies that package offers are net9.0, and a net10.0 <c>Quartz.dll</c> on a net9.0
/// compilation is CS1705 before any analyzer runs.
/// </para>
/// <para>
/// A snippet that does not compile fails the test that wrote it. That check is not decoration: a
/// misspelled Quartz member would otherwise make an analyzer look silent when it was never asked.
/// </para>
/// </remarks>
internal static class AnalyzerRunner
{
    /// <summary>
    /// The assembly whose copy of the cron parser must never reach a test compilation: it declares
    /// <c>Quartz.CronExpression</c> too, and a snippet referencing both would not compile.
    /// </summary>
    private const string AnalyzerAssemblyFileName = "Quartz.Analyzers.dll";

    private static readonly Lazy<ImmutableArray<MetadataReference>> references = new Lazy<ImmutableArray<MetadataReference>>(BuildReferences);

    /// <summary>
    /// Every diagnostic <typeparamref name="TAnalyzer" /> reports over <paramref name="source" />,
    /// in source order.
    /// </summary>
    internal static async Task<IReadOnlyList<Diagnostic>> Run<TAnalyzer>(string source)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Snippet",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest), path: "Snippet.cs")],
            references.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        compilation.GetDiagnostics()
            .Where(x => x.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the snippet a test writes has to compile, or the analyzer is being asked about code the compiler never understood");

        CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(
            [new TAnalyzer()],
            new CompilationWithAnalyzersOptions(
                new AnalyzerOptions([]),
                // An analyzer that throws is reported as AD0001 and the run carries on, which reads
                // as "no diagnostics" from here. Rethrowing is what makes it read as the crash it is.
                onAnalyzerException: (exception, _, _) => throw exception,
                concurrentAnalysis: false,
                logAnalyzerExecutionTime: false));

        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(TestContext.CurrentContext.CancellationToken);

        return diagnostics.OrderBy(x => x.Location.SourceSpan.Start).ToList();
    }

    /// <summary>
    /// The source text a diagnostic points at, which is how a test says "on the literal" without
    /// counting columns.
    /// </summary>
    internal static string SpanText(this Diagnostic diagnostic)
    {
        SyntaxTree tree = diagnostic.Location.SourceTree!;
        return tree.GetText().ToString(diagnostic.Location.SourceSpan);
    }

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        string platformAssemblies = (string) AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;

        List<MetadataReference> result = [];
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in platformAssemblies.Split(Path.PathSeparator))
        {
            string name = Path.GetFileName(path);

            if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, AnalyzerAssemblyFileName, StringComparison.OrdinalIgnoreCase)
                || !seen.Add(name))
            {
                continue;
            }

            result.Add(MetadataReference.CreateFromFile(path));
        }

        result.Should().Contain(
            x => x.Display!.EndsWith("Quartz.dll", StringComparison.Ordinal),
            "the snippets are written against the shipped Quartz, so its assembly has to be on the compiler's reference list");

        return [.. result];
    }
}
