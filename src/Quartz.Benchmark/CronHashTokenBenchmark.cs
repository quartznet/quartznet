using BenchmarkDotNet.Attributes;

namespace Quartz.Benchmark;

/// <summary>
/// Resolving an expression's H (hash) tokens, which is the only thing that reads
/// <c>CronExpression</c>'s field-bound range tables.
/// </summary>
/// <remarks>
/// <para>
/// H resolution happens once, while the expression is being constructed, and never again — the
/// resulting expression carries concrete values. So this is what decides whether the shape of those
/// two tables is worth changing at all, and the answer has to be read next to
/// <see cref="CronExpressionBenchmark" />'s <c>Parse</c>, which is the rest of the same construction.
/// </para>
/// <para>
/// A plain <c>new CronExpression(text)</c> does not resolve H and rejects it; the hash key overload is
/// the one that does, which is why these expressions have a benchmark of their own rather than another
/// entry in the parse benchmark's list.
/// </para>
/// <para>
/// Measures production code, so a before/after is two runs rather than two arms.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class CronHashTokenBenchmark
{
    [Params(
        // Every field an H token, which touches every entry of both tables.
        "H H H * * ?",
        // Ranges and steps around H, the longest resolution path.
        "H(0-30) H/4 H 1,15 * ?")]
    public string CronExpression { get; set; } = "";

    private const string HashKey = "nightly-report-trigger";

    /// <summary>Constructing the expression, H resolution included.</summary>
    [Benchmark]
    public CronExpression ParseWithHashTokens()
    {
        return Quartz.CronExpression.ParseWithHash(CronExpression, HashKey);
    }

    /// <summary>The resolution on its own, without the parse it feeds.</summary>
    [Benchmark]
    public string ResolveHashTokens()
    {
        return Quartz.CronExpression.ResolveHash(CronExpression, HashKey);
    }
}
