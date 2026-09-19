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
/// QZ0003, read the way <c>JobDetailImpl</c> reads the two attributes: the type, a base class, or
/// any interface it implements.
/// </summary>
public class JobAttributesAnalyzerTest
{
    [Test]
    public async Task PersistWithoutDisallowIsReportedOnTheTypeName()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<JobAttributesAnalyzer>(Snippet(
            "[PersistJobDataAfterExecution]",
            "public class MyJob : IJob"));

        Diagnostic diagnostic = diagnostics.Should().ContainSingle().Subject;
        diagnostic.Id.Should().Be("QZ0003");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning, "it is a race rather than a contradiction");
        diagnostic.SpanText().Should().Be("MyJob");
        diagnostic.GetMessage().Should().Contain("the later one wins");
    }

    [Test]
    public async Task BothAttributesIsNotReported()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<JobAttributesAnalyzer>(Snippet(
            "[PersistJobDataAfterExecution]\n[DisallowConcurrentExecution]",
            "public class MyJob : IJob"));

        diagnostics.Should().BeEmpty("one firing at a time is what makes writing the map back safe");
    }

    [Test]
    public async Task NeitherAttributeIsNotReported()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<JobAttributesAnalyzer>(Snippet(
            "",
            "public class MyJob : IJob"));

        diagnostics.Should().BeEmpty("a job that does not write its data map back has no race to lose");
    }

    [Test]
    public async Task DisallowAloneIsNotReported()
    {
        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<JobAttributesAnalyzer>(Snippet(
            "[DisallowConcurrentExecution]",
            "public class MyJob : IJob"));

        diagnostics.Should().BeEmpty("serialising a job that persists nothing is a choice, not a defect");
    }

    [Test]
    public async Task PersistInheritedFromABaseClassIsReported()
    {
        string snippet = """
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            [PersistJobDataAfterExecution]
            public abstract class JobBase : IJob
            {
                public abstract ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default);
            }

            public class MyJob : JobBase
            {
                public override ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<JobAttributesAnalyzer>(snippet);

        diagnostics.Select(x => x.SpanText()).Should().Equal(["JobBase", "MyJob"],
            "the attribute is inherited, so the derived job has the behaviour and the base type has the declaration");
    }

    [Test]
    public async Task DisallowTakenFromAnInterfaceSilencesTheDiagnostic()
    {
        string snippet = """
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            [DisallowConcurrentExecution]
            public interface INightlyReport
            {
            }

            [PersistJobDataAfterExecution]
            public class MyJob : IJob, INightlyReport
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<JobAttributesAnalyzer>(snippet);

        diagnostics.Should().BeEmpty("a job is allowed to take either attribute from a contract rather than declaring it");
    }

    [Test]
    public async Task PersistTakenFromAnInheritedInterfaceIsReportedOnTheJobAlone()
    {
        string snippet = """
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            [PersistJobDataAfterExecution]
            public interface IStateful
            {
            }

            public interface IStatefulReport : IStateful
            {
            }

            public class MyJob : IJob, IStatefulReport
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<JobAttributesAnalyzer>(snippet);

        diagnostics.Select(x => x.SpanText()).Should().Equal(["MyJob"],
            "Type.GetInterfaces() is flat, so the job inherits the attribute through an interface of an interface - "
            + "and neither declaring interface is a job, so neither has firings to serialise");
    }

    [Test]
    public async Task ATypeThatIsNotAJobIsNotReported()
    {
        string snippet = """
            using Quartz;

            [PersistJobDataAfterExecution]
            public class NotAJob
            {
            }
            """;

        IReadOnlyList<Diagnostic> diagnostics = await AnalyzerRunner.Run<JobAttributesAnalyzer>(snippet);

        diagnostics.Should().BeEmpty("nothing schedules a type that is not an IJob, so it has no concurrent firings to lose data between");
    }

    private static string Snippet(string attributes, string declaration)
    {
        return $$"""
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            {{attributes}}
            {{declaration}}
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """;
    }
}
