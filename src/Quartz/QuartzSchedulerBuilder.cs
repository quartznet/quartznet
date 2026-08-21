using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;
using Quartz.Extensibility;

namespace Quartz;

/// <summary>
/// Builds a scheduler without an application-supplied dependency injection container.
/// </summary>
/// <remarks>
/// <para>
/// Console applications, tests and anything else without a host use this instead of registering Quartz
/// into their own container. It is not a second construction path and not a second configuration API:
/// it <em>is</em> an <see cref="IQuartzBuilder"/>, the same one <c>AddQuartz</c> hands out, over a
/// container it creates itself. Whatever works here works identically under a host, and the two cannot
/// drift apart, because there is only one set of members to keep in step.
/// </para>
/// <para>
/// What it adds is the pair of terminal methods a standalone caller needs: <see cref="Build"/> and
/// <see cref="BuildScheduler"/>. Every configuration member returns this type rather than the
/// interface, so a chain reaches them. <see cref="IQuartzBuilder"/> is implemented explicitly
/// underneath, which is how C# spells a covariant return on an interface implementation.
/// </para>
/// <para>
/// The builder owns the <see cref="IServiceProvider"/> it creates and disposes it when the returned
/// factory is disposed, so callers that never dispose behave exactly as they did with the old
/// process-lifetime scheduler.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var scheduler = await QuartzSchedulerBuilder.Create()
///     .ConfigureScheduler(options => options.InstanceName = "reporting")
///     .UseDefaultThreadPool(maxConcurrency: 20)
///     .UseInMemoryStore()
///     .BuildScheduler();
/// </code>
/// </example>
public sealed class QuartzSchedulerBuilder : IQuartzBuilder
{
    private readonly ServiceCollection services = [];
    private readonly QuartzBuilder inner;
    private NameValueCollection? properties;

    private QuartzSchedulerBuilder()
    {
        inner = new QuartzBuilder(services, schedulerKey: null);
    }

    /// <summary>
    /// Creates a new builder.
    /// </summary>
    public static QuartzSchedulerBuilder Create()
    {
        return new QuartzSchedulerBuilder();
    }

    /// <inheritdoc />
    public IServiceCollection Services => services;

    /// <inheritdoc />
    public string SchedulerName => "";

    /// <summary>
    /// Configures the scheduler from flat <c>quartz.*</c> property keys.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the code-free path a properties file or an environment-derived
    /// <see cref="NameValueCollection"/> takes, and the standalone counterpart of
    /// <c>AddQuartz(properties)</c>. The keys are translated into the same typed options and
    /// registrations everything else produces, so a scheduler configured this way is the same scheduler.
    /// </para>
    /// <para>
    /// Configuration written in code wins, whichever order the two are applied in: values from the
    /// properties are applied before anything the builder was told, and implementations they name are
    /// registered after — registration being first-wins and configuration last-wins.
    /// </para>
    /// <para>
    /// Keys are checked against the ones Quartz reads, so a misspelling is reported rather than
    /// silently ignored. Set <c>quartz.checkConfiguration</c> to <see langword="false"/> to allow keys
    /// of your own.
    /// </para>
    /// </remarks>
    /// <param name="properties">The flat <c>quartz.*</c> properties.</param>
    public QuartzSchedulerBuilder UseProperties(NameValueCollection properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        LegacyPropertyKeys.Validate(properties);

        // Copied, so a caller that goes on to reuse its collection cannot change what this scheduler
        // was configured with after the fact.
        this.properties = new NameValueCollection(properties);
        return this;
    }

    /// <summary>
    /// Builds the scheduler factory, along with the container backing it.
    /// </summary>
    /// <remarks>
    /// The returned factory owns that container, so disposing it shuts the scheduler down and disposes
    /// everything the container built. See <see cref="StandaloneSchedulerFactory"/>.
    /// </remarks>
    public StandaloneSchedulerFactory Build()
    {
        ApplyProperties();

        // Defaults last, so anything configured above replaces rather than loses to them.
        services.AddQuartzScheduler();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        return new StandaloneSchedulerFactory(provider);
    }

    /// <summary>
    /// Builds the scheduler.
    /// </summary>
    public ValueTask<IScheduler> BuildScheduler(CancellationToken cancellationToken = default)
    {
        return Build().GetScheduler(cancellationToken);
    }

    /// <summary>
    /// Applies the flat properties, ahead of the configuration written in code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two halves go in at opposite ends because their precedence rules are opposites. Options are
    /// last-wins, so the property-derived ones are inserted at the front of the collection and anything
    /// configured in code is applied over them. Registrations are first-wins, so the implementations the
    /// properties name are appended, and an implementation chosen in code beats one named by a string.
    /// Applying them where <see cref="UseProperties"/> was called would instead make precedence depend
    /// on the order the builder happened to be told things in.
    /// </para>
    /// <para>
    /// This runs even when no properties were given, because keys can also arrive by configuring
    /// <see cref="QuartzOptions.Properties"/> on <see cref="Services"/> — which is only readable once
    /// the container exists, and is what <c>ApplyFromQuartzOptions</c> is for.
    /// </para>
    /// </remarks>
    private void ApplyProperties()
    {
        NameValueCollection configured = properties ?? [];
        ServiceCollection seed = [];

        // Plugins, execution limits and scheduler content are read from QuartzOptions, so the property
        // bag has to be there as well as bound onto the typed options.
        seed.Configure<QuartzOptions>(options =>
        {
            foreach (var key in configured.AllKeys)
            {
                if (key is not null)
                {
                    options.Properties[key] = configured[key];
                }
            }
        });

        QuartzPropertyBridge.ApplyOptions(seed, configured);
        QuartzPropertyBridge.ApplyFromQuartzOptions(seed);

        for (var i = 0; i < seed.Count; i++)
        {
            services.Insert(i, seed[i]);
        }

        QuartzPropertyBridge.ApplyRegistrations(services, configured);
    }

    /// <inheritdoc cref="IQuartzBuilder.ConfigureScheduler" />
    public QuartzSchedulerBuilder ConfigureScheduler(Action<QuartzSchedulerOptions> configure)
    {
        inner.ConfigureScheduler(configure);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.UseDefaultThreadPool(int)" />
    public QuartzSchedulerBuilder UseDefaultThreadPool(int maxConcurrency)
    {
        inner.UseDefaultThreadPool(maxConcurrency);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.UseDefaultThreadPool(Action{ThreadPoolOptions})" />
    public QuartzSchedulerBuilder UseDefaultThreadPool(Action<ThreadPoolOptions>? configure = null)
    {
        inner.UseDefaultThreadPool(configure);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.UseThreadPool{T}(Action{ThreadPoolOptions})" />
    public QuartzSchedulerBuilder UseThreadPool<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Action<ThreadPoolOptions>? configure = null) where T : class, IThreadPool
    {
        inner.UseThreadPool<T>(configure);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.UseThreadPool(IThreadPool)" />
    public QuartzSchedulerBuilder UseThreadPool(IThreadPool threadPool)
    {
        inner.UseThreadPool(threadPool);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.UseInMemoryStore" />
    public QuartzSchedulerBuilder UseInMemoryStore(Action<InMemoryJobStoreOptions>? configure = null)
    {
        inner.UseInMemoryStore(configure);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.UseJobStore(IJobStore)" />
    public QuartzSchedulerBuilder UseJobStore(IJobStore jobStore)
    {
        inner.UseJobStore(jobStore);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.UsePersistentStore(Action{IPersistentStoreBuilder})" />
    public QuartzSchedulerBuilder UsePersistentStore(Action<IPersistentStoreBuilder> configure)
    {
        inner.UsePersistentStore(configure);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.UsePersistentStore{T}(Action{IPersistentStoreBuilder})" />
    public QuartzSchedulerBuilder UsePersistentStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Action<IPersistentStoreBuilder> configure) where T : class, IJobStore
    {
        inner.UsePersistentStore<T>(configure);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.UseJobFactory{T}()" />
    public QuartzSchedulerBuilder UseJobFactory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, IJobFactory
    {
        inner.UseJobFactory<T>();
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.UseJobFactory(IJobFactory)" />
    public QuartzSchedulerBuilder UseJobFactory(IJobFactory jobFactory)
    {
        inner.UseJobFactory(jobFactory);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.UseTypeLoader{T}()" />
    public QuartzSchedulerBuilder UseTypeLoader<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, ITypeLoader
    {
        inner.UseTypeLoader<T>();
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.UseTimeProvider" />
    public QuartzSchedulerBuilder UseTimeProvider(TimeProvider timeProvider)
    {
        inner.UseTimeProvider(timeProvider);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.AddPlugin{T}(string)" />
    public QuartzSchedulerBuilder AddPlugin<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        string? name = null)
        where T : class, ISchedulerPlugin
    {
        inner.AddPlugin<T>(name);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.AddPlugin{T}(Func{IServiceProvider, T}, string)" />
    public QuartzSchedulerBuilder AddPlugin<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory,
        string? name = null) where T : class, ISchedulerPlugin
    {
        inner.AddPlugin(factory, name);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.AddPlugin{T, TOptions}(Action{TOptions}, string)" />
    public QuartzSchedulerBuilder AddPlugin<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        Action<TOptions>? configure = null,
        string? name = null)
        where T : class, ISchedulerPlugin
        where TOptions : class
    {
        inner.AddPlugin<T, TOptions>(configure, name);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.AddSchedulerListener{T}()" />
    public QuartzSchedulerBuilder AddSchedulerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, ISchedulerListener
    {
        inner.AddSchedulerListener<T>();
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.AddSchedulerListener{T}(T)" />
    public QuartzSchedulerBuilder AddSchedulerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T listener) where T : class, ISchedulerListener
    {
        inner.AddSchedulerListener(listener);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.AddSchedulerListener{T}(Func{IServiceProvider, T})" />
    public QuartzSchedulerBuilder AddSchedulerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory) where T : class, ISchedulerListener
    {
        inner.AddSchedulerListener(factory);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.AddJobListener{T}(IMatcher{JobKey}[])" />
    public QuartzSchedulerBuilder AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        params IMatcher<JobKey>[] matchers) where T : class, IJobListener
    {
        inner.AddJobListener<T>(matchers);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.AddJobListener{T}(T, IMatcher{JobKey}[])" />
    public QuartzSchedulerBuilder AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T listener, params IMatcher<JobKey>[] matchers) where T : class, IJobListener
    {
        inner.AddJobListener(listener, matchers);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.AddJobListener{T}(Func{IServiceProvider, T}, IMatcher{JobKey}[])" />
    public QuartzSchedulerBuilder AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory, params IMatcher<JobKey>[] matchers) where T : class, IJobListener
    {
        inner.AddJobListener(factory, matchers);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.AddTriggerListener{T}(IMatcher{TriggerKey}[])" />
    public QuartzSchedulerBuilder AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        params IMatcher<TriggerKey>[] matchers) where T : class, ITriggerListener
    {
        inner.AddTriggerListener<T>(matchers);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.AddTriggerListener{T}(T, IMatcher{TriggerKey}[])" />
    public QuartzSchedulerBuilder AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T listener, params IMatcher<TriggerKey>[] matchers) where T : class, ITriggerListener
    {
        inner.AddTriggerListener(listener, matchers);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.AddTriggerListener{T}(Func{IServiceProvider, T}, IMatcher{TriggerKey}[])" />
    public QuartzSchedulerBuilder AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory, params IMatcher<TriggerKey>[] matchers) where T : class, ITriggerListener
    {
        inner.AddTriggerListener(factory, matchers);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.UseExecutionLimits" />
    public QuartzSchedulerBuilder UseExecutionLimits(Action<ExecutionLimitsBuilder> configure)
    {
        inner.UseExecutionLimits(configure);
        return this;
    }

    // The interface half. Implemented explicitly so the public members above can return this type
    // rather than IQuartzBuilder — the only way C# expresses a covariant return on an interface
    // implementation, and what lets Create()…BuildScheduler() be a single expression.

    IQuartzBuilder IQuartzBuilder.ConfigureScheduler(Action<QuartzSchedulerOptions> configure) => ConfigureScheduler(configure);

    IQuartzBuilder IQuartzBuilder.UseDefaultThreadPool(int maxConcurrency) => UseDefaultThreadPool(maxConcurrency);

    IQuartzBuilder IQuartzBuilder.UseDefaultThreadPool(Action<ThreadPoolOptions>? configure) => UseDefaultThreadPool(configure);

    IQuartzBuilder IQuartzBuilder.UseThreadPool<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Action<ThreadPoolOptions>? configure) => UseThreadPool<T>(configure);

    IQuartzBuilder IQuartzBuilder.UseThreadPool(IThreadPool threadPool) => UseThreadPool(threadPool);

    IQuartzBuilder IQuartzBuilder.UseInMemoryStore(Action<InMemoryJobStoreOptions>? configure) => UseInMemoryStore(configure);

    IQuartzBuilder IQuartzBuilder.UseJobStore(IJobStore jobStore) => UseJobStore(jobStore);

    IQuartzBuilder IQuartzBuilder.UsePersistentStore(Action<IPersistentStoreBuilder> configure) => UsePersistentStore(configure);

    IQuartzBuilder IQuartzBuilder.UsePersistentStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Action<IPersistentStoreBuilder> configure) => UsePersistentStore<T>(configure);

    IQuartzBuilder IQuartzBuilder.UseJobFactory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>() => UseJobFactory<T>();

    IQuartzBuilder IQuartzBuilder.UseJobFactory(IJobFactory jobFactory) => UseJobFactory(jobFactory);

    IQuartzBuilder IQuartzBuilder.UseTypeLoader<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>() => UseTypeLoader<T>();

    IQuartzBuilder IQuartzBuilder.UseTimeProvider(TimeProvider timeProvider) => UseTimeProvider(timeProvider);

    IQuartzBuilder IQuartzBuilder.AddPlugin<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        string? name) => AddPlugin<T>(name);

    IQuartzBuilder IQuartzBuilder.AddPlugin<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory,
        string? name) => AddPlugin(factory, name);

    IQuartzBuilder IQuartzBuilder.AddPlugin<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        Action<TOptions>? configure,
        string? name) => AddPlugin<T, TOptions>(configure, name);

    IQuartzBuilder IQuartzBuilder.AddSchedulerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>() => AddSchedulerListener<T>();

    IQuartzBuilder IQuartzBuilder.AddSchedulerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T listener) => AddSchedulerListener(listener);

    IQuartzBuilder IQuartzBuilder.AddSchedulerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory) => AddSchedulerListener(factory);

    IQuartzBuilder IQuartzBuilder.AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        params IMatcher<JobKey>[] matchers) => AddJobListener<T>(matchers);

    IQuartzBuilder IQuartzBuilder.AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T listener, params IMatcher<JobKey>[] matchers) => AddJobListener(listener, matchers);

    IQuartzBuilder IQuartzBuilder.AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory, params IMatcher<JobKey>[] matchers) => AddJobListener(factory, matchers);

    IQuartzBuilder IQuartzBuilder.AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        params IMatcher<TriggerKey>[] matchers) => AddTriggerListener<T>(matchers);

    IQuartzBuilder IQuartzBuilder.AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T listener, params IMatcher<TriggerKey>[] matchers) => AddTriggerListener(listener, matchers);

    IQuartzBuilder IQuartzBuilder.AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory, params IMatcher<TriggerKey>[] matchers) => AddTriggerListener(factory, matchers);

    IQuartzBuilder IQuartzBuilder.UseExecutionLimits(Action<ExecutionLimitsBuilder> configure) => UseExecutionLimits(configure);
}
