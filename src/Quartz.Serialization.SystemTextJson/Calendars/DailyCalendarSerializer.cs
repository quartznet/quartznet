using System.Text.Json;

using Quartz.Impl.Calendar;
using Quartz.Util;

namespace Quartz.Calendars;

internal sealed class DailyCalendarSerializer : CalendarSerializer<DailyCalendar>
{
    public static DailyCalendarSerializer Instance { get; } = new();

    private DailyCalendarSerializer()
    {
    }

    public static readonly string CalendarTypeKey = typeof(DailyCalendar).AssemblyQualifiedNameWithoutVersion();

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