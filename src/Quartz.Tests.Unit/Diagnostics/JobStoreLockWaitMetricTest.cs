#nullable enable

using System.Data.Common;
using System.Diagnostics.Metrics;

using FakeItEasy;

using Microsoft.Extensions.Time.Testing;

using Quartz.Diagnostics;
using Quartz.Impl.AdoJobStore;
using Quartz.Tests;

namespace Quartz.Tests.Unit.Diagnostics;

/// <summary>
/// How long a clustered scheduler spends waiting for its job store lock, which is the number that says
/// a node is queued behind a peer — or behind a database session whose client is gone — before anything
/// has failed. Nothing else in the store reports it: a blocked lock statement neither returns nor
/// throws, so the operation histogram only shows the round trips that finished.
/// </summary>
/// <remarks>
/// The measurement is taken in the store rather than in the lock handler, so that every handler is
/// covered — the row-lock ones, the in-process monitor, SQLite's, and one written by an application —
/// and so that the tags a scheduler's other measurements carry are the tags this one carries.
/// </remarks>
[NonParallelizable]
public sealed class JobStoreLockWaitMetricTest
{
    // Spelled out rather than read from the constants the product publishes them from: the wire name is
    // the contract a dashboard is written against.
    private const string LockWaitDuration = "quartz.jobstore.lock.wait.duration";

    private const string SchedulerNameTag = "quartz.scheduler.name";
    private const string SchedulerIdTag = "quartz.scheduler.id";
    private const string LockTag = "quartz.jobstore.lock";
    private const string ErrorTypeTag = "error.type";

    private readonly List<RecordedMeasurement> measurements = [];

    private MeterListener meterListener = null!;

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
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        meterListener.Start();
    }

    [TearDown]
    public void TearDown()
    {
        meterListener?.Dispose();
    }

    [Test]
    public async Task AWaitForTheTriggerLockIsRecordedInSecondsUnderTheLockName()
    {
        string schedulerName = UniqueName("lock-wait");
        FakeTimeProvider clock = new();
        WaitingLockHandler lockHandler = new(clock, TimeSpan.FromSeconds(2));

        LockWaitStore store = new(schedulerName, lockHandler, clock);

        await store.CallExecuteInLocalTransactionLock(SchedulerLock.TriggerAccess);

        RecordedMeasurement wait = MeasurementsFor(schedulerName)
            .Should().ContainSingle(m => m.Instrument == LockWaitDuration).Which;

        wait.Unit.Should().Be("s", "a histogram's default bucket boundaries assume seconds");
        wait.Value.Should().Be(2, "the handler took two seconds of the store's clock to hand the lock over");
        wait.Tags[SchedulerIdTag].Should().Be("node-a",
            "a cluster is several schedulers sharing one name, and which node is queued is the question");
        wait.Tags[LockTag].Should().Be("TRIGGER_ACCESS");
        wait.Tags.Should().NotContainKey(ErrorTypeTag, "the lock was given");
    }

    [Test]
    public async Task AWaitForTheStateLockNamesThatLock()
    {
        string schedulerName = UniqueName("lock-wait-state");
        FakeTimeProvider clock = new();
        WaitingLockHandler lockHandler = new(clock, TimeSpan.FromSeconds(2));

        LockWaitStore store = new(schedulerName, lockHandler, clock);

        await store.CallExecuteInLocalTransactionLock(SchedulerLock.StateAccess);

        MeasurementsFor(schedulerName)
            .Should().ContainSingle(m => m.Instrument == LockWaitDuration)
            .Which.Tags[LockTag].Should().Be("STATE_ACCESS",
                "the check-in's lock and the scheduling lock stall for different reasons, so folding them "
                + "into one series would hide whichever is the quieter of the two");
    }

    [Test]
    public async Task AnAcquisitionThatFailedIsRecordedWithWhatItFailedWith()
    {
        string schedulerName = UniqueName("lock-wait-failed");
        FakeTimeProvider clock = new();
        WaitingLockHandler lockHandler = new(clock, TimeSpan.FromSeconds(2))
        {
            Failure = new LockException("could not obtain lock"),
        };

        LockWaitStore store = new(schedulerName, lockHandler, clock);

        Func<Task> act = async () => await store.CallExecuteInLocalTransactionLock(SchedulerLock.TriggerAccess);
        await act.Should().ThrowAsync<LockException>();

        RecordedMeasurement wait = MeasurementsFor(schedulerName)
            .Should().ContainSingle(m => m.Instrument == LockWaitDuration).Which;

        wait.Value.Should().Be(2, "a refused lock still waited, and that wait is the interesting one");
        wait.Tags[ErrorTypeTag].Should().Be(typeof(LockException).FullName,
            "leaving the failures out would report only the acquisitions that ended well");
    }

    [Test]
    public async Task AReentrantAcquisitionIsNotRecorded()
    {
        string schedulerName = UniqueName("lock-wait-reentrant");
        FakeTimeProvider clock = new();
        WaitingLockHandler lockHandler = new(clock, TimeSpan.Zero) { Acquired = false };

        LockWaitStore store = new(schedulerName, lockHandler, clock);

        await store.CallExecuteInLocalTransactionLock(SchedulerLock.TriggerAccess);

        MeasurementsFor(schedulerName).Should().NotContain(m => m.Instrument == LockWaitDuration,
            "false means this requestor already held the lock, so nothing was waited for and recording it "
            + "would pull the histogram towards zero with hand-backs nobody asked about");
    }

    [Test]
    public async Task AnOperationThatTakesNoLockRecordsNothing()
    {
        string schedulerName = UniqueName("lock-wait-none");
        FakeTimeProvider clock = new();
        WaitingLockHandler lockHandler = new(clock, TimeSpan.FromSeconds(2));

        LockWaitStore store = new(schedulerName, lockHandler, clock);

        await store.CallExecuteInLocalTransactionLock(lockKind: null);

        MeasurementsFor(schedulerName).Should().NotContain(m => m.Instrument == LockWaitDuration,
            "a read that runs outside the lock never asked the handler for one");
    }

    private static string UniqueName(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        Dictionary<string, object?> copy = new(tags.Length, StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> tag in tags)
        {
            copy[tag.Key] = tag.Value;
        }

        lock (measurements)
        {
            measurements.Add(new RecordedMeasurement(instrument.Name, instrument.Unit, value, copy));
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
        string? Unit,
        double Value,
        Dictionary<string, object?> Tags);

    /// <summary>
    /// A lock handler that costs the store a configurable amount of its own clock, which is what a
    /// contended row lock looks like to everything above it.
    /// </summary>
    private sealed class WaitingLockHandler : ILockHandler
    {
        private readonly FakeTimeProvider clock;
        private readonly TimeSpan wait;

        public WaitingLockHandler(FakeTimeProvider clock, TimeSpan wait)
        {
            this.clock = clock;
            this.wait = wait;
        }

        /// <summary>What the acquisition answers, when it answers at all.</summary>
        public bool Acquired { get; init; } = true;

        /// <summary>What the acquisition throws instead of answering.</summary>
        public Exception? Failure { get; init; }

        public bool RequiresConnection => false;

        public void Initialize(LockHandlerContext context)
        {
        }

        public ValueTask<bool> AcquireLock(
            Guid requestorId,
            ConnectionAndTransactionHolder? conn,
            SchedulerLock lockKind,
            CancellationToken cancellationToken = default)
        {
            clock.Advance(wait);

            if (Failure is not null)
            {
                return ValueTask.FromException<bool>(Failure);
            }

            return new ValueTask<bool>(Acquired);
        }

        public ValueTask ReleaseLock(Guid requestorId, SchedulerLock lockKind, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// A store that never opens a connection, so the lock paths can be driven without a database.
    /// </summary>
    private sealed class LockWaitStore : AdoJobStoreBase
    {
        public LockWaitStore(string schedulerName, ILockHandler lockHandler, TimeProvider timeProvider)
            : base(TestJobStores.Dependencies(
                timeProvider: timeProvider,
                schedulerOptions: TestJobStores.SchedulerOptions(schedulerName, "node-a"),
                storeOptions: TestJobStores.StoreOptions(configure: options =>
                {
                    // One attempt, so a failed acquisition is one measurement rather than a series of
                    // them, and no retry is waited out on a clock the test controls.
                    options.MaxTransientRetries = 0;
                    options.TransientRetryInterval = TimeSpan.Zero;
                }),
                lockHandler: lockHandler))
        {
        }

        public ValueTask<bool> CallExecuteInLocalTransactionLock(SchedulerLock? lockKind)
        {
            return ExecuteInLocalTransactionLock(lockKind, _ => new ValueTask<bool>(true));
        }

        protected override ValueTask<ConnectionAndTransactionHolder> GetLocalTransactionConnection(CancellationToken cancellationToken = default)
        {
            return new ValueTask<ConnectionAndTransactionHolder>(
                new ConnectionAndTransactionHolder(A.Fake<DbConnection>(), null));
        }

        protected override ValueTask<T> ExecuteInLock<T>(
            SchedulerLock? lockKind,
            Func<ConnectionAndTransactionHolder, ValueTask<T>> txCallback,
            CancellationToken cancellationToken = default)
        {
            return ExecuteInLocalTransactionLock(lockKind, txCallback, cancellationToken: cancellationToken);
        }
    }
}
