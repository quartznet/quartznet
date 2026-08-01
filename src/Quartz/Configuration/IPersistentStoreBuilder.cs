using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl.AdoJobStore;
using Quartz.Extensibility;

namespace Quartz;

/// <summary>
/// Configures a database-backed job store.
/// </summary>
/// <remarks>
/// <para>
/// A scheduler has one job store and therefore one database, so there is no data source name to
/// invent: <c>UseSqlServer(connectionString)</c> says everything. Schedulers that need different
/// databases are registered under different names, and their components are keyed accordingly.
/// </para>
/// <para>
/// Serialization and clustering live here rather than on the scheduler because they are properties of
/// how the schedule is stored, and mean nothing for an in-memory store.
/// </para>
/// </remarks>
public interface IPersistentStoreBuilder
{
    /// <summary>
    /// The services this scheduler is built from.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// The name of the scheduler this store belongs to, or an empty string for the default scheduler.
    /// </summary>
    string SchedulerName { get; }

    /// <summary>
    /// Configures the job store.
    /// </summary>
    /// <remarks>
    /// These are the same settings, under the same names, as the <c>Quartz:JobStore</c> configuration
    /// section.
    /// </remarks>
    IPersistentStoreBuilder Configure(Action<AdoJobStoreOptions> configure);

    /// <summary>
    /// Lets the job store take part in a transaction the application owns, instead of always managing
    /// an ADO.NET transaction of its own, so that scheduling commits together with the rest of the
    /// application's work.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The job store then uses a connection enlisted with
    /// <see cref="SchedulerEnlistmentExtensions.EnlistTransaction" /> or
    /// <see cref="SchedulerEnlistmentExtensions.EnlistConnection" />. Handing over a connection is the
    /// only way to take part, and the one that works on every provider: with the default
    /// <see cref="Quartz.Impl.AdoJobStore.LocalTransactionJobStore" />, a connection the job store opens for itself
    /// stays out of any ambient <see cref="System.Transactions.TransactionScope" />, since a second
    /// connection in that transaction would require it to be promoted to a distributed one.
    /// <see cref="Quartz.Impl.AdoJobStore.ExternalTransactionJobStore" /> is the exception, since running inside a
    /// container-managed transaction is that store's contract.
    /// </para>
    /// <para>
    /// Locks are held until the application commits, so keep such transactions short. This also
    /// switches locking to database locks unless an explicit lock handler was configured.
    /// </para>
    /// </remarks>
    IPersistentStoreBuilder AcceptEnlistedTransactions();

    /// <summary>
    /// Names this store's data source, which is how its connection provider is registered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The name defaults to the scheduler's name, or <c>quartz</c> for the default scheduler. Connection
    /// providers are held per process, so two default schedulers in one process — two standalone
    /// <see cref="QuartzSchedulerBuilder"/>s, say — would otherwise share the one name and the second
    /// would replace the first's connection provider. Name them and they stay apart.
    /// </para>
    /// <para>
    /// Call this before choosing the database, since the name is fixed when the data source is
    /// configured.
    /// </para>
    /// </remarks>
    IPersistentStoreBuilder UseDataSourceName(string name);

    /// <summary>
    /// Configures the database connection.
    /// </summary>
    /// <remarks>
    /// Prefer the database-specific methods such as <c>UseSqlServer</c>, which also select the matching
    /// driver delegate. Use this directly only for a provider Quartz does not know about.
    /// </remarks>
    IPersistentStoreBuilder UseDataSource(Action<DataSourceOptions> configure);

    /// <summary>
    /// Connects through a <c>DbDataSource</c> registered in the container, rather than through a
    /// connection string of Quartz's own.
    /// </summary>
    IPersistentStoreBuilder UseDataSourceConnectionProvider();

    /// <summary>
    /// Uses a specific driver delegate, which adapts Quartz's SQL to a particular database.
    /// </summary>
    IPersistentStoreBuilder UseDriverDelegate<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, IDriverDelegate;

    /// <summary>
    /// Takes part in a cluster with every other scheduler sharing this database.
    /// </summary>
    /// <remarks>
    /// Clustering requires database locking, which this enables as well — the two have never been
    /// separable in practice.
    /// </remarks>
    IPersistentStoreBuilder UseClustering(Action<ClusteringOptions>? configure = null);

    /// <summary>
    /// Uses a specific serializer for job data held in the database.
    /// </summary>
    IPersistentStoreBuilder UseSerializer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, IObjectSerializer;

    /// <summary>
    /// Uses a serializer the caller builds, for cases where it needs configuring first.
    /// </summary>
    /// <remarks>
    /// The serializer belongs to this store rather than to the container, so use this rather than
    /// registering <see cref="IObjectSerializer"/> against <see cref="Services"/>: a named scheduler
    /// resolves its serializer under its own key and would never see an unkeyed registration.
    /// </remarks>
    IPersistentStoreBuilder UseSerializer(Func<IServiceProvider, IObjectSerializer> factory);

    /// <summary>
    /// Uses a specific lock handler, which decides how competing schedulers serialize their work.
    /// </summary>
    /// <remarks>
    /// Left unset, the store chooses for itself once it knows which database it is talking to: database
    /// row locks when clustered, and an in-process monitor otherwise.
    /// </remarks>
    IPersistentStoreBuilder UseLockHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, ISemaphore;

    /// <summary>
    /// Uses a lock handler the caller builds, for cases where it needs configuring first.
    /// </summary>
    /// <remarks>
    /// As with <see cref="UseSerializer(Func{IServiceProvider, IObjectSerializer})"/>, this registers
    /// under the scheduler's own key, which registering against <see cref="Services"/> would not.
    /// </remarks>
    IPersistentStoreBuilder UseLockHandler(Func<IServiceProvider, ISemaphore> factory);
}
