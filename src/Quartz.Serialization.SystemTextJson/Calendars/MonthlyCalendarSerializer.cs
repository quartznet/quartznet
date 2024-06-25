using System.Text.Json;

using Quartz.Impl.Calendar;
using Quartz.Serialization.SystemTextJson;
using Quartz.Util;

namespace Quartz.Calendars;

internal sealed class MonthlyCalendarSerializer : CalendarSerializer<MonthlyCalendar>
{
    public override string CalendarTypeName => "MonthlyCalendar";

    protected override MonthlyCalendar Create(JsonElement jsonElement, JsonSerializerOptions options)
    {
        return new MonthlyCalendar();
    }

    protected override void SerializeFields(Utf8JsonWriter writer, MonthlyCalendar calendar, JsonSerializerOptions options)
    {
        writer.WriteBooleanArray(options.GetPropertyName("ExcludedDays"), calendar.DaysExcluded);
    }

    protected override void DeserializeFields(MonthlyCalendar calendar, JsonElement jsonElement, JsonSerializerOptions options)
    {
        calendar.DaysExcluded = jsonElement.GetProperty(options.GetPropertyName("ExcludedDays")).GetBooleanArray();
    }
}