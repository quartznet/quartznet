using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Impl.Calendar;
using Quartz.Serialization.Newtonsoft;

namespace Quartz.Calendars;

internal sealed class HolidayCalendarSerializer : CalendarSerializer<HolidayCalendar>
{
    protected override HolidayCalendar Create(JObject source)
    {
        return new HolidayCalendar();
    }

    protected override void SerializeFields(JsonWriter writer, HolidayCalendar calendar)
    {
        writer.WritePropertyName("ExcludedDates");
        writer.WriteStartArray();
        foreach (var day in calendar.ExcludedDates)
        {
            writer.WriteValue(day);
        }
        writer.WriteEndArray();
    }

    protected override void DeserializeFields(HolidayCalendar calendar, JObject source)
    {
        var excludedDates = source["ExcludedDates"]!.Values<DateTimeOffset>();
        foreach (var date in excludedDates)
        {
            calendar.AddExcludedDate(date.DateTime);
        }
    }
}