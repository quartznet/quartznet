using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Quartz.Diagnostics;

internal static class Meters
{
    private static bool _isConfigured;

    private static Meter _meter = null!;
    private static Counter<long> _jobExecuteTotal = null!;
    internal static Counter<long> _jobExecuteErrorTotal = null!;
    internal static UpDownCounter<long> _jobExecuteInProgress = null!;

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
        // An up-down counter, not a counter: the number of jobs running goes down as often as it goes up,
        // and a counter is monotonic by definition, so an aggregating exporter is entitled to drop or
        // mis-render the decrement and show a running count that only ever climbs.
        _jobExecuteInProgress = _meter.CreateUpDownCounter<long>("scheduling.quartz.execute.active", "ea", "Number of jobs currently running");
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
    private const string ExceptionTypeTag = "scheduling.quartz.exception_type";

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

        // Nullable<T>.Value hands back a copy of the struct, so a tag added to _tagList.Value goes onto a
        // temporary that is discarded. One local, mutated and then measured with, is what makes it stick.
        TagList tags = _tagList.Value;

        // The running count only nets back to zero if the decrement carries exactly the tags the
        // increment carried — an up-down counter is aggregated per attribute set — so it is measured
        // before the failure tag is added.
        Meters._jobExecuteInProgress.Add(-1, tags);

        if (exception != null)
        {
            tags.Add(ExceptionTypeTag, exception.GetType().Name);
            Meters._jobExecuteErrorTotal.Add(1, tags);
        }

        Meters._jobExecuteDuration.Record(duration.TotalMilliseconds, tags);
    }
}