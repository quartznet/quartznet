using System.Text.Json;

using Quartz.Impl.Calendar;

namespace Quartz.Calendars;

internal sealed class DailyCalendarSerializer : CalendarSerializer<DailyCalendar>
{
    public const string CalendarTypeKey = "DailyCalendar";

    public override string CalendarTypeForJson => CalendarTypeKey;

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