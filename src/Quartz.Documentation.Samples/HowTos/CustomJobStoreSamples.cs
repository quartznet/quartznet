using System.Diagnostics;
using System.Diagnostics.Metrics;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Impl;

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

#region sample_custom_job_store_fire_on_acquire

public sealed class RoundCountingJobStore(IJobStore inner) : DelegatingJobStore(inner)
{
    private long rounds;

    public long Rounds => Interlocked.Read(ref rounds);

    // The member the scheduler calls. Forwarded, the inner store fires what is already due in the
    // transaction that acquires it. Left to DelegatingJobStore, it fires nothing, and the scheduler
    // fires every trigger in a second transaction.
    public override ValueTask<TriggerAcquisitionResult> AcquireNextTriggersAndFireDue(
        TriggerAcquisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref rounds);
        return InnerJobStore.AcquireNextTriggersAndFireDue(request, cancellationToken);
    }
}

#endregion

#region sample_custom_job_store_acquisition_budget

public sealed class BudgetedJobStore(IJobStore inner, int nodeBudget) : DelegatingJobStore(inner)
{
    public override ValueTask<List<IOperableTrigger>> AcquireNextTriggers(
        TriggerAcquisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        return base.AcquireNextTriggers(Narrow(request), cancellationToken);
    }

    // The same request, rewritten the same way, so the inner store still fires what is due on acquisition.
    public override ValueTask<TriggerAcquisitionResult> AcquireNextTriggersAndFireDue(
        TriggerAcquisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        return InnerJobStore.AcquireNextTriggersAndFireDue(Narrow(request), cancellationToken);
    }

    private TriggerAcquisitionRequest Narrow(TriggerAcquisitionRequest request)
    {
        return request with { MaxCount = Math.Min(request.MaxCount, nodeBudget) };
    }
}

#endregion

#region sample_custom_job_store_excluded_job_types

public sealed class MaintenanceWindowJobStore(IJobStore inner, IMaintenanceWindow window)
    : DelegatingJobStore(inner)
{
    // JobType.FullName is the spelling the store persists - "Namespace.TypeName, AssemblyName".
    // Type.FullName carries no assembly name and would never match a stored row.
    private static readonly string reportingJobTypeName = new JobType(typeof(ReportingJob)).FullName;

    public override ValueTask<List<IOperableTrigger>> AcquireNextTriggers(
        TriggerAcquisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        // Asked again on every acquisition, so a window that opens between two of them takes effect on
        // the next one without restarting anything.
        if (!window.IsOpen)
        {
            return base.AcquireNextTriggers(request, cancellationToken);
        }

        return base.AcquireNextTriggers(
            request with { ExcludedJobTypeNames = [reportingJobTypeName] },
            cancellationToken);
    }
}

#endregion

/// <summary>
/// The deployment's own notion of when the heavy jobs are not welcome. Named only so the sample above
/// compiles.
/// </summary>
public interface IMaintenanceWindow
{
    bool IsOpen { get; }
}
