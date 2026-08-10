using System.Text.Json;

namespace Quartz.Serialization.SystemTextJson.Triggers;

public class CronTriggerSerializer : TriggerSerializer<ICronTrigger>
{
    public override string TriggerTypeName => "CronTrigger";

    public override IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement, JsonSerializerOptions options)
    {
        var cronExpressionString = jsonElement.GetProperty(options.GetPropertyName("CronExpressionString")).GetString()!;
        var timeZone = jsonElement.GetProperty(options.GetPropertyName("TimeZone")).GetTimeZone();

        return CronScheduleBuilder.Create(cronExpressionString)
            .InTimeZone(timeZone);
    }

    protected override void SerializeFields(Utf8JsonWriter writer, ICronTrigger trigger, JsonSerializerOptions options)
    {
        writer.WriteString(options.GetPropertyName("CronExpressionString"), trigger.CronExpressionString);
        writer.WriteTimeZoneInfo(options.GetPropertyName("TimeZone"), trigger.TimeZone);
    }
}