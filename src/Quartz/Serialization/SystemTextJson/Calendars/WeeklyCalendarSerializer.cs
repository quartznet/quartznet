using System.Text.Json;

using Quartz.Impl.Calendar;

namespace Quartz.Serialization.SystemTextJson.Calendars;

internal sealed class WeeklyCalendarSerializer : CalendarSerializer<WeeklyCalendar>
{
    public override string CalendarTypeName => "WeeklyCalendar";

    protected override WeeklyCalendar Create(JsonElement jsonElement, JsonSerializerOptions options)
    {
        return new WeeklyCalendar();
    }

    protected override void SerializeFields(Utf8JsonWriter writer, WeeklyCalendar calendar, JsonSerializerOptions options)
    {
        writer.WriteArray(options.GetPropertyName("ExcludedDays"), calendar.DaysExcluded.Order(), static (w, v) => w.WriteEnumValue(v));
    }

    protected override void DeserializeFields(WeeklyCalendar calendar, JsonElement jsonElement, JsonSerializerOptions options)
    {
        var excludedDays = jsonElement.GetProperty(options.GetPropertyName("ExcludedDays"));

        // A default WeeklyCalendar excludes the weekend, and the payload is the whole truth about
        // what is excluded, so start from nothing.
        calendar.RemoveExcludedDay(DayOfWeek.Saturday);
        calendar.RemoveExcludedDay(DayOfWeek.Sunday);

        foreach (DayOfWeek day in excludedDays.GetDayOfWeekArray())
        {
            calendar.AddExcludedDay(day);
        }
    }
}
