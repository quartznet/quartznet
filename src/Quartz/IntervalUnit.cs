namespace Quartz;

/// <summary>
/// Supported interval units used by <see cref="ICalendarIntervalTrigger" />.
/// </summary>
public enum IntervalUnit
{
    /// <summary>
    /// Milliseconds.
    /// </summary>
    Millisecond,
    /// <summary>
    /// Seconds.
    /// </summary>
    Second,
    /// <summary>
    /// Minutes.
    /// </summary>
    Minute,
    /// <summary>
    /// Hours.
    /// </summary>
    Hour,
    /// <summary>
    /// Days. A calendar-interval trigger counts calendar days, so a daily interval keeps its
    /// time of day across a daylight saving change.
    /// </summary>
    Day,
    /// <summary>
    /// Weeks, of seven calendar days.
    /// </summary>
    Week,
    /// <summary>
    /// Calendar months. The day of the month is kept, and clamped to the last day of a shorter one.
    /// </summary>
    Month,
    /// <summary>
    /// Calendar years.
    /// </summary>
    Year
}