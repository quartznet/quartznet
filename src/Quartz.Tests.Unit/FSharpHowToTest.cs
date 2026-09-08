namespace Quartz.Tests.Unit;

/// <summary>
/// The F# in the migration guide's "Upgrading an F# project" is the F# in
/// <c>src/Quartz.Examples.FSharp</c>.
/// </summary>
/// <remarks>
/// That section carries plain fences rather than generated snippets, because <c>DocsSnippets</c> reads
/// <c>Quartz.Documentation.Samples</c>, which is a C# project and so can hold no F# at all — the same
/// bind the Wolverine how-to is in for a different reason, and the arrangement both use is described on
/// <see cref="AttributedSamples" />. Everything else on the page is C#, so this test looks at
/// <c>fsharp</c> fences alone.
/// </remarks>
public class FSharpHowToTest
{
    private const string PagePath = "docs/documentation/quartz-4.x/migration-guide.md";

    private const string ExampleDirectory = "src/Quartz.Examples.FSharp";

    /// <summary>
    /// One fence per error the section answers, plus the job, plus the hosted registration: six, which
    /// is the fewest the section can carry and still be the section this test exists for.
    /// </summary>
    private const int LeastExpectedFences = 6;

    [Test]
    public void EveryFencedSampleAppearsVerbatimInTheExample()
    {
        AttributedSamples.EachFenceAppearsVerbatimInTheExample(PagePath, ExampleDirectory, "fsharp", LeastExpectedFences);
    }

    [Test]
    public void EveryFencedSampleSaysWhereItCameFrom()
    {
        AttributedSamples.EachFenceSaysWhereItCameFrom(PagePath, ExampleDirectory, "fsharp");
    }
}
