using System.Linq;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Impl.Calendar;

namespace Quartz.Calendars;

internal sealed class MonthlyCalendarSerializer : CalendarSerializer<MonthlyCalendar>
{
    protected override MonthlyCalendar Create(JObject source)
    {
        return new MonthlyCalendar();
    }

    protected override void SerializeFields(JsonWriter writer, MonthlyCalendar calendar)
    {
        writer.WritePropertyName("ExcludedDays");
        writer.WriteStartArray();
        foreach (var day in calendar.DaysExcluded)
        {
            writer.WriteValue(day);
        }
        writer.WriteEndArray();
    }

    protected override void DeserializeFields(MonthlyCalendar value, JObject source)
    {
        value.DaysExcluded = source["ExcludedDays"]!.Values<bool>().ToArray();
    }
}