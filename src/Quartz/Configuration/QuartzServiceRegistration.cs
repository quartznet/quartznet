using System.Collections.Specialized;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Quartz.Core;
using Quartz.Impl;
using Quartz.Simpl;
using Quartz.Spi;
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

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ITypeLoadHelper, SimpleTypeLoadHelper>();
        services.TryAddSingleton<ISchedulerRepository, SchedulerRepository>();
        services.TryAddSingleton<IDbConnectionManager, DBConnectionManager>();

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

        services.TryAddKeyed<QuartzSchedulerResources>(key, static (provider, key) =>
        {
            var options = provider.GetSchedulerOptions<QuartzSchedulerOptions>(key);
            var instanceName = key as string ?? options.InstanceName;

            var resources = new QuartzSchedulerResources
            {
                Name = instanceName,
                InstanceId = options.InstanceId,
                ThreadName = options.ThreadName ?? $"{instanceName}_QuartzSchedulerThread",
                IdleWaitTime = options.IdleWaitTime,
                MaxBatchSize = options.MaxBatchSize,
                BatchTimeWindow = options.BatchTriggerAcquisitionFireAheadTimeWindow,
                MakeSchedulerThreadDaemon = options.MakeSchedulerThreadDaemon,
                InterruptJobsOnShutdown = options.InterruptJobsOnShutdown,
                InterruptJobsOnShutdownWithWait = options.InterruptJobsOnShutdownWithWait,
                TimeProvider = provider.GetRequiredService<TimeProvider>(),
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
            provider.GetRequiredService<TimeProvider>()));

        // The jobs, triggers and listeners a scheduler should carry are per-scheduler, so these are
        // keyed too. Handing them the resolved QuartzOptions keeps a named scheduler from picking up
        // the unnamed scheduler's content.
        services.TryAddKeyed<ContainerConfigurationProcessor>(key, static (provider, key) =>
            ActivatorUtilities.CreateInstance<ContainerConfigurationProcessor>(
                Scoped(provider, key), provider.GetSchedulerOptions<QuartzOptions>(key)));

        services.TryAddKeyed<SchedulerContentInitializer>(key, static (provider, key) =>
            ActivatorUtilities.CreateInstance<SchedulerContentInitializer>(
                Scoped(provider, key),
                provider.GetSchedulerOptions<QuartzOptions>(key),
                provider.GetScheduler<ContainerConfigurationProcessor>(key)));

        services.TryAddKeyed<ISchedulerFactory>(key, static (provider, key) =>
            ActivatorUtilities.CreateInstance<DefaultSchedulerFactory>(Scoped(provider, key), new SchedulerKey(key)));

        if (schedulerName is not null)
        {
            services.AddSingleton(new SchedulerRegistration(schedulerName));
        }

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
    /// Resolves all scheduler-scoped services of a type, treating a <see langword="null"/> key as the
    /// default scheduler's unkeyed registrations.
    /// </summary>
    internal static IEnumerable<T> GetSchedulerServices<T>(this IServiceProvider provider, object? key)
    {
        return key is null ? provider.GetServices<T>() : provider.GetKeyedServices<T>(key);
    }

    /// <summary>
    /// Resolves the options belonging to a scheduler. The service key and the options name are the same
    /// string, so a scheduler's registrations and its configuration always agree.
    /// </summary>
    internal static T GetSchedulerOptions<T>(this IServiceProvider provider, object? key) where T : class
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
        return options is null ? [] : options.Get(optionsName).ToNameValueCollection();
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

/// <summary>
/// Marks a named scheduler as registered, so the hosted service can start every scheduler in the
/// container without the container having to be searched for keys.
/// </summary>
internal sealed record SchedulerRegistration(string Name);
