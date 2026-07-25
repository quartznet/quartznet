using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl.AdoJobStore;
using Quartz.Spi;

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
    /// Uses a specific lock handler, which decides how competing schedulers serialize their work.
    /// </summary>
    IPersistentStoreBuilder UseLockHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, ISemaphore;
}
