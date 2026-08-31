using Quartz.Extensibility;

namespace Quartz;

/// <summary>
/// Schedule builders offer fluent interface and are responsible for creating schedules.
/// </summary>
/// <seealso cref="SimpleScheduleBuilder"/>
/// <seealso cref="CalendarIntervalScheduleBuilder"/>
/// <seealso cref="CronScheduleBuilder"/>
/// <seealso cref="DailyTimeIntervalScheduleBuilder"/>
public interface IScheduleBuilder
{
    /// <summary>
    /// Build the actual Trigger -- NOT intended to be invoked by end users,
    /// but will rather be invoked by a TriggerBuilder which this
    /// ScheduleBuilder is given to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="IMutableTrigger" /> in the signature is deliberate, and it is the reason this
    /// member says it is not for end users. <see cref="TriggerBuilder{TJob}.Build" /> has to write the
    /// identity, the job key, the calendar name, the start and end times, the priority and the misfire
    /// instruction onto what it is handed, so a schedule builder that returned <see cref="ITrigger" />
    /// would only move the downcast one line later and lose the compiler's help doing it.
    /// </para>
    /// <para>
    /// The alternative — moving this interface into <c>Quartz.Extensibility</c> beside its return type —
    /// was considered and refused: <c>WithSchedule(IScheduleBuilder)</c> is the most-read signature on
    /// the trigger builder, and putting an extensibility namespace into it would charge every reader for
    /// a member no mainstream caller invokes. The leak is a declaration, not a call.
    /// </para>
    /// </remarks>
    /// <seealso cref="TriggerBuilder{TJob}.WithSchedule" />
    IMutableTrigger Build();
}