using Quartz.Impl.Triggers;

namespace Quartz;

/// <summary>
/// A <see cref="ITrigger" /> that is used to fire a <see cref="IJob" />
/// at a given moment in time, and optionally repeated at a specified interval.
/// </summary>
/// <remarks>
/// This is a read model: to change a trigger's schedule, rebuild it with
/// <see cref="ITrigger.GetTriggerBuilder" /> and hand it to
/// <see cref="IScheduler.RescheduleJob" />.
/// </remarks>
/// <seealso cref="TriggerBuilder" />
/// <seealso cref="SimpleScheduleBuilder" />
/// <author>James House</author>
/// <author>Contributions by Lieven Govaerts of Ebitec Nv, Belgium.</author>
/// <author>Marko Lahma (.NET)</author>
public interface ISimpleTrigger : ITrigger
{
    /// <summary>
    /// The number of times the <see cref="ISimpleTrigger" /> should
    /// repeat, after which it will be automatically deleted.
    /// </summary>
    /// <seealso cref="SimpleTriggerImpl.RepeatIndefinitely" />
    int RepeatCount { get; }

    /// <summary>
    /// The time interval at which the <see cref="ISimpleTrigger" /> repeats.
    /// </summary>
    TimeSpan RepeatInterval { get; }

    /// <summary>
    /// Get the number of times the <see cref="ISimpleTrigger" /> has already
    /// fired.
    /// </summary>
    int TimesTriggered { get; }

    /// <summary>
    /// What the scheduler does when this trigger misses a firing.
    /// </summary>
    /// <seealso cref="SimpleScheduleBuilder.WithMisfireInstruction" />
    SimpleTriggerMisfireInstruction MisfireInstruction { get; }

    /// <summary>
    /// Whether the interval is measured from the previous run's completion rather than from its scheduled
    /// time: a fixed delay rather than a fixed rate. <see langword="false" /> — the default — is a fixed
    /// rate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// While a firing runs the trigger is held, on every node, so it never overlaps itself and its
    /// <see cref="ITrigger.OverlapPolicy" /> is never asked. When the firing completes, however it ended,
    /// the next fire time becomes the completion time plus <see cref="RepeatInterval" />, within the end
    /// time and the repeat count. A retry fires at its retry time; the delay follows the last attempt.
    /// </para>
    /// <para>
    /// A cluster measures from completion only once every node is 4.7 or later: an older node ignores the
    /// column and runs the trigger at a fixed rate.
    /// </para>
    /// </remarks>
    /// <seealso cref="SimpleScheduleBuilder.MeasureIntervalFromCompletion" />
    bool MeasureIntervalFromCompletion => false;
}