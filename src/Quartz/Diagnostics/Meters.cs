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
    private readonly Counter<long> triggerMisfires;
    private readonly Counter<long> triggerRetries;
    private readonly Histogram<double> triggerAcquisitionDuration;
    private readonly Counter<long> triggersAcquired;
    private readonly Histogram<double> clusterCheckinDuration;
    private readonly Counter<long> clusterRecoveredTriggers;
    private readonly Histogram<double> jobStoreOperationDuration;

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

        // A misfire is a fire that was owed and did not happen on time, which is the number an operator
        // wants an alert on. Counted once per trigger the scheduler is told misfired, wherever the store
        // noticed it: the notification is what every store has in common.
        triggerMisfires = meter.CreateCounter<long>("quartz.trigger.misfire", "{trigger}", "Number of trigger misfires the scheduler was notified of");

        // A retry is a fire that happened, failed, and is being attempted again — the other half of the
        // picture a misfire count gives, and the number that says whether a job is limping along on its
        // retries rather than working. Counted once per retry the scheduler schedules, not per attempt
        // configured, so a policy that never has to be used contributes nothing.
        triggerRetries = meter.CreateCounter<long>("quartz.trigger.retry", "{trigger}", "Number of trigger retries the scheduler scheduled after a job failed");

        // How long the scheduling loop waits on its store for the next batch. This is the round trip the
        // loop cannot overlap with anything, so it is what a slow or contended store shows up as.
        triggerAcquisitionDuration = meter.CreateHistogram<double>("quartz.trigger.acquisition.duration", "s", "Elapsed time spent acquiring the next batch of triggers");

        // And how many the round actually returned, which is what tells an idle scheduler apart from a
        // saturated one at the same acquisition rate.
        triggersAcquired = meter.CreateCounter<long>("quartz.trigger.acquired", "{trigger}", "Number of triggers acquired for firing");

        // A check-in that slows down is how a cluster starts failing: the other nodes decide this one
        // died once it stops arriving, and recover work it is still doing.
        clusterCheckinDuration = meter.CreateHistogram<double>("quartz.cluster.checkin.duration", "s", "Elapsed time spent on a cluster check-in");

        // What recovering a failed node cost, counted against the node that failed rather than the one
        // doing the recovering.
        clusterRecoveredTriggers = meter.CreateCounter<long>("quartz.cluster.recovery.trigger", "{trigger}", "Number of fired triggers recovered from a failed cluster node");

        // Every round trip a scheduler makes to its store, named by the operation. The histogram's own
        // count is how many of each there were, and its error.type subset how many failed, so this one
        // instrument answers rate, latency and failure for the whole store surface.
        jobStoreOperationDuration = meter.CreateHistogram<double>("quartz.jobstore.operation.duration", "s", "Elapsed time spent on a job store operation");
    }

    public static Meters Shared => shared.Value;

    /// <summary>
    /// Whether anything is collecting the acquisition instruments, so the scheduling loop can skip
    /// timing the round when nothing would read the answer.
    /// </summary>
    internal bool TriggerAcquisitionEnabled => triggerAcquisitionDuration.Enabled || triggersAcquired.Enabled;

    /// <summary>
    /// Whether anything is collecting the check-in histogram, asked before the check-in is timed.
    /// </summary>
    internal bool ClusterCheckinEnabled => clusterCheckinDuration.Enabled;

    /// <summary>
    /// Whether anything is collecting the recovery counter.
    /// </summary>
    internal bool ClusterRecoveryEnabled => clusterRecoveredTriggers.Enabled;

    /// <summary>
    /// Whether anything is collecting the store-operation histogram. Asked once per store call, before
    /// a timestamp is read, so that a scheduler nobody is watching pays a boolean per operation.
    /// </summary>
    internal bool JobStoreOperationEnabled => jobStoreOperationDuration.Enabled;

    /// <summary>
    /// One round trip to the job store, named by <c>operationName</c> — which is one of the
    /// <see cref="OperationName.JobStore"/> names, and so the same string the operation's span is
    /// called, so one value finds a slow operation in a trace and in a metric alike.
    /// </summary>
    internal void RecordJobStoreOperation(
        string schedulerName,
        string schedulerId,
        string operationName,
        TimeSpan duration,
        Exception? exception)
    {
        TagList tags = new()
        {
            { ActivityTags.SchedulerName, schedulerName },
            { ActivityTags.SchedulerId, schedulerId },
            { ActivityTags.JobStoreOperation, operationName },
        };

        if (exception is not null)
        {
            tags.Add(ErrorType.TagName, ErrorType.Of(exception));
        }

        jobStoreOperationDuration.Record(duration.TotalSeconds, tags);
    }

    /// <summary>
    /// One trigger the scheduler was told had misfired.
    /// </summary>
    internal void TriggerMisfired(string schedulerName, string schedulerId, ITrigger trigger)
    {
        if (!triggerMisfires.Enabled)
        {
            return;
        }

        // The trigger's group but not its name: a misfire storm is a property of a group or of an
        // execution group, and one series per trigger is a cardinality an alert cannot be built on.
        TagList tags = new()
        {
            { ActivityTags.SchedulerName, schedulerName },
            { ActivityTags.SchedulerId, schedulerId },
            { ActivityTags.TriggerGroup, trigger.Key.Group },
        };

        if (trigger.ExecutionGroup is { } executionGroup)
        {
            tags.Add(ActivityTags.ExecutionGroup, executionGroup);
        }

        triggerMisfires.Add(1, tags);
    }

    /// <summary>
    /// One retry the scheduler has just scheduled for a trigger whose job failed.
    /// </summary>
    internal void TriggerRetryScheduled(string schedulerName, string schedulerId, ITrigger trigger)
    {
        if (!triggerRetries.Enabled)
        {
            return;
        }

        // The same tags a misfire carries, and for the same reason: one series per trigger is a
        // cardinality nobody can alert on, and retries cluster by group and by execution group.
        TagList tags = new()
        {
            { ActivityTags.SchedulerName, schedulerName },
            { ActivityTags.SchedulerId, schedulerId },
            { ActivityTags.TriggerGroup, trigger.Key.Group },
        };

        if (trigger.ExecutionGroup is { } executionGroup)
        {
            tags.Add(ActivityTags.ExecutionGroup, executionGroup);
        }

        triggerRetries.Add(1, tags);
    }

    /// <summary>
    /// One round of the scheduling loop's acquisition, however many triggers it came back with.
    /// </summary>
    internal void TriggersAcquired(string schedulerName, string schedulerId, int count, TimeSpan duration)
    {
        TagList tags = new()
        {
            { ActivityTags.SchedulerName, schedulerName },
            { ActivityTags.SchedulerId, schedulerId },
        };

        // Recorded even when the round came back empty: an acquisition that returns nothing still made
        // the round trip, and leaving those out would report only the busy scheduler's latency.
        triggerAcquisitionDuration.Record(duration.TotalSeconds, tags);

        if (count > 0)
        {
            triggersAcquired.Add(count, tags);
        }
    }

    /// <summary>
    /// One cluster check-in, and what it failed with when it did.
    /// </summary>
    internal void ClusterCheckinCompleted(string schedulerName, string schedulerId, TimeSpan duration, Exception? exception)
    {
        TagList tags = new()
        {
            { ActivityTags.SchedulerName, schedulerName },
            { ActivityTags.SchedulerId, schedulerId },
        };

        if (exception is not null)
        {
            tags.Add(ErrorType.TagName, ErrorType.Of(exception));
        }

        clusterCheckinDuration.Record(duration.TotalSeconds, tags);
    }

    /// <summary>
    /// The fired-trigger rows recovered from one failed node.
    /// </summary>
    /// <remarks>
    /// A counter's increment of <paramref name="count" /> is the same series as <paramref name="count" />
    /// increments of one, so a whole node's recovery is one instrument write rather than one per row.
    /// </remarks>
    internal void ClusterTriggersRecovered(string schedulerName, string schedulerId, string recoveredInstanceId, long count)
    {
        if (count <= 0 || !clusterRecoveredTriggers.Enabled)
        {
            return;
        }

        clusterRecoveredTriggers.Add(count, new TagList
        {
            { ActivityTags.SchedulerName, schedulerName },
            { ActivityTags.SchedulerId, schedulerId },
            // Which node's work this was. quartz.scheduler.id above is the node that did the recovering,
            // and a recovery is one node saying something about another.
            { ActivityTags.RecoveredInstanceId, recoveredInstanceId },
        });
    }

    /// <summary>
    /// A node that found its own scheduler state row gone: a peer judged it failed and took its work
    /// over. The one case where the two node ids of a recovery are the same node.
    /// </summary>
    /// <remarks>
    /// Recorded on the recovery counter, because it is the same event seen from the other side, and a
    /// dashboard filtering on the recovered node wants it there. It counts one rather than a number of
    /// triggers: the rows are gone by the time this node reads the table, so how much the peer recovered
    /// is not knowable here — the peer's own measurement carries that, under the same tag. A series where
    /// <see cref="ActivityTags.RecoveredInstanceId" /> equals <see cref="ActivityTags.SchedulerId" /> is
    /// therefore a count of events, not of triggers, and is what an alert on "this node is being failed
    /// out" watches.
    /// </remarks>
    internal void ClusterSelfRecoveryObserved(string schedulerName, string schedulerId)
    {
        if (!clusterRecoveredTriggers.Enabled)
        {
            return;
        }

        clusterRecoveredTriggers.Add(1, new TagList
        {
            { ActivityTags.SchedulerName, schedulerName },
            { ActivityTags.SchedulerId, schedulerId },
            { ActivityTags.RecoveredInstanceId, schedulerId },
        });
    }

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
