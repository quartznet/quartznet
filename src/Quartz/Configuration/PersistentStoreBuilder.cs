using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Quartz.Impl.AdoJobStore;
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
        return Configure(options => options.DataSource = DataSourceName);
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
