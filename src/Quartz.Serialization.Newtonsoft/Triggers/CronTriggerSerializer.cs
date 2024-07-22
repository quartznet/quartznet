using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Util;

namespace Quartz.Triggers;

public class CronTriggerSerializer : TriggerSerializer<ICronTrigger>
{
    public override string TriggerTypeForJson => "CronTrigger";

    public override IScheduleBuilder CreateScheduleBuilder(JObject source)
    {
        var cronExpressionString = source.Value<string>("CronExpressionString")!;
        var timeZone = TimeZoneUtil.FindTimeZoneById(source.Value<string>("TimeZone")!);

        return CronScheduleBuilder.CronSchedule(cronExpressionString)
            .InTimeZone(timeZone);
    }

    protected override void SerializeFields(JsonWriter writer, ICronTrigger trigger)
    {
        writer.WritePropertyName("CronExpressionString");
        writer.WriteValue(trigger.CronExpressionString);

        writer.WritePropertyName("TimeZone");
        writer.WriteValue(trigger.TimeZone.Id);
    }
}