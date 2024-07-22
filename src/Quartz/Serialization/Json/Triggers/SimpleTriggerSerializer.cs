using System.Text.Json;

namespace Quartz.Serialization.Json.Triggers;

public class SimpleTriggerSerializer : TriggerSerializer<ISimpleTrigger>
{
    public override string TriggerTypeForJson => "SimpleTrigger";

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
        writer.WriteNumber(options.GetPropertyName("TimesTriggered"), trigger.TimesTriggered);
    }

    protected override void DeserializeFields(ISimpleTrigger trigger, JsonElement jsonElement, JsonSerializerOptions options)
    {
        // This property might not exist in the JSON if trigger was serialized with older version
        var timesTriggered = jsonElement.GetPropertyOrNull(options.GetPropertyName("TimesTriggered"))?.GetInt32();
        trigger.TimesTriggered = timesTriggered ?? 0;
    }
}