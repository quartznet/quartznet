using System.Text.Json;

using Quartz.Impl.Calendar;
using Quartz.Util;

namespace Quartz.Calendars;

internal sealed class HolidayCalendarSerializer : CalendarSerializer<HolidayCalendar>
{
    public const string CalendarTypeKey = "HolidayCalendar";

    public override string CalendarTypeForJson => CalendarTypeKey;

    protected override HolidayCalendar Create(JsonElement jsonElement)
    {
        return new HolidayCalendar();
    }

    protected override void SerializeFields(Utf8JsonWriter writer, HolidayCalendar calendar)
    {
        writer.WriteDateTimeArray("ExcludedDates", calendar.ExcludedDates);
    }

    protected override void DeserializeFields(HolidayCalendar calendar, JsonElement jsonElement)
    {
        var excludedDates = jsonElement.GetProperty("ExcludedDates").GetDateTimeArray();
        foreach (var date in excludedDates)
        {
            calendar.AddExcludedDate(date);
        }
    }
}