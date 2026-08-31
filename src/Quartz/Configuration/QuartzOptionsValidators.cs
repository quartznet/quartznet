using System.Collections.Frozen;
using System.Reflection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Impl;
using Quartz.Util;

namespace Quartz.Configuration;

/// <summary>
/// Validates <see cref="QuartzSchedulerOptions"/>, replacing the ad-hoc checks previously scattered
/// through scheduler instantiation.
/// </summary>
internal sealed class QuartzSchedulerOptionsValidator : IValidateOptions<QuartzSchedulerOptions>
{
    private static readonly TimeSpan minimumIdleWaitTime = TimeSpan.FromSeconds(1);

    private readonly IOptionsMonitor<ThreadPoolOptions> threadPoolOptions;

    public QuartzSchedulerOptionsValidator(IOptionsMonitor<ThreadPoolOptions> threadPoolOptions)
    {
        this.threadPoolOptions = threadPoolOptions;
    }

    public ValidateOptionsResult Validate(string? name, QuartzSchedulerOptions options)
    {
        List<string>? failures = null;

        if (string.IsNullOrWhiteSpace(options.InstanceName))
        {
            (failures ??= []).Add($"{nameof(QuartzSchedulerOptions.InstanceName)} must not be empty.");
        }

        if (!options.GenerateInstanceId && string.IsNullOrWhiteSpace(options.InstanceId))
        {
            (failures ??= []).Add(
                $"{nameof(QuartzSchedulerOptions.InstanceId)} must not be empty unless " +
                $"{nameof(QuartzSchedulerOptions.GenerateInstanceId)} is enabled.");
        }

        // Bounded below and not above. The idle wait is spent on a SemaphoreSlim, so that a scheduling
        // change can cut it short, and a semaphore takes a timeout of any length — unlike the timer the
        // durations in TimerLimits are checked against. A wait of years is a strange thing to configure,
        // but it is not a thing that breaks.
        if (options.IdleWaitTime < minimumIdleWaitTime)
        {
            (failures ??= []).Add(
                $"{nameof(QuartzSchedulerOptions.IdleWaitTime)} must be at least {minimumIdleWaitTime.TotalMilliseconds}ms, " +
                $"was {options.IdleWaitTime.TotalMilliseconds}ms.");
        }

        if (options.MaxBatchSize < 1)
        {
            (failures ??= []).Add(
                $"{nameof(QuartzSchedulerOptions.MaxBatchSize)} must be at least 1, was {options.MaxBatchSize}.");
        }
        else
        {
            // Acquiring more triggers than there are threads to run them on does not make the surplus
            // fire sooner: it makes them this node's, unfireable by any other, until the pool drains.
            // The two are configured through different builder methods and different sections, so the
            // pair is only ever wrong by accident.
            var maxConcurrency = threadPoolOptions.Get(name ?? Options.DefaultName).MaxConcurrency;
            if (maxConcurrency >= 1 && options.MaxBatchSize > maxConcurrency)
            {
                (failures ??= []).Add(
                    $"{nameof(QuartzSchedulerOptions.MaxBatchSize)} is {options.MaxBatchSize}, which is more than the "
                    + $"thread pool's {nameof(ThreadPoolOptions.MaxConcurrency)} of {maxConcurrency}. Triggers acquired "
                    + "beyond the number of threads available to run them are held by this node until the pool drains.");
            }
        }

        if (options.BatchTriggerAcquisitionFireAheadTimeWindow < TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(QuartzSchedulerOptions.BatchTriggerAcquisitionFireAheadTimeWindow)} must not be negative.");
        }

        return Result(failures);
    }

    internal static ValidateOptionsResult Result(List<string>? failures)
    {
        return failures is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>
/// Refuses a default scheduler configured with the name of a scheduler registered by
/// <c>AddQuartz(name, …)</c>.
/// </summary>
/// <remarks>
/// <see cref="SchedulerNameRegistry"/> only sees the named registrations, so this is the one collision it
/// cannot catch where it is written. Without it the two schedulers agree right up until the second one
/// binds itself, and the report is an <c>ArgumentException</c> about a duplicate name arriving from
/// somewhere inside host start — naming neither of the two calls that disagreed.
/// </remarks>
internal sealed class DefaultSchedulerNameValidator : IValidateOptions<QuartzSchedulerOptions>
{
    private readonly SchedulerNameRegistry registry;

    public DefaultSchedulerNameValidator(SchedulerNameRegistry registry)
    {
        this.registry = registry;
    }

    public ValidateOptionsResult Validate(string? name, QuartzSchedulerOptions options)
    {
        // Only the default scheduler can collide this way. A named scheduler's instance name is pinned to
        // the name it was registered under, and the registry has already refused a second registration
        // under that name.
        if (!string.IsNullOrEmpty(name))
        {
            return ValidateOptionsResult.Skip;
        }

        if (registry.Find(options.InstanceName) is not { } registered)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            $"AddQuartz() configured InstanceName '{options.InstanceName}' but AddQuartz(\"{registered}\", ...) "
            + "is also registered, and two schedulers cannot share a name — the repository indexes them by "
            + "name, ignoring case. Give the default scheduler a different InstanceName, or move what "
            + "AddQuartz() configures onto the named scheduler and register only that one.");
    }
}

/// <summary>
/// Refuses a registered job type whose constructor takes one of a scheduler's own parts.
/// </summary>
/// <remarks>
/// <para>
/// A job type the container holds is built by the <em>container</em>, which resolves constructor
/// parameters without a service key — so a job on scheduler <c>acme</c> taking an
/// <see cref="ISchedulerFactory"/> is handed the default scheduler's, and in a container holding only
/// named schedulers cannot be built at all. A job type the container does not hold is activated by the
/// job factory through <see cref="SchedulerScopedServiceProvider"/> and is handed its own scheduler's
/// parts. Registering a job — the documented, recommended thing to do — therefore changed which
/// scheduler's collaborators it saw, and nothing about the job said which path it was on.
/// </para>
/// <para>
/// The rule this enforces is the one the documentation already gives: a registered job does not take a
/// scheduler's parts by constructor. It reads the scheduler running it from
/// <see cref="IJobExecutionContext.Scheduler"/>, the firing from
/// <see cref="IJobExecutionContextAccessor"/>, and where it genuinely has to be <em>constructed</em>
/// with something of its scheduler's it is registered with <c>AddJobType&lt;T&gt;(factory)</c> and
/// resolves that part by key inside the factory — which is why a registration that builds the job with
/// a factory, or hands one over ready made, is not examined here.
/// </para>
/// <para>
/// Every public constructor is examined rather than the one the container would pick. Which one that is
/// depends on what else the container holds, since a constructor is only chosen when every parameter of
/// it can be resolved: a job whose clean constructor is chosen today is chosen differently the moment an
/// unrelated registration appears, and the trap would be back without the job having changed. A job that
/// must not be built with a scheduler's parts therefore does not declare a constructor taking them.
/// </para>
/// <para>
/// It runs wherever <see cref="QuartzSchedulerOptions"/> are created for a scheduler, which on a host is
/// <c>ValidateOnStart</c> and in a container built by <see cref="QuartzSchedulerBuilder"/> — where
/// nothing runs the startup validation — is when the scheduler is first built.
/// </para>
/// </remarks>
internal sealed class RegisteredJobConstructorValidator : IValidateOptions<QuartzSchedulerOptions>
{
    /// <summary>
    /// The service types belonging to one scheduler rather than to the container.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="SchedulerScopedServiceProvider"/>'s own list, so the two cannot drift apart, plus
    /// <see cref="IScheduler"/> — registered per scheduler but resolved by key rather than through that
    /// wrapper — plus every shape of the options types a scheduler owns, whose unnamed members read the
    /// default scheduler's instance.
    /// </para>
    /// <para>
    /// <see cref="TimeProvider"/> is deliberately absent although the wrapper routes it too: a scheduler
    /// given no clock of its own inherits the container's, injecting one is what the rest of this
    /// repository asks code to do rather than reading a clock statically, and refusing it would cost far
    /// more than the case it would catch.
    /// </para>
    /// </remarks>
    private static readonly FrozenSet<Type> schedulerParts = SchedulerParts();

    private readonly RegisteredJobTypes jobTypes;
    private readonly IEnumerable<SchedulerNamedOptions> pluginOptions;
    private HashSet<Type>? declaredPluginOptions;

    public RegisteredJobConstructorValidator(
        RegisteredJobTypes jobTypes,
        IEnumerable<SchedulerNamedOptions> pluginOptions)
    {
        this.jobTypes = jobTypes;
        this.pluginOptions = pluginOptions;
    }

    public ValidateOptionsResult Validate(string? name, QuartzSchedulerOptions options)
    {
        List<string>? failures = null;
        string schedulerName = name ?? Options.DefaultName;

        foreach (ServiceDescriptor registration in jobTypes.Registrations(schedulerName))
        {
            Type? implementationType = RegisteredJobTypes.ImplementationType(registration);
            if (implementationType is null)
            {
                continue;
            }

            foreach (ConstructorInfo constructor in implementationType.GetConstructors())
            {
                foreach (ParameterInfo parameter in constructor.GetParameters())
                {
                    if (!IsSchedulerPart(parameter.ParameterType))
                    {
                        continue;
                    }

                    string failure = Failure(schedulerName, registration.ServiceType, implementationType, parameter);

                    // Two constructors can take the same part under the same name, and the report names
                    // the job and the parameter rather than which constructor it was found on.
                    failures ??= [];
                    if (!failures.Contains(failure))
                    {
                        failures.Add(failure);
                    }
                }
            }
        }

        return QuartzSchedulerOptionsValidator.Result(failures);
    }

    private static FrozenSet<Type> SchedulerParts()
    {
        HashSet<Type> parts = [.. SchedulerScopedServiceProvider.SchedulerScopedServiceTypes, typeof(IScheduler)];

        foreach (Type optionsService in SchedulerScopedServiceProvider.DeclareQuartzOptions().Keys)
        {
            parts.Add(optionsService);
        }

        return FrozenSet.ToFrozenSet(parts);
    }

    private bool IsSchedulerPart(Type parameterType)
    {
        return schedulerParts.Contains(parameterType) || IsPluginOptions(parameterType);
    }

    /// <summary>
    /// Whether the parameter is one shape of an options type a plugin declared as its scheduler's.
    /// </summary>
    /// <remarks>
    /// The static set cannot list these: the type comes from the plugin rather than from Quartz.
    /// <c>AddPlugin&lt;T, TOptions&gt;</c> registers a declaration instead, and a declaration names the
    /// same three closed generic services <see cref="SchedulerScopedServiceProvider"/> answers.
    /// </remarks>
    private bool IsPluginOptions(Type parameterType)
    {
        // Every options service is a closed generic over IOptions<>, IOptionsMonitor<> or
        // IOptionsSnapshot<>, so anything else is turned away before the declarations are read.
        if (!parameterType.IsConstructedGenericType)
        {
            return false;
        }

        return (declaredPluginOptions ??= DeclarePluginOptions()).Contains(parameterType);
    }

    private HashSet<Type> DeclarePluginOptions()
    {
        Dictionary<Type, Func<IServiceProvider, string, object>> declared = [];

        foreach (SchedulerNamedOptions options in pluginOptions)
        {
            options.DeclareInto(declared);
        }

        return [.. declared.Keys];
    }

    private static string Failure(
        string schedulerName,
        Type jobType,
        Type implementationType,
        ParameterInfo parameter)
    {
        string scheduler = string.IsNullOrEmpty(schedulerName)
            ? "the default scheduler"
            : $"scheduler '{schedulerName}'";

        string built = implementationType == jobType ? "" : $", built as {TypeName(implementationType)},";

        return $"Job type {TypeName(jobType)}{built} is registered on {scheduler}, and its constructor takes "
            + $"{TypeName(parameter.ParameterType)} {parameter.Name} — a part that belongs to one scheduler. "
            + "A registered job type is built by the container, which resolves constructor parameters without "
            + "a scheduler's service key: what the job is handed is the unkeyed registration — the default "
            + "scheduler's — whichever scheduler the job belongs to, and in a container holding only named "
            + "schedulers there is no unkeyed registration to hand it. Read the scheduler running the job from "
            + "IJobExecutionContext.Scheduler, take IJobExecutionContextAccessor for the firing it is part of, "
            + $"or register the job with AddJobType<{TypeName(jobType)}>(provider => ...) and resolve the part "
            + "by key inside that factory.";
    }

    /// <summary>
    /// A type as it is spelled in source, one level of generic arguments deep — which is as deep as
    /// <c>IOptions&lt;QuartzSchedulerOptions&gt;</c>, the only generic shape that reaches here, goes.
    /// </summary>
    private static string TypeName(Type type)
    {
        if (!type.IsConstructedGenericType)
        {
            return type.Name;
        }

        string name = type.Name;
        int arity = name.IndexOf('`', StringComparison.Ordinal);
        string[] arguments = Array.ConvertAll(type.GetGenericArguments(), static argument => argument.Name);

        return $"{(arity < 0 ? name : name[..arity])}<{string.Join(", ", arguments)}>";
    }
}

/// <summary>
/// Validates <see cref="ThreadPoolOptions"/>.
/// </summary>
internal sealed class ThreadPoolOptionsValidator : IValidateOptions<ThreadPoolOptions>
{
    public ValidateOptionsResult Validate(string? name, ThreadPoolOptions options)
    {
        if (options.MaxConcurrency < 1)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(ThreadPoolOptions.MaxConcurrency)} must be at least 1, was {options.MaxConcurrency}.");
        }

        return ValidateOptionsResult.Success;
    }
}

/// <summary>
/// Refuses a type loader alias that maps a name onto nothing, because such an alias is a rename that
/// has not happened.
/// </summary>
/// <remarks>
/// <para>
/// An alias is only ever consulted for a name that is about to be turned into a type — a stored
/// <c>JOB_CLASS_NAME</c>, most of the time — so an alias whose target does not resolve fails on the
/// first job that needs it, in a <c>TypeLoadException</c> naming the <em>old</em> name and nothing
/// about the mapping that was supposed to save it. Checked here instead, it is a startup failure that
/// names both halves of the entry that is wrong.
/// </para>
/// <para>
/// The target is resolved the way the loader resolves it, so a target may itself be spelled with a
/// pre-4.0 name; only the key is left alone, since a key that no longer resolves is exactly the point.
/// A blank key is refused as well: it would prefix-match every name the loader is ever asked for.
/// </para>
/// </remarks>
internal sealed class TypeLoaderOptionsValidator : IValidateOptions<TypeLoaderOptions>
{
    public ValidateOptionsResult Validate(string? name, TypeLoaderOptions options)
    {
        List<string>? failures = null;

        foreach ((string alias, string? target) in options.Aliases)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                (failures ??= []).Add(
                    $"{nameof(TypeLoaderOptions.Aliases)} contains a blank name, mapped to '{target}'. An alias is "
                    + "matched against the start of every type name Quartz resolves, so a blank one would claim all "
                    + "of them; give it the name as it is stored or configured.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(target))
            {
                (failures ??= []).Add(
                    $"Type loader alias '{alias}' maps to a blank type name. Give it the name of the type that "
                    + "replaced it, or remove the entry.");
                continue;
            }

            if (!SimpleTypeLoader.CanResolve(target))
            {
                (failures ??= []).Add(
                    $"Type loader alias '{alias}' maps to '{target}', which names no type this application can "
                    + "load. The target is the type as it is called now, assembly-qualified — "
                    + "'Namespace.TypeName, AssemblyName' — and the assembly has to be one this application "
                    + "references; the alias is the dead name, and is not checked.");
            }
        }

        return QuartzSchedulerOptionsValidator.Result(failures);
    }
}

/// <summary>
/// Validates <see cref="InMemoryJobStoreOptions"/>.
/// </summary>
internal sealed class InMemoryJobStoreOptionsValidator : IValidateOptions<InMemoryJobStoreOptions>
{
    public ValidateOptionsResult Validate(string? name, InMemoryJobStoreOptions options)
    {
        if (options.MisfireThreshold < TimeSpan.FromMilliseconds(1))
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(InMemoryJobStoreOptions.MisfireThreshold)} must be at least 1ms.");
        }

        return ValidateOptionsResult.Success;
    }
}

/// <summary>
/// Validates <see cref="AdoJobStoreOptions"/>.
/// </summary>
internal sealed class AdoJobStoreOptionsValidator : IValidateOptions<AdoJobStoreOptions>
{
    public ValidateOptionsResult Validate(string? name, AdoJobStoreOptions options)
    {
        List<string>? failures = null;

        if (string.IsNullOrWhiteSpace(options.DataSource))
        {
            (failures ??= []).Add($"{nameof(AdoJobStoreOptions.DataSource)} must be specified for a persistent job store.");
        }

        if (options.MisfireThreshold < TimeSpan.FromMilliseconds(1))
        {
            (failures ??= []).Add($"{nameof(AdoJobStoreOptions.MisfireThreshold)} must be at least 1ms.");
        }

        // How long the misfire handler sleeps between passes has two spellings, and both of them reach
        // the same Task.Delay: the frequency when it is set, and the threshold when it is not. The
        // ceiling is checked against whichever one the application actually wrote, so the failure names
        // a setting that is in its configuration.
        if (options.MisfireHandlerFrequency is { } frequency)
        {
            if (frequency < TimeSpan.FromMilliseconds(1))
            {
                (failures ??= []).Add($"{nameof(AdoJobStoreOptions.MisfireHandlerFrequency)} must be at least 1ms when set.");
            }
            else if (frequency > TimerLimits.MaxDelay)
            {
                (failures ??= []).Add(TimerLimits.TooLong(
                    nameof(AdoJobStoreOptions.MisfireHandlerFrequency),
                    frequency,
                    TimerLimits.MaxDelay,
                    "The misfire handler sleeps for it between passes, and a delay longer than this is "
                    + "refused by the timer rather than by the store — which is why an unbounded value "
                    + "used to be reported out of Shutdown."));
            }
        }
        else if (options.MisfireThreshold > TimerLimits.MaxDelay)
        {
            (failures ??= []).Add(TimerLimits.TooLong(
                nameof(AdoJobStoreOptions.MisfireThreshold),
                options.MisfireThreshold,
                TimerLimits.MaxDelay,
                $"{nameof(AdoJobStoreOptions.MisfireHandlerFrequency)} is unset, so this is also how long "
                + $"the misfire handler sleeps between passes. Set {nameof(AdoJobStoreOptions.MisfireHandlerFrequency)} "
                + "to keep a threshold this long."));
        }

        if (options.DbRetryInterval < TimeSpan.Zero)
        {
            (failures ??= []).Add($"{nameof(AdoJobStoreOptions.DbRetryInterval)} must not be negative.");
        }
        else if (options.DbRetryInterval > TimerLimits.MaxDelay)
        {
            (failures ??= []).Add(TimerLimits.TooLong(
                nameof(AdoJobStoreOptions.DbRetryInterval),
                options.DbRetryInterval,
                TimerLimits.MaxDelay,
                "The store waits it out after a database failure, and the misfire handler and the cluster "
                + "manager both sleep for it while their last pass is failing."));
        }

        if (options.TransientRetryInterval < TimeSpan.Zero)
        {
            (failures ??= []).Add($"{nameof(AdoJobStoreOptions.TransientRetryInterval)} must not be negative.");
        }
        else if (options.TransientRetryInterval > TimerLimits.MaxDelay)
        {
            (failures ??= []).Add(TimerLimits.TooLong(
                nameof(AdoJobStoreOptions.TransientRetryInterval),
                options.TransientRetryInterval,
                TimerLimits.MaxDelay,
                "It is waited out between the retries of a transiently failed statement."));
        }

        // Zero is not "no timeout" here even though ADO.NET reads it that way, because nothing in the
        // store wants to wait forever; a caller who does leaves this unset and gets the provider's own
        // default.
        if (options.CommandTimeout is { } commandTimeout && commandTimeout <= TimeSpan.Zero)
        {
            (failures ??= []).Add($"{nameof(AdoJobStoreOptions.CommandTimeout)} must be positive when set.");
        }

        if (options.MaxMisfiresToHandleAtATime < 1)
        {
            (failures ??= []).Add($"{nameof(AdoJobStoreOptions.MaxMisfiresToHandleAtATime)} must be at least 1.");
        }

        return QuartzSchedulerOptionsValidator.Result(failures);
    }
}

/// <summary>
/// Validates <see cref="ClusteringOptions"/>.
/// </summary>
internal sealed class ClusteringOptionsValidator : IValidateOptions<ClusteringOptions>
{
    public ValidateOptionsResult Validate(string? name, ClusteringOptions options)
    {
        List<string>? failures = null;

        if (options.CheckinInterval <= TimeSpan.Zero)
        {
            (failures ??= []).Add($"{nameof(ClusteringOptions.CheckinInterval)} must be positive.");
        }
        else if (options.CheckinInterval > TimerLimits.MaxDelay)
        {
            (failures ??= []).Add(TimerLimits.TooLong(
                nameof(ClusteringOptions.CheckinInterval),
                options.CheckinInterval,
                TimerLimits.MaxDelay,
                "The cluster manager sleeps for it between check-ins."));
        }

        if (options.CheckinMisfireThreshold < TimeSpan.Zero)
        {
            (failures ??= []).Add($"{nameof(ClusteringOptions.CheckinMisfireThreshold)} must not be negative.");
        }

        return QuartzSchedulerOptionsValidator.Result(failures);
    }
}

/// <summary>
/// Refuses a scheduler that asked for clustering and then turned it off.
/// </summary>
/// <remarks>
/// Registered by <c>UseClustering</c>, so it exists only for a scheduler that asked. Turning
/// <see cref="ClusteringOptions.Enabled"/> off inside the callback — or in a later
/// <c>Configure&lt;ClusteringOptions&gt;</c> — leaves the store with database locking on, no cluster
/// manager and no check-in row, which is a configuration nobody means to write. Not clustering at all
/// is spelled by not calling <c>UseClustering</c>.
/// </remarks>
internal sealed class ClusteringStaysEnabledValidator : IValidateOptions<ClusteringOptions>
{
    private readonly string optionsName;

    public ClusteringStaysEnabledValidator(string optionsName)
    {
        this.optionsName = optionsName;
    }

    public ValidateOptionsResult Validate(string? name, ClusteringOptions options)
    {
        // Only this scheduler's options; another scheduler in the same container may legitimately have
        // clustering off.
        if (!string.Equals(name ?? Options.DefaultName, optionsName, StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Skip;
        }

        if (options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            $"{nameof(ClusteringOptions.Enabled)} is false, but UseClustering() was called for this "
            + "scheduler. Remove the UseClustering() call to run un-clustered; setting Enabled to false "
            + "inside it leaves database locking on with no cluster manager and no check-in.");
    }
}

/// <summary>
/// Validates <see cref="QuartzHostedServiceOptions"/>.
/// </summary>
/// <remarks>
/// The start delay is the one setting here that can be wrong rather than merely unusual, and its
/// symptom is the worst of the durations Quartz waits out: <c>StartDelayed</c> runs its wait on a task
/// nobody observes, so a delay the timer refuses faults that task, is collected unnoticed, and leaves
/// a scheduler that was created, bound and reported healthy and simply never starts.
/// </remarks>
internal sealed class QuartzHostedServiceOptionsValidator : IValidateOptions<QuartzHostedServiceOptions>
{
    public ValidateOptionsResult Validate(string? name, QuartzHostedServiceOptions options)
    {
        if (options.StartDelay is not { } delay)
        {
            return ValidateOptionsResult.Success;
        }

        if (delay < TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(QuartzHostedServiceOptions.StartDelay)} must not be negative. Leave it unset to "
                + "start the scheduler as soon as the host does.");
        }

        if (delay > TimerLimits.MaxDelay)
        {
            return ValidateOptionsResult.Fail(TimerLimits.TooLong(
                nameof(QuartzHostedServiceOptions.StartDelay),
                delay,
                TimerLimits.MaxDelay,
                "The hosted service waits it out before starting the scheduler. Set "
                + $"{nameof(QuartzHostedServiceOptions.AutoStart)} to false and start the scheduler yourself "
                + "if it should wait longer than that."));
        }

        return ValidateOptionsResult.Success;
    }
}

/// <summary>
/// Validates <see cref="DataSourceOptions"/>.
/// </summary>
internal sealed class DataSourceOptionsValidator : IValidateOptions<DataSourceOptions>
{
    public ValidateOptionsResult Validate(string? name, DataSourceOptions options)
    {
        List<string>? failures = null;
        var described = string.IsNullOrEmpty(name) ? "data source" : $"data source '{name}'";

        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            (failures ??= []).Add($"{nameof(DataSourceOptions.Provider)} must be specified for {described}.");
        }

        // A DbDataSource carries its own connection details, so Quartz needs none — however the data
        // source is reached: built by a factory, resolved under a key of its own, or the container's
        // single unkeyed one.
        var suppliesItsOwnConnections = options.DataSourceFactory is not null
            || options.DataSourceServiceKey is not null
            || options.UseRegisteredDataSource;

        if (!suppliesItsOwnConnections
            && string.IsNullOrWhiteSpace(options.ConnectionString)
            && string.IsNullOrWhiteSpace(options.ConnectionStringName))
        {
            (failures ??= []).Add(
                $"Either {nameof(DataSourceOptions.ConnectionString)} or {nameof(DataSourceOptions.ConnectionStringName)} " +
                $"must be specified for {described}.");
        }

        return QuartzSchedulerOptionsValidator.Result(failures);
    }
}

/// <summary>
/// Refuses a key in <see cref="QuartzOptions.Properties"/> that is not a <c>quartz.*</c> key, because
/// nothing will ever read it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="QuartzOptions.Properties"/> is the flat legacy format, and <c>QuartzPropertyBridge</c> is
/// its only reader: it hands the bag to a <c>PropertyReader</c> that looks up <c>quartz.</c>-prefixed
/// keys and nothing else. A key without that prefix is therefore provably read by nobody, and today it
/// is accepted in silence — the scheduler runs as if the application had said nothing.
/// </para>
/// <para>
/// The commonest way to produce one is <c>services.Configure&lt;QuartzOptions&gt;(section)</c> against a
/// section written for a type that used to <em>be</em> a dictionary. That mis-bind is only partly
/// visible from here: a section whose keys match no property at all binds to nothing, and an options
/// instance that ended up empty is indistinguishable from one nobody configured — which is why the
/// check is on the keys that did arrive rather than on emptiness. What it catches is the half that is
/// decidable: <c>Quartz:Properties:scheduler.instanceName</c>, a key that lost its prefix on the way
/// through a section already called <c>Quartz</c>.
/// </para>
/// <para>
/// It deliberately does not check the key against the ones Quartz reads, which is what
/// <c>LegacyPropertyKeys.Validate</c> does for a bag a caller hands in directly. A configuration section
/// is flattened into this bag with every sub-section turned into a <c>quartz.*</c> key whether Quartz
/// reads it or not, so the stricter check would fail an <c>appsettings.json</c> holding settings for
/// something else. <c>quartz.checkConfiguration = false</c> turns this off too, since it is the existing
/// way to say "these keys are mine".
/// </para>
/// </remarks>
internal sealed class QuartzOptionsValidator : IValidateOptions<QuartzOptions>
{
    public ValidateOptionsResult Validate(string? name, QuartzOptions options)
    {
        if (options.Properties.Count == 0)
        {
            return ValidateOptionsResult.Success;
        }

        if (options.Properties.TryGetValue(LegacyPropertyKeys.CheckConfiguration, out string? check)
            && bool.TryParse(check, out bool enabled)
            && !enabled)
        {
            return ValidateOptionsResult.Skip;
        }

        List<string>? failures = null;

        foreach (string key in options.Properties.Keys)
        {
            if (key.StartsWith(LegacyPropertyKeys.Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            (failures ??= []).Add(
                $"'{key}' is in QuartzOptions.Properties, which holds the flat {LegacyPropertyKeys.Prefix}* keys and is read "
                + $"by nothing else, so a key without that prefix is never read. Spell it '{LegacyPropertyKeys.Prefix}{key}' "
                + "if it is a Quartz setting — a section already named Quartz does not supply the prefix — or set "
                + $"'{LegacyPropertyKeys.CheckConfiguration}' to false if the key is yours. Settings that have a typed "
                + "option belong on that option rather than here; see the migration guide.");
        }

        return QuartzSchedulerOptionsValidator.Result(failures);
    }
}
