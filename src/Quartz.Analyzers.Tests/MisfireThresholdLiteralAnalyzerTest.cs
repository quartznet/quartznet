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
/// QZ0008, on the <c>MisfireThreshold</c> both <c>[CronTrigger]</c> and <c>[SimpleTrigger]</c> take.
/// </summary>
public class MisfireThresholdLiteralAnalyzerTest
{
    [TestCase("CronTrigger(\"0 0 * * * ?\"")]
    [TestCase("SimpleTrigger(\"00:10:00\"")]
    public async Task AThresholdATriggerCanHaveIsNotReported(string attribute)
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<MisfireThresholdLiteralAnalyzer>(
            Snippet(attribute + ", MisfireThreshold = \"00:05:00\")"));

        diagnostics.Should().BeEmpty("a positive whole number of milliseconds is what the attributes' property accepts");
    }

    [TestCase("CronTrigger(\"0 0 * * * ?\"")]
    [TestCase("SimpleTrigger(\"00:10:00\"")]
    public async Task AThresholdThatIsNotATimeSpanIsReportedOnTheLiteral(string attribute)
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<MisfireThresholdLiteralAnalyzer>(
            Snippet(attribute + ", MisfireThreshold = \"five minutes\")"));

        Diagnostic diagnostic = diagnostics.Should().ContainSingle().Subject;
        diagnostic.Id.Should().Be("QZ0008");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error, "the generator cannot write a threshold it cannot read");
        diagnostic.SpanText().Should().Be("\"five minutes\"");
        diagnostic.GetMessage().Should().Be(
            "'five minutes' is not a TimeSpan. Spell the trigger's misfire threshold the way TimeSpan does, invariantly: \"00:05:00\" for five minutes.");
    }

    [TestCase("00:00:00", "A trigger's misfire threshold has to be longer than zero, and '00:00:00' is not.")]
    [TestCase("-00:05:00", "A trigger's misfire threshold has to be longer than zero, and '-00:05:00' is not.")]
    [TestCase("00:00:00.0001", "A trigger's misfire threshold is a whole number of milliseconds, and '00:00:00.0001' is not.")]
    public async Task AThresholdNoTriggerCanHaveIsReported(string threshold, string message)
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<MisfireThresholdLiteralAnalyzer>(
            Snippet($"CronTrigger(\"0 0 * * * ?\", MisfireThreshold = \"{threshold}\")"));

        diagnostics.Should().ContainSingle().Which.GetMessage().Should().Be(message);
    }

    [Test]
    public async Task AnotherAttributeOfTheSameNameIsNotReported()
    {
        string snippet = """
            using System;

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class CronTriggerAttribute : Attribute
            {
                public CronTriggerAttribute(string expression) { }

                public string MisfireThreshold { get; set; }
            }

            [CronTrigger("0 0 * * * ?", MisfireThreshold = "soon")]
            public class ReportJob
            {
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<MisfireThresholdLiteralAnalyzer>(snippet);

        diagnostics.Should().BeEmpty("the attribute is matched by symbol; an attribute of your own with the same name is your own");
    }

    /// <summary>
    /// The analyzer and the attributes say the same thing about the same value.
    /// </summary>
    [TestCase("five minutes")]
    [TestCase("00:00:00")]
    [TestCase("00:00:00.0001")]
    public void TheAnalyzersMessageIsTheAttributesOwn(string threshold)
    {
        string? message = MisfireThresholdLiteralAnalyzer.Validate(threshold, out _);

        Action cron = () => _ = new CronTriggerAttribute("0 0 * * * ?") { MisfireThreshold = threshold };
        Action simple = () => _ = new SimpleTriggerAttribute("00:10:00") { MisfireThreshold = threshold };

        cron.Should().Throw<ArgumentException>().Which.Message.Should().StartWith(message!);
        simple.Should().Throw<ArgumentException>().Which.Message.Should().StartWith(message!);
    }

    private static string Snippet(string attribute)
    {
        return $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            [QuartzJob]
            [{{attribute}}]
            public class ReportJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """;
    }
}
