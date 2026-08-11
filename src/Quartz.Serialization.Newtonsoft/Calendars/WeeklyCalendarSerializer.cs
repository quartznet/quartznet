using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Impl.Calendar;
using Quartz.Util;

namespace Quartz.Serialization.Newtonsoft.Calendars;

internal sealed class WeeklyCalendarSerializer : CalendarSerializer<WeeklyCalendar>
{
    public override string CalendarTypeName => "WeeklyCalendar";

    protected override WeeklyCalendar Create(JObject source)
    {
        return new WeeklyCalendar();
    }

    protected override void SerializeFields(JsonWriter writer, WeeklyCalendar calendar)
    {
        writer.WriteArray("ExcludedDays", calendar.DaysExcluded.Order(), static (w, v) => w.WriteValue(v.ToString()));
    }

    protected override void DeserializeFields(WeeklyCalendar calendar, JObject source)
    {
        // A default WeeklyCalendar excludes the weekend, and the payload is the whole truth about
        // what is excluded, so start from nothing.
        calendar.RemoveExcludedDay(DayOfWeek.Saturday);
        calendar.RemoveExcludedDay(DayOfWeek.Sunday);

        foreach (DayOfWeek day in source["ExcludedDays"]!.GetDayOfWeekArray())
        {
            calendar.AddExcludedDay(day);
        }
    }
}
