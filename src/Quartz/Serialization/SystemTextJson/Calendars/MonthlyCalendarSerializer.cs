using System.Text.Json;

using Quartz.Impl.Calendar;

namespace Quartz.Serialization.SystemTextJson.Calendars;

internal sealed class MonthlyCalendarSerializer : CalendarSerializer<MonthlyCalendar>
{
    public override string CalendarTypeName => "MonthlyCalendar";

    protected override MonthlyCalendar Create(JsonElement jsonElement, JsonSerializerOptions options)
    {
        return new MonthlyCalendar();
    }

    protected override void SerializeFields(Utf8JsonWriter writer, MonthlyCalendar calendar, JsonSerializerOptions options)
    {
        writer.WriteArray(options.GetPropertyName("ExcludedDays"), calendar.DaysExcluded.Order(), static (w, v) => w.WriteNumberValue(v));
    }

    protected override void DeserializeFields(MonthlyCalendar calendar, JsonElement jsonElement, JsonSerializerOptions options)
    {
        var excludedDays = jsonElement.GetProperty(options.GetPropertyName("ExcludedDays"));
        foreach (int day in excludedDays.GetDayOfMonthArray())
        {
            calendar.AddExcludedDay(day);
        }
    }
}
