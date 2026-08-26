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

        // A persistent store is being chosen, so these two will be read — which is what makes it safe to
        // have the host check them at startup. A scheduler on the in-memory store never resolves them,
        // and validating them there would make an unset DataSource a startup failure for a
        // configuration nobody wrote.
        services.ValidateOnStart<AdoJobStoreOptions>(schedulerKey);
        services.ValidateOnStart<ClusteringOptions>(schedulerKey);
    }

    public IServiceCollection Services { get; }

    public string SchedulerName => schedulerKey ?? "";

    private string OptionsName => schedulerKey ?? Microsoft.Extensions.Options.Options.DefaultName;

    public IPersistentStoreBuilder ConfigureStore(Action<AdoJobStoreOptions> configure)
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
        Services.ValidateOnStart<DataSourceOptions>(DataSourceName);
        ConfigureStore(options => options.DataSource = DataSourceName);

        var name = DataSourceName;
        RegisterProvider(provider =>
        {
            var options = provider.GetRequiredService<IOptionsMonitor<DataSourceOptions>>().Get(name);

            // The driver description comes from the container, so a provider Quartz ships no description
            // for is usable as soon as the application registers one.
            var metadata = provider.GetRequiredService<DbMetadataResolver>().Resolve(options.Provider);

            // Where the connection comes from is the data source's own setting, decided here rather than
            // by a second entry point that had to be called in the right order to take effect. Most
            // specific first: a data source the caller builds, then one registered under a key of its
            // own, then the container's single unkeyed one.
            if (options.DataSourceFactory is { } dataSourceFactory)
            {
                return new DataSourceDbProvider(metadata, dataSourceFactory(provider));
            }

            if (options.DataSourceServiceKey is { } dataSourceKey)
            {
                return new DataSourceDbProvider(metadata, provider.GetRequiredKeyedService<DbDataSource>(dataSourceKey));
            }

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

    public IPersistentStoreBuilder UseConnectionProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, IDbProvider
    {
        return ReplaceProvider(provider => ActivatorUtilities.CreateInstance<T>(provider));
    }

    public IPersistentStoreBuilder UseConnectionProvider(Func<IServiceProvider, IDbProvider> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return ReplaceProvider(factory);
    }

    /// <summary>
    /// Registers a connection provider that beats whichever one the database choice registered,
    /// whichever order the two were called in.
    /// </summary>
    /// <remarks>
    /// The rest of this builder defers to what is already registered, which works because each of its
    /// methods answers a different question. <c>UseSqlServer</c> and <c>UseConnectionProvider</c> answer
    /// the same one, so first-wins would make the result depend on call order — and the call that loses
    /// is silently the one that said something Quartz could not have worked out for itself. Removing
    /// first and adding is what makes it order-independent; the data-source path stays <c>TryAdd</c>, so
    /// it can never overwrite this.
    /// </remarks>
    private IPersistentStoreBuilder ReplaceProvider(Func<IServiceProvider, IDbProvider> factory)
    {
        RemoveProviderRegistrations();

        if (schedulerKey is null)
        {
            Services.AddSingleton(factory);
        }
        else
        {
            Services.AddKeyedSingleton<IDbProvider>(
                schedulerKey,
                (provider, key) => factory(SchedulerScopedServiceProvider.For(provider, key)));
        }

        // The provider carries everything needed to reach the database, but the store still refuses to
        // start without a data source name. Name it after the scheduler, exactly as UseDataSource would,
        // so UseConnectionProvider on its own is a complete configuration.
        return ConfigureStore(options => options.DataSource = DataSourceName);
    }

    /// <summary>
    /// Drops this scheduler's connection provider registrations, and only this scheduler's.
    /// </summary>
    /// <remarks>
    /// The default scheduler's provider is unkeyed and a named one's is keyed by its name, so the two
    /// tests are different — and a named scheduler must not remove the default scheduler's provider,
    /// nor another named scheduler's.
    /// </remarks>
    private void RemoveProviderRegistrations()
    {
        for (int i = Services.Count - 1; i >= 0; i--)
        {
            ServiceDescriptor descriptor = Services[i];
            if (descriptor.ServiceType != typeof(IDbProvider))
            {
                continue;
            }

            bool ours = schedulerKey is null
                ? !descriptor.IsKeyedService
                : descriptor.IsKeyedService && Equals(descriptor.ServiceKey, schedulerKey);

            if (ours)
            {
                Services.RemoveAt(i);
            }
        }
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

        // Asking for clustering and then switching it off inside the callback used to leave a store
        // with database locking on, no cluster manager, and no complaint. It is a contradiction, so it
        // is reported as one.
        Services.AddSingleton<IValidateOptions<ClusteringOptions>>(new ClusteringStaysEnabledValidator(OptionsName));

        // Clustering has never worked without database locking, so enabling one enables the other
        // rather than leaving a configuration nobody meant to write.
        return ConfigureStore(options => options.UseDbLocks = true);
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
