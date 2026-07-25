using Microsoft.Extensions.Options;

namespace Quartz.Configuration;

/// <summary>
/// Validates <see cref="QuartzSchedulerOptions"/>, replacing the ad-hoc checks previously scattered
/// through scheduler instantiation.
/// </summary>
internal sealed class QuartzSchedulerOptionsValidator : IValidateOptions<QuartzSchedulerOptions>
{
    private static readonly TimeSpan minimumIdleWaitTime = TimeSpan.FromSeconds(1);

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

        if (options.Clustered && !options.UseDbLocks)
        {
            (failures ??= []).Add(
                $"{nameof(AdoJobStoreOptions.UseDbLocks)} must be enabled when {nameof(AdoJobStoreOptions.Clustered)} is enabled.");
        }

        return QuartzSchedulerOptionsValidator.Result(failures);
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

        if (string.IsNullOrWhiteSpace(options.ConnectionString) && string.IsNullOrWhiteSpace(options.ConnectionStringName))
        {
            (failures ??= []).Add(
                $"Either {nameof(DataSourceOptions.ConnectionString)} or {nameof(DataSourceOptions.ConnectionStringName)} " +
                $"must be specified for {described}.");
        }

        return QuartzSchedulerOptionsValidator.Result(failures);
    }
}
