using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Diagnostics;
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
    private IConfiguration? configuration;

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
    /// This is the code-free path a properties file or an environment-derived bag takes, and the
    /// standalone counterpart of <c>AddQuartz(properties)</c>. The keys are translated into the same
    /// typed options and registrations everything else produces, so a scheduler configured this way is
    /// the same scheduler.
    /// </para>
    /// <para>
    /// The parameter is the shape every dictionary already has, so a
    /// <see cref="Dictionary{TKey,TValue}"/>, an <see cref="IReadOnlyDictionary{TKey,TValue}"/> and
    /// <see cref="QuartzOptions.Properties"/> all go in without a conversion step.
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
    public QuartzSchedulerBuilder UseProperties(IEnumerable<KeyValuePair<string, string?>> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return UsePropertyBag(QuartzConfigurationHelper.ToNameValueCollection(properties));
    }

    /// <summary>
    /// Configures the scheduler from flat <c>quartz.*</c> property keys held in a
    /// <see cref="NameValueCollection"/>.
    /// </summary>
    /// <remarks>
    /// A <see cref="NameValueCollection"/> is what a caller migrating from 3.x already holds — it is what
    /// <c>StdSchedulerFactory</c> took — so it stays a single call. Everything else about it is
    /// <see cref="UseProperties(IEnumerable{KeyValuePair{string, string}})"/>, which this forwards to.
    /// </remarks>
    /// <param name="properties">The flat <c>quartz.*</c> properties.</param>
    public QuartzSchedulerBuilder UseProperties(NameValueCollection properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        // Copied, so a caller that goes on to reuse its collection cannot change what this scheduler
        // was configured with after the fact.
        return UsePropertyBag(new NameValueCollection(properties));
    }

    private QuartzSchedulerBuilder UsePropertyBag(NameValueCollection properties)
    {
        LegacyPropertyKeys.Validate(properties);
        this.properties = properties;
        return this;
    }

    /// <summary>
    /// Configures the scheduler from a configuration section, the standalone counterpart of
    /// <c>AddQuartz(configuration)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The section is read exactly as it is under a host: hierarchical sections such as
    /// <c>Scheduler</c> and <c>ThreadPool</c> bind onto the typed options, a <c>Schedule</c> section
    /// becomes jobs and triggers, and flat <c>quartz.*</c> keys still mean what they always did. There
    /// is no flattening step for a caller to write.
    /// </para>
    /// <para>
    /// Configuration written in code wins, as it does everywhere else: the section is applied before
    /// anything the builder was told.
    /// </para>
    /// </remarks>
    /// <param name="configuration">
    /// The Quartz configuration section, typically <c>configuration.GetSection("Quartz")</c>.
    /// </param>
    public QuartzSchedulerBuilder UseConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        this.configuration = configuration;
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

        BridgeLoggingToLogProvider();

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
    /// <remarks>
    /// The factory that owns the container is dropped, so the container outlives every reference to it:
    /// shutting this scheduler down is still <see cref="IScheduler.Shutdown"/>, but nothing will ever
    /// dispose what built it. That is the right trade for a scheduler that lives as long as the process
    /// and the wrong one for anything shorter-lived — use <see cref="Build"/> and keep the factory there.
    /// </remarks>
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
    /// Applying them where <c>UseProperties</c> was called would instead make precedence depend
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
        NameValueCollection configured = [];
        ServiceCollection seed = [];

        if (configuration is not null)
        {
            // The typed binder first, then everything the section says in flat form — the same pair, in
            // the same order, that AddQuartz(configuration) applies. Every section is flattened,
            // including the ones that also bind, so a setting that has no options type of its own is
            // still read.
            seed.BindQuartzOptions(configuration);
            JsonSchedulingHelper.ConfigureOptionsFromConfiguration(seed, configuration, Options.DefaultName);
            QuartzConfigurationHelper.PopulateProperties(configuration, configured);
        }

        foreach (var key in properties?.AllKeys ?? [])
        {
            if (key is not null)
            {
                // A bag handed in by hand is the more specific of the two, so it wins where both speak.
                configured[key] = properties![key];
            }
        }

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

    /// <summary>
    /// Points this container's logging at <see cref="LogProvider" /> when the caller configured none of
    /// its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything Quartz builds is injected an <see cref="ILogger" /> from the container. That is what a
    /// host is for, and a host's container has the application's logging providers in it. This container
    /// is one the builder created, and a standalone application says where its logging goes by calling
    /// <see cref="LogProvider.SetLogProvider" /> — so without this, every injected logger here would be
    /// a real logger writing to a factory with nothing behind it.
    /// </para>
    /// <para>
    /// Skipped as soon as a logging provider has been registered on <see cref="Services" />, because
    /// then the caller has said where logging goes and the container's own factory is the answer.
    /// </para>
    /// </remarks>
    private void BridgeLoggingToLogProvider()
    {
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(ILoggerProvider)))
        {
            return;
        }

        // Replace rather than TryAdd: a caller who called AddLogging() without adding a provider has
        // already registered the factory this stands in for.
        services.Replace(ServiceDescriptor.Singleton<ILoggerFactory>(LogProviderLoggerFactory.Instance));
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

    /// <inheritdoc cref="IQuartzBuilder.UseThreadPool{T}()" />
    public QuartzSchedulerBuilder UseThreadPool<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, IThreadPool
    {
        inner.UseThreadPool<T>();
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

    /// <inheritdoc cref="IQuartzBuilder.UseJobStore{T}()" />
    public QuartzSchedulerBuilder UseJobStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, IJobStore
    {
        inner.UseJobStore<T>();
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.UseJobStore{T, TOptions}(Action{TOptions})" />
    public QuartzSchedulerBuilder UseJobStore<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        Action<TOptions>? configure = null)
        where T : class, IJobStore
        where TOptions : class
    {
        inner.UseJobStore<T, TOptions>(configure);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.UseJobStore(Func{IServiceProvider, IJobStore})" />
    public QuartzSchedulerBuilder UseJobStore(Func<IServiceProvider, IJobStore> factory)
    {
        inner.UseJobStore(factory);
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

    /// <inheritdoc cref="IQuartzBuilder.UseInstanceIdGenerator{T}()" />
    public QuartzSchedulerBuilder UseInstanceIdGenerator<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, IInstanceIdGenerator
    {
        inner.UseInstanceIdGenerator<T>();
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.UseInstanceIdGenerator{T, TOptions}(Action{TOptions})" />
    public QuartzSchedulerBuilder UseInstanceIdGenerator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        Action<TOptions>? configure = null)
        where T : class, IInstanceIdGenerator
        where TOptions : class
    {
        inner.UseInstanceIdGenerator<T, TOptions>(configure);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.UseInstanceIdGenerator(IInstanceIdGenerator)" />
    public QuartzSchedulerBuilder UseInstanceIdGenerator(IInstanceIdGenerator generator)
    {
        inner.UseInstanceIdGenerator(generator);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.UseTimeProvider" />
    public QuartzSchedulerBuilder UseTimeProvider(TimeProvider timeProvider)
    {
        inner.UseTimeProvider(timeProvider);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.ConfigureOptions{TOptions}" />
    public QuartzSchedulerBuilder ConfigureOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        Action<TOptions>? configure = null) where TOptions : class
    {
        inner.ConfigureOptions(configure);
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

    /// <inheritdoc cref="IQuartzBuilder.AddJobListener{T}(System.Collections.Generic.IReadOnlyCollection{Quartz.IMatcher{Quartz.JobKey}})" />
    public QuartzSchedulerBuilder AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        params IReadOnlyCollection<IMatcher<JobKey>> matchers) where T : class, IJobListener
    {
        inner.AddJobListener<T>(matchers);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.AddJobListener{T}(T, System.Collections.Generic.IReadOnlyCollection{Quartz.IMatcher{Quartz.JobKey}})" />
    public QuartzSchedulerBuilder AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T listener, params IReadOnlyCollection<IMatcher<JobKey>> matchers) where T : class, IJobListener
    {
        inner.AddJobListener(listener, matchers);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.AddJobListener{T}(Func{IServiceProvider, T}, System.Collections.Generic.IReadOnlyCollection{Quartz.IMatcher{Quartz.JobKey}})" />
    public QuartzSchedulerBuilder AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory, params IReadOnlyCollection<IMatcher<JobKey>> matchers) where T : class, IJobListener
    {
        inner.AddJobListener(factory, matchers);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.AddTriggerListener{T}(System.Collections.Generic.IReadOnlyCollection{Quartz.IMatcher{Quartz.TriggerKey}})" />
    public QuartzSchedulerBuilder AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        params IReadOnlyCollection<IMatcher<TriggerKey>> matchers) where T : class, ITriggerListener
    {
        inner.AddTriggerListener<T>(matchers);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.AddTriggerListener{T}(T, System.Collections.Generic.IReadOnlyCollection{Quartz.IMatcher{Quartz.TriggerKey}})" />
    public QuartzSchedulerBuilder AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T listener, params IReadOnlyCollection<IMatcher<TriggerKey>> matchers) where T : class, ITriggerListener
    {
        inner.AddTriggerListener(listener, matchers);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.AddTriggerListener{T}(Func{IServiceProvider, T}, System.Collections.Generic.IReadOnlyCollection{Quartz.IMatcher{Quartz.TriggerKey}})" />
    public QuartzSchedulerBuilder AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory, params IReadOnlyCollection<IMatcher<TriggerKey>> matchers) where T : class, ITriggerListener
    {
        inner.AddTriggerListener(factory, matchers);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.AddJobMiddleware{T}()" />
    public QuartzSchedulerBuilder AddJobMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, IJobExecutionMiddleware
    {
        inner.AddJobMiddleware<T>();
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.AddJobMiddleware{T}(Func{IServiceProvider, T})" />
    public QuartzSchedulerBuilder AddJobMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory) where T : class, IJobExecutionMiddleware
    {
        inner.AddJobMiddleware(factory);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.AddJobMiddleware{T}(T)" />
    public QuartzSchedulerBuilder AddJobMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T middleware) where T : class, IJobExecutionMiddleware
    {
        inner.AddJobMiddleware(middleware);
        return this;
    }

    /// <inheritdoc cref="IQuartzBuilder.UseExecutionLimits" />
    public QuartzSchedulerBuilder UseExecutionLimits(Action<ExecutionLimitsBuilder> configure)
    {
        inner.UseExecutionLimits(configure);
        return this;
    }

    // The extension half. Every IQuartzBuilder extension in QuartzBuilderExtensions is mirrored here
    // as an instance method returning this type, for the same reason the interface members above are:
    // an extension method cannot preserve the receiver's type, because the ones that matter take an
    // explicit type argument of their own (AddJob<MyJob>) and C# has no partial type-argument
    // inference — neither AddJob<TBuilder, TJob> nor an extension<TBuilder> block can be called as
    // AddJob<MyJob>. An instance method wins over an extension method, so this is what keeps
    // Create()…AddJob<MyJob>(…)…BuildScheduler() a single expression. QuartzBuilderExtensionsMirrorTest
    // fails when an extension is added without one.

    /// <inheritdoc cref="QuartzBuilderExtensions.UseSimpleTypeLoader" />
    public QuartzSchedulerBuilder UseSimpleTypeLoader()
    {
        inner.UseSimpleTypeLoader();
        return this;
    }

    /// <inheritdoc cref="QuartzBuilderExtensions.ConfigureJobScope" />
    public QuartzSchedulerBuilder ConfigureJobScope(Action<IServiceScope, TriggerFiredBundle, IScheduler> configure)
    {
        inner.ConfigureJobScope(configure);
        return this;
    }

    /// <inheritdoc cref="QuartzBuilderExtensions.AddJob{T}(IQuartzBuilder, Action{IJobConfigurator{T}})" />
    public QuartzSchedulerBuilder AddJob<
            [DynamicallyAccessedMembers(JobTypeMembers.Required)]
    T>(Action<IJobConfigurator<T>> configure) where T : IJob
    {
        inner.AddJob(configure);
        return this;
    }

    /// <inheritdoc cref="QuartzBuilderExtensions.AddJob{T}(IQuartzBuilder, Action{IServiceProvider, IJobConfigurator{T}})" />
    public QuartzSchedulerBuilder AddJob<
            [DynamicallyAccessedMembers(JobTypeMembers.Required)]
    T>(Action<IServiceProvider, IJobConfigurator<T>> configure) where T : IJob
    {
        inner.AddJob(configure);
        return this;
    }

    /// <inheritdoc cref="QuartzBuilderExtensions.AddJob(IQuartzBuilder, Type, Action{IJobConfigurator{IJob}})" />
    public QuartzSchedulerBuilder AddJob(
           [DynamicallyAccessedMembers(JobTypeMembers.Required)]
        Type jobType,
        Action<IJobConfigurator<IJob>> configure)
    {
        inner.AddJob(jobType, configure);
        return this;
    }

    /// <inheritdoc cref="QuartzBuilderExtensions.AddJob(IQuartzBuilder, Type, Action{IServiceProvider, IJobConfigurator{IJob}})" />
    public QuartzSchedulerBuilder AddJob(
           [DynamicallyAccessedMembers(JobTypeMembers.Required)]
        Type jobType,
        Action<IServiceProvider, IJobConfigurator<IJob>> configure)
    {
        inner.AddJob(jobType, configure);
        return this;
    }

    /// <inheritdoc cref="QuartzBuilderExtensions.AddTrigger{TJob}(IQuartzBuilder, Action{ITriggerConfigurator{TJob}})" />
    public QuartzSchedulerBuilder AddTrigger<[DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob>(
        Action<ITriggerConfigurator<TJob>> configure) where TJob : IJob
    {
        inner.AddTrigger(configure);
        return this;
    }

    /// <inheritdoc cref="QuartzBuilderExtensions.AddTrigger{TJob}(IQuartzBuilder, Action{IServiceProvider, ITriggerConfigurator{TJob}})" />
    public QuartzSchedulerBuilder AddTrigger<[DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob>(
        Action<IServiceProvider, ITriggerConfigurator<TJob>> configure) where TJob : IJob
    {
        inner.AddTrigger(configure);
        return this;
    }

    /// <inheritdoc cref="QuartzBuilderExtensions.AddTrigger(IQuartzBuilder, Action{ITriggerConfigurator{IJob}})" />
    public QuartzSchedulerBuilder AddTrigger(Action<ITriggerConfigurator<IJob>> configure)
    {
        inner.AddTrigger(configure);
        return this;
    }

    /// <inheritdoc cref="QuartzBuilderExtensions.AddTrigger(IQuartzBuilder, Action{IServiceProvider, ITriggerConfigurator{IJob}})" />
    public QuartzSchedulerBuilder AddTrigger(Action<IServiceProvider, ITriggerConfigurator<IJob>> configure)
    {
        inner.AddTrigger(configure);
        return this;
    }

    /// <inheritdoc cref="QuartzBuilderExtensions.ScheduleJob{T}(IQuartzBuilder, Action{ITriggerConfigurator{T}}, Action{IJobConfigurator{T}})" />
    public QuartzSchedulerBuilder ScheduleJob<
            [DynamicallyAccessedMembers(JobTypeMembers.Required)]
    T>(
        Action<ITriggerConfigurator<T>> trigger,
        Action<IJobConfigurator<T>>? job = null) where T : IJob
    {
        inner.ScheduleJob(trigger, job);
        return this;
    }

    /// <inheritdoc cref="QuartzBuilderExtensions.ScheduleJob{T}(IQuartzBuilder, Action{IServiceProvider, ITriggerConfigurator{T}}, Action{IServiceProvider, IJobConfigurator{T}})" />
    public QuartzSchedulerBuilder ScheduleJob<
            [DynamicallyAccessedMembers(JobTypeMembers.Required)]
    T>(
        Action<IServiceProvider, ITriggerConfigurator<T>> trigger,
        Action<IServiceProvider, IJobConfigurator<T>>? job = null) where T : IJob
    {
        inner.ScheduleJob(trigger, job);
        return this;
    }

    /// <inheritdoc cref="QuartzBuilderExtensions.AddJobType{TJob}(IQuartzBuilder)" />
    public QuartzSchedulerBuilder AddJobType<
            [DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob>()
        where TJob : class, IJob
    {
        inner.AddJobType<TJob>();
        return this;
    }

    /// <inheritdoc cref="QuartzBuilderExtensions.AddJobType{TJob}(IQuartzBuilder, ServiceLifetime)" />
    public QuartzSchedulerBuilder AddJobType<
            [DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob>(
        ServiceLifetime lifetime) where TJob : class, IJob
    {
        inner.AddJobType<TJob>(lifetime);
        return this;
    }

    /// <inheritdoc cref="QuartzBuilderExtensions.AddJobType{TJob, TImplementation}(IQuartzBuilder)" />
    public QuartzSchedulerBuilder AddJobType<
            TJob,
            [DynamicallyAccessedMembers(JobTypeMembers.Required)] TImplementation>()
        where TJob : class, IJob
        where TImplementation : class, TJob
    {
        inner.AddJobType<TJob, TImplementation>();
        return this;
    }

    /// <inheritdoc cref="QuartzBuilderExtensions.AddJobType{TJob, TImplementation}(IQuartzBuilder, ServiceLifetime)" />
    public QuartzSchedulerBuilder AddJobType<
            TJob,
            [DynamicallyAccessedMembers(JobTypeMembers.Required)] TImplementation>(
        ServiceLifetime lifetime)
        where TJob : class, IJob
        where TImplementation : class, TJob
    {
        inner.AddJobType<TJob, TImplementation>(lifetime);
        return this;
    }

    /// <inheritdoc cref="QuartzBuilderExtensions.AddJobType{TJob}(IQuartzBuilder, Func{IServiceProvider, TJob})" />
    public QuartzSchedulerBuilder AddJobType<TJob>(
        Func<IServiceProvider, TJob> implementationFactory) where TJob : class, IJob
    {
        inner.AddJobType(implementationFactory);
        return this;
    }

    /// <inheritdoc cref="QuartzBuilderExtensions.AddJobType{TJob}(IQuartzBuilder, Func{IServiceProvider, TJob}, ServiceLifetime)" />
    public QuartzSchedulerBuilder AddJobType<TJob>(
        Func<IServiceProvider, TJob> implementationFactory,
        ServiceLifetime lifetime) where TJob : class, IJob
    {
        inner.AddJobType(implementationFactory, lifetime);
        return this;
    }

    /// <inheritdoc cref="QuartzBuilderExtensions.AddCalendar{T}(IQuartzBuilder, string, AddCalendarOptions, Action{T})" />
    public QuartzSchedulerBuilder AddCalendar<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        string name,
        AddCalendarOptions options = default,
        Action<T>? configure = null) where T : ICalendar, new()
    {
        inner.AddCalendar(name, options, configure);
        return this;
    }

    /// <inheritdoc cref="QuartzBuilderExtensions.AddCalendar{T}(IQuartzBuilder, string, AddCalendarOptions, Action{IServiceProvider, T})" />
    public QuartzSchedulerBuilder AddCalendar<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        string name,
        AddCalendarOptions options,
        Action<IServiceProvider, T> configure) where T : ICalendar, new()
    {
        inner.AddCalendar(name, options, configure);
        return this;
    }

    /// <inheritdoc cref="QuartzBuilderExtensions.AddCalendar(IQuartzBuilder, string, ICalendar, AddCalendarOptions)" />
    public QuartzSchedulerBuilder AddCalendar(
        string name,
        ICalendar calendar,
        AddCalendarOptions options = default)
    {
        inner.AddCalendar(name, calendar, options);
        return this;
    }

    // The interface half. Implemented explicitly so the public members above can return this type
    // rather than IQuartzBuilder — the only way C# expresses a covariant return on an interface
    // implementation, and what lets Create()…BuildScheduler() be a single expression.

    IQuartzBuilder IQuartzBuilder.ConfigureScheduler(Action<QuartzSchedulerOptions> configure) => ConfigureScheduler(configure);

    IQuartzBuilder IQuartzBuilder.UseDefaultThreadPool(int maxConcurrency) => UseDefaultThreadPool(maxConcurrency);

    IQuartzBuilder IQuartzBuilder.UseDefaultThreadPool(Action<ThreadPoolOptions>? configure) => UseDefaultThreadPool(configure);

    IQuartzBuilder IQuartzBuilder.UseThreadPool<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>() => UseThreadPool<T>();

    IQuartzBuilder IQuartzBuilder.UseThreadPool(IThreadPool threadPool) => UseThreadPool(threadPool);

    IQuartzBuilder IQuartzBuilder.UseInMemoryStore(Action<InMemoryJobStoreOptions>? configure) => UseInMemoryStore(configure);

    IQuartzBuilder IQuartzBuilder.UseJobStore(IJobStore jobStore) => UseJobStore(jobStore);

    IQuartzBuilder IQuartzBuilder.UseJobStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>() => UseJobStore<T>();

    IQuartzBuilder IQuartzBuilder.UseJobStore<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        Action<TOptions>? configure) => UseJobStore<T, TOptions>(configure);

    IQuartzBuilder IQuartzBuilder.UseJobStore(Func<IServiceProvider, IJobStore> factory) => UseJobStore(factory);

    IQuartzBuilder IQuartzBuilder.UsePersistentStore(Action<IPersistentStoreBuilder> configure) => UsePersistentStore(configure);

    IQuartzBuilder IQuartzBuilder.UsePersistentStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Action<IPersistentStoreBuilder> configure) => UsePersistentStore<T>(configure);

    IQuartzBuilder IQuartzBuilder.UseJobFactory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>() => UseJobFactory<T>();

    IQuartzBuilder IQuartzBuilder.UseJobFactory(IJobFactory jobFactory) => UseJobFactory(jobFactory);

    IQuartzBuilder IQuartzBuilder.UseTypeLoader<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>() => UseTypeLoader<T>();

    IQuartzBuilder IQuartzBuilder.UseInstanceIdGenerator<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>() => UseInstanceIdGenerator<T>();

    IQuartzBuilder IQuartzBuilder.UseInstanceIdGenerator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        Action<TOptions>? configure) => UseInstanceIdGenerator<T, TOptions>(configure);

    IQuartzBuilder IQuartzBuilder.UseInstanceIdGenerator(IInstanceIdGenerator generator) => UseInstanceIdGenerator(generator);

    IQuartzBuilder IQuartzBuilder.UseTimeProvider(TimeProvider timeProvider) => UseTimeProvider(timeProvider);

    IQuartzBuilder IQuartzBuilder.ConfigureOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        Action<TOptions>? configure) => ConfigureOptions(configure);

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
        params IReadOnlyCollection<IMatcher<JobKey>> matchers) => AddJobListener<T>(matchers);

    IQuartzBuilder IQuartzBuilder.AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T listener, params IReadOnlyCollection<IMatcher<JobKey>> matchers) => AddJobListener(listener, matchers);

    IQuartzBuilder IQuartzBuilder.AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory, params IReadOnlyCollection<IMatcher<JobKey>> matchers) => AddJobListener(factory, matchers);

    IQuartzBuilder IQuartzBuilder.AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        params IReadOnlyCollection<IMatcher<TriggerKey>> matchers) => AddTriggerListener<T>(matchers);

    IQuartzBuilder IQuartzBuilder.AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T listener, params IReadOnlyCollection<IMatcher<TriggerKey>> matchers) => AddTriggerListener(listener, matchers);

    IQuartzBuilder IQuartzBuilder.AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory, params IReadOnlyCollection<IMatcher<TriggerKey>> matchers) => AddTriggerListener(factory, matchers);

    IQuartzBuilder IQuartzBuilder.AddJobMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>() => AddJobMiddleware<T>();

    IQuartzBuilder IQuartzBuilder.AddJobMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory) => AddJobMiddleware(factory);

    IQuartzBuilder IQuartzBuilder.AddJobMiddleware<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T middleware) => AddJobMiddleware(middleware);

    IQuartzBuilder IQuartzBuilder.UseExecutionLimits(Action<ExecutionLimitsBuilder> configure) => UseExecutionLimits(configure);
}
