using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Impl.Triggers;

namespace Quartz.Serialization.Newtonsoft.Triggers;

public class RecurrenceTriggerSerializer : TriggerSerializer<RecurrenceTriggerImpl>
{
    public override string TriggerTypeName => "RecurrenceTrigger";

    public override IScheduleBuilder CreateScheduleBuilder(JObject source)
    {
        var recurrenceRule = source.Value<string>("RecurrenceRule")!;
        var timeZone = TimeZoneUtil.FindTimeZoneById(source.Value<string>("TimeZone")!);

        return RecurrenceScheduleBuilder.Create(recurrenceRule)
            .InTimeZone(timeZone);
    }

    protected override void SerializeFields(JsonWriter writer, RecurrenceTriggerImpl trigger)
    {
        writer.WritePropertyName("RecurrenceRule");
        writer.WriteValue(trigger.RecurrenceRule);

        writer.WritePropertyName("TimeZone");
        writer.WriteValue(trigger.TimeZone.Id);

        writer.WritePropertyName("TimesTriggered");
        writer.WriteValue(trigger.TimesTriggered);
    }

    protected override void DeserializeFields(RecurrenceTriggerImpl trigger, JObject source)
    {
        var timesTriggered = source.Value<int?>("TimesTriggered");
        trigger.TimesTriggered = timesTriggered ?? 0;
    }
}
