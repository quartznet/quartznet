using System.Text.Json;

using Quartz.Impl.Calendar;

namespace Quartz.Serialization.SystemTextJson.Calendars;

internal sealed class DailyCalendarSerializer : CalendarSerializer<DailyCalendar>
{
    public override string CalendarTypeName => "DailyCalendar";

    protected override DailyCalendar Create(JsonElement jsonElement, JsonSerializerOptions options)
    {
        var (start, end) = jsonElement.GetDailyCalendarRange(options);
        return new DailyCalendar(start, end);
    }

    protected override void SerializeFields(Utf8JsonWriter writer, DailyCalendar calendar, JsonSerializerOptions options)
    {
        writer.WriteBoolean(options.GetPropertyName("InvertTimeRange"), calendar.InvertTimeRange);
        writer.WriteTimeOnly(options.GetPropertyName("RangeStart"), calendar.TimeRange.Start);
        writer.WriteTimeOnly(options.GetPropertyName("RangeEnd"), calendar.TimeRange.End);
    }

    protected override void DeserializeFields(DailyCalendar calendar, JsonElement jsonElement, JsonSerializerOptions options)
    {
        calendar.InvertTimeRange = jsonElement.GetProperty(options.GetPropertyName("InvertTimeRange")).GetBoolean();
    }
}
