using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Impl.Calendar;
using Quartz.Serialization.Newtonsoft;
using Quartz.Util;

namespace Quartz.Calendars;

internal sealed class DailyCalendarSerializer : CalendarSerializer<DailyCalendar>
{
    protected override DailyCalendar Create(JObject source)
    {
        var (start, end) = source.GetDailyCalendarRange();
        return new DailyCalendar(start, end);
    }

    protected override void SerializeFields(JsonWriter writer, DailyCalendar calendar)
    {
        writer.WritePropertyName("InvertTimeRange");
        writer.WriteValue(calendar.InvertTimeRange);

        writer.WriteTimeOnly("RangeStart", calendar.TimeRange.Start);
        writer.WriteTimeOnly("RangeEnd", calendar.TimeRange.End);
    }

    protected override void DeserializeFields(DailyCalendar calendar, JObject source)
    {
        calendar.InvertTimeRange = source["InvertTimeRange"]!.Value<bool>();
    }
}
