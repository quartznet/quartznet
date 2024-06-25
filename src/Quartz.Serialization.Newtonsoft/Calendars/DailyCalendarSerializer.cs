using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Impl.Calendar;
using Quartz.Serialization.Newtonsoft;

namespace Quartz.Calendars;

internal sealed class DailyCalendarSerializer : CalendarSerializer<DailyCalendar>
{
    protected override DailyCalendar Create(JObject source)
    {
        var rangeStartingTime = source["RangeStartingTime"]!.Value<string>()!;
        var rangeEndingTime = source["RangeEndingTime"]!.Value<string>()!;
        return new DailyCalendar(null, rangeStartingTime, rangeEndingTime);
    }

    protected override void SerializeFields(JsonWriter writer, DailyCalendar calendar)
    {
        writer.WritePropertyName("InvertTimeRange");
        writer.WriteValue(calendar.InvertTimeRange);

        writer.WritePropertyName("RangeStartingTime");
        writer.WriteValue(calendar.RangeStartingTime);

        writer.WritePropertyName("RangeEndingTime");
        writer.WriteValue(calendar.RangeEndingTime);
    }

    protected override void DeserializeFields(DailyCalendar calendar, JObject source)
    {
        calendar.InvertTimeRange = source["InvertTimeRange"]!.Value<bool>();
    }
}