#nullable enable

using System.Data.Common;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl;
using Quartz.Extensibility;

namespace Quartz.Tests;

/// <summary>
/// Builds job stores for tests that construct one directly rather than through a container.
/// </summary>
/// <remarks>
/// Job stores take their collaborators through their constructors, so tests that new one up have to
/// supply them. This keeps that noise in one place, and lets a test pass only the collaborator it
/// actually cares about — usually a signaler it wants to observe.
/// </remarks>
public static class TestJobStores
{
    public static ILogger<T> Logger<T>() => NullLogger<T>.Instance;

    public static ILoggerFactory LoggerFactory() => NullLoggerFactory.Instance;

    /// <summary>
    /// A connection manager of its own for the store under test. There is no process-wide one, so a test
    /// that wants to read a provider back out has to hold on to the manager it passed in.
    /// </summary>
    public static IDbConnectionManager ConnectionManager() => new DbConnectionManager(Logger<DbConnectionManager>());

    /// <summary>
    /// A database provider that never connects, for tests that only exercise the store's logic.
    /// </summary>
    public static IDbProvider DbProvider() => new StubDbProvider();

    public static IDriverDelegate DriverDelegate() => new StdAdoDelegate();

    public static ISemaphore LockHandler() => new SimpleSemaphore();

    public static IObjectSerializer Serializer()
    {
        var serializer = new SystemTextJsonObjectSerializer();
        return serializer;
    }

    public static IOptions<AdoJobStoreOptions> StoreOptions(
        string dataSource = "test",
        string tablePrefix = AdoJobStoreOptions.DefaultTablePrefix,
        Action<AdoJobStoreOptions>? configure = null)
    {
        var options = new AdoJobStoreOptions { DataSource = dataSource, TablePrefix = tablePrefix };
        configure?.Invoke(options);
        return Options.Create(options);
    }

    public static IOptions<ClusteringOptions> ClusteringOptions(Action<ClusteringOptions>? configure = null)
    {
        var options = new ClusteringOptions();
        configure?.Invoke(options);
        return Options.Create(options);
    }

    public static IOptions<QuartzSchedulerOptions> SchedulerOptions(
        string instanceName = "TestScheduler",
        string instanceId = "TestInstance")
    {
        return Options.Create(new QuartzSchedulerOptions
        {
            InstanceName = instanceName,
            InstanceId = instanceId,
        });
    }

    public static ISchedulerSignaler Signaler() => new NoOpSchedulerSignaler();

    public static ITypeLoader TypeLoader() => new SimpleTypeLoader();

    public static RAMJobStore Ram(ISchedulerSignaler? signaler = null, TimeProvider? timeProvider = null)
    {
        return new RAMJobStore(
            LoggerFactory(),
            signaler ?? new NoOpSchedulerSignaler(),
            timeProvider ?? TimeProvider.System);
    }

    public static LocalTransactionJobStore Tx(
        ISchedulerSignaler? signaler = null,
        ITypeLoader? typeLoader = null,
        TimeProvider? timeProvider = null,
        string instanceName = "TestScheduler",
        string instanceId = "TestInstance")
    {
        return new LocalTransactionJobStore(
            signaler ?? new NoOpSchedulerSignaler(),
            typeLoader ?? new SimpleTypeLoader(),
            timeProvider ?? TimeProvider.System,
            SchedulerOptions(instanceName, instanceId),
            StoreOptions(),
            ClusteringOptions(),
            Serializer(),
            ConnectionManager(),
            DbProvider(),
            DriverDelegate(),
            LockHandler());
    }

    public static ExternalTransactionJobStore Cmt(
        ISchedulerSignaler? signaler = null,
        ITypeLoader? typeLoader = null,
        TimeProvider? timeProvider = null)
    {
        return new ExternalTransactionJobStore(
            signaler ?? new NoOpSchedulerSignaler(),
            typeLoader ?? new SimpleTypeLoader(),
            timeProvider ?? TimeProvider.System,
            SchedulerOptions(),
            StoreOptions(),
            ClusteringOptions(),
            Serializer(),
            ConnectionManager(),
            DbProvider(),
            DriverDelegate(),
            LockHandler());
    }
}

/// <summary>
/// A database provider that never opens a connection, so a job store can be constructed in a test
/// without a real driver assembly on hand.
/// </summary>
public sealed class StubDbProvider : IDbProvider
{
    public string ConnectionString => "";

    public DbMetadata Metadata { get; } = new();

    public DbCommand CreateCommand() => throw new NotSupportedException("StubDbProvider does not connect.");

    public DbConnection CreateConnection() => throw new NotSupportedException("StubDbProvider does not connect.");

    public void Shutdown()
    {
    }
}

/// <summary>
/// A signaler that does nothing, for tests that do not care about scheduling signals.
/// </summary>
public sealed class NoOpSchedulerSignaler : ISchedulerSignaler
{
    public ValueTask NotifyTriggerListenersMisfired(ITrigger trigger, CancellationToken cancellationToken = default) => default;

    public ValueTask NotifySchedulerListenersFinalized(ITrigger trigger, CancellationToken cancellationToken = default) => default;

    public ValueTask NotifySchedulerListenersJobDeleted(JobKey jobKey, CancellationToken cancellationToken = default) => default;

    public ValueTask SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc, CancellationToken cancellationToken = default) => default;

    public ValueTask NotifySchedulerListenersError(string message, SchedulerException jpe, CancellationToken cancellationToken = default) => default;
}
