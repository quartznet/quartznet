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
/// <see cref="BuildScheduler"/>. Configuration members return <see cref="IQuartzBuilder"/> and so cannot
/// be chained into them — hold the builder in a variable and build from it, the way
/// <c>WebApplicationBuilder</c> is used.
/// </para>
/// <para>
/// The builder owns the <see cref="IServiceProvider"/> it creates and disposes it when the returned
/// factory is disposed, so callers that never dispose behave exactly as they did with the old
/// process-lifetime scheduler.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var builder = QuartzSchedulerBuilder.Create();
/// builder.ConfigureScheduler(options => options.InstanceName = "reporting")
///     .UseDefaultThreadPool(maxConcurrency: 20)
///     .UseInMemoryStore();
///
/// var scheduler = await builder.BuildScheduler();
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

    /// <inheritdoc />
    public IQuartzBuilder ConfigureScheduler(Action<QuartzSchedulerOptions> configure) => inner.ConfigureScheduler(configure);

    /// <inheritdoc />
    public IQuartzBuilder UseDefaultThreadPool(int maxConcurrency) => inner.UseDefaultThreadPool(maxConcurrency);

    /// <inheritdoc />
    public IQuartzBuilder UseDefaultThreadPool(Action<ThreadPoolOptions>? configure = null) => inner.UseDefaultThreadPool(configure);

    /// <inheritdoc />
    public IQuartzBuilder UseThreadPool<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Action<ThreadPoolOptions>? configure = null) where T : class, IThreadPool => inner.UseThreadPool<T>(configure);

    /// <inheritdoc />
    public IQuartzBuilder UseThreadPool(IThreadPool threadPool) => inner.UseThreadPool(threadPool);

    /// <inheritdoc />
    public IQuartzBuilder UseInMemoryStore(Action<InMemoryJobStoreOptions>? configure = null) => inner.UseInMemoryStore(configure);

    /// <inheritdoc />
    public IQuartzBuilder UseJobStore(IJobStore jobStore) => inner.UseJobStore(jobStore);

    /// <inheritdoc />
    public IQuartzBuilder UsePersistentStore(Action<IPersistentStoreBuilder> configure) => inner.UsePersistentStore(configure);

    /// <inheritdoc />
    public IQuartzBuilder UsePersistentStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Action<IPersistentStoreBuilder> configure) where T : class, IJobStore => inner.UsePersistentStore<T>(configure);

    /// <inheritdoc />
    public IQuartzBuilder UseJobFactory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, IJobFactory => inner.UseJobFactory<T>();

    /// <inheritdoc />
    public IQuartzBuilder UseJobFactory(IJobFactory jobFactory) => inner.UseJobFactory(jobFactory);

    /// <inheritdoc />
    public IQuartzBuilder UseTypeLoader<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, ITypeLoader => inner.UseTypeLoader<T>();

    /// <inheritdoc />
    public IQuartzBuilder UseTimeProvider(TimeProvider timeProvider) => inner.UseTimeProvider(timeProvider);

    /// <inheritdoc />
    public IQuartzBuilder AddPlugin<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        string? name = null)
        where T : class, ISchedulerPlugin => inner.AddPlugin<T>(name);

    /// <inheritdoc />
    public IQuartzBuilder AddPlugin<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory,
        string? name = null) where T : class, ISchedulerPlugin => inner.AddPlugin(factory, name);

    /// <inheritdoc />
    public IQuartzBuilder AddPlugin<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        Action<TOptions>? configure = null,
        string? name = null)
        where T : class, ISchedulerPlugin
        where TOptions : class => inner.AddPlugin<T, TOptions>(configure, name);

    /// <inheritdoc />
    public IQuartzBuilder AddSchedulerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : class, ISchedulerListener => inner.AddSchedulerListener<T>();

    /// <inheritdoc />
    public IQuartzBuilder AddSchedulerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T listener) where T : class, ISchedulerListener => inner.AddSchedulerListener(listener);

    /// <inheritdoc />
    public IQuartzBuilder AddSchedulerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory) where T : class, ISchedulerListener => inner.AddSchedulerListener(factory);

    /// <inheritdoc />
    public IQuartzBuilder AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        params IMatcher<JobKey>[] matchers) where T : class, IJobListener => inner.AddJobListener<T>(matchers);

    /// <inheritdoc />
    public IQuartzBuilder AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T listener, params IMatcher<JobKey>[] matchers) where T : class, IJobListener => inner.AddJobListener(listener, matchers);

    /// <inheritdoc />
    public IQuartzBuilder AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory, params IMatcher<JobKey>[] matchers) where T : class, IJobListener => inner.AddJobListener(factory, matchers);

    /// <inheritdoc />
    public IQuartzBuilder AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        params IMatcher<TriggerKey>[] matchers) where T : class, ITriggerListener => inner.AddTriggerListener<T>(matchers);

    /// <inheritdoc />
    public IQuartzBuilder AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T listener, params IMatcher<TriggerKey>[] matchers) where T : class, ITriggerListener => inner.AddTriggerListener(listener, matchers);

    /// <inheritdoc />
    public IQuartzBuilder AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        Func<IServiceProvider, T> factory, params IMatcher<TriggerKey>[] matchers) where T : class, ITriggerListener => inner.AddTriggerListener(factory, matchers);

    /// <inheritdoc />
    public IQuartzBuilder UseExecutionLimits(Action<ExecutionLimitsBuilder> configure) => inner.UseExecutionLimits(configure);
}
