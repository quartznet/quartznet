using System.Text.Json;

using Quartz.Impl.Calendar;

namespace Quartz.Serialization.Json.Calendars;

internal sealed class AnnualCalendarSerializer : CalendarSerializer<AnnualCalendar>
{
    public override string CalendarTypeName => "AnnualCalendar";

    protected override AnnualCalendar Create(JsonElement jsonElement, JsonSerializerOptions options)
    {
        return new AnnualCalendar();
    }

    protected override void SerializeFields(Utf8JsonWriter writer, AnnualCalendar calendar, JsonSerializerOptions options)
    {
        writer.WriteDateTimeArray(options.GetPropertyName("ExcludedDays"), calendar.DaysExcluded);
    }

    protected override void DeserializeFields(AnnualCalendar calendar, JsonElement jsonElement, JsonSerializerOptions options)
    {
        var excludedDates = jsonElement.GetProperty(options.GetPropertyName("ExcludedDays")).GetDateTimeArray();
        foreach (var date in excludedDates)
        {
            calendar.SetDayExcluded(date, exclude: true);
        }
    }
}