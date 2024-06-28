using System.Text.Json;

using Quartz.Impl.Calendar;

namespace Quartz.Serialization.Json.Calendars;

internal sealed class WeeklyCalendarSerializer : CalendarSerializer<WeeklyCalendar>
{
    public override string CalendarTypeName => "WeeklyCalendar";

    protected override WeeklyCalendar Create(JsonElement jsonElement, JsonSerializerOptions options)
    {
        return new WeeklyCalendar();
    }

    protected override void SerializeFields(Utf8JsonWriter writer, WeeklyCalendar calendar, JsonSerializerOptions options)
    {
        writer.WriteBooleanArray(options.GetPropertyName("ExcludedDays"), calendar.DaysExcluded);
    }

    protected override void DeserializeFields(WeeklyCalendar calendar, JsonElement jsonElement, JsonSerializerOptions options)
    {
        calendar.DaysExcluded = jsonElement.GetProperty(options.GetPropertyName("ExcludedDays")).GetBooleanArray();
    }
}