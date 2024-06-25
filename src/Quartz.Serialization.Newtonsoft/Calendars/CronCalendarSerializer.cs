using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Impl.Calendar;
using Quartz.Serialization.Newtonsoft;

namespace Quartz.Calendars;

internal sealed class CronCalendarSerializer : CalendarSerializer<CronCalendar>
{
    protected override CronCalendar Create(JObject source)
    {
        string cronExpression = source["CronExpressionString"]!.Value<string>()!;
        return new CronCalendar(cronExpression);
    }

    protected override void SerializeFields(JsonWriter writer, CronCalendar calendar)
    {
        writer.WritePropertyName("CronExpressionString");
        writer.WriteValue(calendar.CronExpression?.CronExpressionString);
    }

    protected override void DeserializeFields(CronCalendar value, JObject source)
    {
    }
}