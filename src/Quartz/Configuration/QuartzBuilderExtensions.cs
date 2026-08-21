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
