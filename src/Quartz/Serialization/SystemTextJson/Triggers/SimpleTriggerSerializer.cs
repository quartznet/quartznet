using System.Text.Json;

using Quartz.Impl.Triggers;

namespace Quartz.Serialization.SystemTextJson.Triggers;

/// <summary>
/// How a <see cref="ISimpleTrigger" /> is written to and read from the store's JSON.
/// </summary>
/// <remarks>
/// Public and unsealed on purpose: a trigger deriving from the built-in implementation pairs with a
/// serializer deriving from this one, overriding <c>SerializeFields</c> and <c>DeserializeFields</c>
/// and calling base so the built-in fields keep their stored shape.
/// </remarks>
public class SimpleTriggerSerializer : TriggerSerializer<SimpleTriggerImpl>
{
    /// <inheritdoc />
    public override string TriggerTypeName => "SimpleTrigger";

    /// <inheritdoc />
    public override IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement, JsonSerializerOptions options)
    {
        var repeatInterval = jsonElement.GetProperty(options.GetPropertyName("RepeatIntervalTimeSpan")).GetTimeSpan();
        var repeatCount = jsonElement.GetProperty(options.GetPropertyName("RepeatCount")).GetInt32();

        return SimpleScheduleBuilder.Create()
            .WithInterval(repeatInterval)
            .WithRepeatCount(repeatCount);
    }

    /// <inheritdoc />
    protected override void SerializeFields(Utf8JsonWriter writer, SimpleTriggerImpl trigger, JsonSerializerOptions options)
    {
        writer.WriteNumber(options.GetPropertyName("RepeatCount"), trigger.RepeatCount);
        writer.WriteString(options.GetPropertyName("RepeatIntervalTimeSpan"), trigger.RepeatInterval);
        writer.WriteNumber(options.GetPropertyName("TimesTriggered"), trigger.TimesTriggered);
    }

    /// <inheritdoc />
    protected override void DeserializeFields(SimpleTriggerImpl trigger, JsonElement jsonElement, JsonSerializerOptions options)
    {
        // This property might not exist in the JSON if trigger was serialized with older version
        var timesTriggered = jsonElement.GetPropertyOrNull(options.GetPropertyName("TimesTriggered"))?.GetInt32();
        trigger.TimesTriggered = timesTriggered ?? 0;
    }
}