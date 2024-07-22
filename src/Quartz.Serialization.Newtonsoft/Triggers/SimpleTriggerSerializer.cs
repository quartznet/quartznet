using System.Globalization;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Quartz.Triggers;

public class SimpleTriggerSerializer : TriggerSerializer<ISimpleTrigger>
{
    public override string TriggerTypeForJson => "SimpleTrigger";

    public override IScheduleBuilder CreateScheduleBuilder(JObject source)
    {
        var repeatInterval = TimeSpan.ParseExact(source.Value<string>("RepeatIntervalTimeSpan")!, "c", CultureInfo.InvariantCulture);
        var repeatCount = source.Value<int>("RepeatCount");

        return SimpleScheduleBuilder.Create()
            .WithInterval(repeatInterval)
            .WithRepeatCount(repeatCount);
    }

    protected override void SerializeFields(JsonWriter writer, ISimpleTrigger trigger)
    {
        writer.WritePropertyName("RepeatCount");
        writer.WriteValue(trigger.RepeatCount);

        writer.WritePropertyName("RepeatIntervalTimeSpan");
        writer.WriteValue(trigger.RepeatInterval);

        writer.WritePropertyName("TimesTriggered");
        writer.WriteValue(trigger.TimesTriggered);
    }

    protected override void DeserializeFields(ISimpleTrigger trigger, JObject source)
    {
        // This properties might not exist in the JSON if trigger was serialized with older version
        var timesTriggered = source.Value<int?>("TimesTriggered");
        trigger.TimesTriggered = timesTriggered ?? 0;
    }
}