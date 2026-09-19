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

using Microsoft.CodeAnalysis;

using QuartzAnalyzers::Quartz.Analyzers;

namespace Quartz.Analyzers.Tests;

/// <summary>
/// QZ0001, at every call that reads a cron expression string.
/// </summary>
public class CronLiteralAnalyzerTest
{
    /// <summary>
    /// One snippet per entry point, each written the way a reader would write it, with
    /// <c>{0}</c> where the expression goes.
    /// </summary>
    private static readonly (string Name, string Template)[] entryPoints =
    [
        ("CronScheduleBuilder.Create", "_ = CronScheduleBuilder.Create({0});"),
        ("WithCronSchedule", "_ = TriggerBuilder.Create().WithCronSchedule({0}).Build();"),
        ("CronExpression ctor", "_ = new CronExpression({0});"),
        ("CronExpression ctor with zone", "_ = new CronExpression({0}, TimeZoneInfo.Utc);"),
        ("CronExpression.Parse", "_ = CronExpression.Parse({0});"),
        ("CronExpression.TryParse", "_ = CronExpression.TryParse({0}, out _);"),
        ("CronExpression.ParseWithHash", "_ = CronExpression.ParseWithHash({0}, 17);"),
        ("CronExpression.TryParseWithHash", "_ = CronExpression.TryParseWithHash({0}, \"key\", out _);"),
        ("CronExpression.ResolveHash", "_ = CronExpression.ResolveHash({0}, \"key\");"),
        ("CronCalendar ctor", "_ = new Quartz.Impl.Calendar.CronCalendar({0});"),
        ("CronTriggerImpl ctor", "_ = new Quartz.Impl.Triggers.CronTriggerImpl(\"n\", \"g\", {0});"),
    ];

    public static IEnumerable<TestCaseData> EntryPoints() =>
        entryPoints.Select(x => new TestCaseData(x.Template).SetArgDisplayNames(x.Name));

    [TestCaseSource(nameof(EntryPoints))]
    public async Task ValidExpressionIsNotReported(string template)
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(SnippetFor(template, "\"0 0 12 * * ?\""));

        diagnostics.Should().BeEmpty("noon every day parses, and an analyzer that reports a valid expression is a build break it invented");
    }

    [TestCaseSource(nameof(EntryPoints))]
    public async Task InvalidExpressionIsReportedOnTheLiteral(string template)
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(SnippetFor(template, "\"nonsense\""));

        Diagnostic diagnostic = diagnostics.Should().ContainSingle().Subject;
        diagnostic.Id.Should().Be("QZ0001");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error, "there is no reading of the program in which this literal works");
        diagnostic.SpanText().Should().Be("\"nonsense\"", "the squiggle belongs under the expression, not under the whole call");
        diagnostic.GetMessage().Should().StartWith("'nonsense' is not a valid cron expression: ");
    }

    [TestCaseSource(nameof(EntryPoints))]
    public async Task NonConstantExpressionIsNotReported(string template)
    {
        string snippet = SnippetFor(template, "Read()", "    private static string Read() => \"nonsense\";");

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(snippet);

        diagnostics.Should().BeEmpty("the compiler does not know what this string is, so neither does the analyzer");
    }

    [Test]
    public async Task ConstantIsReadTheSameWayALiteralIs()
    {
        string snippet = Snippet("_ = CronExpression.Parse(Expression);", "    private const string Expression = \"0 0 12 * *\";");

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(snippet);

        diagnostics.Should().ContainSingle("a const is a literal by the time the semantic model sees it")
            .Which.SpanText().Should().Be("Expression");
    }

    [Test]
    public async Task InterpolatedStringWithNoHolesIsReadAsALiteral()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(Snippet("_ = CronExpression.Parse($\"0 0 12 * *\");"));

        diagnostics.Should().ContainSingle("an interpolation with nothing in it is a constant string, and the compiler folds it to one");
    }

    [Test]
    public async Task InterpolatedStringWithAHoleIsNotRead()
    {
        string snippet = Snippet("_ = CronExpression.Parse($\"0 0 {Hour} * *\");", "    private static int Hour => 12;");

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(snippet);

        diagnostics.Should().BeEmpty("the hole is filled at run time, so the expression this becomes is unknown here");
    }

    [Test]
    public async Task FiveFieldsAreValidInTheUnixDialectAndNotInTheQuartzOne()
    {
        IReadOnlyList<Diagnostic> unix = await AnalyzerRunner.Run<CronLiteralAnalyzer>(
            Snippet("_ = CronExpression.Parse(\"30 4 * * 1\", CronFormat.Unix);"));

        unix.Should().BeEmpty("'30 4 * * 1' is a crontab line, and CronFormat.Unix is how a caller says so");

        IReadOnlyList<Diagnostic> quartz = await AnalyzerRunner.Run<CronLiteralAnalyzer>(
            Snippet("_ = CronExpression.Parse(\"30 4 * * 1\", CronFormat.Quartz);"));

        quartz.Should().ContainSingle("Quartz cron has six or seven fields, seconds first")
            .Which.Id.Should().Be("QZ0001");
    }

    [Test]
    public async Task SixFieldsAreNotValidInTheUnixDialect()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(
            Snippet("_ = CronScheduleBuilder.Create(\"0 0 12 * * ?\", CronFormat.Unix);"));

        diagnostics.Should().ContainSingle("the Unix form has exactly five fields, and this is the Quartz one")
            .Which.GetMessage().Should().Contain("has 6 fields");
    }

    [Test]
    public async Task NonConstantFormatSkipsTheCheckAltogether()
    {
        string snippet = Snippet(
            "_ = CronExpression.Parse(\"30 4 * * 1\", Format);",
            "    private static CronFormat Format => CronFormat.Unix;");

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(snippet);

        diagnostics.Should().BeEmpty(
            "the same five fields are a valid crontab line and an invalid Quartz expression, so a dialect "
            + "the compiler does not know leaves nothing to check rather than something to guess");
    }

    /// <summary>
    /// The <c>H</c> token is where the entry points genuinely differ: the ones that resolve it accept
    /// an expression the ones that do not reject.
    /// </summary>
    [Test]
    public async Task HashTokenIsAcceptedOnlyWhereTheEntryPointResolvesIt()
    {
        IReadOnlyList<Diagnostic> deferred = await AnalyzerRunner.Run<CronLiteralAnalyzer>(
            Snippet("_ = TriggerBuilder.Create().WithCronSchedule(\"0 H 3 * * ?\").Build();"));

        deferred.Should().BeEmpty("WithCronSchedule resolves H against the trigger key, so an H expression is valid there");

        IReadOnlyList<Diagnostic> immediate = await AnalyzerRunner.Run<CronLiteralAnalyzer>(
            Snippet("_ = CronExpression.Parse(\"0 H 3 * * ?\");"));

        immediate.Should().ContainSingle("CronExpression.Parse has no seed to resolve H with, and says so")
            .Which.Id.Should().Be("QZ0001");
    }

    [Test]
    public async Task AMethodOfSomebodyElsesWithTheSameNameIsNotACronEntryPoint()
    {
        string snippet = """
            using System;

            namespace Mine
            {
                public static class CronExpression
                {
                    public static object Parse(string cronExpression) => cronExpression;
                }

                public class Caller
                {
                    public void Call()
                    {
                        _ = CronExpression.Parse("nonsense");
                    }
                }
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(snippet);

        diagnostics.Should().BeEmpty("the entry points are matched by symbol, so a name is not enough to be one");
    }

    private static string SnippetFor(string template, string expression, string? extraMember = null)
    {
        return Snippet(string.Format(System.Globalization.CultureInfo.InvariantCulture, template, expression), extraMember);
    }

    private static string Snippet(string statement, string? extraMember = null)
    {
        return $$"""
            using System;

            using Quartz;

            public class Caller
            {
                public void Call()
                {
                    {{statement}}
                }

            {{extraMember}}
            }
            """;
    }
}
