using System.Text.Json;

using Quartz.Util;

namespace Quartz.Serialization.Json.Triggers;

internal sealed class SimpleTriggerSerializer : TriggerSerializer<ISimpleTrigger>
{
    public static SimpleTriggerSerializer Instance { get; } = new();

    private SimpleTriggerSerializer()
    {
    }

    public const string TriggerTypeKey = "SimpleTrigger";

    public override string TriggerTypeForJson => TriggerTypeKey;

    public override IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement, JsonSerializerOptions options)
    {
        var repeatInterval = jsonElement.GetProperty(options.GetPropertyName("RepeatIntervalTimeSpan")).GetTimeSpan();
        var repeatCount = jsonElement.GetProperty(options.GetPropertyName("RepeatCount")).GetInt32();

        return SimpleScheduleBuilder.Create()
            .WithInterval(repeatInterval)
            .WithRepeatCount(repeatCount);
    }

    protected override void SerializeFields(Utf8JsonWriter writer, ISimpleTrigger trigger, JsonSerializerOptions options)
    {
        writer.WriteNumber(options.GetPropertyName("RepeatCount"), trigger.RepeatCount);
        writer.WriteString(options.GetPropertyName("RepeatIntervalTimeSpan"), trigger.RepeatInterval);
    }
}