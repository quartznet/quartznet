using System.Collections.Frozen;
using System.Collections.Specialized;
using System.Diagnostics.Metrics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Core;
using Quartz.Diagnostics;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Serialization.SystemTextJson;
using Quartz.Util;

namespace Quartz.Configuration;

/// <summary>
/// One scheduler built after the application's container was: its own service collection, its own
/// provider, and the scheduler that came out of them.
/// </summary>
/// <remarks>
/// <para>
/// The name is deliberate. A generation is <em>one</em> set of instances — a thread pool, a job store,
/// a connection provider, the plugins and listeners the recipe registered — and none of those can be
/// re-used after they have been shut down. So a scheduler that is to exist while the host is already
/// running gets a container of its own to hold them, and removing it disposes that container rather
/// than trying to persuade dead components back to life.
/// </para>
/// <para>
/// The container is not a general-purpose child container, which is a thing the DI platform declined to
/// ship and for good reasons. It holds exactly one scheduler's registrations plus the application's
/// shared services forwarded in as instances, and everything else is resolved from the application
/// through <see cref="ApplicationLink" />. That is the whole of the mechanism: this type builds the
/// collection, and <see cref="SchedulerScopedServiceProvider" /> is what routes a request afterwards.
/// </para>
/// <para>
/// Registration goes through <c>QuartzServiceCollectionExtensions.AddQuartzScheduler</c>, keyed by the
/// tenant's name, exactly as a container-registered scheduler is. There is no second construction path
/// to keep in step, and a recipe written for <c>AddQuartz(name, …)</c> is the same recipe here.
/// </para>
/// </remarks>
internal sealed class SchedulerGeneration : IAsyncDisposable
{
    /// <summary>
    /// The services <c>AddQuartzSharedServices</c> registers that a generation is handed rather than
    /// registering for itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each of these means one thing per container and not one thing per scheduler: the repository a
    /// tenant binds itself into is the application's, and so is the meter its measurements land on, the
    /// type loader that reads its aliases, and the serializer registry the HTTP API reads its triggers
    /// with. A second copy of any of them would be a tenant the dashboard cannot see, or measurements
    /// nobody is collecting.
    /// </para>
    /// <para>
    /// They are forwarded as <em>instances</em>. Microsoft's container never disposes an instance it did
    /// not create, so disposing a generation cannot take the application's meter or repository with it —
    /// which a <c>AddSingleton&lt;T&gt;(_ =&gt; theApplicationsOne)</c> factory registration would.
    /// </para>
    /// <para>
    /// <c>SharedServiceForwardingTest</c> is the guard: it enumerates what <c>AddQuartzSharedServices</c>
    /// registers and fails on anything that is neither in this list nor excused there with a reason. A
    /// new shared service has to be classified, because the failure mode of forgetting one is a tenant
    /// that works until the day it does not.
    /// </para>
    /// </remarks>
    internal static readonly FrozenSet<Type> ForwardedServiceTypes = FrozenSet.ToFrozenSet(
    [
        typeof(ILoggerFactory),
        typeof(Meters),
        typeof(TimeProvider),
        typeof(ITypeLoader),
        typeof(ISchedulerRepository),
        typeof(SharedDatabaseValidator),
        typeof(IJobExecutionContextAccessor),
        typeof(SystemTextJsonSerializerRegistry),
        typeof(DbMetadataFactory),
        typeof(DbMetadataResolver),
    ]);

    /// <summary>
    /// The container-wide reads a generation must never answer for itself, removed from its collection
    /// after registration so they fall through to the application's.
    /// </summary>
    /// <remarks>
    /// Each of these answers "what schedulers are there", and a tenant's container knows about exactly
    /// one — itself. A job that resolved <see cref="ISchedulerRegistry" /> and was handed its own
    /// scheduler as the whole world would be quietly wrong, and one that resolved a
    /// <c>SchedulerRuntime</c> of its own could add a tenant that is forgotten the moment the generation
    /// that owns it is disposed.
    /// </remarks>
    private static readonly Type[] containerWideReads =
    [
        typeof(ISchedulerRegistry),
        typeof(ISchedulerRuntime),
        typeof(SchedulerRuntime),
        typeof(ContainerSchedulerRegistry),
    ];

    private readonly ServiceProvider provider;

    private SchedulerGeneration(
        int number,
        string schedulerName,
        ServiceProvider provider,
        IServiceProvider scoped,
        QuartzSchedulerResources resources)
    {
        Number = number;
        SchedulerName = schedulerName;
        this.provider = provider;
        Scoped = scoped;
        StoreInstance = JobStores.Unwrap(resources.JobStore);
        PoolInstance = resources.ThreadPool;
    }

    /// <summary>
    /// Which generation of this name this is, counting from one.
    /// </summary>
    /// <remarks>
    /// Nothing reads it yet. It is recorded because a generation is only meaningful as one of a series —
    /// the whole reason for the name — and "which set of instances is this" is what a restart has to be
    /// able to say about a scheduler whose name did not change.
    /// </remarks>
    public int Number { get; }

    /// <summary>
    /// The name this scheduler was added under, which is also its service key and its options name.
    /// </summary>
    public string SchedulerName { get; }

    /// <summary>
    /// This generation's view of its own container: keyed to the tenant's name and linked back to the
    /// application.
    /// </summary>
    public IServiceProvider Scoped { get; }

    /// <summary>
    /// The store this generation was built with, underneath whatever the application decorated it with.
    /// </summary>
    /// <remarks>
    /// Recorded rather than resolved on demand, so that "is this the same object the last generation
    /// had" can be answered after that generation's container is gone. A part supplied as an instance —
    /// <c>UseJobStore(myStore)</c> — is the one thing a replayed recipe cannot give a second generation,
    /// and comparing by reference is how that is detected rather than guessed.
    /// </remarks>
    public IJobStore StoreInstance { get; }

    /// <inheritdoc cref="StoreInstance" />
    public IThreadPool PoolInstance { get; }

    /// <summary>
    /// The scheduler this generation built, once it has been built.
    /// </summary>
    public IScheduler? Scheduler { get; private set; }

    /// <summary>
    /// The scheduler behind the facade, once it has been built.
    /// </summary>
    /// <remarks>
    /// Recorded rather than resolved on demand, because it is read after the scheduler has been shut
    /// down and what is wanted then is <see cref="Core.QuartzScheduler.RunningWorkDrained" /> — the
    /// answer that shutdown could not return to its caller, and the only thing that says whether a
    /// second generation may safely start over the same store.
    /// </remarks>
    public QuartzScheduler? QuartzScheduler { get; private set; }

    /// <summary>
    /// Builds one scheduler's container, without creating the scheduler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything that can fail on bad configuration fails here, before anything has been bound or
    /// started: the property bag is checked, the recipe runs, the container is built, and the parts are
    /// constructed. What construction deliberately does not do is <em>initialize</em> them — a thread
    /// pool starts no thread and a database store opens no connection until <c>Initialize</c>, which is
    /// <see cref="Create" />'s to call.
    /// </para>
    /// <para>
    /// <c>refuseInstanceParts</c> is set for every generation after the first, and is what makes
    /// replaying a recipe safe: a recipe that closes over an object hands this generation the very object
    /// the last one is using, and the refusal has to happen before a provider exists to own it. It is
    /// <see langword="false" /> for a scheduler's first generation, where the object has not been used
    /// yet and there is nothing to protect it from.
    /// </para>
    /// </remarks>
    public static SchedulerGeneration Build(
        IServiceProvider application,
        string schedulerName,
        SchedulerAddOptions options,
        Action<IQuartzBuilder>? configure,
        int number,
        bool refuseInstanceParts)
    {
        ServiceCollection services = new();

        Forward(services, application);

        // Applied before the tenant's own registration, so that a scheduler registered here by a
        // container-wide delegate is covered by the delegates recorded after it, exactly as it would be
        // in the application's collection.
        SchedulerNameRegistry registry = SchedulerNameRegistry.For(services);
        SchedulerNameRegistry? applicationNames = application.GetService<SchedulerNameRegistry>();
        foreach (Action<IQuartzBuilder> configureAll in applicationNames?.ConfigureAllDelegates ?? [])
        {
            registry.AddConfigureAll(configureAll);
        }

        if (options.Configuration is not null)
        {
            // Both would mean one of them read and the other dropped without a word, which is what
            // AddQuartzSchedulers refuses in the same words for the same reason: a section says
            // everything a property bag does, so there is no reading of "both" that is not a silent loss.
            if (options.Properties is not null)
            {
                Throw.SchedulerConfigException(
                    $"The options for scheduler '{schedulerName}' set both Properties and Configuration. A "
                    + "configuration section says everything a flat property bag does, so only one of them "
                    + "would be read and the other dropped without a word. Use one or the other.");
            }

            QuartzServiceCollectionExtensions.AddQuartzScheduler(services, schedulerName, options.Configuration, configure);
        }
        else
        {
            NameValueCollection properties = QuartzServiceCollectionExtensions.PropertyBag(options.Properties ?? []);
            QuartzServiceCollectionExtensions.AddQuartzScheduler(services, schedulerName, properties, configure);
        }

        ThrowIfAJobIsRegisteredHere(services, schedulerName);

        if (refuseInstanceParts)
        {
            // Before the provider exists, which is the whole point: a refusal after one has been built
            // has to dispose it, and Microsoft's container disposes whatever a factory delegate returned
            // - which for these overloads is the object the running scheduler is using.
            ThrowIfAPartWasSuppliedAsAnInstance(services, schedulerName);
        }

        foreach (Type containerWide in containerWideReads)
        {
            services.RemoveAll(containerWide);
        }

        services.AddSingleton(new ApplicationLink(application) { ConfiguredOptions = ConfiguredOptions(services) });

        // ValidateOnBuild would check every constructor against this collection alone, and half of what
        // a tenant's components take is the application's - so it would refuse a graph that resolves
        // perfectly through the link. Scope validation is kept: it is what catches the leak a composite
        // provider is famous for, and it costs a dictionary lookup per resolution.
        ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = false, ValidateScopes = true });

        try
        {
            IServiceProvider scoped = SchedulerScopedServiceProvider.For(provider, schedulerName);
            QuartzSchedulerResources resources = scoped.GetScheduler<QuartzSchedulerResources>(schedulerName);
            return new SchedulerGeneration(number, schedulerName, provider, scoped, resources);
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates the scheduler, which initializes its parts and binds it into the application's
    /// repository.
    /// </summary>
    /// <remarks>
    /// The factory asked is this generation's own, so everything <c>DefaultSchedulerFactory</c> does for
    /// a container-registered scheduler is done here too — the instance id, the plugins, the store's
    /// schema check, the declared jobs and triggers — and the repository it binds into is the
    /// application's, because that is the one that was forwarded in.
    /// </remarks>
    public async ValueTask<IScheduler> Create(CancellationToken cancellationToken = default)
    {
        IScheduler scheduler = await Scoped.GetRequiredKeyedService<ISchedulerFactory>(SchedulerName)
            .GetScheduler(cancellationToken)
            .ConfigureAwait(false);

        Scheduler = scheduler;
        QuartzScheduler = Scoped.GetScheduler<QuartzScheduler>(SchedulerName);
        return scheduler;
    }

    /// <summary>
    /// A part of this generation that a replayed recipe would hand to the next one unchanged, or
    /// <see langword="null" /> when the recipe named a type or a factory instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The store and the pool are recorded at construction, because they are read after this
    /// generation's container is gone. These two are not: they are only ever compared while both
    /// generations are alive, which is the moment a restart decides whether the recipe can produce a
    /// second set of instances at all.
    /// </para>
    /// <para>
    /// Resolved rather than inspected, because "did the recipe close over an instance" is not a question
    /// a service descriptor answers: <c>UseJobFactory(myFactory)</c> and <c>UseJobFactory&lt;T&gt;()</c>
    /// both end as a keyed singleton, and the difference only shows in whether two containers produce the
    /// same object.
    /// </para>
    /// </remarks>
    public T? Part<T>() where T : class
    {
        return Scoped.GetSchedulerService<T>(SchedulerName);
    }

    /// <summary>
    /// Releases this generation's container, and with it every part it built.
    /// </summary>
    /// <remarks>
    /// The forwarded services are untouched: they were registered as instances this container did not
    /// create, and Microsoft's container disposes only what it created.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        return provider.DisposeAsync();
    }

    /// <summary>
    /// Hands the tenant's collection the application's shared services, as instances.
    /// </summary>
    /// <remarks>
    /// Added before anything else, so that <c>AddQuartzSharedServices</c>' <c>TryAdd</c> registrations
    /// all lose to what is already here. <see cref="DbMetadataFactory" /> is the one enumerable of the
    /// set, and every one the application holds is forwarded in the application's order, because the
    /// order is what decides which description of a provider name wins.
    /// </remarks>
    private static void Forward(IServiceCollection services, IServiceProvider application)
    {
        Add(services, application.GetService<ILoggerFactory>());
        Add(services, application.GetService<Meters>());
        Add(services, application.GetService<TimeProvider>());
        Add(services, application.GetService<ITypeLoader>());
        Add(services, application.GetService<ISchedulerRepository>());
        Add(services, application.GetService<SharedDatabaseValidator>());
        Add(services, application.GetService<IJobExecutionContextAccessor>());
        Add(services, application.GetService<SystemTextJsonSerializerRegistry>());
        Add(services, application.GetService<DbMetadataResolver>());

        foreach (DbMetadataFactory factory in application.GetServices<DbMetadataFactory>())
        {
            services.AddSingleton(factory);
        }

        static void Add<T>(IServiceCollection services, T? instance) where T : class
        {
            if (instance is not null)
            {
                services.AddSingleton(instance);
            }
        }
    }

    /// <summary>
    /// The options types this collection says something about, so that reading one of them resolves
    /// here rather than from the application.
    /// </summary>
    /// <remarks>
    /// Read off the descriptors rather than probed for through the built container, because probing
    /// means closing <c>IConfigureOptions&lt;&gt;</c> over a type known only at run time — which a
    /// native AOT publish cannot do, and which this list gets for free by reading the closed generics
    /// that are already there.
    /// </remarks>
    private static FrozenSet<Type> ConfiguredOptions(IServiceCollection services)
    {
        HashSet<Type> configured = [];
        foreach (ServiceDescriptor descriptor in services)
        {
            if (!descriptor.ServiceType.IsConstructedGenericType)
            {
                continue;
            }

            Type definition = descriptor.ServiceType.GetGenericTypeDefinition();
            if (definition == typeof(IConfigureOptions<>)
                || definition == typeof(IPostConfigureOptions<>)
                || definition == typeof(IValidateOptions<>))
            {
                configured.Add(descriptor.ServiceType.GenericTypeArguments[0]);
            }
        }

        return configured.ToFrozenSet();
    }

    /// <summary>
    /// Refuses a recipe that hands this generation an object the last one is already using.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A recipe made of <c>Use…&lt;T&gt;()</c> and factory registrations produces a new set of instances
    /// every time it runs. One that closes over an object — <c>UseJobStore(myStore)</c> — produces the
    /// same object, and that object belongs to a scheduler that is still running. Re-initialising it is
    /// what <c>#3297</c> refused; handing it to a second scheduler is the same mistake wearing a
    /// different hat.
    /// </para>
    /// <para>
    /// Answered by reading the collection rather than by comparing what two containers produced, because
    /// the timing is the point. A refusal here has built nothing and disposes nothing. A refusal after
    /// the provider exists has to release it, and Microsoft's container releases whatever a factory
    /// delegate returned — so refusing at that stage would dispose the running scheduler's own store if
    /// it happened to be <see cref="IDisposable" />, which is a worse outcome than the mistake being
    /// reported. <c>QuartzBuilder</c> leaves the note; this reads it.
    /// </para>
    /// </remarks>
    private static void ThrowIfAPartWasSuppliedAsAnInstance(IServiceCollection services, string schedulerName)
    {
        List<string> parts = [];
        foreach (ServiceDescriptor descriptor in services)
        {
            if (descriptor.ServiceType == typeof(SchedulerInstancePart)
                && Instance(descriptor) is { } part
                && !parts.Contains(part.PartType.Name, StringComparer.Ordinal))
            {
                parts.Add(part.PartType.Name);
            }
        }

        if (parts.Count == 0)
        {
            return;
        }

        parts.Sort(StringComparer.Ordinal);

        Throw.SchedulerConfigException(
            $"The recipe for scheduler '{schedulerName}' supplies {string.Join(" and ", parts)} as an instance "
            + "— UseJobStore(IJobStore), UseThreadPool(IThreadPool), UseJobFactory(instance) or "
            + "UseInstanceIdGenerator(instance) — so replaying it would hand the new scheduler the very object "
            + "the old one is using, and a shut-down instance cannot be re-initialised. Register a type or a "
            + "factory instead: UseJobStore<T>(), UseThreadPool<T>() and UseJobStore(provider => new …) each "
            + "build a new instance every time the recipe runs. A factory must return a new instance per "
            + $"generation rather than close over a shared one. Scheduler '{schedulerName}' was not touched.");

        static SchedulerInstancePart? Instance(ServiceDescriptor descriptor)
        {
            return descriptor.IsKeyedService
                ? descriptor.KeyedImplementationInstance as SchedulerInstancePart
                : descriptor.ImplementationInstance as SchedulerInstancePart;
        }
    }

    /// <summary>
    /// Refuses a recipe that says how <em>this scheduler</em> builds a job type, which a generation
    /// cannot honour.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A job is built by the application, because its constructor takes the application's services and a
    /// container resolves a service's dependencies from itself. So <c>AddJobType</c> here — whichever
    /// overload — would either be read by nobody or build the job out of the half of the graph that does
    /// not have its dependencies. Saying so is the only honest answer; silently ignoring it is how a
    /// tenant comes to run the wrong code, or to fail at its first firing rather than where the mistake
    /// was written.
    /// </para>
    /// <para>
    /// <c>AddJob&lt;T&gt;</c> and <c>ScheduleJob&lt;T&gt;</c> register a job type as itself and without a
    /// key, which is inert rather than wrong: the type resolves from the application if it is registered
    /// there and is activated from this provider if it is not, which is what would have happened anyway.
    /// </para>
    /// </remarks>
    private static void ThrowIfAJobIsRegisteredHere(IServiceCollection services, string schedulerName)
    {
        foreach (ServiceDescriptor descriptor in services)
        {
            if (!typeof(IJob).IsAssignableFrom(descriptor.ServiceType))
            {
                continue;
            }

            // A keyed descriptor's ImplementationType throws rather than answering, so the shape is
            // tested before anything is read off it - and every AddJobType overload is keyed here,
            // because a scheduler added at runtime always has a name.
            if (!descriptor.IsKeyedService && descriptor.ImplementationType == descriptor.ServiceType)
            {
                continue;
            }

            Throw.SchedulerConfigException(
                $"The recipe for scheduler '{schedulerName}' says how it builds the job type "
                + $"'{descriptor.ServiceType.FullName}', and a scheduler added at runtime does not build its own "
                + "jobs: they are built by the application's container, which is where their dependencies are. "
                + "Register the job type there instead — services.AddScoped<TJob>() or "
                + "services.AddScoped<TJob, TImplementation>() — and leave the recipe to schedule it with "
                + "AddJob<TJob>(...).");
        }
    }
}
