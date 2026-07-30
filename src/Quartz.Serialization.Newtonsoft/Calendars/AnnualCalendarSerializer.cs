using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Impl.Calendar;
using Quartz.Serialization.Newtonsoft;
using Quartz.Util;

namespace Quartz.Calendars;

internal sealed class AnnualCalendarSerializer : CalendarSerializer<AnnualCalendar>
{
    protected override AnnualCalendar Create(JObject source)
    {
        return new AnnualCalendar();
    }

    protected override void SerializeFields(JsonWriter writer, AnnualCalendar calendar)
    {
        writer.WriteDateOnlyArray("ExcludedDays", calendar.DaysExcluded);
    }

    protected override void DeserializeFields(AnnualCalendar calendar, JObject source)
    {
        // Payloads written before 4.0 carry full timestamps here rather than dates.
        foreach (var day in source["ExcludedDays"]!.GetDateOnlyArray())
        {
            calendar.AddExcludedDay(day);
        }
    }
}
