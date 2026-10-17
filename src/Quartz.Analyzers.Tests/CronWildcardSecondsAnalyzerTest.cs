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
/// QZ0007: a trigger's cron schedule with <c>*</c> in the seconds field under a restricted minute or
/// hour, which fires every second of each minute it matches.
/// </summary>
public class CronWildcardSecondsAnalyzerTest
{
    /// <summary>
    /// The calls whose expression is a trigger's schedule, with <c>{0}</c> where the expression goes.
    /// </summary>
    private static readonly (string Name, string Template)[] scheduleEntryPoints =
    [
        ("CronScheduleBuilder.Create", "_ = CronScheduleBuilder.Create({0});"),
        ("WithCronSchedule", "_ = TriggerBuilder.Create().WithCronSchedule({0}).Build();"),
        ("CronTriggerImpl ctor", "_ = new Quartz.Impl.Triggers.CronTriggerImpl(\"n\", \"g\", {0});"),
    ];

    /// <summary>
    /// The calls whose expression may be a window of time rather than a schedule.
    /// </summary>
    private static readonly (string Name, string Template)[] windowEntryPoints =
    [
        ("CronCalendar ctor", "_ = new Quartz.Impl.Calendar.CronCalendar({0});"),
        ("CronExpression ctor", "_ = new CronExpression({0});"),
        ("CronExpression.Parse", "_ = CronExpression.Parse({0});"),
        ("CronExpression.TryParse", "_ = CronExpression.TryParse({0}, out _);"),
        ("CronExpression.ParseWithHash", "_ = CronExpression.ParseWithHash({0}, 17);"),
        ("CronExpression.ResolveHash", "_ = CronExpression.ResolveHash({0}, \"key\");"),
    ];

    public static IEnumerable<TestCaseData> ScheduleEntryPoints() =>
        scheduleEntryPoints.Select(x => new TestCaseData(x.Template).SetArgDisplayNames(x.Name));

    public static IEnumerable<TestCaseData> WindowEntryPoints() =>
        windowEntryPoints.Select(x => new TestCaseData(x.Template).SetArgDisplayNames(x.Name));

    [TestCase("* */5 * ? * *", "0 */5 * ? * *")]
    [TestCase("* 0 9 * * ?", "0 0 9 * * ?")]
    [TestCase("* * 9-17 * * ?", "0 * 9-17 * * ?")]
    public async Task WildcardSecondsUnderARestrictedMinuteOrHourIsReported(string expression, string suggested)
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(
            Snippet($"_ = TriggerBuilder.Create().WithCronSchedule(\"{expression}\").Build();"));

        Diagnostic diagnostic = diagnostics.Should().ContainSingle().Subject;
        diagnostic.Id.Should().Be("QZ0007");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning, "the expression is valid and every second can be what was meant");
        diagnostic.SpanText().Should().Be($"\"{expression}\"", "the squiggle belongs under the expression, not under the whole call");
        diagnostic.GetMessage().Should().Be(
            $"'{expression}' fires 60 times in each minute it matches, because its seconds field is '*'; "
            + $"write '{suggested}' to fire once in each of those minutes, or '0-59' for the seconds if every second is intended",
            "the message has to name both ways out: the fix most authors meant, and the spelling that keeps every second");
    }

    [TestCase("* * * * * ?", "nothing is restricted, so every second is plainly what it says")]
    [TestCase("0-59 */5 * ? * *", "0-59 is every second written on purpose")]
    [TestCase("*/1 */5 * ? * *", "*/1 is every second written on purpose")]
    [TestCase("0 */5 * ? * *", "this is the fix: once every five minutes")]
    [TestCase("* */1 * * * ?", "*/1 in the minute field names every minute, so it restricts nothing")]
    [TestCase("* 0-59 0-23 * * ?", "a minute and an hour field that name every value restrict nothing however they are spelled")]
    [TestCase("@hourly", "a macro expands with a seconds field of 0")]
    public async Task ShapesThatSayWhatTheyMeanAreNotReported(string expression, string because)
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(
            Snippet($"_ = TriggerBuilder.Create().WithCronSchedule(\"{expression}\").Build();"));

        diagnostics.Should().BeEmpty(because);
    }

    [Test]
    public async Task AUnixExpressionHasNoSecondsFieldToReport()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(
            Snippet("_ = CronScheduleBuilder.Create(\"* */5 * * *\", CronFormat.Unix);"));

        diagnostics.Should().BeEmpty("the five-field form starts at the minute and is read with a seconds field of 0");
    }

    [Test]
    public async Task ConstantIsReadTheSameWayALiteralIs()
    {
        string snippet = Snippet(
            "_ = TriggerBuilder.Create().WithCronSchedule(Schedule).Build();",
            "    private const string Schedule = \"* */5 * ? * *\";");

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(snippet);

        Diagnostic diagnostic = diagnostics.Should().ContainSingle("a const is a literal by the time the semantic model sees it").Subject;
        diagnostic.Id.Should().Be("QZ0007");
        diagnostic.SpanText().Should().Be("Schedule");
        diagnostic.GetMessage().Should().StartWith("'* */5 * ? * *' fires 60 times",
            "the squiggle is under the constant's name, so the message has to say what the constant holds");
    }

    [TestCase("* */5 * * *", "five fields are the Unix form, which the default format refuses")]
    [TestCase("* 60 * * * ?", "a minute of 60 does not exist")]
    [TestCase("* */5 * * * ? 1969", "a year before 1970 is refused")]
    public async Task AnExpressionThatCannotParseIsReportedOnlyAsInvalid(string expression, string because)
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(
            Snippet($"_ = TriggerBuilder.Create().WithCronSchedule(\"{expression}\").Build();"));

        diagnostics.Should().ContainSingle(because).Which.Id.Should().Be("QZ0001",
            "an expression that does not parse has no seconds field to speak of, so QZ0001 says everything there is to say");
    }

    [TestCaseSource(nameof(ScheduleEntryPoints))]
    public async Task EveryCallThatSchedulesATriggerIsChecked(string template)
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(SnippetFor(template, "\"* */5 * ? * *\""));

        diagnostics.Should().ContainSingle("the expression is the trigger's schedule").Which.Id.Should().Be("QZ0007");
    }

    [TestCaseSource(nameof(WindowEntryPoints))]
    public async Task ACallWhoseExpressionMayBeAWindowIsNotChecked(string template)
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(SnippetFor(template, "\"* * 0-7 ? * *\""));

        diagnostics.Should().BeEmpty("'* * 0-7 ? * *' is how a calendar or an IsSatisfiedBy check writes the window from midnight to eight");
    }

    [Test]
    public async Task CronTriggerAttributeIsChecked()
    {
        string snippet = """
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            [QuartzJob]
            [CronTrigger("* */5 * ? * *")]
            public sealed class CleanupJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(snippet);

        diagnostics.Should().ContainSingle("the attribute declares the job's trigger, so its expression is a schedule")
            .Which.SpanText().Should().Be("\"* */5 * ? * *\"");
    }

    /// <summary>
    /// An <c>H</c> in the minute field resolves to one minute, which restricts it; the suggestion keeps the
    /// <c>H</c> as written, since the literal is what the author edits.
    /// </summary>
    [Test]
    public async Task HashTokenRestrictsAndIsKeptInTheSuggestion()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(
            Snippet("_ = TriggerBuilder.Create().WithCronSchedule(\"* H * * * ?\").Build();"));

        diagnostics.Should().ContainSingle().Which.GetMessage().Should().Contain("write '0 H * * * ?'");
    }

    private static string SnippetFor(string template, string expression)
    {
        return Snippet(string.Format(System.Globalization.CultureInfo.InvariantCulture, template, expression));
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
