namespace Quartz.Diagnostics;

public static class ActivityTags
{
    public const string SchedulerName = "scheduler.name";
    public const string SchedulerId = "scheduler.id";
    public const string FireInstanceId = "fire.instance.id";
    public const string TriggerGroup = "trigger.group";
    public const string TriggerName = "trigger.name";
    public const string JobType = "job.type";
    public const string JobGroup = "job.group";
    public const string JobName = "job.name";

    // Job store operation tags
    public const string TriggerCount = "jobstore.trigger.count";
    public const string BatchSize = "jobstore.batch.size";
}

/// <summary>
/// OpenTelemetry's <c>error.type</c> attribute: what a failed job execution failed with, named the
/// same way on the span and on the errors counter.
/// </summary>
internal static class ErrorType
{
    /// <summary>
    /// The attribute name. OpenTelemetry's semantic conventions have one attribute for what an
    /// operation failed with, and every instrumentation that reports a failure spells it this way, so a
    /// Quartz-specific name would only keep these series from lining up with the rest of a dashboard.
    /// </summary>
    internal const string TagName = "error.type";

    /// <summary>
    /// The value: the fully-qualified name of the type of exception that ended the execution. A type
    /// name is bounded by the exception types an application can throw, which is what keeps the
    /// attribute's cardinality low enough to aggregate on; nothing derived from a message belongs here.
    /// </summary>
    internal static string Of(Exception exception)
    {
        Type type = Unwrap(exception).GetType();
        return type.FullName ?? type.Name;
    }

    /// <summary>
    /// The exception an application would recognise, out of the ones the run shell has wrapped it in.
    /// </summary>
    private static Exception Unwrap(Exception exception)
    {
        // JobRunShell reports anything a job throws as JobExecutionException -> JobExecutionProcessException
        // -> what the job threw. Naming the exception it hands the instrumentation would therefore answer
        // "JobExecutionException" for very nearly every failure there is: both of those layers are Quartz's
        // own bookkeeping, and neither says anything an application can act on.
        //
        // Only that exact pair is peeled off. A job that raises a JobExecutionException itself has no
        // JobExecutionProcessException underneath it, so it is reported as the type it chose to throw; and
        // the cause is never unwrapped further, because an AggregateException is what failed, not its
        // children.
        if (exception is JobExecutionException { InnerException: JobExecutionProcessException process })
        {
            return process.InnerException ?? process;
        }

        return exception;
    }
}