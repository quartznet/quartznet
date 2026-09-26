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

using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using QuartzAnalyzers::Quartz.Analyzers;

namespace Quartz.Analyzers.Tests;

/// <summary>
/// <c>ConfigurationKey</c> on <c>[CronTrigger]</c>: what the generator writes for it, what it reports,
/// and — by running the generated registration against a real scheduler — what the key does.
/// </summary>
/// <remarks>
/// The generated text is half of it. The other half only a scheduler can say: that a configured value
/// is the schedule, that the attribute's own is where none is configured, and that a configured value
/// the parser refuses stops the scheduler being built rather than leaving a trigger that never fires.
/// </remarks>
public class DeclaredJobsConfigurationKeyTest
{
    private const string Declarations = """
        [QuartzJob(Name = "cleanup")]
        [CronTrigger("0 0 3 * * ?", ConfigurationKey = "Jobs:Cleanup:Cron")]
        [CronTrigger("0 0 12 ? * MON-FRI", Name = "noon", TimeZone = "UTC", ConfigurationKey = "Jobs:Cleanup:Noon")]
        [CronTrigger("0 0 18 * * ?", Name = "evening")]
        public sealed class CleanupJob : IJob
        {
            public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
        }
        """;

    [Test]
    public async Task AConfiguredScheduleIsReadThroughTheContainer()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet(Declarations));

        run.Diagnostics.Should().BeEmpty();
        run.Generated.Should().Contain("builder.AddTrigger<global::App.CleanupJob>((services, trigger) => trigger",
            "a schedule read from configuration needs the container, which only the (IServiceProvider, …) overload is handed");
        run.Generated.Should().Contain("builder.AddTrigger<global::App.CleanupJob>(trigger => trigger",
            "a schedule that is the attribute's alone keeps the shorter call, so what is written is what was asked for");
        run.Generated.Should().NotContain("System.Reflection").And.NotContain("Type.GetType",
            "the read is a service lookup, which a trimmed or native publish keeps");

        await Verify(run.Generated!, extension: "txt").UseDirectory("Verify").UseFileName("DeclaredJobsGeneratorTest_ConfigurationKey");
    }

    [Test]
    public void TheHelperIsWrittenOnlyWhenAScheduleIsConfigured()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            [QuartzJob]
            [CronTrigger("0 0 3 * * ?")]
            public sealed class CleanupJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """));

        run.Generated.Should().NotContain("ConfiguredCronExpression").And.NotContain("IConfiguration",
            "a call that is not in the attributes is not in the file");
    }

    /// <summary>
    /// The attribute's expression stays the fallback, so it is still checked where it was written.
    /// </summary>
    [Test]
    public async Task TheAttributesOwnExpressionIsStillCheckedAtBuildTime()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            [QuartzJob(Name = "cleanup")]
            [CronTrigger("0 0 12 * *", ConfigurationKey = "Jobs:Cleanup:Cron")]
            public sealed class CleanupJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """));

        IReadOnlyList<Diagnostic> reported = await AnalyzerRunner.Analyze<CronLiteralAnalyzer>(run.Output);

        Diagnostic diagnostic = reported.Should().ContainSingle(
            "the literal is what the trigger fires on wherever the key is not set, so a bad one is still a deployment waiting to fail").Subject;
        diagnostic.Id.Should().Be("QZ0001");
        run.TextAt(diagnostic).Should().Be("\"0 0 12 * *\"");
    }

    /// <summary>
    /// An assembly that cannot see <c>IConfiguration</c> cannot compile the read, and ignoring the key
    /// would be the silent failure it exists to prevent.
    /// </summary>
    [Test]
    public void AScheduleReadFromConfigurationWithoutConfigurationIsRefused()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(
            Snippet(Declarations + """

                [QuartzJob(Name = "archive")]
                [CronTrigger("0 0 1 * * ?")]
                public sealed class ArchiveJob : IJob
                {
                    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
                }
                """),
            withoutReferences: ["Microsoft.Extensions.Configuration.Abstractions.dll"]);

        run.Generated.Should().Contain("builder.AddJob<global::App.ArchiveJob>",
            "a job that names no key needs no configuration, and is registered as ever");
        run.Diagnostics.Should().HaveCount(2, "each schedule that names a key is reported where it was written")
            .And.AllSatisfy(x =>
            {
                x.Id.Should().Be("QZ1005");
                x.Severity.Should().Be(DiagnosticSeverity.Error, "a configured schedule that is never read is worse than a build that fails");
            });

        run.Diagnostics[0].GetMessage().Should().Be(
            "'CleanupJob' declares a schedule read from the configuration key 'Jobs:Cleanup:Cron', but Microsoft.Extensions.Configuration.IConfiguration "
            + "is not referenced here, so the key could never be read; reference Microsoft.Extensions.Configuration.Abstractions, or remove ConfigurationKey");
        run.TextAt(run.Diagnostics[0]).Should().StartWith("CronTrigger(\"0 0 3 * * ?\"");

        run.Generated.Should().NotContain("IConfiguration",
            "the file is still written, without the read, so the diagnostic is the one error the build shows");
    }

    [Test]
    public async Task AConfiguredExpressionIsTheSchedule()
    {
        await using Built built = await Build(new Dictionary<string, string?>
        {
            ["Jobs:Cleanup:Cron"] = "0 30 4 * * ?",
            ["Jobs:Cleanup:Noon"] = "0 15 13 ? * MON-FRI",
        });

        (await CronOf(built.Scheduler, "cleanup")).Should().Be("0 30 4 * * ?");
        (await CronOf(built.Scheduler, "noon")).Should().Be("0 15 13 ? * MON-FRI",
            "the key replaces the expression and nothing else: the schedule's time zone still comes from the attribute");
        (await CronOf(built.Scheduler, "evening")).Should().Be("0 0 18 * * ?");

        ICronTrigger noon = (ICronTrigger) (await built.Scheduler.GetTrigger(new TriggerKey("noon")))!;
        noon.TimeZone.Id.Should().Be(TimeZones.FindById("UTC").Id);
    }

    [Test]
    public async Task WithoutAConfiguredValueTheAttributesExpressionIsTheSchedule()
    {
        await using Built built = await Build(new Dictionary<string, string?>
        {
            ["Jobs:Cleanup:Noon"] = "0 15 13 ? * MON-FRI",
        });

        (await CronOf(built.Scheduler, "cleanup")).Should().Be("0 0 3 * * ?", "an unset key falls back to the literal the analyzer checked");
        (await CronOf(built.Scheduler, "noon")).Should().Be("0 15 13 ? * MON-FRI");
    }

    [Test]
    public async Task WithoutConfigurationRegisteredTheAttributesExpressionIsTheSchedule()
    {
        await using Built built = await Build(configuration: null);

        (await CronOf(built.Scheduler, "cleanup")).Should().Be("0 0 3 * * ?",
            "a container with no IConfiguration, as a standalone builder's has, still gets the declared schedule");
        (await CronOf(built.Scheduler, "noon")).Should().Be("0 0 12 ? * MON-FRI");
    }

    /// <summary>
    /// The loud failure: a configured value the parser refuses is an exception while the scheduler is
    /// built, naming the value, rather than a trigger that is registered and never fires.
    /// </summary>
    [TestCase("* * * * *", "*has 5 fields*")]
    [TestCase("", "*")]
    [TestCase("every ten minutes", "*")]
    public async Task AConfiguredValueTheParserRefusesStopsTheSchedulerBeingBuilt(string configured, string message)
    {
        Func<Task> act = async () =>
        {
            await using Built built = await Build(new Dictionary<string, string?> { ["Jobs:Cleanup:Cron"] = configured });
        };

        Exception? cause = (await act.Should().ThrowAsync<Exception>()).Which;
        while (cause is not null and not FormatException)
        {
            cause = cause.InnerException;
        }

        cause.Should().BeOfType<FormatException>(
            "the parser's own refusal is what reaches the host, so its message says what is wrong with the value");
        cause!.Message.Should().Match(message);
    }

    private static async Task<string> CronOf(IScheduler scheduler, string triggerName)
    {
        ITrigger? trigger = await scheduler.GetTrigger(new TriggerKey(triggerName));
        trigger.Should().NotBeNull("the declared registration adds every schedule the job declares");
        return ((ICronTrigger) trigger!).CronExpressionString!;
    }

    /// <summary>
    /// Runs the generator, loads what it wrote, and builds a scheduler with the generated
    /// <c>AddDeclaredJobs</c> — the registration an application's own build would produce.
    /// </summary>
    private static async Task<Built> Build(IReadOnlyDictionary<string, string?>? configuration)
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet(Declarations));
        Assembly generated = run.Load();

        MethodInfo addDeclaredJobs = generated.GetType("Quartz.QuartzDeclaredJobs", throwOnError: true)!
            .GetMethod("AddDeclaredJobs", BindingFlags.Public | BindingFlags.Static)!;

        ServiceCollection services = new();
        if (configuration is not null)
        {
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());
        }

        string schedulerName = "declared-" + Guid.NewGuid().ToString("N");
        services.AddQuartz(schedulerName, q => addDeclaredJobs.Invoke(null, [q]));

        ServiceProvider provider = services.BuildServiceProvider();
        try
        {
            IScheduler scheduler = await provider.GetRequiredKeyedService<ISchedulerFactory>(schedulerName).GetScheduler();
            return new Built(provider, scheduler);
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }
    }

    private sealed record Built(ServiceProvider Provider, IScheduler Scheduler) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Scheduler.Shutdown();
            await Provider.DisposeAsync();
        }
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
