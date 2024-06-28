using System.Text.Json;

using Quartz.Impl.Calendar;

namespace Quartz.Serialization.Json.Calendars;

internal sealed class CronCalendarSerializer : CalendarSerializer<CronCalendar>
{
    public override string CalendarTypeName => "CronCalendar";

    protected override CronCalendar Create(JsonElement jsonElement, JsonSerializerOptions options)
    {
        var cronExpression = jsonElement.GetProperty(options.GetPropertyName("CronExpressionString")).GetString();
        return new CronCalendar(cronExpression!);
    }

    protected override void SerializeFields(Utf8JsonWriter writer, CronCalendar calendar, JsonSerializerOptions options)
    {
        writer.WriteString(options.GetPropertyName("CronExpressionString"), calendar.CronExpression.CronExpressionString);
    }

    protected override void DeserializeFields(CronCalendar value, JsonElement jsonElement, JsonSerializerOptions options)
    {
    }
}