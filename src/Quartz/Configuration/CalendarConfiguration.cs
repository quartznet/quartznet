namespace Quartz.Configuration;

internal sealed class CalendarConfiguration(
    string name,
    ICalendar calendar,
    bool replace,
    bool updateTriggers)
{
    public string Name { get; } = name;
    public ICalendar Calendar { get; } = calendar;
    public bool Replace { get; } = replace;
    public bool UpdateTriggers { get; } = updateTriggers;
}