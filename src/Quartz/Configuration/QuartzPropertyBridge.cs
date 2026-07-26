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
using Quartz.Extensibility;
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
    /// Applies a flat property collection as both typed options and service registrations.
    /// </summary>
    /// <remarks>
    /// Only for callers with no configuration callback of their own. Anything that lets the application
    /// configure a scheduler in code calls <see cref="ApplyOptions"/> before that callback and
    /// <see cref="ApplyRegistrations"/> after it, so that code beats strings.
    /// </remarks>
    /// <param name="services">The service collection being configured.</param>
    /// <param name="properties">The flat <c>quartz.*</c> properties.</param>
    /// <param name="schedulerName">
    /// The scheduler these properties belong to, or <see langword="null"/> for the default scheduler.
    /// </param>
    public static void Apply(IServiceCollection services, NameValueCollection properties, string? schedulerName = null)
    {
        ApplyOptions(services, properties, schedulerName);
        ApplyRegistrations(services, properties, schedulerName);
    }

    /// <summary>
    /// Maps the configuration-valued properties onto the typed options.
    /// </summary>
    /// <remarks>
    /// Applied before the caller's configuration callback, so a value set in code is applied last and
    /// therefore wins.
    /// </remarks>
    public static void ApplyOptions(IServiceCollection services, NameValueCollection properties, string? schedulerName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(properties);

        var parser = new PropertyReader(properties);
        var name = schedulerName ?? Microsoft.Extensions.Options.Options.DefaultName;

        services.Configure<QuartzSchedulerOptions>(name, options => MapScheduler(options, parser, schedulerName));
        services.Configure<ThreadPoolOptions>(name, options => MapThreadPool(options, parser));

        // Both store option types are configured because which one is in play depends on the job store
        // that ends up registered, which is not decided yet. The one that turns out to be unused is
        // never resolved, so filling it in costs nothing.
        services.Configure<InMemoryJobStoreOptions>(name, options => MapInMemoryJobStore(options, parser));
        services.Configure<AdoJobStoreOptions>(name, options => MapAdoJobStore(options, parser));

        ApplyDataSourceOptions(services, parser);
    }

    /// <summary>
    /// Registers the implementations the properties select.
    /// </summary>
    /// <remarks>
    /// Ordering matters. Applied after the caller's configuration callback and before the built-in
    /// defaults, so that an implementation chosen in code beats one named by a string, and both beat the
    /// fallback they were meant to replace. Every registration here is a <c>TryAdd</c>, so anything the
    /// application registered earlier still wins.
    /// </remarks>
    public static void ApplyRegistrations(IServiceCollection services, NameValueCollection properties, string? schedulerName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(properties);

        // The type load helper is resolved first and then used to load every other configured type, so
        // an application that keeps its job types in an assembly only its own helper can find gets that
        // helper consulted rather than the built-in one failing on the very next key.
        var parser = new PropertyReader(properties, ConfiguredTypeLoadHelper(services, properties));

        // The serializer goes in before the job store, because the store's persistent branch registers
        // the built-in serializer as a fallback and registration is first-wins.
        ApplySerializer(services, parser, schedulerName);
        RegisterSchedulerParts(services, parser, schedulerName);
        RegisterThreadPool(services, parser, schedulerName);
        RegisterDbProviderMetadata(services, properties);
        RegisterJobStore(services, parser, schedulerName);
        RegisterExecutionLimits(services, properties, schedulerName);
    }

    /// <summary>
    /// Turns <c>quartz.executionLimit.*</c> keys into the same registration <c>UseExecutionLimits</c>
    /// produces, so the scheduler has one place to read limits from rather than two.
    /// </summary>
    private static void RegisterExecutionLimits(IServiceCollection services, NameValueCollection properties, string? schedulerName)
    {
        var limits = ExecutionLimitsParser.Parse(properties);
        if (limits is null)
        {
            return;
        }

        RegisterConfigured<SchedulerExecutionLimits>(services, schedulerName, (_, _) => new SchedulerExecutionLimits(limits));
    }

    /// <summary>
    /// Registers the ADO.NET driver descriptions declared by <c>quartz.dbprovider.*</c> keys.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the one thing the flat format could say that had no code-first equivalent, which is why a
    /// <c>quartz.config</c> file was still being read for it. The keys are unchanged; they now arrive
    /// through the container like everything else, so declaring them in <c>appsettings.json</c> works.
    /// </para>
    /// <para>
    /// A driver description belongs to an ADO.NET provider name rather than to a scheduler, so this is
    /// registered container-wide. Registration order is resolution order: this runs after the
    /// configuration callback and before the built-in descriptions, so a description written in code beats
    /// one written as keys, and both beat the built-in of the same name.
    /// </para>
    /// </remarks>
    private static void RegisterDbProviderMetadata(IServiceCollection services, NameValueCollection properties)
    {
        var prefix = StdSchedulerFactory.PropertyDbProvider + ".";
        var declared = properties.AllKeys.Any(key => key is not null && key.StartsWith(prefix, StringComparison.Ordinal));
        if (!declared)
        {
            // Registering an empty factory would cost a wasted lookup on every provider name resolved.
            return;
        }

        // Copied, so a caller reusing its property collection for another scheduler cannot change what
        // this registration describes after the fact.
        services.AddSingleton<DbMetadataFactory>(
            new ConfigurationBasedDbMetadataFactory(new NameValueCollection(properties), StdSchedulerFactory.PropertyDbProvider));
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

        public void Configure(string? name, TOptions options)
        {
            if (!string.Equals(name ?? Microsoft.Extensions.Options.Options.DefaultName, this.name, StringComparison.Ordinal))
            {
                return;
            }

            var properties = provider.GetRequiredService<IOptionsMonitor<QuartzOptions>>().Get(this.name).ToNameValueCollection();
            map(options, new PropertyReader(properties));
        }
    }

    /// <summary>
    /// Builds the type load helper the configuration names, if it names one.
    /// </summary>
    /// <remarks>
    /// This runs while the service collection is still open, so the helper cannot be resolved from a
    /// container that does not exist yet — it is constructed directly. A helper that needs services of
    /// its own is therefore not supported here, which is the same limit the properties format always had.
    /// </remarks>
    private static ITypeLoadHelper? ConfiguredTypeLoadHelper(IServiceCollection services, NameValueCollection properties)
    {
        var configured = new PropertyReader(properties).Type(StdSchedulerFactory.PropertySchedulerTypeLoadHelperType);
        if (configured is null)
        {
            return null;
        }

        var helper = (ITypeLoadHelper) Activator.CreateInstance(configured)!;

        // Container-wide rather than per-scheduler, and replaced rather than tried, because the built-in
        // default may already have been registered by an earlier scheduler in the same container — and a
        // helper named explicitly must not lose to it.
        services.Replace(ServiceDescriptor.Singleton(helper));
        return helper;
    }

    private static void RegisterSchedulerParts(
        IServiceCollection services,
        PropertyReader parser,
        string? schedulerName)
    {
        var instanceIdGeneratorType = parser.Type(StdSchedulerFactory.PropertySchedulerInstanceIdGeneratorType)
            ?? SystemPropertyGeneratorIfRequested(parser);

        if (instanceIdGeneratorType is not null)
        {
            // A generator has no typed options, so its settings — the system property to read, the
            // prefix and suffix that keep one datacentre's ids from colliding with another's — arrive as
            // strings and have to be applied after construction.
            RegisterConfigured<IInstanceIdGenerator>(services, schedulerName, (provider, key) =>
            {
                var generator = (IInstanceIdGenerator) ActivatorUtilities.CreateInstance(
                    SchedulerScopedServiceProvider.For(provider, key), instanceIdGeneratorType);

                ApplyStringProperties(generator, provider, key, StdSchedulerFactory.PropertySchedulerInstanceIdGeneratorPrefix);
                return generator;
            });
        }

        Register<IJobFactory>(services, schedulerName, parser.Type(StdSchedulerFactory.PropertySchedulerJobFactoryType));

        // Container-wide and replaced rather than tried, for the same reason as the type load helper.
        if (parser.Type(StdSchedulerFactory.PropertyTimeProviderType) is { } timeProviderType)
        {
            services.Replace(ServiceDescriptor.Singleton(typeof(TimeProvider), timeProviderType));
        }
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

    private static void RegisterThreadPool(
        IServiceCollection services,
        PropertyReader parser,
        string? schedulerName)
    {
        // SimpleThreadPool was renamed to DefaultThreadPool, and the old name is still in plenty of
        // config files. Treating it as a synonym is what main did; loading it would just fail.
        // The spelling here is the pre-4.0 one on purpose: Quartz.Simpl.SimpleThreadPool is what those
        // files contain, and no type has ever been called Quartz.Impl.SimpleThreadPool.
        var configured = parser.String(StdSchedulerFactory.PropertyThreadPoolType);
        var threadPoolType = configured is not null
            && configured.StartsWith("Quartz.Simpl.SimpleThreadPool", StringComparison.OrdinalIgnoreCase)
                ? typeof(DefaultThreadPool)
                : parser.Type(StdSchedulerFactory.PropertyThreadPoolType);

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

    private static void RegisterJobStore(
        IServiceCollection services,
        PropertyReader parser,
        string? schedulerName)
    {
        // The delegate and lock handler stand on their own: an application that has moved store
        // selection into code can still be naming its driver delegate in a configuration file, and
        // returning early on a missing quartz.jobStore.type would drop it.
        Register<IDriverDelegate>(services, schedulerName, parser.Type("quartz.jobStore.driverDelegateType"));

        if (parser.Type(StdSchedulerFactory.PropertyJobStoreLockHandlerType) is { } lockHandlerType)
        {
            // A lock handler has no typed options, so its settings — a Redis semaphore's key prefix and
            // lock TTL, say — arrive as strings under its own prefix and are applied after construction.
            RegisterConfigured<ISemaphore>(services, schedulerName, (provider, key) =>
            {
                var lockHandler = (ISemaphore) ActivatorUtilities.CreateInstance(
                    SchedulerScopedServiceProvider.For(provider, key), lockHandlerType);

                ApplyStringProperties(lockHandler, provider, key, StdSchedulerFactory.PropertyJobStoreLockHandlerPrefix);
                return lockHandler;
            });
        }

        var jobStoreType = parser.Type(StdSchedulerFactory.PropertyJobStoreType);
        if (jobStoreType is null)
        {
            return;
        }

        var persistent = typeof(JobStoreSupport).IsAssignableFrom(jobStoreType);

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

                var metadata = provider.GetRequiredService<DbMetadataResolver>().Resolve(dataSource.Provider);
                return new DbProvider(metadata, connectionString!);
            });

            // The driver delegate and serializer fallbacks are registered with the rest of the defaults,
            // after everything explicit, so a configured one is never beaten to the registration by the
            // fallback it was meant to replace. There is deliberately no lock handler fallback either:
            // left unregistered, the store chooses between database row locks and an in-process monitor
            // itself, once it knows how it is clustered and which database it is talking to.
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
        parser.Milliseconds("quartz.jobStore.misfireHandlerFrequency", value => options.MisfireHandlerFrequency = value);
        parser.Int("quartz.jobStore.maxMisfiresToHandleAtATime", value => options.MaxMisfiresToHandleAtATime = value);
        parser.Int("quartz.jobStore.maxTransientRetries", value => options.MaxTransientRetries = value);
        parser.Milliseconds("quartz.jobStore.transientRetryInterval", value => options.TransientRetryInterval = value);
        parser.Int("quartz.jobStore.retryableActionErrorLogThreshold", value => options.RetryableActionErrorLogThreshold = value);
        parser.Bool("quartz.jobStore.makeThreadsDaemons", value => options.MakeThreadsDaemons = value);
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

    private static void ApplySerializer(IServiceCollection services, PropertyReader parser, string? schedulerName)
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
            "newtonsoft" => typeLoadHelper.LoadType("Quartz.Impl.NewtonsoftJsonObjectSerializer, Quartz.Serialization.Newtonsoft"),
            _ => typeLoadHelper.LoadType(configured),
        };

        if (serializerType is null)
        {
            Throw.SchedulerException($"Object serializer type '{configured}' could not be loaded.");
        }

        RegisterConfigured<IObjectSerializer>(services, schedulerName, (provider, key) =>
        {
            var serializer = (IObjectSerializer) ActivatorUtilities.CreateInstance(
                SchedulerScopedServiceProvider.For(provider, key), serializerType!);

            // The serializer's own settings, such as whether to register the optimized trigger
            // converters, are applied before Initialize builds the converter set from them.
            ApplyStringProperties(serializer, provider, key, SerializerPrefix);
            return serializer;
        });
    }

    private const string SerializerPrefix = "quartz.serializer";


    /// <summary>
    /// Registers a configured implementation type, keyed for a named scheduler and unkeyed for the
    /// default one. A <see langword="null"/> type means the key was not set, so the built-in default
    /// registration stands.
    /// </summary>
    /// <remarks>
    /// Construction goes through <see cref="SchedulerScopedServiceProvider"/> rather than being left to
    /// the container, which would resolve the implementation's own dependencies unkeyed and hand a named
    /// scheduler's component the default scheduler's collaborators — or none at all.
    /// </remarks>
    private static void Register<TService>(IServiceCollection services, string? schedulerName, Type? implementationType)
        where TService : class
    {
        if (implementationType is null)
        {
            return;
        }

        RegisterConfigured<TService>(services, schedulerName, (provider, key) =>
            (TService) ActivatorUtilities.CreateInstance(SchedulerScopedServiceProvider.For(provider, key), implementationType));
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
        RegisterConfigured<TService>(services, schedulerName, (provider, key) =>
            ActivatorUtilities.CreateInstance<TImplementation>(SchedulerScopedServiceProvider.For(provider, key)));
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

        public PropertyReader(NameValueCollection properties, ITypeLoadHelper? loader = null)
        {
            this.properties = properties;
            this.loader = loader ?? typeLoadHelper;
        }

        private readonly ITypeLoadHelper loader;

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
        /// Reads a duration in either spelling.
        /// </summary>
        /// <remarks>
        /// The legacy format writes a bare integer count of milliseconds; the typed options are
        /// <see cref="TimeSpan"/> and are written <c>00:00:30</c>. Both reach this reader, because the
        /// same setting can be said either way, and the two are told apart by shape: a bare integer is
        /// milliseconds, which is also what stops <c>30000</c> being read as thirty thousand days.
        /// </remarks>
        public void Milliseconds(string key, Action<TimeSpan> apply)
        {
            if (String(key) is not { } value)
            {
                return;
            }

            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds))
            {
                apply(TimeSpan.FromMilliseconds(milliseconds));
                return;
            }

            if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var timeSpan))
            {
                Throw.SchedulerConfigException(
                    $"Value '{value}' of '{key}' is neither a count of milliseconds nor a time span.");
            }

            apply(timeSpan);
        }

        public Type? Type(string key)
        {
            var value = String(key);
            if (value is null)
            {
                return null;
            }

            var type = loader.LoadType(value);
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
