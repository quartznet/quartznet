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

using System.Diagnostics.Metrics;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Quartz.Diagnostics;
using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The due-trigger gauges (#4401) on PostgreSQL: two nodes over one database report the same
/// whole-scheduler backlog, the groups a node does not serve included.
/// </summary>
/// <remarks>
/// The SQLite unit test covers the gauges' behaviour; this one holds the statement to PostgreSQL's
/// types and grouping, and shows why the series aggregate with <c>max</c>.
/// </remarks>
[TestFixture]
[NonParallelizable]
[Category("db-postgres")]
public sealed class DueTriggerGaugePostgresTest
{
    private const string SchedulerName = "DueTriggerGaugePostgresTest";
    private const string Group = "due";
    private const string Ungrouped = "(none)";

    private FakeTimeProvider clock;
    private Node servingA;
    private Node servingAll;

    [SetUp]
    public async Task BuildNodes()
    {
        string connectionString = Environment.GetEnvironmentVariable("PG_CONNECTION_STRING");
        connectionString.Should().NotBeNullOrWhiteSpace(
            "PG_CONNECTION_STRING is set by the container this assembly starts; run the fixture through the db-postgres leg");

        // Whole seconds, so the ages below are exact.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        clock = new FakeTimeProvider(new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, TimeSpan.Zero));

        servingA = await Node.Build("node-a", served: ["a"], clock, connectionString);
        servingAll = await Node.Build("node-b", served: null, clock, connectionString);

        await servingA.Store.Clear();
    }

    [TearDown]
    public async Task ShutDownNodes()
    {
        await servingA.Store.Clear();
        await servingA.DisposeAsync();
        await servingAll.DisposeAsync();
    }

    [Test]
    public async Task EveryNodeReportsTheWholeSchedulersBacklog()
    {
        await ScheduleDue("a1", "a", TimeSpan.FromSeconds(20));
        await ScheduleDue("a2", "a", TimeSpan.FromSeconds(40));
        await ScheduleDue("b1", "b", TimeSpan.FromSeconds(10));
        await ScheduleDue("u1", executionGroup: null, TimeSpan.FromSeconds(30));
        await ScheduleDue("later", "a", TimeSpan.FromSeconds(-60));

        await servingA.Store.RecoverMisfires(Guid.NewGuid());
        await servingAll.Store.RecoverMisfires(Guid.NewGuid());

        Dictionary<string, double> expectedCounts = new() { ["a"] = 2, ["b"] = 1, [Ungrouped] = 1 };
        Dictionary<string, double> expectedAges = new() { ["a"] = 40, ["b"] = 10, [Ungrouped] = 30 };

        foreach (Node node in (Node[]) [servingA, servingAll])
        {
            node.Collect();

            node.ByGroup(QuartzInstrumentation.Instruments.TriggerDue).Should().BeEquivalentTo(expectedCounts,
                "{0} counts the whole scheduler, not only the groups it serves", node.InstanceId);
            node.ByGroup(QuartzInstrumentation.Instruments.TriggerDueAge).Should().BeEquivalentTo(expectedAges);
            node.Measurements.Should().AllSatisfy(x => x.Tags[ActivityTags.SchedulerId].Should().Be(node.InstanceId));
        }
    }

    private async Task ScheduleDue(string name, string executionGroup, TimeSpan dueAgo)
    {
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity(name, Group).Build();
        TriggerBuilder<IJob> builder = TriggerBuilder.Create(clock)
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(clock.GetUtcNow() - dueAgo);

        if (executionGroup is not null)
        {
            builder = builder.WithExecutionGroup(executionGroup);
        }

        IOperableTrigger trigger = (IOperableTrigger) builder.Build();
        trigger.ComputeFirstFireTimeUtc(calendar: null);
        await servingA.Store.ScheduleJob(job, trigger);
    }

    /// <summary>One store with its own meter and a listener on it, as one node of a cluster has.</summary>
    private sealed class Node : IAsyncDisposable
    {
        private readonly Meters meters = new(meterFactory: null);
        private readonly MeterListener listener = new();

        private Node(string instanceId, LocalTransactionJobStore store)
        {
            InstanceId = instanceId;
            Store = store;
            Store.Meters = meters;

            // Listening before the pass, so the store sees the gauges enabled and counts.
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (ReferenceEquals(instrument.Meter, meters.Meter))
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
            listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
            listener.Start();
        }

        public string InstanceId { get; }

        public LocalTransactionJobStore Store { get; }

        public List<(string Instrument, double Value, Dictionary<string, object> Tags)> Measurements { get; } = [];

        public static async Task<Node> Build(string instanceId, string[] served, TimeProvider clock, string connectionString)
        {
            QuartzSchedulerOptions schedulerOptions = new() { InstanceName = SchedulerName, InstanceId = instanceId };
            if (served is not null)
            {
                schedulerOptions.ServedExecutionGroups.AddRange(served);
            }

            LocalTransactionJobStore store = new(TestJobStores.Dependencies(
                timeProvider: clock,
                schedulerOptions: Options.Create(schedulerOptions),
                storeOptions: TestJobStores.StoreOptions("due-trigger-gauges"),
                dbProvider: new DbProvider(DataSourceOptions.Providers.Npgsql, connectionString),
                driverDelegate: new PostgreSQLDelegate()));

            await store.Initialize(new SchedulerIdentity { SchedulerName = SchedulerName, InstanceId = instanceId });
            Node node = new(instanceId, store);
            store.StartDueTriggerBacklog();
            return node;
        }

        public void Collect()
        {
            Measurements.Clear();
            listener.RecordObservableInstruments();
        }

        public Dictionary<string, double> ByGroup(string instrument) => Measurements
            .Where(x => string.Equals(x.Instrument, instrument, StringComparison.Ordinal))
            .ToDictionary(
                x => x.Tags.TryGetValue(ActivityTags.ExecutionGroup, out object group) ? (string) group : Ungrouped,
                x => x.Value);

        public async ValueTask DisposeAsync()
        {
            await Store.Shutdown();
            listener.Dispose();
            meters.Dispose();
        }

        private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object>> tags)
        {
            Dictionary<string, object> copy = new(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object> tag in tags)
            {
                copy[tag.Key] = tag.Value;
            }

            Measurements.Add((instrument.Name, value, copy));
        }
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
