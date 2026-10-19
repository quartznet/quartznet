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

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Tests.Unit.Plugin.History;
using Quartz.Tests.Unit.Simpl;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// A clustered node that generates its instance id carries a different one at every start, so a declared
/// trigger pinned to a node by name prefers a node no start of it ever is. The node says so once, as it
/// applies the declared triggers (#4139).
/// </summary>
/// <remarks>
/// The nodes are built over <see cref="ClusteredNodeBuilder" />'s clustered in-memory store: the id is
/// generated only when the store reports itself clustered, and the warning needs nothing else of one.
/// </remarks>
public sealed class PinnedTriggerGeneratedInstanceIdWarningTest
{
    /// <summary>Log event <c>ConfigurationLog.PinnedTriggersUnderGeneratedInstanceId</c>.</summary>
    private const int PinnedTriggersUnderGeneratedInstanceId = 4031;

    private RecordingLoggerProvider logs;

    [SetUp]
    public void CreateLogs()
    {
        logs = new RecordingLoggerProvider();
    }

    [TearDown]
    public void DisposeLogs()
    {
        logs.Dispose();
    }

    [Test]
    public async Task AFixedPinOnANodeWithAGeneratedInstanceIdWarnsOnce()
    {
        await using StandaloneSchedulerFactory node = ClusteredNodeBuilder.Build(q =>
        {
            Clustered(q);
            Pinned(q, "nightly", PreferredNode.For("node-1"));
        });

        IScheduler scheduler = await node.GetScheduler();

        LogEntry warning = logs.Entries.Should().ContainSingle(x => x.EventId.Id == PinnedTriggersUnderGeneratedInstanceId,
            "the fix is the node's configuration, so one warning covers every pinned trigger").Which;
        warning.Level.Should().Be(LogLevel.Warning);
        warning.Message.Should().Contain($"generated instance id '{scheduler.SchedulerInstanceId}'",
                "the reader has to see which id this start carries to see why no pin can name it")
            .And.Contain("1 declared trigger(s) are pinned to a named node: 'DEFAULT.nightly'")
            .And.Contain("Set InstanceId (quartz.scheduler.instanceId)")
            .And.Contain("PreferredNode.Auto");
    }

    [Test]
    public async Task AFixedPinInASchedulingFileWarns()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Schedule:Jobs:0:Name"] = "reports",
                ["Schedule:Jobs:0:JobType"] = typeof(ReportJob).AssemblyQualifiedName,
                ["Schedule:Jobs:0:Durable"] = "true",
                ["Schedule:Triggers:0:Name"] = "nightly",
                ["Schedule:Triggers:0:JobName"] = "reports",
                ["Schedule:Triggers:0:PreferredNode"] = "node-1",
                ["Schedule:Triggers:0:Cron:Expression"] = "0 0 2 * * ?",
            })
            .Build();

        await using StandaloneSchedulerFactory node = ClusteredNodeBuilder.Build(Clustered, configuration);

        await node.GetScheduler();

        logs.Entries.Should().ContainSingle(x => x.EventId.Id == PinnedTriggersUnderGeneratedInstanceId,
                "a Quartz:Schedule file declares its triggers as code does, and its pins break the same way")
            .Which.Message.Should().Contain("pinned to a named node: 'DEFAULT.nightly'");
    }

    [Test]
    public async Task AChosenInstanceIdDoesNotWarn()
    {
        await using StandaloneSchedulerFactory node = ClusteredNodeBuilder.Build(q =>
        {
            Clustered(q);
            q.ConfigureScheduler(options => options.InstanceId = "node-1");
            Pinned(q, "nightly", PreferredNode.For("node-1"));
        });

        IScheduler scheduler = await node.GetScheduler();

        scheduler.SchedulerInstanceId.Should().Be("node-1");
        logs.Entries.Should().NotContain(x => x.EventId.Id == PinnedTriggersUnderGeneratedInstanceId,
            "a chosen id is the same at every start, so a pin can name it");
    }

    [Test]
    public async Task AChosenGeneratorDoesNotWarn()
    {
        await using StandaloneSchedulerFactory node = ClusteredNodeBuilder.Build(q =>
        {
            Clustered(q);
            q.UseInstanceIdGenerator<ClusteredInstanceIdTest.FixedInstanceIdGenerator>();
            Pinned(q, "nightly", PreferredNode.For(ClusteredInstanceIdTest.FixedInstanceIdGenerator.Id));
        });

        await node.GetScheduler();

        logs.Entries.Should().NotContain(x => x.EventId.Id == PinnedTriggersUnderGeneratedInstanceId,
            "a generator the application chose derives the id by its own rule, such as the host name, which a pin can name");
    }

    [Test]
    public async Task AnAutomaticPinDoesNotWarn()
    {
        await using StandaloneSchedulerFactory node = ClusteredNodeBuilder.Build(q =>
        {
            Clustered(q);
            Pinned(q, "nightly", PreferredNode.Auto);
        });

        await node.GetScheduler();

        logs.Entries.Should().NotContain(x => x.EventId.Id == PinnedTriggersUnderGeneratedInstanceId,
            "an automatic pin is claimed by whichever node fires the trigger, whatever its id");
    }

    [Test]
    public async Task AnUnclusteredStoreDoesNotWarn()
    {
        await using StandaloneSchedulerFactory node = QuartzSchedulerBuilder.Create(q =>
        {
            q.Services.AddLogging(logging => logging.AddProvider(logs));
            q.ConfigureScheduler(options => options.GenerateInstanceId = true);
            Pinned(q, "nightly", PreferredNode.For("node-1"));
        }).Build();

        IScheduler scheduler = await node.GetScheduler();

        scheduler.SchedulerInstanceId.Should().Be(QuartzSchedulerOptions.DefaultInstanceId,
            "a store that shares its database with nobody is never given a generated id");
        logs.Entries.Should().NotContain(x => x.EventId.Id == PinnedTriggersUnderGeneratedInstanceId,
            "a pin means nothing without a cluster, and the node-affinity page says so already");
    }

    [Test]
    public async Task ManyPinsAreCountedAndTheFirstFiveNamed()
    {
        await using StandaloneSchedulerFactory node = ClusteredNodeBuilder.Build(q =>
        {
            Clustered(q);
            for (int i = 1; i <= 7; i++)
            {
                Pinned(q, $"pinned-{i}", PreferredNode.For("node-1"));
            }

            Pinned(q, "automatic", PreferredNode.Auto);
            Pinned(q, "unpinned", PreferredNode.None);
        });

        await node.GetScheduler();

        LogEntry warning = logs.Entries.Should().ContainSingle(x => x.EventId.Id == PinnedTriggersUnderGeneratedInstanceId).Which;
        warning.Message.Should().Contain(
            "7 declared trigger(s) are pinned to a named node: 'DEFAULT.pinned-1', 'DEFAULT.pinned-2', 'DEFAULT.pinned-3', "
            + "'DEFAULT.pinned-4', 'DEFAULT.pinned-5' and 2 more.",
            "the count is every named pin, the automatic and unpinned triggers are not among them, and the list stops at five");
        warning.Message.Should().NotContain("pinned-6");
    }

    private void Clustered(IQuartzBuilder quartz)
    {
        quartz.Services.AddLogging(logging => logging.AddProvider(logs));
        quartz.UsePersistentStore(store => store.UseClustering());
    }

    private static void Pinned(IQuartzBuilder quartz, string name, PreferredNode preferredNode)
    {
        quartz.ScheduleJob<ReportJob>(trigger => trigger
            .WithIdentity(name)
            .WithPreferredNode(preferredNode)
            .StartAt(DateTimeOffset.UtcNow.AddHours(1)));
    }

    private sealed class ReportJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }
}
