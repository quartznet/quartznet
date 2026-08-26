using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Quartz.Diagnostics;

/// <summary>
/// The instruments a scheduler reports job execution on.
/// </summary>
/// <remarks>
/// <para>
/// An instance rather than a static, built from the container's <see cref="IMeterFactory"/> where there is
/// one. A factory-created meter belongs to the container that created it, which is what lets two hosts in
/// one process — a test suite, most often — collect each other's measurements apart, and what
/// <c>MetricCollector</c> keys on when a test asserts on them.
/// </para>
/// <para>
/// The factory is optional. It arrives with <c>AddMetrics()</c>, which every application built on the
/// generic host calls for itself, and Quartz does not register one of its own: an <see cref="IMeterFactory"/>
/// registered here would be the one the whole application got, and would take the place of the real one
/// wherever <c>AddMetrics()</c> happened to be called after <c>AddQuartz</c>. Without a factory the meter
/// is created directly, which is what the static version always did.
/// </para>
/// </remarks>
internal sealed class Meters
{
    /// <summary>
    /// The instruments used by a scheduler built without a container — a hand-constructed
    /// <see cref="Core.QuartzSchedulerResources"/>, in tests and benchmarks. One per process, as the
    /// static version was.
    /// </summary>
    private static readonly Lazy<Meters> shared = new(static () => new Meters(meterFactory: null));

    private readonly Meter meter;
    private readonly UpDownCounter<long> jobExecuteInProgress;
    private readonly Histogram<double> jobExecuteDuration;

    public Meters(IMeterFactory? meterFactory)
    {
        MeterOptions options = new(QuartzInstrumentation.MeterName) { Version = QuartzInstrumentation.Version };

        meter = meterFactory?.Create(options) ?? new Meter(options);

        // An up-down counter, not a counter: the number of jobs running goes down as often as it goes up,
        // and a counter is monotonic by definition, so an aggregating exporter is entitled to drop or
        // mis-render the decrement and show a running count that only ever climbs. The unit is UCUM's
        // annotation form for a dimensionless count of a thing, which is how OpenTelemetry spells one.
        jobExecuteInProgress = meter.CreateUpDownCounter<long>("quartz.job.execution.active", "{job}", "Number of jobs currently running");

        // Seconds, per OpenTelemetry's duration convention. A histogram's default bucket boundaries assume
        // a duration is in seconds, so recording milliseconds piled every execution longer than ten
        // seconds into the last bucket, next to every other duration series in the application.
        jobExecuteDuration = meter.CreateHistogram<double>("quartz.job.execution.duration", "s", "Elapsed time spent executing a job");
    }

    public static Meters Shared => shared.Value;

    public Instrumentation StartJobExecute(IJobExecutionContext context)
    {
        // Nothing is subscribed to either instrument, so the whole tag list is work for a measurement no
        // one will read. Both are asked: an application is free to configure a view that keeps one.
        if (!jobExecuteDuration.Enabled && !jobExecuteInProgress.Enabled)
        {
            return default;
        }

        TagList tagList = new()
        {
            // Which scheduler ran the job. A process can hold several, and without this their measurements
            // are one series that no dashboard can separate again.
            { ActivityTags.SchedulerName, context.Scheduler.SchedulerName },
            // And which node of it. A cluster is several schedulers sharing one name, so the name alone
            // answers "which application" and never "which node" — the question every cluster incident
            // starts with.
            { ActivityTags.SchedulerId, context.Scheduler.SchedulerInstanceId },
            { ActivityTags.TriggerGroup, context.Trigger.Key.Group },
            { ActivityTags.TriggerName, context.Trigger.Key.Name },
            { ActivityTags.JobGroup, context.JobDetail.Key.Group },
            { ActivityTags.JobName, context.JobDetail.Key.Name },
        };

        // Absent rather than empty when the trigger names no group: an execution group is the bucket a
        // thread limit is applied per, and most triggers are in none, so an empty value on all of them
        // would be a dimension whose commonest member means "not applicable".
        if (context.Trigger.ExecutionGroup is { } executionGroup)
        {
            tagList.Add(ActivityTags.ExecutionGroup, executionGroup);
        }

        jobExecuteInProgress.Add(1, tagList);

        return new Instrumentation(this, tagList);
    }

    /// <summary>
    /// Records the end of an execution on the instruments that started it.
    /// </summary>
    internal void EndJobExecute(TagList tags, TimeSpan duration, Exception? exception)
    {
        // The running count only nets back to zero if the decrement carries exactly the tags the
        // increment carried — an up-down counter is aggregated per attribute set — so it is measured
        // before the failure tag is added.
        jobExecuteInProgress.Add(-1, tags);

        if (exception != null)
        {
            // The exception the job threw, not the JobExecutionException the run shell wrapped it in —
            // which is also what the execution's span reports, so the two signals name the same failure.
            tags.Add(ErrorType.TagName, ErrorType.Of(exception));
        }

        // The one measurement per execution: its count is the number of executions and its error.type
        // subset is the number of failures, so the two counters that used to report those numbers were
        // two extra instrument writes per fire for something the exporter already had.
        jobExecuteDuration.Record(duration.TotalSeconds, tags);
    }
}

internal readonly struct Instrumentation
{
    private readonly Meters? meters;
    private readonly TagList tagList;

    public Instrumentation(Meters meters, TagList tagList)
    {
        this.meters = meters;
        this.tagList = tagList;
    }

    public void EndJobExecute(TimeSpan duration, Exception? exception)
    {
        // Nothing was recorded at the start — no listener was collecting — so there is nothing to close.
        meters?.EndJobExecute(tagList, duration, exception);
    }
}
