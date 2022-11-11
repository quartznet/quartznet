using System.Text.Json;

using Quartz.Util;

namespace Quartz.Triggers;

internal class CronTriggerSerializer : TriggerSerializer<ICronTrigger>
{
    public static CronTriggerSerializer Instance { get; } = new();

    private CronTriggerSerializer()
    {
    }

    public const string TriggerTypeKey = "CronTrigger";

    public override string TriggerTypeForJson => TriggerTypeKey;

    public override IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement)
    {
        var cronExpressionString = jsonElement.GetProperty("CronExpressionString").GetString()!;
        var timeZone = jsonElement.GetProperty("TimeZone").GetTimeZone();

        return CronScheduleBuilder.CronSchedule(cronExpressionString)
            .InTimeZone(timeZone);
    }

    protected override void SerializeFields(Utf8JsonWriter writer, ICronTrigger trigger)
    {
        writer.WriteString("CronExpressionString", trigger.CronExpressionString);
        writer.WriteTimeZoneInfo("TimeZone", trigger.TimeZone);
    }
}