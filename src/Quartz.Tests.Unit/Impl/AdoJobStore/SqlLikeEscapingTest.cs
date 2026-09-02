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

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// What a name filter means when the name contains a character the dialect reads as a wildcard.
/// </summary>
/// <remarks>
/// Every predicate is a bound parameter with an <c>ESCAPE '!'</c> clause and is scoped by
/// <c>SCHED_NAME</c>, so this is correctness rather than a boundary: a filter that matched by character
/// class would return the wrong rows, not somebody else's.
/// </remarks>
public class SqlLikeEscapingTest
{
    [Test]
    public void EveryDialectEscapesTheTwoWildcardsTheStandardHas()
    {
        Translate(new PortableDelegate(), StringOperator.Contains, "50%_off")
            .Should().Be("%50!%!_off%", "'%' and '_' are wildcards everywhere, and '!' is the escape character");

        Translate(new PortableDelegate(), StringOperator.Equality, "bang!")
            .Should().Be("bang!!", "the escape character escapes itself");
    }

    /// <summary>
    /// T-SQL reads <c>[</c> as the start of a character class, so a name filter containing one matched by
    /// class on SQL Server and Sybase while matching literally on every other dialect.
    /// </summary>
    [Test]
    public void SqlServerEscapesTheBracketItReadsAsACharacterClass()
    {
        Translate(new SqlServerLikeDelegate(), StringOperator.Contains, "[a-z]")
            .Should().Be("%![a-z]%",
                "a group literally named '[a-z]' is found by asking for '[a-z]', and a group named 'b' is not");
    }

    /// <summary>
    /// The bracket is not escaped elsewhere, and must not be: the standard says an escape character has
    /// to be followed by a wildcard or itself, and PostgreSQL enforces that — so <c>![</c> would be an
    /// error on a dialect where <c>[</c> is not a wildcard.
    /// </summary>
    [Test]
    public void NoOtherDialectEscapesTheBracket()
    {
        Translate(new PortableDelegate(), StringOperator.Contains, "[a-z]")
            .Should().Be("%[a-z]%");
    }

    [Test]
    public void AValueWithNothingToEscapeIsHandedBackAsItIs()
    {
        Translate(new SqlServerLikeDelegate(), StringOperator.StartsWith, "reports").Should().Be("reports%");
    }

    private static string Translate(ITranslate translator, StringOperator compareWith, string compareToValue)
    {
        return translator.Translate(compareWith, compareToValue);
    }

    private interface ITranslate
    {
        string Translate(StringOperator compareWith, string compareToValue);
    }

    /// <summary>
    /// Reaches the protected translation every ADO store goes through, for a dialect that adds no
    /// wildcards of its own.
    /// </summary>
    private sealed class PortableDelegate : StdAdoDelegate, ITranslate
    {
        public string Translate(StringOperator compareWith, string compareToValue) => ToSqlLikeClause(compareWith, compareToValue);
    }

    /// <inheritdoc cref="PortableDelegate" />
    private sealed class SqlServerLikeDelegate : SqlServerDelegate, ITranslate
    {
        public string Translate(StringOperator compareWith, string compareToValue) => ToSqlLikeClause(compareWith, compareToValue);
    }
}
