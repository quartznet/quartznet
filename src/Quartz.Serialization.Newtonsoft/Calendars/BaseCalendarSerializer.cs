using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Impl.Calendar;

namespace Quartz.Serialization.Newtonsoft.Calendars;

internal sealed class BaseCalendarSerializer : CalendarSerializer<BaseCalendar>
{
    public override string CalendarTypeName => "BaseCalendar";

    protected override BaseCalendar Create(JObject source)
    {
        return new BaseCalendar();
    }

    protected override void SerializeFields(JsonWriter writer, BaseCalendar calendar)
    {
    }

    protected override void DeserializeFields(BaseCalendar calendar, JObject source)
    {
    }
}