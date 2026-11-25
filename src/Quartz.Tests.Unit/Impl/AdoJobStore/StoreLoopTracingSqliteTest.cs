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
using System.Diagnostics;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Quartz.Diagnostics;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Tests.Unit.Diagnostics;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// #4375: with <see cref="QuartzSchedulerOptions.TraceRootStoreOperations" /> off, a misfire scan or a
/// cluster check-in run outside any activity is made current as the unrecorded stand-in a store call is,
/// so a parent-based sampler drops the database driver's spans beneath it.
/// </summary>
/// <remarks>
/// <para>
/// The store is clustered once initialized, as <c>ClusterLeaveSqliteTest</c>'s are, and never started. A
/// check-in pass is the one <see cref="ClusterManager.Initialize" /> runs inline; the loop it then starts
/// sleeps on a clock nothing advances. A misfire pass is the first one the misfire loop runs, which ends
/// when the loop asks that clock for its sleep.
/// </para>
/// <para>
/// The delegate opens a driver span for each command it prepares, under whatever is current. Not
/// parallelizable, because the stand-in is seen on the default source, which every fixture shares.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class StoreLoopTracingSqliteTest
{
    private const string SchedulerName = "StoreLoopTracingSqliteTest";
    private const string SchedulerId = "node-a";
    private const string DataSourceName = "store-loop-tracing";
    private const string CheckInPass = "Quartz.ClusterManager.CheckIn";
    private const string ScanPass = "Quartz.MisfireHandler.Scan";

    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    private SqliteTestDatabase database = null!;
    private SleepCountingClock clock = null!;
    private TracingSqliteDelegate statements = null!;
    private Meters meters = null!;
    private LocalTransactionJobStore store = null!;
    private ParentBasedTracing tracing = null!;
    private ActivityListener standIns = null!;
    private readonly List<Activity> standInsStarted = [];
    private readonly List<Activity> standInsStopped = [];

    [SetUp]
    public async Task BuildStore()
    {
        database = new SqliteTestDatabase("store-loop-tracing");
        await using (SqliteConnection connection = new(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = new(await File.ReadAllTextAsync(TablesScript()), connection);
            await command.ExecuteNonQueryAsync();
        }

        clock = new SleepCountingClock(new FakeTimeProvider(epoch));
        meters = new Meters(meterFactory: null);
        statements = new TracingSqliteDelegate();
        store = new LocalTransactionJobStore(TestJobStores.Dependencies(
            timeProvider: clock,
            schedulerOptions: TestJobStores.SchedulerOptions(instanceName: SchedulerName, instanceId: SchedulerId),
            storeOptions: TestJobStores.StoreOptions(DataSourceName),
            dbProvider: new DbProvider("SQLite-Microsoft", database.ConnectionString),
            driverDelegate: statements));
        store.Meters = meters;

        await store.Initialize(TestJobStores.Identity(instanceName: SchedulerName, instanceId: SchedulerId));
        store.Clustered = true;

        lock (standInsStarted)
        {
            standInsStarted.Clear();
            standInsStopped.Clear();
        }

        // The stand-in is created without a source, so it is announced on the default one, whose name is empty.
        standIns = new ActivityListener
        {
            ShouldListenTo = static source => source.Name.Length == 0,
            ActivityStarted = activity => Keep(standInsStarted, activity),
            ActivityStopped = activity => Keep(standInsStopped, activity),
        };
        ActivitySource.AddActivityListener(standIns);

        // Only once the store is built, so the statements its initialization ran open no spans.
        tracing = new ParentBasedTracing(listenToQuartz: true);
        statements.Tracing = tracing;
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        statements.Tracing = null;
        tracing.Dispose();
        standIns.Dispose();
        await store.Shutdown();
        meters.Dispose();
        database.Dispose();
    }

    [Test]
    public async Task ACheckIn_WithRootOperationsOn_LeavesTheDriverSpansAsRoots()
    {
        store.TraceRootStoreOperations = true;

        Activity.Current = null;
        await CheckInOnce();

        tracing.StoppedChildren.Should().NotBeEmpty("the check-in ran its statements").And
            .OnlyContain(a => a.ParentSpanId == default, "nothing was current and the option asks for nothing to be made so");
        StandIns(CheckInPass).Should().BeEmpty();
    }

    [Test]
    public async Task ACheckIn_WithRootOperationsOff_RunsUnderAStandInAndItsDriverSpansAreDropped()
    {
        store.TraceRootStoreOperations = false;

        Activity.Current = null;
        await CheckInOnce();

        tracing.ChildrenStarted.Should().BePositive("the check-in still ran its statements");
        tracing.StoppedChildren.Should().BeEmpty(
            "under an unsampled parent a parent-based sampler drops them, where with nothing current each would be a root");
        Activity.Current.Should().BeNull();
        AssertOneUnrecordedStandIn(CheckInPass);
    }

    /// <summary>
    /// The first check-in runs from the call that starts the scheduler. Inside that caller's activity it is
    /// the caller's work, and its driver spans stay in the caller's trace.
    /// </summary>
    [Test]
    public async Task TheFirstCheckIn_WithRootOperationsOff_InsideTheCallersActivity_StaysInTheCallersTrace()
    {
        store.TraceRootStoreOperations = false;

        Activity.Current = null;
        using (Activity caller = tracing.StartCaller())
        {
            await CheckInOnce();

            Activity.Current.Should().BeSameAs(caller);
            tracing.StoppedChildren.Should().NotBeEmpty().And
                .OnlyContain(a => a.TraceId == caller.TraceId && a.ParentSpanId == caller.SpanId);
        }

        StandIns(CheckInPass).Should().BeEmpty("a pass that has a parent needs no stand-in");
    }

    [Test]
    public async Task AMisfireScan_WithRootOperationsOn_LeavesTheDriverSpansAsRoots()
    {
        store.TraceRootStoreOperations = true;

        Activity.Current = null;
        await ScanOnce();

        tracing.StoppedChildren.Should().NotBeEmpty("the scan ran its statements").And
            .OnlyContain(a => a.ParentSpanId == default);
        StandIns(ScanPass).Should().BeEmpty();
    }

    [Test]
    public async Task AMisfireScan_WithRootOperationsOff_RunsUnderAStandInAndItsDriverSpansAreDropped()
    {
        store.TraceRootStoreOperations = false;

        Activity.Current = null;
        await ScanOnce();

        tracing.ChildrenStarted.Should().BePositive("the scan still ran its statements");
        tracing.StoppedChildren.Should().BeEmpty();
        Activity.Current.Should().BeNull();
        AssertOneUnrecordedStandIn(ScanPass);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task TheCheckInDuration_IsRecordedEitherWay(bool traceRootStoreOperations)
    {
        store.TraceRootStoreOperations = traceRootStoreOperations;
        using MetricCollector<double> checkins = new(meters.Meter, QuartzInstrumentation.Instruments.ClusterCheckinDuration);

        Activity.Current = null;
        await CheckInOnce();

        checkins.GetMeasurementSnapshot().Should().ContainSingle("the option is about spans, not metrics");
    }

    private async Task CheckInOnce()
    {
        ClusterManager clusterManager = new(store, NullLogger<ClusterManager>.Instance);
        await clusterManager.Initialize();
        await clusterManager.Shutdown();
    }

    /// <summary>
    /// Runs the misfire loop until its first pass is over, which is when it asks the clock for its sleep,
    /// then shuts it down: the handler's thread is a foreground one and would keep the test host alive.
    /// </summary>
    private async Task ScanOnce()
    {
        int sleepsBefore = clock.TimersCreated;
        MisfireHandler misfireHandler = new(store, NullLogger<MisfireHandler>.Instance);
        misfireHandler.Initialize();
        try
        {
            // Real time: the loop runs on its own thread, and only wall time lets it get to its sleep.
            for (int i = 0; i < 1000 && clock.TimersCreated == sleepsBefore; i++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5));
            }

            clock.TimersCreated.Should().BeGreaterThan(sleepsBefore, "the misfire loop was expected to have finished its first pass by now");
        }
        finally
        {
            await misfireHandler.Shutdown();
        }
    }

    private List<Activity> StandIns(string name)
    {
        lock (standInsStarted)
        {
            return standInsStarted.Where(a => a.OperationName == name).ToList();
        }
    }

    private void AssertOneUnrecordedStandIn(string name)
    {
        Activity standIn = StandIns(name).Should().ContainSingle("one stand-in per pass, not one per statement").Subject;
        standIn.Recorded.Should().BeFalse();
        standIn.IsAllDataRequested.Should().BeFalse();

        lock (standInsStarted)
        {
            standInsStopped.Should().Contain(standIn, "the pass stops what it started, in its own context (#3797)");
        }
    }

    private void Keep(List<Activity> activities, Activity activity)
    {
        lock (standInsStarted)
        {
            activities.Add(activity);
        }
    }

    private static string TablesScript()
    {
        return File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";
    }

    /// <summary>A clock that never moves, counting the timers created on it, which are the loops' sleeps.</summary>
    private sealed class SleepCountingClock(FakeTimeProvider inner) : TimeProvider
    {
        private int timersCreated;

        public int TimersCreated => Volatile.Read(ref timersCreated);

        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

        public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

        public override long TimestampFrequency => inner.TimestampFrequency;

        public override long GetTimestamp() => inner.GetTimestamp();

        public override ITimer CreateTimer(TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period)
        {
            ITimer timer = inner.CreateTimer(callback, state, dueTime, period);
            Interlocked.Increment(ref timersCreated);
            return timer;
        }
    }

    /// <summary>The shipped SQLite delegate, opening a driver span for every command it prepares.</summary>
    private sealed class TracingSqliteDelegate : SQLiteDelegate
    {
        public ParentBasedTracing Tracing { get; set; }

        public override DbCommand PrepareCommand(ConnectionAndTransactionHolder cth, string commandText)
        {
            Tracing?.OpenChild();
            return base.PrepareCommand(cth, commandText);
        }
    }
}
