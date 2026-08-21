using System.Collections.Specialized;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a Quartz scheduler.
    /// </summary>
    /// <inheritdoc cref="AddQuartz(IServiceCollection, IConfiguration, Action{IQuartzBuilder})" path="/remarks" />
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        Action<IQuartzBuilder>? configure = null)
    {
        return AddQuartzScheduler(services, schedulerName: null, new NameValueCollection(), configure);
    }

    /// <summary>
    /// Registers a Quartz scheduler, seeded with flat <c>quartz.*</c> properties.
    /// </summary>
    /// <inheritdoc cref="AddQuartz(IServiceCollection, IConfiguration, Action{IQuartzBuilder})" path="/remarks" />
    /// <param name="services">The service collection to register into.</param>
    /// <param name="properties">
    /// The flat <c>quartz.*</c> properties. They are checked against the keys Quartz reads, as they are
    /// on the standalone builder, so a misspelling — or a key 4.0 stopped reading — is reported rather
    /// than silently ignored. Set <c>quartz.checkConfiguration</c> to <see langword="false"/> to allow
    /// keys of your own.
    /// </param>
    /// <param name="configure">Configures the scheduler.</param>
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        NameValueCollection properties,
        Action<IQuartzBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(properties);
        LegacyPropertyKeys.Validate(properties);
        return AddQuartzScheduler(services, schedulerName: null, properties, configure);
    }

    /// <summary>
    /// Registers a Quartz scheduler from a configuration section.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hierarchical sections such as <c>Scheduler</c> and <c>ThreadPool</c> bind onto the typed options
    /// directly. Flat <c>quartz.*</c> keys are still accepted and mean the same thing. Several
    /// schedulers described by one section are registered with
    /// <see cref="AddQuartzSchedulers(IServiceCollection, IConfiguration, Action{IQuartzBuilder})"/>.
    /// </para>
    /// <para>
    /// A scheduler is described by six things, applied in this order — which is what decides who wins
    /// when two of them say something about the same setting:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// The flat <c>quartz.*</c> properties are recorded in <see cref="QuartzOptions.Properties"/>, so
    /// the parts that read the property bag directly — plugins, execution limits — can see them.
    /// </description></item>
    /// <item><description>
    /// Those properties are translated into typed options.
    /// </description></item>
    /// <item><description>
    /// Properties contributed later by configuring <see cref="QuartzOptions"/> are translated too, which
    /// is what makes <c>services.Configure&lt;QuartzOptions&gt;</c> equivalent to passing them here.
    /// </description></item>
    /// <item><description>
    /// The <paramref name="configure"/> callback runs. Options are <em>last-wins</em>, so anything it
    /// sets is applied over the property-derived values: configuration written in code beats a string.
    /// </description></item>
    /// <item><description>
    /// The implementations the properties name — a job store type, a thread pool type, a serializer —
    /// are registered. Registration is <em>first-wins</em>, so this comes after the callback: a store
    /// chosen by <c>UsePersistentStore</c> has to beat a leftover <c>quartz.jobStore.type</c> from an
    /// old configuration file. Code beats strings in both directions, opposite orders notwithstanding.
    /// </description></item>
    /// <item><description>
    /// Quartz's own defaults are registered last, so an explicitly configured job store, thread pool or
    /// serializer is never beaten to the registration by the fallback it was meant to replace.
    /// </description></item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IQuartzBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.GetSection("Schedulers").Exists())
        {
            Throw.SchedulerConfigException(
                "The Quartz configuration section contains a 'Schedulers' sub-section, which describes " +
                "several named schedulers rather than one. Call AddQuartzSchedulers(configuration) to " +
                "register them, or AddQuartz(name, configuration) to register one of them.");
        }

        // Bound before the callback runs, so configuration is the starting point and code overrides it.
        services.BindQuartzOptions(configuration);
        JsonSchedulingHelper.ConfigureOptionsFromConfiguration(services, configuration, Options.DefaultName);
        AddQuartzScheduler(services, schedulerName: null, LegacyProperties(configuration), configure);
        return services;
    }

    /// <summary>
    /// Registers one named Quartz scheduler per child of the section's <c>Schedulers</c> sub-section.
    /// </summary>
    /// <remarks>
    /// Each child's key is the scheduler's name, and its contents are that scheduler's configuration —
    /// exactly what <c>AddQuartz(name, section)</c> would be given. The fan-out is its own method
    /// because registering several schedulers is a different act from registering one, and reading it
    /// out of the shape of a configuration file made <c>AddQuartz</c> mean two things depending on data
    /// it was handed.
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">
    /// The Quartz configuration section, containing a <c>Schedulers</c> sub-section.
    /// </param>
    /// <param name="configure">Applied to every scheduler described by the section.</param>
    public static IServiceCollection AddQuartzSchedulers(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IQuartzBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var schedulers = configuration.GetSection("Schedulers");
        if (!schedulers.Exists())
        {
            Throw.SchedulerConfigException(
                "The Quartz configuration section has no 'Schedulers' sub-section, so it describes one " +
                "scheduler rather than several. Call AddQuartz(configuration) instead.");
        }

        if (HasDirectSchedulerConfiguration(configuration))
        {
            Throw.SchedulerConfigException(
                "The Quartz configuration section contains both a 'Schedulers' sub-section and direct " +
                "scheduler configuration. Use one or the other.");
        }

        if (configuration.GetSection("Schedule").Exists() || configuration.GetSection("Scheduling").Exists())
        {
            Throw.SchedulerConfigException(
                "The Quartz configuration section contains a 'Schedulers' sub-section and a top-level " +
                "'Schedule' section. Jobs and triggers belong to a specific scheduler, so move them " +
                "under the scheduler they belong to.");
        }

        foreach (var scheduler in schedulers.GetChildren())
        {
            AddQuartz(services, scheduler.Key, scheduler, configure);
        }

        return services;
    }

    /// <summary>
    /// Registers a named Quartz scheduler, so several independent schedulers can share a container.
    /// </summary>
    /// <remarks>
    /// The name is the scheduler's instance name, the key its components are registered under, and the
    /// name of its options, so its registrations and its configuration always agree.
    /// </remarks>
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        string name,
        Action<IQuartzBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return AddQuartzScheduler(services, name, new NameValueCollection(), configure);
    }

    /// <summary>
    /// Registers a named Quartz scheduler, seeded with flat <c>quartz.*</c> properties.
    /// </summary>
    /// <inheritdoc cref="AddQuartz(IServiceCollection, NameValueCollection, Action{IQuartzBuilder})" path="/param[@name='properties']" />
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        string name,
        NameValueCollection properties,
        Action<IQuartzBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(properties);
        LegacyPropertyKeys.Validate(properties);
        return AddQuartzScheduler(services, name, properties, configure);
    }

    /// <summary>
    /// Registers a named Quartz scheduler from a configuration section.
    /// </summary>
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        Action<IQuartzBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configuration);

        // Callers may pass either the scheduler's own section or the root section containing
        // "Schedulers:{name}", so resolve to whichever actually holds this scheduler's settings.
        var own = configuration.GetSection("Schedulers").GetSection(name);
        var effective = own.Exists() ? own : configuration;

        services.BindQuartzOptions(effective, name);
        JsonSchedulingHelper.ConfigureOptionsFromConfiguration(services, effective, name);
        AddQuartzScheduler(services, name, LegacyProperties(effective), configure);
        return services;
    }

    /// <summary>
    /// Uses the default type loader, which resolves type names against loaded assemblies.
    /// </summary>
    /// <remarks>
    /// This is the public way to ask for the built-in loader: <c>SimpleTypeLoader</c> is internal,
    /// because a type-loading strategy is not something to derive from, so there is no
    /// <c>UseTypeLoader&lt;SimpleTypeLoader&gt;()</c> to write instead. It is also already the
    /// default, so calling it only matters where something else registered a loader first.
    /// </remarks>
    public static IQuartzBuilder UseSimpleTypeLoader(this IQuartzBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseTypeLoader<SimpleTypeLoader>();
    }

    /// <summary>
    /// Returns everything a configuration section says, in the legacy flat form.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every section is flattened, including the ones that also bind onto typed options. Sending the
    /// same value through both readers is deliberate: the typed binder covers the settings that have an
    /// option of their own, and the legacy adapter covers the rest — the type names that select an
    /// implementation, and the knobs of components that have no options type at all. Splitting the
    /// sections between the two readers instead leaves anything that falls between them read by nobody,
    /// which is how a documented <c>JobStore:Type</c> came to be accepted and then ignored.
    /// </para>
    /// <para>
    /// The two readers do disagree about the shape of a duration — <c>00:00:30</c> to the binder, a count
    /// of milliseconds to the legacy format. That is settled in the adapter, which accepts both
    /// spellings, and by ordering: the adapter is applied after the binder, so its reading stands.
    /// </para>
    /// </remarks>
    private static NameValueCollection LegacyProperties(IConfiguration configuration)
    {
        var properties = new NameValueCollection();
        QuartzConfigurationHelper.PopulateProperties(configuration, properties);
        return properties;
    }

    private static bool HasDirectSchedulerConfiguration(IConfiguration configuration)
    {
        foreach (var child in configuration.GetChildren())
        {
            if (string.Equals(child.Key, "Schedulers", StringComparison.OrdinalIgnoreCase)
                || string.Equals(child.Key, "Schedule", StringComparison.OrdinalIgnoreCase)
                || string.Equals(child.Key, "Scheduling", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Registers one scheduler: its services, its configuration, and whatever the caller adds to it.
    /// </summary>
    /// <remarks>
    /// The six phases below, and why they are in this order, are documented on
    /// <see cref="AddQuartz(IServiceCollection, IConfiguration, Action{IQuartzBuilder})"/> — where a
    /// caller can read them.
    /// </remarks>
    private static IServiceCollection AddQuartzScheduler(
        IServiceCollection services,
        string? schedulerName,
        NameValueCollection properties,
        Action<IQuartzBuilder>? configure)
    {
        services.AddOptions();

        var optionsName = schedulerName ?? Options.DefaultName;

        // Phase 1.
        services.Configure<QuartzOptions>(optionsName, options =>
        {
            foreach (var key in properties.AllKeys)
            {
                if (key is not null)
                {
                    options.Properties[key] = properties[key];
                }
            }
        });

        // Phases 2 and 3: options are last-wins, so the property-derived ones go in before the callback.
        QuartzPropertyBridge.ApplyOptions(services, properties, schedulerName);
        QuartzPropertyBridge.ApplyFromQuartzOptions(services, schedulerName);

        // Phase 4.
        configure?.Invoke(new QuartzBuilder(services, schedulerName));

        // Phase 5: registration is first-wins, so the implementations named by keys go in after it.
        QuartzPropertyBridge.ApplyRegistrations(services, properties, schedulerName);

        // Phase 6.
        services.AddQuartzScheduler(schedulerName);

        if (schedulerName is not null)
        {
            SchedulerNameRegistry.For(services).Add(schedulerName);
        }

        return services;
    }
    /// <summary>
    /// Adds a job the scheduler should carry.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="TryRegisterJobType" path="/summary" />
    /// <inheritdoc cref="TryRegisterJobType" path="/remarks" />
    /// </remarks>
    /// <typeparam name="T">The job's type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">
    /// Configures the job. Its identity is set here with <c>WithIdentity</c>; a job given none gets a
    /// generated one, which a persistent store cannot recognise again on the next start.
    /// </param>
    public static IQuartzBuilder AddJob<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties)]
    T>(
        this IQuartzBuilder builder,
        Action<IJobConfigurator<T>> configure) where T : IJob
    {
        ArgumentNullException.ThrowIfNull(configure);
        return builder.AddJob<T>((_, jobConfigurator) => configure.Invoke(jobConfigurator));
    }

    /// <inheritdoc cref="AddJob{T}(IQuartzBuilder, Action{IJobConfigurator{T}})" />
    public static IQuartzBuilder AddJob<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties)]
    T>(
        this IQuartzBuilder builder,
        Action<IServiceProvider, IJobConfigurator<T>> configure) where T : IJob
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        TryRegisterJobType(builder.Services, typeof(T));

        SchedulerContent.Register(builder.Services, builder.SchedulerName, serviceProvider =>
            new SchedulerContent().Add(
                ConfigureAndBuildJobDetail(serviceProvider, JobBuilder.Create<T>(), configure)));

        return builder;
    }

    /// <summary>
    /// Adds a job of a type only known at runtime.
    /// </summary>
    /// <inheritdoc cref="AddJob{T}(IQuartzBuilder, Action{IJobConfigurator{T}})" path="/remarks" />
    /// <param name="builder">The builder.</param>
    /// <param name="jobType">The job's type, which must implement <see cref="IJob"/>.</param>
    /// <param name="configure">
    /// Configures the job. Its identity is set here with <c>WithIdentity</c>; a job given none gets a
    /// generated one, which a persistent store cannot recognise again on the next start.
    /// </param>
    public static IQuartzBuilder AddJob(
        this IQuartzBuilder builder,
           [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties)]
        Type jobType,
        Action<IJobConfigurator<IJob>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return builder.AddJob(jobType, (_, jobConfigurator) => configure.Invoke(jobConfigurator));
    }

    /// <inheritdoc cref="AddJob(IQuartzBuilder, Type, Action{IJobConfigurator{IJob}})" />
    public static IQuartzBuilder AddJob(
        this IQuartzBuilder builder,
           [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties)]
        Type jobType,
        Action<IServiceProvider, IJobConfigurator<IJob>> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(jobType);
        ArgumentNullException.ThrowIfNull(configure);

        if (!typeof(IJob).IsAssignableFrom(jobType))
        {
            Throw.ArgumentException("jobType must implement the IJob interface", nameof(jobType));
        }

        TryRegisterJobType(builder.Services, jobType);

        SchedulerContent.Register(builder.Services, builder.SchedulerName, serviceProvider =>
            new SchedulerContent().Add(
                ConfigureAndBuildJobDetail(serviceProvider, JobBuilder.Create().OfType(jobType), configure)));

        return builder;
    }

    /// <summary>
    /// Adds a trigger for a job of a known type.
    /// </summary>
    /// <remarks>
    /// Naming the job type is what lets the trigger's job data name the job's properties. The trigger still
    /// has to be pointed at a job with <c>ForJob</c>, and since that is done by key here, nothing checks
    /// that the key resolves to a <typeparamref name="TJob" /> - the type names the properties, it does not
    /// pick the job. Use <c>AddTrigger&lt;IJob&gt;</c> for a trigger whose job data names nothing.
    /// </remarks>
    /// <typeparam name="TJob">The type of job the trigger fires.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">Configures the trigger.</param>
    public static IQuartzBuilder AddTrigger<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties)] TJob>(
        this IQuartzBuilder builder,
        Action<ITriggerConfigurator<TJob>> configure) where TJob : IJob
    {
        ArgumentNullException.ThrowIfNull(configure);
        return builder.AddTrigger<TJob>((_, triggerConfigurator) => configure.Invoke(triggerConfigurator));
    }

    /// <inheritdoc cref="AddTrigger{TJob}(IQuartzBuilder, Action{ITriggerConfigurator{TJob}})" />
    public static IQuartzBuilder AddTrigger<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties)] TJob>(
        this IQuartzBuilder builder,
        Action<IServiceProvider, ITriggerConfigurator<TJob>> configure) where TJob : IJob
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        SchedulerContent.Register(builder.Services, builder.SchedulerName, serviceProvider =>
        {
            var c = TriggerBuilder.Create<TJob>(serviceProvider.GetService<TimeProvider>());
            configure.Invoke(serviceProvider, c);
            var trigger = c.Build();

            if (trigger.JobKey is null)
            {
                throw new InvalidOperationException("Trigger hasn't been associated with a job");
            }

            return new SchedulerContent().Add(trigger);
        });

        return builder;
    }

    /// <summary>
    /// Adds a job together with the one trigger that fires it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The job takes the trigger's identity unless it is given one of its own, so a job and its only
    /// trigger can be referred to by a single name.
    /// </para>
    /// <inheritdoc cref="TryRegisterJobType" path="/summary" />
    /// <inheritdoc cref="TryRegisterJobType" path="/remarks" />
    /// </remarks>
    /// <typeparam name="T">The job's type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="trigger">Configures the trigger.</param>
    /// <param name="job">Configures the job, which most schedules do not need to.</param>
    public static IQuartzBuilder ScheduleJob<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties)]
    T>(
        this IQuartzBuilder builder,
        Action<ITriggerConfigurator<T>> trigger,
        Action<IJobConfigurator<T>>? job = null) where T : IJob
    {
        ArgumentNullException.ThrowIfNull(trigger);
        return builder.ScheduleJob<T>((_, triggerConfigurator) => trigger(triggerConfigurator), (_, jobConfigurator) => job?.Invoke(jobConfigurator));
    }

    /// <inheritdoc cref="ScheduleJob{T}(IQuartzBuilder, Action{ITriggerConfigurator{T}}, Action{IJobConfigurator{T}})" />
    public static IQuartzBuilder ScheduleJob<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.PublicProperties)]
    T>(
        this IQuartzBuilder builder,
        Action<IServiceProvider, ITriggerConfigurator<T>> trigger,
        Action<IServiceProvider, IJobConfigurator<T>>? job = null) where T : IJob
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(trigger);

        TryRegisterJobType(builder.Services, typeof(T));

        // One registration carrying both, because the job's key may be derived from the trigger's: built
        // as two independent registrations they could not agree on it.
        SchedulerContent.Register(builder.Services, builder.SchedulerName, serviceProvider =>
        {
            var jobBuilder = JobBuilder.Create<T>();
            job?.Invoke(serviceProvider, jobBuilder);

            var triggerBuilder = TriggerBuilder.Create<T>(serviceProvider.GetService<TimeProvider>());

            // Pointed at the job before the caller configures the trigger, so a ForJob of their own
            // still wins — and is then checked below rather than silently ignored.
            if (jobBuilder.Key is not null)
            {
                triggerBuilder.ForJob(jobBuilder.Key);
            }

            trigger.Invoke(serviceProvider, triggerBuilder);
            var builtTrigger = triggerBuilder.Build();

            if (jobBuilder.Key is null)
            {
                // The job was given no identity of its own, so it takes the trigger's. That is only
                // knowable once the trigger is built, because a trigger given no identity is generated
                // one there; the builder keeps it, so building again produces the same trigger with the
                // job it now points at.
                jobBuilder.WithIdentity(builtTrigger.Key.Name, builtTrigger.Key.Group);
                triggerBuilder.ForJob(new JobKey(builtTrigger.Key.Name, builtTrigger.Key.Group));
                builtTrigger = triggerBuilder.Build();
            }

            var jobDetail = jobBuilder.Build();

            if (builtTrigger.JobKey is null || !builtTrigger.JobKey.Equals(jobDetail.Key))
            {
                Throw.InvalidOperationException("Trigger doesn't refer to job being scheduled");
            }

            return new SchedulerContent().Add(jobDetail).Add(builtTrigger);
        });

        return builder;
    }

    private static IJobDetail ConfigureAndBuildJobDetail<TJob>(
        IServiceProvider serviceProvider,
        JobBuilder<TJob> builder,
        Action<IServiceProvider, IJobConfigurator<TJob>> configure) where TJob : IJob
    {
        configure.Invoke(serviceProvider, builder);
        return builder.Build();
    }

    /// <summary>
    /// Registers the job type with the container, so a dependency it cannot be given is reported when
    /// the container is validated rather than when the trigger fires.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The registration is <em>scoped</em>, matching the lifetime the job factory resolves with: a scope
    /// is opened per fire, the job is resolved from it, and the scope is disposed once the job returns.
    /// A singleton would serve every fire from one instance and capture the scoped dependencies handed
    /// to the first one; a transient would leave two resolutions inside one fire — the job and something
    /// it injects — disagreeing about which unit of work they are in.
    /// </para>
    /// <para>
    /// It is a <c>TryAdd</c>, so a registration the application made itself — with its own lifetime,
    /// factory or implementation type — is kept, and adding the same job twice is harmless.
    /// </para>
    /// </remarks>
    private static void TryRegisterJobType(
        IServiceCollection services,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type jobType)
    {
        // A job named by an interface or an abstract type is one the container could not construct
        // anyway, and registering it would turn a job the factory can still activate into a startup
        // failure. JobBuilder rejects it when the job detail is built.
        if (jobType.IsAbstract || jobType.IsInterface)
        {
            return;
        }

        services.TryAddScoped(jobType);
    }

    /// <summary>
    /// Adds a calendar the scheduler should carry, which triggers exclude days with.
    /// </summary>
    /// <typeparam name="T">The calendar's type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="name">The name triggers refer to the calendar by.</param>
    /// <param name="options">How the calendar is added: whether it replaces one of the same name, and
    /// whether triggers using that name are recomputed. Defaults to replacing nothing.</param>
    /// <param name="configure">Configures the calendar, which is created with its default constructor.</param>
    public static IQuartzBuilder AddCalendar<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        this IQuartzBuilder builder,
        string name,
        AddCalendarOptions options = default,
        Action<T>? configure = null) where T : ICalendar, new()
    {
        return builder.AddCalendar<T>(name, options, (_, calendar) => configure?.Invoke(calendar));
    }

    /// <inheritdoc cref="AddCalendar{T}(IQuartzBuilder, string, AddCalendarOptions, Action{T})" />
    public static IQuartzBuilder AddCalendar<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        this IQuartzBuilder builder,
        string name,
        AddCalendarOptions options,
        Action<IServiceProvider, T> configure) where T : ICalendar, new()
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        SchedulerContentRegistration.Add(builder, serviceProvider =>
        {
            var calendar = new T();
            configure(serviceProvider, calendar);

            return new CalendarConfiguration(name, calendar, options);
        });
        return builder;
    }

    /// <summary>
    /// Adds a calendar the caller has already built.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="name">The name triggers refer to the calendar by.</param>
    /// <param name="calendar">The calendar.</param>
    /// <param name="options">How the calendar is added: whether it replaces one of the same name, and
    /// whether triggers using that name are recomputed. Defaults to replacing nothing.</param>
    public static IQuartzBuilder AddCalendar(
        this IQuartzBuilder builder,
        string name,
        ICalendar calendar,
        AddCalendarOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(calendar);

        SchedulerContentRegistration.Add(builder, new CalendarConfiguration(name, calendar, options));
        return builder;
    }
}