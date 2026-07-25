using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Quartz.Impl.AdoJobStore;
using Quartz.Simpl;
using Quartz.Spi;

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
        return UseThreadPool<DefaultThreadPool>(configure);
    }

    public IQuartzBuilder UseThreadPool<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Action<ThreadPoolOptions>? configure = null) where T : class, IThreadPool
    {
        if (configure is not null)
        {
            Services.Configure(OptionsName, configure);
        }

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

    public IQuartzBuilder UseInMemoryStore(Action<InMemoryJobStoreOptions>? configure = null)
    {
        if (configure is not null)
        {
            Services.Configure(OptionsName, configure);
        }

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
        return UsePersistentStore<JobStoreTX>(configure);
    }

    public IQuartzBuilder UsePersistentStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Action<IPersistentStoreBuilder> configure) where T : class, IJobStore
    {
        ArgumentNullException.ThrowIfNull(configure);

        RegisterConfigured<IJobStore>((provider, key) =>
            ActivatorUtilities.CreateInstance<T>(SchedulerScopedServiceProvider.For(provider, key)));

        var store = new PersistentStoreBuilder(Services, schedulerKey);
        configure(store);

        // Registered after the callback so an explicitly chosen serializer wins over this fallback.
        store.UseSerializer<SystemTextJsonObjectSerializer>();
        return this;
    }

    public IQuartzBuilder UseJobFactory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Action<JobFactoryOptions>? configure = null) where T : class, IJobFactory
    {
        Register<IJobFactory, T>();
        if (configure is not null)
        {
            Services.Configure<QuartzOptions>(OptionsName, options => configure(options.JobFactory));
        }

        return this;
    }

    public IQuartzBuilder UseTypeLoader<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, ITypeLoadHelper
    {
        // Type loading is a container-wide concern rather than a per-scheduler one.
        Services.Replace(ServiceDescriptor.Singleton<ITypeLoadHelper, T>());
        return this;
    }

    public IQuartzBuilder UseTimeProvider(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        Services.Replace(ServiceDescriptor.Singleton(timeProvider));
        return this;
    }

    public IQuartzBuilder AddPlugin<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, ISchedulerPlugin
    {
        AddEnumerable<ISchedulerPlugin, T>();
        return this;
    }

    public IQuartzBuilder AddPlugin<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T, TOptions>(
        Action<TOptions>? configure = null)
        where T : class, ISchedulerPlugin
        where TOptions : class
    {
        if (configure is not null)
        {
            Services.Configure(OptionsName, configure);
        }

        return AddPlugin<T>();
    }

    public IQuartzBuilder AddSchedulerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, ISchedulerListener
    {
        Services.AddSingleton(new SchedulerListenerConfiguration(typeof(T), SchedulerName));
        AddEnumerable<ISchedulerListener, T>();
        return this;
    }

    public IQuartzBuilder AddSchedulerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T listener) where T : class, ISchedulerListener
    {
        Services.AddSingleton(new SchedulerListenerConfiguration(typeof(T), SchedulerName, listenerInstance: listener));
        return this;
    }

    public IQuartzBuilder AddSchedulerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory) where T : class, ISchedulerListener
    {
        Services.AddSingleton(new SchedulerListenerConfiguration(typeof(T), SchedulerName, listenerFactory: provider => factory(provider)));
        return this;
    }

    public IQuartzBuilder AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        params IMatcher<JobKey>[] matchers) where T : class, IJobListener
    {
        Services.AddSingleton(new JobListenerConfiguration(typeof(T), matchers, SchedulerName));
        AddEnumerable<IJobListener, T>();
        return this;
    }

    public IQuartzBuilder AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T listener, params IMatcher<JobKey>[] matchers) where T : class, IJobListener
    {
        Services.AddSingleton(new JobListenerConfiguration(typeof(T), matchers, SchedulerName, listenerInstance: listener));
        return this;
    }

    public IQuartzBuilder AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory, params IMatcher<JobKey>[] matchers) where T : class, IJobListener
    {
        Services.AddSingleton(new JobListenerConfiguration(typeof(T), matchers, SchedulerName, listenerFactory: provider => factory(provider)));
        return this;
    }

    public IQuartzBuilder AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        params IMatcher<TriggerKey>[] matchers) where T : class, ITriggerListener
    {
        Services.AddSingleton(new TriggerListenerConfiguration(typeof(T), matchers, SchedulerName));
        AddEnumerable<ITriggerListener, T>();
        return this;
    }

    public IQuartzBuilder AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T listener, params IMatcher<TriggerKey>[] matchers) where T : class, ITriggerListener
    {
        Services.AddSingleton(new TriggerListenerConfiguration(typeof(T), matchers, SchedulerName, listenerInstance: listener));
        return this;
    }

    public IQuartzBuilder AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory, params IMatcher<TriggerKey>[] matchers) where T : class, ITriggerListener
    {
        Services.AddSingleton(new TriggerListenerConfiguration(typeof(T), matchers, SchedulerName, listenerFactory: provider => factory(provider)));
        return this;
    }

    public IQuartzBuilder UseExecutionLimits(Action<ExecutionLimits> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var limits = new ExecutionLimits();
        configure(limits);
        Services.AddSingleton(new SchedulerExecutionLimits(SchedulerName, limits));
        return this;
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
            Services.TryAddEnumerable(ServiceDescriptor.KeyedSingleton<TService, TImplementation>(schedulerKey));
        }
    }
}

/// <summary>
/// Execution group limits configured for a scheduler.
/// </summary>
internal sealed record SchedulerExecutionLimits(string SchedulerName, ExecutionLimits Limits);
