using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl;
using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Configuration;

/// <inheritdoc />
internal sealed class QuartzBuilder : IQuartzBuilder
{
    private readonly string? schedulerKey;

    public QuartzBuilder(IServiceCollection services, string? schedulerKey)
    {
        Services = services;
        this.schedulerKey = schedulerKey;
    }

    public IServiceCollection Services { get; }

    public string SchedulerName => schedulerKey ?? "";

    private string OptionsName => schedulerKey ?? Microsoft.Extensions.Options.Options.DefaultName;

    public IQuartzBuilder ConfigureScheduler(Action<QuartzSchedulerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        Services.Configure(OptionsName, configure);

        // A named scheduler's name is fixed by its registration; re-apply it so configuration cannot
        // move it out from under the service key its components are registered with.
        if (schedulerKey is not null)
        {
            Services.Configure<QuartzSchedulerOptions>(OptionsName, options => options.InstanceName = schedulerKey);
        }

        return this;
    }

    public IQuartzBuilder UseDefaultThreadPool(int maxConcurrency)
    {
        return UseDefaultThreadPool(options => options.MaxConcurrency = maxConcurrency);
    }

    public IQuartzBuilder UseDefaultThreadPool(Action<ThreadPoolOptions>? configure = null)
    {
        if (configure is not null)
        {
            Services.Configure(OptionsName, configure);
        }

        return UseThreadPool<DefaultThreadPool>();
    }

    public IQuartzBuilder UseThreadPool<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, IThreadPool
    {
        RegisterConfigured<IThreadPool>((provider, key) =>
        {
            var threadPool = ActivatorUtilities.CreateInstance<T>(SchedulerScopedServiceProvider.For(provider, key));
            if (threadPool is TaskSchedulingThreadPool schedulingThreadPool)
            {
                schedulingThreadPool.MaxConcurrency = provider.GetSchedulerOptions<ThreadPoolOptions>(key).MaxConcurrency;
            }

            return threadPool;
        });

        return this;
    }

    public IQuartzBuilder UseThreadPool(IThreadPool threadPool)
    {
        ArgumentNullException.ThrowIfNull(threadPool);
        RegisterConfigured<IThreadPool>((_, _) => threadPool);
        return this;
    }

    public IQuartzBuilder UseJobStore(IJobStore jobStore)
    {
        ArgumentNullException.ThrowIfNull(jobStore);
        RegisterConfigured<IJobStore>((_, _) => jobStore);
        return this;
    }

    public IQuartzBuilder UseJobStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, IJobStore
    {
        Register<IJobStore, T>();
        return this;
    }

    public IQuartzBuilder UseJobStore<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        Action<TOptions>? configure = null)
        where T : class, IJobStore
        where TOptions : class
    {
        ConfigureOptions(configure);
        return UseJobStore<T>();
    }

    public IQuartzBuilder UseJobStore(Func<IServiceProvider, IJobStore> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        RegisterConfigured<IJobStore>((provider, key) => factory(SchedulerScopedServiceProvider.For(provider, key)));
        return this;
    }

    public IQuartzBuilder UseInMemoryStore(Action<InMemoryJobStoreOptions>? configure = null)
    {
        if (configure is not null)
        {
            Services.Configure(OptionsName, configure);
        }

        // Chosen, so it will be read — which makes a bad value in it something the host can report at
        // startup rather than when the store is built.
        Services.ValidateOnStart<InMemoryJobStoreOptions>(schedulerKey);

        RegisterConfigured<IJobStore>((provider, key) =>
        {
            var jobStore = ActivatorUtilities.CreateInstance<RAMJobStore>(SchedulerScopedServiceProvider.For(provider, key));
            jobStore.MisfireThreshold = provider.GetSchedulerOptions<InMemoryJobStoreOptions>(key).MisfireThreshold;
            return jobStore;
        });

        return this;
    }

    public IQuartzBuilder UsePersistentStore(Action<IPersistentStoreBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        // Which of the two shipped stores this is decided inside the callback, so the registration
        // waits for it: registration is first-wins, and registering the local store before running the
        // callback would make UseAmbientTransactions a call that quietly did nothing.
        PersistentStoreBuilder store = ConfigurePersistentStore(configure);

        if (store.AmbientTransactions)
        {
            RegisterConfigured<IJobStore>((provider, key) =>
                ActivatorUtilities.CreateInstance<ExternalTransactionJobStore>(SchedulerScopedServiceProvider.For(provider, key)));
        }
        else
        {
            RegisterConfigured<IJobStore>((provider, key) =>
                ActivatorUtilities.CreateInstance<LocalTransactionJobStore>(SchedulerScopedServiceProvider.For(provider, key)));
        }

        return this;
    }

    public IQuartzBuilder UsePersistentStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Action<IPersistentStoreBuilder> configure) where T : class, IJobStore
    {
        ArgumentNullException.ThrowIfNull(configure);

        RegisterConfigured<IJobStore>((provider, key) =>
            ActivatorUtilities.CreateInstance<T>(SchedulerScopedServiceProvider.For(provider, key)));

        PersistentStoreBuilder store = ConfigurePersistentStore(configure);

        if (store.AmbientTransactions)
        {
            // The type argument already named the store, and UseAmbientTransactions names a different
            // one. Silently keeping T would leave a scheduler committing transactions the caller
            // believed somebody else owned.
            Throw.SchedulerConfigException(
                "UseAmbientTransactions() selects the store that runs inside a transaction somebody else owns, "
                + $"but this store was already named as '{typeof(T).Name}'. Call UsePersistentStore(...) without a "
                + "type argument to use it, or drop the UseAmbientTransactions() call.");
        }

        return this;
    }

    /// <summary>
    /// Runs a persistent store's configuration callback and hands back what it decided.
    /// </summary>
    /// <remarks>
    /// The serializer, driver delegate and lock handler fallbacks are deliberately not registered here.
    /// A serializer named in a configuration file is applied after this callback, so a fallback
    /// registered inside it would win the first-wins race and silently replace the caller's choice.
    /// They go in with the rest of the defaults instead, after everything explicit — and the lock
    /// handler has no fallback at all, so the store can choose one once it knows its database.
    /// </remarks>
    private PersistentStoreBuilder ConfigurePersistentStore(Action<IPersistentStoreBuilder> configure)
    {
        PersistentStoreBuilder store = new(Services, schedulerKey);
        configure(store);
        return store;
    }

    public IQuartzBuilder UseJobFactory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, IJobFactory
    {
        Register<IJobFactory, T>();
        return this;
    }

    public IQuartzBuilder UseJobFactory(IJobFactory jobFactory)
    {
        ArgumentNullException.ThrowIfNull(jobFactory);
        RegisterConfigured<IJobFactory>((_, _) => jobFactory);
        return this;
    }

    public IQuartzBuilder UseTypeLoader<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, ITypeLoader
    {
        // Type loading is a container-wide concern rather than a per-scheduler one.
        Services.Replace(ServiceDescriptor.Singleton<ITypeLoader, T>());
        return this;
    }

    public IQuartzBuilder UseInstanceIdGenerator<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, IInstanceIdGenerator
    {
        GenerateInstanceId();
        Register<IInstanceIdGenerator, T>();
        return this;
    }

    public IQuartzBuilder UseInstanceIdGenerator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        Action<TOptions>? configure = null)
        where T : class, IInstanceIdGenerator
        where TOptions : class
    {
        ConfigureOptions(configure);
        return UseInstanceIdGenerator<T>();
    }

    public IQuartzBuilder UseInstanceIdGenerator(IInstanceIdGenerator generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        GenerateInstanceId();
        RegisterConfigured<IInstanceIdGenerator>((_, _) => generator);
        return this;
    }

    /// <summary>
    /// Says the instance id is to be generated, which is what choosing a generator means.
    /// </summary>
    /// <remarks>
    /// The flat format spells this as <c>quartz.scheduler.instanceId = AUTO</c> beside the generator's
    /// type name, and a generator named without it is never called. Two things to say for one intention
    /// is one thing to forget, so choosing a generator says both. A scheduler that means the opposite can
    /// still say so afterwards, since options are last-wins.
    /// </remarks>
    private void GenerateInstanceId()
    {
        Services.Configure<QuartzSchedulerOptions>(OptionsName, options => options.GenerateInstanceId = true);
    }

    public IQuartzBuilder UseTimeProvider(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        // Registered at this scheduler's slot, like every other component. A container-wide replacement
        // would re-time every other scheduler in the container from one scheduler's configuration — and
        // a test that hands one scheduler a fake clock does not mean the others should start lying too.
        if (schedulerKey is null)
        {
            Services.Replace(ServiceDescriptor.Singleton(timeProvider));
        }
        else
        {
            RemoveKeyed<TimeProvider>();
            Services.AddKeyedSingleton(schedulerKey, timeProvider);
        }

        return this;
    }

    /// <summary>
    /// Removes this scheduler's registrations of a service, so one made here replaces rather than joins
    /// them.
    /// </summary>
    /// <remarks>
    /// <see cref="ServiceCollectionDescriptorExtensions.Replace"/>'s counterpart for a keyed
    /// registration: it matches on the service type alone, so it would remove some other scheduler's.
    /// </remarks>
    private void RemoveKeyed<TService>()
    {
        for (var i = Services.Count - 1; i >= 0; i--)
        {
            ServiceDescriptor descriptor = Services[i];
            if (descriptor.ServiceType == typeof(TService)
                && descriptor.IsKeyedService
                && Equals(descriptor.ServiceKey, schedulerKey))
            {
                Services.RemoveAt(i);
            }
        }
    }

    public IQuartzBuilder ConfigureOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        Action<TOptions>? configure = null) where TOptions : class
    {
        if (configure is not null)
        {
            Services.Configure(OptionsName, configure);
        }

        // The options are configured under this scheduler's name, but the component is built by
        // ActivatorUtilities and so asks for IOptions<TOptions>, which resolves the unnamed instance.
        // This says the type is a scheduler's own, the way the built-in options types are declared to
        // be, so the component is handed what was configured for it rather than defaults. Registered
        // whether or not a callback was given: where the options come from is not something adding one
        // should change.
        Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<SchedulerNamedOptions>(new SchedulerNamedOptions<TOptions>()));

        return this;
    }

    public IQuartzBuilder AddPlugin<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        string? name = null)
        where T : class, ISchedulerPlugin
    {
        NamePlugin<T>(name);
        AddEnumerable<ISchedulerPlugin, T>();
        return this;
    }

    /// <summary>
    /// Adds a plugin the caller builds and configures, under this scheduler's key.
    /// </summary>
    /// <remarks>
    /// Plugin packages use this rather than registering against <see cref="Services"/> directly, which
    /// would register unkeyed and leave a named scheduler without the plugin.
    /// </remarks>
    public IQuartzBuilder AddPlugin<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory,
        string? name = null) where T : class, ISchedulerPlugin
    {
        ArgumentNullException.ThrowIfNull(factory);
        NamePlugin<T>(name);

        if (schedulerKey is null)
        {
            Services.AddSingleton<ISchedulerPlugin>(provider => factory(provider));
        }
        else
        {
            Services.AddKeyedSingleton<ISchedulerPlugin>(
                schedulerKey,
                (provider, key) => factory(SchedulerScopedServiceProvider.For(provider, key)));
        }

        return this;
    }

    public IQuartzBuilder AddPlugin<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        Action<TOptions>? configure = null,
        string? name = null)
        where T : class, ISchedulerPlugin
        where TOptions : class
    {
        // Sugar over the general mechanism: a plugin's options are declared the same way any other
        // component's are.
        ConfigureOptions(configure);
        return AddPlugin<T>(name);
    }

    /// <summary>
    /// Records the name a plugin registered in code should be known by.
    /// </summary>
    private void NamePlugin<T>(string? name) where T : class, ISchedulerPlugin
    {
        if (name is null)
        {
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Services.AddSingleton(new SchedulerPluginName(SchedulerName, typeof(T), name));
    }

    /// <remarks>
    /// Only the registration is added. Registering the listener as a service as well would attach it
    /// twice, once from each source.
    /// </remarks>
    public IQuartzBuilder AddSchedulerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, ISchedulerListener
    {
        AddContent(new SchedulerListenerRegistration(typeof(T)));
        return this;
    }

    public IQuartzBuilder AddSchedulerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T listener) where T : class, ISchedulerListener
    {
        AddContent(new SchedulerListenerRegistration(typeof(T), listenerInstance: listener));
        return this;
    }

    public IQuartzBuilder AddSchedulerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory) where T : class, ISchedulerListener
    {
        AddContent(new SchedulerListenerRegistration(typeof(T), listenerFactory: provider => factory(provider)));
        return this;
    }

    public IQuartzBuilder AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        params IReadOnlyCollection<IMatcher<JobKey>> matchers) where T : class, IJobListener
    {
        AddContent(new JobListenerRegistration(typeof(T), [.. matchers]));
        return this;
    }

    public IQuartzBuilder AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T listener, params IReadOnlyCollection<IMatcher<JobKey>> matchers) where T : class, IJobListener
    {
        AddContent(new JobListenerRegistration(typeof(T), [.. matchers], listenerInstance: listener));
        return this;
    }

    public IQuartzBuilder AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory, params IReadOnlyCollection<IMatcher<JobKey>> matchers) where T : class, IJobListener
    {
        AddContent(new JobListenerRegistration(typeof(T), [.. matchers], listenerFactory: provider => factory(provider)));
        return this;
    }

    public IQuartzBuilder AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        params IReadOnlyCollection<IMatcher<TriggerKey>> matchers) where T : class, ITriggerListener
    {
        AddContent(new TriggerListenerRegistration(typeof(T), [.. matchers]));
        return this;
    }

    public IQuartzBuilder AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T listener, params IReadOnlyCollection<IMatcher<TriggerKey>> matchers) where T : class, ITriggerListener
    {
        AddContent(new TriggerListenerRegistration(typeof(T), [.. matchers], listenerInstance: listener));
        return this;
    }

    public IQuartzBuilder AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory, params IReadOnlyCollection<IMatcher<TriggerKey>> matchers) where T : class, ITriggerListener
    {
        AddContent(new TriggerListenerRegistration(typeof(T), [.. matchers], listenerFactory: provider => factory(provider)));
        return this;
    }

    /// <remarks>
    /// Registered as content rather than as a service, for the reason a listener is: a middleware that
    /// was also resolvable as <c>IJobExecutionMiddleware</c> would be a second source the pipeline would
    /// have to deduplicate against, and there is no name to deduplicate by.
    /// </remarks>
    public IQuartzBuilder AddJobMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, IJobExecutionMiddleware
    {
        AddContent(new JobExecutionMiddlewareRegistration(typeof(T)));
        return this;
    }

    public IQuartzBuilder AddJobMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory) where T : class, IJobExecutionMiddleware
    {
        ArgumentNullException.ThrowIfNull(factory);
        AddContent(new JobExecutionMiddlewareRegistration(typeof(T), middlewareFactory: provider => factory(provider)));
        return this;
    }

    public IQuartzBuilder AddJobMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T middleware) where T : class, IJobExecutionMiddleware
    {
        ArgumentNullException.ThrowIfNull(middleware);
        AddContent(new JobExecutionMiddlewareRegistration(typeof(T), middlewareInstance: middleware));
        return this;
    }

    public IQuartzBuilder UseExecutionLimits(Action<ExecutionLimitsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = ExecutionLimitsBuilder.Create();
        configure(builder);
        ExecutionLimits limits = builder.Build();

        // TryAdd, and this runs before the property-derived registration, so limits set in code beat the
        // same limits spelled as quartz.executionLimit.* keys — as everywhere else.
        RegisterConfigured<SchedulerExecutionLimits>((_, _) => new SchedulerExecutionLimits(limits));
        return this;
    }

    /// <summary>
    /// Registers something this scheduler carries — a listener — under its own key.
    /// </summary>
    /// <remarks>
    /// Several of these can exist per scheduler and they are resolved as a set, so they go in the same
    /// shape plugins do rather than as a single keyed service.
    /// </remarks>
    private void AddContent<TContent>(TContent content) where TContent : class
    {
        SchedulerContentRegistration.Add(this, content);
    }

    /// <summary>
    /// Registers a per-scheduler service, keyed for a named scheduler and unkeyed for the default one.
    /// </summary>
    /// <remarks>
    /// Construction goes through <see cref="SchedulerScopedServiceProvider"/> so the component is given
    /// its own scheduler's collaborators. Registering the implementation type directly would resolve
    /// them unkeyed, which for a named scheduler means the wrong ones or none at all.
    /// </remarks>
    private void Register<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        RegisterConfigured<TService>((provider, key) =>
            ActivatorUtilities.CreateInstance<TImplementation>(SchedulerScopedServiceProvider.For(provider, key)));
    }

    /// <summary>
    /// Registers a per-scheduler service built by a factory that knows which scheduler it belongs to.
    /// </summary>
    private void RegisterConfigured<TService>(Func<IServiceProvider, object?, TService> factory)
        where TService : class
    {
        if (schedulerKey is null)
        {
            Services.TryAddSingleton(provider => factory(provider, null));
        }
        else
        {
            Services.TryAddKeyedSingleton(schedulerKey, (provider, key) => factory(provider, key));
        }
    }

    /// <summary>
    /// Registers one of several services of the same type, keyed for a named scheduler.
    /// </summary>
    private void AddEnumerable<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        if (schedulerKey is null)
        {
            Services.TryAddEnumerable(ServiceDescriptor.Singleton<TService, TImplementation>());
        }
        else
        {
            // Built through the scoped provider, so a plugin that depends on the scheduler it extends
            // gets that scheduler's parts rather than the default scheduler's.
            Services.TryAddEnumerable(ServiceDescriptor.KeyedSingleton<TService, TImplementation>(
                schedulerKey,
                static (provider, key) => ActivatorUtilities.CreateInstance<TImplementation>(SchedulerScopedServiceProvider.For(provider, key))));
        }
    }
}

/// <summary>
/// Execution group limits configured for a scheduler.
/// </summary>
/// <remarks>
/// Registered per scheduler like every other component, so the scheduler it belongs to is the service
/// key rather than a field to be filtered on afterwards.
/// </remarks>
internal sealed record SchedulerExecutionLimits(ExecutionLimits Limits);

/// <summary>
/// The name a plugin registered in code should be known by.
/// </summary>
/// <remarks>
/// Without this a plugin built in code would be named after its type, while the same plugin named by a
/// <c>quartz.plugin.&lt;name&gt;.*</c> key keeps the short name — and plugins that derive persisted job
/// keys from their name would write a different set of rows depending on how they were configured.
/// </remarks>
internal sealed record SchedulerPluginName(string SchedulerName, Type PluginType, string Name);
