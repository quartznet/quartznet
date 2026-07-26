using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using System.Data.Common;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Util;
using Quartz.Spi;

namespace Quartz.Configuration;

/// <inheritdoc />
internal sealed class PersistentStoreBuilder : IPersistentStoreBuilder
{
    private readonly string? schedulerKey;

    public PersistentStoreBuilder(IServiceCollection services, string? schedulerKey)
    {
        Services = services;
        this.schedulerKey = schedulerKey;
    }

    public IServiceCollection Services { get; }

    public string SchedulerName => schedulerKey ?? "";

    private string OptionsName => schedulerKey ?? Microsoft.Extensions.Options.Options.DefaultName;

    public IPersistentStoreBuilder Configure(Action<AdoJobStoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Services.Configure(OptionsName, configure);
        return this;
    }

    public IPersistentStoreBuilder UseDataSource(Action<DataSourceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        // The data source is named after the scheduler that owns it, so the name never has to be
        // invented by the caller or kept in step by hand.
        dataSourceConfigured = true;
        Services.Configure(DataSourceName, configure);
        Configure(options => options.DataSource = DataSourceName);

        var name = DataSourceName;
        RegisterProvider(provider =>
        {
            var options = provider.GetRequiredService<IOptionsMonitor<DataSourceOptions>>().Get(name);
            var connectionString = options.ConnectionString;

            if (string.IsNullOrWhiteSpace(connectionString) && !string.IsNullOrWhiteSpace(options.ConnectionStringName))
            {
                connectionString = provider.GetService<IConfiguration>()?.GetConnectionString(options.ConnectionStringName);
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    Throw.SchedulerConfigException(
                        $"Named connection string '{options.ConnectionStringName}' was not found.");
                }
            }

            // The driver description comes from the container, so a provider Quartz ships no description
            // for is usable as soon as the application registers one.
            var metadata = provider.GetRequiredService<DbMetadataResolver>().Resolve(options.Provider);
            return new DbProvider(metadata, connectionString!);
        });

        return this;
    }

    /// <summary>
    /// The name this scheduler's data source is registered under.
    /// </summary>
    internal string DataSourceName => dataSourceName ?? schedulerKey ?? DefaultDataSourceName;

    internal const string DefaultDataSourceName = "quartz";

    private string? dataSourceName;
    private bool dataSourceConfigured;

    public IPersistentStoreBuilder UseDataSourceName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (dataSourceConfigured)
        {
            Throw.SchedulerConfigException(
                "The data source has already been configured. Name it before choosing the database, "
                + "because the name is what the connection provider is registered under.");
        }

        dataSourceName = name;
        return this;
    }

    public IPersistentStoreBuilder UseDriverDelegate<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, IDriverDelegate
    {
        Register<IDriverDelegate, T>();
        return this;
    }

    public IPersistentStoreBuilder UseClustering(Action<ClusteringOptions>? configure = null)
    {
        var clustering = new ClusteringOptions();
        configure?.Invoke(clustering);

        return Configure(options =>
        {
            options.Clustered = true;
            // Clustering has never worked without database locking, so enabling one enables the other
            // rather than failing validation later for a configuration nobody meant to write.
            options.UseDbLocks = true;

            // Only what the caller actually asked for. Writing these unconditionally would mean
            // UseClustering() with no arguments silently reset intervals that came from configuration.
            if (clustering.CheckinInterval is { } checkinInterval)
            {
                options.ClusterCheckinInterval = checkinInterval;
            }

            if (clustering.CheckinMisfireThreshold is { } checkinMisfireThreshold)
            {
                options.ClusterCheckinMisfireThreshold = checkinMisfireThreshold;
            }
        });
    }

    public IPersistentStoreBuilder UseSerializer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, IObjectSerializer
    {
        // A serializer is unusable until Initialize builds its converter set, so register it already
        // initialized rather than relying on somebody remembering to call it.
        return UseSerializer(provider =>
        {
            var serializer = ActivatorUtilities.CreateInstance<T>(provider);
            return serializer;
        });
    }

    public IPersistentStoreBuilder UseSerializer(Func<IServiceProvider, IObjectSerializer> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        RegisterScoped(factory);
        return this;
    }

    public IPersistentStoreBuilder UseLockHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, ISemaphore
    {
        Register<ISemaphore, T>();
        return this;
    }

    public IPersistentStoreBuilder UseLockHandler(Func<IServiceProvider, ISemaphore> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        RegisterScoped(factory);
        return this;
    }

    /// <summary>
    /// Connects through a <c>DbDataSource</c> registered in the container, rather than a
    /// connection string of Quartz's own.
    /// </summary>
    public IPersistentStoreBuilder UseDataSourceConnectionProvider()
    {
        Services.Configure<DataSourceOptions>(DataSourceName, options => options.UseRegisteredDataSource = true);

        // Asking for the container's data source explicitly overrides whatever connection provider the
        // database method implied, whichever order they were called in.
        var name = DataSourceName;
        IDbProvider Create(IServiceProvider provider)
        {
            var options = provider.GetRequiredService<IOptionsMonitor<DataSourceOptions>>().Get(name);
            var metadata = provider.GetRequiredService<DbMetadataResolver>().Resolve(options.Provider);
            return new DataSourceDbProvider(metadata, provider.GetRequiredService<DbDataSource>());
        }

        if (schedulerKey is null)
        {
            Services.Replace(ServiceDescriptor.Singleton<IDbProvider>(Create));
        }
        else
        {
            Services.Replace(ServiceDescriptor.KeyedSingleton<IDbProvider>(schedulerKey, (provider, _) => Create(provider)));
        }

        return this;
    }

    private void RegisterProvider(Func<IServiceProvider, IDbProvider> factory)
    {
        if (schedulerKey is null)
        {
            Services.TryAddSingleton(factory);
        }
        else
        {
            Services.TryAddKeyedSingleton<IDbProvider>(schedulerKey, (provider, _) => factory(provider));
        }
    }

    /// <summary>
    /// Registers a per-scheduler service, keyed for a named scheduler and unkeyed for the default one.
    /// </summary>
    /// <remarks>
    /// Construction goes through <see cref="SchedulerScopedServiceProvider"/>, so a component is handed
    /// its own scheduler's collaborators. Registering the implementation type directly would let the
    /// container activate it with unkeyed dependencies, which for a named scheduler means the default
    /// scheduler's parts or a resolution failure — a lock handler that takes an <see cref="IDbProvider"/>
    /// is the case that shows it.
    /// </remarks>
    private void Register<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        RegisterScoped<TService>(provider => ActivatorUtilities.CreateInstance<TImplementation>(provider));
    }

    /// <summary>
    /// Registers a per-scheduler service built by a factory, which is given a provider resolving this
    /// scheduler's parts.
    /// </summary>
    private void RegisterScoped<TService>(Func<IServiceProvider, TService> factory) where TService : class
    {
        if (schedulerKey is null)
        {
            Services.TryAddSingleton(factory);
        }
        else
        {
            Services.TryAddKeyedSingleton<TService>(
                schedulerKey,
                (provider, key) => factory(SchedulerScopedServiceProvider.For(provider, key)));
        }
    }
}
