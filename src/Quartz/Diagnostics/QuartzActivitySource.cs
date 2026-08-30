using System.Diagnostics;

using Quartz.Impl;

namespace Quartz.Diagnostics;

internal static class QuartzActivitySource
{
    internal static readonly ActivitySource Instance = new(QuartzInstrumentation.ActivitySourceName, QuartzInstrumentation.Version);

    public static StartedActivity StartJobExecute(JobExecutionContextImpl context, DateTimeOffset startTime)
    {
        Activity? activity = Instance.CreateActivity(OperationName.Job.Execute, ActivityKind.Internal);
        if (activity == null)
        {
            return new StartedActivity(activity: null);
        }

        activity.SetStartTime(startTime.UtcDateTime);
        activity.EnrichFrom(context);
        activity.LinkToScheduler(context);
        activity.Start();

        return new StartedActivity(activity);
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
    /// Added before <see cref="Activity.Start" />, so the link is on the activity by the time a listener
    /// is told about it — a sampler that reads links reads them at start.
    /// </para>
    /// </remarks>
    private static void LinkToScheduler(this Activity activity, JobExecutionContextImpl context)
    {
        if (!context.MergedJobDataMap.TryGetValue(SchedulerConstants.TraceParent, out object? stored)
            || stored is not string traceParent)
        {
            return;
        }

        context.MergedJobDataMap.TryGetValue(SchedulerConstants.TraceState, out object? state);

        if (ActivityContext.TryParse(traceParent, state as string, isRemote: true, out ActivityContext scheduledBy))
        {
            activity.AddLink(new ActivityLink(scheduledBy));
        }
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

internal readonly struct StartedActivity
{
    private readonly Activity? _activity;

    public StartedActivity(Activity? activity)
    {
        this._activity = activity;
    }

    public void Stop(DateTimeOffset endTime, JobExecutionException? jobExEx)
    {
        if (_activity == null)
        {
            return;
        }

        _activity.SetEndTime(endTime.UtcDateTime);

        if (jobExEx != null)
        {
            _activity.SetStatus(ActivityStatusCode.Error, jobExEx.Message);
            // The same value the duration measurement is tagged with, so a failure can be found by the same
            // attribute in a trace and in a metric. The exception event below keeps the whole chain,
            // wrappers included, because that is where the stack traces are.
            _activity.SetTag(ErrorType.TagName, ErrorType.Of(jobExEx));
            _activity.AddException(jobExEx);
        }
        _activity.Stop();
    }
}