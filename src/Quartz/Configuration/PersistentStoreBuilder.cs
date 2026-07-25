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
        Services.Configure(DataSourceName, configure);
        Configure(options => options.DataSource = DataSourceName);

        var dataSourceName = DataSourceName;
        RegisterProvider(provider =>
        {
            var options = provider.GetRequiredService<IOptionsMonitor<DataSourceOptions>>().Get(dataSourceName);
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

            return new DbProvider(options.Provider, connectionString!);
        });

        return this;
    }

    /// <summary>
    /// The name this scheduler's data source is registered under.
    /// </summary>
    internal string DataSourceName => schedulerKey ?? "quartz";

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
            options.ClusterCheckinInterval = clustering.CheckinInterval;
            options.ClusterCheckinMisfireThreshold = clustering.CheckinMisfireThreshold;
        });
    }

    public IPersistentStoreBuilder UseSerializer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, IObjectSerializer
    {
        Services.TryAddSingleton<IObjectSerializer, T>();
        return this;
    }

    public IPersistentStoreBuilder UseLockHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, ISemaphore
    {
        Register<ISemaphore, T>();
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
        var dataSourceName = DataSourceName;
        IDbProvider Create(IServiceProvider provider)
        {
            var options = provider.GetRequiredService<IOptionsMonitor<DataSourceOptions>>().Get(dataSourceName);
            return new DataSourceDbProvider(options.Provider, provider.GetRequiredService<DbDataSource>());
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

    private void Register<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        if (schedulerKey is null)
        {
            Services.TryAddSingleton<TService, TImplementation>();
        }
        else
        {
            Services.TryAddKeyedSingleton<TService, TImplementation>(schedulerKey);
        }
    }
}
