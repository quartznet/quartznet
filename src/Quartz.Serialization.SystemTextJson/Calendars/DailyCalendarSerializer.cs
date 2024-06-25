using System.Text.Json;

using Quartz.Impl.Calendar;
using Quartz.Serialization.SystemTextJson;

namespace Quartz.Calendars;

internal sealed class DailyCalendarSerializer : CalendarSerializer<DailyCalendar>
{
    public override string CalendarTypeName => "DailyCalendar";

    protected override DailyCalendar Create(JsonElement jsonElement)
    {
        var rangeStartingTime = jsonElement.GetProperty("RangeStartingTime").GetString()!;
        var rangeEndingTime = jsonElement.GetProperty("RangeEndingTime").GetString()!;
        return new DailyCalendar(rangeStartingTime, rangeEndingTime);
    }

    protected override void SerializeFields(Utf8JsonWriter writer, DailyCalendar calendar)
    {
        writer.WriteString("RangeStartingTime", calendar.RangeStartingTime);
        writer.WriteString("RangeEndingTime", calendar.RangeEndingTime);
        writer.WriteBoolean("InvertTimeRange", calendar.InvertTimeRange);
    }

    protected override void DeserializeFields(DailyCalendar calendar, JsonElement jsonElement)
    {
        calendar.InvertTimeRange = jsonElement.GetProperty("InvertTimeRange").GetBoolean();
    }
}