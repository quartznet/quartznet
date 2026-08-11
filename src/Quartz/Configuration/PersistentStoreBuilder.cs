using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using System.Data.Common;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Util;
using Quartz.Extensibility;

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

    public IPersistentStoreBuilder AcceptEnlistedTransactions()
    {
        return Configure(options => options.AcceptEnlistedTransactions = true);
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

            // The driver description comes from the container, so a provider Quartz ships no description
            // for is usable as soon as the application registers one.
            var metadata = provider.GetRequiredService<DbMetadataResolver>().Resolve(options.Provider);

            // Where the connection comes from is the data source's own setting, decided here rather than
            // by a second entry point that had to be called in the right order to take effect.
            if (options.UseRegisteredDataSource)
            {
                return new DataSourceDbProvider(metadata, provider.GetRequiredService<DbDataSource>());
            }

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
        // Configured rather than assigned from a copy, so UseClustering() with no arguments turns
        // clustering on without resetting intervals that came from configuration.
        Services.Configure<ClusteringOptions>(OptionsName, options =>
        {
            options.Enabled = true;
            configure?.Invoke(options);
        });

        // Clustering has never worked without database locking, so enabling one enables the other
        // rather than leaving a configuration nobody meant to write.
        return Configure(options => options.UseDbLocks = true);
    }

    public IPersistentStoreBuilder UseSerializer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, IObjectSerializer
    {
        // The converter set is built on first use, so register the serializer already
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

    public IPersistentStoreBuilder UseTriggerPersistenceDelegate<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, ITriggerPersistenceDelegate
    {
        // TryAddEnumerable dedupes by implementation type, so registering the same delegate twice
        // collapses to one — several are expected, so this is not the single-service Register path.
        if (schedulerKey is null)
        {
            Services.TryAddEnumerable(ServiceDescriptor.Singleton<ITriggerPersistenceDelegate, T>());
        }
        else
        {
            Services.TryAddEnumerable(ServiceDescriptor.KeyedSingleton<ITriggerPersistenceDelegate, T>(
                schedulerKey,
                static (provider, key) => ActivatorUtilities.CreateInstance<T>(SchedulerScopedServiceProvider.For(provider, key))));
        }

        return this;
    }

    public IPersistentStoreBuilder UseTriggerPersistenceDelegate(Func<IServiceProvider, ITriggerPersistenceDelegate> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (schedulerKey is null)
        {
            Services.AddSingleton(provider => factory(provider));
        }
        else
        {
            Services.AddKeyedSingleton<ITriggerPersistenceDelegate>(
                schedulerKey,
                (provider, key) => factory(SchedulerScopedServiceProvider.For(provider, key)));
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
