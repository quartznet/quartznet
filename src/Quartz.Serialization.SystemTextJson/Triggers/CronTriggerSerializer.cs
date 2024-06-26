using System.Text.Json;

using Quartz.Util;

namespace Quartz.Triggers;

internal sealed class CronTriggerSerializer : TriggerSerializer<ICronTrigger>
{
    public static CronTriggerSerializer Instance { get; } = new();

    private CronTriggerSerializer()
    {
    }

    public const string TriggerTypeKey = "CronTrigger";

    public override string TriggerTypeForJson => TriggerTypeKey;

    public override IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement, JsonSerializerOptions options)
    {
        var cronExpressionString = jsonElement.GetProperty(options.GetPropertyName("CronExpressionString")).GetString()!;
        var timeZone = jsonElement.GetProperty(options.GetPropertyName("TimeZone")).GetTimeZone();

        return CronScheduleBuilder.CronSchedule(cronExpressionString)
            .InTimeZone(timeZone);
    }

    protected override void SerializeFields(Utf8JsonWriter writer, ICronTrigger trigger, JsonSerializerOptions options)
    {
        writer.WriteString(options.GetPropertyName("CronExpressionString"), trigger.CronExpressionString);
        writer.WriteTimeZoneInfo(options.GetPropertyName("TimeZone"), trigger.TimeZone);
    }
}