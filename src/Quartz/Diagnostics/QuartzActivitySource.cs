using System.Diagnostics;

using Quartz.Impl;

namespace Quartz.Diagnostics;

internal static class QuartzActivitySource
{
    internal static readonly ActivitySource Instance = new(ActivityOptions.DefaultListenerName, ActivityOptions.Version);

    public static StartedActivity StartJobExecute(JobExecutionContextImpl jec, DateTimeOffset startTime)
    {
        Activity? activity = Instance.CreateActivity(OperationName.Job.Execute, ActivityKind.Internal);
        if (activity == null)
        {
            return new StartedActivity(activity: null);
        }

        activity.SetStartTime(startTime.UtcDateTime);
        activity.EnrichFrom(jec);
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
            activity.AddTag(ActivityOptions.SchedulerName, context.Scheduler.SchedulerName);
            activity.AddTag(ActivityOptions.SchedulerId, context.Scheduler.SchedulerInstanceId);
            activity.AddTag(ActivityOptions.JobType, context.JobDetail.JobType.ToString());
            activity.AddTag(ActivityOptions.FireInstanceId, context.FireInstanceId);
        }

        activity.AddTag(ActivityOptions.TriggerGroup, context.Trigger.Key.Group);
        activity.AddTag(ActivityOptions.TriggerName, context.Trigger.Key.Name);
        activity.AddTag(ActivityOptions.JobGroup, context.JobDetail.Key.Group);
        activity.AddTag(ActivityOptions.JobName, context.JobDetail.Key.Name);
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
        }
        _activity.Stop();
    }
}