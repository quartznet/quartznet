using System.Collections.Specialized;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Microsoft.Extensions.Configuration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Configuration;

/// <summary>
/// Translates the flat <c>quartz.*</c> property keys into typed options and service registrations.
/// </summary>
/// <remarks>
/// <para>
/// This is the only place that understands the legacy string keys. Everything downstream sees typed
/// options and ordinary registrations, which is what lets the rest of the codebase stop caring that the
/// string format exists at all.
/// </para>
/// <para>
/// Two kinds of key are handled differently. A <c>&lt;prefix&gt;.type</c> key selects an implementation
/// and therefore has to become a <em>registration</em>, which can only happen while the service
/// collection is still open. Every other key is configuration and becomes an options value.
/// </para>
/// <para>
/// Note the unit mismatch this has to absorb: legacy time values are bare integer milliseconds
/// (<c>idleWaitTime = 30000</c>), whereas typed options are <see cref="TimeSpan"/> and bind from the
/// usual <c>00:00:30</c> form. Conversion happens here so both spellings land on the same option.
/// </para>
/// </remarks>
internal static class QuartzPropertyBridge
{
    private static readonly SimpleTypeLoadHelper typeLoadHelper = new();

    /// <summary>
    /// Applies a flat property collection to the service collection as typed options and registrations,
    /// then registers the scheduler's remaining parts with their defaults.
    /// </summary>
    /// <remarks>
    /// Ordering matters. This registers only what configuration explicitly asked for; the built-in
    /// defaults are added afterwards, by <c>AddQuartzScheduler</c>, so that anything chosen explicitly —
    /// here or in the caller's configuration callback — beats them. Anything the application registered
    /// before calling in wins over both: code beats strings.
    /// </remarks>
    /// <param name="services">The service collection being configured.</param>
    /// <param name="properties">The flat <c>quartz.*</c> properties.</param>
    /// <param name="schedulerName">
    /// The scheduler these properties belong to, or <see langword="null"/> for the default scheduler.
    /// </param>
    public static void Apply(IServiceCollection services, NameValueCollection properties, string? schedulerName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(properties);

        var parser = new PropertyReader(properties);
        var name = schedulerName ?? Microsoft.Extensions.Options.Options.DefaultName;

        ApplySchedulerOptions(services, parser, name, schedulerName);
        ApplyThreadPoolOptions(services, parser, name, schedulerName);
        ApplyJobStoreOptions(services, parser, name, schedulerName);
        ApplyDataSourceOptions(services, parser);
        ApplySerializer(services, parser);
    }

    /// <summary>
    /// Maps the properties again once the container is built, from the final <see cref="QuartzOptions"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Configuration that is deferred until an <see cref="IServiceProvider"/> exists cannot have
    /// contributed anything by the time <see cref="Apply"/> runs, so a scheduler name or thread pool
    /// size set from a deferred callback would otherwise be silently dropped. Reading the property bag
    /// again at options-resolution time picks those up.
    /// </para>
    /// <para>
    /// Only configuration can be recovered this way, not registrations: choosing an implementation type
    /// requires an open service collection, and by this point the container is built. Deferred callbacks
    /// that select implementations register them directly instead.
    /// </para>
    /// </remarks>
    public static void ApplyFromQuartzOptions(IServiceCollection services, string? schedulerName = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var name = schedulerName ?? Microsoft.Extensions.Options.Options.DefaultName;

        Reconfigure<QuartzSchedulerOptions>(services, name, (options, parser) => MapScheduler(options, parser, schedulerName));
        Reconfigure<ThreadPoolOptions>(services, name, MapThreadPool);
        Reconfigure<InMemoryJobStoreOptions>(services, name, MapInMemoryJobStore);
        Reconfigure<AdoJobStoreOptions>(services, name, MapAdoJobStore);
    }

    private static void Reconfigure<TOptions>(
        IServiceCollection services,
        string name,
        Action<TOptions, PropertyReader> map) where TOptions : class
    {
        services.AddSingleton<IConfigureOptions<TOptions>>(provider =>
            new ConfigureFromProperties<TOptions>(provider, name, map));
    }

    /// <summary>
    /// Applies the flat properties held in <see cref="QuartzOptions"/> onto a typed options instance,
    /// at the point the typed options are resolved.
    /// </summary>
    private sealed class ConfigureFromProperties<TOptions> : IConfigureNamedOptions<TOptions> where TOptions : class
    {
        private readonly IServiceProvider provider;
        private readonly string name;
        private readonly Action<TOptions, PropertyReader> map;

        public ConfigureFromProperties(IServiceProvider provider, string name, Action<TOptions, PropertyReader> map)
        {
            this.provider = provider;
            this.name = name;
            this.map = map;
        }

        public void Configure(TOptions options) => Configure(Microsoft.Extensions.Options.Options.DefaultName, options);

        public void Configure(string? optionsName, TOptions options)
        {
            if (!string.Equals(optionsName ?? Microsoft.Extensions.Options.Options.DefaultName, name, StringComparison.Ordinal))
            {
                return;
            }

            var properties = provider.GetRequiredService<IOptionsMonitor<QuartzOptions>>().Get(name).ToNameValueCollection();
            map(options, new PropertyReader(properties));
        }
    }

    private static void ApplySchedulerOptions(
        IServiceCollection services,
        PropertyReader parser,
        string name,
        string? schedulerName)
    {
        services.Configure<QuartzSchedulerOptions>(name, options => MapScheduler(options, parser, schedulerName));

        Register<IInstanceIdGenerator>(
            services,
            schedulerName,
            parser.Type(StdSchedulerFactory.PropertySchedulerInstanceIdGeneratorType)
            ?? SystemPropertyGeneratorIfRequested(parser));

        Register<ITypeLoadHelper>(services, schedulerName: null, parser.Type(StdSchedulerFactory.PropertySchedulerTypeLoadHelperType));
        Register<IJobFactory>(services, schedulerName, parser.Type(StdSchedulerFactory.PropertySchedulerJobFactoryType));
        Register<TimeProvider>(services, schedulerName: null, parser.Type(StdSchedulerFactory.PropertyTimeProviderType));
    }

    private static void MapScheduler(QuartzSchedulerOptions options, PropertyReader parser, string? schedulerName)
    {
        // A named scheduler's name is fixed by its registration and must not drift.
        if (schedulerName is not null)
        {
            options.InstanceName = schedulerName;
        }
        else
        {
            parser.String(StdSchedulerFactory.PropertySchedulerInstanceName, value => options.InstanceName = value);
        }

        parser.String(StdSchedulerFactory.PropertySchedulerInstanceId, value =>
        {
            switch (value)
            {
                case StdSchedulerFactory.AutoGenerateInstanceId:
                case StdSchedulerFactory.SystemPropertyAsInstanceId:
                    options.GenerateInstanceId = true;
                    break;
                default:
                    options.InstanceId = value;
                    break;
            }
        });

        parser.String(StdSchedulerFactory.PropertySchedulerThreadName, value => options.ThreadName = value);
        parser.Milliseconds(StdSchedulerFactory.PropertySchedulerIdleWaitTime, value => options.IdleWaitTime = value);
        parser.Int(StdSchedulerFactory.PropertySchedulerMaxBatchSize, value => options.MaxBatchSize = value);
        parser.Milliseconds(StdSchedulerFactory.PropertySchedulerBatchTimeWindow, value => options.BatchTriggerAcquisitionFireAheadTimeWindow = value);
        parser.Bool(StdSchedulerFactory.PropertySchedulerMakeSchedulerThreadDaemon, value => options.MakeSchedulerThreadDaemon = value);
        parser.Bool(StdSchedulerFactory.PropertySchedulerInterruptJobsOnShutdown, value => options.InterruptJobsOnShutdown = value);
        parser.Bool(StdSchedulerFactory.PropertySchedulerInterruptJobsOnShutdownWithWait, value => options.InterruptJobsOnShutdownWithWait = value);

        var context = parser.Group(StdSchedulerFactory.PropertySchedulerContextPrefix);
        foreach (var key in context.AllKeys)
        {
            if (key is not null && context[key] is { } value)
            {
                options.Context[key] = value;
            }
        }
    }

    /// <summary>
    /// <c>SYS_PROP</c> selects a generator rather than naming one, so it has to be mapped explicitly.
    /// </summary>
    private static Type? SystemPropertyGeneratorIfRequested(PropertyReader parser)
    {
        var instanceId = parser.String(StdSchedulerFactory.PropertySchedulerInstanceId);
        return instanceId == StdSchedulerFactory.SystemPropertyAsInstanceId
            ? typeof(SystemPropertyInstanceIdGenerator)
            : null;
    }

    private static void ApplyThreadPoolOptions(
        IServiceCollection services,
        PropertyReader parser,
        string name,
        string? schedulerName)
    {
        services.Configure<ThreadPoolOptions>(name, options => MapThreadPool(options, parser));

        var threadPoolType = parser.Type(StdSchedulerFactory.PropertyThreadPoolType);
        if (threadPoolType is null)
        {
            return;
        }

        // Registering the type alone would win the TryAdd race against the default registration and so
        // skip the configuration it applies, leaving a thread pool silently built with defaults. A
        // configured component still has to be configured.
        RegisterConfigured<IThreadPool>(services, schedulerName, (provider, key) =>
        {
            var threadPool = (IThreadPool) ActivatorUtilities.CreateInstance(SchedulerScopedServiceProvider.For(provider, key), threadPoolType);
            if (threadPool is TaskSchedulingThreadPool schedulingThreadPool)
            {
                schedulingThreadPool.MaxConcurrency = provider.GetSchedulerOptions<ThreadPoolOptions>(key).MaxConcurrency;
            }
            else
            {
                // A third-party pool has no typed options, so its knobs still arrive as strings.
                ApplyStringProperties(threadPool, provider, key, StdSchedulerFactory.PropertyThreadPoolPrefix);
            }

            return threadPool;
        });
    }

    private static void MapThreadPool(ThreadPoolOptions options, PropertyReader parser)
    {
        // threadCount is the older spelling of the same knob and is still in use in the wild.
        parser.Int("quartz.threadPool.threadCount", value => options.MaxConcurrency = value);
        parser.Int("quartz.threadPool.maxConcurrency", value => options.MaxConcurrency = value);
    }

    private static void ApplyJobStoreOptions(
        IServiceCollection services,
        PropertyReader parser,
        string name,
        string? schedulerName)
    {
        var jobStoreType = parser.Type(StdSchedulerFactory.PropertyJobStoreType);
        var persistent = jobStoreType is not null && typeof(JobStoreSupport).IsAssignableFrom(jobStoreType);

        if (persistent)
        {
            services.Configure<AdoJobStoreOptions>(name, options => MapAdoJobStore(options, parser));
        }
        else
        {
            services.Configure<InMemoryJobStoreOptions>(name, options => MapInMemoryJobStore(options, parser));
        }

        if (jobStoreType is null)
        {
            return;
        }

        Register<IDriverDelegate>(services, schedulerName, parser.Type("quartz.jobStore.driverDelegateType"));
        Register<ISemaphore>(services, schedulerName, parser.Type(StdSchedulerFactory.PropertyJobStoreLockHandlerType));

        if (persistent)
        {
            // A persistent store needs the same companions the code-first path registers; configuring it
            // by properties must not leave it half-built.
            var dataSourceName = parser.String("quartz.jobStore.dataSource") ?? "quartz";
            RegisterConfigured<IDbProvider>(services, schedulerName, (provider, _) =>
            {
                var dataSource = provider.GetRequiredService<IOptionsMonitor<DataSourceOptions>>().Get(dataSourceName);
                var connectionString = dataSource.ConnectionString;

                if (string.IsNullOrWhiteSpace(connectionString) && !string.IsNullOrWhiteSpace(dataSource.ConnectionStringName))
                {
                    connectionString = provider.GetService<IConfiguration>()?.GetConnectionString(dataSource.ConnectionStringName);
                }

                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    Throw.SchedulerConfigException($"No connection string configured for data source '{dataSourceName}'.");
                }

                return new DbProvider(dataSource.Provider, connectionString!);
            });

            RegisterDefault<IDriverDelegate, StdAdoDelegate>(services, schedulerName);
            RegisterDefault<ISemaphore, SimpleSemaphore>(services, schedulerName);
            services.TryAddSingleton<IObjectSerializer>(provider =>
            {
                var serializer = ActivatorUtilities.CreateInstance<SystemTextJsonObjectSerializer>(provider);
                serializer.Initialize();
                return serializer;
            });
        }

        RegisterConfigured<IJobStore>(services, schedulerName, (provider, key) =>
        {
            var jobStore = (IJobStore) ActivatorUtilities.CreateInstance(SchedulerScopedServiceProvider.For(provider, key), jobStoreType);
            if (jobStore is RAMJobStore ramJobStore)
            {
                ramJobStore.MisfireThreshold = provider.GetSchedulerOptions<InMemoryJobStoreOptions>(key).MisfireThreshold;
            }
            else if (jobStore is not JobStoreSupport)
            {
                // A third-party store has no typed options, so its knobs still arrive as strings. The
                // ADO store reads AdoJobStoreOptions in its constructor and needs none of this.
                ApplyStringProperties(
                    jobStore, provider, key,
                    StdSchedulerFactory.PropertyJobStorePrefix,
                    StdSchedulerFactory.PropertyJobStoreLockHandlerPrefix);
            }

            return jobStore;
        });
    }

    private static void MapInMemoryJobStore(InMemoryJobStoreOptions options, PropertyReader parser)
    {
        parser.Milliseconds("quartz.jobStore.misfireThreshold", value => options.MisfireThreshold = value);
    }

    private static void MapAdoJobStore(AdoJobStoreOptions options, PropertyReader parser)
    {
        parser.String("quartz.jobStore.dataSource", value => options.DataSource = value);
        parser.String("quartz.jobStore.tablePrefix", value => options.TablePrefix = value);
        parser.Bool("quartz.jobStore.useProperties", value => options.UseProperties = value);
        parser.Milliseconds("quartz.jobStore.misfireThreshold", value => options.MisfireThreshold = value);
        parser.Int("quartz.jobStore.maxMisfiresToHandleAtATime", value => options.MaxMisfiresToHandleAtATime = value);
        parser.Bool("quartz.jobStore.clustered", value =>
        {
            options.Clustered = value;
            // Clustering has always implied database locking; the legacy format never made it a
            // separate decision, so keep it implied rather than failing validation.
            if (value)
            {
                options.UseDbLocks = true;
            }
        });
        parser.Milliseconds("quartz.jobStore.clusterCheckinInterval", value => options.ClusterCheckinInterval = value);
        parser.Milliseconds("quartz.jobStore.clusterCheckinMisfireThreshold", value => options.ClusterCheckinMisfireThreshold = value);
        parser.Milliseconds(StdSchedulerFactory.PropertyJobStoreDbRetryInterval, value => options.DbRetryInterval = value);
        parser.Bool("quartz.jobStore.useDBLocks", value => options.UseDbLocks = value);
        parser.Bool("quartz.jobStore.lockOnInsert", value => options.LockOnInsert = value);
        parser.Bool("quartz.jobStore.acquireTriggersWithinLock", value => options.AcquireTriggersWithinLock = value);
        parser.Bool("quartz.jobStore.txIsolationLevelSerializable", value => options.TxIsolationLevelSerializable = value);
        parser.Bool("quartz.jobStore.doubleCheckLockMisfireHandler", value => options.DoubleCheckLockMisfireHandler = value);
        parser.Bool("quartz.jobStore.performSchemaValidation", value => options.PerformSchemaValidation = value);
        parser.String("quartz.jobStore.selectWithLockSQL", value => options.SelectWithLockSql = value);
        parser.String("quartz.jobStore.driverDelegateInitString", value => options.DriverDelegateInitString = value);
    }

    private static void ApplyDataSourceOptions(IServiceCollection services, PropertyReader parser)
    {
        foreach (var dataSourceName in parser.Groups(StdSchedulerFactory.PropertyDataSourcePrefix))
        {
            var prefix = $"{StdSchedulerFactory.PropertyDataSourcePrefix}.{dataSourceName}";
            services.Configure<DataSourceOptions>(dataSourceName, options =>
            {
                parser.String($"{prefix}.provider", value => options.Provider = value);
                parser.String($"{prefix}.connectionString", value => options.ConnectionString = value);
                parser.String($"{prefix}.connectionStringName", value => options.ConnectionStringName = value);
            });
        }
    }

    private static void ApplySerializer(IServiceCollection services, PropertyReader parser)
    {
        var configured = parser.String("quartz.serializer.type");
        if (configured is null)
        {
            return;
        }

        if (string.Equals(configured, "binary", StringComparison.OrdinalIgnoreCase))
        {
            Throw.SchedulerException(
                "Binary serialization is not supported anymore. Use JSON serialization instead. " +
                "You can also manually configure a custom serializer.");
        }

        var serializerType = configured.ToLowerInvariant() switch
        {
            "stj" or "json" => typeof(SystemTextJsonObjectSerializer),
            "newtonsoft" => typeLoadHelper.LoadType("Quartz.Simpl.NewtonsoftJsonObjectSerializer, Quartz.Serialization.Newtonsoft"),
            _ => typeLoadHelper.LoadType(configured),
        };

        if (serializerType is null)
        {
            Throw.SchedulerException($"Object serializer type '{configured}' could not be loaded.");
        }

        services.TryAddSingleton<IObjectSerializer>(provider =>
        {
            var serializer = (IObjectSerializer) ActivatorUtilities.CreateInstance(provider, serializerType!);
            serializer.Initialize();
            return serializer;
        });
    }

    /// <summary>
    /// Registers a configured implementation type, keyed for a named scheduler and unkeyed for the
    /// default one. A <see langword="null"/> type means the key was not set, so the built-in default
    /// registration stands.
    /// </summary>
    private static void Register<TService>(IServiceCollection services, string? schedulerName, Type? implementationType)
        where TService : class
    {
        if (implementationType is null)
        {
            return;
        }

        if (schedulerName is null)
        {
            services.TryAddSingleton(typeof(TService), implementationType);
        }
        else
        {
            services.TryAddKeyedSingleton(typeof(TService), schedulerName, implementationType);
        }
    }

    /// <summary>
    /// Registers a fallback implementation, which an explicit choice made earlier still beats.
    /// </summary>
    private static void RegisterDefault<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] TImplementation>(
        IServiceCollection services,
        string? schedulerName)
        where TService : class
        where TImplementation : class, TService
    {
        if (schedulerName is null)
        {
            services.TryAddSingleton<TService, TImplementation>();
        }
        else
        {
            services.TryAddKeyedSingleton<TService, TImplementation>(schedulerName);
        }
    }

    /// <summary>
    /// Registers a component that needs configuring after construction, keyed for a named scheduler and
    /// unkeyed for the default one.
    /// </summary>
    private static void RegisterConfigured<TService>(
        IServiceCollection services,
        string? schedulerName,
        Func<IServiceProvider, object?, TService> factory) where TService : class
    {
        if (schedulerName is null)
        {
            services.TryAddSingleton(provider => factory(provider, null));
        }
        else
        {
            services.TryAddKeyedSingleton(schedulerName, (provider, key) => factory(provider, key));
        }
    }

    /// <summary>
    /// Applies the leftover <c>&lt;prefix&gt;.*</c> string properties to a component that has no typed
    /// options of its own, which is how third-party implementations stay configurable.
    /// </summary>
    private static void ApplyStringProperties(
        object target,
        IServiceProvider provider,
        object? key,
        string prefix,
        params string[] excludedPrefixes)
    {
        var properties = new PropertyReader(provider.GetSchedulerProperties(key as string ?? Options.DefaultName))
            .Group(prefix);

        properties.Remove(StdSchedulerFactory.PropertyPluginType);
        foreach (var excluded in excludedPrefixes)
        {
            foreach (var name in properties.AllKeys.ToArray())
            {
                if (name is not null && $"{prefix}.{name}".StartsWith(excluded, StringComparison.Ordinal))
                {
                    properties.Remove(name);
                }
            }
        }

        if (properties.Count > 0)
        {
            ObjectUtils.SetObjectProperties(target, properties);
        }
    }

    /// <summary>
    /// Reads flat property values, converting them to the shapes the typed options expect.
    /// </summary>
    private sealed class PropertyReader
    {
        private readonly NameValueCollection properties;

        public PropertyReader(NameValueCollection properties)
        {
            this.properties = properties;
        }

        public string? String(string key)
        {
            var value = properties[key];
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        public void String(string key, Action<string> apply)
        {
            if (String(key) is { } value)
            {
                apply(value);
            }
        }

        public void Int(string key, Action<int> apply)
        {
            if (String(key) is { } value)
            {
                apply(int.Parse(value, CultureInfo.InvariantCulture));
            }
        }

        public void Bool(string key, Action<bool> apply)
        {
            if (String(key) is { } value)
            {
                apply(bool.Parse(value));
            }
        }

        /// <summary>
        /// Reads a legacy duration, which is a bare integer count of milliseconds.
        /// </summary>
        public void Milliseconds(string key, Action<TimeSpan> apply)
        {
            if (String(key) is { } value)
            {
                apply(TimeSpan.FromMilliseconds(long.Parse(value, CultureInfo.InvariantCulture)));
            }
        }

        public Type? Type(string key)
        {
            var value = String(key);
            if (value is null)
            {
                return null;
            }

            var type = typeLoadHelper.LoadType(value);
            if (type is null)
            {
                Throw.SchedulerConfigException($"Unable to load type '{value}' configured by '{key}'.");
            }

            return type;
        }

        /// <summary>
        /// Returns the properties directly under a prefix, with the prefix stripped.
        /// </summary>
        public NameValueCollection Group(string prefix)
        {
            var result = new NameValueCollection();
            var start = prefix + ".";
            foreach (var key in properties.AllKeys)
            {
                if (key is not null && key.StartsWith(start, StringComparison.Ordinal))
                {
                    result[key[start.Length..]] = properties[key];
                }
            }

            return result;
        }

        /// <summary>
        /// Returns the distinct group names appearing directly under a prefix.
        /// </summary>
        public HashSet<string> Groups(string prefix)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var start = prefix + ".";
            foreach (var key in properties.AllKeys)
            {
                if (key is null || !key.StartsWith(start, StringComparison.Ordinal))
                {
                    continue;
                }

                var remainder = key[start.Length..];
                var separator = remainder.IndexOf('.');
                var group = separator < 0 ? remainder : remainder[..separator];
                if (group.Length > 0)
                {
                    seen.Add(group);
                }
            }

            return seen;
        }
    }
}
