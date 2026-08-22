#nullable enable

using System.Data.Common;

using FakeItEasy;

using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;
using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// <c>SelectWithLockSql</c> only ever reaches a lock handler the job store builds for itself. Supplying a
/// handler and a row-lock statement together is a configuration that reads as if it tuned the locking and
/// does not, which is the kind of silence that has to be said out loud instead.
/// </summary>
/// <remarks>
/// Non-parallelizable because reading what the store logged means replacing the process-wide logger
/// factory for the duration.
/// </remarks>
[NonParallelizable]
public sealed class SelectWithLockSqlWarningTest
{
    private const string LockSql = "SELECT * FROM {0}LOCKS WITH (UPDLOCK) WHERE SCHED_NAME = @schedulerName AND LOCK_NAME = @lockName";

    [Test]
    public async Task ASuppliedLockHandlerWithARowLockStatementIsReported()
    {
        var entries = await InitializeAndCaptureLogs(new SimpleSemaphore(), LockSql);

        entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("row-lock statement is configured"))
            .Which.Message.Should().Contain(nameof(SimpleSemaphore),
                "the message has to name the handler that is ignoring the statement");
    }

    [Test]
    public async Task ASuppliedLockHandlerOnItsOwnIsNotReported()
    {
        var entries = await InitializeAndCaptureLogs(new SimpleSemaphore(), selectWithLockSql: null);

        entries.Should().NotContain(entry => entry.Message.Contains("row-lock statement is configured"),
            "a store with no statement configured has nothing to be silently dropped");
    }

    [Test]
    public async Task AStoreThatChoosesItsOwnLockHandlerIsNotReported()
    {
        var entries = await InitializeAndCaptureLogs(lockHandler: null, LockSql);

        entries.Should().NotContain(entry => entry.Message.Contains("row-lock statement is configured"),
            "a handler the store builds for itself is the one the statement reaches");
    }

    [Test]
    public async Task SQLiteIsReportedAsThePropertyOfTheDatabaseThatItIs()
    {
        var entries = await InitializeAndCaptureLogs(new SQLiteSemaphore(), LockSql);

        entries.Should().ContainSingle(entry => entry.Message.Contains("row-lock statement is configured"))
            .Which.Message.Should().Contain("SQLite serializes callers in process",
                "SQLite gets this handler from the store rather than from the caller, so telling them to "
                + "remove a lock handler they never chose would send them looking for one");
    }

    private static async Task<List<LogEntry>> InitializeAndCaptureLogs(ISemaphore? lockHandler, string? selectWithLockSql)
    {
        var recorder = new RecordingLoggerProvider();
        using var factory = new LoggerFactory();
        factory.AddProvider(recorder);

        LogProvider.SetLogProvider(factory);
        try
        {
            var store = new WarningStore(lockHandler, selectWithLockSql);
            await store.Initialize(TestJobStores.Identity());
        }
        finally
        {
            LogProvider.SetLogProvider(NullLoggerFactoryFor());
        }

        return recorder.Entries;
    }

    private static ILoggerFactory NullLoggerFactoryFor() => Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;

    /// <summary>
    /// A store that gets as far as choosing its lock handler and no further: schema validation is off, so
    /// nothing tries to reach a database.
    /// </summary>
    private sealed class WarningStore : AdoJobStoreBase
    {
        public WarningStore(ISemaphore? lockHandler, string? selectWithLockSql)
            : base(
                TestJobStores.Signaler(),
                TestJobStores.TypeLoader(),
                TimeProvider.System,
                TestJobStores.SchedulerOptions(),
                TestJobStores.StoreOptions(configure: options =>
                {
                    options.PerformSchemaValidation = false;
                    options.SelectWithLockSql = selectWithLockSql;
                }),
                TestJobStores.ClusteringOptions(),
                TestJobStores.Serializer(),
                TestJobStores.DbProvider(),
                TestJobStores.DriverDelegate(),
                lockHandler)
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
