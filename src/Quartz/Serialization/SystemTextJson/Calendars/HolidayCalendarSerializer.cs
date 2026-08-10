using System.Text.Json;

using Quartz.Impl.Calendar;

namespace Quartz.Serialization.SystemTextJson.Calendars;

internal sealed class HolidayCalendarSerializer : CalendarSerializer<HolidayCalendar>
{
    public override string CalendarTypeName => "HolidayCalendar";

    protected override HolidayCalendar Create(JsonElement jsonElement, JsonSerializerOptions options)
    {
        return new HolidayCalendar();
    }

    protected override void SerializeFields(Utf8JsonWriter writer, HolidayCalendar calendar, JsonSerializerOptions options)
    {
        writer.WriteDateOnlyArray(options.GetPropertyName("ExcludedDates"), calendar.DaysExcluded);
    }

    protected override void DeserializeFields(HolidayCalendar calendar, JsonElement jsonElement, JsonSerializerOptions options)
    {
        // Payloads written before 4.0 carry full timestamps here rather than dates.
        var excludedDates = jsonElement.GetProperty(options.GetPropertyName("ExcludedDates")).GetDateOnlyArray();
        foreach (var date in excludedDates)
        {
            calendar.AddExcludedDay(date);
        }
    }
}
