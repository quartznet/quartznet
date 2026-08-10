using System.Text.Json;

using Quartz.Impl.Calendar;

namespace Quartz.Serialization.SystemTextJson.Calendars;

internal sealed class AnnualCalendarSerializer : CalendarSerializer<AnnualCalendar>
{
    public override string CalendarTypeName => "AnnualCalendar";

    protected override AnnualCalendar Create(JsonElement jsonElement, JsonSerializerOptions options)
    {
        return new AnnualCalendar();
    }

    protected override void SerializeFields(Utf8JsonWriter writer, AnnualCalendar calendar, JsonSerializerOptions options)
    {
        writer.WriteDateOnlyArray(options.GetPropertyName("ExcludedDays"), calendar.DaysExcluded);
    }

    protected override void DeserializeFields(AnnualCalendar calendar, JsonElement jsonElement, JsonSerializerOptions options)
    {
        // Payloads written before 4.0 carry full timestamps here rather than dates.
        var excludedDays = jsonElement.GetProperty(options.GetPropertyName("ExcludedDays")).GetDateOnlyArray();
        foreach (var day in excludedDays)
        {
            calendar.AddExcludedDay(day);
        }
    }
}
