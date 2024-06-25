using System.Text.Json;

using Quartz.Impl.Calendar;
using Quartz.Serialization.SystemTextJson;
using Quartz.Util;

namespace Quartz.Calendars;

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
        writer.WriteString(options.GetPropertyName("RangeStartingTime"), calendar.RangeStartingTime);
        writer.WriteString(options.GetPropertyName("RangeEndingTime"), calendar.RangeEndingTime);
        writer.WriteBoolean(options.GetPropertyName("InvertTimeRange"), calendar.InvertTimeRange);
    }

    protected override void DeserializeFields(DailyCalendar calendar, JsonElement jsonElement, JsonSerializerOptions options)
    {
        calendar.InvertTimeRange = jsonElement.GetProperty(options.GetPropertyName("InvertTimeRange")).GetBoolean();
    }
}