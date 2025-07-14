namespace Quartz;

internal sealed class CalendarConfiguration
{
    public CalendarConfiguration(
        string name,
        ICalendar calendar,
        bool replace,
        bool updateTriggers)
    {
        Name = name;
        Calendar = calendar;
        Replace = replace;
        UpdateTriggers = updateTriggers;
    }

    public string Name { get; }
    public ICalendar Calendar { get; }
    public bool Replace { get; }
    public bool UpdateTriggers { get; }
}