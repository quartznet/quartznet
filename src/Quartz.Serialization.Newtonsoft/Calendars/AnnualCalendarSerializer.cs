using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Impl.Calendar;
using Quartz.Serialization.Newtonsoft;

namespace Quartz.Calendars;

internal sealed class AnnualCalendarSerializer : CalendarSerializer<AnnualCalendar>
{
    protected override AnnualCalendar Create(JObject source)
    {
        return new AnnualCalendar();
    }

    protected override void SerializeFields(JsonWriter writer, AnnualCalendar calendar)
    {
        writer.WritePropertyName("ExcludedDays");
        writer.WriteStartArray();
        foreach (var day in calendar.DaysExcluded)
        {
            writer.WriteValue(day);
        }
        writer.WriteEndArray();
    }

    protected override void DeserializeFields(AnnualCalendar calendar, JObject source)
    {
        var annualCalendar = calendar;
        var excludedDates = source["ExcludedDays"]!.Values<DateTimeOffset>();
        foreach (var date in excludedDates)
        {
            annualCalendar.SetDayExcluded(date.DateTime, exclude: true);
        }
    }
}