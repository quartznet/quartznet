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

using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using QuartzAnalyzers::Quartz.Analyzers;

namespace Quartz.Analyzers.Tests;

/// <summary>
/// Which delegate jobs are bound when they are compiled, what the interceptor that binds them says,
/// and which are left to be bound by reflection.
/// </summary>
/// <remarks>
/// <para>
/// The harness compiles the generated file against the shipped <c>Quartz.dll</c>, and
/// <see cref="Intercept" /> emits it as well: an interceptor whose signature does not match, or whose
/// location names no call, is only refused as the assembly is written. A snapshot that reads well is
/// therefore an interceptor the compiler accepted for the call it names.
/// </para>
/// <para>
/// The location's data is scrubbed from the snapshots. It carries a checksum of the file, and the file
/// here is a raw string literal whose line endings are whatever the checkout's are.
/// </para>
/// </remarks>
public class DelegateJobsGeneratorTest
{
    private static readonly Dictionary<string, string> interceptorsEnabled = new(StringComparer.Ordinal)
    {
        ["InterceptorsNamespaces"] = "Quartz.Generated",
    };

    [Test]
    public async Task EachParameterIsHandedFromWhereQuartzWouldHandIt()
    {
        GeneratorRun run = Intercept(Snippet("""
            public static class Registration
            {
                public static void Register(IQuartzBuilder q)
                {
                    q.ScheduleJob(
                        "cleanup",
                        (IJobExecutionContext context, CancellationToken token, IServiceProvider services, Repository repository) => repository.Purge(token),
                        trigger => trigger.StartNow());
                }
            }
            """));

        run.Generated.Should().Contain("context,").And.Contain("cancellationToken,").And.Contain("services,")
            .And.Contain("GetRequiredService(services, typeof(global::App.Repository))",
                "the firing, its token and its scope are handed as they are, and anything else is a required service from that scope");

        await VerifyInterceptors(run, "EveryParameterSource");
    }

    /// <summary>
    /// A nullable annotation is no part of the type reflection reads, so it is no part of the binding:
    /// <c>IServiceProvider?</c> is still the scope, and <c>Repository?</c> is still a required service.
    /// </summary>
    [Test]
    public void ANullableAnnotationBindsAsItsTypeDoes()
    {
        GeneratorRun run = Intercept(Snippet("""
            public static class Registration
            {
                public static void Register(IQuartzBuilder q)
                {
                    q.AddJob("cleanup", (IServiceProvider? services, Repository? repository) => { });
                }
            }
            """));

        run.Generated.Should().Contain("services,").And.Contain("typeof(global::App.Repository)")
            .And.NotContain("Repository?", "typeof refuses a nullable reference type, and the container is asked for the type itself");
    }

    [Test]
    public async Task EachWayOfFinishingIsAwaitedAsQuartzAwaitsIt()
    {
        GeneratorRun run = Intercept(Snippet("""
            public static class Registration
            {
                public static void Register(IQuartzBuilder q)
                {
                    q.AddJob("nothing", () => { });
                    q.AddJob("task", (Repository repository) => repository.Purge(CancellationToken.None));
                    q.AddJob("value-task", (IJobExecutionContext context, CancellationToken token) => new ValueTask());
                }
            }
            """));

        run.Generated.Should().Contain("return default(global::System.Threading.Tasks.ValueTask);", "a void handler has finished when it returns")
            .And.Contain("AsValueTask(typed(", "a Task is awaited, and a null one refused as Quartz refuses it")
            .And.Contain("private static global::System.Threading.Tasks.ValueTask AsValueTask(");

        await VerifyInterceptors(run, "ReturnKinds");
    }

    [Test]
    public async Task EachMemberIsReplacedByAnInterceptorOfItsOwnShape()
    {
        GeneratorRun run = Intercept(Snippet("""
            public static class Registration
            {
                public static void Register(IQuartzBuilder q)
                {
                    q.AddJob("added", () => { });
                    q.AddJob("added-configured", () => { }, (services, job) => job.WithDescription("from services"));
                    q.ScheduleJob("scheduled", () => { }, trigger => trigger.StartNow());
                    q.ScheduleJob("scheduled-configured", () => { }, (services, trigger) => trigger.StartNow());
                }
            }
            """));

        run.Generated.Should().Contain("AddJob0(").And.Contain("AddJobWithServices0(")
            .And.Contain("ScheduleJob0(").And.Contain("ScheduleJobWithServices0(",
                "each member has a signature of its own, and an interceptor has to match the one it replaces");

        await VerifyInterceptors(run, "FourMembers");
    }

    [Test]
    public async Task MethodGroupsAreBoundAsTheirLambdasWouldBe()
    {
        GeneratorRun run = Intercept(Snippet("""
            public sealed class Handlers
            {
                public Task Instance(Repository repository) => repository.Purge(CancellationToken.None);

                public static ValueTask Static(IJobExecutionContext context) => default;
            }

            public static class RepositoryExtensions
            {
                public static Task PurgeNow(this Repository repository, CancellationToken token) => repository.Purge(token);
            }

            public static class Registration
            {
                public static void Register(IQuartzBuilder q, Handlers handlers, Repository repository)
                {
                    q.AddJob("instance", handlers.Instance);
                    q.AddJob("static", Handlers.Static);
                    q.AddJob("extension", repository.PurgeNow);
                    q.AddJob("static-lambda", static (IJobExecutionContext context) => default(ValueTask));
                }
            }
            """));

        run.Generated.Should().Contain("global::System.Func<global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task> typed",
            "an extension method group closed over its receiver has the receiver's parameter bound already, as Quartz reads it");

        await VerifyInterceptors(run, "MethodGroups");
    }

    [Test]
    public async Task ANamedSchedulersJobIsInterceptedLikeAnyOther()
    {
        GeneratorRun run = Intercept(Snippet("""
            public static class Registration
            {
                public static void Register(IServiceCollection services)
                {
                    services.AddQuartz("reporting", q => q.ScheduleJob(
                        "nightly",
                        async (Repository repository, CancellationToken token) => await repository.Purge(token),
                        trigger => trigger.WithCronSchedule("0 0 2 * * ?")));
                }
            }
            """));

        run.Generated.Should().Contain("ScheduleJob0(",
            "which scheduler the job belongs to is the builder's to know, and the builder is passed on as it came");

        await VerifyInterceptors(run, "NamedScheduler");
    }

    [Test]
    public void HandlersOfOneShapeShareOneInterceptor()
    {
        GeneratorRun run = Intercept(Snippet("""
            public static class Registration
            {
                public static void Register(IQuartzBuilder q)
                {
                    q.AddJob("first", (Repository repository) => repository.Purge(CancellationToken.None));
                    q.AddJob("second", (Repository other) => other.Purge(CancellationToken.None));
                    q.ScheduleJob("third", (Repository repository) => repository.Purge(CancellationToken.None), trigger => trigger.StartNow());
                }
            }
            """));

        Regex.Matches(run.Generated!, "InterceptsLocation\\(1, ").Count.Should().Be(3, "every call is intercepted");
        run.Generated.Should().Contain("AddJob0(").And.NotContain("AddJob1(",
            "the binding depends on the member and the delegate type alone, so two calls with both in common share it")
            .And.Contain("ScheduleJob0(", "another member is another signature, whatever the handler");
    }

    [Test]
    public void AStaticCallAndAConditionalCallAreInterceptedToo()
    {
        GeneratorRun run = Intercept(Snippet("""
            public static class Registration
            {
                public static void Register(IQuartzBuilder q, IQuartzBuilder? maybe)
                {
                    QuartzBuilderExtensions.AddJob(q, "static-form", () => { });
                    maybe?.AddJob("conditional", () => { });
                }
            }
            """));

        Regex.Matches(run.Generated!, "InterceptsLocation\\(1, ").Count.Should().Be(2,
            "a call is intercepted by the name that denotes the method, however the call reaches it");
    }

    [Test]
    public void TheInterceptorNamesTheCallItReplaces()
    {
        string source = Snippet("""
            public static class Registration
            {
                public static void Register(IQuartzBuilder q) => q.AddJob("cleanup", () => { });
            }
            """);

        GeneratorRun run = Intercept(source);

        (string file, int position) = Decode(run.Generated!);
        file.Should().Be("Snippet.cs");
        position.Should().Be(source.IndexOf("AddJob(", StringComparison.Ordinal),
            "an interceptor names the position of the simple name that denotes the method it replaces");
        run.Generated.Should().Contain("// Snippet.cs(" + LineAndColumn(source, position) + ")",
            "the comment beside the attribute is how a reader finds the call");
    }

    /// <summary>
    /// Every shape Quartz refuses, and every handler the generated code could not spell, is left alone,
    /// so the call is bound by reflection as it would be without the generator — and refused, where it is
    /// refused, by Quartz's own words.
    /// </summary>
    [TestCase("Delegate handler = () => { }; q.AddJob(\"job\", handler);", TestName = "A Delegate variable")]
    [TestCase("q.AddJob(\"job\", new Action(Handle));", TestName = "An explicit delegate creation")]
    [TestCase("q.AddJob(\"job\", (Action) (() => { }));", TestName = "A cast lambda")]
    [TestCase("q.AddJob(\"job\", () => Task.FromResult(42));", TestName = "A Task of a result")]
    [TestCase("q.AddJob(\"job\", () => new ValueTask<int>(42));", TestName = "A ValueTask of a result")]
    [TestCase("q.AddJob(\"job\", () => \"done\");", TestName = "Any other result")]
    [TestCase("q.AddJob(\"job\", RunAsync);", TestName = "An async void method group")]
    [TestCase("q.AddJob(\"job\", (ref int value) => { value++; });", TestName = "A ref parameter")]
    [TestCase("q.AddJob(\"job\", (Span<byte> buffer) => buffer.Clear());", TestName = "A ref struct parameter")]
    [TestCase("q.AddJob(\"job\", (int retries = 3) => { });", TestName = "An optional parameter")]
    [TestCase("q.AddJob(\"job\", (TService service) => { });", TestName = "A type parameter")]
    [TestCase("q.AddJob(\"job\", (Secret secret) => { });", TestName = "A private nested type")]
    [TestCase("q.AddJob(\"job\", (Local local) => { });", TestName = "A file-local type")]
    [TestCase("q.AddJob(\"job\", (List<Secret> secrets) => { });", TestName = "A private type as a type argument")]
    [TestCase("q.AddJob(\"job\", ((int, string) pair) => { });", TestName = "A tuple")]
    [TestCase("q.AddJob(\"job\", (dynamic value) => { });", TestName = "A dynamic parameter")]
    [TestCase("q.AddJob<NoOpJob>(job => job.WithIdentity(\"job\"));", TestName = "A job class")]
    [TestCase("Custom.AddJob(q, \"job\", () => { });", TestName = "Another method of the same name")]
    public void AHandlerTheGeneratedCodeCouldNotBindIsLeftToReflection(string registration)
    {
        GeneratorRun run = Intercept(Snippet($$"""
            file sealed class Local
            {
            }

            public sealed class NoOpJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }

            public static class Custom
            {
                public static IQuartzBuilder AddJob(IQuartzBuilder builder, string name, Delegate handler) => builder;
            }

            public static class Registration
            {
                private sealed class Secret
                {
                }

                public static void Register<TService>(IQuartzBuilder q)
                {
                    {{registration}}
                }

                private static void Handle()
                {
                }

                private static async void RunAsync() => await Task.Yield();
            }
            """));

        run.Diagnostics.Should().BeEmpty("declining a call is the generator's ordinary business, not something to report");
        run.Generated.Should().BeNull("Quartz binds this handler by reflection when the call runs, or refuses it there with its own message");
    }

    [Test]
    public void OnlyTheCallsTheGeneratedCodeCanBindAreIntercepted()
    {
        GeneratorRun run = Intercept(Snippet("""
            public static class Registration
            {
                public static void Register(IQuartzBuilder q, Delegate handler)
                {
                    q.AddJob("reflected", handler);
                    q.AddJob("compiled", () => { });
                    q.AddJob("refused", () => Task.FromResult(42));
                }
            }
            """));

        Regex.Matches(run.Generated!, "InterceptsLocation\\(1, ").Count.Should().Be(1,
            "one call in a file being left to reflection says nothing about the calls beside it");
    }

    /// <summary>
    /// C# 10 gave a lambda a delegate type of its own, which is what a delegate job needs, but not
    /// file-local types, which is what the interceptor is declared with. It is the one version that falls
    /// back.
    /// </summary>
    [Test]
    public void BelowCSharp11NothingIsGenerated()
    {
        GeneratorRun run = Intercept(
            Snippet("""
                public static class Registration
                {
                    public static void Register(IQuartzBuilder q) => q.AddJob("cleanup", (Repository repository) => repository.Purge(CancellationToken.None));
                }
                """),
            languageVersion: LanguageVersion.CSharp10);

        run.Generated.Should().BeNull("a compiler that cannot read a file-local type would refuse the whole file, and the call works without it");
    }

    [Test]
    public void TheGeneratedFileCompilesUnderCSharp11()
    {
        GeneratorRun run = Intercept(
            Snippet("""
                public static class Registration
                {
                    public static void Register(IQuartzBuilder q)
                    {
                        q.AddJob("nothing", () => { });
                        q.AddJob("task", (Repository repository, CancellationToken token) => repository.Purge(token));
                        q.ScheduleJob("value-task", (IJobExecutionContext context) => new ValueTask(), (services, trigger) => trigger.StartNow());
                        q.AddJob("configured", (IServiceProvider services) => { }, (services, job) => job.StoreDurably());
                    }
                }
                """),
            languageVersion: LanguageVersion.CSharp11);

        run.Generated.Should().Contain("AsValueTask(").And.Contain("return default(")
            .And.Contain("ScheduleJobWithServices0(").And.Contain("AddJobWithServices0(",
                "the snippet has to reach every shape of code the generator writes, or this proves less than it says");
        run.Output.SyntaxTrees.Should().HaveCount(2).And.AllSatisfy(x =>
            ((CSharpParseOptions) x.Options).LanguageVersion.Should().Be(LanguageVersion.CSharp11,
                "the generated file is parsed at the language version of the project it is generated into"));
    }

    [TestCase(null, false, TestName = "No InterceptorsNamespaces")]
    [TestCase("Microsoft.AspNetCore.Http.Generated", false, TestName = "Another generator's namespace")]
    [TestCase("Quartz", false, TestName = "The parent namespace")]
    [TestCase("Microsoft.AspNetCore.Http.Generated;Quartz.Generated", true, TestName = "Quartz.Generated among others")]
    public void OnlyAProjectThatAllowsTheInterceptorsGetsThem(string? namespaces, bool intercepted)
    {
        Dictionary<string, string> features = namespaces is null ? [] : new() { ["InterceptorsNamespaces"] = namespaces };

        GeneratorRun run = AnalyzerRunner.RunGenerator<DelegateJobsGenerator>(
            Snippet("""
                public static class Registration
                {
                    public static void Register(IQuartzBuilder q) => q.AddJob("cleanup", () => { });
                }
                """),
            features: features);

        if (intercepted)
        {
            run.Generated.Should().NotBeNull("Quartz.targets adds the namespace, and a project that keeps it gets the interceptors");
            run.AssertEmits();
        }
        else
        {
            run.Generated.Should().BeNull(
                "the compiler refuses an interceptor in a namespace the project has not listed, so emitting one would break "
                + "a build that works without it");
        }
    }

    [Test]
    public void AnUnchangedFileIsNotGeneratedAgain()
    {
        IReadOnlyList<IncrementalStepRunReason> reasons = AnalyzerRunner.RerunReasons<DelegateJobsGenerator>(
            Snippet("""
                public static class Registration
                {
                    public static void Register(IQuartzBuilder q)
                    {
                        q.AddJob("cleanup", (Repository repository, CancellationToken token) => repository.Purge(token));
                        q.ScheduleJob("report", (IJobExecutionContext context) => { }, trigger => trigger.StartNow());
                    }
                }
                """),
            interceptorsEnabled);

        reasons.Should().NotBeEmpty("the run has to have produced an output step for its reason to mean anything");
        reasons.Should().AllSatisfy(x => x.Should().Be(IncrementalStepRunReason.Cached),
            "the calls are the same ones, so what the transform read out of them has to compare equal to what it read before");
    }

    /// <summary>
    /// What the generator wrote, run: the snippet schedules a job through the intercepted call and the
    /// handler says what it was handed and how it was called.
    /// </summary>
    [Test]
    public async Task TheGeneratedBindingRunsTheHandler()
    {
        GeneratorRun run = Intercept(Snippet("""
            public sealed record Greeting(string Text);

            public static class Program
            {
                public static async Task<string> Run(string schedulerName)
                {
                    TaskCompletionSource<string> ran = new(TaskCreationOptions.RunContinuationsAsynchronously);

                    ServiceCollection services = new();
                    services.AddSingleton(new Greeting("hello"));
                    services.AddQuartz(schedulerName, q => q.ScheduleJob(
                        "greet",
                        (Greeting greeting, IJobExecutionContext context, CancellationToken token) =>
                        {
                            string how = Environment.StackTrace.Contains("Quartz.Generated.", StringComparison.Ordinal)
                                && !Environment.StackTrace.Contains("System.Reflection.", StringComparison.Ordinal)
                                ? "compiled"
                                : "reflected";
                            ran.TrySetResult($"{greeting.Text} from {context.JobDetail.Key.Name}, {how}, {(token.CanBeCanceled ? "with" : "without")} its token");
                            return Task.CompletedTask;
                        },
                        trigger => trigger.StartNow()));

                    await using ServiceProvider provider = services.BuildServiceProvider();
                    IScheduler scheduler = await provider.GetRequiredKeyedService<ISchedulerFactory>(schedulerName).GetScheduler();
                    try
                    {
                        await scheduler.Start();
                        return await ran.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    }
                    finally
                    {
                        await scheduler.Shutdown(waitForJobsToComplete: true);
                    }
                }
            }
            """));

        MethodInfo entry = run.Load().GetType("App.Program", throwOnError: true)!.GetMethod("Run")!;
        string result = await (Task<string>) entry.Invoke(null, ["delegate-" + Guid.NewGuid().ToString("N")])!;

        result.Should().Be("hello from greet, compiled, with its token",
            "the generated code resolved the service, handed the firing and its token, and called the handler with no reflection on the way");
    }

    private static GeneratorRun Intercept(string source, LanguageVersion languageVersion = LanguageVersion.Latest)
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DelegateJobsGenerator>(source, languageVersion: languageVersion, features: interceptorsEnabled);

        run.Diagnostics.Should().BeEmpty("the generator reports nothing: a call it cannot bind is bound by reflection instead");
        if (run.Generated is not null)
        {
            run.AssertEmits();
        }

        return run;
    }

    private static Task VerifyInterceptors(GeneratorRun run, string name)
    {
        run.Generated.Should().NotBeNull("the snippet's calls are all of shapes the generated code can bind");

        // The data is a checksum of the snippet, whose line endings are the checkout's: scrubbed, so that
        // the snapshot is the same on every machine. TheInterceptorNamesTheCallItReplaces decodes it.
        string scrubbed = Regex.Replace(run.Generated!, "InterceptsLocation\\((\\d+), \"[^\"]*\"\\)", "InterceptsLocation($1, \"{data}\")");

        return Verify(scrubbed, extension: "txt").UseDirectory("Verify").UseFileName("DelegateJobsGeneratorTest_" + name);
    }

    /// <summary>
    /// The file and position the first interception in a generated file names.
    /// </summary>
    /// <remarks>
    /// Roslyn's version 1 location is base64 of a 16-byte content checksum, a 4-byte little-endian
    /// position and the display file name, as <c>RequestDelegatesAreSourceGeneratedTest</c> reads it.
    /// </remarks>
    private static (string File, int Position) Decode(string generated)
    {
        Match match = Regex.Match(generated, "InterceptsLocation\\(1, \"(?<data>[^\"]*)\"\\)");
        match.Success.Should().BeTrue("the file intercepts at least one call");

        byte[] data = Convert.FromBase64String(match.Groups["data"].Value);
        return (Encoding.UTF8.GetString(data, 20, data.Length - 20), BitConverter.ToInt32(data, 16));
    }

    private static string LineAndColumn(string source, int position)
    {
        int line = source.Take(position).Count(x => x == '\n') + 1;
        int column = position - source.LastIndexOf('\n', position - 1);
        return $"{line},{column}";
    }

    private static string Snippet(string declarations)
    {
        return $$"""
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;

            using Microsoft.Extensions.DependencyInjection;

            using Quartz;

            namespace App;

            public sealed class Repository
            {
                public Task Purge(CancellationToken cancellationToken) => Task.CompletedTask;
            }

            {{declarations}}
            """;
    }
}
