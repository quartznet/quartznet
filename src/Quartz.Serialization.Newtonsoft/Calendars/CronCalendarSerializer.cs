using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Impl.Calendar;

namespace Quartz.Serialization.Newtonsoft.Calendars;

internal sealed class CronCalendarSerializer : CalendarSerializer<CronCalendar>
{
    public override string CalendarTypeName => "CronCalendar";

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