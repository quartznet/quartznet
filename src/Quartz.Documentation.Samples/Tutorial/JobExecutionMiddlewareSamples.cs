using System.Diagnostics.Metrics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Quartz.Documentation.Samples.Tutorial;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/tutorial/job-execution-middleware.md.
/// </summary>
public static class JobExecutionMiddlewareSamples
{
    public static void Registering(IHostApplicationBuilder builder)
    {
        #region sample_job_middleware_register

        builder.AddQuartz(q =>
        {
            // built by the container, so it can take dependencies of its own
            q.AddJobMiddleware<LogScopeMiddleware>();

            // built by you, from this scheduler's services
            q.AddJobMiddleware(provider => new MeteredMiddleware(provider.GetRequiredService<IMeterFactory>()));

            // one you already have
            q.AddJobMiddleware(new TenantScopeMiddleware());
        });

        #endregion
    }

    public static async Task Standalone()
    {
        #region sample_job_middleware_standalone

        IScheduler scheduler = await QuartzSchedulerBuilder.Create()
            .UseInMemoryStore()
            .AddJobMiddleware<LogScopeMiddleware>()
            .BuildScheduler();

        #endregion
    }

    public static void PerFiringState(IHostApplicationBuilder builder)
    {
        #region sample_job_middleware_job_scope

        builder.AddQuartz(q =>
        {
            // Populated once per firing, before anything in the job's scope is resolved.
            q.ConfigureJobScope((scope, bundle, scheduler) =>
                scope.ServiceProvider.GetRequiredService<TenantHolder>().Tenant = bundle.Trigger.Key.Group);
        });

        #endregion
    }

    public static void Timeout(IHostApplicationBuilder builder)
    {
        #region sample_job_timeout_register

        builder.AddQuartz(q =>
        {
            // every job gets five minutes, unless it says otherwise
            q.AddJobTimeout(TimeSpan.FromMinutes(5));

            // or: no scheduler-wide budget, and only the jobs carrying [JobTimeout] are bounded
            q.AddJobTimeout();
        });

        #endregion
    }
}

#region sample_job_timeout_attribute

// Thirty seconds for this job, whatever the scheduler's default is.
[JobTimeout("00:00:30")]
public sealed class ReportJob : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        // Forward the token: a job that never looks at it cannot be stopped by anything, and is simply
        // reported as having timed out once it finally returns.
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
    }
}

// No timeout at all, whatever the scheduler's default is.
[JobTimeout("00:00:00")]
public sealed class NightlyRebuildJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}

#endregion

#region sample_job_middleware_scoped

public sealed class AuditMiddleware(IServiceScopeFactory scopeFactory) : IJobExecutionMiddleware
{
    public async ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
    {
        // A middleware is built once, from the container's root, so a scoped service cannot be a
        // constructor parameter. Resolve it per firing instead.
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        AuditLog audit = scope.ServiceProvider.GetRequiredService<AuditLog>();

        await audit.Starting(context.JobDetail.Key, cancellationToken);
        await next(context, cancellationToken);
    }
}

#endregion

#region sample_job_middleware_log_scope

public sealed class LogScopeMiddleware(ILogger<LogScopeMiddleware> logger) : IJobExecutionMiddleware
{
    public async ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
    {
        using IDisposable? scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["JobKey"] = context.JobDetail.Key,
            ["FireInstanceId"] = context.FireInstanceId,
        });

        await next(context, cancellationToken);
    }
}

#endregion

#region sample_job_middleware_translate

public sealed class TransientFailureMiddleware : IJobExecutionMiddleware
{
    public async ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
    {
        try
        {
            await next(context, cancellationToken);
        }
        catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            // Quartz understands this; it does not understand HttpRequestException.
            throw new JobExecutionException(e) { RefireImmediately = true };
        }
    }
}

#endregion

#region sample_job_middleware_short_circuit

public sealed class FeatureFlagMiddleware(FeatureFlags flags) : IJobExecutionMiddleware
{
    public ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
    {
        // Not calling next means the job does not run. The firing still completes, the listeners are
        // still notified, and the trigger is left where a successful execution leaves it.
        return flags.IsEnabled(context.JobDetail.Key) ? next(context, cancellationToken) : default;
    }
}

#endregion

#region sample_job_middleware_ambient

public sealed class TenantScopeMiddleware : IJobExecutionMiddleware
{
    public async ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
    {
        // An AsyncLocal, not a field: one instance of this middleware serves every firing the scheduler
        // performs, and several of them can be in flight at once.
        TenantScope.Current.Value = context.Trigger.Key.Group;
        try
        {
            await next(context, cancellationToken);
        }
        finally
        {
            TenantScope.Current.Value = null;
        }
    }
}

#endregion

/// <summary>
/// The supporting types the samples above name, which the page describes rather than shows.
/// </summary>
public static class TenantScope
{
    public static readonly AsyncLocal<string?> Current = new();
}

public sealed class TenantHolder
{
    public string? Tenant { get; set; }
}

public sealed class FeatureFlags
{
    public bool IsEnabled(JobKey jobKey) => true;
}

public sealed class AuditLog
{
    public ValueTask Starting(JobKey jobKey, CancellationToken cancellationToken = default) => default;
}

public sealed class MeteredMiddleware(IMeterFactory meterFactory) : IJobExecutionMiddleware
{
    public ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
    {
        return next(context, cancellationToken);
    }
}
