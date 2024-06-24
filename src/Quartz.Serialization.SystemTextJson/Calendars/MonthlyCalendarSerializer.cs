using System.Text.Json;

using Quartz.Impl.Calendar;
using Quartz.Util;

namespace Quartz.Calendars;

internal sealed class MonthlyCalendarSerializer : CalendarSerializer<MonthlyCalendar>
{
    public static MonthlyCalendarSerializer Instance { get; } = new();

    private MonthlyCalendarSerializer()
    {
    }

    public static readonly string CalendarTypeKey = typeof(MonthlyCalendar).AssemblyQualifiedNameWithoutVersion();

    public override string CalendarTypeForJson => CalendarTypeKey;

    protected override MonthlyCalendar Create(JsonElement jsonElement)
    {
        return new MonthlyCalendar();
    }

    protected override void SerializeFields(Utf8JsonWriter writer, MonthlyCalendar calendar)
    {
        writer.WriteBooleanArray("ExcludedDays", calendar.DaysExcluded);
    }

    protected override void DeserializeFields(MonthlyCalendar calendar, JsonElement jsonElement)
    {
        calendar.DaysExcluded = jsonElement.GetProperty("ExcludedDays").GetBooleanArray();
    }
}