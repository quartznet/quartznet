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

#nullable enable

using System.Collections.Specialized;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// How a node says which execution groups it serves: the two option lists, the rules they are held to,
/// the flat keys and the builder sugar that fill them, and what the scheduler reports back (#4069).
/// </summary>
/// <remarks>
/// The lists are configuration a node reads once, so every mistake they can carry has to be refused at
/// startup with the words that say what to do instead — a node that quietly served the wrong groups
/// would be the trap the feature exists to avoid, one level up.
/// </remarks>
public class ExecutionGroupRoutingConfigurationTest
{
    [Test]
    public void ServingAndRefusingAtOnceIsRefusedAtStartup()
    {
        Action act = () => Resolve(options =>
        {
            options.ServedExecutionGroups.Add("reports");
            options.RefusedExecutionGroups.Add("gpu");
        });

        act.Should().Throw<OptionsValidationException>(
                "a node lists what it serves or what it refuses; both at once has no meaning the other list does not already say")
            .WithMessage("*ServedExecutionGroups and RefusedExecutionGroups are both set*clear one of them*");
    }

    [TestCase("*", "*the execution limits' catch-all*leaving both lists empty*")]
    [TestCase("tenant:*", "*a prefix*Routing has no prefix form*")]
    [TestCase("", "*blank entry*")]
    [TestCase("   ", "*blank entry*")]
    public void AnEntryThatIsNotAGroupNameIsRefusedByName(string entry, string expectedMessage)
    {
        Action act = () => Resolve(options => options.ServedExecutionGroups.Add(entry));

        act.Should().Throw<OptionsValidationException>(
                "the limits' spellings mean something in a limits key and nothing in a route, and saying so beats a node that serves nothing")
            .WithMessage(expectedMessage);
    }

    [Test]
    public void AGroupNamedTwiceIsRefused()
    {
        Action act = () => Resolve(options => options.RefusedExecutionGroups.AddRange(["reports", "reports"]));

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*RefusedExecutionGroups names 'reports' more than once*");
    }

    [Test]
    public void TheUngroupedBucketSpelledBothWaysIsOneEntry()
    {
        Action act = () => Resolve(options => options.ServedExecutionGroups.AddRange(["_", "null"]));

        act.Should().Throw<OptionsValidationException>(
                "'_' and 'null' are the two spellings of one bucket, the ones an execution limit's key uses")
            .WithMessage("*ungrouped bucket more than once*");
    }

    [Test]
    public void MoreThanAThousandGroupsIsRefused()
    {
        Action act = () => Resolve(options =>
        {
            for (int i = 0; i <= ExecutionGroupRouting.MaxGroups; i++)
            {
                options.ServedExecutionGroups.Add("group-" + i);
            }
        });

        act.Should().Throw<OptionsValidationException>("the list becomes an IN list, and Oracle refuses one of more than a thousand")
            .WithMessage("*at most 1000*");
    }

    [Test]
    public void TheUngroupedBucketAloneIsAServeList()
    {
        QuartzSchedulerOptions options = Resolve(options => options.ServedExecutionGroups.Add("_"));

        ExecutionGroupRouting? routing = ExecutionGroupRouting.From(options);

        routing.Should().NotBeNull();
        routing!.IsServeList.Should().BeTrue();
        routing.IncludesUngrouped.Should().BeTrue("'_' is the bucket for triggers that carry no group");
        routing.Groups.Should().BeEmpty();
        routing.Serves(null).Should().BeTrue();
        routing.Serves("reports").Should().BeFalse("a serve-list that names only the bucket serves nothing else");
        routing.Served.Should().ContainSingle().Which.IsDefault.Should().BeTrue();
    }

    [Test]
    public void ARefuseListThatOmitsTheBucketStillServesUngroupedTriggers()
    {
        QuartzSchedulerOptions options = Resolve(options => options.RefusedExecutionGroups.Add("gpu"));

        ExecutionGroupRouting routing = ExecutionGroupRouting.From(options)!;

        routing.IsServeList.Should().BeFalse();
        routing.Serves(null).Should().BeTrue("a refuse-list refuses what it names, and the bucket was not named");
        routing.Serves("gpu").Should().BeFalse();
        routing.Serves("reports").Should().BeTrue();
        routing.Serves("GPU").Should().BeTrue("names are compared ordinally, as job type names are");
    }

    [Test]
    public void NoListAtAllIsNoRouting()
    {
        ExecutionGroupRouting.From(new QuartzSchedulerOptions()).Should().BeNull(
            "a node that lists nothing serves every group, and nothing downstream should pay for a filter that filters nothing");
    }

    [Test]
    public void TheLegacyKeysBindTrimmedAndOnceAcrossBothPasses()
    {
        NameValueCollection properties = new()
        {
            ["quartz.scheduler.servedExecutionGroups"] = " reports , gpu ,_",
        };

        ServiceCollection services = new();
        services.AddQuartz(properties);
        using ServiceProvider provider = services.BuildServiceProvider();

        QuartzSchedulerOptions options = provider.GetRequiredService<IOptionsMonitor<QuartzSchedulerOptions>>().Get(Options.DefaultName);

        options.ServedExecutionGroups.Should().Equal(["reports", "gpu", "_"],
            "the flat key is comma-separated and each entry is trimmed; the bridge maps the bag once when the properties are applied "
            + "and again when the options resolve, and the second pass must not list every group twice");
        options.RefusedExecutionGroups.Should().BeEmpty();
    }

    [Test]
    public void TheRefusedLegacyKeyBindsTheOtherList()
    {
        NameValueCollection properties = new()
        {
            ["quartz.scheduler.refusedExecutionGroups"] = "gpu",
        };

        ServiceCollection services = new();
        services.AddQuartz(properties);
        using ServiceProvider provider = services.BuildServiceProvider();

        QuartzSchedulerOptions options = provider.GetRequiredService<IOptionsMonitor<QuartzSchedulerOptions>>().Get(Options.DefaultName);

        options.RefusedExecutionGroups.Should().Equal(["gpu"]);
        options.ServedExecutionGroups.Should().BeEmpty();
    }

    [Test]
    public void AHierarchicalSectionBindsTheListAndIsNotRefusedAsAnUnknownKey()
    {
        ServiceCollection services = new();
        services.BindQuartzOptions(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Quartz:Scheduler:ServedExecutionGroups:0"] = "reports",
                ["Quartz:Scheduler:ServedExecutionGroups:1"] = "_",
            })
            .Build()
            .GetSection("Quartz"));
        using ServiceProvider provider = services.BuildServiceProvider();

        QuartzSchedulerOptions options = provider.GetRequiredService<IOptionsMonitor<QuartzSchedulerOptions>>().Get(Options.DefaultName);

        options.ServedExecutionGroups.Should().Equal(["reports", "_"],
            "the typed binder owns the list; the flattener must not synthesize an indexed key nothing reads and the validator would refuse");
    }

    [Test]
    public void TheBuilderSugarLandsInTheNamedSchedulersOptions()
    {
        ServiceCollection services = new();
        services.AddQuartz("reports", q => q.ServeExecutionGroups("reports", "_"));
        services.AddQuartz("gpu", q => q.RefuseExecutionGroups("reports"));
        using ServiceProvider provider = services.BuildServiceProvider();

        IOptionsMonitor<QuartzSchedulerOptions> monitor = provider.GetRequiredService<IOptionsMonitor<QuartzSchedulerOptions>>();

        monitor.Get("reports").ServedExecutionGroups.Should().Equal(["reports", "_"]);
        monitor.Get("reports").RefusedExecutionGroups.Should().BeEmpty();
        monitor.Get("gpu").RefusedExecutionGroups.Should().Equal(["reports"], "each scheduler's options are its own");
        monitor.Get("gpu").ServedExecutionGroups.Should().BeEmpty();
    }

    [Test]
    public void CallingBothBuilderMethodsFailsAtValidation()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.ServeExecutionGroups("reports").RefuseExecutionGroups("gpu"));
        using ServiceProvider provider = services.BuildServiceProvider();

        Action act = () => provider.GetRequiredService<IOptionsMonitor<QuartzSchedulerOptions>>().Get(Options.DefaultName);

        act.Should().Throw<OptionsValidationException>().WithMessage("*both set*");
    }

    [Test]
    public async Task TheSchedulerReportsWhatItRoutesAndTheStoreIsHandedIt()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.ServeExecutionGroups("reports", "_"));
        await using ServiceProvider provider = services.BuildServiceProvider();

        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        SchedulerMetadata metadata = await scheduler.GetMetadata();

        metadata.ServedExecutionGroups.Should().Equal(["_", "reports"],
            "the metadata reports the configuration spellings, bucket first, so the health check and the dashboard can rebuild the predicate");
        metadata.RefusedExecutionGroups.Should().BeEmpty();
    }

    [Test]
    public async Task ASchedulerThatRoutesNothingReportsEmptyLists()
    {
        ServiceCollection services = new();
        services.AddQuartz();
        await using ServiceProvider provider = services.BuildServiceProvider();

        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        SchedulerMetadata metadata = await scheduler.GetMetadata();

        metadata.ServedExecutionGroups.Should().BeEmpty();
        metadata.RefusedExecutionGroups.Should().BeEmpty();
    }

    /// <summary>
    /// The whole way round: the option lands on the acquisition request the scheduler thread makes, and
    /// the in-memory store honours it, so a refused group never runs on this node while a served one does.
    /// </summary>
    [Test]
    public async Task ARefusedGroupNeverFiresOnTheNodeThatRefusesIt()
    {
        RoutedJob.Reset();

        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.RefuseExecutionGroups("gpu");
            q.ConfigureScheduler(options => options.IdleWaitTime = TimeSpan.FromSeconds(1));
        });
        await using ServiceProvider provider = services.BuildServiceProvider();

        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        IJobDetail job = JobBuilder.Create<RoutedJob>().WithIdentity("routed").StoreDurably().Build();
        await scheduler.AddJob(job);
        await scheduler.ScheduleJob(TriggerBuilder.Create().WithIdentity("gpu-1").ForJob(job).WithExecutionGroup("gpu").StartNow().Build());
        await scheduler.ScheduleJob(TriggerBuilder.Create().WithIdentity("reports-1").ForJob(job).WithExecutionGroup("reports").StartNow().Build());
        await scheduler.Start();

        try
        {
            (await RoutedJob.Fired.Task.WaitAsync(TimeSpan.FromSeconds(30))).Should().Be("reports-1",
                "the served group's trigger is the one that fires; the refused one is left for a node that serves it");

            // Long enough for several acquisition rounds on a one-second idle wait.
            await Task.Delay(TimeSpan.FromSeconds(3));

            RoutedJob.Executions.Should().Equal(["reports-1"], "the refused trigger never ran here");
            (await scheduler.GetTriggerState(new TriggerKey("gpu-1"))).Should().Be(TriggerState.Normal,
                "it is neither acquired nor misfire-handled: it waits, as it was, for a node that serves gpu");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    private static QuartzSchedulerOptions Resolve(Action<QuartzSchedulerOptions> configure)
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.ConfigureScheduler(configure));
        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptionsMonitor<QuartzSchedulerOptions>>().Get(Options.DefaultName);
    }

    private sealed class RoutedJob : IJob
    {
        private static readonly List<string> executions = [];

        public static TaskCompletionSource<string> Fired { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static List<string> Executions
        {
            get
            {
                lock (executions)
                {
                    return [.. executions];
                }
            }
        }

        public static void Reset()
        {
            lock (executions)
            {
                executions.Clear();
            }

            Fired = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            lock (executions)
            {
                executions.Add(context.Trigger.Key.Name);
            }

            Fired.TrySetResult(context.Trigger.Key.Name);
            return default;
        }
    }
}
