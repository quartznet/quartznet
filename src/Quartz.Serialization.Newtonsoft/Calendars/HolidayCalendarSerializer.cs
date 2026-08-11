using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Impl.Calendar;
using Quartz.Util;

namespace Quartz.Serialization.Newtonsoft.Calendars;

internal sealed class HolidayCalendarSerializer : CalendarSerializer<HolidayCalendar>
{
    public override string CalendarTypeName => "HolidayCalendar";

    protected override HolidayCalendar Create(JObject source)
    {
        return new HolidayCalendar();
    }

    protected override void SerializeFields(JsonWriter writer, HolidayCalendar calendar)
    {
        writer.WriteDateOnlyArray("ExcludedDates", calendar.DaysExcluded);
    }

    protected override void DeserializeFields(HolidayCalendar calendar, JObject source)
    {
        // Payloads written before 4.0 carry full timestamps here rather than dates.
        foreach (var date in source["ExcludedDates"]!.GetDateOnlyArray())
        {
            calendar.AddExcludedDay(date);
        }
    }
}
