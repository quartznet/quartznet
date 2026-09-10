#nullable enable

using System.Data.Common;

using FakeItEasy;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Quartz.Impl.AdoJobStore;
using Quartz.Tests;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// A lock statement that is blocked by another session returns nothing and throws nothing, so every
/// path the store has for reporting trouble — the handler's retry loop, the store's transient-failure
/// classification, the scheduler thread's error listener — waits with it, in silence. A clustered
/// scheduler behind a peer that stopped without releasing <c>QRTZ_LOCKS</c>, or behind a database
/// session whose client is gone, therefore stops firing anything at all with nothing in the log to say
/// why. That was #3764; this is the thing that speaks up.
/// </summary>
/// <remarks>
/// Nothing here waits on wall time. The warning is a timer on the store's <see cref="TimeProvider" />,
/// and it is armed before the lock statement is issued, so a test can create the acquisition task and
/// advance the clock straight afterwards and know the timer was already registered.
/// </remarks>
public class DbLockHandlerSlowWaitWarningTest
{
    /// <summary>
    /// Spelled out rather than read from the product: an event id is what an operator's alert filters
    /// on, so a renumbering has to fail a test rather than quietly re-point the alert.
    /// </summary>
    private const int LockWaitExceededThresholdEvent = 3716;

    private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(30);

    [Test]
    public async Task AWaitThatOutlivesTheThresholdIsWarnedAboutOnce()
    {
        FakeTimeProvider clock = new();
        RecordingLoggerProvider log = new();
        using LoggerFactory factory = new();
        factory.AddProvider(log);

        using BlockingLockHandler lockHandler = new();
        lockHandler.Initialize(Context(clock, factory, Threshold));

        Task<bool> obtain = lockHandler.AcquireLock(Guid.NewGuid(), conn: null, SchedulerLock.TriggerAccess).AsTask();
        obtain.IsCompleted.Should().BeFalse("the lock statement is blocked, which is the case this warning exists for");

        clock.Advance(Threshold);

        LogEntry warning = log.Entries.Should().ContainSingle(entry => entry.EventId.Id == LockWaitExceededThresholdEvent).Which;
        warning.Level.Should().Be(LogLevel.Warning,
            "a scheduler that has stopped scheduling is not an informational event");
        warning.Message.Should().Contain("TRIGGER_ACCESS",
            "which lock is stuck is the first thing an operator has to know: TRIGGER_ACCESS is all scheduling, "
            + "STATE_ACCESS only the cluster check-in");

        clock.Advance(TimeSpan.FromMinutes(1));

        log.Entries.Count(entry => entry.EventId.Id == LockWaitExceededThresholdEvent).Should().Be(1,
            "one warning per acquisition; a wait that goes on for hours must not fill the log with the same line");

        lockHandler.Release();
        (await obtain).Should().BeTrue("the lock was eventually given, and the warning did not change that");
    }

    [Test]
    public async Task AWaitThatCompletesBeforeTheThresholdIsNotWarnedAbout()
    {
        FakeTimeProvider clock = new();
        RecordingLoggerProvider log = new();
        using LoggerFactory factory = new();
        factory.AddProvider(log);

        using BlockingLockHandler lockHandler = new();
        lockHandler.Initialize(Context(clock, factory, Threshold));

        Task<bool> obtain = lockHandler.AcquireLock(Guid.NewGuid(), conn: null, SchedulerLock.TriggerAccess).AsTask();

        clock.Advance(TimeSpan.FromSeconds(29));
        lockHandler.Release();
        (await obtain).Should().BeTrue();

        clock.Advance(TimeSpan.FromMinutes(5));

        log.Entries.Should().NotContain(entry => entry.EventId.Id == LockWaitExceededThresholdEvent,
            "the lock arrived inside the threshold, so the timer had to be disposed rather than left to "
            + "report a wait that is over");
    }

    [Test]
    public async Task AWaitThatFailsBeforeTheThresholdDisposesItsTimer()
    {
        FakeTimeProvider clock = new();
        RecordingLoggerProvider log = new();
        using LoggerFactory factory = new();
        factory.AddProvider(log);

        using BlockingLockHandler lockHandler = new();
        lockHandler.Initialize(Context(clock, factory, Threshold));

        Task<bool> obtain = lockHandler.AcquireLock(Guid.NewGuid(), conn: null, SchedulerLock.TriggerAccess).AsTask();

        lockHandler.Fail(new InvalidOperationException("ORA-30006: resource busy; acquire with WAIT timeout expired"));

        Func<Task> act = async () => await obtain;
        await act.Should().ThrowAsync<InvalidOperationException>();

        clock.Advance(TimeSpan.FromMinutes(5));

        log.Entries.Should().NotContain(entry => entry.EventId.Id == LockWaitExceededThresholdEvent,
            "a statement that failed has already been reported by whatever the store does with the failure");
    }

    [Test]
    public async Task ACancelledWaitDisposesItsTimer()
    {
        FakeTimeProvider clock = new();
        RecordingLoggerProvider log = new();
        using LoggerFactory factory = new();
        factory.AddProvider(log);

        using BlockingLockHandler lockHandler = new();
        lockHandler.Initialize(Context(clock, factory, Threshold));

        using CancellationTokenSource cancellation = new();
        Task<bool> obtain = lockHandler.AcquireLock(Guid.NewGuid(), conn: null, SchedulerLock.TriggerAccess, cancellation.Token).AsTask();

        await cancellation.CancelAsync();

        Func<Task> act = async () => await obtain;
        await act.Should().ThrowAsync<OperationCanceledException>();

        clock.Advance(TimeSpan.FromMinutes(5));

        log.Entries.Should().NotContain(entry => entry.EventId.Id == LockWaitExceededThresholdEvent,
            "a caller that gave up is not waiting on anything, so there is nothing left to warn about");
    }

    [Test]
    public async Task NoThresholdMeansNoWarning()
    {
        FakeTimeProvider clock = new();
        RecordingLoggerProvider log = new();
        using LoggerFactory factory = new();
        factory.AddProvider(log);

        using BlockingLockHandler lockHandler = new();
        lockHandler.Initialize(Context(clock, factory, threshold: null));

        Task<bool> obtain = lockHandler.AcquireLock(Guid.NewGuid(), conn: null, SchedulerLock.TriggerAccess).AsTask();

        clock.Advance(TimeSpan.FromHours(1));

        log.Entries.Should().NotContain(entry => entry.EventId.Id == LockWaitExceededThresholdEvent,
            "null is how an application says it does not want this reported, and no timer is armed at all");

        lockHandler.Release();
        (await obtain).Should().BeTrue();
    }

    [Test]
    public async Task AStateAccessWaitNamesThatLock()
    {
        FakeTimeProvider clock = new();
        RecordingLoggerProvider log = new();
        using LoggerFactory factory = new();
        factory.AddProvider(log);

        using BlockingLockHandler lockHandler = new();
        lockHandler.Initialize(Context(clock, factory, Threshold));

        Task<bool> obtain = lockHandler.AcquireLock(Guid.NewGuid(), conn: null, SchedulerLock.StateAccess).AsTask();

        clock.Advance(Threshold);

        log.Entries.Should().ContainSingle(entry => entry.EventId.Id == LockWaitExceededThresholdEvent)
            .Which.Message.Should().Contain("STATE_ACCESS",
                "a check-in blocked on the state lock is a different incident from a scheduler blocked on "
                + "the trigger lock, and the message is what tells them apart");

        lockHandler.Release();
        (await obtain).Should().BeTrue();
    }

    /// <summary>
    /// A threshold no timer will wait out is refused where it arrives, rather than at the first
    /// contended lock — which is the one moment nobody wants a second failure, and the moment at which
    /// the report would name a parameter called <c>dueTime</c> instead of the setting.
    /// </summary>
    [TestCase(60)]
    [TestCase(-1)]
    public void AThresholdNoTimerWillWaitOutIsRefusedAtInitialize(int days)
    {
        using BlockingLockHandler lockHandler = new();

        Action act = () => lockHandler.Initialize(Context(new FakeTimeProvider(), NullLoggerFactory.Instance, TimeSpan.FromDays(days)));

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*LockWaitWarningThreshold*49.7 days*");
    }

    /// <summary>
    /// The setting is on the store's options and the wait happens in the handler, so the one carries it
    /// to the other. A handler the container supplied gets it too, which is the whole reason it travels
    /// on <see cref="LockHandlerContext" /> rather than being read where it is configured.
    /// </summary>
    [Test]
    public async Task TheStoreHandsTheThresholdToItsLockHandler()
    {
        ILockHandler lockHandler = A.Fake<ILockHandler>();
        LockHandlerContext? captured = null;
        A.CallTo(() => lockHandler.Initialize(A<LockHandlerContext>._))
            .Invokes((LockHandlerContext context) => captured = context);

        ThresholdStore store = new(lockHandler, TimeSpan.FromSeconds(5));
        await store.Initialize(TestJobStores.Identity());

        captured.Should().NotBeNull("the store initializes its lock handler before anything asks for a lock");
        captured!.LockWaitWarningThreshold.Should().Be(TimeSpan.FromSeconds(5),
            "AdoJobStoreOptions.LockWaitWarningThreshold is where it is configured, and the handler is where "
            + "the wait actually happens");
    }

    private static LockHandlerContext Context(TimeProvider timeProvider, ILoggerFactory loggerFactory, TimeSpan? threshold) => new()
    {
        SchedulerName = "TESTSCHED",
        InstanceId = "node-1",
        TablePrefix = "QRTZ_",
        TimeProvider = timeProvider,
        LockWaitWarningThreshold = threshold,
        LoggerFactory = loggerFactory,
    };

    /// <summary>
    /// A lock handler whose statement blocks until the test says otherwise, which is what a row lock
    /// held by somebody else looks like from here.
    /// </summary>
    private sealed class BlockingLockHandler : DbLockHandler, IDisposable
    {
        private const string LockStatement =
            "SELECT * FROM {0}LOCKS WHERE SCHED_NAME = @schedulerName AND LOCK_NAME = @lockName FOR UPDATE";

        private const string InsertStatement =
            "INSERT INTO {0}LOCKS (SCHED_NAME, LOCK_NAME) VALUES (@schedulerName, @lockName)";

        private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingLockHandler()
            : base("QRTZ_", schedulerName: null, LockStatement, InsertStatement, new StubDbProvider())
        {
        }

        public void Release() => gate.TrySetResult();

        public void Fail(Exception exception) => gate.TrySetException(exception);

        /// <summary>
        /// Lets go of anything still waiting, so a test that failed before releasing the gate does not
        /// leave a task parked for the rest of the run.
        /// </summary>
        public void Dispose() => gate.TrySetResult();

        protected override async ValueTask ExecuteSql(
            Guid requestorId,
            ConnectionAndTransactionHolder conn,
            string lockName,
            string expandedSql,
            string expandedInsertSql,
            CancellationToken cancellationToken = default)
        {
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A store that gets as far as initializing its lock handler and no further: schema provisioning is
    /// off, so nothing reaches for a database.
    /// </summary>
    private sealed class ThresholdStore : AdoJobStoreBase
    {
        public ThresholdStore(ILockHandler lockHandler, TimeSpan threshold)
            : base(TestJobStores.Dependencies(
                storeOptions: TestJobStores.StoreOptions(configure: options =>
                {
                    options.SchemaProvisioning = SchemaProvisioning.None;
                    options.LockWaitWarningThreshold = threshold;
                }),
                lockHandler: lockHandler))
        {
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
            return new ValueTask<T>(default(T)!);
        }
    }
}
