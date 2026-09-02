using System.Text.Json;

using Quartz.Impl.Triggers;

namespace Quartz.Serialization.SystemTextJson.Triggers;

/// <summary>
/// How a <see cref="IRecurrenceTrigger" /> is written to and read from the store's JSON.
/// </summary>
/// <remarks>
/// Public and unsealed on purpose: a trigger deriving from the built-in implementation pairs with a
/// serializer deriving from this one, overriding <c>SerializeFields</c> and <c>DeserializeFields</c>
/// and calling base so the built-in fields keep their stored shape.
/// </remarks>
public class RecurrenceTriggerSerializer : TriggerSerializer<RecurrenceTriggerImpl>
{
    /// <inheritdoc />
    public override string TriggerTypeName => "RecurrenceTrigger";

    /// <inheritdoc />
    public override IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement, JsonSerializerOptions options)
    {
        var recurrenceRule = jsonElement.GetProperty(options.GetPropertyName("RecurrenceRule")).GetString()!;
        var timeZone = jsonElement.GetProperty(options.GetPropertyName("TimeZone")).GetTimeZone();

        return RecurrenceScheduleBuilder.Create(recurrenceRule)
            .InTimeZone(timeZone);
    }

    /// <inheritdoc />
    protected override void SerializeFields(Utf8JsonWriter writer, RecurrenceTriggerImpl trigger, JsonSerializerOptions options)
    {
        writer.WriteString(options.GetPropertyName("RecurrenceRule"), trigger.RecurrenceRule);
        writer.WriteTimeZoneInfo(options.GetPropertyName("TimeZone"), trigger.TimeZone);
        writer.WriteNumber(options.GetPropertyName("TimesTriggered"), trigger.TimesTriggered);
    }

    /// <inheritdoc />
    protected override void DeserializeFields(RecurrenceTriggerImpl trigger, JsonElement jsonElement, JsonSerializerOptions options)
    {
        var timesTriggered = jsonElement.GetPropertyOrNull(options.GetPropertyName("TimesTriggered"))?.GetInt32();
        trigger.TimesTriggered = timesTriggered ?? 0;
    }
}
