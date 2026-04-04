#if DIAGNOSTICS_SOURCE

using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Quartz.Logging;

internal sealed class JobStoreDiagnosticsWriter
{
    private readonly DiagnosticListener diagnosticListener;
    private string schedulerName = "";
    private string schedulerId = "";

    internal JobStoreDiagnosticsWriter() : this(LogContext.Cached.Default.Value)
    {
    }

    internal JobStoreDiagnosticsWriter(DiagnosticListener diagnosticListener)
    {
        this.diagnosticListener = diagnosticListener;
    }

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
            return await operation().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            diagnosticListener.Write(operationName + ".Exception", ex);
            throw;
        }
        finally
        {
            diagnosticListener.StopActivity(activity, operationName);
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
        }
        catch (Exception ex)
        {
            diagnosticListener.Write(operationName + ".Exception", ex);
            throw;
        }
        finally
        {
            diagnosticListener.StopActivity(activity, operationName);
        }
    }
}

#endif
