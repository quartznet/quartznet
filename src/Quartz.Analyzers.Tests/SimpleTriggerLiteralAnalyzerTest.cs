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
/// QZ0005, on the interval and the repeat count <c>[SimpleTrigger]</c> takes.
/// </summary>
public class SimpleTriggerLiteralAnalyzerTest
{
    [TestCase("\"00:10:00\"")]
    [TestCase("\"1.00:00:00\"")]
    [TestCase("\"00:00:00.500\"")]
    [TestCase("\"00:10:00\", RepeatCount = -1")]
    [TestCase("\"00:10:00\", RepeatCount = 0")]
    [TestCase("\"00:10:00\", RepeatCount = 12")]
    [TestCase("interval: \"00:10:00\", Name = \"poll\"")]
    public async Task ASchedulesATriggerCanHaveIsNotReported(string arguments)
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<SimpleTriggerLiteralAnalyzer>(Snippet(arguments));

        diagnostics.Should().BeEmpty("a positive interval and a repeat count of -1 or more are what the attribute's constructor accepts");
    }

    [Test]
    public async Task AnIntervalThatIsNotATimeSpanIsReportedOnTheLiteral()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<SimpleTriggerLiteralAnalyzer>(Snippet("\"10 minutes\""));

        Diagnostic diagnostic = diagnostics.Should().ContainSingle().Subject;
        diagnostic.Id.Should().Be("QZ0005");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error, "the generator cannot write a schedule it cannot read, so the job would silently lack it");
        diagnostic.SpanText().Should().Be("\"10 minutes\"");
        diagnostic.GetMessage().Should().Be(
            "'10 minutes' is not a TimeSpan. Spell the trigger's interval the way TimeSpan does, invariantly: \"00:10:00\" for ten minutes, \"1.00:00:00\" for a day.");
    }

    [TestCase("00:00:00")]
    [TestCase("-00:10:00")]
    public async Task AnIntervalThatIsNotPositiveIsReported(string interval)
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<SimpleTriggerLiteralAnalyzer>(Snippet($"\"{interval}\""));

        diagnostics.Should().ContainSingle("a repeating trigger with no time between firings is one Quartz refuses to schedule")
            .Which.GetMessage().Should().Be($"A trigger's interval has to be longer than zero, and '{interval}' is not.");
    }

    [Test]
    public async Task AMissingIntervalIsReported()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<SimpleTriggerLiteralAnalyzer>(Snippet("null!"));

        Diagnostic diagnostic = diagnostics.Should().ContainSingle().Subject;
        diagnostic.GetMessage().Should().Be("The interval is missing: the argument is null.");
        diagnostic.SpanText().Should().Be("null!");
    }

    [Test]
    public async Task ARepeatCountBelowMinusOneIsReportedOnTheValue()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<SimpleTriggerLiteralAnalyzer>(Snippet("\"00:10:00\", RepeatCount = -2"));

        Diagnostic diagnostic = diagnostics.Should().ContainSingle().Subject;
        diagnostic.Id.Should().Be("QZ0005");
        diagnostic.SpanText().Should().Be("-2");
        diagnostic.GetMessage().Should().Be(
            "RepeatCount cannot be -2: it counts the firings after the first, so it is 0 or more, or -1 to repeat forever.");
    }

    [Test]
    public async Task ABadIntervalAndABadRepeatCountAreEachReported()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<SimpleTriggerLiteralAnalyzer>(Snippet("\"soon\", RepeatCount = -5"));

        diagnostics.Select(x => x.SpanText()).Should().Equal(["\"soon\"", "-5"], "each value is its own mistake, with its own fix");
    }

    [Test]
    public async Task ConstantsAreReadTheSameWayLiteralsAre()
    {
        string snippet = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            [QuartzJob]
            [SimpleTrigger(Interval, RepeatCount = Repeats)]
            public class PollJob : IJob
            {
                private const string Interval = "every ten minutes";
                private const int Repeats = -3;

                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<SimpleTriggerLiteralAnalyzer>(snippet);

        diagnostics.Select(x => x.SpanText()).Should().Equal(["Interval", "Repeats"]);
    }

    [Test]
    public async Task AnotherAttributeOfTheSameNameIsNotReported()
    {
        string snippet = """
            using System;

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class SimpleTriggerAttribute : Attribute
            {
                public SimpleTriggerAttribute(string interval) { }

                public int RepeatCount { get; set; }
            }

            [SimpleTrigger("10 minutes", RepeatCount = -2)]
            public class PollJob
            {
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<SimpleTriggerLiteralAnalyzer>(snippet);

        diagnostics.Should().BeEmpty("the attribute is matched by symbol; an attribute of your own with the same name is your own");
    }

    /// <summary>
    /// The analyzer and the attribute say the same thing about the same value, so a build and a
    /// reflection over the attribute cannot disagree about what was wrong with it.
    /// </summary>
    [TestCase("10 minutes")]
    [TestCase("00:00:00")]
    [TestCase("-00:10:00")]
    public void TheAnalyzersMessageIsTheAttributesOwn(string interval)
    {
        string? message = SimpleTriggerLiteralAnalyzer.ValidateInterval(interval, out _);

        Action act = () => _ = new SimpleTriggerAttribute(interval);

        act.Should().Throw<ArgumentException>().Which.Message.Should().StartWith(message!);
    }

    [Test]
    public void TheAnalyzersRepeatCountMessageIsTheAttributesOwn()
    {
        string? message = SimpleTriggerLiteralAnalyzer.ValidateRepeatCount(-2);

        Action act = () => _ = new SimpleTriggerAttribute("00:10:00") { RepeatCount = -2 };

        act.Should().Throw<ArgumentOutOfRangeException>().Which.Message.Should().StartWith(message!);
    }

    private static string Snippet(string arguments)
    {
        return $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            [QuartzJob]
            [SimpleTrigger({{arguments}})]
            public class PollJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """;
    }
}
