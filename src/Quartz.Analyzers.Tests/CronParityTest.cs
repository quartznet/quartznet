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

extern alias QuartzAnalyzers;

using LinkedCronFormat = QuartzAnalyzers::Quartz.CronFormat;
using LinkedValidator = QuartzAnalyzers::Quartz.Analyzers.CronLiteralValidator;

namespace Quartz.Analyzers.Tests;

/// <summary>
/// The two copies of the parser answer the same way, expression by expression.
/// </summary>
/// <remarks>
/// <para>
/// <c>Quartz.Analyzers</c> links <c>src/Quartz/CronExpression.cs</c> and its closure rather than
/// referencing <c>Quartz.dll</c>, which it cannot: it is netstandard2.0 because the compiler host is.
/// Linked source is the right answer only while it stays the same source, and the two
/// <c>#if NET7_0_OR_GREATER</c> guards, the polyfills and the netstandard2.0 compiler itself are all
/// places where it could quietly stop being. This is what says it has not.
/// </para>
/// <para>
/// The comparison is on the message as well as on validity, because an analyzer's whole value over a
/// reduced parser is that it says what the run time would have said.
/// </para>
/// </remarks>
public class CronParityTest
{
    /// <summary>
    /// Every shape <c>CronExpressionTest</c> and its neighbours exercise: the macros, <c>L</c>,
    /// <c>W</c>, <c>#</c>, <c>H</c> in each of its forms, ranges that wrap, a textual day of week
    /// with a step, seconds, years, the five-field Unix dialect - and the ways each of them is
    /// written wrong.
    /// </summary>
    private static readonly (string Expression, CronFormat Format)[] corpus =
    [
        // Everyday Quartz expressions.
        ("0 0 12 * * ?", CronFormat.Quartz),
        ("0 15 10 ? * *", CronFormat.Quartz),
        ("0 15 10 * * ?", CronFormat.Quartz),
        ("0 15 10 * * ? 2025", CronFormat.Quartz),
        ("0 * 14 * * ?", CronFormat.Quartz),
        ("0 0/5 14 * * ?", CronFormat.Quartz),
        ("0 0/5 14,18 * * ?", CronFormat.Quartz),
        ("0 0-5 14 * * ?", CronFormat.Quartz),
        ("0 10,44 14 ? 3 WED", CronFormat.Quartz),
        ("0 15 10 ? * MON-FRI", CronFormat.Quartz),
        ("0 15 10 15 * ?", CronFormat.Quartz),
        ("0 0 12 1/5 * ?", CronFormat.Quartz),
        ("0 11 11 11 11 ?", CronFormat.Quartz),
        ("* * * * * ?", CronFormat.Quartz),
        ("30 30 12 * * ?", CronFormat.Quartz),
        ("0 0/15 * * * ?", CronFormat.Quartz),
        ("0 0,30 8-17 ? * MON-FRI", CronFormat.Quartz),
        ("0 0 0 1,15 * ?", CronFormat.Quartz),
        ("0 5 0 * 8 ?", CronFormat.Quartz),
        ("0 0 0 29 2 ?", CronFormat.Quartz),
        ("0 0 12 ? JAN,MAR,MAY *", CronFormat.Quartz),
        ("0 0 12 ? * 2,4,6", CronFormat.Quartz),
        ("0 0 0 ? * SUN", CronFormat.Quartz),
        ("0 0 0 ? * 7", CronFormat.Quartz),
        ("0 0 0 ? * 1", CronFormat.Quartz),
        ("0 0 0 * * ? *", CronFormat.Quartz),

        // Macros.
        ("@yearly", CronFormat.Quartz),
        ("@annually", CronFormat.Quartz),
        ("@monthly", CronFormat.Quartz),
        ("@weekly", CronFormat.Quartz),
        ("@daily", CronFormat.Quartz),
        ("@midnight", CronFormat.Quartz),
        ("@hourly", CronFormat.Quartz),
        ("@YEARLY", CronFormat.Quartz),
        ("@reboot", CronFormat.Quartz),
        ("@nonsense", CronFormat.Quartz),
        ("@", CronFormat.Quartz),

        // L, W and # - Quartz's own day-of-month and day-of-week modifiers.
        ("0 15 10 L * ?", CronFormat.Quartz),
        ("0 15 10 L-2 * ?", CronFormat.Quartz),
        ("0 15 10 ? * 6L", CronFormat.Quartz),
        ("0 15 10 ? * FRIL", CronFormat.Quartz),
        ("0 15 10 ? * 6L 2025-2027", CronFormat.Quartz),
        ("0 15 10 ? * 6#3", CronFormat.Quartz),
        ("0 0 12 ? * 1#1", CronFormat.Quartz),
        ("0 0 0 1W * ?", CronFormat.Quartz),
        ("0 0 0 15W * ?", CronFormat.Quartz),
        ("0 0 0 LW * ?", CronFormat.Quartz),
        ("0 0 0 L-3W * ?", CronFormat.Quartz),
        ("0 0 0 LW-4 * ?", CronFormat.Quartz),
        ("0 0 12 L * ? 2027", CronFormat.Quartz),
        ("0 0 12 L-31 * ?", CronFormat.Quartz),
        ("0 0 12 ? * MON#6", CronFormat.Quartz),
        ("0 0 12 ? * MON#0", CronFormat.Quartz),
        ("0 0 12 ? * L,MON", CronFormat.Quartz),
        ("0 0 12 ? * MON#2,TUE", CronFormat.Quartz),
        ("0 0 12 ? * LW", CronFormat.Quartz),

        // Ranges that wrap, which are a Quartz superset over standard cron.
        ("0 0 22-2 * * ?", CronFormat.Quartz),
        ("0 0 0 ? * FRI-MON", CronFormat.Quartz),
        ("0 0 0 ? * SAT-SUN", CronFormat.Quartz),
        ("0 0 0 ? 11-2 *", CronFormat.Quartz),
        ("0 30-15 * * * ?", CronFormat.Quartz),

        // A textual day of week taking a step, which 4.1 reads and 3.x did not.
        ("0 0 12 ? * MON/2", CronFormat.Quartz),
        ("0 0 12 ? * MON-FRI/2", CronFormat.Quartz),

        // Years.
        ("0 0 12 * * ? 2026-2030", CronFormat.Quartz),
        ("0 0 12 * * ? 2026/2", CronFormat.Quartz),
        ("0 0 0 1 1 ? 2030", CronFormat.Quartz),
        ("0 0 12 * * ? 1969", CronFormat.Quartz),
        ("0 0 12 * * ? 2025-2020", CronFormat.Quartz),

        // H, in every form the hash parser reads - and four ways of writing it wrong.
        ("0 H 3 * * ?", CronFormat.Quartz),
        ("H H * * * ?", CronFormat.Quartz),
        ("H H H * * ?", CronFormat.Quartz),
        ("* * H(9-17) * * ?", CronFormat.Quartz),
        ("* H/15 * * * ?", CronFormat.Quartz),
        ("* H,30,45 * * * ?", CronFormat.Quartz),
        ("* H(0-29)/10 * * * ?", CronFormat.Quartz),
        ("0 H H(9-17) * * ?", CronFormat.Quartz),
        ("0 0 0 ? * H", CronFormat.Quartz),
        ("H(0-5 * * * * ?", CronFormat.Quartz),
        ("H(30-5) * * * * ?", CronFormat.Quartz),
        ("H(0-60) * * * * ?", CronFormat.Quartz),
        ("H/0 * * * * ?", CronFormat.Quartz),
        ("HX * * * * ?", CronFormat.Quartz),

        // Malformed, one way each.
        ("nonsense", CronFormat.Quartz),
        ("", CronFormat.Quartz),
        ("   ", CronFormat.Quartz),
        ("0 0 12 * *", CronFormat.Quartz),
        ("0 0 12 * * ? 2025 extra", CronFormat.Quartz),
        ("60 0 12 * * ?", CronFormat.Quartz),
        ("0 60 12 * * ?", CronFormat.Quartz),
        ("0 0 24 * * ?", CronFormat.Quartz),
        ("0 0 12 32 * ?", CronFormat.Quartz),
        ("0 0 12 * 13 ?", CronFormat.Quartz),
        ("0 0 12 ? * 8", CronFormat.Quartz),
        ("0 0 12 ? * 0", CronFormat.Quartz),
        ("0 0 12 ? * ?", CronFormat.Quartz),
        ("0 0 12 ?x * *", CronFormat.Quartz),
        ("0 0/ * * * ?", CronFormat.Quartz),
        ("0 0 12 */0 * ?", CronFormat.Quartz),
        ("*/70 * * * * ?", CronFormat.Quartz),
        ("0 0 12 1-2-3 * ?", CronFormat.Quartz),
        ("0 0 12 ? * MON-", CronFormat.Quartz),
        ("0 0 12 ? * XYZ", CronFormat.Quartz),
        ("0 0 12 ? XYZ *", CronFormat.Quartz),
        ("0 0 12 * * 5C", CronFormat.Quartz),
        ("0 0 12 * * ? 2025 2026 2027", CronFormat.Quartz),
        ("0 0 12 ** * ?", CronFormat.Quartz),

        // The five-field Unix dialect, read as itself.
        ("30 4 * * 1", CronFormat.Unix),
        ("* * * * *", CronFormat.Unix),
        ("0 0 * * 0", CronFormat.Unix),
        ("0 0 * * 7", CronFormat.Unix),
        ("*/15 * * * *", CronFormat.Unix),
        ("0 12 * * MON-FRI", CronFormat.Unix),
        ("0 0 1 * *", CronFormat.Unix),
        ("5 0 * 8 *", CronFormat.Unix),
        ("15 14 1 * *", CronFormat.Unix),
        ("0 22 * * 1-5", CronFormat.Unix),
        ("23 0-20/2 * * *", CronFormat.Unix),
        ("0 0,12 1 */2 *", CronFormat.Unix),
        ("0 0 13 * FRI", CronFormat.Unix),
        ("0 0 * * 1-7", CronFormat.Unix),
        ("H 4 * * 1", CronFormat.Unix),
        ("@daily", CronFormat.Unix),

        // The five-field dialect, written wrong.
        ("0 0 12 * * ?", CronFormat.Unix),
        ("nonsense", CronFormat.Unix),
        ("* * * *", CronFormat.Unix),
        ("0 0 * * 8", CronFormat.Unix),
        ("0 0 * * 1-9", CronFormat.Unix),
        ("H(0-99) 4 * * 1", CronFormat.Unix),
    ];

    public static IEnumerable<TestCaseData> Corpus()
    {
        foreach ((string expression, CronFormat format) in corpus)
        {
            foreach (bool resolvesHash in new[] { false, true })
            {
                yield return new TestCaseData(expression, format, resolvesHash)
                    .SetArgDisplayNames($"{(expression.Length == 0 ? "<empty>" : expression)} [{format}{(resolvesHash ? ", hash" : "")}]");
            }
        }
    }

    [TestCaseSource(nameof(Corpus))]
    public void TheLinkedParserAnswersExactlyAsQuartzDoes(string expression, CronFormat format, bool resolvesHash)
    {
        string? shipped = ValidateWithQuartz(expression, format, resolvesHash);
        string? linked = LinkedValidator.Validate(expression, (LinkedCronFormat) (int) format, resolvesHash);

        linked.Should().Be(shipped,
            "Quartz.Analyzers links src/Quartz/CronExpression.cs rather than referencing Quartz.dll, so the "
            + "compiler must read this expression exactly as the run time would - including the sentence it "
            + "refuses it with, which is the whole value of linking the parser instead of writing a smaller one");
    }

    /// <summary>
    /// The corpus is wide enough to mean something, measured rather than declared.
    /// </summary>
    [Test]
    public void TheCorpusCoversEnoughOfTheGrammar()
    {
        corpus.Should().HaveCountGreaterThanOrEqualTo(80, "a parity claim over a handful of expressions is not a parity claim");

        corpus.Should().OnlyHaveUniqueItems("a duplicate entry widens the list without widening what it covers");

        int refused = corpus.Count(x => ValidateWithQuartz(x.Expression, x.Format, resolvesHash: false) is not null);

        refused.Should().BeGreaterThanOrEqualTo(25,
            "an agreement about which expressions parse is only worth having alongside an agreement about "
            + "which do not, and about what each of them is refused with");
    }

    /// <summary>
    /// What the shipped parser says, read the way the analyzer's copy reads it.
    /// </summary>
    private static string? ValidateWithQuartz(string expression, CronFormat format, bool resolvesHash)
    {
        try
        {
            _ = resolvesHash
                ? CronExpression.ParseWithHash(expression, format, hashSeed: 0)
                : CronExpression.Parse(expression, format);

            return null;
        }
        catch (FormatException e)
        {
            return e.Message;
        }
        catch (ArgumentException e)
        {
            return e.Message;
        }
    }
}
