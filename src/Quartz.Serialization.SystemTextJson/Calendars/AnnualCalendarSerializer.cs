using System.Text.Json;

using Quartz.Impl.Calendar;
using Quartz.Util;

namespace Quartz.Calendars;

internal sealed class AnnualCalendarSerializer : CalendarSerializer<AnnualCalendar>
{
    public const string CalendarTypeKey = "AnnualCalendar";

    public override string CalendarTypeForJson => CalendarTypeKey;

    protected override AnnualCalendar Create(JsonElement jsonElement)
    {
        return new AnnualCalendar();
    }

    protected override void SerializeFields(Utf8JsonWriter writer, AnnualCalendar calendar)
    {
        writer.WriteDateTimeArray("ExcludedDays", calendar.DaysExcluded);
    }

    protected override void DeserializeFields(AnnualCalendar calendar, JsonElement jsonElement)
    {
        var excludedDates = jsonElement.GetProperty("ExcludedDays").GetDateTimeArray();
        foreach (var date in excludedDates)
        {
            calendar.SetDayExcluded(date, true);
        }
    }
}