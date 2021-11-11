using System.Text.Json;

using Quartz.Util;

namespace Quartz.Triggers;

internal class SimpleTriggerSerializer : TriggerSerializer<ISimpleTrigger>
{
    public const string TriggerTypeKey = "SimpleTrigger";

    public override string TriggerTypeForJson => TriggerTypeKey;

    public override IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement)
    {
        var repeatInterval = jsonElement.GetProperty("RepeatIntervalTimeSpan").GetTimeSpan();
        var repeatCount = jsonElement.GetProperty("RepeatCount").GetInt32();

        return SimpleScheduleBuilder.Create()
            .WithInterval(repeatInterval)
            .WithRepeatCount(repeatCount);
    }

    protected override void SerializeFields(Utf8JsonWriter writer, ISimpleTrigger trigger)
    {
        writer.WriteNumber("RepeatCount", trigger.RepeatCount);
        writer.WriteString("RepeatIntervalTimeSpan", trigger.RepeatInterval);
    }
}