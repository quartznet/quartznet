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

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Tests.Unit.Simpl;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// A clustered scheduler that chose no instance id generates one (#4091).
/// </summary>
/// <remarks>
/// <para>
/// Every scheduler starts on <c>NON_CLUSTERED</c>, and a cluster node recognises its own check-in row
/// and its own fired triggers by its id. Two nodes built from the quick start's <c>UseClustering()</c>
/// shared that id: each read the other's fired triggers as its own, neither saw the other fail, and a
/// restarted node's recovery replayed its live peer's firings.
/// </para>
/// <para>
/// The id is decided when the options are resolved, so these tests read the resolved options; the two
/// that build nodes do so over <see cref="ClusteredNodeBuilder" />'s clustered in-memory store, because
/// SQLite refuses clustering as the store initializes.
/// </para>
/// </remarks>
public sealed class ClusteredInstanceIdTest
{
    private const string ChosenId = "orders-0";

    [Test]
    public void UseClusteringGeneratesTheInstanceIdWhenNoneWasChosen()
    {
        QuartzSchedulerOptions options = Resolve(q => q.UsePersistentStore(store => store.UseClustering()));

        options.GenerateInstanceId.Should().BeTrue(
            "every node of a quick-start cluster otherwise answers to NON_CLUSTERED, and two nodes on one "
            + "id read each other's fired triggers as their own");
    }

    [Test]
    public void AnInstanceIdSetBeforeUseClusteringIsKept()
    {
        QuartzSchedulerOptions options = Resolve(q =>
        {
            q.ConfigureScheduler(scheduler => scheduler.InstanceId = ChosenId);
            q.UsePersistentStore(store => store.UseClustering());
        });

        options.InstanceId.Should().Be(ChosenId);
        options.GenerateInstanceId.Should().BeFalse(
            "an application that named its node, an ordinal pod for instance, has said what it is called");
    }

    [Test]
    public void AnInstanceIdSetAfterUseClusteringIsKept()
    {
        QuartzSchedulerOptions options = Resolve(q =>
        {
            q.UsePersistentStore(store => store.UseClustering());
            q.ConfigureScheduler(scheduler => scheduler.InstanceId = ChosenId);
        });

        options.InstanceId.Should().Be(ChosenId);
        options.GenerateInstanceId.Should().BeFalse(
            "the decision is a post-configure step, so an id configured after UseClustering() is seen too; "
            + "deciding at the call would turn generation on before the id arrived, and the generator would "
            + "then replace it");
    }

    [Test]
    public void AnInstanceIdFromConfigurationIsKept()
    {
        ServiceCollection services = new();
        services.AddQuartz(
            Section(("Scheduler:InstanceId", ChosenId)),
            q => q.UsePersistentStore(store => store.UseClustering()));

        QuartzSchedulerOptions options = Options(services);

        options.InstanceId.Should().Be(ChosenId);
        options.GenerateInstanceId.Should().BeFalse(
            "Quartz:Scheduler:InstanceId is how a deployment names its nodes without touching code");
    }

    [Test]
    public void GenerateInstanceIdAlreadyOnIsLeftOn()
    {
        QuartzSchedulerOptions options = Resolve(q =>
        {
            q.ConfigureScheduler(scheduler => scheduler.GenerateInstanceId = true);
            q.UsePersistentStore(store => store.UseClustering());
        });

        options.GenerateInstanceId.Should().BeTrue();
        options.InstanceId.Should().Be(QuartzSchedulerOptions.DefaultInstanceId,
            "only the switch is turned on; the id itself is the generator's to produce at startup");
    }

    [Test]
    public void AnUnclusteredStoreIsLeftAlone()
    {
        QuartzSchedulerOptions options = Resolve(q => q.UsePersistentStore(store => store.ConfigureStore(options => options.TablePrefix = "QRTZ_")));

        options.GenerateInstanceId.Should().BeFalse(
            "a single-node scheduler shares its database with nobody, and NON_CLUSTERED says exactly that");
    }

    [Test]
    public void EachNamedSchedulerDecidesForItself()
    {
        ServiceCollection services = new();
        services.AddQuartz("orders", q => q.UsePersistentStore(store => store.UseClustering()));
        services.AddQuartz("billing", q =>
        {
            q.ConfigureScheduler(scheduler => scheduler.InstanceId = "billing-0");
            q.UsePersistentStore(store => store.UseClustering());
        });
        services.AddQuartz("reports");

        using ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<QuartzSchedulerOptions> monitor = provider.GetRequiredService<IOptionsMonitor<QuartzSchedulerOptions>>();

        monitor.Get("orders").GenerateInstanceId.Should().BeTrue(
            "the clustered scheduler that chose no id gets one");
        monitor.Get("billing").InstanceId.Should().Be("billing-0");
        monitor.Get("billing").GenerateInstanceId.Should().BeFalse(
            "the one that named its node keeps the name, whatever its neighbour in the container did");
        monitor.Get("reports").GenerateInstanceId.Should().BeFalse(
            "the post-configure step is registered under one scheduler's name and reaches no other");
    }

    [TestCase("quartz.jobStore.clustered")]
    [TestCase("quartz.jobStore.clustering.enabled")]
    public void AClusteredKeyGeneratesTheInstanceId(string key)
    {
        QuartzSchedulerOptions options = Resolve(new NameValueCollection { [key] = "true" });

        options.GenerateInstanceId.Should().BeTrue(
            "a cluster configured from a quartz.config file is the same cluster as one configured in code, "
            + "and its nodes need distinct ids just the same");
    }

    [Test]
    public void AHierarchicalClusteringSectionGeneratesTheInstanceId()
    {
        ServiceCollection services = new();
        services.AddQuartz(Section(("JobStore:Clustering:Enabled", "true")));

        Options(services).GenerateInstanceId.Should().BeTrue(
            "Quartz:JobStore:Clustering:Enabled is the appsettings.json spelling of UseClustering()");
    }

    [Test]
    public void AClusteredKeyArrivingThroughQuartzOptionsGeneratesTheInstanceId()
    {
        ServiceCollection services = new();
        services.AddQuartz();
        services.Configure<QuartzOptions>(options => options.Properties["quartz.jobStore.clustered"] = "true");

        Options(services).GenerateInstanceId.Should().BeTrue(
            "a key configured onto QuartzOptions is read when the options are resolved, like every other key");
    }

    [Test]
    public void AClusteredKeyKeepsAnInstanceIdKey()
    {
        QuartzSchedulerOptions options = Resolve(new NameValueCollection
        {
            ["quartz.jobStore.clustered"] = "true",
            ["quartz.scheduler.instanceId"] = ChosenId,
        });

        options.InstanceId.Should().Be(ChosenId);
        options.GenerateInstanceId.Should().BeFalse("quartz.scheduler.instanceId named the node");
    }

    [Test]
    public void AClusteredKeyKeepsAnInstanceIdSetInCode()
    {
        ServiceCollection services = new();
        services.AddQuartz(
            new NameValueCollection { ["quartz.jobStore.clustered"] = "true" },
            q => q.ConfigureScheduler(scheduler => scheduler.InstanceId = ChosenId));

        QuartzSchedulerOptions options = Options(services);

        options.InstanceId.Should().Be(ChosenId);
        options.GenerateInstanceId.Should().BeFalse(
            "the keys are applied before the configuration callback, so deciding there rather than after "
            + "every Configure would turn generation on before the id set in code arrived");
    }

    [TestCase("false", null)]
    [TestCase("yes", null)]
    [TestCase("true", "false")]
    public void AKeyThatDoesNotSayClusteredChangesNothing(string clustered, string? enabled)
    {
        NameValueCollection properties = new() { ["quartz.jobStore.clustered"] = clustered };
        if (enabled is not null)
        {
            properties["quartz.jobStore.clustering.enabled"] = enabled;
        }

        QuartzSchedulerOptions options = Resolve(properties);

        options.GenerateInstanceId.Should().BeFalse(
            "false is not clustered; a value that is not a boolean is the clustering options' to refuse when "
            + "a store reads them, not the scheduler options'; and the hierarchical spelling decides when "
            + "both are given, as it does for ClusteringOptions.Enabled");
    }

    [Test]
    public async Task AChosenGeneratorIsKept()
    {
        await using StandaloneSchedulerFactory node = ClusteredNodeBuilder.Build(q =>
        {
            q.UsePersistentStore(store => store.UseClustering());
            q.UseInstanceIdGenerator<FixedInstanceIdGenerator>();
        });

        IScheduler scheduler = await node.GetScheduler();

        scheduler.SchedulerInstanceId.Should().Be(FixedInstanceIdGenerator.Id,
            "a generator the application chose is what derives the id, not the default one");
    }

    /// <summary>
    /// The defect as reported: two nodes built the quick start's way, with no id, are two nodes.
    /// </summary>
    /// <remarks>
    /// Each <see cref="QuartzSchedulerBuilder" /> owns its own container and repository, so these are two
    /// schedulers rather than one looked up twice by name.
    /// </remarks>
    [Test]
    public async Task TwoNodesBuiltWithUseClusteringGetDistinctInstanceIds()
    {
        await using StandaloneSchedulerFactory firstNode = ClusteredNodeBuilder.Build(UseClusteringOnly);
        await using StandaloneSchedulerFactory secondNode = ClusteredNodeBuilder.Build(UseClusteringOnly);

        IScheduler first = await firstNode.GetScheduler();
        IScheduler second = await secondNode.GetScheduler();

        first.SchedulerInstanceId.Should().NotBe(QuartzSchedulerOptions.DefaultInstanceId,
            "NON_CLUSTERED is the id of a scheduler that shares its database with nobody");
        first.SchedulerInstanceId.Should().NotBe(second.SchedulerInstanceId,
            "two nodes of one cluster that agree on their id are indistinguishable in every table the store "
            + "writes");
    }

    [Test]
    public async Task TwoNodesClusteredByTheFlatKeyGetDistinctInstanceIds()
    {
        NameValueCollection properties = new()
        {
            ["quartz.scheduler.instanceName"] = nameof(TwoNodesClusteredByTheFlatKeyGetDistinctInstanceIds),
            ["quartz.jobStore.clustered"] = "true",
        };

        await using StandaloneSchedulerFactory firstNode = ClusteredNodeBuilder.Build(properties);
        await using StandaloneSchedulerFactory secondNode = ClusteredNodeBuilder.Build(properties);

        IScheduler first = await firstNode.GetScheduler();
        IScheduler second = await secondNode.GetScheduler();

        first.SchedulerInstanceId.Should().NotBe(QuartzSchedulerOptions.DefaultInstanceId);
        first.SchedulerInstanceId.Should().NotBe(second.SchedulerInstanceId,
            "a 3.x quartz.config carried over unchanged forms the same cluster UseClustering() does");
    }

    private static void UseClusteringOnly(IQuartzBuilder quartz)
    {
        quartz.ConfigureScheduler(options => options.InstanceName = nameof(TwoNodesBuiltWithUseClusteringGetDistinctInstanceIds));
        quartz.UsePersistentStore(store => store.UseClustering());
    }

    private static QuartzSchedulerOptions Resolve(Action<IQuartzBuilder> configure)
    {
        ServiceCollection services = new();
        services.AddQuartz(configure);
        return Options(services);
    }

    private static QuartzSchedulerOptions Resolve(NameValueCollection properties)
    {
        ServiceCollection services = new();
        services.AddQuartz(properties);
        return Options(services);
    }

    private static QuartzSchedulerOptions Options(ServiceCollection services)
    {
        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value;
    }

    private static IConfiguration Section(params (string Key, string Value)[] values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(x => new KeyValuePair<string, string?>("Quartz:" + x.Key, x.Value)))
            .Build()
            .GetSection("Quartz");
    }

    public sealed class FixedInstanceIdGenerator : IInstanceIdGenerator
    {
        public const string Id = "chosen-by-the-application";

        public ValueTask<string> GenerateInstanceId(CancellationToken cancellationToken = default) => new(Id);
    }
}
