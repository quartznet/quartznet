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
/// QZ0004, on a job body that awaits or loops and never reads the token that would stop it.
/// </summary>
public class CancellationTokenAnalyzerTest
{
    [Test]
    public async Task AnAwaitWithoutTheTokenIsReportedOnTheMethodName()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CancellationTokenAnalyzer>(Snippet(
            "await Task.Delay(1000);"));

        Diagnostic diagnostic = diagnostics.Should().ContainSingle().Subject;
        diagnostic.Id.Should().Be("QZ0004");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Info, "whether a piece of work is interruptible is a judgement, so this informs rather than warns");
        diagnostic.SpanText().Should().Be("Execute");
        diagnostic.GetMessage().Should().Contain("MyJob.Execute");
    }

    [Test]
    public async Task ALoopWithoutTheTokenIsReported()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CancellationTokenAnalyzer>(Snippet(
            "for (int i = 0; i < 1000; i++) { Work(); }",
            "private static void Work() { }"));

        diagnostics.Should().ContainSingle("a loop is work a cancellation could shorten, and nothing here reads the token");
    }

    [Test]
    public async Task NeitherAnAwaitNorALoopIsNotReported()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CancellationTokenAnalyzer>(Snippet(
            "Work();",
            "private static void Work() { }"));

        diagnostics.Should().BeEmpty("a job that returns straight away has nothing to interrupt, and CA2016 covers the calls that take a token");
    }

    [Test]
    public async Task ForwardingTheParameterIsObservingIt()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CancellationTokenAnalyzer>(Snippet(
            "await Task.Delay(1000, cancellationToken);"));

        diagnostics.Should().BeEmpty("the token reaches the call that can honour it");
    }

    [Test]
    public async Task ReadingTheContextsTokenIsObservingIt()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CancellationTokenAnalyzer>(Snippet(
            "await Task.Delay(1000, context.CancellationToken);"));

        diagnostics.Should().BeEmpty("IJobExecutionContext.CancellationToken is the same token the parameter carries");
    }

    [Test]
    public async Task ThrowIfCancellationRequestedInsideTheLoopIsObservingIt()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CancellationTokenAnalyzer>(Snippet(
            "for (int i = 0; i < 1000; i++) { cancellationToken.ThrowIfCancellationRequested(); }"));

        diagnostics.Should().BeEmpty("a loop that checks the token each turn is exactly what this asks for");
    }

    [Test]
    public async Task AGenericJobIsReadTheSameWay()
    {
        string snippet = """
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            public record Input(int Count);

            public class MyJob : IJob<Input>
            {
                public async ValueTask Execute(IJobExecutionContext context, Input input, CancellationToken cancellationToken = default)
                {
                    await Task.Delay(input.Count);
                }
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CancellationTokenAnalyzer>(snippet);

        diagnostics.Should().ContainSingle("IJob<TInput>.Execute is handed the same token, and this one never looks at it");
    }

    [Test]
    public async Task AnExplicitImplementationIsReadTheSameWay()
    {
        string snippet = """
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            public class MyJob : IJob
            {
                async ValueTask IJob.Execute(IJobExecutionContext context, CancellationToken cancellationToken)
                {
                    await Task.Delay(1000);
                }
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CancellationTokenAnalyzer>(snippet);

        diagnostics.Should().ContainSingle("how the interface is implemented does not change what the token is for");
    }

    [Test]
    public async Task AnOverrideOfAnAbstractBaseJobIsReadTheSameWay()
    {
        string snippet = """
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            public abstract class JobBase : IJob
            {
                public abstract ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default);
            }

            public sealed class MyJob : JobBase
            {
                public override async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
                {
                    await Task.Delay(1000);
                }
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CancellationTokenAnalyzer>(snippet);

        Diagnostic diagnostic = diagnostics.Should().ContainSingle(
            "the scheduler calls IJob.Execute, which runs the override, so the override is the body the token is handed to").Subject;

        diagnostic.GetMessage().Should().Contain("MyJob.Execute");
    }

    /// <summary>
    /// Two overrides deep, one forwarding the token and the one below it not.
    /// </summary>
    [Test]
    public async Task AnOverrideOfAVirtualBaseJobIsReadTheSameWay()
    {
        string snippet = """
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            public class JobBase : IJob
            {
                public virtual ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }

            public class PausingJob : JobBase
            {
                public override async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
                {
                    await Task.Delay(1000, cancellationToken);
                }
            }

            public sealed class MyJob : PausingJob
            {
                public override async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
                {
                    await Task.Delay(1000);
                }
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CancellationTokenAnalyzer>(snippet);

        Diagnostic diagnostic = diagnostics.Should().ContainSingle(
            "the base neither awaits nor loops, the first override forwards the token, and only the second ignores it").Subject;

        diagnostic.GetMessage().Should().Contain("MyJob.Execute");
    }

    [Test]
    public async Task APartialImplementationIsReadTheSameWay()
    {
        string snippet = """
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            public partial class MyJob : IJob
            {
                public partial ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default);
            }

            public partial class MyJob
            {
                public partial async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
                {
                    await Task.Delay(1000);
                }
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CancellationTokenAnalyzer>(snippet);

        Diagnostic diagnostic = diagnostics.Should().ContainSingle(
            "IJob.Execute is implemented by the partial method's declaration, and its body is in the other part").Subject;

        diagnostic.SpanText().Should().Be("Execute");
        diagnostic.Location.GetLineSpan().StartLinePosition.Line.Should().Be(12, "the report goes where the body is");
    }

    [Test]
    public async Task AMethodThatHidesTheBaseJobsExecuteIsNotReported()
    {
        string snippet = """
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            public class JobBase : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }

            public sealed class HidingJob : JobBase
            {
                public new async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
                {
                    await Task.Delay(1000);
                }
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CancellationTokenAnalyzer>(snippet);

        diagnostics.Should().BeEmpty("a 'new' method overrides nothing, and IJob.Execute on HidingJob still runs the base's body");
    }

    [Test]
    public async Task AMethodCalledExecuteOnATypeThatIsNotAJobIsNotReported()
    {
        string snippet = """
            using System.Threading;
            using System.Threading.Tasks;

            public class NotAJob
            {
                public async ValueTask Execute(object context, CancellationToken cancellationToken = default)
                {
                    await Task.Delay(1000);
                }
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<CancellationTokenAnalyzer>(snippet);

        diagnostics.Should().BeEmpty("the method is found through IJob.Execute rather than by its name");
    }

    private static string Snippet(string body, string? extraMember = null)
    {
        return $$"""
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            public class MyJob : IJob
            {
                public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
                {
                    {{body}}
                }

                {{extraMember}}
            }
            """;
    }
}
