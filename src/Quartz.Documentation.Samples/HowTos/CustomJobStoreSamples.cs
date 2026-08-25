using System.Diagnostics;
using System.Diagnostics.Metrics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Documentation.Samples.HowTos;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/how-tos/custom-job-store.md.
/// </summary>
public static class CustomJobStoreSamples
{
    public static void RegisteringADecorator(IServiceCollection services)
    {
        services.AddQuartz(q =>
        {
            #region sample_custom_job_store_registering_a_decorator

            q.UseJobStore(sp => new MetricsJobStore(
                ActivatorUtilities.CreateInstance<RAMJobStore>(sp),
                sp.GetRequiredService<IMeterFactory>()));

            #endregion
        });
    }

    public static void ResolvingTriggerState(StoredTriggerState stored, bool isExecuting)
    {
        #region sample_custom_job_store_trigger_state_resolver

        TriggerState reported = TriggerStateResolver.Resolve(stored, isExecuting);

        #endregion
    }
}

#region sample_custom_job_store_decorator

public sealed class MetricsJobStore(IJobStore inner, IMeterFactory meters) : DelegatingJobStore(inner)
{
    private readonly Histogram<double> acquireDuration = meters
        .Create("App.Quartz")
        .CreateHistogram<double>("app.quartz.acquire.duration", "s");

    public override async ValueTask<List<IOperableTrigger>> AcquireNextTriggers(
        TriggerAcquisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            return await base.AcquireNextTriggers(request, cancellationToken);
        }
        finally
        {
            acquireDuration.Record(Stopwatch.GetElapsedTime(start).TotalSeconds);
        }
    }
}

#endregion

/// <summary>
/// Scaffolding for the one member the page shows: <c>AdoJobStoreBase</c> has an eleven-parameter
/// constructor its subclasses forward, and none of that belongs on the page.
/// </summary>
internal sealed class BudgetedJobStore(
    ISchedulerSignaler schedulerSignaler,
    ITypeLoader typeLoader,
    TimeProvider timeProvider,
    IOptions<QuartzSchedulerOptions> schedulerOptions,
    IOptions<AdoJobStoreOptions> storeOptions,
    IOptions<ClusteringOptions> clusteringOptions,
    IObjectSerializer objectSerializer,
    IDbProvider dbProvider,
    IDriverDelegate driverDelegate)
    : LocalTransactionJobStore(
        schedulerSignaler,
        typeLoader,
        timeProvider,
        schedulerOptions,
        storeOptions,
        clusteringOptions,
        objectSerializer,
        dbProvider,
        driverDelegate)
{
    private readonly int nodeBudget = 5;

    #region sample_custom_job_store_acquisition_criteria

    protected override TriggerAcquisitionCriteria CreateAcquisitionCriteria(TriggerAcquisitionRequest request)
    {
        TriggerAcquisitionCriteria criteria = base.CreateAcquisitionCriteria(request);
        return criteria with { MaxCount = Math.Min(criteria.MaxCount, this.nodeBudget) };
    }

    #endregion
}

/// <summary>
/// The same scaffolding as <see cref="BudgetedJobStore" />, for the second override the page shows.
/// </summary>
internal sealed class MaintenanceWindowJobStore(
    ISchedulerSignaler schedulerSignaler,
    ITypeLoader typeLoader,
    TimeProvider timeProvider,
    IOptions<QuartzSchedulerOptions> schedulerOptions,
    IOptions<AdoJobStoreOptions> storeOptions,
    IOptions<ClusteringOptions> clusteringOptions,
    IObjectSerializer objectSerializer,
    IDbProvider dbProvider,
    IDriverDelegate driverDelegate)
    : LocalTransactionJobStore(
        schedulerSignaler,
        typeLoader,
        timeProvider,
        schedulerOptions,
        storeOptions,
        clusteringOptions,
        objectSerializer,
        dbProvider,
        driverDelegate)
{
    private readonly IMaintenanceWindow maintenanceWindow = null!;

    #region sample_custom_job_store_excluded_job_types

    // JobType.FullName is the spelling the store persists - "Namespace.TypeName, AssemblyName".
    // Type.FullName carries no assembly name and would never match a stored row.
    private static readonly string reportingJobTypeName = new JobType(typeof(ReportingJob)).FullName;

    protected override TriggerAcquisitionCriteria CreateAcquisitionCriteria(TriggerAcquisitionRequest request)
    {
        // Asked again on every acquisition attempt, so a window that opens between two of them takes
        // effect on the next one without restarting anything.
        string[]? excluded = this.maintenanceWindow.IsOpen ? [reportingJobTypeName] : null;

        return base.CreateAcquisitionCriteria(request) with { ExcludedJobTypeNames = excluded };
    }

    #endregion
}

/// <summary>
/// The deployment's own notion of when the heavy jobs are not welcome. Named only so the sample above
/// compiles.
/// </summary>
public interface IMaintenanceWindow
{
    bool IsOpen { get; }
}
