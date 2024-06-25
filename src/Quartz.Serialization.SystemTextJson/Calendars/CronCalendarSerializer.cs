using System.Text.Json;

using Quartz.Impl.Calendar;
using Quartz.Serialization.SystemTextJson;

namespace Quartz.Calendars;

internal sealed class CronCalendarSerializer : CalendarSerializer<CronCalendar>
{
    public override string CalendarTypeName => "CronCalendar";

    protected override CronCalendar Create(JsonElement jsonElement)
    {
        var cronExpression = jsonElement.GetProperty("CronExpressionString").GetString();
        return new CronCalendar(cronExpression!);
    }

    protected override void SerializeFields(Utf8JsonWriter writer, CronCalendar calendar)
    {
        writer.WriteString("CronExpressionString", calendar.CronExpression.CronExpressionString);
    }

    protected override void DeserializeFields(CronCalendar value, JsonElement jsonElement)
    {
    }
}