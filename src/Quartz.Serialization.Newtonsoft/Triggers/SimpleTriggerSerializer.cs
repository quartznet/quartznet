using System.Globalization;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Impl.Triggers;

namespace Quartz.Serialization.Newtonsoft.Triggers;

/// <summary>
/// Stores an <see cref="ISimpleTrigger" /> as its repeat count and interval.
/// </summary>
/// <remarks>
/// Public and unsealed on purpose: a trigger deriving from <see cref="SimpleTriggerImpl" /> pairs with
/// a serializer deriving from this one, calling base from
/// <see cref="TriggerSerializer{TTrigger}.SerializeFields" /> so the built-in fields keep their stored
/// shape.
/// </remarks>
public class SimpleTriggerSerializer : TriggerSerializer<SimpleTriggerImpl>
{
    /// <inheritdoc />
    public override string TriggerTypeName => "SimpleTrigger";

    /// <inheritdoc />
    public override IScheduleBuilder CreateScheduleBuilder(JObject source)
    {
        var repeatInterval = TimeSpan.ParseExact(source.Value<string>("RepeatIntervalTimeSpan")!, "c", CultureInfo.InvariantCulture);
        var repeatCount = source.Value<int>("RepeatCount");

        return SimpleScheduleBuilder.Create()
            .WithInterval(repeatInterval)
            .WithRepeatCount(repeatCount);
    }

    /// <inheritdoc />
    protected override void SerializeFields(JsonWriter writer, SimpleTriggerImpl trigger)
    {
        writer.WritePropertyName("RepeatCount");
        writer.WriteValue(trigger.RepeatCount);

        writer.WritePropertyName("RepeatIntervalTimeSpan");
        writer.WriteValue(trigger.RepeatInterval);

        writer.WritePropertyName("TimesTriggered");
        writer.WriteValue(trigger.TimesTriggered);
    }

    /// <inheritdoc />
    protected override void DeserializeFields(SimpleTriggerImpl trigger, JObject source)
    {
        // This properties might not exist in the JSON if trigger was serialized with older version
        var timesTriggered = source.Value<int?>("TimesTriggered");
        trigger.TimesTriggered = timesTriggered ?? 0;
    }
}