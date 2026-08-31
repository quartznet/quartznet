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

using System.Data.Common;
using System.Diagnostics.Metrics;

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Quartz.Core;
using Quartz.Diagnostics;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Tests;

namespace Quartz.Tests.Unit.Diagnostics;

/// <summary>
/// The measurements a scheduler publishes about everything that is not a job execution: misfires,
/// trigger acquisition, and — for the one clustered store there is — check-in and recovery.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these was missing entirely, which is half of what
/// <see href="https://github.com/quartznet/quartznet/issues/3421">#3421</see> reported; the other half
/// was that no measurement said which node made it, so two nodes of a cluster were one series.
/// Measurements are matched by the scheduler name, which is unique per test.
/// </para>
/// <para>
/// Nothing here waits on a clock it does not control except the acquisition test, which needs the
/// scheduling loop to go round once and polls for the measurement under a timeout rather than sleeping
/// for a fixed period.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class SchedulerMetricsTest
{
    // Spelled out rather than read from the constants the product publishes them from: the wire name is
    // the contract a dashboard is written against, and reading both sides from one constant would let a
    // rename pass unnoticed.
    private const string MisfireCounter = "quartz.trigger.misfire";
    private const string AcquisitionDuration = "quartz.trigger.acquisition.duration";
    private const string AcquiredCounter = "quartz.trigger.acquired";
    private const string CheckinDuration = "quartz.cluster.checkin.duration";
    private const string RecoveryCounter = "quartz.cluster.recovery.trigger";

    private const string SchedulerNameTag = "quartz.scheduler.name";
    private const string SchedulerIdTag = "quartz.scheduler.id";
    private const string TriggerGroupTag = "quartz.trigger.group";
    private const string ExecutionGroupTag = "quartz.execution.group";
    private const string RecoveredInstanceIdTag = "quartz.cluster.recovered.instance.id";
    private const string ErrorTypeTag = "error.type";

    private readonly List<RecordedMeasurement> measurements = [];

    private MeterListener meterListener;

    [SetUp]
    public void SetUp()
    {
        lock (measurements)
        {
            measurements.Clear();
        }

        meterListener = new MeterListener
        {
            InstrumentPublished = static (instrument, listener) =>
            {
                if (instrument.Meter.Name == QuartzInstrumentation.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        meterListener.Start();
    }

    [TearDown]
    public void TearDown()
    {
        meterListener?.Dispose();
    }

    /// <summary>
    /// The in-memory store's own misfire detection, which happens as it hands triggers over.
    /// </summary>
    [Test]
    public async Task Misfire_FromTheInMemoryStore_IsCounted()
    {
        string schedulerName = UniqueName("misfire-ram");
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero));

        QuartzSchedulerResources resources = Resources(schedulerName, "node-a", clock);
        QuartzScheduler scheduler = new(resources, clock);

        // The store takes the signaler through its constructor and the signaler belongs to the scheduler,
        // so the store is built second and handed back to the resources it belongs to.
        RAMJobStore store = TestJobStores.Ram(scheduler.SchedulerSignaler, clock);
        resources.JobStore = store;
        await store.Initialize(new SchedulerIdentity { SchedulerName = schedulerName, InstanceId = "node-a" });

        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job", "jobs").Build();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .ForJob(job)
            .WithIdentity("trigger", "reports")
            .WithExecutionGroup("heavy")
            .StartAt(clock.GetUtcNow())
            .Build();
        trigger.ComputeFirstFireTimeUtc(null);

        await store.ScheduleJob(job, trigger);

        // Past the trigger's fire time by more than the misfire threshold, so the next acquisition finds
        // a firing that was owed and never happened.
        clock.Advance(store.MisfireThreshold + TimeSpan.FromMinutes(5));

        await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddMinutes(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero,
        });

        RecordedMeasurement misfire = MeasurementsFor(schedulerName)
            .Should().ContainSingle(m => m.Instrument == MisfireCounter,
                "a misfire is a firing that was owed and did not happen, which is the number an operator "
                + "puts an alert on — and no store published one")
            .Subject;

        misfire.Value.Should().Be(1);
        misfire.Unit.Should().Be("{trigger}");
        misfire.InstrumentType.Should().Be<Counter<long>>("misfires only ever accumulate");

        misfire.Tags.Should().Contain(new KeyValuePair<string, object>(SchedulerIdTag, "node-a"))
            .And.Contain(new KeyValuePair<string, object>(TriggerGroupTag, "reports"))
            .And.Contain(new KeyValuePair<string, object>(ExecutionGroupTag, "heavy"));

        misfire.Tags.Should().NotContainKey("quartz.trigger.name",
            "a misfire storm is a property of a group, and one series per trigger is a cardinality no "
            + "alert can be built on");
    }

    /// <summary>
    /// And the database store's misfire handler, which is a different scan in a different class — and
    /// arrives at the same counter, because the notification is what every store has in common.
    /// </summary>
    [Test]
    public async Task Misfire_FromThePersistentStoresMisfireHandler_IsCounted()
    {
        string schedulerName = UniqueName("misfire-ado");
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero));

        IDriverDelegate driverDelegate = A.Fake<IDriverDelegate>();
        MetricsAdoJobStore store = new(clock) { DirectDelegate = driverDelegate };

        QuartzSchedulerResources resources = Resources(schedulerName, "node-b", clock);
        resources.JobStore = store;
        QuartzScheduler scheduler = new(resources, clock);
        store.DirectSignaler = scheduler.SchedulerSignaler;
        await store.Initialize(new SchedulerIdentity { SchedulerName = schedulerName, InstanceId = "node-b" });

        IOperableTrigger misfired = (IOperableTrigger) TriggerBuilder.Create()
            .ForJob("job", "jobs")
            .WithIdentity("trigger", "reports")
            .Build();
        misfired.NextFireTimeUtc = clock.GetUtcNow().AddMinutes(-10);

        A.CallTo(() => driverDelegate.SelectMisfiredTriggersToRecover(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<StoredTriggerState>.Ignored,
                A<DateTimeOffset>.Ignored,
                A<int>.Ignored,
                A<CancellationToken>.Ignored))
            .Returns(new ValueTask<MisfiredTriggerBatch>(new MisfiredTriggerBatch([misfired], false)));

        // Initialized rather than started: the handler's own loop would run on a clock this test does not
        // own, and the scan is what is under test rather than the schedule it runs on.
        await store.RecoverMisfiredJobs(conn: null, recovering: false);

        RecordedMeasurement misfire = MeasurementsFor(schedulerName)
            .Should().ContainSingle(m => m.Instrument == MisfireCounter,
                "the database store's misfire handler signals the scheduler exactly as the in-memory "
                + "store does, so one recording site covers both")
            .Subject;

        misfire.Tags.Should().Contain(new KeyValuePair<string, object>(SchedulerIdTag, "node-b"))
            .And.Contain(new KeyValuePair<string, object>(TriggerGroupTag, "reports"));

        misfire.Tags.Should().NotContainKey(ExecutionGroupTag,
            "this trigger names no execution group, and an absent attribute is not the same series as an "
            + "empty one");
    }

    /// <summary>
    /// The scheduling loop's wait on its store, which is the round trip nothing else overlaps with.
    /// </summary>
    [Test]
    public async Task TriggerAcquisition_IsTimedAndCounted()
    {
        string schedulerName = UniqueName("acquisition");

        ServiceCollection services = new();
        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options =>
            {
                options.InstanceName = schedulerName;
                options.InstanceId = "node-c";
                // The shortest the options allow, so a round that finds nothing comes back around soon.
                options.IdleWaitTime = TimeSpan.FromSeconds(1);
            });
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        try
        {
            await scheduler.Start();
            await WaitFor(() => MeasurementsFor(schedulerName).Any(m => m.Instrument == AcquisitionDuration));
        }
        finally
        {
            await scheduler.Shutdown();
        }

        RecordedMeasurement acquisition = MeasurementsFor(schedulerName)
            .First(m => m.Instrument == AcquisitionDuration);

        acquisition.Unit.Should().Be("s", "OpenTelemetry records a duration in seconds");
        acquisition.Value.Should().BeGreaterThanOrEqualTo(0);
        acquisition.Tags.Should().Contain(new KeyValuePair<string, object>(SchedulerNameTag, schedulerName))
            .And.Contain(new KeyValuePair<string, object>(SchedulerIdTag, "node-c"));

        acquisition.Tags.Should().HaveCount(2,
            "which scheduler asked and which node it was is the whole of an acquisition's identity — a "
            // A round returns a batch, so there is no one trigger to name.
            + "round is about no particular trigger");

        MeasurementsFor(schedulerName).Should().NotContain(m => m.Instrument == AcquiredCounter,
            "an idle scheduler acquires nothing, and a counter that recorded the zeroes would report a "
            + "rate of acquisitions rather than of triggers");
    }

    /// <summary>
    /// A trigger that is there to be acquired turns the empty round into a counted one.
    /// </summary>
    [Test]
    public async Task TriggersAcquired_CountsWhatTheRoundReturned()
    {
        string schedulerName = UniqueName("acquired");

        ServiceCollection services = new();
        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options =>
            {
                options.InstanceName = schedulerName;
                options.InstanceId = "node-d";
                // The shortest the options allow, so a round that finds nothing comes back around soon.
                options.IdleWaitTime = TimeSpan.FromSeconds(1);
            });
            quartz.ScheduleJob<NoOpJob>(
                trigger => trigger.WithIdentity("acquired-trigger").StartNow(),
                job => job.WithIdentity("acquired-job"));
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        try
        {
            await scheduler.Start();
            await WaitFor(() => MeasurementsFor(schedulerName).Any(m => m.Instrument == AcquiredCounter));
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        RecordedMeasurement acquired = MeasurementsFor(schedulerName)
            .First(m => m.Instrument == AcquiredCounter);

        acquired.Value.Should().BeGreaterThan(0);
        acquired.Unit.Should().Be("{trigger}");
        acquired.InstrumentType.Should().Be<Counter<long>>();
        acquired.Tags.Should().Contain(new KeyValuePair<string, object>(SchedulerIdTag, "node-d"),
            "the count of work a node picked up is meaningless without knowing which node picked it up");
    }

    /// <summary>
    /// A check-in that slows down is how a cluster starts failing, so its latency is a measurement.
    /// </summary>
    [Test]
    public async Task ClusterCheckin_IsTimed()
    {
        MetricsAdoJobStore store = await ClusteredStore("checkin", "node-e");

        await store.CheckIn(Guid.NewGuid());

        RecordedMeasurement checkin = MeasurementsFor(store.InstanceName)
            .Should().ContainSingle(m => m.Instrument == CheckinDuration,
                "the other nodes decide this one died once its check-ins stop arriving, so how long they "
                + "take is worth watching before they stop")
            .Subject;

        checkin.Unit.Should().Be("s");
        checkin.Value.Should().BeGreaterThanOrEqualTo(0);
        checkin.Tags.Should().Contain(new KeyValuePair<string, object>(SchedulerIdTag, "node-e"));
        checkin.Tags.Should().NotContainKey(ErrorTypeTag, "this check-in succeeded");
    }

    /// <summary>
    /// And a check-in that fails is the same series wearing <c>error.type</c>.
    /// </summary>
    [Test]
    public async Task ClusterCheckin_ThatFails_IsTaggedWithTheFailure()
    {
        MetricsAdoJobStore store = await ClusteredStore("checkin-failure", "node-f");

        A.CallTo(() => store.FakeDelegate.SelectSchedulerStateRecords(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<string>.Ignored,
                A<CancellationToken>.Ignored))
            .Throws(new InvalidOperationException("the database is gone"));

        Func<Task> act = async () => await store.CheckIn(Guid.NewGuid());
        await act.Should().ThrowAsync<JobPersistenceException>();

        MeasurementsFor(store.InstanceName).Where(m => m.Instrument == CheckinDuration)
            .Should().NotBeEmpty()
            .And.AllSatisfy(m => m.Tags.Should().ContainKey(ErrorTypeTag)
                .WhoseValue.Should().Be(typeof(JobPersistenceException).FullName,
                    "a check-in that could not reach the database is what an alert is looking for, and it "
                    + "would otherwise be indistinguishable from a fast one"));
    }

    /// <summary>
    /// Recovery is counted against the node that failed, not the one doing the recovering.
    /// </summary>
    [Test]
    public async Task ClusterRecovery_IsCountedAgainstTheFailedNode()
    {
        MetricsAdoJobStore store = await ClusteredStore("recovery", "node-g");

        A.CallTo(() => store.FakeDelegate.SelectFiredTriggerRecords(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<FiredTriggerQuery>.Ignored,
                A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<FiredTriggerRecord>>(new List<FiredTriggerRecord>
            {
                FiredTrigger("one"),
                FiredTrigger("two"),
            }));

        // Zero check-in interval and timestamp is what an orphaned instance looks like, which is the one
        // shape recovery never defers.
        SchedulerStateRecord failed = new("dead-node", CheckinTimestamp: default, CheckinInterval: default);

        await store.CallClusterRecover([failed]);

        RecordedMeasurement recovery = MeasurementsFor(store.InstanceName)
            .Should().ContainSingle(m => m.Instrument == RecoveryCounter).Subject;

        recovery.Value.Should().Be(2, "both fired-trigger rows of the failed node were recovered");
        recovery.Unit.Should().Be("{trigger}");
        recovery.Tags.Should().Contain(new KeyValuePair<string, object>(SchedulerIdTag, "node-g"))
            .And.Contain(new KeyValuePair<string, object>(RecoveredInstanceIdTag, "dead-node"),
                "the node reporting the recovery and the node being recovered are two different nodes, so "
                + "they are two different attributes");
    }

    /// <summary>
    /// The one recovery a node reports about itself: it found its own state row gone, which means a peer
    /// judged it failed and took its work over.
    /// </summary>
    [Test]
    public async Task ClusterCheckin_ThatFindsItsOwnRowGone_IsCountedAsARecoveryOfThisNode()
    {
        MetricsAdoJobStore store = await ClusteredStore("self-recovery", "node-i");

        // Not the first check-in: a node starting up has no row of its own either, and that means
        // nothing more than that it has not written one yet.
        store.SetFirstCheckIn(false);

        A.CallTo(() => store.FakeDelegate.SelectSchedulerStateRecords(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<string>.Ignored,
                A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<SchedulerStateRecord>>(new List<SchedulerStateRecord>
            {
                new("node-peer", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(15)),
            }));

        await store.CheckIn(Guid.NewGuid());

        RecordedMeasurement recovery = MeasurementsFor(store.InstanceName)
            .Should().ContainSingle(m => m.Instrument == RecoveryCounter).Subject;

        recovery.Value.Should().Be(1,
            "how many fired triggers the peer took over is not knowable from here — the rows are gone — so "
            + "this records that it happened rather than how big it was");

        recovery.Tags.Should().Contain(new KeyValuePair<string, object>(SchedulerIdTag, "node-i"))
            .And.Contain(new KeyValuePair<string, object>(RecoveredInstanceIdTag, "node-i"),
                "the recovering node and the recovered node are the same node here, and that equality is "
                + "what an alert on 'this node is being failed out' matches");
    }

    /// <summary>
    /// The whole of what #3421 was about: no measurement could say which node made it.
    /// </summary>
    [Test]
    public async Task EveryMeasurement_CarriesTheSchedulerNameAndId()
    {
        MetricsAdoJobStore store = await ClusteredStore("identity", "node-h");
        await store.CheckIn(Guid.NewGuid());

        List<RecordedMeasurement> published = MeasurementsFor(store.InstanceName);

        published.Should().NotBeEmpty().And.AllSatisfy(m =>
        {
            m.Tags.Should().ContainKey(SchedulerNameTag).WhoseValue.Should().Be(store.InstanceName);
            m.Tags.Should().ContainKey(SchedulerIdTag).WhoseValue.Should().Be("node-h",
                "a cluster is several schedulers sharing one name, so the name alone never answers which "
                + "node a measurement came from");
        });
    }

    private static FiredTriggerRecord FiredTrigger(string name)
    {
        return new FiredTriggerRecord
        {
            FireInstanceId = name,
            SchedulerInstanceId = "dead-node",
            TriggerKey = new TriggerKey(name, "recovered"),
            JobKey = new JobKey(name, "recovered"),
            FireInstanceState = StoredTriggerState.Acquired,
            FireTimestamp = DateTimeOffset.UnixEpoch,
            ScheduleTimestamp = DateTimeOffset.UnixEpoch,
        };
    }

    /// <summary>A job nothing here runs; the tests never get as far as an execution.</summary>
    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// A clustered database store whose delegate answers everything with nothing, so a check-in runs
    /// end to end without a database.
    /// </summary>
    private static async Task<MetricsAdoJobStore> ClusteredStore(string prefix, string instanceId)
    {
        IDriverDelegate driverDelegate = A.Fake<IDriverDelegate>();

        A.CallTo(() => driverDelegate.SelectSchedulerStateRecords(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<string>.Ignored,
                A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<SchedulerStateRecord>>(new List<SchedulerStateRecord>()));

        A.CallTo(() => driverDelegate.SelectFiredTriggerInstanceNames(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<string>>(new List<string>()));

        A.CallTo(() => driverDelegate.SelectFiredTriggerRecords(
                A<ConnectionAndTransactionHolder>.Ignored,
                A<FiredTriggerQuery>.Ignored,
                A<CancellationToken>.Ignored))
            .Returns(new ValueTask<List<FiredTriggerRecord>>(new List<FiredTriggerRecord>()));

        string schedulerName = UniqueName(prefix);
        MetricsAdoJobStore store = new(TimeProvider.System, clustered: true)
        {
            DirectDelegate = driverDelegate,
            FakeDelegate = driverDelegate,
            Meters = Meters.Shared,
        };

        await store.Initialize(new SchedulerIdentity { SchedulerName = schedulerName, InstanceId = instanceId });
        return store;
    }

    /// <summary>
    /// The resources of a scheduler that exists to be signalled and nothing more. Constructing a
    /// scheduler over them starts no thread, so the store is what drives every one of these tests.
    /// </summary>
    private static QuartzSchedulerResources Resources(string name, string instanceId, TimeProvider timeProvider)
    {
        return new QuartzSchedulerResources
        {
            Name = name,
            InstanceId = instanceId,
            ThreadPool = new DefaultThreadPool { MaxConcurrency = 1 },
            JobRunShellFactory = new StdJobRunShellFactory(NullLogger<JobRunShell>.Instance),
            TimeProvider = timeProvider,
        };
    }

    private static string UniqueName(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    /// <summary>
    /// Polls until the condition holds, so a test never sleeps for a fixed period and never races the
    /// loop it is watching.
    /// </summary>
    private static async Task WaitFor(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the scheduling loop should have gone round by now");
            await Task.Delay(10);
        }
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object>> tags)
    {
        Dictionary<string, object> copy = new(tags.Length, StringComparer.Ordinal);
        foreach (KeyValuePair<string, object> tag in tags)
        {
            copy[tag.Key] = tag.Value;
        }

        lock (measurements)
        {
            measurements.Add(new RecordedMeasurement(instrument.Name, instrument.GetType(), instrument.Unit, value, copy));
        }
    }

    private List<RecordedMeasurement> MeasurementsFor(string schedulerName)
    {
        lock (measurements)
        {
            return measurements
                .Where(m => Equals(m.Tags.GetValueOrDefault(SchedulerNameTag), schedulerName))
                .ToList();
        }
    }

    private sealed record RecordedMeasurement(
        string Instrument,
        Type InstrumentType,
        string Unit,
        double Value,
        Dictionary<string, object> Tags);

    /// <summary>
    /// A database store that never opens a connection, so the cluster paths can be driven without one.
    /// </summary>
    internal sealed class MetricsAdoJobStore : AdoJobStoreBase
    {
        public MetricsAdoJobStore(TimeProvider timeProvider, bool clustered = false)
            : base(TestJobStores.Dependencies(
                timeProvider: timeProvider,
                clusteringOptions: TestJobStores.ClusteringOptions(configure: options => options.Enabled = clustered)))
        {
        }

        /// <summary>The same fake <see cref="DirectDelegate" /> is set to, kept for arranging calls.</summary>
        public IDriverDelegate FakeDelegate { get; set; }

        public IDriverDelegate DirectDelegate
        {
            set => typeof(AdoJobStoreBase)
                .GetField("driverDelegate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(this, value);
        }

        public ISchedulerSignaler DirectSignaler
        {
            set => typeof(AdoJobStoreBase)
                .GetField("signaler", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(this, value);
        }

        /// <summary>
        /// Writes the private flag that tells the check-in path this is not the node's first pass, which
        /// is what makes a missing row of its own mean it was failed out rather than that it is new.
        /// </summary>
        public void SetFirstCheckIn(bool value)
        {
            typeof(AdoJobStoreBase)
                .GetField("firstCheckIn", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(this, value);
        }

        public ValueTask CallClusterRecover(IReadOnlyCollection<SchedulerStateRecord> failedInstances)
        {
            return ClusterRecover(
                new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null),
                failedInstances,
                CancellationToken.None);
        }

        protected override ValueTask<ConnectionAndTransactionHolder> GetLocalTransactionConnection(CancellationToken cancellationToken = default)
        {
            return new ValueTask<ConnectionAndTransactionHolder>(new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null));
        }

        protected override ValueTask<T> ExecuteInLock<T>(
            SchedulerLock? lockKind,
            Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<T>(default(T));
        }
    }
}
