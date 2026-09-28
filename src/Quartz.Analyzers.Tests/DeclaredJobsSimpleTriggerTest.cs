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

using System.Collections.Concurrent;
using System.Reflection;

using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using QuartzAnalyzers::Quartz.Analyzers;

namespace Quartz.Analyzers.Tests;

/// <summary>
/// <c>[SimpleTrigger]</c>: what the generator writes for it, what it reports, and — by running the
/// generated registration against a real scheduler — the schedule it turns into.
/// </summary>
public class DeclaredJobsSimpleTriggerTest
{
    [Test]
    public async Task AnIntervalIsWrittenAsTheTicksThisBuildRead()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            [QuartzJob]
            [SimpleTrigger("00:10:00")]
            public sealed class PollJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """));

        run.Diagnostics.Should().BeEmpty("a job and one interval is a complete declaration");
        run.Generated.Should().Contain(".WithInterval(global::System.TimeSpan.FromTicks(6000000000)) // 00:10:00",
            "the interval was parsed at build time, so the registration carries the ticks and nothing parses the string again");
        run.Generated.Should().Contain(".RepeatForever()", "an unwritten RepeatCount is -1, which a hand-written registration spells RepeatForever()");
        run.Generated.Should().Contain(".WithIdentity(\"PollJob\")", "a job naming nothing is named after its class, and its first schedule after the job");

        await Verify(run.Generated!, extension: "txt").UseDirectory("Verify").UseFileName("DeclaredJobsGeneratorTest_SimpleTrigger");
    }

    [Test]
    public async Task EveryPropertyReachesTheRegistration()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            [QuartzJob(Name = "poll", Group = "inbox")]
            [SimpleTrigger(
                "00:00:30",
                Name = "poll-fast",
                Group = "polling",
                RepeatCount = 9,
                MisfireInstruction = SimpleTriggerMisfireInstruction.NextWithRemainingCount,
                Priority = 7,
                Description = "every thirty seconds, ten times",
                ExecutionGroup = "io")]
            public sealed class PollJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """));

        run.Diagnostics.Should().BeEmpty();
        run.Generated.Should().Contain(".WithRepeatCount(9)")
            .And.Contain("global::Quartz.SimpleTriggerMisfireInstruction.NextWithRemainingCount",
                "the misfire instruction is the simple trigger family's, not the cron one's");

        await Verify(run.Generated!, extension: "txt").UseDirectory("Verify").UseFileName("DeclaredJobsGeneratorTest_SimpleTriggerEveryProperty");
    }

    [Test]
    public async Task IntervalAndCronSchedulesCountUpTogether()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            [QuartzJob(Name = "sync")]
            [SimpleTrigger("00:05:00")]
            [CronTrigger("0 0 3 * * ?")]
            [SimpleTrigger("1.00:00:00", RepeatCount = 0, MisfireInstruction = (SimpleTriggerMisfireInstruction) 42)]
            public sealed class SyncJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """));

        run.Diagnostics.Should().BeEmpty();
        run.Generated.Should().Contain(".WithIdentity(\"sync-2\")").And.Contain(".WithIdentity(\"sync-3\")",
            "the default names count the schedules in the order they are written, whichever kind each is");
        run.Generated.Should().Contain(".WithRepeatCount(0)", "a repeat count of zero is one firing, which is a schedule too")
            .And.Contain("(global::Quartz.SimpleTriggerMisfireInstruction) 42");

        await Verify(run.Generated!, extension: "txt").UseDirectory("Verify").UseFileName("DeclaredJobsGeneratorTest_SimpleAndCronSchedules");
    }

    [Test]
    public async Task AConfiguredIntervalIsReadThroughTheContainer()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet(ConfiguredDeclarations));

        run.Diagnostics.Should().BeEmpty();
        run.Generated.Should().Contain("builder.AddTrigger<global::App.PollJob>((services, trigger) => trigger",
            "an interval read from configuration needs the container, which only the (IServiceProvider, …) overload is handed");
        run.Generated.Should().Contain("ConfiguredInterval(services, \"Jobs:Poll:Interval\", global::System.TimeSpan.FromTicks(6000000000))");
        run.Generated.Should().NotContain("ConfiguredCronExpression", "a helper nothing calls is not written");
        run.Generated.Should().NotContain("System.Reflection").And.NotContain("Type.GetType",
            "the read is a service lookup, which a trimmed or native publish keeps");

        await Verify(run.Generated!, extension: "txt").UseDirectory("Verify").UseFileName("DeclaredJobsGeneratorTest_SimpleTriggerConfigurationKey");
    }

    /// <summary>
    /// A schedule QZ0005 refuses is not written, and the ones after it keep the names their position
    /// gives them.
    /// </summary>
    [Test]
    public async Task AScheduleTheAnalyzerRefusesIsNotGenerated()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            [QuartzJob(Name = "poll")]
            [SimpleTrigger("ten minutes")]
            [SimpleTrigger("00:00:00")]
            [SimpleTrigger("00:10:00", RepeatCount = -2)]
            [SimpleTrigger(null!)]
            [SimpleTrigger("00:10:00")]
            public sealed class PollJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """));

        run.Diagnostics.Should().BeEmpty("each refused value is reported once, by QZ0005, where it was written");
        run.Generated.Should().Contain(".WithIdentity(\"poll-5\")",
            "a schedule keeps the name its position gives it, so supplying the refused values later renames nothing");
        run.Generated!.Split("builder.AddTrigger").Should().HaveCount(2, "the one valid schedule is the only one written");
        run.Generated.Should().NotContain(".StoreDurably", "one schedule is still declared, so the job needs no durability to survive");

        IReadOnlyList<Diagnostic> reported = await AnalyzerRunner.Analyze<SimpleTriggerLiteralAnalyzer>(run.Output);

        reported.Select(run.TextAt).Should().Equal(["\"ten minutes\"", "\"00:00:00\"", "-2", "null!"],
            "the generator skips exactly the schedules the analyzer reports, each where it was written");
    }

    [Test]
    public void AJobWhoseOnlyIntervalIsRefusedIsStoredDurably()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            [QuartzJob(Name = "poll")]
            [SimpleTrigger("-00:10:00")]
            public sealed class PollJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """));

        run.Generated.Should().NotContain("AddTrigger");
        run.Generated.Should().Contain(".StoreDurably(true)",
            "with QZ0005 turned down to a warning the build succeeds, and a non-durable job with no trigger is refused when it is added");
    }

    [Test]
    public async Task AnIntervalMissingItsArgumentIsLeftToTheCompiler()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(
            Snippet("""
                [QuartzJob(Name = "poll")]
                [SimpleTrigger]
                public sealed class PollJob : IJob
                {
                    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
                }
                """),
            toleratedErrors: ["CS7036"]);

        run.Output.GetDiagnostics().Should().Contain(x => x.Id == "CS7036", "the compiler refuses an attribute missing its required argument");
        run.Diagnostics.Should().BeEmpty();
        run.Generated.Should().Contain("builder.AddJob<global::App.PollJob>")
            .And.NotContain("AddTrigger", "an attribute carrying no interval has no schedule to write");

        IReadOnlyList<Diagnostic> reported = await AnalyzerRunner.Analyze<SimpleTriggerLiteralAnalyzer>(run.Output);
        reported.Should().BeEmpty("there is no value to read, and the compiler has already said so");
    }

    [Test]
    public void AnIntervalWithoutAJobIsRefused()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            [SimpleTrigger("00:10:00")]
            [SimpleTrigger("00:20:00")]
            public sealed class PollJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """));

        Diagnostic diagnostic = run.Diagnostics.Should().ContainSingle("the fix is the missing attribute, and it is missing once").Subject;
        diagnostic.Id.Should().Be("QZ1003");
        diagnostic.GetMessage().Should().Be(
            "'PollJob' carries [SimpleTrigger] without [QuartzJob], so the schedule declares a trigger for a job that is never registered");
        run.TextAt(diagnostic).Should().Be("SimpleTrigger(\"00:10:00\")");
    }

    [Test]
    public void AClassCarryingBothKindsWithoutAJobIsReportedOnce()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            [SimpleTrigger("00:10:00")]
            [CronTrigger("0 0 3 * * ?")]
            public sealed class PollJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }

            [SimpleTrigger("00:10:00")]
            public sealed class OtherJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """));

        run.Diagnostics.Should().HaveCount(2, "one report per class, however many schedules of whichever kind it carries")
            .And.AllSatisfy(x => x.Id.Should().Be("QZ1003"));
        run.Diagnostics.Select(x => x.GetMessage()).Should().BeEquivalentTo(
            [
                "'OtherJob' carries [SimpleTrigger] without [QuartzJob], so the schedule declares a trigger for a job that is never registered",
                "'PollJob' carries [CronTrigger] without [QuartzJob], so the schedule declares a trigger for a job that is never registered",
            ]);
    }

    [Test]
    public void AnIntervalAndACronScheduleWithOneKeyAreRefused()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet("""
            [QuartzJob(Name = "sync")]
            [CronTrigger("0 0 3 * * ?", Name = "nightly")]
            [SimpleTrigger("1.00:00:00", Name = "nightly")]
            public sealed class SyncJob : IJob
            {
                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
            }
            """));

        run.Diagnostics.Should().ContainSingle("a trigger key is an identity whichever schedule the trigger has").Which.GetMessage().Should()
            .Be("More than one declared trigger resolves to the key 'DEFAULT.nightly'; give one of them a different Name or Group");
    }

    [Test]
    public void AnIntervalReadFromConfigurationWithoutConfigurationIsRefused()
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(
            Snippet(ConfiguredDeclarations),
            withoutReferences: ["Microsoft.Extensions.Configuration.Abstractions.dll"]);

        Diagnostic diagnostic = run.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Id.Should().Be("QZ1005");
        diagnostic.GetMessage().Should().StartWith("'PollJob' declares a schedule read from the configuration key 'Jobs:Poll:Interval'");
        run.Generated.Should().NotContain("IConfiguration").And.Contain(".WithInterval(global::System.TimeSpan.FromTicks(6000000000))",
            "the file is still written with the attribute's interval, so the diagnostic is the one error the build shows");
    }

    [Test]
    public void SourceIsNotWrittenAgainForAnEditThatChangedNothing()
    {
        IReadOnlyList<IncrementalStepRunReason> reasons = AnalyzerRunner.RerunReasons<DeclaredJobsGenerator>(Snippet(ConfiguredDeclarations));

        reasons.Should().NotBeEmpty();
        reasons.Should().AllSatisfy(x => x.Should().Be(IncrementalStepRunReason.Cached),
            "an interval schedule is a value like the rest of the model, so the same declaration compares equal to itself");
    }

    [Test]
    public async Task TheBuiltTriggerIsTheDeclaredSchedule()
    {
        await using Built built = await Build(ConfiguredDeclarations, configuration: null);

        ISimpleTrigger poll = await SimpleTriggerOf(built.Scheduler, "PollJob");
        poll.RepeatInterval.Should().Be(TimeSpan.FromMinutes(10));
        poll.RepeatCount.Should().Be(-1, "an unwritten RepeatCount repeats forever");
        poll.MisfireInstructionCode.Should().Be((int) SimpleTriggerMisfireInstruction.SmartPolicy);

        ISimpleTrigger warmUp = await SimpleTriggerOf(built.Scheduler, "warm-up");
        warmUp.RepeatInterval.Should().Be(TimeSpan.FromSeconds(5));
        warmUp.RepeatCount.Should().Be(3);
        warmUp.MisfireInstructionCode.Should().Be((int) SimpleTriggerMisfireInstruction.FireNow);
        warmUp.Priority.Should().Be(8);
        warmUp.Description.Should().Be("four quick polls as the host starts");
        warmUp.ExecutionGroup.Should().Be("io");
        warmUp.JobKey.Should().Be(new JobKey("PollJob"));
    }

    [Test]
    public async Task AConfiguredIntervalIsTheSchedule()
    {
        await using Built built = await Build(ConfiguredDeclarations, new Dictionary<string, string?> { ["Jobs:Poll:Interval"] = "00:02:30" });

        ISimpleTrigger poll = await SimpleTriggerOf(built.Scheduler, "PollJob");
        poll.RepeatInterval.Should().Be(TimeSpan.FromSeconds(150));
        poll.RepeatCount.Should().Be(-1, "the key replaces the interval and nothing else");
    }

    [Test]
    public async Task WithoutAConfiguredValueTheAttributesIntervalIsTheSchedule()
    {
        await using Built built = await Build(ConfiguredDeclarations, new Dictionary<string, string?> { ["Jobs:Other"] = "00:02:30" });

        (await SimpleTriggerOf(built.Scheduler, "PollJob")).RepeatInterval.Should().Be(TimeSpan.FromMinutes(10),
            "an unset key falls back to the interval the analyzer checked");
    }

    /// <summary>
    /// The loud failure: a configured value that is not a positive interval stops the scheduler being
    /// built, naming the key and the value, rather than registering a trigger that never fires.
    /// </summary>
    [TestCase("ten minutes")]
    [TestCase("")]
    [TestCase("00:00:00")]
    [TestCase("-00:10:00")]
    public async Task AConfiguredValueThatIsNotAPositiveIntervalStopsTheSchedulerBeingBuilt(string configured)
    {
        Func<Task> act = async () =>
        {
            await using Built built = await Build(ConfiguredDeclarations, new Dictionary<string, string?> { ["Jobs:Poll:Interval"] = configured });
        };

        Exception? cause = (await act.Should().ThrowAsync<Exception>()).Which;
        while (cause is not null and not FormatException)
        {
            cause = cause.InnerException;
        }

        cause.Should().BeOfType<FormatException>("a bad configured interval is refused while the host starts, not discovered when nothing fires");
        cause!.Message.Should().Be(
            $"The configuration key 'Jobs:Poll:Interval' holds '{configured}', which is not a positive TimeSpan. "
            + "Spell the trigger's interval the way TimeSpan does, invariantly: \"00:10:00\" for ten minutes, \"1.00:00:00\" for a day.");
    }

    /// <summary>
    /// The declared job fires on an in-memory store, as often as it said, as far apart as it said.
    /// </summary>
    [Test]
    public async Task TheDeclaredJobFiresOnTheDeclaredInterval()
    {
        await using Built built = await Build("""
            [QuartzJob(Name = "tick")]
            [SimpleTrigger("00:00:00.200", RepeatCount = 2)]
            public sealed class TickJob : IJob
            {
                public static readonly System.Collections.Concurrent.ConcurrentQueue<DateTimeOffset> Fired = new();

                public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
                {
                    Fired.Enqueue(context.ScheduledFireTimeUtc!.Value);
                    return default;
                }
            }
            """, configuration: null);

        ConcurrentQueue<DateTimeOffset> fired = (ConcurrentQueue<DateTimeOffset>) built.Generated
            .GetType("App.TickJob", throwOnError: true)!
            .GetField("Fired", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

        TriggerKey key = new TriggerKey("tick");
        (await built.Scheduler.GetTrigger(key)).Should().NotBeNull("the registration adds the trigger as the scheduler is built");

        await built.Scheduler.Start();

        using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (await built.Scheduler.GetTrigger(key, timeout.Token) is not null)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }

        DateTimeOffset[] times = [.. fired];
        times.Should().HaveCount(3, "RepeatCount = 2 is the first firing and two repeats, after which the trigger completes");
        times.Zip(times.Skip(1), (earlier, later) => later - earlier).Should().AllSatisfy(x =>
            x.Should().Be(TimeSpan.FromMilliseconds(200), "each firing is scheduled one declared interval after the last"));
    }

    private const string ConfiguredDeclarations = """
        [QuartzJob]
        [SimpleTrigger("00:10:00", ConfigurationKey = "Jobs:Poll:Interval")]
        [SimpleTrigger(
            "00:00:05",
            Name = "warm-up",
            RepeatCount = 3,
            MisfireInstruction = SimpleTriggerMisfireInstruction.FireNow,
            Priority = 8,
            Description = "four quick polls as the host starts",
            ExecutionGroup = "io")]
        public sealed class PollJob : IJob
        {
            public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
        }
        """;

    private static async Task<ISimpleTrigger> SimpleTriggerOf(IScheduler scheduler, string triggerName)
    {
        ITrigger? trigger = await scheduler.GetTrigger(new TriggerKey(triggerName));
        return trigger.Should().BeAssignableTo<ISimpleTrigger>("the declared registration adds every interval the job declares as a simple trigger").Subject;
    }

    /// <summary>
    /// Runs the generator, loads what it wrote, and builds a scheduler with the generated
    /// <c>AddDeclaredJobs</c> on the default in-memory store — the registration an application's own
    /// build would produce.
    /// </summary>
    private static async Task<Built> Build(string declarations, IReadOnlyDictionary<string, string?>? configuration)
    {
        GeneratorRun run = AnalyzerRunner.RunGenerator<DeclaredJobsGenerator>(Snippet(declarations));
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
            return new Built(provider, scheduler, generated);
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }
    }

    private sealed record Built(ServiceProvider Provider, IScheduler Scheduler, Assembly Generated) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Scheduler.Shutdown(waitForJobsToComplete: true);
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
