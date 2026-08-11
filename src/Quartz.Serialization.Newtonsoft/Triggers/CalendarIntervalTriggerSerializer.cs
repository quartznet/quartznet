using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Impl.Triggers;

namespace Quartz.Serialization.Newtonsoft.Triggers;

public class CalendarIntervalTriggerSerializer : TriggerSerializer<CalendarIntervalTriggerImpl>
{
    public override string TriggerTypeName => "CalendarIntervalTrigger";

    public override IScheduleBuilder CreateScheduleBuilder(JObject source)
    {
        var repeatIntervalUnit = source["RepeatIntervalUnit"]!.ToObject<IntervalUnit>();
        var repeatInterval = source.Value<int>("RepeatInterval");
        var timeZone = TimeZoneUtil.FindTimeZoneById(source.Value<string>("TimeZone")!);
        var preserveHourOfDayAcrossDaylightSavings = source.Value<bool>("PreserveHourOfDayAcrossDaylightSavings");
        var skipDayIfHourDoesNotExist = source.Value<bool>("SkipDayIfHourDoesNotExist");

        return CalendarIntervalScheduleBuilder.Create()
            .WithInterval(repeatInterval, repeatIntervalUnit)
            .InTimeZone(timeZone)
            .PreserveHourOfDayAcrossDaylightSavings(preserveHourOfDayAcrossDaylightSavings)
            .SkipDayIfHourDoesNotExist(skipDayIfHourDoesNotExist);
    }

    protected override void SerializeFields(JsonWriter writer, CalendarIntervalTriggerImpl trigger)
    {
        writer.WritePropertyName("RepeatInterval");
        writer.WriteValue(trigger.RepeatInterval);

        writer.WritePropertyName("RepeatIntervalUnit");
        writer.WriteValue(trigger.RepeatIntervalUnit.ToString());

        writer.WritePropertyName("TimeZone");
        writer.WriteValue(trigger.TimeZone.Id);

        writer.WritePropertyName("PreserveHourOfDayAcrossDaylightSavings");
        writer.WriteValue(trigger.PreserveHourOfDayAcrossDaylightSavings);

        writer.WritePropertyName("SkipDayIfHourDoesNotExist");
        writer.WriteValue(trigger.SkipDayIfHourDoesNotExist);

        writer.WritePropertyName("TimesTriggered");
        writer.WriteValue(trigger.TimesTriggered);
    }

    protected override void DeserializeFields(CalendarIntervalTriggerImpl trigger, JObject source)
    {
        // This properties might not exist in the JSON if trigger was serialized with older version
        var timesTriggered = source.Value<int?>("TimesTriggered");
        trigger.TimesTriggered = timesTriggered ?? 0;
    }
}