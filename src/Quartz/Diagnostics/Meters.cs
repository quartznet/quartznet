using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Quartz.Diagnostics;

internal static class Meters
{
    private static bool _isConfigured;

    private static Meter _meter = null!;
    private static Counter<long> _jobExecuteTotal = null!;
    internal static Counter<long> _jobExecuteErrorTotal = null!;
    internal static Counter<long> _jobExecuteInProgress = null!;

    internal static Histogram<double> _jobExecuteDuration = null!;

    public static void Configure()
    {
        if (_isConfigured)
        {
            return;
        }

        _meter = new Meter(InstrumentationOptions.MeterName, InstrumentationOptions.Version);

        _jobExecuteTotal = _meter.CreateCounter<long>("scheduling.quartz.execute", "ea", "Number jobs executed");
        _jobExecuteErrorTotal = _meter.CreateCounter<long>("scheduling.quartz.execute.errors", "ea", "Number of job execution errors");
        _jobExecuteInProgress = _meter.CreateCounter<long>("scheduling.quartz.execute.active", "ea", "Number of job currently running");
        _jobExecuteDuration = _meter.CreateHistogram<double>("scheduling.quartz.execute.duration", "ms", "Elapsed time spent executing a job, in milliseconds");

        _isConfigured = true;
    }

    public static Instrumentation StartJobExecute(IJobExecutionContext context)
    {
        if (!_isConfigured || !_jobExecuteTotal.Enabled)
        {
            return new Instrumentation(null);
        }

        TagList tagList = new()
        {
            { ActivityOptions.TriggerGroup, context.Trigger.Key.Group },
            { ActivityOptions.TriggerName, context.Trigger.Key.Name },
            { ActivityOptions.JobGroup, context.JobDetail.Key.Group },
            { ActivityOptions.JobName, context.JobDetail.Key.Name },
        };

        _jobExecuteTotal.Add(1, tagList);
        _jobExecuteInProgress.Add(1, tagList);

        return new Instrumentation(tagList);
    }
}

internal readonly struct Instrumentation
{
    private readonly TagList? _tagList;

    public Instrumentation(TagList? tagList)
    {
        this._tagList = tagList;
    }

    public void EndJobExecute(TimeSpan duration, Exception? exception)
    {
        if (_tagList == null)
        {
            return;
        }

        if (exception != null)
        {
            _tagList.Value.Add("scheduling.quartz.exception_type", exception.GetType().Name);
            Meters._jobExecuteErrorTotal.Add(1, _tagList.Value);
        }

        Meters._jobExecuteInProgress.Add(-1, _tagList.Value);
        Meters._jobExecuteDuration.Record(duration.TotalMilliseconds, _tagList.Value);
    }
}