using System.Text.Json;

using Quartz.Impl.Calendar;
using Quartz.Serialization.SystemTextJson;
using Quartz.Util;

namespace Quartz.Calendars;

internal sealed class WeeklyCalendarSerializer : CalendarSerializer<WeeklyCalendar>
{
    public override string CalendarTypeName => "WeeklyCalendar";

    protected override WeeklyCalendar Create(JsonElement jsonElement)
    {
        return new WeeklyCalendar();
    }

    protected override void SerializeFields(Utf8JsonWriter writer, WeeklyCalendar calendar)
    {
        writer.WriteBooleanArray("ExcludedDays", calendar.DaysExcluded);
    }

    protected override void DeserializeFields(WeeklyCalendar calendar, JsonElement jsonElement)
    {
        calendar.DaysExcluded = jsonElement.GetProperty("ExcludedDays").GetBooleanArray();
    }
}