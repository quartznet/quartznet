using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Util;

namespace Quartz;

/// <summary>
/// Adds jobs, triggers and calendars to a scheduler, and selects the built-in type loader.
/// </summary>
/// <remarks>
/// These extend <see cref="IQuartzBuilder"/>, so they read the same whether the scheduler is being
/// registered with <c>AddQuartz</c> or built by <see cref="QuartzSchedulerBuilder"/>. They are
/// extension methods rather than interface members because each is a composition of what the
/// interface already offers, and an implementation of it should not have to reproduce them.
/// </remarks>
public static class QuartzBuilderExtensions
{
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
    /// Prepares the dependency injection scope each job is built in, so services that are scoped can be
    /// given the ambient context of the job about to run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The callback runs before the job is resolved, so anything it sets is in place while the job and
    /// everything it injects are constructed. It is synchronous by design: an asynchronous hook would be
    /// awaited, and the <see cref="System.Threading.ExecutionContext"/> restored on the way back would
    /// discard exactly the <see cref="System.Threading.AsyncLocal{T}"/> values it exists to set.
    /// </para>
    /// <para>
    /// Callbacks combine rather than replace, and run in the order they were added. This was previously
    /// reachable only by deriving from <c>MicrosoftDependencyInjectionJobFactory</c> and overriding a
    /// protected method, which is a great deal of ceremony for setting one ambient value.
    /// </para>
    /// </remarks>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">
    /// Prepares the scope, given the scope, the fire it was opened for and the scheduler firing it.
    /// </param>
    public static IQuartzBuilder ConfigureJobScope(
        this IQuartzBuilder builder,
        Action<IServiceScope, TriggerFiredBundle, IScheduler> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        return builder.ConfigureOptions<JobFactoryOptions>(options => options.ConfigureScope += configure);
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
            [DynamicallyAccessedMembers(JobTypeMembers.Required)]
    T>(
        this IQuartzBuilder builder,
        Action<IJobConfigurator<T>> configure) where T : IJob
    {
        ArgumentNullException.ThrowIfNull(configure);
        return builder.AddJob<T>((_, jobConfigurator) => configure.Invoke(jobConfigurator));
    }

    /// <inheritdoc cref="AddJob{T}(IQuartzBuilder, Action{IJobConfigurator{T}})" />
    public static IQuartzBuilder AddJob<
            [DynamicallyAccessedMembers(JobTypeMembers.Required)]
    T>(
        this IQuartzBuilder builder,
        Action<IServiceProvider, IJobConfigurator<T>> configure) where T : IJob
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        TryRegisterJobType(builder, typeof(T));

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
           [DynamicallyAccessedMembers(JobTypeMembers.Required)]
        Type jobType,
        Action<IJobConfigurator<IJob>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return builder.AddJob(jobType, (_, jobConfigurator) => configure.Invoke(jobConfigurator));
    }

    /// <inheritdoc cref="AddJob(IQuartzBuilder, Type, Action{IJobConfigurator{IJob}})" />
    public static IQuartzBuilder AddJob(
        this IQuartzBuilder builder,
           [DynamicallyAccessedMembers(JobTypeMembers.Required)]
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

        TryRegisterJobType(builder, jobType);

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
    public static IQuartzBuilder AddTrigger<[DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob>(
        this IQuartzBuilder builder,
        Action<ITriggerConfigurator<TJob>> configure) where TJob : IJob
    {
        ArgumentNullException.ThrowIfNull(configure);
        return builder.AddTrigger<TJob>((_, triggerConfigurator) => configure.Invoke(triggerConfigurator));
    }

    /// <inheritdoc cref="AddTrigger{TJob}(IQuartzBuilder, Action{ITriggerConfigurator{TJob}})" />
    public static IQuartzBuilder AddTrigger<[DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob>(
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
    /// Adds a trigger for a job added elsewhere, named by key.
    /// </summary>
    /// <remarks>
    /// The job type on <see cref="AddTrigger{TJob}(IQuartzBuilder, Action{ITriggerConfigurator{TJob}})" />
    /// is what lets a trigger's job data name the job's properties; a trigger that only points at a job
    /// with <c>ForJob</c> has no use for it, and this is that call without the <c>&lt;IJob&gt;</c>.
    /// </remarks>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">Configures the trigger, which must name its job with <c>ForJob</c>.</param>
    public static IQuartzBuilder AddTrigger(
        this IQuartzBuilder builder,
        Action<ITriggerConfigurator<IJob>> configure)
    {
        return builder.AddTrigger<IJob>(configure);
    }

    /// <inheritdoc cref="AddTrigger(IQuartzBuilder, Action{ITriggerConfigurator{IJob}})" />
    public static IQuartzBuilder AddTrigger(
        this IQuartzBuilder builder,
        Action<IServiceProvider, ITriggerConfigurator<IJob>> configure)
    {
        return builder.AddTrigger<IJob>(configure);
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
            [DynamicallyAccessedMembers(JobTypeMembers.Required)]
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
            [DynamicallyAccessedMembers(JobTypeMembers.Required)]
    T>(
        this IQuartzBuilder builder,
        Action<IServiceProvider, ITriggerConfigurator<T>> trigger,
        Action<IServiceProvider, IJobConfigurator<T>>? job = null) where T : IJob
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(trigger);

        TryRegisterJobType(builder, typeof(T));

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

    private static IJobDetail ConfigureAndBuildJobDetail<[DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob>(
        IServiceProvider serviceProvider,
        JobBuilder<TJob> builder,
        Action<IServiceProvider, IJobConfigurator<TJob>> configure) where TJob : IJob
    {
        configure.Invoke(serviceProvider, builder);
        return builder.Build();
    }

    /// <summary>
    /// Registers how <em>this</em> scheduler builds a job type, so two schedulers in one container can
    /// build the same job type differently.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="AddJob{T}(IQuartzBuilder, Action{IJobConfigurator{T}})"/> registers the job type with
    /// the container unkeyed, which is what a single-scheduler application wants and what container
    /// validation reads. Under a scheduler per tenant that one registration is shared: whichever
    /// implementation or lifetime was registered first is what every scheduler gets. This says the
    /// registration belongs to one scheduler, and the job factory looks there first.
    /// </para>
    /// <para>
    /// The registration is made under this scheduler's service key, or unkeyed for the default
    /// scheduler — whose registrations are the unkeyed ones, so an empty key would not be the same
    /// thing. It replaces rather than defers to what <c>AddJob</c> registered: naming the
    /// implementation is the whole point of the call.
    /// </para>
    /// <para>
    /// A job is built with <see cref="ServiceLifetime.Scoped"/> unless another lifetime is named, which
    /// matches the lifetime the job factory resolves with: a scope is opened per fire, the job is
    /// resolved from it, and the scope is disposed once the job returns.
    /// </para>
    /// <para>
    /// The lifetime is an overload rather than an optional parameter deliberately. A default value that
    /// is an enum from an assembly which only exists in a shared framework —
    /// <see cref="ServiceLifetime"/> is one — is a metadata constant whose type Cecil has to resolve to
    /// write it, and coverlet's resolver cannot: it fails to instrument the <em>whole</em> assembly and
    /// reports Quartz as untested, silently. One optional parameter cost the core assembly its entire
    /// coverage figure; overloads carry no constant.
    /// </para>
    /// </remarks>
    /// <typeparam name="TJob">The job type, as named on the job detail.</typeparam>
    /// <param name="builder">The builder.</param>
    public static IQuartzBuilder AddJobType<
            [DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob>(
        this IQuartzBuilder builder) where TJob : class, IJob
    {
        return builder.AddJobType<TJob, TJob>(ServiceLifetime.Scoped);
    }

    /// <inheritdoc cref="AddJobType{TJob}(IQuartzBuilder)" />
    /// <param name="builder">The builder.</param>
    /// <param name="lifetime">How long one instance lives.</param>
    public static IQuartzBuilder AddJobType<
            [DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob>(
        this IQuartzBuilder builder,
        ServiceLifetime lifetime) where TJob : class, IJob
    {
        return builder.AddJobType<TJob, TJob>(lifetime);
    }

    /// <summary>
    /// Registers the implementation this scheduler builds a job type with.
    /// </summary>
    /// <inheritdoc cref="AddJobType{TJob}(IQuartzBuilder)" path="/remarks" />
    /// <typeparam name="TJob">The job type, as named on the job detail.</typeparam>
    /// <typeparam name="TImplementation">The type actually constructed.</typeparam>
    /// <param name="builder">The builder.</param>
    public static IQuartzBuilder AddJobType<
            TJob,
            [DynamicallyAccessedMembers(JobTypeMembers.Required)] TImplementation>(
        this IQuartzBuilder builder)
        where TJob : class, IJob
        where TImplementation : class, TJob
    {
        return builder.AddJobType<TJob, TImplementation>(ServiceLifetime.Scoped);
    }

    /// <inheritdoc cref="AddJobType{TJob, TImplementation}(IQuartzBuilder)" />
    /// <param name="builder">The builder.</param>
    /// <param name="lifetime">How long one instance lives.</param>
    public static IQuartzBuilder AddJobType<
            TJob,
            [DynamicallyAccessedMembers(JobTypeMembers.Required)] TImplementation>(
        this IQuartzBuilder builder,
        ServiceLifetime lifetime)
        where TJob : class, IJob
        where TImplementation : class, TJob
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Add(Describe(builder, typeof(TJob), lifetime, implementationType: typeof(TImplementation)));
        RegisteredJobTypes.For(builder.Services).Add(builder.SchedulerName, typeof(TJob));
        return builder;
    }

    /// <summary>
    /// Registers how this scheduler constructs a job type, with a factory of your own.
    /// </summary>
    /// <inheritdoc cref="AddJobType{TJob}(IQuartzBuilder)" path="/remarks" />
    /// <remarks>
    /// The factory is handed the provider the job is being built from, which is the per-fire scope — so
    /// a scoped dependency resolved out of it belongs to that fire.
    /// </remarks>
    /// <typeparam name="TJob">The job type, as named on the job detail.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <param name="implementationFactory">Builds the job.</param>
    public static IQuartzBuilder AddJobType<TJob>(
        this IQuartzBuilder builder,
        Func<IServiceProvider, TJob> implementationFactory) where TJob : class, IJob
    {
        return builder.AddJobType(implementationFactory, ServiceLifetime.Scoped);
    }

    /// <inheritdoc cref="AddJobType{TJob}(IQuartzBuilder, Func{IServiceProvider, TJob})" />
    /// <param name="builder">The builder.</param>
    /// <param name="implementationFactory">Builds the job.</param>
    /// <param name="lifetime">How long one instance lives.</param>
    public static IQuartzBuilder AddJobType<TJob>(
        this IQuartzBuilder builder,
        Func<IServiceProvider, TJob> implementationFactory,
        ServiceLifetime lifetime) where TJob : class, IJob
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(implementationFactory);

        // Recorded like any other job-type registration even though a job built by a factory has no
        // constructor to check: what it says is that this scheduler builds TJob its own way, which is
        // what makes an AddJob<TJob> registration of the same type stop being the one that would be used.
        builder.Services.Add(Describe(builder, typeof(TJob), lifetime, factory: provider => implementationFactory(provider)));
        RegisteredJobTypes.For(builder.Services).Add(builder.SchedulerName, typeof(TJob));
        return builder;
    }

    /// <summary>
    /// Describes a job-type registration belonging to one scheduler: keyed by its name, or unkeyed for
    /// the default scheduler.
    /// </summary>
    private static ServiceDescriptor Describe(
        IQuartzBuilder builder,
        Type serviceType,
        ServiceLifetime lifetime,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
    {
        string? key = SchedulerServiceKey(builder);

        return key is null
            ? ServiceDescriptor.Describe(serviceType, implementationType, lifetime)
            : ServiceDescriptor.DescribeKeyed(serviceType, key, implementationType, lifetime);
    }

    /// <inheritdoc cref="Describe(IQuartzBuilder, Type, ServiceLifetime, Type)" />
    private static ServiceDescriptor Describe(
        IQuartzBuilder builder,
        Type serviceType,
        ServiceLifetime lifetime,
        Func<IServiceProvider, object> factory)
    {
        string? key = SchedulerServiceKey(builder);

        return key is null
            ? ServiceDescriptor.Describe(serviceType, factory, lifetime)
            : ServiceDescriptor.DescribeKeyed(serviceType, key, (provider, _) => factory(provider), lifetime);
    }

    /// <summary>
    /// The service key a scheduler's own registrations go under.
    /// </summary>
    /// <remarks>
    /// <c>Options.DefaultName</c> is the empty string, and <see cref="IQuartzBuilder.SchedulerName"/> is
    /// that for the default scheduler. A key of <c>""</c> is not the same as no key to a container, and
    /// the default scheduler's parts are the unkeyed registrations, so the two spellings cannot be
    /// collapsed.
    /// </remarks>
    private static string? SchedulerServiceKey(IQuartzBuilder builder)
    {
        return string.IsNullOrEmpty(builder.SchedulerName) ? null : builder.SchedulerName;
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
    /// <para>
    /// Being registered is also what makes the job the container's to build rather than the job
    /// factory's to activate, so the scheduler is recorded as having been given it and
    /// <see cref="RegisteredJobConstructorValidator"/> checks at startup that its constructor asks for
    /// nothing that belongs to one scheduler.
    /// </para>
    /// </remarks>
    private static void TryRegisterJobType(
        IQuartzBuilder builder,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type jobType)
    {
        // A job named by an interface or an abstract type is one the container could not construct
        // anyway, and registering it would turn a job the factory can still activate into a startup
        // failure. JobBuilder rejects it when the job detail is built.
        if (jobType.IsAbstract || jobType.IsInterface)
        {
            return;
        }

        builder.Services.TryAddScoped(jobType);
        RegisteredJobTypes.For(builder.Services).Add(builder.SchedulerName, jobType);
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
