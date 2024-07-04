using System.Text.Json;

using Quartz.Impl.Calendar;

namespace Quartz.Serialization.Json.Calendars;

internal sealed class DailyCalendarSerializer : CalendarSerializer<DailyCalendar>
{
    public override string CalendarTypeName => "DailyCalendar";

    protected override DailyCalendar Create(JsonElement jsonElement, JsonSerializerOptions options)
    {
        var rangeStartingTime = jsonElement.GetProperty(options.GetPropertyName("RangeStartingTime")).GetString()!;
        var rangeEndingTime = jsonElement.GetProperty(options.GetPropertyName("RangeEndingTime")).GetString()!;
        return new DailyCalendar(rangeStartingTime, rangeEndingTime);
    }

    protected override void SerializeFields(Utf8JsonWriter writer, DailyCalendar calendar, JsonSerializerOptions options)
    {
        writer.WriteBoolean(options.GetPropertyName("InvertTimeRange"), calendar.InvertTimeRange);
        writer.WriteString(options.GetPropertyName("RangeStartingTime"), calendar.RangeStartingTime);
        writer.WriteString(options.GetPropertyName("RangeEndingTime"), calendar.RangeEndingTime);
    }

    protected override void DeserializeFields(DailyCalendar calendar, JsonElement jsonElement, JsonSerializerOptions options)
    {
        calendar.InvertTimeRange = jsonElement.GetProperty(options.GetPropertyName("InvertTimeRange")).GetBoolean();
    }
}