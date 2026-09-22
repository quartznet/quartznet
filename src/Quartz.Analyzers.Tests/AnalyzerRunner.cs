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
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Quartz.Analyzers.Tests;

/// <summary>
/// Compiles a snippet against the real <c>Quartz.dll</c> and runs one analyzer — or one source
/// generator — over it.
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

    private const string DefaultAssemblyName = "Snippet";

    private static readonly Lazy<ImmutableArray<MetadataReference>> references = new Lazy<ImmutableArray<MetadataReference>>(BuildReferences);

    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Latest);

    /// <summary>
    /// Every diagnostic <typeparamref name="TAnalyzer" /> reports over <paramref name="source" />,
    /// in source order.
    /// </summary>
    internal static async Task<IReadOnlyList<Diagnostic>> Run<TAnalyzer>(string source)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        CSharpCompilation compilation = Compile(source);

        compilation.GetDiagnostics()
            .Where(x => x.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the snippet a test writes has to compile, or the analyzer is being asked about code the compiler never understood");

        return await Analyze<TAnalyzer>(compilation);
    }

    /// <summary>
    /// Every diagnostic <typeparamref name="TAnalyzer" /> reports over a compilation already built —
    /// which is how a test asks the analyzer about source a generator contributed.
    /// </summary>
    internal static async Task<IReadOnlyList<Diagnostic>> Analyze<TAnalyzer>(Compilation compilation)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
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
    /// Runs one incremental generator over a snippet and hands back what it said and what it wrote.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same compilation the analyzers are given — the references are this test process's own, so
    /// the snippet is a net10.0 one written against the shipped <c>Quartz.dll</c>. What the generator
    /// emits is therefore compiled against the real <c>AddJob&lt;T&gt;</c> and <c>AddTrigger&lt;T&gt;</c>
    /// rather than against a stub, which is what makes "it compiles" worth asserting.
    /// </para>
    /// <para>
    /// The snippet is <em>not</em> checked before the generator runs, unlike the analyzer path: a
    /// snippet is allowed to call the extension method the generator is about to write. The output
    /// compilation is checked instead, and that check covers both halves at once.
    /// </para>
    /// </remarks>
    /// <param name="source">The snippet.</param>
    /// <param name="assemblyName">
    /// What the snippet's assembly is called, which is what another snippet's
    /// <c>InternalsVisibleTo</c> names.
    /// </param>
    /// <param name="references">
    /// Assemblies beyond this process's own, such as another run's <see cref="GeneratorRun.ToReference" />.
    /// </param>
    /// <param name="toleratedErrors">
    /// Compiler errors the snippet is written to provoke, which are the compiler's to report rather
    /// than the generator's; every other error still fails the run.
    /// </param>
    /// <param name="languageVersion">
    /// The C# the snippet is written in, and so the C# the generated file is parsed and compiled as —
    /// which is what an older project's build does with it.
    /// </param>
    internal static GeneratorRun RunGenerator<TGenerator>(
        string source,
        string assemblyName = DefaultAssemblyName,
        IEnumerable<MetadataReference>? references = null,
        IReadOnlyCollection<string>? toleratedErrors = null,
        LanguageVersion languageVersion = LanguageVersion.Latest)
        where TGenerator : IIncrementalGenerator, new()
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(languageVersion);
        CSharpCompilation compilation = Compile(source, assemblyName, references, parseOptions);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new TGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);

        foreach (GeneratorRunResult result in driver.GetRunResult().Results)
        {
            // A generator that throws is reported as CS8785 and the build carries on with the file
            // missing, which reads as "it generated nothing" from here. Rethrowing is what makes it
            // read as the crash it is.
            if (result.Exception is not null)
            {
                throw result.Exception;
            }
        }

        output.GetDiagnostics()
            .Where(x => x.Severity == DiagnosticSeverity.Error && toleratedErrors?.Contains(x.Id) != true)
            .Should().BeEmpty("what a generator writes has to compile against the shipped Quartz, and so does the snippet that called it");

        ImmutableArray<SyntaxTree> generated = [.. output.SyntaxTrees.Where(x => !ReferenceEquals(x, compilation.SyntaxTrees[0]))];

        return new GeneratorRun(
            source,
            diagnostics.OrderBy(x => x.Location.SourceSpan.Start).ToList(),
            generated.Length == 0 ? null : string.Join(Environment.NewLine, generated.Select(x => x.ToString())),
            output);
    }

    /// <summary>
    /// Runs a generator twice over the same source, the second time over a re-parsed copy of it, and
    /// says why each output step ran.
    /// </summary>
    /// <remarks>
    /// Replacing the tree with an identically parsed one is what makes this worth asking: the
    /// compiler sees a tree it has not seen before, so the generator's transform runs again — and
    /// everything downstream of it is only cached if what the transform produced compares equal to
    /// what it produced the first time. That is the whole reason the generator's model is built out
    /// of values rather than symbols.
    /// </remarks>
    internal static IReadOnlyList<IncrementalStepRunReason> RerunReasons<TGenerator>(string source)
        where TGenerator : IIncrementalGenerator, new()
    {
        CSharpCompilation compilation = Compile(source);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new TGenerator().AsSourceGenerator()],
            additionalTexts: [],
            parseOptions: ParseOptions,
            optionsProvider: null,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);

        CSharpCompilation reparsed = compilation.ReplaceSyntaxTree(
            compilation.SyntaxTrees[0],
            CSharpSyntaxTree.ParseText(source, ParseOptions, path: "Snippet.cs"));

        driver = driver.RunGenerators(reparsed);

        return driver.GetRunResult().Results[0].TrackedOutputSteps
            .SelectMany(x => x.Value)
            .SelectMany(x => x.Outputs)
            .Select(x => x.Reason)
            .ToList();
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

    private static CSharpCompilation Compile(
        string source,
        string assemblyName = DefaultAssemblyName,
        IEnumerable<MetadataReference>? additionalReferences = null,
        CSharpParseOptions? parseOptions = null)
    {
        return CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, parseOptions ?? ParseOptions, path: "Snippet.cs")],
            [.. references.Value, .. additionalReferences ?? []],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
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

/// <summary>
/// What one run of a generator over a snippet produced.
/// </summary>
/// <param name="Snippet">The source the generator was run over.</param>
/// <param name="Diagnostics">Everything the generator reported, in source order.</param>
/// <param name="Generated">
/// The source the generator added, or <see langword="null" /> when it added none — which is a thing
/// this generator does deliberately, so it is a value rather than an empty string.
/// </param>
/// <param name="Output">
/// The compilation the generator's own files are part of, so that a test can ask an analyzer what it
/// makes of them.
/// </param>
internal sealed record GeneratorRun(string Snippet, IReadOnlyList<Diagnostic> Diagnostics, string? Generated, Compilation Output)
{
    /// <summary>
    /// The snippet text a diagnostic points at, which is how a test says "on the attribute" without
    /// counting columns.
    /// </summary>
    /// <remarks>
    /// <see cref="AnalyzerRunner.SpanText" /> cannot answer this one: a generator reports through a
    /// location rebuilt from a file path and a span rather than through one holding a syntax tree it
    /// kept alive, so the text has to come from the snippet the test wrote.
    /// </remarks>
    internal string TextAt(Diagnostic diagnostic)
    {
        TextSpan span = diagnostic.Location.SourceSpan;
        return Snippet.Substring(span.Start, span.Length);
    }

    /// <summary>
    /// The assembly this run built, generated file included, as another snippet would reference it.
    /// </summary>
    /// <remarks>
    /// Emitted and read back as metadata rather than handed over as a compilation reference, because
    /// that is what a referencing project's compiler sees: an internal type, and whichever
    /// <c>InternalsVisibleTo</c> grants the assembly carries.
    /// </remarks>
    internal MetadataReference ToReference()
    {
        using MemoryStream image = new MemoryStream();
        EmitResult result = Output.Emit(image);

        result.Success.Should().BeTrue("an assembly another snippet references has to exist: {0}", string.Join(Environment.NewLine, result.Diagnostics));

        return MetadataReference.CreateFromImage(image.ToArray());
    }
}
