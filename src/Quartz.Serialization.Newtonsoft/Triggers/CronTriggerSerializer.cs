using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Quartz.Serialization.Newtonsoft.Triggers;

/// <summary>
/// Stores an <see cref="ICronTrigger" /> as its cron expression and time zone.
/// </summary>
/// <remarks>
/// <inheritdoc cref="SimpleTriggerSerializer" path="/remarks" />
/// </remarks>
public class CronTriggerSerializer : TriggerSerializer<ICronTrigger>
{
    /// <inheritdoc />
    public override string TriggerTypeName => "CronTrigger";

    /// <inheritdoc />
    public override IScheduleBuilder CreateScheduleBuilder(JObject source)
    {
        var cronExpressionString = source.Value<string>("CronExpressionString")!;
        var timeZone = TimeZones.FindById(source.Value<string>("TimeZone")!);

        return CronScheduleBuilder.Create(cronExpressionString)
            .InTimeZone(timeZone);
    }

    /// <inheritdoc />
    protected override void SerializeFields(JsonWriter writer, ICronTrigger trigger)
    {
        writer.WritePropertyName("CronExpressionString");
        writer.WriteValue(trigger.CronExpressionString);

        writer.WritePropertyName("TimeZone");
        writer.WriteValue(trigger.TimeZone.Id);
    }
}