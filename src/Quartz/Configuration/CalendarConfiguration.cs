namespace Quartz.Configuration;

/// <summary>
/// A calendar a scheduler was told to carry, registered under that scheduler's service key.
/// </summary>
internal sealed class CalendarConfiguration
{
    public CalendarConfiguration(
        string name,
        ICalendar calendar,
        AddCalendarOptions options)
    {
        Name = name;
        Calendar = calendar;
        Options = options;
    }

    public string Name { get; }
    public ICalendar Calendar { get; }

    /// <summary>
    /// How the calendar is added.
    /// </summary>
    public AddCalendarOptions Options { get; }
}
