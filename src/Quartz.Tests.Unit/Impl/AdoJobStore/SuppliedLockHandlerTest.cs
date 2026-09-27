#nullable enable

using System.Data.Common;

using FakeItEasy;

using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;
using Quartz.Impl.AdoJobStore;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The lock handler the configuration names is the one the store locks with. The store builds a handler
/// of its own only when it was handed none, and on SQL Server the one it builds locks with
/// <c>UPDLOCK,ROWLOCK</c> — hints a memory-optimized <c>QRTZ_LOCKS</c> refuses — so a configured
/// <see cref="SqlServerMemoryOptimizedUpdateRowLockHandler" /> that lost to the store's own choice would
/// fail every lock a deployment on <c>tables_sqlServerMOT.sql</c> takes (#3903).
/// </summary>
/// <remarks>
/// Non-parallelizable because reading what the store logged means replacing the process-wide logger
/// factory for the duration.
/// </remarks>
[NonParallelizable]
public sealed class SuppliedLockHandlerTest
{
    private const int UsingDatabaseLocking = 3006;
    private const int SqlServerCouldUseRowLocking = 3012;
    private const int UsingConfiguredLockHandler = 3048;

    [Test]
    public async Task AClusteredSqlServerStoreKeepsTheMemoryOptimizedHandlerItWasHanded()
    {
        SqlServerMemoryOptimizedUpdateRowLockHandler lockHandler = new(TestJobStores.DbProvider());

        (ChoosingStore store, List<LogEntry> entries) = await InitializeAndCaptureLogs(lockHandler, clustered: true);

        store.LockHandler.Should().BeSameAs(lockHandler,
            "clustering forces database locks, which decides what the store builds when it was handed "
            + "nothing - not whether it replaces what it was handed");
        entries.Should().NotContain(entry => entry.EventId.Id == UsingDatabaseLocking,
            "that line names a handler the store built for itself, and it built none");
        entries.Should().NotContain(entry => entry.EventId.Id == SqlServerCouldUseRowLocking,
            "the advice to drop the handler for UPDLOCK,ROWLOCK is for an ordinary QRTZ_LOCKS; the "
            + "memory-optimized handler exists because that table refuses those hints");
    }

    [Test]
    public async Task TheHandlerTheStoreWasHandedIsNamedInTheLog()
    {
        (_, List<LogEntry> entries) = await InitializeAndCaptureLogs(
            new SqlServerMemoryOptimizedUpdateRowLockHandler(TestJobStores.DbProvider()), clustered: true);

        entries.Should().ContainSingle(entry => entry.EventId.Id == UsingConfiguredLockHandler)
            .Which.Message.Should().Contain(nameof(SqlServerMemoryOptimizedUpdateRowLockHandler),
                "the store used to log only the handler it chose itself, so a configured one that failed "
                + "its first lock could only be identified from the stack trace");
    }

    [Test]
    public async Task AnUpdateRowHandlerOnAnOrdinarySqlServerTableIsStillAdvisedAgainst()
    {
        (ChoosingStore store, List<LogEntry> entries) = await InitializeAndCaptureLogs(
            new UpdateRowLockHandler(TestJobStores.DbProvider()), clustered: true);

        store.LockHandler.Should().BeOfType<UpdateRowLockHandler>();
        entries.Should().ContainSingle(entry => entry.EventId.Id == SqlServerCouldUseRowLocking,
            "the plain update handler is the one that gives up UPDLOCK,ROWLOCK for nothing");
    }

    [Test]
    public async Task AClusteredSqlServerStoreHandedNoHandlerLocksWithRowLockHints()
    {
        (ChoosingStore store, List<LogEntry> entries) = await InitializeAndCaptureLogs(lockHandler: null, clustered: true);

        store.LockHandler.Should().BeOfType<SelectForUpdateLockHandler>(
            "this is the handler a memory-optimized QRTZ_LOCKS refuses, so it is what a configuration "
            + "that lost its lock handler ends up running");
        entries.Should().ContainSingle(entry => entry.EventId.Id == UsingDatabaseLocking)
            .Which.Message.Should().Contain(nameof(SelectForUpdateLockHandler));
        entries.Should().NotContain(entry => entry.EventId.Id == UsingConfiguredLockHandler,
            "nothing was configured, so the line saying so would be a lie");
    }

    [Test]
    public async Task SqliteAnnouncesItsSubstitutionRatherThanAConfiguredHandler()
    {
        (_, List<LogEntry> entries) = await InitializeAndCaptureLogs(new SqliteLockHandler(), clustered: false);

        entries.Should().NotContain(entry => entry.EventId.Id == UsingConfiguredLockHandler,
            "the store installs SqliteLockHandler itself and says so with its own line; calling it "
            + "configured would send the reader looking for configuration that does not exist");
    }

    /// <summary>
    /// Builds and initializes the store with the recorder installed as the ambient logger factory. The
    /// store takes its logger in its constructor, so a store built before the swap logs into the void.
    /// </summary>
    private static async Task<(ChoosingStore Store, List<LogEntry> Entries)> InitializeAndCaptureLogs(ILockHandler? lockHandler, bool clustered)
    {
        RecordingLoggerProvider recorder = new();
        using LoggerFactory factory = new();
        factory.AddProvider(recorder);

        LogProvider.SetLogProvider(factory);
        try
        {
            ChoosingStore store = new(lockHandler, clustered);
            await store.Initialize(TestJobStores.Identity());
            return (store, recorder.Entries);
        }
        finally
        {
            LogProvider.SetLogProvider(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        }
    }

    /// <summary>
    /// A SQL Server store that gets as far as settling its lock handler and no further: schema validation
    /// is off, so nothing tries to reach a database.
    /// </summary>
    private sealed class ChoosingStore : AdoJobStoreBase
    {
        public ChoosingStore(ILockHandler? lockHandler, bool clustered)
            : base(TestJobStores.Dependencies(
                storeOptions: TestJobStores.StoreOptions(configure: options => options.SchemaProvisioning = SchemaProvisioning.None),
                clusteringOptions: TestJobStores.ClusteringOptions(options => options.Enabled = clustered),
                driverDelegate: new SqlServerDelegate())
                // Through `with`, because the fixture's null means "none configured" — the case where
                // Initialize picks a handler itself — rather than "give me the default double".
                with
                { LockHandler = lockHandler })
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
