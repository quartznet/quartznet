namespace Quartz.Benchmark;

/// <summary>
/// The categories the <c>BenchmarkSmoke</c> build target excludes from its dry run, and the only reasons
/// a benchmark is allowed to sit outside it.
/// </summary>
/// <remarks>
/// The smoke run executes every benchmark in this assembly once — see <c>build/Build.cs</c> — so a
/// benchmark that stops working is caught by the same pull request that broke it. A benchmark carrying
/// one of these categories is not covered by that, and rots unnoticed until somebody runs it; add one
/// only when the benchmark genuinely cannot be executed unattended in a couple of seconds, and say why
/// on the benchmark itself.
/// </remarks>
internal static class BenchmarkCategories
{
    /// <summary>
    /// What <c>--smoke</c> leaves out. Everything else in the assembly is in the smoke run, including
    /// whatever is written next.
    /// </summary>
    public static readonly string[] ExcludedFromSmokeRun = [RequiresDatabase, LongRunning];

    /// <summary>
    /// Measures against a database that has to be running and pointed at by an environment variable.
    /// BenchmarkDotNet runs a process per benchmark case, so the benchmark cannot own a container.
    /// </summary>
    public const string RequiresDatabase = "RequiresDatabase";

    /// <summary>
    /// One iteration is hundreds of thousands of operations, so a single dry run of the benchmark is
    /// minutes rather than seconds — measured on purpose, and too much for a smoke run to carry.
    /// </summary>
    public const string LongRunning = "LongRunning";
}
