namespace Quartz.Diagnostics;

/// <summary>
/// The attribute names Quartz puts on its spans and its measurements.
/// </summary>
/// <remarks>
/// <para>
/// Every one of them is spelled under <c>quartz.</c>, which is what OpenTelemetry asks of an attribute a
/// library defines for itself: <c>job.name</c> is a name any other instrumented library in the same
/// process could reasonably claim, and once two of them do, a dashboard has one attribute meaning two
/// things and no way to tell which. The one exception is <c>error.type</c>, which Quartz writes on a
/// failed operation: it is the semantic convention's shared attribute for that, and a Quartz-specific
/// spelling would only keep these series from lining up with the rest of an application's failures.
/// </para>
/// <para>
/// The values are a telemetry contract rather than a compile-time one, so code reading them by these
/// constants needs no change while a dashboard, alert or recording rule matching the 3.x spellings does.
/// </para>
/// </remarks>
public static class ActivityTags
{
    public const string SchedulerName = "quartz.scheduler.name";
    public const string SchedulerId = "quartz.scheduler.id";
    public const string FireInstanceId = "quartz.fire.instance.id";
    public const string TriggerGroup = "quartz.trigger.group";
    public const string TriggerName = "quartz.trigger.name";
    public const string JobType = "quartz.job.type";
    public const string JobGroup = "quartz.job.group";
    public const string JobName = "quartz.job.name";

    /// <summary>
    /// The execution group a trigger belongs to, which is the bucket a thread limit is applied per.
    /// </summary>
    /// <remarks>
    /// A trigger that names no execution group carries no such attribute at all rather than an empty
    /// one: the two are the same series to a reader that treats a missing attribute as empty, and
    /// different series to one that does not, and "there is no group" is not a group name.
    /// </remarks>
    public const string ExecutionGroup = "quartz.execution.group";

    // Job store operation tags
    public const string TriggerCount = "quartz.jobstore.trigger.count";
    public const string BatchSize = "quartz.jobstore.batch.size";

    /// <summary>
    /// Which store operation a measurement is about — one of the <see cref="OperationName.JobStore"/>
    /// names, which is also what the operation's span is called.
    /// </summary>
    public const string JobStoreOperation = "quartz.jobstore.operation";

    /// <summary>
    /// The instance id of the cluster node whose work is being recovered, which is a node other than the
    /// one reporting the measurement.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="SchedulerId"/>: every measurement carries that already, and it names
    /// the node that made the measurement. A recovery is one node saying something about another, so the
    /// two ids are two attributes.
    /// </remarks>
    public const string RecoveredInstanceId = "quartz.cluster.recovered.instance.id";
}

/// <summary>
/// OpenTelemetry's <c>error.type</c> attribute: what a failed job execution failed with, named the
/// same way on the span and on the duration histogram.
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