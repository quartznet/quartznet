using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Impl.Calendar;
using Quartz.Serialization.Newtonsoft;

namespace Quartz.Calendars;

internal sealed class WeeklyCalendarSerializer : CalendarSerializer<WeeklyCalendar>
{
    protected override WeeklyCalendar Create(JObject source)
    {
        return new WeeklyCalendar();
    }

    protected override void SerializeFields(JsonWriter writer, WeeklyCalendar calendar)
    {
        writer.WritePropertyName("ExcludedDays");
        writer.WriteStartArray();
        foreach (var day in calendar.DaysExcluded)
        {
            writer.WriteValue(day);
        }
        writer.WriteEndArray();
    }

    protected override void DeserializeFields(WeeklyCalendar calendar, JObject source)
    {
        calendar.DaysExcluded = source["ExcludedDays"]!.Values<bool>().ToArray();
    }
}