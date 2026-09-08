namespace Quartz.Tests.Unit;

/// <summary>
/// The C# on the Wolverine how-to page is the C# in <c>src/Quartz.Examples.Wolverine</c>.
/// </summary>
/// <remarks>
/// The page carries plain fences rather than generated snippets, because <c>DocsSnippets</c> reads
/// <c>Quartz.Documentation.Samples</c> and that project may not take a <c>WolverineFx</c> dependency for
/// the sake of a sample — the rule <c>CONTRIBUTING.md</c> states under "Code samples in the
/// documentation", and the reason the Aspire how-to's AppHost blocks are hand-written too.
/// <see cref="AttributedSamples" /> is what stands in for <c>VerifyDocsSnippets</c> here, and explains
/// the arrangement.
/// </remarks>
public class WolverineHowToTest
{
    private const string PagePath = "docs/documentation/quartz-4.x/how-tos/wolverine.md";

    private const string ExampleDirectory = "src/Quartz.Examples.Wolverine";

    /// <summary>
    /// The page teaches six parts of the example, so six is the fewest fences it can carry and still be
    /// the page this test exists for.
    /// </summary>
    private const int LeastExpectedFences = 6;

    [Test]
    public void EveryFencedSampleAppearsVerbatimInTheExample()
    {
        AttributedSamples.EachFenceAppearsVerbatimInTheExample(PagePath, ExampleDirectory, "csharp", LeastExpectedFences);
    }

    [Test]
    public void EveryFencedSampleSaysWhereItCameFrom()
    {
        AttributedSamples.EachFenceSaysWhereItCameFrom(PagePath, ExampleDirectory, "csharp");
    }
}
