using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Impl.Triggers;

namespace Quartz.Serialization.Newtonsoft.Triggers;

/// <summary>
/// Stores an <see cref="IRecurrenceTrigger" /> as its RFC 5545 recurrence rule and time zone.
/// </summary>
/// <remarks>
/// <inheritdoc cref="SimpleTriggerSerializer" path="/remarks" />
/// </remarks>
public class RecurrenceTriggerSerializer : TriggerSerializer<RecurrenceTriggerImpl>
{
    /// <inheritdoc />
    public override string TriggerTypeName => "RecurrenceTrigger";

    /// <inheritdoc />
    public override IScheduleBuilder CreateScheduleBuilder(JObject source)
    {
        var recurrenceRule = source.Value<string>("RecurrenceRule")!;
        var timeZone = TimeZones.FindById(source.Value<string>("TimeZone")!);

        return RecurrenceScheduleBuilder.Create(recurrenceRule)
            .InTimeZone(timeZone);
    }

    /// <inheritdoc />
    protected override void SerializeFields(JsonWriter writer, RecurrenceTriggerImpl trigger)
    {
        writer.WritePropertyName("RecurrenceRule");
        writer.WriteValue(trigger.RecurrenceRule);

        writer.WritePropertyName("TimeZone");
        writer.WriteValue(trigger.TimeZone.Id);

        writer.WritePropertyName("TimesTriggered");
        writer.WriteValue(trigger.TimesTriggered);
    }

    /// <inheritdoc />
    protected override void DeserializeFields(RecurrenceTriggerImpl trigger, JObject source)
    {
        var timesTriggered = source.Value<int?>("TimesTriggered");
        trigger.TimesTriggered = timesTriggered ?? 0;
    }
}
