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
/// QZ0002, on the one argument <c>[JobTimeout]</c> takes.
/// </summary>
public class JobTimeoutLiteralAnalyzerTest
{
    [TestCase("00:05:00")]
    [TestCase("1.00:00:00")]
    [TestCase("00:00:00")]
    [TestCase("0:00:00.500")]
    public async Task TimeSpanLiteralIsNotReported(string timeout)
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<JobTimeoutLiteralAnalyzer>(Snippet($"\"{timeout}\""));

        diagnostics.Should().BeEmpty("the attribute's constructor parses this, and zero is how a job says it has no timeout");
    }

    [Test]
    public async Task ALiteralThatIsNotATimeSpanIsReportedOnTheLiteral()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<JobTimeoutLiteralAnalyzer>(Snippet("\"5 minutes\""));

        Diagnostic diagnostic = diagnostics.Should().ContainSingle().Subject;
        diagnostic.Id.Should().Be("QZ0002");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.SpanText().Should().Be("\"5 minutes\"");
        diagnostic.GetMessage().Should().Be(
            "'5 minutes' is not a TimeSpan. Spell the job's timeout the way TimeSpan does, invariantly: \"00:05:00\" for five minutes, \"1.00:00:00\" for a day.",
            "the analyzer says at build time exactly what the attribute's constructor would have said at run time");
    }

    [Test]
    public async Task ANegativeTimeSpanIsReported()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<JobTimeoutLiteralAnalyzer>(Snippet("\"-00:05:00\""));

        diagnostics.Should().ContainSingle().Which.GetMessage().Should().Be(
            "A job's timeout cannot be negative, and '-00:05:00' is. Use \"00:00:00\" to say the job has no timeout.");
    }

    [Test]
    public async Task AConstantIsReadTheSameWayALiteralIs()
    {
        string snippet = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            [JobTimeout(Budget)]
            public class MyJob : IJob
            {
                private const string Budget = "5 minutes";

                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<JobTimeoutLiteralAnalyzer>(snippet);

        diagnostics.Should().ContainSingle().Which.SpanText().Should().Be("Budget");
    }

    [Test]
    public async Task ANonConstantArgumentIsNotReported()
    {
        // An attribute argument has to be a constant expression, so the only way to write one whose
        // value the semantic model does not know is to write one that is not a string at all.
        string snippet = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            [JobTimeout(nameof(MyJob))]
            public class MyJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<JobTimeoutLiteralAnalyzer>(snippet);

        diagnostics.Should().ContainSingle("nameof folds to a constant string, and 'MyJob' is not a TimeSpan");
    }

    [Test]
    public async Task AnotherAttributeTakingAStringIsNotReported()
    {
        string snippet = """
            using System;

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class JobTimeoutAttribute : Attribute
            {
                public JobTimeoutAttribute(string timeout) { }
            }

            [JobTimeout("5 minutes")]
            public class MyJob
            {
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<JobTimeoutLiteralAnalyzer>(snippet);

        diagnostics.Should().BeEmpty("the attribute is matched by symbol; an attribute of your own with the same name is your own");
    }

    private static string Snippet(string argument)
    {
        return $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            [JobTimeout({{argument}})]
            public class MyJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """;
    }
}
