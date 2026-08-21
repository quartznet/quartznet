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
    /// Says which data source this store reads and writes through, by name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This <em>refers to</em> a data source rather than defining one:
    /// <see cref="UseDataSource(Action{DataSourceOptions})"/> defines it, and the two are told apart by
    /// that. The settings it names are the <see cref="DataSourceOptions"/> registered under this name —
    /// from a <c>Quartz:DataSource:&lt;name&gt;</c> configuration section, say, or from another
    /// scheduler that already configured it.
    /// </para>
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
    /// Defines this store's data source: which ADO.NET driver, and how to reach the database.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Prefer the database-specific methods such as <c>UseSqlServer</c>, which also select the matching
    /// driver delegate. Use this directly only for a provider Quartz does not know about.
    /// </para>
    /// <para>
    /// Where the connection itself comes from is <see cref="DataSourceOptions"/>' to say: a connection
    /// string, the name of one in <c>IConfiguration</c>, or
    /// <see cref="DataSourceOptions.UseRegisteredDataSource"/> for a <c>DbDataSource</c> the application
    /// registered in the container.
    /// </para>
    /// </remarks>
    IPersistentStoreBuilder UseDataSource(Action<DataSourceOptions> configure);

    /// <summary>
    /// Uses a specific driver delegate, which adapts Quartz's SQL to a particular database.
    /// </summary>
    IPersistentStoreBuilder UseDriverDelegate<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, IDriverDelegate;

    /// <summary>
    /// Takes part in a cluster with every other scheduler sharing this database.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Clustering requires database locking, which this enables as well — the two have never been
    /// separable in practice.
    /// </para>
    /// <para>
    /// Everything about clustering is said here, on <see cref="ClusteringOptions" />. The job store has
    /// no clustering settings of its own to disagree with these, and reports whether it is clustered
    /// rather than offering a second place to say so.
    /// </para>
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

    /// <summary>
    /// Adds a trigger persistence delegate, which stores and rebuilds a custom trigger type's
    /// scheduling data in its own tables rather than as a serialized blob.
    /// </summary>
    /// <remarks>
    /// The built-in delegates for the five shipped trigger types are always present; delegates added
    /// here serve additional trigger types. Call once per delegate — repeated registrations of the
    /// same type collapse to one.
    /// </remarks>
    IPersistentStoreBuilder UseTriggerPersistenceDelegate<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, ITriggerPersistenceDelegate;

    /// <summary>
    /// Adds a trigger persistence delegate the caller builds, for cases where it needs configuring
    /// first.
    /// </summary>
    /// <remarks>
    /// As with <see cref="UseSerializer(Func{IServiceProvider, IObjectSerializer})"/>, this registers
    /// under the scheduler's own key, which registering against <see cref="Services"/> would not.
    /// </remarks>
    IPersistentStoreBuilder UseTriggerPersistenceDelegate(Func<IServiceProvider, ITriggerPersistenceDelegate> factory);
}
