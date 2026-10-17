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
/// QZ0007, at every call that schedules a trigger from a cron expression string.
/// </summary>
public class CronFiresEverySecondTest
{
    /// <summary>
    /// The entry points whose expression becomes a trigger's schedule, with <c>{0}</c> where the
    /// expression goes.
    /// </summary>
    private static readonly (string Name, string Template)[] scheduleEntryPoints =
    [
        ("CronScheduleBuilder.Create", "_ = CronScheduleBuilder.Create({0});"),
        ("WithCronSchedule", "_ = TriggerBuilder.Create().WithCronSchedule({0}).Build();"),
        ("CronTriggerImpl ctor", "_ = new Quartz.Impl.Triggers.CronTriggerImpl(\"n\", \"g\", {0});"),
    ];

    /// <summary>
    /// The entry points that read an expression without it being a schedule. A <c>CronCalendar</c>
    /// written <c>* * 9 ? * *</c> excludes every second of nine o'clock, which is what it is for, and a
    /// <c>CronExpression</c> may be read for <c>IsSatisfiedBy</c> as easily as for a trigger.
    /// </summary>
    private static readonly (string Name, string Template)[] otherEntryPoints =
    [
        ("CronExpression ctor", "_ = new CronExpression({0});"),
        ("CronExpression.Parse", "_ = CronExpression.Parse({0});"),
        ("CronExpression.TryParse", "_ = CronExpression.TryParse({0}, out _);"),
        ("CronCalendar ctor", "_ = new Quartz.Impl.Calendar.CronCalendar({0});"),
    ];

    public static IEnumerable<TestCaseData> ScheduleEntryPoints() =>
        scheduleEntryPoints.Select(x => new TestCaseData(x.Template).SetArgDisplayNames(x.Name));

    public static IEnumerable<TestCaseData> OtherEntryPoints() =>
        otherEntryPoints.Select(x => new TestCaseData(x.Template).SetArgDisplayNames(x.Name));

    [TestCaseSource(nameof(ScheduleEntryPoints))]
    public async Task WildcardSecondsUnderARestrictedMinuteIsReportedOnTheLiteral(string template)
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(SnippetFor(template, "\"* */5 * ? * *\""));

        Diagnostic diagnostic = diagnostics.Should().ContainSingle("'* */5 * ? * *' fires every second of every fifth minute (#2055)").Subject;
        diagnostic.Id.Should().Be("QZ0007");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning, "the expression is valid, and firing every second of a minute is occasionally meant");
        diagnostic.SpanText().Should().Be("\"* */5 * ? * *\"", "the squiggle belongs under the expression, not under the whole call");
        diagnostic.GetMessage().Should().Be(
            "'* */5 * ? * *' fires every second of each minute it matches, 60 times a minute, because its seconds field is '*'; did you mean '0 */5 * ? * *'?");
    }

    [Test]
    public async Task WildcardSecondsUnderARestrictedHourIsReported()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(
            Snippet("_ = CronScheduleBuilder.Create(\"* * 9-17 ? * MON-FRI\");"));

        diagnostics.Should().ContainSingle("restricting the hour alone still fires every second of every minute in it")
            .Which.GetMessage().Should().EndWith("did you mean '0 * 9-17 ? * MON-FRI'?");
    }

    [Test]
    public async Task CronTriggerAttributeIsReported()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(JobSnippet("\"* 0 3 * * ?\""));

        Diagnostic diagnostic = diagnostics.Should().ContainSingle("the attribute declares a cron trigger, and this one fires sixty times at three o'clock").Subject;
        diagnostic.Id.Should().Be("QZ0007");
        diagnostic.SpanText().Should().Be("\"* 0 3 * * ?\"");
    }

    [TestCaseSource(nameof(ScheduleEntryPoints))]
    public async Task EverySecondOfEveryMinuteIsNotReported(string template)
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(SnippetFor(template, "\"* * * * * ?\""));

        diagnostics.Should().BeEmpty("'* * * * * ?' restricts nothing, so firing every second is plainly what it says");
    }

    [TestCase("0 */5 * ? * *", TestName = "seconds pinned to zero")]
    [TestCase("*/10 */5 * ? * *", TestName = "seconds stepped")]
    [TestCase("0-59 */5 * ? * *", TestName = "seconds written out as a range")]
    [TestCase("* 0-59 * ? * MON", TestName = "minute and hour unrestricted, only the day")]
    [TestCase("* */1 * ? * *", TestName = "minute step of one")]
    public async Task ExpressionsThatSayWhatTheyMeanAreNotReported(string expression)
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(
            Snippet($"_ = CronScheduleBuilder.Create(\"{expression}\");"));

        diagnostics.Should().BeEmpty("only a bare '*' under a minute or hour that skips values reads as a five-field habit");
    }

    [Test]
    public async Task UnixDialectIsNotReported()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(
            Snippet("_ = CronScheduleBuilder.Create(\"*/5 * * * *\", CronFormat.Unix);"));

        diagnostics.Should().BeEmpty("a crontab line has no seconds field, so it fires at second zero");
    }

    [TestCaseSource(nameof(OtherEntryPoints))]
    public async Task EntryPointsThatAreNotSchedulesAreNotReported(string template)
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(SnippetFor(template, "\"* * 9 ? * *\""));

        diagnostics.Should().BeEmpty("an expression that is not a trigger's schedule may mean every second, as a calendar's exclusion does");
    }

    [Test]
    public async Task InvalidExpressionIsReportedOnlyAsInvalid()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(
            Snippet("_ = CronScheduleBuilder.Create(\"* */5 * * *\");"));

        diagnostics.Should().ContainSingle("five fields do not parse as Quartz cron, and that is the one thing to say")
            .Which.Id.Should().Be("QZ0001");
    }

    [Test]
    public async Task SuggestionKeepsTheRestOfTheExpressionAsWritten()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CronLiteralAnalyzer>(
            Snippet("_ = TriggerBuilder.Create().WithCronSchedule(\"  *\\t0 H(0-5) ? * mon\").Build();"));

        diagnostics.Should().ContainSingle()
            .Which.GetMessage().Should().EndWith("did you mean '0\t0 H(0-5) ? * mon'?", "only the seconds field changes, and the rest is what the caller wrote");
    }

    private static string JobSnippet(string expression)
    {
        return $$"""
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            [QuartzJob]
            [CronTrigger({{expression}})]
            public sealed class CleanupJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """;
    }

    private static string SnippetFor(string template, string expression)
    {
        return Snippet(string.Format(System.Globalization.CultureInfo.InvariantCulture, template, expression));
    }

    private static string Snippet(string statement)
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
            }
            """;
    }
}
