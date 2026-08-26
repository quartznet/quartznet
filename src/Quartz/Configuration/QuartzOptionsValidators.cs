using System.Collections.Frozen;
using System.Reflection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

        if (options.MisfireHandlerFrequency is { } frequency && frequency < TimeSpan.FromMilliseconds(1))
        {
            (failures ??= []).Add($"{nameof(AdoJobStoreOptions.MisfireHandlerFrequency)} must be at least 1ms when set.");
        }

        if (options.DbRetryInterval < TimeSpan.Zero)
        {
            (failures ??= []).Add($"{nameof(AdoJobStoreOptions.DbRetryInterval)} must not be negative.");
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
