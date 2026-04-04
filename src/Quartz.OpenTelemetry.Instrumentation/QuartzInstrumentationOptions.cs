using Quartz.Diagnostics;

namespace Quartz.OpenTelemetry.Instrumentation;

public class QuartzInstrumentationOptions
{
    /// <summary>
    /// Whether to add exception details to logs. Defaults to false as they may contain
    /// Personally Identifiable Information (PII), passwords or usernames.
    /// </summary>
    public bool IncludeExceptionDetails { get; set; }

    /// <summary>
    /// Default traced operations.
    /// </summary>
    public static readonly IEnumerable<string> DefaultTracedOperations = new[]
    {
        OperationName.Job.Execute,
        OperationName.Job.Veto,

        // Job store operations
        OperationName.JobStore.AcquireNextTriggers,
        OperationName.JobStore.TriggersFired,
        OperationName.JobStore.TriggeredJobComplete,
        OperationName.JobStore.ReleaseAcquiredTrigger,
        OperationName.JobStore.StoreJobAndTrigger,
        OperationName.JobStore.StoreJob,
        OperationName.JobStore.StoreJobsAndTriggers,
        OperationName.JobStore.StoreTrigger,
        OperationName.JobStore.StoreCalendar,
        OperationName.JobStore.RemoveJob,
        OperationName.JobStore.RemoveJobs,
        OperationName.JobStore.RemoveTrigger,
        OperationName.JobStore.RemoveTriggers,
        OperationName.JobStore.RemoveCalendar,
        OperationName.JobStore.ReplaceTrigger,
        OperationName.JobStore.UpdateTriggerDetails,
        OperationName.JobStore.PauseTrigger,
        OperationName.JobStore.PauseTriggers,
        OperationName.JobStore.PauseJob,
        OperationName.JobStore.PauseJobs,
        OperationName.JobStore.ResumeTrigger,
        OperationName.JobStore.ResumeTriggers,
        OperationName.JobStore.ResumeJob,
        OperationName.JobStore.ResumeJobs,
        OperationName.JobStore.PauseAll,
        OperationName.JobStore.ResumeAll,
        OperationName.JobStore.ResetTriggerFromErrorState,
        OperationName.JobStore.ClearAllSchedulingData,
    };

    /// <summary>
    /// Gets or sets traced operations set.
    /// </summary>
    public HashSet<string> TracedOperations { get; set; } = new HashSet<string>(DefaultTracedOperations);
}