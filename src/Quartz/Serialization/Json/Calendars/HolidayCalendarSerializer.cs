using System.Text.Json;

using Quartz.Impl.Calendar;

namespace Quartz.Serialization.Json.Calendars;

internal sealed class HolidayCalendarSerializer : CalendarSerializer<HolidayCalendar>
{
    public override string CalendarTypeName => "HolidayCalendar";

    protected override HolidayCalendar Create(JsonElement jsonElement, JsonSerializerOptions options)
    {
        return new HolidayCalendar();
    }

    protected override void SerializeFields(Utf8JsonWriter writer, HolidayCalendar calendar, JsonSerializerOptions options)
    {
        writer.WriteDateTimeArray(options.GetPropertyName("ExcludedDates"), calendar.ExcludedDates);
    }

    protected override void DeserializeFields(HolidayCalendar calendar, JsonElement jsonElement, JsonSerializerOptions options)
    {
        var excludedDates = jsonElement.GetProperty(options.GetPropertyName("ExcludedDates")).GetDateTimeArray();
        foreach (var date in excludedDates)
        {
            calendar.AddExcludedDate(date);
        }
    }
}