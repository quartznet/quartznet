using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Core;
using Quartz.Diagnostics;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Serialization.SystemTextJson;
using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Configuration;

/// <summary>
/// Registers a scheduler's object graph as ordinary services, so the container — not a reflective
/// factory — is what constructs a scheduler.
/// </summary>
/// <remarks>
/// <para>
/// A scheduler's parts are registered under the scheduler's own name as the service key, which is the
/// container's native way to express "several of these, told apart by name". The default scheduler is
/// registered without a key, so <c>GetRequiredService&lt;IScheduler&gt;()</c> keeps meaning what it
/// always did.
/// </para>
/// <para>
/// This is what makes named schedulers ordinary rather than special. Previously a named scheduler could
/// not register its own job store or thread pool, because a container holds one registration per service
/// type and the name was not known to the container at all; names existed only in
/// <see cref="ISchedulerRepository"/>, after the fact. Everything named therefore had to be deferred and
/// filtered by name at resolution time. With the name as the service key, each scheduler simply has its
/// own registrations, and the repository goes back to being a lookup of live schedulers rather than a
/// registry that construction depends on.
/// </para>
/// <para>
/// Every component is registered with <c>TryAdd</c>, so anything the application registered first wins.
/// That is what makes "bring your own job store / thread pool / serializer" work without type-name strings.
/// </para>
/// </remarks>
internal static class QuartzServiceRegistration
{
    /// <summary>
    /// Registers services shared by every scheduler in the container. Safe to call repeatedly.
    /// </summary>
    public static IServiceCollection AddQuartzSharedServices(this IServiceCollection services)
    {
        services.AddQuartzOptionsValidation();
        services.AddLogging();

        // Job execution metrics. Built from the container's IMeterFactory when it has one — every
        // application on the generic host calls AddMetrics() for itself — so the measurements belong to
        // this container rather than to the process, and two hosts in one test run can be told apart.
        services.TryAddSingleton(static provider => new Meters(provider.GetService<IMeterFactory>()));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ITypeLoader, SimpleTypeLoader>();
        // The repository belongs to this container and nothing else. It has no process-wide instance any
        // more, so "which repository am I in" is answered by which container built the scheduler rather
        // than by how it was built.
        services.TryAddSingleton<ISchedulerRepository, SchedulerRepository>();

        // What the container was told to register, which the repository cannot know - it holds
        // schedulers, and a registration nothing has resolved yet has none. Listing tenants must not
        // start them, so the two are read together rather than by building everything registered.
        services.TryAddSingleton<ISchedulerRegistry, ContainerSchedulerRegistry>();

        // "Another scheduler shares this database" is only knowable here, one level above the store.
        services.TryAddSingleton<SharedDatabaseValidator>();

        // The firing a component is serving, for components that are not handed the execution context.
        // Container-wide because a firing is a property of the asynchronous flow rather than of one
        // scheduler: a flow is inside at most one of them, whichever scheduler started it.
        services.TryAddSingleton<IJobExecutionContextAccessor, JobExecutionContextAccessor>();

        // The container-wide set of trigger and calendar serializers, holding the built-in types. This is
        // what the parts of Quartz that are not tied to one scheduler read — the HTTP API, the dashboard
        // and the HTTP client all serialize triggers without knowing which scheduler they came from — so
        // a custom serializer that should be visible there is registered here rather than through one
        // scheduler's UseSystemTextJsonSerializer callback.
        services.TryAddSingleton<SystemTextJsonSerializerRegistry>();

        // The descriptions of the ADO.NET drivers Quartz ships, added last so that a driver described in
        // code or by quartz.dbprovider.* keys — both of which register earlier — wins over a built-in of
        // the same name. Resolution walks the registrations in order.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<DbMetadataFactory, BuiltInDbMetadataFactory>(
                static _ => new BuiltInDbMetadataFactory()));

        // One resolver per container, so its cache of resolved descriptions cannot leak one container's
        // idea of a provider name into another's.
        services.TryAddSingleton<DbMetadataResolver>();

        return services;
    }

    /// <summary>
    /// Registers one scheduler's object graph.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="schedulerName">
    /// The scheduler's name, used both as the service key and as the options name, or
    /// <see langword="null"/> for the default scheduler. The default scheduler is registered without a
    /// service key and reads unnamed options.
    /// </param>
    public static IServiceCollection AddQuartzScheduler(this IServiceCollection services, string? schedulerName = null)
    {
        services.AddQuartzSharedServices();

        // Recorded here rather than beside the named registrations because this is the one call every
        // road to a default scheduler passes through - AddQuartz(), the standalone builder, and a caller
        // registering the graph by hand. A named scheduler is added to the registry by AddQuartz(name),
        // which is where a duplicate name is refused.
        if (schedulerName is null)
        {
            SchedulerNameRegistry.For(services).AddDefault();
        }

        // Every scheduler resolves these two, so a bad value in either is a mistake the host can report
        // at startup instead of leaving for whichever component reads it first. The store, clustering
        // and data-source options are declared where they are chosen, since a scheduler that did not
        // choose them never reads them.
        services.ValidateOnStart<QuartzSchedulerOptions>(schedulerName);
        services.ValidateOnStart<ThreadPoolOptions>(schedulerName);

        // The default scheduler has no key, so plain resolution finds it. Named schedulers are keyed by
        // name, which is also their options name.
        object? key = schedulerName;

        services.TryAddKeyed<IJobFactory>(key, static (provider, key) =>
            ActivatorUtilities.CreateInstance<MicrosoftDependencyInjectionJobFactory>(Scoped(provider, key)));

        services.TryAddKeyed<IJobRunShellFactory>(key, static (provider, key) =>
            ActivatorUtilities.CreateInstance<StdJobRunShellFactory>(Scoped(provider, key)));

        services.TryAddKeyed<IInstanceIdGenerator>(key, static (provider, key) =>
            ActivatorUtilities.CreateInstance<SimpleInstanceIdGenerator>(Scoped(provider, key)));

        services.TryAddKeyed<ISchedulerSignaler>(key, static (provider, key) =>
            new LazySchedulerSignaler(provider, new SchedulerKey(key)));

        services.TryAddKeyed<IThreadPool>(key, static (provider, key) =>
        {
            var options = provider.GetSchedulerOptions<ThreadPoolOptions>(key);
            var threadPool = ActivatorUtilities.CreateInstance<DefaultThreadPool>(Scoped(provider, key));
            threadPool.MaxConcurrency = options.MaxConcurrency;
            return threadPool;
        });

        services.TryAddKeyed<IJobStore>(key, static (provider, key) =>
        {
            var options = provider.GetSchedulerOptions<InMemoryJobStoreOptions>(key);
            var jobStore = ActivatorUtilities.CreateInstance<RAMJobStore>(Scoped(provider, key));
            jobStore.MisfireThreshold = options.MisfireThreshold;
            return jobStore;
        });

        // The database store's companions. Registered here with the rest of the defaults rather than
        // alongside the store itself, because registration is first-wins and these have to lose to
        // anything the application chose — including a serializer named in a configuration file, which
        // is applied after the configuration callback has run.
        services.TryAddKeyed<IDriverDelegate>(key, static (provider, key) =>
            ActivatorUtilities.CreateInstance<StdAdoDelegate>(Scoped(provider, key)));

        // A named scheduler that was not given its own set of custom trigger and calendar serializers
        // reads the container's. Registered keyed so an application can hand one scheduler a different
        // set — services.AddKeyedSingleton(schedulerName, registry) — without affecting the others.
        if (schedulerName is not null)
        {
            services.TryAddKeyedSingleton<SystemTextJsonSerializerRegistry>(
                schedulerName,
                static (provider, _) => provider.GetRequiredService<SystemTextJsonSerializerRegistry>());
        }

        services.TryAddKeyed<IObjectSerializer>(key, static (provider, key) =>
        {
            // Construction goes through ActivatorUtilities so this scheduler's
            // SystemTextJsonSerializerRegistry is injected. The converter set is built on first use.
            var serializer = ActivatorUtilities.CreateInstance<SystemTextJsonObjectSerializer>(Scoped(provider, key));
            return serializer;
        });

        services.TryAddKeyed<QuartzSchedulerResources>(key, static (provider, key) =>
        {
            var options = provider.GetSchedulerOptions<QuartzSchedulerOptions>(key);
            var instanceName = key as string ?? options.InstanceName;

            var resources = new QuartzSchedulerResources
            {
                Name = instanceName,
                InstanceId = options.InstanceId,
                IdleWaitTime = options.IdleWaitTime,
                MaxBatchSize = options.MaxBatchSize,
                BatchTimeWindow = options.BatchTriggerAcquisitionFireAheadTimeWindow,
                ShutdownJobInterruption = options.ShutdownJobInterruption,
                TimeProvider = provider.GetSchedulerTimeProvider(key),
                LoggerFactory = provider.GetSchedulerLoggerFactory(),
                Meters = provider.GetRequiredService<Meters>(),
                ThreadPool = provider.GetScheduler<IThreadPool>(key),
                JobStore = provider.GetScheduler<IJobStore>(key),
                JobRunShellFactory = provider.GetScheduler<IJobRunShellFactory>(key),
                SchedulerRepository = provider.GetRequiredService<ISchedulerRepository>(),
            };

            // Plugins are added by the scheduler factory rather than here, because those named by
            // configuration are only knowable once the container exists.
            return resources;
        });

        services.TryAddKeyed<QuartzScheduler>(key, static (provider, key) => new QuartzScheduler(
            provider.GetScheduler<QuartzSchedulerResources>(key),
            provider.GetSchedulerTimeProvider(key)));

        // The jobs, triggers, listeners and calendars a scheduler should carry are per-scheduler, so these
        // are keyed too, and are handed this scheduler's own key, content and properties rather than
        // resolving them unkeyed — which for a named scheduler would be the default scheduler's.
        services.TryAddKeyed<ContainerConfigurationProcessor>(key, static (provider, key) =>
            ActivatorUtilities.CreateInstance<ContainerConfigurationProcessor>(
                Scoped(provider, key),
                provider.GetSchedulerOptions<QuartzOptions>(key),
                provider.GetSchedulerServices<ISchedulerContent>(key).ToArray()));

        services.TryAddKeyed<SchedulerContentInitializer>(key, static (provider, key) =>
            ActivatorUtilities.CreateInstance<SchedulerContentInitializer>(
                Scoped(provider, key),
                new SchedulerKey(key),
                provider.GetSchedulerProperties(key as string ?? Options.DefaultName),
                provider.GetScheduler<ContainerConfigurationProcessor>(key)));

        services.TryAddKeyed<ISchedulerFactory>(key, static (provider, key) =>
            ActivatorUtilities.CreateInstance<DefaultSchedulerFactory>(Scoped(provider, key), new SchedulerKey(key)));

        // The scheduler itself, as an ordinary service. Building one is asynchronous and a container
        // constructs synchronously, so what is registered is a handle that builds it on first use — see
        // DeferredScheduler. The default scheduler is unkeyed, so GetRequiredService<IScheduler>() means
        // it; a named scheduler is keyed by its name, which is what makes
        // [FromKeyedServices("reporting")] IScheduler work.
        services.TryAddKeyed<IScheduler>(key, static (provider, key) => new DeferredScheduler(
            provider.GetScheduler<ISchedulerFactory>(key),
            provider.GetRequiredService<IOptionsMonitor<QuartzSchedulerOptions>>(),
            new SchedulerKey(key)));

        return services;
    }

    /// <summary>
    /// Registers a service either keyed or unkeyed depending on whether a key was given, so callers do
    /// not have to branch on "is this the default scheduler" at every registration.
    /// </summary>
    private static void TryAddKeyed<TService>(
        this IServiceCollection services,
        object? key,
        Func<IServiceProvider, object?, TService> factory) where TService : class
    {
        if (key is null)
        {
            services.TryAddSingleton(provider => factory(provider, null));
        }
        else
        {
            services.TryAddKeyedSingleton(key, (provider, serviceKey) => factory(provider, serviceKey));
        }
    }

    /// <summary>
    /// Returns a provider that resolves this scheduler's parts rather than the default scheduler's.
    /// </summary>
    private static IServiceProvider Scoped(IServiceProvider provider, object? key)
    {
        return SchedulerScopedServiceProvider.For(provider, key);
    }

    /// <summary>
    /// Resolves a scheduler-scoped service, treating a <see langword="null"/> key as the default
    /// scheduler's unkeyed registration.
    /// </summary>
    internal static T GetScheduler<T>(this IServiceProvider provider, object? key) where T : notnull
    {
        return key is null ? provider.GetRequiredService<T>() : provider.GetRequiredKeyedService<T>(key);
    }

    /// <summary>
    /// Resolves a scheduler-scoped service that a scheduler may not have, treating a
    /// <see langword="null"/> key as the default scheduler's unkeyed registration.
    /// </summary>
    internal static T? GetSchedulerService<T>(this IServiceProvider provider, object? key) where T : class
    {
        return key is null ? provider.GetService<T>() : provider.GetKeyedService<T>(key);
    }

    /// <summary>
    /// Resolves all scheduler-scoped services of a type, treating a <see langword="null"/> key as the
    /// default scheduler's unkeyed registrations.
    /// </summary>
    internal static IEnumerable<T> GetSchedulerServices<T>(this IServiceProvider provider, object? key)
    {
        return key is null ? provider.GetServices<T>() : provider.GetKeyedServices<T>(key);
    }

    /// <summary>
    /// Resolves the factory a scheduler's own parts create their loggers from.
    /// </summary>
    /// <remarks>
    /// <see cref="AddQuartzSharedServices" /> calls <c>AddLogging()</c>, so this is the application's
    /// factory in every container Quartz assembles. The fallback is for a container put together by
    /// hand that has none: logging through <see cref="LogProvider" /> is what those components did
    /// before they were injected a factory at all, so it is what they keep doing rather than falling
    /// silent.
    /// </remarks>
    internal static ILoggerFactory GetSchedulerLoggerFactory(this IServiceProvider provider)
    {
        return provider.GetService<ILoggerFactory>() ?? LogProviderLoggerFactory.Instance;
    }

    /// <summary>
    /// Resolves the clock a scheduler runs on.
    /// </summary>
    /// <remarks>
    /// Three answers in order of how specifically they were meant: the one this scheduler was given with
    /// <c>UseTimeProvider</c>, the one the container holds, and the system clock. A named scheduler
    /// therefore inherits an application-wide clock without having to be told about it, while a clock
    /// given to one scheduler stays that scheduler's.
    /// </remarks>
    internal static TimeProvider GetSchedulerTimeProvider(this IServiceProvider provider, object? key)
    {
        return (key is null ? null : provider.GetKeyedService<TimeProvider>(key))
            ?? provider.GetService<TimeProvider>()
            ?? TimeProvider.System;
    }

    /// <summary>
    /// Resolves the options belonging to a scheduler. The service key and the options name are the same
    /// string, so a scheduler's registrations and its configuration always agree.
    /// </summary>
    internal static T GetSchedulerOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(
        this IServiceProvider provider,
        object? key) where T : class
    {
        var name = key as string ?? Options.DefaultName;
        return provider.GetRequiredService<IOptionsMonitor<T>>().Get(name);
    }

    /// <summary>
    /// Returns the flat property bag a scheduler was configured with, or an empty one when the scheduler
    /// was configured entirely in code.
    /// </summary>
    internal static NameValueCollection GetSchedulerProperties(this IServiceProvider provider, string optionsName)
    {
        var options = provider.GetService<IOptionsMonitor<QuartzOptions>>();
        return options is null ? [] : QuartzConfigurationHelper.ToNameValueCollection(options.Get(optionsName).Properties);
    }

}

/// <summary>
/// Identifies which scheduler a service belongs to. Injected into components that need to resolve
/// their siblings, so they ask for the right scheduler's parts rather than the default one's.
/// </summary>
internal sealed record SchedulerKey(object? Key)
{
    /// <summary>
    /// The options name matching this scheduler.
    /// </summary>
    public string OptionsName => Key as string ?? Options.DefaultName;
}
