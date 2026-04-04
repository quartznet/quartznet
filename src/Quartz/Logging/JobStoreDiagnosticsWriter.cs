#if DIAGNOSTICS_SOURCE

using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Quartz.Logging;

internal sealed class JobStoreDiagnosticsWriter
{
    private readonly DiagnosticListener diagnosticListener = LogContext.Cached.Default.Value;
    private string schedulerName = "";
    private string schedulerId = "";

    internal void SetSchedulerContext(string name, string id)
    {
        schedulerName = name;
        schedulerId = id;
    }

    internal Task<T> Trace<T>(
        string operationName,
        Func<Task<T>> operation,
        Action<Activity>? enrichActivity = null)
    {
        if (!diagnosticListener.IsEnabled(operationName))
        {
            return operation();
        }

        return TraceCore(operationName, operation, enrichActivity);
    }

    internal Task Trace(
        string operationName,
        Func<Task> operation,
        Action<Activity>? enrichActivity = null)
    {
        if (!diagnosticListener.IsEnabled(operationName))
        {
            return operation();
        }

        return TraceCoreVoid(operationName, operation, enrichActivity);
    }

    private async Task<T> TraceCore<T>(
        string operationName,
        Func<Task<T>> operation,
        Action<Activity>? enrichActivity)
    {
        var activity = new Activity(operationName);
        activity.AddTag(DiagnosticHeaders.SchedulerName, schedulerName);
        activity.AddTag(DiagnosticHeaders.SchedulerId, schedulerId);
        enrichActivity?.Invoke(activity);

        diagnosticListener.StartActivity(activity, operationName);
        try
        {
            T result = await operation().ConfigureAwait(false);
            diagnosticListener.StopActivity(activity, operationName);
            return result;
        }
        catch (Exception ex)
        {
            diagnosticListener.Write(operationName + ".Exception", ex);
            diagnosticListener.StopActivity(activity, operationName);
            throw;
        }
    }

    private async Task TraceCoreVoid(
        string operationName,
        Func<Task> operation,
        Action<Activity>? enrichActivity)
    {
        var activity = new Activity(operationName);
        activity.AddTag(DiagnosticHeaders.SchedulerName, schedulerName);
        activity.AddTag(DiagnosticHeaders.SchedulerId, schedulerId);
        enrichActivity?.Invoke(activity);

        diagnosticListener.StartActivity(activity, operationName);
        try
        {
            await operation().ConfigureAwait(false);
            diagnosticListener.StopActivity(activity, operationName);
        }
        catch (Exception ex)
        {
            diagnosticListener.Write(operationName + ".Exception", ex);
            diagnosticListener.StopActivity(activity, operationName);
            throw;
        }
    }
}

#endif
