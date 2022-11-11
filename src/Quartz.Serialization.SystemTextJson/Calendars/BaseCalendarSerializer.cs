using System.Text.Json;

using Quartz.Impl.Calendar;

namespace Quartz.Calendars;

internal sealed class BaseCalendarSerializer : CalendarSerializer<BaseCalendar>
{
    public static BaseCalendarSerializer Instance { get; } = new();

    private BaseCalendarSerializer()
    {
    }

    public const string CalendarTypeKey = "BaseCalendar";

    public override string CalendarTypeForJson => CalendarTypeKey;

    protected override BaseCalendar Create(JsonElement jsonElement)
    {
        return new BaseCalendar();
    }

    protected override void SerializeFields(Utf8JsonWriter writer, BaseCalendar calendar)
    {
    }

    protected override void DeserializeFields(BaseCalendar calendar, JsonElement jsonElement)
    {
    }
}