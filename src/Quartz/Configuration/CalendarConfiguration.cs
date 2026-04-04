namespace Quartz.Configuration;

internal sealed class CalendarConfiguration
{
    public CalendarConfiguration(
        string name,
        ICalendar calendar,
        bool replace,
        bool updateTriggers,
        string? optionsName = null)
    {
        Name = name;
        Calendar = calendar;
        Replace = replace;
        UpdateTriggers = updateTriggers;
        OptionsName = optionsName ?? "";
    }

    public string Name { get; }
    public ICalendar Calendar { get; }
    public bool Replace { get; }
    public bool UpdateTriggers { get; }
    public string OptionsName { get; }
}