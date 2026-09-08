using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Core;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Serialization.SystemTextJson;
using Quartz.Extensibility;

namespace Quartz.Configuration;

/// <summary>
/// Resolves a scheduler's own parts when constructing one of its components.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ActivatorUtilities"/> resolves constructor dependencies without a service key, so a
/// component built for a named scheduler would be handed the default scheduler's collaborators — or
/// fail outright, because for a named scheduler they are only registered keyed. That is silent
/// cross-wiring of exactly the kind keyed registration exists to prevent.
/// </para>
/// <para>
/// Wrapping the provider fixes it in one place: while constructing a component for scheduler
/// <c>reporting</c>, a request for <see cref="ISchedulerSignaler"/> resolves <c>reporting</c>'s
/// signaler. Services that are genuinely shared, such as loggers and the scheduler repository, are
/// resolved normally.
/// </para>
/// </remarks>
internal sealed class SchedulerScopedServiceProvider
    : IKeyedServiceProvider, IServiceProviderIsKeyedService, IServiceScopeFactory
{
    /// <summary>
    /// The services registered once per scheduler rather than once per container.
    /// </summary>
    /// <remarks>
    /// Frozen because it is read on the way to every service a named scheduler's components resolve —
    /// the job type and each of a job's dependencies, once per fire — and never written after type
    /// initialization. <c>SchedulerScopedServiceProviderBenchmark</c> puts the two shapes side by side.
    /// </remarks>
    private static readonly FrozenSet<Type> schedulerScoped = FrozenSet.ToFrozenSet(
    [
        typeof(ISchedulerSignaler),
        typeof(IJobStore),
        typeof(IDbProvider),
        typeof(IDriverDelegate),
        typeof(AdoJobStoreDependencies),
        typeof(ILockHandler),
        typeof(IObjectSerializer),
        typeof(IJobInputSerializer),
        typeof(SystemTextJsonSerializerRegistry),
        typeof(IThreadPool),
        typeof(IJobFactory),
        typeof(IJobRunShellFactory),
        typeof(IInstanceIdGenerator),
        typeof(ISchedulerFactory),
        typeof(QuartzSchedulerResources),
        typeof(QuartzScheduler),
        typeof(ContainerConfigurationProcessor),
        typeof(SchedulerContentInitializer),
    ]);

    /// <summary>
    /// The options a scheduler's components ask for, in each of the shapes the options framework offers
    /// them: <see cref="IOptions{TOptions}"/>, <see cref="IOptionsMonitor{TOptions}"/> and
    /// <see cref="IOptionsSnapshot{TOptions}"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A scheduler's options are <em>named</em> options, but the unnamed members of all three interfaces
    /// — <c>Value</c> and <c>CurrentValue</c> — resolve the unnamed instance. A named scheduler's job
    /// store would therefore be configured from the default scheduler's settings, which is the same
    /// cross-wiring this class exists to prevent, so those members are answered under this scheduler's
    /// name. <c>Get(name)</c> asks for an instance by name and is passed through as asked.
    /// </para>
    /// <para>
    /// The map is explicit rather than generic because closing <c>OptionsWrapper&lt;&gt;</c> over a
    /// runtime type is not something a trimmer can follow. Options types Quartz does not know — a
    /// plugin's own — say so with <see cref="SchedulerNamedOptions"/>, which is closed over its type
    /// where that type is still known at compile time.
    /// </para>
    /// <para>
    /// Frozen for the same reason <see cref="schedulerScoped"/> is: it is consulted on every request
    /// that set does not answer, and it is never written after type initialization.
    /// </para>
    /// </remarks>
    private static readonly FrozenDictionary<Type, Func<IServiceProvider, string, object>> namedOptions
        = DeclareQuartzOptions().ToFrozenDictionary();

    /// <summary>
    /// The two lookup tables above, so that <c>SchedulerScopedServiceProviderBenchmark</c> measures the
    /// real ones rather than a copy that can drift from them.
    /// </summary>
    internal static IReadOnlyCollection<Type> SchedulerScopedServiceTypes => schedulerScoped;

    /// <inheritdoc cref="SchedulerScopedServiceTypes" />
    internal static Dictionary<Type, Func<IServiceProvider, string, object>> DeclareQuartzOptions()
    {
        Dictionary<Type, Func<IServiceProvider, string, object>> map = [];
        SchedulerNamedOptions<QuartzSchedulerOptions>.Declare(map);
        SchedulerNamedOptions<ThreadPoolOptions>.Declare(map);
        SchedulerNamedOptions<InMemoryJobStoreOptions>.Declare(map);
        SchedulerNamedOptions<AdoJobStoreOptions>.Declare(map);
        SchedulerNamedOptions<ClusteringOptions>.Declare(map);
        SchedulerNamedOptions<QuartzOptions>.Declare(map);
        return map;
    }

    private readonly IServiceProvider inner;
    private readonly object? key;

    /// <summary>
    /// The application container this scheduler was built beside, when it was built at runtime into a
    /// container of its own, or <see langword="null" /> for a scheduler the application container
    /// registered.
    /// </summary>
    /// <remarks>
    /// A scheduler registered with <c>AddQuartz</c> lives in the application's own container, so
    /// "everything else" is already one <see cref="IServiceProvider.GetService" /> away and there is
    /// nothing to link to. One added at runtime lives in a container built for it alone, and this is
    /// what everything the recipe did not register resolves from — which is what
    /// makes a tenant's job an ordinary application component rather than something assembled out of a
    /// second, half-empty container.
    /// </remarks>
    private readonly ApplicationLink? link;

    private Dictionary<Type, Func<IServiceProvider, string, object>>? declared;

    private SchedulerScopedServiceProvider(IServiceProvider inner, object? key, ApplicationLink? link)
    {
        this.inner = inner;
        this.key = key;
        this.link = link;
    }

    private string Name => key as string ?? Options.DefaultName;

    /// <summary>
    /// The service key this scheduler's parts are registered under.
    /// </summary>
    /// <remarks>
    /// Read by components that resolve a type the container knows nothing about in advance — a job type
    /// is the case — and so cannot be routed by the type lists above. The default scheduler has no
    /// wrapper at all, so a component that finds one of these knows it belongs to a named scheduler.
    /// </remarks>
    internal object? SchedulerServiceKey => key;

    /// <summary>
    /// Finds the resolver for an options type this scheduler owns, whether Quartz declared it or a plugin
    /// brought it, or <see langword="null"/> when the request is for something else.
    /// </summary>
    /// <remarks>
    /// The static map above cannot list a plugin's: the type comes from the caller, not from Quartz.
    /// <c>AddPlugin&lt;T, TOptions&gt;</c> registers a declaration instead, closed over <c>TOptions</c>
    /// where it is still a compile-time type, so nothing here has to close a generic over a runtime one.
    /// </remarks>
    private Func<IServiceProvider, string, object>? NamedOptions(Type serviceType)
    {
        return namedOptions.GetValueOrDefault(serviceType) ?? Declared(serviceType);
    }

    /// <summary>
    /// Finds the resolver for an options type a plugin brought with it.
    /// </summary>
    private Func<IServiceProvider, string, object>? Declared(Type serviceType)
    {
        // Asking the container for every declaration is not free, so the requests that cannot be one are
        // turned away first. Every options service is a closed generic over one of these three.
        if (!serviceType.IsConstructedGenericType)
        {
            return null;
        }

        Type definition = serviceType.GetGenericTypeDefinition();
        if (definition != typeof(IOptions<>)
            && definition != typeof(IOptionsMonitor<>)
            && definition != typeof(IOptionsSnapshot<>))
        {
            return null;
        }

        // Built into a local and then published, because this provider outlives the construction it was
        // made for — a component handed it as IServiceProvider keeps resolving through it — so two
        // threads can arrive here at once, and a half-filled dictionary must never be one of them.
        Dictionary<Type, Func<IServiceProvider, string, object>>? known = declared;
        if (known is null)
        {
            known = [];
            foreach (var options in inner.GetServices<SchedulerNamedOptions>())
            {
                options.DeclareInto(known);
            }

            declared = known;
        }

        return known.GetValueOrDefault(serviceType);
    }

    /// <summary>
    /// Returns a provider that resolves the given scheduler's parts. The default scheduler's services
    /// are not keyed, so it needs no wrapper.
    /// </summary>
    /// <remarks>
    /// The <see cref="ApplicationLink" /> is read from the provider rather than passed in, because the
    /// registrations that call this are the same ones for a container scheduler and a runtime one — the
    /// whole point of building a tenant through <c>AddQuartzScheduler</c> is that there is one
    /// registration path. A container-registered scheduler pays one lookup that answers
    /// <see langword="null" /> per part it constructs, which is build time rather than fire time; the
    /// default scheduler pays nothing at all, since it needs no wrapper.
    /// </remarks>
    public static IServiceProvider For(IServiceProvider provider, object? key)
    {
        return key is null
            ? provider
            : new SchedulerScopedServiceProvider(provider, key, provider.GetService<ApplicationLink>());
    }

    public object? GetService(Type serviceType)
    {
        if (schedulerScoped.Contains(serviceType))
        {
            return inner.GetKeyedService(serviceType, key);
        }

        // A scheduler's clock is the one it was given, or the container's, or the system one. It is not
        // in the set above because the fallback is what makes a named scheduler inherit an
        // application-wide TimeProvider it was never told about, rather than be handed nothing.
        if (serviceType == typeof(TimeProvider))
        {
            return inner.GetKeyedService(serviceType, key) ?? inner.GetService(serviceType);
        }

        // The trigger persistence delegates are one service type registered several times, so they
        // resolve as an enumerable rather than through the single-service set above. The container's
        // own answer for an IEnumerable is the unkeyed registrations, which for a named scheduler
        // would be another scheduler's delegates — or none — so the request is redirected to this
        // scheduler's keyed set.
        if (serviceType == typeof(IEnumerable<ITriggerPersistenceDelegate>))
        {
            return inner.GetKeyedServices<ITriggerPersistenceDelegate>(key);
        }

        if (NamedOptions(serviceType) is { } options)
        {
            return options(inner, Name);
        }

        // A component handed "the container" so it can resolve things later must keep resolving this
        // scheduler's parts, not the default scheduler's — plugins built from a type name are the case
        // that makes the difference visible. The same goes for the "is this registered?" services, which
        // ActivatorUtilities consults while choosing a constructor, and for the scope factory: a job
        // scope that resolved from the raw container would build jobs with the wrong scheduler's parts.
        if (serviceType == typeof(IServiceProvider)
            || serviceType == typeof(IServiceProviderIsService)
            || serviceType == typeof(IServiceProviderIsKeyedService)
            || serviceType == typeof(IServiceScopeFactory))
        {
            return this;
        }

        return link is null ? inner.GetService(serviceType) : FromGeneration(serviceType, link);
    }

    /// <summary>
    /// Answers a request this scheduler's own registrations did not, for a scheduler that lives in a
    /// container of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three requests are decided rather than tried, because "ask the tenant, then the application" is
    /// the wrong answer for each of them.
    /// </para>
    /// <para>
    /// A <see cref="IJob" /> is the application's to build. Its constructor takes the application's
    /// services — a unit of work, a database context — and a container resolves a service's dependencies
    /// from itself rather than through whatever wrapper was asked, so a job built by the tenant's
    /// container would be built from the half of the graph that does not have them.
    /// </para>
    /// <para>
    /// Options belong to whoever configured them. The tenant's own settings are its container's, and so
    /// is anything Quartz declares, since the tenant is the only scheduler its container holds; an
    /// application's options type is the application's, and reading it from the tenant would answer with
    /// a freshly defaulted instance nobody configured.
    /// </para>
    /// <para>
    /// An <see cref="IEnumerable{T}" /> is the one shape where "the tenant answered nothing" and "the
    /// tenant has none of these" are indistinguishable — a container answers an empty sequence rather
    /// than <see langword="null" /> — so the decision is made on whether the tenant declares the item
    /// type at all rather than on what came back.
    /// </para>
    /// </remarks>
    private object? FromGeneration(Type serviceType, ApplicationLink application)
    {
        if (typeof(IJob).IsAssignableFrom(serviceType))
        {
            return application.Application.GetService(serviceType);
        }

        if (serviceType.IsConstructedGenericType)
        {
            Type definition = serviceType.GetGenericTypeDefinition();

            if (definition == typeof(IOptions<>)
                || definition == typeof(IOptionsMonitor<>)
                || definition == typeof(IOptionsSnapshot<>)
                || definition == typeof(IOptionsFactory<>))
            {
                return application.ConfiguresOptions(serviceType.GenericTypeArguments[0])
                    ? inner.GetService(serviceType)
                    : application.Application.GetService(serviceType);
            }

            if (definition == typeof(IEnumerable<>))
            {
                return Declares(serviceType.GenericTypeArguments[0])
                    ? inner.GetService(serviceType)
                    : application.Application.GetService(serviceType);
            }
        }

        return inner.GetService(serviceType) ?? application.Application.GetService(serviceType);
    }

    /// <summary>
    /// Whether this scheduler's own container holds a registration of a service type.
    /// </summary>
    private bool Declares(Type serviceType)
    {
        return inner.GetService<IServiceProviderIsService>()?.IsService(serviceType) ?? false;
    }

    /// <summary>
    /// Creates a scope whose provider still resolves this scheduler's parts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Jobs are built inside a scope. Without this the scope comes straight from the container, so a job
    /// that takes an <see cref="ISchedulerFactory"/> is handed the default scheduler's — or, when only
    /// named schedulers exist, cannot be constructed at all.
    /// </para>
    /// <para>
    /// A scheduler that lives in a container of its own opens <em>two</em> scopes, one in each container,
    /// and hands out a provider over both. That is what makes a firing's scope the application's: a
    /// scoped service a job and a middleware both take is one instance, created in the application's
    /// scope and disposed with it when the job is returned. One scope over the tenant's container alone
    /// would give the application's scoped services no scope to live in — the leak the platform's own
    /// refusal of child containers warns about.
    /// </para>
    /// </remarks>
    public IServiceScope CreateScope()
    {
        if (link is null)
        {
            return new Scope(inner.GetRequiredService<IServiceScopeFactory>().CreateScope(), key, link: null, application: null);
        }

        // The application's scope first: if the tenant's container cannot open one, there is something
        // to dispose, and the other order would leave the application's scope behind instead.
        IServiceScope application = link.Application.GetRequiredService<IServiceScopeFactory>().CreateScope();
        try
        {
            IServiceScope scope = inner.GetRequiredService<IServiceScopeFactory>().CreateScope();
            return new Scope(scope, key, link with { Application = application.ServiceProvider }, application);
        }
        catch
        {
            application.Dispose();
            throw;
        }
    }

    private sealed class Scope : IServiceScope, IAsyncDisposable
    {
        private readonly IServiceScope scope;

        /// <summary>
        /// The scope opened in the application's container, for a scheduler that lives in one of its
        /// own, or <see langword="null" /> when there is no second container.
        /// </summary>
        private readonly IServiceScope? application;

        public Scope(IServiceScope scope, object? key, ApplicationLink? link, IServiceScope? application)
        {
            this.scope = scope;
            this.application = application;
            ServiceProvider = new SchedulerScopedServiceProvider(scope.ServiceProvider, key, link);
        }

        public IServiceProvider ServiceProvider { get; }

        public void Dispose()
        {
            try
            {
                scope.Dispose();
            }
            finally
            {
                // Always, even when the tenant's scope threw on the way out: the application's scope
                // holds the job's own scoped services, and leaking those is the worse of the two.
                application?.Dispose();
            }
        }

        /// <summary>
        /// Disposes the wrapped scope asynchronously where it supports it.
        /// </summary>
        /// <remarks>
        /// Jobs are torn down through <see cref="IAsyncDisposable"/>. Without it here, a scoped service
        /// that is only async-disposable throws when the container disposes the scope synchronously.
        /// </remarks>
        public async ValueTask DisposeAsync()
        {
            try
            {
                await DisposeOne(scope).ConfigureAwait(false);
            }
            finally
            {
                if (application is not null)
                {
                    await DisposeOne(application).ConfigureAwait(false);
                }
            }
        }

        private static ValueTask DisposeOne(IServiceScope scope)
        {
            if (scope is IAsyncDisposable asyncDisposable)
            {
                return asyncDisposable.DisposeAsync();
            }

            scope.Dispose();
            return default;
        }
    }

    public object? GetKeyedService(Type serviceType, object? serviceKey)
    {
        if (link is null)
        {
            return inner.GetKeyedService(serviceType, serviceKey);
        }

        // This scheduler's own key means its own container first — that is where its recipe registered
        // whatever it keyed. Any other key is somebody else's scheduler, and the application is the one
        // that has those.
        return Equals(serviceKey, key)
            ? inner.GetKeyedService(serviceType, serviceKey) ?? link.Application.GetKeyedService(serviceType, serviceKey)
            : link.Application.GetKeyedService(serviceType, serviceKey) ?? inner.GetKeyedService(serviceType, serviceKey);
    }

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
    {
        if (link is null)
        {
            return inner.GetRequiredKeyedService(serviceType, serviceKey);
        }

        return GetKeyedService(serviceType, serviceKey)
            ?? throw new InvalidOperationException(
                $"No service for type '{serviceType}' with key '{serviceKey}' has been registered.");
    }

    /// <summary>
    /// Answers what this scheduler can be given, not what the container holds unkeyed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ActivatorUtilities"/> asks this before choosing a constructor, and treats a service it
    /// is told does not exist as a parameter it cannot supply. Answering from the container directly
    /// would report every one of a named scheduler's own parts as missing, so a component with more than
    /// one constructor gets the wrong one chosen — or is rejected outright.
    /// </para>
    /// <para>
    /// For a scheduler that lives in a container of its own the answer is the union of the two, because
    /// what this provider <em>can supply</em> is the union: whichever container holds it,
    /// <see cref="GetService" /> will find it.
    /// </para>
    /// </remarks>
    public bool IsService(Type serviceType)
    {
        if (schedulerScoped.Contains(serviceType))
        {
            return IsKeyedService(serviceType, key);
        }

        if (serviceType == typeof(TimeProvider))
        {
            return IsKeyedService(serviceType, key)
                || (inner.GetService<IServiceProviderIsService>()?.IsService(serviceType) ?? false);
        }

        if (NamedOptions(serviceType) is not null
            || serviceType == typeof(IEnumerable<ITriggerPersistenceDelegate>)
            || serviceType == typeof(IServiceProvider)
            || serviceType == typeof(IServiceProviderIsService)
            || serviceType == typeof(IServiceProviderIsKeyedService)
            || serviceType == typeof(IServiceScopeFactory))
        {
            return true;
        }

        return Declares(serviceType)
            || (link is not null && (link.Application.GetService<IServiceProviderIsService>()?.IsService(serviceType) ?? false));
    }

    /// <inheritdoc cref="IsService" />
    public bool IsKeyedService(Type serviceType, object? serviceKey)
    {
        return (inner.GetService<IServiceProviderIsKeyedService>()?.IsKeyedService(serviceType, serviceKey) ?? false)
            || (link is not null
                && (link.Application.GetService<IServiceProviderIsKeyedService>()?.IsKeyedService(serviceType, serviceKey) ?? false));
    }
}

/// <summary>
/// The application container a scheduler built at runtime resolves everything else from.
/// </summary>
/// <remarks>
/// <para>
/// Registered as an instance in the container <c>SchedulerGeneration</c> builds for one tenant, which is
/// how <see cref="SchedulerScopedServiceProvider.For" /> finds it without every registration having to
/// be told about it. A container-registered scheduler has none, and behaves exactly as it did before
/// this type existed.
/// </para>
/// <para>
/// A record so that <c>with</c> can re-point it at a scope's provider while keeping what was computed
/// once for the generation.
/// </para>
/// </remarks>
/// <param name="Application">The application's provider, or the scope of it a firing is running in.</param>
internal sealed record ApplicationLink(IServiceProvider Application)
{
    private static readonly Assembly quartz = typeof(IScheduler).Assembly;

    /// <summary>
    /// The options types the tenant's own container configures, validates or post-configures.
    /// </summary>
    /// <remarks>
    /// Read off the tenant's service collection while it is being built, rather than probed for at
    /// resolution time: asking the container whether it holds an <c>IConfigureOptions&lt;X&gt;</c> means
    /// closing that generic over a type only known at run time, which is the one thing a native AOT
    /// publish cannot do.
    /// </remarks>
    public FrozenSet<Type> ConfiguredOptions { get; init; } = FrozenSet<Type>.Empty;

    /// <summary>
    /// Whether an options type belongs to the tenant rather than to the application.
    /// </summary>
    /// <remarks>
    /// Quartz's own options types always do: a tenant's container holds exactly one scheduler, so
    /// <see cref="QuartzSchedulerOptions" /> read there is that scheduler's, and reading it from the
    /// application would answer with whatever the application's own scheduler was configured with — or
    /// with nothing. Anything else belongs to the tenant only if the recipe configured it.
    /// </remarks>
    public bool ConfiguresOptions(Type optionsType)
    {
        return optionsType.Assembly == quartz || ConfiguredOptions.Contains(optionsType);
    }
}

/// <summary>
/// Declares that an options type is configured once per scheduler rather than once per container.
/// </summary>
/// <remarks>
/// <see cref="SchedulerScopedServiceProvider"/> lists Quartz's own options types itself, but a plugin
/// brings an options type Quartz has never heard of. This is how <c>AddPlugin&lt;T, TOptions&gt;</c> says
/// so, and it is registered rather than reflected over so the closed generic exists at compile time.
/// </remarks>
internal abstract class SchedulerNamedOptions
{
    /// <summary>
    /// Adds the services a component asks for when it wants these options, each resolving the instance
    /// configured for one scheduler.
    /// </summary>
    public abstract void DeclareInto(Dictionary<Type, Func<IServiceProvider, string, object>> map);
}

/// <inheritdoc />
/// <remarks>
/// <see cref="IOptions{TOptions}"/>, <see cref="IOptionsMonitor{TOptions}"/> and
/// <see cref="OptionsWrapper{TOptions}"/> each require their options type to keep its public
/// parameterless constructor, so this one has to promise the same — an annotation only holds if it is
/// carried at every hop, and the chain runs from <c>AddPlugin&lt;T, TOptions&gt;</c> down to here.
/// </remarks>
internal sealed class SchedulerNamedOptions<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>
    : SchedulerNamedOptions where TOptions : class
{
    /// <inheritdoc />
    public override void DeclareInto(Dictionary<Type, Func<IServiceProvider, string, object>> map)
    {
        Declare(map);
    }

    /// <summary>
    /// Declares all three shapes of one options type. Also how <see cref="SchedulerScopedServiceProvider"/>
    /// builds its own list, so Quartz's options types and a plugin's are answered by the same code.
    /// </summary>
    internal static void Declare(Dictionary<Type, Func<IServiceProvider, string, object>> map)
    {
        // A fixed value, so there is nothing to keep watching: read the scheduler's instance once and hand
        // it over.
        map[typeof(IOptions<TOptions>)] = static (provider, name) =>
            new OptionsWrapper<TOptions>(provider.GetRequiredService<IOptionsMonitor<TOptions>>().Get(name));

        map[typeof(IOptionsMonitor<TOptions>)] = static (provider, name) =>
            new SchedulerOptionsMonitor<TOptions>(provider.GetRequiredService<IOptionsMonitor<TOptions>>(), name);

        // Resolved from whatever provider is asking, so a snapshot taken in a job's scope still lives and
        // caches for that scope rather than for the container.
        map[typeof(IOptionsSnapshot<TOptions>)] = static (provider, name) =>
            new SchedulerOptionsSnapshot<TOptions>(provider.GetRequiredService<IOptionsSnapshot<TOptions>>(), name);
    }
}

/// <summary>
/// An <see cref="IOptionsMonitor{TOptions}"/> whose unnamed members mean one scheduler's options.
/// </summary>
/// <remarks>
/// <para>
/// The rule is the same one <see cref="SchedulerScopedServiceProvider"/> applies throughout: a member
/// that does not name an instance means <em>this</em> scheduler, and a member that names one is left
/// alone. So <see cref="CurrentValue"/> is the scheduler's instance, while <see cref="Get"/> answers for
/// whatever name it was handed.
/// </para>
/// <para>
/// <see cref="OnChange"/> follows <see cref="CurrentValue"/> and reports only this scheduler's changes.
/// The listener has an overload that discards the name — <c>monitor.OnChange(options =&gt; …)</c> — and
/// forwarding every name to it would hand a component another scheduler's options as though they were
/// its own, which is the very confusion this class exists to prevent. A component that wants to watch a
/// different scheduler's configuration can still read it, by name, through <see cref="Get"/>.
/// </para>
/// </remarks>
internal sealed class SchedulerOptionsMonitor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>
    : IOptionsMonitor<TOptions>
{
    private readonly IOptionsMonitor<TOptions> inner;
    private readonly string name;

    public SchedulerOptionsMonitor(IOptionsMonitor<TOptions> inner, string name)
    {
        this.inner = inner;
        this.name = name;
    }

    public TOptions CurrentValue => inner.Get(name);

    public TOptions Get(string? name) => inner.Get(name);

    public IDisposable? OnChange(Action<TOptions, string?> listener)
    {
        // The name rather than this wrapper is what the filter closes over, so a live registration does not
        // keep the component's copy of the monitor alive.
        string scheduler = name;

        // The registration the inner monitor hands back is returned as it is, so disposing it unhooks the
        // filter along with the listener it wraps.
        return inner.OnChange((options, changed) =>
        {
            if (string.Equals(changed ?? Options.DefaultName, scheduler, StringComparison.Ordinal))
            {
                listener(options, changed);
            }
        });
    }
}

/// <summary>
/// An <see cref="IOptionsSnapshot{TOptions}"/> whose unnamed member means one scheduler's options.
/// </summary>
/// <remarks>
/// <see cref="IOptionsSnapshot{TOptions}"/> derives from <see cref="IOptions{TOptions}"/>, so
/// <see cref="Value"/> has to mean what <c>IOptions&lt;TOptions&gt;.Value</c> means here: this
/// scheduler's instance. The snapshot itself comes from the container, so the per-scope caching a
/// snapshot exists for is unchanged.
/// </remarks>
internal sealed class SchedulerOptionsSnapshot<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>
    : IOptionsSnapshot<TOptions> where TOptions : class
{
    private readonly IOptionsSnapshot<TOptions> inner;
    private readonly string name;

    public SchedulerOptionsSnapshot(IOptionsSnapshot<TOptions> inner, string name)
    {
        this.inner = inner;
        this.name = name;
    }

    public TOptions Value => inner.Get(name);

    public TOptions Get(string? name) => inner.Get(name);
}
