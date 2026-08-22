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
        activity.Start();

        return new StartedActivity(activity);
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
            // The same value the errors counter is tagged with, so a failure can be found by the same
            // attribute in a trace and in a metric. The exception event below keeps the whole chain,
            // wrappers included, because that is where the stack traces are.
            _activity.SetTag(ErrorType.TagName, ErrorType.Of(jobExEx));
            _activity.AddException(jobExEx);
        }
        _activity.Stop();
    }
}