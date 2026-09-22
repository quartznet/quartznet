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
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using QuartzAnalyzers::Quartz.Analyzers;

namespace Quartz.Analyzers.Tests;

/// <summary>
/// What <c>[QuartzJob]</c> and <c>[CronTrigger]</c> turn into, and what they are refused for.
/// </summary>
/// <remarks>
/// The generated registration is snapshotted rather than asserted on call by call: the file as a
/// whole is the contract, and the harness has already compiled it against the shipped
/// <c>Quartz.dll</c> — so a snapshot that reads well is a registration that also builds.
/// </remarks>
public class DeclaredJobsGeneratorTest
{
    [Test]
    public async Task JobWithNoScheduleIsRegisteredDurably()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            [QuartzJob]
            public sealed class CleanupJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """));

        run.Diagnostics.Should().BeEmpty("a job declared with nothing but [QuartzJob] is a complete declaration");
        run.Generated.Should().Contain(".StoreDurably(true)", "a job no trigger points at is deleted as soon as it is stored unless it is durable");

        await Verify(run.Generated!, extension: "txt").UseDirectory("Verify").UseFileName("DeclaredJobsGeneratorTest_JobWithNoSchedule");
    }

    [Test]
    public async Task SecondAndThirdSchedulesCountUpFromTheJobsName()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            [QuartzJob(Name = "cleanup")]
            [CronTrigger("0 0 0/6 * * ?")]
            [CronTrigger("0 0 12 ? * MON-FRI")]
            [CronTrigger("0 0 3 1 * ?")]
            public sealed class CleanupJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """));

        run.Diagnostics.Should().BeEmpty();
        run.Generated.Should().Contain("\"cleanup-2\"").And.Contain("\"cleanup-3\"",
            "the first schedule is named after the job, because that is what a single trigger would have been called by hand");
        run.Generated.Should().NotContain(".StoreDurably", "a job three triggers point at needs no durability to survive");

        await Verify(run.Generated!, extension: "txt").UseDirectory("Verify").UseFileName("DeclaredJobsGeneratorTest_ThreeSchedules");
    }

    [Test]
    public async Task EveryPropertyReachesTheRegistration()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            [QuartzJob(
                Name = "cleanup",
                Group = "maintenance",
                Description = "removes rows nobody reads",
                Durable = true,
                RequestRecovery = true)]
            [CronTrigger(
                "0 0 0/6 * * ?",
                Name = "six-hourly",
                Group = "housekeeping",
                TimeZone = "Europe/Helsinki",
                MisfireInstruction = CronTriggerMisfireInstruction.DoNothing,
                Priority = 9,
                Description = "every six hours, Helsinki time",
                ExecutionGroup = "maintenance")]
            public sealed class CleanupJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """));

        run.Diagnostics.Should().BeEmpty();

        await Verify(run.Generated!, extension: "txt").UseDirectory("Verify").UseFileName("DeclaredJobsGeneratorTest_EveryProperty");
    }

    [Test]
    public async Task SchedulerOnTheJobFiltersItsRegistration()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            [QuartzJob(Name = "report", Scheduler = "reporting")]
            [CronTrigger("0 0 6 * * ?")]
            public sealed class ReportJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }

            [QuartzJob(Name = "cleanup")]
            public sealed class CleanupJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """));

        run.Diagnostics.Should().BeEmpty();
        run.Generated.Should().Contain("if (builder.SchedulerName == \"reporting\")",
            "a job naming a scheduler is registered on that one and skipped by every other, the unnamed one included");

        await Verify(run.Generated!, extension: "txt").UseDirectory("Verify").UseFileName("DeclaredJobsGeneratorTest_NamedScheduler");
    }

    [Test]
    public void TypedJobIsAJob()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            public sealed record ImportRequest(string File);

            [QuartzJob(Name = "import")]
            [CronTrigger("0 0 1 * * ?")]
            public sealed class ImportJob : IJob<ImportRequest>
            {
                public ValueTask Execute(IJobExecutionContext context, ImportRequest input, CancellationToken cancellationToken = default) => default;
            }
            """));

        run.Diagnostics.Should().BeEmpty("IJob<TInput> inherits IJob, so a typed job is one AddJob<T> takes");
        run.Generated.Should().Contain("builder.AddJob<global::App.ImportJob>");
    }

    /// <summary>
    /// The half a snapshot cannot show: that the call an application writes resolves to what was
    /// generated, from the application's own namespace.
    /// </summary>
    /// <remarks>
    /// The harness compiles the snippet together with the generated file and fails on any error, so
    /// this test is the assertion.
    /// </remarks>
    [Test]
    public void GeneratedExtensionIsReachedFromTheApplicationsOwnNamespace()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            [QuartzJob]
            [CronTrigger("0 0 0/6 * * ?")]
            public sealed class CleanupJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }

            public static class Registration
            {
                public static void Register(IQuartzBuilder builder) => builder.AddDeclaredJobs();
            }
            """));

        run.Diagnostics.Should().BeEmpty();
    }

    /// <summary>
    /// <c>[QuartzJob]</c> and <c>[CronTrigger]</c> where the compiler already refuses them are the
    /// compiler's to report, and must not take the assembly's other declared jobs down with them.
    /// </summary>
    /// <remarks>
    /// The attributes are matched by name, so a misplaced one still reaches the generator — with a
    /// method or the assembly as its target rather than a type. The harness rethrows what a generator
    /// throws, which a build would otherwise turn into CS8785 and a missing file.
    /// </remarks>
    [Test]
    public void AttributesOnAMethodAreLeftToTheCompiler()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(
            Snippet("""
                [QuartzJob(Name = "cleanup")]
                public sealed class CleanupJob : IJob
                {
                    [QuartzJob]
                    [CronTrigger("0 0 3 * * ?")]
                    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
                }
                """),
            toleratedErrors: ["CS0592"]);

        run.Output.GetDiagnostics().Where(x => x.Id == "CS0592").Should().HaveCount(2, "the compiler refuses each attribute where its usage does not allow it");
        run.Diagnostics.Should().BeEmpty("a misplaced attribute is the compiler's to report, and it already has");
        run.Generated.Should().Contain("builder.AddJob<global::App.CleanupJob>", "the job declared where the attribute belongs is still registered");
    }

    [Test]
    public void AttributesOnTheAssemblyAreLeftToTheCompiler()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(
            """
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            [assembly: QuartzJob]
            [assembly: CronTrigger("0 0 3 * * ?")]

            namespace App;

            [QuartzJob(Name = "cleanup")]
            public sealed class CleanupJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """,
            toleratedErrors: ["CS0592"]);

        run.Output.GetDiagnostics().Where(x => x.Id == "CS0592").Should().HaveCount(2, "the compiler refuses each attribute where its usage does not allow it");
        run.Diagnostics.Should().BeEmpty("a misplaced attribute is the compiler's to report, and it already has");
        run.Generated.Should().Contain("builder.AddJob<global::App.CleanupJob>", "the job declared where the attribute belongs is still registered");
    }

    /// <summary>
    /// The generated file is compiled as part of the project it is generated into, so it has to be
    /// C# that project's language version reads — including a project that pins an older
    /// <c>LangVersion</c> than its target framework's default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The harness compiles what the generator wrote at the snippet's language version, so the run
    /// building at all is the assertion. <c>#nullable</c> is the newest thing the file may use, and it
    /// arrived in C# 8.
    /// </para>
    /// <para>
    /// The attributes' named properties are <c>init</c>-only, which C# 8 cannot set (CS8400), so the
    /// bare attributes here are everything a C# 8 project can declare;
    /// <see cref="GeneratedFileCompilesUnderCSharp9" /> asks for every call the generator can write.
    /// </para>
    /// </remarks>
    [Test]
    public void GeneratedFileCompilesUnderCSharp8()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(
            """
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            namespace App
            {
                [QuartzJob]
                [CronTrigger("0 0 6 * * ?")]
                [CronTrigger("0 0 18 * * ?")]
                public sealed class ReportJob : IJob
                {
                    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
                }

                [QuartzJob]
                public sealed class CleanupJob : IJob
                {
                    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
                }

                public static class Registration
                {
                    public static void Register(IQuartzBuilder builder)
                    {
                        builder.AddDeclaredJobs();
                    }
                }
            }
            """,
            languageVersion: LanguageVersion.CSharp8);

        run.Diagnostics.Should().BeEmpty();
        run.Generated.Should().Contain(".StoreDurably(true)").And.Contain("\"ReportJob-2\"");
        AssertParsedAt(run, LanguageVersion.CSharp8);
    }

    /// <summary>
    /// C# 9, the first version that can set the attributes' named properties, over every call the
    /// generator can write.
    /// </summary>
    [Test]
    public void GeneratedFileCompilesUnderCSharp9()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(
            """
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            namespace App
            {
                [QuartzJob(
                    Name = "report",
                    Group = "reports",
                    Description = "the morning report",
                    Durable = true,
                    RequestRecovery = true,
                    Scheduler = "reporting")]
                [CronTrigger(
                    "0 0 6 * * ?",
                    Name = "morning",
                    Group = "mornings",
                    TimeZone = "Europe/Helsinki",
                    MisfireInstruction = CronTriggerMisfireInstruction.DoNothing,
                    Priority = 9,
                    Description = "six o'clock, Helsinki time",
                    ExecutionGroup = "reports")]
                [CronTrigger("0 0 18 * * ?", MisfireInstruction = (CronTriggerMisfireInstruction) 42)]
                public sealed class ReportJob : IJob
                {
                    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
                }

                [QuartzJob]
                public sealed class CleanupJob : IJob
                {
                    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
                }

                public static class Registration
                {
                    public static void Register(IQuartzBuilder builder)
                    {
                        builder.AddDeclaredJobs();
                    }
                }
            }
            """,
            languageVersion: LanguageVersion.CSharp9);

        run.Diagnostics.Should().BeEmpty();
        run.Generated.Should().Contain("if (builder.SchedulerName == \"reporting\")")
            .And.Contain(".InTimeZone(")
            .And.Contain("global::Quartz.CronTriggerMisfireInstruction.DoNothing")
            .And.Contain("(global::Quartz.CronTriggerMisfireInstruction) 42",
                "the snippet has to reach every shape of call the generator writes, or this proves less than it says");
        AssertParsedAt(run, LanguageVersion.CSharp9);
    }

    private static void AssertParsedAt(GeneratorRun run, LanguageVersion languageVersion)
    {
        run.Output.SyntaxTrees.Should().HaveCount(2).And.AllSatisfy(x =>
            ((CSharpParseOptions) x.Options).LanguageVersion.Should().Be(languageVersion,
                "the generated file is parsed at the language version of the project it is generated into"));
    }

    [Test]
    public void AssemblyThatDeclaresNoJobGetsNoRegistration()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            public sealed class CleanupJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """));

        run.Diagnostics.Should().BeEmpty();
        run.Generated.Should().BeNull("a compilation that never heard of the attributes is left exactly as it was");
    }

    /// <summary>
    /// Two assemblies that both declare jobs, the first granting the second <c>InternalsVisibleTo</c>.
    /// </summary>
    /// <remarks>
    /// Both generated classes are in scope in the second assembly, so an unrenamed one there made
    /// <c>AddDeclaredJobs()</c> CS0121-ambiguous with no spelling that resolved it — naming the class
    /// was ambiguous too. The harness compiles the snippet with the generated file, so the call below
    /// building at all is half of this test.
    /// </remarks>
    [TestCase("App", "App")]
    [TestCase("My.App", "My_App")]
    [TestCase("3rd-Party.App", "_3rd_Party_App")]
    public void AssemblyThatSeesAnotherAssemblysDeclaredJobsNamesItsOwnAfterItself(string assemblyName, string identifier)
    {
        GeneratorRun library = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Library(grantInternalsTo: assemblyName), assemblyName: "Lib");

        library.Diagnostics.Should().BeEmpty("the library can see nobody else's registration, so nothing about its own changes");
        library.Generated.Should().Contain("internal static class QuartzDeclaredJobs").And.NotContain("QuartzDeclaredJobs_");

        string method = "AddDeclaredJobsFrom" + identifier;

        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(
            Snippet($$"""
                [QuartzJob(Name = "app-job")]
                public sealed class AppJob : IJob
                {
                    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
                }

                [QuartzJob(Name = "other-app-job")]
                public sealed class OtherAppJob : IJob
                {
                    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
                }

                public static class Registration
                {
                    public static void Register(IQuartzBuilder builder) => builder.AddDeclaredJobs().{{method}}();
                }
                """),
            assemblyName: assemblyName,
            references: [library.ToReference()]);

        Diagnostic diagnostic = run.Diagnostics.Should().ContainSingle("the rename is one fact about the assembly, however many jobs it declares").Subject;
        diagnostic.Id.Should().Be("QZ1004");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning, "nothing is broken, but a call that used to mean this assembly's jobs now means another's");
        diagnostic.GetMessage().Should().Be(
            $"AddDeclaredJobs() in this assembly resolves to 'Lib''s declared jobs, which are visible through InternalsVisibleTo; call {method}() for this assembly's own");
        diagnostic.Location.SourceSpan.Start.Should().Be(run.Snippet.IndexOf("QuartzJob", StringComparison.Ordinal),
            "the warning goes on the first job the assembly declares");

        IMethodSymbol libraries = Resolve(run, "AddDeclaredJobs");
        libraries.ContainingAssembly.Name.Should().Be("Lib", "the ordinary name stays with the assembly that had it first, which this one can see");
        libraries.ContainingType.ToDisplayString().Should().Be("Quartz.QuartzDeclaredJobs");

        IMethodSymbol own = Resolve(run, method);
        own.ContainingAssembly.Name.Should().Be(assemblyName);
        own.ContainingType.ToDisplayString().Should().Be("Quartz.QuartzDeclaredJobs_" + identifier,
            "the class is named after the assembly, made into an identifier, so that it cannot collide with the one it steps aside for");

        library.Generated.Should().Contain("builder.AddJob<global::Lib.LibraryJob>").And.NotContain("AppJob");
        run.Generated.Should().Contain("builder.AddJob<global::App.AppJob>")
            .And.Contain("builder.AddJob<global::App.OtherAppJob>")
            .And.NotContain("LibraryJob", "each registration carries its own assembly's jobs and nobody else's");
    }

    [Test]
    public void AssemblyThatCannotSeeAnotherAssemblysDeclaredJobsKeepsTheOrdinaryName()
    {
        GeneratorRun library = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Library(grantInternalsTo: null), assemblyName: "Lib");

        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(
            Snippet("""
                [QuartzJob(Name = "app-job")]
                public sealed class AppJob : IJob
                {
                    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
                }

                public static class Registration
                {
                    public static void Register(IQuartzBuilder builder) => builder.AddDeclaredJobs();
                }
                """),
            assemblyName: "App",
            references: [library.ToReference()]);

        run.Diagnostics.Should().BeEmpty("without InternalsVisibleTo the library's generated class is invisible here, so nothing collides");
        run.Generated.Should().Contain("internal static class QuartzDeclaredJobs").And.NotContain("QuartzDeclaredJobs_");
        Resolve(run, "AddDeclaredJobs").ContainingAssembly.Name.Should().Be("App");
    }

    [Test]
    public void AssemblyThatDeclaresNoJobReachesALibrarysThroughInternalsVisibleTo()
    {
        GeneratorRun library = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Library(grantInternalsTo: "App"), assemblyName: "Lib");

        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(
            Snippet("""
                public static class Registration
                {
                    public static void Register(IQuartzBuilder builder) => builder.AddDeclaredJobs();
                }
                """),
            assemblyName: "App",
            references: [library.ToReference()]);

        run.Diagnostics.Should().BeEmpty("an assembly that declares nothing has no registration of its own to rename");
        run.Generated.Should().BeNull();
        Resolve(run, "AddDeclaredJobs").ContainingAssembly.Name.Should().Be("Lib");
    }

    /// <summary>
    /// Two libraries granting the same assembly <c>InternalsVisibleTo</c>: the warning names one of
    /// them, and the same one whichever order the references arrive in.
    /// </summary>
    [TestCase("Zeta", "Alpha")]
    [TestCase("Alpha", "Zeta")]
    public void RenameWarningNamesTheSameAssemblyWhateverOrderTheReferencesArriveIn(string first, string second)
    {
        MetadataReference[] libraries =
        [
            AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Library(grantInternalsTo: "App"), assemblyName: first).ToReference(),
            AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Library(grantInternalsTo: "App"), assemblyName: second).ToReference(),
        ];

        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(
            Snippet("""
                [QuartzJob(Name = "app-job")]
                public sealed class AppJob : IJob
                {
                    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
                }

                public static class Registration
                {
                    public static void Register(IQuartzBuilder builder) => builder.AddDeclaredJobsFromApp();
                }
                """),
            assemblyName: "App",
            references: libraries);

        run.Diagnostics.Should().ContainSingle().Which.GetMessage().Should().StartWith(
            "AddDeclaredJobs() in this assembly resolves to 'Alpha''s declared jobs",
            "the first by ordinal name is named, so the same references never produce a different warning");
    }

    [Test]
    public void TypeThatIsNotAJobIsRefused()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            [QuartzJob]
            public sealed class NotAJob
            {
            }
            """));

        Diagnostic diagnostic = run.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Id.Should().Be("QZ1001");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error, "the alternative is silence, and a job that was declared and never fires");
        diagnostic.GetMessage().Should().Be("'NotAJob' carries [QuartzJob] but does not implement IJob, so no registration can be generated for it");
        run.TextAt(diagnostic).Should().Be("QuartzJob", "the squiggle belongs under the declaration that cannot be honoured");
    }

    [Test]
    public void AbstractJobIsRefused()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            [QuartzJob]
            public abstract class CleanupJobBase : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """));

        run.Diagnostics.Should().ContainSingle().Which.GetMessage().Should()
            .Be("'CleanupJobBase' carries [QuartzJob] but is abstract, so no registration can be generated for it");
    }

    [Test]
    public void GenericJobIsRefused()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            [QuartzJob]
            public sealed class CleanupJob<T> : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """));

        run.Diagnostics.Should().ContainSingle().Which.Id.Should().Be("QZ1001");
    }

    [Test]
    public void JobThatTheGeneratedFileCannotNameIsRefused()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            public sealed class Host
            {
                [QuartzJob]
                private sealed class CleanupJob : IJob
                {
                    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
                }
            }
            """));

        run.Diagnostics.Should().ContainSingle().Which.GetMessage().Should()
            .Contain("cannot be named from another file in this assembly",
                "the registration is a file of its own, so a private nested job is out of its reach however visible it is where it is written");
    }

    [Test]
    public void TwoJobsWithOneKeyAreRefused()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            [QuartzJob(Name = "cleanup")]
            public sealed class FirstJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }

            [QuartzJob(Name = "cleanup")]
            public sealed class SecondJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """));

        Diagnostic diagnostic = run.Diagnostics.Should().ContainSingle("the report belongs on the second declaration, which is the one that overwrites").Subject;
        diagnostic.Id.Should().Be("QZ1002");
        diagnostic.GetMessage().Should().Be("More than one declared job resolves to the key 'DEFAULT.cleanup'; give one of them a different Name or Group");
    }

    [Test]
    public void TwoSchedulesWithOneKeyAreRefused()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            [QuartzJob(Name = "cleanup", Group = "maintenance")]
            [CronTrigger("0 0 0/6 * * ?", Name = "nightly")]
            [CronTrigger("0 0 3 * * ?", Name = "nightly")]
            public sealed class CleanupJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """));

        run.Diagnostics.Should().ContainSingle().Which.GetMessage().Should()
            .Be("More than one declared trigger resolves to the key 'maintenance.nightly'; give one of them a different Name or Group");
    }

    [Test]
    public void OneKeyOnTwoSchedulersIsTwoJobs()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            [QuartzJob(Name = "cleanup", Scheduler = "first")]
            public sealed class FirstJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }

            [QuartzJob(Name = "cleanup", Scheduler = "second")]
            public sealed class SecondJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """));

        run.Diagnostics.Should().BeEmpty("a key is an identity within a scheduler, and these two are registered on different ones");
    }

    [Test]
    public void ScheduleWithoutAJobIsRefused()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            [CronTrigger("0 0 0/6 * * ?")]
            [CronTrigger("0 0 3 * * ?")]
            public sealed class CleanupJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """));

        Diagnostic diagnostic = run.Diagnostics.Should()
            .ContainSingle("the fix is the missing attribute, and it is missing once however many schedules were written under it").Subject;

        diagnostic.Id.Should().Be("QZ1003");
        diagnostic.GetMessage().Should()
            .Be("'CleanupJob' carries [CronTrigger] without [QuartzJob], so the schedule declares a trigger for a job that is never registered");
    }

    /// <summary>
    /// A cron literal on <c>[CronTrigger]</c> is the analyzer's business, and the generator copying
    /// it into the registration must not make the compiler say so twice.
    /// </summary>
    /// <remarks>
    /// The analyzer is run over the compilation the generator has already added its file to, which is
    /// the only arrangement in which "once rather than twice" is a claim that can be checked.
    /// <c>CronLiteralAnalyzer</c> passes over generated code, so the report lands where the
    /// expression was written.
    /// </remarks>
    [Test]
    public async Task BadCronOnTheAttributeIsReportedOnceAndWhereItWasWritten()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            [QuartzJob(Name = "cleanup")]
            [CronTrigger("0 0 12 * *")]
            public sealed class CleanupJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """));

        run.Diagnostics.Should().BeEmpty("an expression that does not parse is still a declaration the generator understands; QZ0001 is what says it will not work");

        IReadOnlyList<Diagnostic> reported = await AnalyzerRunner.Analyze<CronLiteralAnalyzer>(run.Output);

        Diagnostic diagnostic = reported.Should().ContainSingle(
            "the generated file carries the same literal, and an analyzer that read generated code too would report the same mistake twice").Subject;

        diagnostic.Id.Should().Be("QZ0001");
        run.TextAt(diagnostic).Should().Be("\"0 0 12 * *\"", "the squiggle belongs under the attribute argument, which is the only copy anybody can edit");
    }

    /// <summary>
    /// An edit that changes nothing about the declarations writes no source again.
    /// </summary>
    /// <remarks>
    /// This is what the generator's value-typed model buys, and the only test that can tell whether
    /// it is still bought: a model holding a symbol or a <c>Location</c> compares unequal to itself
    /// on the next keystroke, and a generator that cannot tell "the same" from "changed" re-emits
    /// while its user is typing.
    /// </remarks>
    [Test]
    public void SourceIsNotWrittenAgainForAnEditThatChangedNothing()
    {
        IReadOnlyList<IncrementalStepRunReason> reasons = AnalyzerRunner.RerunReasons<DeclaredJobsGenerator>(Snippet("""
            [QuartzJob(Name = "cleanup", Group = "maintenance")]
            [CronTrigger("0 0 0/6 * * ?", TimeZone = "Europe/Helsinki")]
            public sealed class CleanupJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """));

        reasons.Should().NotBeEmpty("the run has to have produced an output step for its reason to mean anything");
        reasons.Should().AllSatisfy(x => x.Should().Be(IncrementalStepRunReason.Cached),
            "the declarations are the same ones, so what the transform read out of them has to compare equal to what it read before");
    }

    /// <summary>
    /// The method a call in the snippet binds to, which is how a test says which assembly's
    /// registration a line of application code reaches.
    /// </summary>
    private static IMethodSymbol Resolve(GeneratorRun run, string methodName)
    {
        SyntaxTree snippet = run.Output.SyntaxTrees.First();
        SemanticModel model = run.Output.GetSemanticModel(snippet);

        InvocationExpressionSyntax call = snippet.GetRoot().DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(x => x.Expression is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == methodName);

        return model.GetSymbolInfo(call).Symbol.Should().BeAssignableTo<IMethodSymbol>().Subject;
    }

    /// <summary>
    /// A second assembly that declares a job, optionally granting another <c>InternalsVisibleTo</c>.
    /// </summary>
    private static string Library(string? grantInternalsTo)
    {
        string grant = grantInternalsTo is null
            ? ""
            : $"[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"{grantInternalsTo}\")]";

        return $$"""
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            {{grant}}

            namespace Lib;

            [QuartzJob(Name = "library-job")]
            public sealed class LibraryJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """;
    }

    private static string Snippet(string declarations)
    {
        return $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            using Quartz;

            namespace App;

            {{declarations}}
            """;
    }
}
