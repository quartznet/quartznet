using System.Reflection;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The parameter names the statements carry and the names the binders pass are one constant, and the
/// statements are still statements.
/// </summary>
public class SqlParametersTest
{
    private static readonly string[] statements =
    [
        .. typeof(StdAdoConstants)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(string))
            .Select(field => (string) field.GetValue(null)!)
    ];

    [Test]
    public void NoStatementCarriesAConstantReferenceInsteadOfItsValue()
    {
        // A statement is built by interpolation, so `@{SqlParameters.TriggerName}` becomes
        // `@triggerName`. Written into a string that is *not* interpolated, the same text stays
        // literal and reaches the database, which is a failure this catches at build time rather
        // than on whichever provider is asked first.
        statements.Should().NotContain(sql => sql.Contains("SqlParameters", StringComparison.Ordinal),
            "a statement holding the text 'SqlParameters' is an interpolation hole that landed in a plain string literal");
    }

    [Test]
    public void EveryPlaceholderIsAKnownParameter()
    {
        string[] declared =
        [
            .. typeof(SqlParameters)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(field => (string) field.GetValue(null)!)
        ];

        // Statements also mention the fixed-width generated names, which are built per index from a
        // single function rather than declared here.
        string[] generated = ["excludedJobType", "tkn", "tkg", "jkn", "jkg", "oldState"];

        List<string> unknown =
        [
            .. statements
                .SelectMany(Placeholders)
                .Distinct()
                .Where(name => !declared.Contains(name, StringComparer.Ordinal))
                .Where(name => !generated.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
        ];

        unknown.Should().BeEmpty(
            "every placeholder is spelled from a constant the binder also uses, so one that is not a constant is one nothing binds");
    }

    /// <summary>
    /// The paging pair is spelled twice: here, for the statements Quartz builds, and on
    /// <see cref="AdoConstants" />, which is the public spelling a dialect delegate outside this
    /// assembly binds by.
    /// </summary>
    /// <remarks>
    /// Two constants rather than one because a delegate has to be able to name them and this class is
    /// internal. Their drifting apart is silent in the worst way — the statement would carry one name
    /// and the binder supply another, which a provider reports as an unbound parameter at run time, or
    /// worse binds positionally to the wrong column — so it is worth a test rather than a comment.
    /// </remarks>
    [Test]
    public void ThePagingParametersHaveOneSpellingBetweenTheirTwoHomes()
    {
        SqlParameters.PageSkip.Should().Be(AdoConstants.ParameterPageSkip);
        SqlParameters.PageTake.Should().Be(AdoConstants.ParameterPageTake);
    }

    private static IEnumerable<string> Placeholders(string sql)
    {
        for (int i = sql.IndexOf('@', StringComparison.Ordinal); i >= 0; i = sql.IndexOf('@', i + 1))
        {
            int end = i + 1;
            while (end < sql.Length && (char.IsAsciiLetterOrDigit(sql[end]) || sql[end] == '_'))
            {
                end++;
            }

            if (end > i + 1)
            {
                yield return sql[(i + 1)..end];
            }
        }
    }
}
