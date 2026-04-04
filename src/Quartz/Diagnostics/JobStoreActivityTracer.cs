using System.Diagnostics;

namespace Quartz.Diagnostics;

internal sealed class JobStoreActivityTracer
{
    private readonly ActivitySource activitySource;
    private string schedulerName = "";
    private string schedulerId = "";

    internal JobStoreActivityTracer() : this(QuartzActivitySource.Instance)
    {
    }

    internal JobStoreActivityTracer(ActivitySource activitySource)
    {
        this.activitySource = activitySource;
    }

    internal void SetSchedulerContext(string name, string id)
    {
        schedulerName = name;
        schedulerId = id;
    }

    internal ValueTask<T> Trace<T>(
        string operationName,
        Func<ValueTask<T>> operation,
        Action<Activity>? enrichActivity = null)
    {
        Activity? activity = activitySource.CreateActivity(operationName, ActivityKind.Client);
        if (activity is null)
        {
            return operation();
        }

        return TraceCore(activity, operation, enrichActivity);
    }

    internal ValueTask Trace(
        string operationName,
        Func<ValueTask> operation,
        Action<Activity>? enrichActivity = null)
    {
        Activity? activity = activitySource.CreateActivity(operationName, ActivityKind.Client);
        if (activity is null)
        {
            return operation();
        }

        return TraceCoreVoid(activity, operation, enrichActivity);
    }

    private async ValueTask<T> TraceCore<T>(
        Activity activity,
        Func<ValueTask<T>> operation,
        Action<Activity>? enrichActivity)
    {
        activity.SetTag(ActivityOptions.SchedulerName, schedulerName);
        activity.SetTag(ActivityOptions.SchedulerId, schedulerId);
        if (activity.IsAllDataRequested)
        {
            enrichActivity?.Invoke(activity);
        }
        activity.Start();
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity.AddException(ex);
            throw;
        }
        finally
        {
            activity.Stop();
        }
    }

    private async ValueTask TraceCoreVoid(
        Activity activity,
        Func<ValueTask> operation,
        Action<Activity>? enrichActivity)
    {
        activity.SetTag(ActivityOptions.SchedulerName, schedulerName);
        activity.SetTag(ActivityOptions.SchedulerId, schedulerId);
        if (activity.IsAllDataRequested)
        {
            enrichActivity?.Invoke(activity);
        }
        activity.Start();
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity.AddException(ex);
            throw;
        }
        finally
        {
            activity.Stop();
        }
    }
}
