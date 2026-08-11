using Quartz.Extensibility;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// What a trigger persistence delegate reads back for one trigger: the schedule builder that
/// recreates its schedule, and optionally an applier that restores trigger state the schedule
/// builder cannot carry, such as how many times the trigger has fired.
/// </summary>
public sealed class TriggerPropertyBundle
{
    public TriggerPropertyBundle(IScheduleBuilder scheduleBuilder)
        : this(scheduleBuilder, applyState: null)
    {
    }

    public TriggerPropertyBundle(IScheduleBuilder scheduleBuilder, Action<IOperableTrigger>? applyState)
    {
        ScheduleBuilder = scheduleBuilder;
        ApplyState = applyState;
    }

    /// <summary>
    /// Recreates the trigger's schedule when the trigger is materialized from its stored rows.
    /// </summary>
    public IScheduleBuilder ScheduleBuilder { get; }

    /// <summary>
    /// Restores state onto the materialized trigger, or <see langword="null"/> when the delegate
    /// carries no state beyond the schedule. The shipped delegates assign the fire count by casting
    /// to the concrete trigger type: <c>t =&gt; ((SimpleTriggerImpl) t).TimesTriggered = timesTriggered</c>.
    /// </summary>
    public Action<IOperableTrigger>? ApplyState { get; }
}
