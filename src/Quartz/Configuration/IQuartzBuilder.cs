using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;

namespace Quartz;

/// <summary>
/// Configures a scheduler and the services it is built from.
/// </summary>
/// <remarks>
/// <para>
/// Every member configures typed options or registers a service — nothing writes configuration
/// strings. The option names are the same words as the configuration keys, so
/// <c>Quartz:ThreadPool:MaxConcurrency</c> and <see cref="ThreadPoolOptions.MaxConcurrency"/> are the
/// same setting said two ways, and code-first and file-based configuration describe one vocabulary
/// rather than two.
/// </para>
/// <para>
/// Members return the builder so configuration can be chained.
/// </para>
/// </remarks>
public interface IQuartzBuilder
{
    /// <summary>
    /// The services this scheduler is built from. Register your own implementations here to replace
    /// the defaults — anything registered wins over Quartz's own registration.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// The name this scheduler was registered under, or an empty string for the default scheduler.
    /// </summary>
    /// <remarks>
    /// This is also the service key its components are registered under and the name of its options,
    /// so a named scheduler's registrations and configuration always agree.
    /// </remarks>
    string SchedulerName { get; }

    /// <summary>
    /// Configures the scheduler itself.
    /// </summary>
    IQuartzBuilder ConfigureScheduler(Action<QuartzSchedulerOptions> configure);

    /// <summary>
    /// Uses the default thread pool, limited to the given number of concurrently executing jobs.
    /// </summary>
    IQuartzBuilder UseDefaultThreadPool(int maxConcurrency);

    /// <summary>
    /// Uses the default thread pool.
    /// </summary>
    IQuartzBuilder UseDefaultThreadPool(Action<ThreadPoolOptions>? configure = null);

    /// <summary>
    /// Uses a specific thread pool implementation.
    /// </summary>
    IQuartzBuilder UseThreadPool<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Action<ThreadPoolOptions>? configure = null) where T : class, IThreadPool;

    /// <summary>
    /// Uses the in-memory job store, which does not survive process restarts.
    /// </summary>
    IQuartzBuilder UseInMemoryStore(Action<InMemoryJobStoreOptions>? configure = null);

    /// <summary>
    /// Uses a database-backed job store, so jobs and triggers survive restarts and can be clustered.
    /// </summary>
    IQuartzBuilder UsePersistentStore(Action<IPersistentStoreBuilder> configure);

    /// <summary>
    /// Uses a specific database-backed job store implementation.
    /// </summary>
    IQuartzBuilder UsePersistentStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Action<IPersistentStoreBuilder> configure) where T : class, IJobStore;

    /// <summary>
    /// Uses a specific job factory, which decides how job instances are produced.
    /// </summary>
    IQuartzBuilder UseJobFactory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, IJobFactory;

    /// <summary>
    /// Uses a job factory the caller has already built.
    /// </summary>
    IQuartzBuilder UseJobFactory(IJobFactory jobFactory);

    /// <summary>
    /// Uses a specific type load helper, which decides how type names are resolved.
    /// </summary>
    IQuartzBuilder UseTypeLoader<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, ITypeLoadHelper;

    /// <summary>
    /// Uses a specific time provider. Useful for testing time-dependent scheduling.
    /// </summary>
    IQuartzBuilder UseTimeProvider(TimeProvider timeProvider);

    /// <summary>
    /// Adds a plugin, which extends the scheduler's behaviour for its whole lifetime.
    /// </summary>
    IQuartzBuilder AddPlugin<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, ISchedulerPlugin;

    /// <summary>
    /// Adds a plugin the caller builds and configures.
    /// </summary>
    IQuartzBuilder AddPlugin(Func<IServiceProvider, ISchedulerPlugin> factory);

    /// <summary>
    /// Adds a plugin under a specific name, which the caller builds and configures.
    /// </summary>
    /// <remarks>
    /// The name is how the scheduler refers to the plugin, and some plugins derive persisted job and
    /// trigger keys from it — so it is part of the deployment's identity, not a label. Plugins shipped
    /// with Quartz use their conventional short name (<c>xml</c>, <c>json</c>) for that reason.
    /// </remarks>
    IQuartzBuilder AddPlugin<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        string name,
        Func<IServiceProvider, T> factory) where T : class, ISchedulerPlugin;

    /// <summary>
    /// Adds a plugin with configuration of its own.
    /// </summary>
    IQuartzBuilder AddPlugin<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T, TOptions>(
        Action<TOptions>? configure = null)
        where T : class, ISchedulerPlugin
        where TOptions : class;

    IQuartzBuilder AddSchedulerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, ISchedulerListener;

    IQuartzBuilder AddSchedulerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T listener) where T : class, ISchedulerListener;

    IQuartzBuilder AddSchedulerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory) where T : class, ISchedulerListener;

    IQuartzBuilder AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        params IMatcher<JobKey>[] matchers) where T : class, IJobListener;

    IQuartzBuilder AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T listener, params IMatcher<JobKey>[] matchers) where T : class, IJobListener;

    IQuartzBuilder AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory, params IMatcher<JobKey>[] matchers) where T : class, IJobListener;

    IQuartzBuilder AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        params IMatcher<TriggerKey>[] matchers) where T : class, ITriggerListener;

    IQuartzBuilder AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T listener, params IMatcher<TriggerKey>[] matchers) where T : class, ITriggerListener;

    IQuartzBuilder AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory, params IMatcher<TriggerKey>[] matchers) where T : class, ITriggerListener;

    /// <summary>
    /// Configures per-node execution group limits, so resource-hungry jobs cannot saturate every thread.
    /// </summary>
    IQuartzBuilder UseExecutionLimits(Action<ExecutionLimits> configure);
}
