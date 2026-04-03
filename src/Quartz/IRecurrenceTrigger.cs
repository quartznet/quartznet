
namespace Quartz;

/// <summary>
/// A trigger that fires based on an iCalendar RFC 5545 recurrence rule (RRULE).
/// </summary>
/// <remarks>
/// <para>
/// This trigger supports complex scheduling patterns that cannot be expressed with
/// CRON expressions, such as "every 2nd Monday of the month" or "every other week
/// on Monday, Wednesday, and Friday".
/// </para>
/// <para>
/// The recurrence rule string follows the RFC 5545 RRULE format, for example:
/// <c>FREQ=MONTHLY;BYDAY=2MO</c> or <c>FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR</c>.
/// </para>
/// </remarks>
/// <seealso cref="RecurrenceScheduleBuilder"/>
public interface IRecurrenceTrigger : ITrigger
{
    /// <summary>
    /// The RFC 5545 RRULE string, e.g. "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR".
    /// </summary>
    string RecurrenceRule { get; set; }

    /// <summary>
    /// The time zone within which recurrence calculations are performed.
    /// Defaults to <see cref="TimeZoneInfo.Local"/> if not set.
    /// </summary>
    TimeZoneInfo TimeZone { get; set; }

    /// <summary>
    /// The number of times this trigger has already fired.
    /// </summary>
    int TimesTriggered { get; set; }
}
