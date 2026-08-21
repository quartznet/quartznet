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

        // A container-registered data source carries its own connection details, so Quartz needs none.
        if (!options.UseRegisteredDataSource
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
