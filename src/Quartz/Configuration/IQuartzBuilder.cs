using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Quartz.Configuration;
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
    /// <remarks>
    /// A pool of your own has options of its own: declare them with
    /// <see cref="ConfigureOptions{TOptions}"/> and take <c>IOptions&lt;TOptions&gt;</c> through its
    /// constructor. <see cref="ThreadPoolOptions"/> belongs to the built-in pools, so it is configured
    /// on <see cref="UseDefaultThreadPool(Action{ThreadPoolOptions})"/> where it is read.
    /// </remarks>
    IQuartzBuilder UseThreadPool<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, IThreadPool;

    /// <summary>
    /// Uses a thread pool the caller has already built.
    /// </summary>
    /// <remarks>
    /// For a pool that needs constructing with something the container cannot supply. A pool the
    /// container can build is better selected with <see cref="UseThreadPool{T}"/>, which lets it have
    /// dependencies of its own.
    /// </remarks>
    IQuartzBuilder UseThreadPool(IThreadPool threadPool);

    /// <summary>
    /// Uses the in-memory job store, which does not survive process restarts.
    /// </summary>
    /// <remarks>
    /// Takes an options object rather than a sub-builder, unlike
    /// <see cref="UsePersistentStore(Action{IPersistentStoreBuilder})"/>: an in-memory store is one
    /// component with a couple of settings, whereas a database-backed store is a composite that also has
    /// a data source, a driver delegate, a serializer and a lock handler to choose. The shape says which
    /// kind of thing is being configured.
    /// </remarks>
    IQuartzBuilder UseInMemoryStore(Action<InMemoryJobStoreOptions>? configure = null);

    /// <summary>
    /// Uses a job store the caller has already built.
    /// </summary>
    /// <remarks>
    /// For a store that needs constructing with something the container cannot supply. A store the
    /// container can build is better selected with <see cref="UseJobStore{T}"/>,
    /// <see cref="UsePersistentStore{T}"/> or <see cref="UseInMemoryStore"/>, which let it have
    /// dependencies of its own.
    /// </remarks>
    IQuartzBuilder UseJobStore(IJobStore jobStore);

    /// <summary>
    /// Uses a job store of your own, built by the container.
    /// </summary>
    /// <remarks>
    /// The seam for a store that keeps scheduling data somewhere Quartz has never heard of. It is
    /// constructed with this scheduler's own collaborators — its signaler, its serializer, its type
    /// loader — so a store written against them behaves the same under a named scheduler as under the
    /// default one. <see cref="UseInMemoryStore"/> and <see cref="UsePersistentStore{T}"/> remain the way
    /// to select the stores Quartz ships, since they configure them as well as choose them.
    /// </remarks>
    /// <typeparam name="T">The job store's type.</typeparam>
    IQuartzBuilder UseJobStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, IJobStore;

    /// <summary>
    /// Uses a job store of your own, with options of its own.
    /// </summary>
    /// <remarks>
    /// Sugar over <see cref="UseJobStore{T}"/> and <see cref="ConfigureOptions{TOptions}"/>: the options
    /// are declared as this scheduler's, so a store that takes <c>IOptions&lt;TOptions&gt;</c> is handed
    /// what was configured for the scheduler it belongs to rather than the unnamed instance.
    /// </remarks>
    /// <typeparam name="T">The job store's type.</typeparam>
    /// <typeparam name="TOptions">
    /// The store's options type. It is resolved through <c>IOptions&lt;TOptions&gt;</c>, so it must keep
    /// its public parameterless constructor when the application is trimmed.
    /// </typeparam>
    /// <param name="configure">Configures the store's options.</param>
    IQuartzBuilder UseJobStore<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        Action<TOptions>? configure = null)
        where T : class, IJobStore
        where TOptions : class;

    /// <summary>
    /// Uses a job store built by a factory of your own.
    /// </summary>
    /// <remarks>
    /// For a store that needs something the container cannot construct on its own — a decorator around
    /// another store, or one whose constructor takes values rather than services. The factory is given
    /// this scheduler's view of the container and runs once, when the scheduler is built.
    /// </remarks>
    /// <param name="factory">Builds the job store.</param>
    IQuartzBuilder UseJobStore(Func<IServiceProvider, IJobStore> factory);

    /// <summary>
    /// Uses a database-backed job store, so jobs and triggers survive restarts and can be clustered.
    /// </summary>
    /// <inheritdoc cref="UseInMemoryStore" path="/remarks" />
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
    /// Uses a specific type loader, which decides how type names are resolved.
    /// </summary>
    IQuartzBuilder UseTypeLoader<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, ITypeLoader;

    /// <summary>
    /// Uses a specific instance id generator, which names this node within a cluster.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Choosing a generator says the id is to be generated, so
    /// <see cref="QuartzSchedulerOptions.GenerateInstanceId"/> is set — the counterpart of writing
    /// <c>quartz.scheduler.instanceId = AUTO</c> beside the generator's type name. A generator that was
    /// chosen and then never called would be the configuration equivalent of silence.
    /// </para>
    /// <para>
    /// Only a clustered scheduler generates one: a scheduler that shares its database with nobody has
    /// nothing to tell itself apart from, so the generator is not called and the id stays
    /// <c>NON_CLUSTERED</c>.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The generator's type.</typeparam>
    IQuartzBuilder UseInstanceIdGenerator<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, IInstanceIdGenerator;

    /// <summary>
    /// Uses an instance id generator with options of its own.
    /// </summary>
    /// <inheritdoc cref="UseInstanceIdGenerator{T}()" path="/remarks" />
    /// <typeparam name="T">The generator's type.</typeparam>
    /// <typeparam name="TOptions">
    /// The generator's options type. It is resolved through <c>IOptions&lt;TOptions&gt;</c>, so it must
    /// keep its public parameterless constructor when the application is trimmed.
    /// </typeparam>
    /// <param name="configure">Configures the generator's options.</param>
    IQuartzBuilder UseInstanceIdGenerator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        Action<TOptions>? configure = null)
        where T : class, IInstanceIdGenerator
        where TOptions : class;

    /// <summary>
    /// Uses an instance id generator the caller has already built.
    /// </summary>
    /// <inheritdoc cref="UseInstanceIdGenerator{T}()" path="/remarks" />
    /// <param name="generator">The generator.</param>
    IQuartzBuilder UseInstanceIdGenerator(IInstanceIdGenerator generator);

    /// <summary>
    /// Uses a specific time provider. Useful for testing time-dependent scheduling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The clock belongs to this scheduler. Handing one to a named scheduler leaves the others on
    /// whatever they were using, which is what lets one scheduler in a container be driven by a fake
    /// clock while the rest keep real time.
    /// </para>
    /// <para>
    /// A scheduler that was not given one asks the container, and falls back to
    /// <see cref="TimeProvider.System"/>. In full, most specific first:
    /// </para>
    /// <list type="number">
    /// <item><description>the provider this scheduler was given here;</description></item>
    /// <item><description>a <see cref="TimeProvider"/> registered in the container;</description></item>
    /// <item><description>a <c>quartz.timeProvider.type</c> key, which loses to both — code beats
    /// strings here as it does everywhere else;</description></item>
    /// <item><description><see cref="TimeProvider.System"/>.</description></item>
    /// </list>
    /// </remarks>
    IQuartzBuilder UseTimeProvider(TimeProvider timeProvider);

    /// <summary>
    /// Configures an options type belonging to this scheduler, so a component of your own can have
    /// settings that follow the scheduler it was registered for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A component the container builds asks for <c>IOptions&lt;TOptions&gt;</c>, which by itself
    /// resolves the <em>unnamed</em> instance — so under <c>AddQuartz("reporting", …)</c> it would be
    /// handed the default scheduler's settings, or defaults. This says the type is a scheduler's own:
    /// the callback is registered under this scheduler's options name, and the type is declared so that
    /// resolving it through the scheduler hands back the named instance.
    /// </para>
    /// <para>
    /// Registered whether or not a callback is given: where the options come from is not something
    /// adding one should change. Calling it repeatedly is harmless — each callback is applied in turn,
    /// and the declaration is deduplicated.
    /// </para>
    /// <para>
    /// This is the mechanism <see cref="AddPlugin{T, TOptions}"/> is built from, available to every
    /// component: a thread pool, a job store, a lock handler, a listener, a job factory.
    /// </para>
    /// </remarks>
    /// <typeparam name="TOptions">
    /// The options type. It is resolved through <c>IOptions&lt;TOptions&gt;</c>, so it must keep its
    /// public parameterless constructor when the application is trimmed.
    /// </typeparam>
    /// <param name="configure">Configures the options.</param>
    IQuartzBuilder ConfigureOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        Action<TOptions>? configure = null) where TOptions : class
    {
        // A default implementation, so an IQuartzBuilder written outside Quartz keeps compiling. It is
        // the whole mechanism, not a stub: SchedulerName is the options name — Options.DefaultName is
        // the empty string, which is what SchedulerName is for the unnamed scheduler.
        if (configure is not null)
        {
            Services.Configure(SchedulerName, configure);
        }

        Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<SchedulerNamedOptions>(new SchedulerNamedOptions<TOptions>()));

        return this;
    }

    /// <summary>
    /// Adds a plugin, which extends the scheduler's behaviour for its whole lifetime.
    /// </summary>
    /// <remarks>
    /// The three shapes match the listener trio: the container builds the plugin, the caller builds it,
    /// or the caller configures options the plugin is given. <paramref name="name" /> is how the
    /// scheduler refers to the plugin, and some plugins derive persisted job and trigger keys from it —
    /// so it is part of the deployment's identity rather than a label, and it is also the name a
    /// <c>quartz.plugin.&lt;name&gt;.*</c> key configures the same plugin under. Left unset, the
    /// plugin's type name is used. Plugins shipped with Quartz use their conventional short name
    /// (<c>xml</c>, <c>json</c>) for that reason.
    /// </remarks>
    /// <typeparam name="T">The plugin's type.</typeparam>
    /// <param name="name">The name the scheduler knows the plugin by.</param>
    IQuartzBuilder AddPlugin<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        string? name = null)
        where T : class, ISchedulerPlugin;

    /// <summary>
    /// Adds a plugin the caller builds and configures.
    /// </summary>
    /// <inheritdoc cref="AddPlugin{T}(string)" path="/remarks" />
    /// <typeparam name="T">The plugin's type.</typeparam>
    /// <param name="factory">Builds the plugin.</param>
    /// <param name="name">The name the scheduler knows the plugin by.</param>
    IQuartzBuilder AddPlugin<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory,
        string? name = null) where T : class, ISchedulerPlugin;

    /// <summary>
    /// Adds a plugin with configuration of its own.
    /// </summary>
    /// <inheritdoc cref="AddPlugin{T}(string)" path="/remarks" />
    /// <typeparam name="T">The plugin's type.</typeparam>
    /// <typeparam name="TOptions">
    /// The plugin's options type, which it takes as a dependency. It is resolved through
    /// <c>IOptions&lt;TOptions&gt;</c>, so it must keep its public parameterless constructor when the
    /// application is trimmed.
    /// </typeparam>
    /// <param name="configure">Configures the plugin's options.</param>
    /// <param name="name">The name the scheduler knows the plugin by.</param>
    IQuartzBuilder AddPlugin<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        Action<TOptions>? configure = null,
        string? name = null)
        where T : class, ISchedulerPlugin
        where TOptions : class;

    /// <summary>
    /// Adds a scheduler listener the container builds.
    /// </summary>
    /// <remarks>
    /// The listener's shape is checked here, while the application is still writing its configuration.
    /// Every member of <see cref="ISchedulerListener" />, <see cref="IJobListener" /> and
    /// <see cref="ITriggerListener" /> has a default implementation, so a public method that carries a
    /// notification's name but not its signature still compiles — it just stops implementing anything,
    /// and the default runs in its place with nothing to say the method is dead. Such a listener is
    /// refused with a <see cref="SchedulerConfigException" /> naming the member and the signature it
    /// should have.
    /// <para>
    /// What is examined is <typeparamref name="T" />: for an instance overload that is the type the call
    /// was written with rather than the instance's own, so a listener handed over as its base type is
    /// checked as that base, and a factory overload declared as the interface has nothing to examine at
    /// all. Neither escapes — the listener's runtime type is checked again when it is attached to a
    /// scheduler, which is only a later moment to hear about it.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The listener's type.</typeparam>
    IQuartzBuilder AddSchedulerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, ISchedulerListener;

    /// <inheritdoc cref="AddSchedulerListener{T}()" path="/remarks" />
    IQuartzBuilder AddSchedulerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T listener) where T : class, ISchedulerListener;

    /// <inheritdoc cref="AddSchedulerListener{T}()" path="/remarks" />
    IQuartzBuilder AddSchedulerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory) where T : class, ISchedulerListener;

    /// <inheritdoc cref="AddSchedulerListener{T}()" path="/remarks" />
    IQuartzBuilder AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        params IReadOnlyCollection<IMatcher<JobKey>> matchers) where T : class, IJobListener;

    /// <inheritdoc cref="AddSchedulerListener{T}()" path="/remarks" />
    IQuartzBuilder AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T listener, params IReadOnlyCollection<IMatcher<JobKey>> matchers) where T : class, IJobListener;

    /// <inheritdoc cref="AddSchedulerListener{T}()" path="/remarks" />
    IQuartzBuilder AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory, params IReadOnlyCollection<IMatcher<JobKey>> matchers) where T : class, IJobListener;

    /// <inheritdoc cref="AddSchedulerListener{T}()" path="/remarks" />
    IQuartzBuilder AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        params IReadOnlyCollection<IMatcher<TriggerKey>> matchers) where T : class, ITriggerListener;

    /// <inheritdoc cref="AddSchedulerListener{T}()" path="/remarks" />
    IQuartzBuilder AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T listener, params IReadOnlyCollection<IMatcher<TriggerKey>> matchers) where T : class, ITriggerListener;

    /// <inheritdoc cref="AddSchedulerListener{T}()" path="/remarks" />
    IQuartzBuilder AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory, params IReadOnlyCollection<IMatcher<TriggerKey>> matchers) where T : class, ITriggerListener;

    /// <summary>
    /// Adds a middleware the container builds, which wraps every job this scheduler executes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Middleware is where a cross-cutting concern lives — a log scope, a tenant context, a timeout, a
    /// translation of what a library throws. A listener cannot do any of those: it is notified before
    /// and after the job rather than around it, so it can neither wrap the call, decline to make it, nor
    /// see what it threw. See <see cref="IJobExecutionMiddleware" /> for what a middleware may and may
    /// not decide.
    /// </para>
    /// <para>
    /// Middleware runs in registration order, outermost first, so the first registered is the first to
    /// see a firing and the last to see its result. Each call adds a stage: registering the same type
    /// twice puts it in the chain twice, the same way registering the same job twice schedules it twice.
    /// </para>
    /// <para>
    /// One instance is built per scheduler and shared by every firing, so a middleware must keep no
    /// per-firing state in a field. It is registered for this scheduler alone, like its listeners and
    /// its job store, so a named scheduler's middleware wraps only its own executions.
    /// </para>
    /// <para>
    /// That instance is built <em>from the container's root</em>, when the scheduler's resources are,
    /// so its constructor dependencies must be singletons. A scoped one throws
    /// <c>Cannot resolve scoped service … from root provider</c> where scope validation is on — the
    /// Host's default in Development — and becomes a captive dependency living as long as the scheduler
    /// where it is not. Take <c>IServiceScopeFactory</c> and open a scope inside <c>Invoke</c> instead,
    /// or read the firing's own scope through <see cref="IJobExecutionContextAccessor" />.
    /// </para>
    /// <para>
    /// A middleware registered through <c>ConfigureAllQuartzSchedulers</c> always composes <em>inside</em>
    /// one registered here, whichever call was written first: a scheduler's own callback runs before what
    /// every scheduler was told, so a library's wrapper sits within the application's.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The middleware's type.</typeparam>
    IQuartzBuilder AddJobMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, IJobExecutionMiddleware;

    /// <inheritdoc cref="AddJobMiddleware{T}()" path="/remarks" />
    /// <typeparam name="T">The middleware's type.</typeparam>
    /// <param name="factory">Builds the middleware from this scheduler's view of the container.</param>
    IQuartzBuilder AddJobMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory) where T : class, IJobExecutionMiddleware;

    /// <inheritdoc cref="AddJobMiddleware{T}()" path="/remarks" />
    /// <typeparam name="T">The middleware's type.</typeparam>
    /// <param name="middleware">The middleware, already built.</param>
    IQuartzBuilder AddJobMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T middleware) where T : class, IJobExecutionMiddleware;

    /// <summary>
    /// Configures execution group limits, so resource-hungry jobs cannot saturate every thread. Each
    /// limit is counted on this node or across the cluster, as its <see cref="ExecutionLimitScope"/> says.
    /// </summary>
    IQuartzBuilder UseExecutionLimits(Action<ExecutionLimitsBuilder> configure);
}
