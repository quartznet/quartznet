using System.Threading.RateLimiting;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Quartz.Documentation.Samples.Tutorial;

/// <summary>
/// An application's own settings, which is where a per-tenant quota usually comes from.
/// </summary>
public sealed class TenantQuotaOptions
{
    public Dictionary<string, int> PerTenant { get; } = [];
}

/// <summary>
/// Samples for docs/documentation/quartz-4.x/tutorial/execution-groups.md.
/// </summary>
public static class ExecutionGroupsSamples
{
    public static void BuildTrigger(IJobDetail job)
    {
        #region sample_execution_groups_trigger

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("myTrigger")
            .ForJob(job)
            .WithExecutionGroup("batch-jobs")
            .WithCronSchedule("0 0 2 * * ?")
            .Build();

        #endregion
    }

    public static async Task MoveBetweenGroups(IScheduler scheduler, ITrigger trigger)
    {
        #region sample_execution_groups_update_trigger

        await scheduler.UpdateTriggerDetails(
            trigger.Key,
            new TriggerDetailsUpdate().WithExecutionGroup("batch-jobs"));

        // pass null to take the trigger out of every group
        await scheduler.UpdateTriggerDetails(
            trigger.Key,
            new TriggerDetailsUpdate().WithExecutionGroup(null));

        #endregion
    }

    public static void ConfigureLimits(IServiceCollection services)
    {
        #region sample_execution_groups_dependency_injection

        services.AddQuartz(q =>
        {
            q.UseExecutionLimits(limits =>
            {
                limits.ForGroup("batch-jobs", maxConcurrent: 2);                        // per node
                limits.ForGroup("high-cpu", maxConcurrent: 3);                          // per node
                limits.ForGroup("tenant-acme", 8, ExecutionLimitScope.Cluster);         // per cluster
                limits.ForDefaultGroup(maxConcurrent: 10);
                limits.ForOtherGroups(maxConcurrent: 5);
            });
        });

        #endregion
    }

    public static void ConfigureLimitsFromOptions(IServiceCollection services)
    {
        #region sample_execution_groups_from_options

        services.AddQuartz(q => q.UseExecutionLimits((serviceProvider, limits) =>
        {
            TenantQuotaOptions quotas = serviceProvider.GetRequiredService<IOptions<TenantQuotaOptions>>().Value;

            foreach ((string tenant, int maxConcurrent) in quotas.PerTenant)
            {
                limits.ForGroup(tenant, maxConcurrent, ExecutionLimitScope.Cluster);
            }
        }));

        #endregion
    }

    public static async Task SetAtRuntime(IScheduler scheduler)
    {
        #region sample_execution_groups_set_at_runtime

        await scheduler.SetExecutionLimits(
            ExecutionLimitsBuilder.Create()
                .ForGroup("batch-jobs", 2)
                .ForDefaultGroup(10)
                .ForOtherGroups(5)
                .Build());

        #endregion
    }

    public static async Task DeriveFromTriggerGroup(IScheduler scheduler)
    {
        #region sample_execution_groups_trigger_group_when_unset

        await scheduler.SetExecutionLimits(
            ExecutionLimitsBuilder.Create()
                .UseTriggerGroupWhenUnset()
                .ForGroup("tenant-a", 4)   // names a trigger group here, because none of its triggers name one
                .ForOtherGroups(2)         // every other tenant gets two
                .Build());

        #endregion
    }

    public static async Task ReadLimitsBack(IScheduler scheduler)
    {
        #region sample_execution_groups_read_limits

        ExecutionLimits? limits = await scheduler.GetExecutionLimits();
        foreach (ExecutionGroupLimit limit in limits?.Groups ?? [])
        {
            string group = limit.Group.IsDefault ? "(no group)"
                : limit.Group.IsOtherGroups ? "(other groups)"
                : limit.Group.IsPrefix ? $"(each group starting with {limit.Group.Prefix})"
                : limit.Group.Name!;
            Console.WriteLine($"{group}: {limit.MaxConcurrent?.ToString() ?? "unlimited"} per {limit.Scope}");
        }

        limits?.TryGetLimit(ExecutionGroupScope.Named("batch-jobs"), out int? batchLimit);

        #endregion
    }

    public static async Task ClearLimits(IScheduler scheduler)
    {
        #region sample_execution_groups_clear_limits

        await scheduler.SetExecutionLimits(null);

        #endregion
    }

    public static void ClusterScopedLimit(IQuartzBuilder q)
    {
        #region sample_execution_groups_cluster_scope

        q.UseExecutionLimits(limits => limits
            .ForGroup("tenant-acme", 8, ExecutionLimitScope.Cluster));

        #endregion
    }

    public static void ProtectInteractiveWork(IQuartzBuilder q)
    {
        #region sample_execution_groups_batch_versus_interactive

        q.UseExecutionLimits(limits =>
        {
            limits.ForGroup("batch", maxConcurrent: 3);    // max 3 batch jobs
            limits.ForOtherGroups(maxConcurrent: 10);      // everything else gets up to 10
        });

        #endregion
    }

    public static void TenantQuotas(ExecutionLimitsBuilder limits)
    {
        #region sample_execution_groups_tenant_quotas

        limits.ForGroup("tenant-a", 5, ExecutionLimitScope.Cluster);
        limits.ForGroup("tenant-b", 5, ExecutionLimitScope.Cluster);
        limits.ForGroup("tenant-c", 5, ExecutionLimitScope.Cluster);

        #endregion
    }

    public static void PerTenantLimits(IQuartzBuilder q)
    {
        #region sample_execution_groups_per_tenant_prefix

        q.UseExecutionLimits(limits => limits
            .ForGroupsWithPrefix("tenant:", 2, ExecutionLimitScope.Cluster) // two for each tenant
            .ForGroup("tenant:vip", 8, ExecutionLimitScope.Cluster));       // a group's own limit wins

        #endregion
    }

    public static ITrigger TenantTrigger(IJobDetail job, string tenantId)
    {
        #region sample_execution_groups_per_tenant_template

        ITrigger trigger = TriggerBuilder.Create()
            .ForJob(job)
            .UsingJobData("TenantId", tenantId)
            .WithExecutionGroup("tenant:{TenantId}")   // stored as "tenant:acme"
            .StartNow()
            .Build();

        #endregion

        return trigger;
    }

    public static ITrigger TenantReportTrigger(string tenantId)
    {
        #region sample_execution_groups_per_tenant_attribute_trigger

        ITrigger trigger = TriggerBuilder.Create<TenantReportJob>()
            .ForJob("tenant-report")
            .UsingJobData("TenantId", tenantId)   // the attribute's {TenantId}
            .StartNow()
            .Build();                             // stored as "tenant:acme"

        #endregion

        return trigger;
    }

    public static async Task TenantOneLiner(IScheduler scheduler, TenantExport export, CancellationToken cancellationToken)
    {
        #region sample_execution_groups_per_tenant_one_liner

        await scheduler.ScheduleJob<TenantExportJob, TenantExport>(
            export,
            TimeSpan.FromMinutes(5),
            new OneOffJobOptions { ExecutionGroup = $"tenant:{export.TenantId}" },
            cancellationToken);

        #endregion
    }

    public static void RateLimit(IServiceCollection services)
    {
        #region sample_execution_groups_rate_limit_register

        // 100 starts an hour for each tenant's group, counted in ten-minute segments
        PartitionedRateLimiter<IJobExecutionContext> limiter = PartitionedRateLimiter.Create<IJobExecutionContext, string>(context =>
            context.Trigger.ExecutionGroup is { } group && group.StartsWith("tenant:", StringComparison.Ordinal)
                ? RateLimitPartition.GetSlidingWindowLimiter(group, _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = 100,
                    Window = TimeSpan.FromHours(1),
                    SegmentsPerWindow = 6,
                    QueueLimit = int.MaxValue, // the execution limit below bounds what can wait
                })
                : RateLimitPartition.GetNoLimiter(string.Empty));

        services.AddQuartz(q =>
        {
            // before AddJobTimeout, so waiting for a permit does not spend the job's budget
            q.AddJobMiddleware(new RateLimitMiddleware(limiter));

            // a waiting firing holds a thread and counts here: two per tenant, waiting or running
            q.UseExecutionLimits(limits => limits.ForGroupsWithPrefix("tenant:", 2));
        });

        #endregion
    }
}

#region sample_execution_groups_rate_limit_middleware

public sealed class RateLimitMiddleware(PartitionedRateLimiter<IJobExecutionContext> limiter) : IJobExecutionMiddleware
{
    public async ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
    {
        // The firing's token, so interrupting the firing ends the wait and the job does not run.
        using RateLimitLease lease = await limiter.AcquireAsync(context, permitCount: 1, cancellationToken);

        if (!lease.IsAcquired)
        {
            // The limiter's queue is full. A failure, so the trigger's retry policy decides what follows.
            throw new JobExecutionException($"No start permit for execution group '{context.Trigger.ExecutionGroup}'.");
        }

        await next(context, cancellationToken);
    }
}

#endregion

#region sample_execution_groups_per_tenant_attribute

[ExecutionGroup("tenant:{TenantId}")]
public sealed class TenantReportJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}

#endregion

/// <summary>
/// The payload of <see cref="TenantExportJob" />.
/// </summary>
public sealed record TenantExport(string TenantId, string Month);

/// <summary>
/// A one-off job whose firings are grouped per tenant at the call site.
/// </summary>
public sealed class TenantExportJob : IJob<TenantExport>
{
    public ValueTask Execute(IJobExecutionContext context, TenantExport input, CancellationToken cancellationToken = default) => default;
}
