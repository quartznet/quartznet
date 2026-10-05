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
/// QZ0006, on every delay <c>[RetryPolicy]</c> can be written with.
/// </summary>
public class RetryPolicyLiteralAnalyzerTest
{
    [TestCase("3, \"00:05:00\"")]
    [TestCase("5, \"00:00:30\", 2, MaxDelay = \"00:10:00\", Jitter = 0.2")]
    [TestCase("\"00:00:10\", \"00:01:00\", \"1.00:00:00\"")]
    [TestCase("0")]
    [TestCase("3, \"00:00:00\"")]
    public async Task DelaysTheConstructorParsesAreNotReported(string arguments)
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<RetryPolicyLiteralAnalyzer>(Snippet(arguments));

        diagnostics.Should().BeEmpty("every delay here is an invariant TimeSpan, and a wait of zero is a wait");
    }

    [Test]
    public async Task ADelayThatIsNotATimeSpanIsReportedOnTheLiteral()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<RetryPolicyLiteralAnalyzer>(Snippet("3, \"5 minutes\""));

        Diagnostic diagnostic = diagnostics.Should().ContainSingle().Subject;
        diagnostic.Id.Should().Be("QZ0006");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.SpanText().Should().Be("\"5 minutes\"");
        diagnostic.GetMessage().Should().Be(
            "'5 minutes' is not a TimeSpan. Spell a retry delay the way TimeSpan does, invariantly: \"00:00:30\" for thirty seconds, \"1.00:00:00\" for a day.",
            "the analyzer says at build time exactly what the attribute's constructor would have said when the job was added");
    }

    [Test]
    public async Task ANegativeDelayIsReported()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<RetryPolicyLiteralAnalyzer>(Snippet("3, \"-00:00:30\""));

        diagnostics.Should().ContainSingle().Which.GetMessage().Should().Be(
            "A retry delay of -00:00:30 is negative; a retry cannot be scheduled into the past.");
    }

    [Test]
    public async Task EachBadDelayOfTheExplicitFormIsReportedOnItsOwn()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<RetryPolicyLiteralAnalyzer>(Snippet("\"00:00:10\", \"soon\", \"later\""));

        diagnostics.Select(x => x.SpanText()).Should().Equal(["\"soon\"", "\"later\""]);
    }

    [Test]
    public async Task TheCeilingIsADelayToo()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<RetryPolicyLiteralAnalyzer>(Snippet("5, \"00:00:30\", 2, MaxDelay = \"ten minutes\""));

        diagnostics.Should().ContainSingle().Which.SpanText().Should().Be("\"ten minutes\"");
    }

    [Test]
    public async Task AConstantIsReadTheSameWayALiteralIs()
    {
        string snippet = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            [RetryPolicy(3, Wait)]
            public class MyJob : IJob
            {
                private const string Wait = "half a minute";

                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<RetryPolicyLiteralAnalyzer>(snippet);

        diagnostics.Should().ContainSingle().Which.SpanText().Should().Be("Wait");
    }

    [Test]
    public async Task AnotherAttributeOfTheSameNameIsNotReported()
    {
        string snippet = """
            using System;

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class RetryPolicyAttribute : Attribute
            {
                public RetryPolicyAttribute(int maxAttempts, string delay) { }
            }

            [RetryPolicy(3, "5 minutes")]
            public class MyJob
            {
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<RetryPolicyLiteralAnalyzer>(snippet);

        diagnostics.Should().BeEmpty("the attribute is matched by symbol; an attribute of your own with the same name is your own");
    }

    private static string Snippet(string arguments)
    {
        return $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            [RetryPolicy({{arguments}})]
            public class MyJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """;
    }
}
