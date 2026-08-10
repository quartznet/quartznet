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
}