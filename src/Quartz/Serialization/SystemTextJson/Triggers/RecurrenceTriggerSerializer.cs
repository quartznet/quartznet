using System.Text.Json;

using Quartz.Impl.Triggers;

namespace Quartz.Serialization.SystemTextJson.Triggers;

public class RecurrenceTriggerSerializer : TriggerSerializer<RecurrenceTriggerImpl>
{
    public override string TriggerTypeName => "RecurrenceTrigger";

    public override IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement, JsonSerializerOptions options)
    {
        var recurrenceRule = jsonElement.GetProperty(options.GetPropertyName("RecurrenceRule")).GetString()!;
        var timeZone = jsonElement.GetProperty(options.GetPropertyName("TimeZone")).GetTimeZone();

        return RecurrenceScheduleBuilder.Create(recurrenceRule)
            .InTimeZone(timeZone);
    }

    protected override void SerializeFields(Utf8JsonWriter writer, RecurrenceTriggerImpl trigger, JsonSerializerOptions options)
    {
        writer.WriteString(options.GetPropertyName("RecurrenceRule"), trigger.RecurrenceRule);
        writer.WriteTimeZoneInfo(options.GetPropertyName("TimeZone"), trigger.TimeZone);
        writer.WriteNumber(options.GetPropertyName("TimesTriggered"), trigger.TimesTriggered);
    }

    protected override void DeserializeFields(RecurrenceTriggerImpl trigger, JsonElement jsonElement, JsonSerializerOptions options)
    {
        var timesTriggered = jsonElement.GetPropertyOrNull(options.GetPropertyName("TimesTriggered"))?.GetInt32();
        trigger.TimesTriggered = timesTriggered ?? 0;
    }
}
