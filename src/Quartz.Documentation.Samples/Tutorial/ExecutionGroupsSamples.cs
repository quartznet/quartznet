using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Documentation.Samples.Tutorial;

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
}
