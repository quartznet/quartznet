using System.Text.Json;

using Quartz.Impl.Calendar;
using Quartz.Serialization.SystemTextJson;

namespace Quartz.Calendars;

internal sealed class BaseCalendarSerializer : CalendarSerializer<BaseCalendar>
{
    public override string CalendarTypeName => "BaseCalendar";

    protected override BaseCalendar Create(JsonElement jsonElement, JsonSerializerOptions options)
    {
        return new BaseCalendar();
    }

    protected override void SerializeFields(Utf8JsonWriter writer, BaseCalendar calendar, JsonSerializerOptions options)
    {
    }

    protected override void DeserializeFields(BaseCalendar calendar, JsonElement jsonElement, JsonSerializerOptions options)
    {
    }
}