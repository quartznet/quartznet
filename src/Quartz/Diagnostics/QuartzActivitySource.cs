using System.Diagnostics;

using Quartz.Impl;

namespace Quartz.Diagnostics;

internal static class QuartzActivitySource
{
    internal static readonly ActivitySource Instance = new(QuartzInstrumentation.ActivitySourceName, QuartzInstrumentation.Version);

    /// <summary>
    /// Opens the span a firing is traced on.
    /// </summary>
    public static StartedActivity StartJobExecute(JobExecutionContextImpl context, DateTimeOffset startTime)
    {
        return StartFiring(OperationName.Job.Execute, context, startTime);
    }

    /// <summary>
    /// Opens the span a refused firing is traced on.
    /// </summary>
    /// <remarks>
    /// The same shape as an execution, for the same reasons: a veto is a firing that a trigger listener
    /// turned down, and it is worth walking back to the call that scheduled it exactly as an execution is.
    /// </remarks>
    public static StartedActivity StartJobVeto(JobExecutionContextImpl context)
    {
        return StartFiring(OperationName.Job.Veto, context, startTime: null);
    }

    /// <summary>
    /// Starts a firing's span as a trace root, linked back to whatever scheduled it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A root, and deliberately so. The worker that runs a firing inherits the execution context of the
    /// scheduler's loop, which in turn inherited the context of whoever called <c>Start()</c> — so a
    /// firing left to take the ambient activity as its parent lands in the trace of the request that
    /// started the scheduler, weeks ago, and stays there for the life of the process. That is #3797; the
    /// report had 1,499 executions arrive as two traces. Nothing above a firing belongs in its trace,
    /// and the connection back to the call that scheduled it is a <see cref="ActivityLink" /> instead.
    /// </para>
    /// <para>
    /// Rooting it takes both halves below, because they answer different questions.
    /// <see cref="ActivitySource.CreateActivity(string, ActivityKind, ActivityContext, IEnumerable{KeyValuePair{string, object?}}?, IEnumerable{ActivityLink}?, ActivityIdFormat)" />
    /// is given an empty parent context so the sampler is asked about a root — the usual sampler is
    /// parent-based, and asking it about the ambient span would let a context we are about to discard
    /// decide whether this firing is recorded. <see cref="Activity.Start" /> then reads
    /// <see cref="Activity.Current" /> again and adopts it if there is one, whatever it was created with,
    /// so the ambient activity is cleared around the call and put back afterwards.
    /// </para>
    /// </remarks>
    private static StartedActivity StartFiring(string operationName, JobExecutionContextImpl context, DateTimeOffset? startTime)
    {
        // Asked first so that nothing below — not the data-map lookup the link needs, not the array it
        // travels in — is paid for by a scheduler nobody is tracing.
        if (!Instance.HasListeners())
        {
            return default;
        }

        Activity? activity = Instance.CreateActivity(
            operationName,
            ActivityKind.Internal,
            parentContext: default,
            tags: null,
            links: LinkToScheduler(context));

        if (activity is null)
        {
            return default;
        }

        if (startTime is { } start)
        {
            activity.SetStartTime(start.UtcDateTime);
        }

        activity.EnrichFrom(context);

        Activity? ambient = Activity.Current;
        Activity.Current = null;
        activity.Start();

        return new StartedActivity(activity, ambient);
    }

    /// <summary>
    /// Links the firing back to the activity that scheduled it, when the trigger recorded one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="ActivityLink" /> and not a parent. The scheduling call and the firing are related but
    /// separated by however long the schedule said — minutes, days, a cron expression's next Sunday — and
    /// a span whose parent is that far away makes a trace no backend can render and no operator can
    /// read. A link is the shape OpenTelemetry gives exactly this: an asynchronous producer and the
    /// consumer that eventually picks the work up. The firing is its own trace root, and the link is how
    /// you walk back to the request that asked for it.
    /// </para>
    /// <para>
    /// Handed to <c>CreateActivity</c> rather than added afterwards, because a sampler is consulted while
    /// the activity is being created and a link added after that is one it never saw. Sampling a
    /// consumer span by the trace that produced it is a standard thing to want, and it needs the link to
    /// be there at the decision.
    /// </para>
    /// </remarks>
    private static ActivityLink[]? LinkToScheduler(JobExecutionContextImpl context)
    {
        if (!context.MergedJobDataMap.TryGetValue(SchedulerConstants.TraceParent, out object? stored)
            || stored is not string traceParent)
        {
            return null;
        }

        context.MergedJobDataMap.TryGetValue(SchedulerConstants.TraceState, out object? state);

        if (!ActivityContext.TryParse(traceParent, state as string, isRemote: true, out ActivityContext scheduledBy))
        {
            return null;
        }

        return [new ActivityLink(scheduledBy)];
    }

    internal static void EnrichFrom(this Activity activity, IJobExecutionContext context)
    {
        if (activity == null)
        {
            return;
        }

        if (activity.IsAllDataRequested)
        {
            activity.AddTag(ActivityTags.SchedulerName, context.Scheduler.SchedulerName);
            activity.AddTag(ActivityTags.SchedulerId, context.Scheduler.SchedulerInstanceId);
            activity.AddTag(ActivityTags.JobType, context.JobDetail.JobType.ToString());
            activity.AddTag(ActivityTags.FireInstanceId, context.FireInstanceId);
        }

        activity.AddTag(ActivityTags.TriggerGroup, context.Trigger.Key.Group);
        activity.AddTag(ActivityTags.TriggerName, context.Trigger.Key.Name);
        activity.AddTag(ActivityTags.JobGroup, context.JobDetail.Key.Group);
        activity.AddTag(ActivityTags.JobName, context.JobDetail.Key.Name);

        // Only when the trigger names one. An execution group is what a thread limit is applied per, so
        // it is the dimension "which bucket saturated" is asked in — but most triggers are in no group,
        // and an empty attribute on all of them would be a dimension whose commonest value means "not
        // applicable".
        if (context.Trigger.ExecutionGroup is { } executionGroup)
        {
            activity.AddTag(ActivityTags.ExecutionGroup, executionGroup);
        }
    }
}

/// <summary>
/// A firing's span, and the activity that was ambient when it was opened — which the span, being a root,
/// cannot put back by itself.
/// </summary>
/// <remarks>
/// <see langword="default" /> when nothing is listening, so a firing nobody is tracing pays one null check
/// to start and one to stop.
/// </remarks>
internal readonly struct StartedActivity
{
    private readonly Activity? activity;
    private readonly Activity? ambient;

    public StartedActivity(Activity? activity, Activity? ambient)
    {
        this.activity = activity;
        this.ambient = ambient;
    }

    /// <summary>
    /// Closes the span, timing it by the clock the rest of the firing is timed by.
    /// </summary>
    public void Stop(DateTimeOffset endTime, JobExecutionException? jobExEx) => StopCore(endTime, jobExEx);

    /// <summary>
    /// Closes the span, timing it by <see cref="Activity" />'s own clock.
    /// </summary>
    public void Stop() => StopCore(endTime: null, jobExEx: null);

    private void StopCore(DateTimeOffset? endTime, JobExecutionException? jobExEx)
    {
        if (activity is null)
        {
            return;
        }

        if (endTime is { } end)
        {
            activity.SetEndTime(end.UtcDateTime);
        }

        if (jobExEx != null)
        {
            activity.SetStatus(ActivityStatusCode.Error, jobExEx.Message);
            // The same value the duration measurement is tagged with, so a failure can be found by the same
            // attribute in a trace and in a metric. The exception event below keeps the whole chain,
            // wrappers included, because that is where the stack traces are.
            activity.SetTag(ErrorType.TagName, ErrorType.Of(jobExEx));
            activity.AddException(jobExEx);
        }

        activity.Stop();

        // Stopping a root puts nothing back, since a root has no parent — so the activity the caller was
        // running under is restored by hand. The span's shape is this type's business; what the run shell
        // does after the firing is not. A stopped activity is one Activity.Current refuses to be set to,
        // and being refused costs a thrown and swallowed exception, so it is asked rather than attempted.
        if (ambient is { IsStopped: false })
        {
            Activity.Current = ambient;
        }
    }
}
