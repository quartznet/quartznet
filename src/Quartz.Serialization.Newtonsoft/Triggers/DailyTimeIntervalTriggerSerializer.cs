using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Util;

namespace Quartz.Triggers;

public class DailyTimeIntervalTriggerSerializer : TriggerSerializer<IDailyTimeIntervalTrigger>
{
    public override string TriggerTypeForJson => "DailyTimeIntervalTrigger";

    public override IScheduleBuilder CreateScheduleBuilder(JObject source)
    {
        var repeatCount = source.Value<int>("RepeatCount");
        var repeatIntervalUnit = source["RepeatIntervalUnit"]!.ToObject<IntervalUnit>();
        var repeatInterval = source.Value<int>("RepeatInterval");
        var startTimeOfDay = source.Value<JObject>("StartTimeOfDay")!.GetTimeOfDay();
        var endTimeOfDay = source.Value<JObject>("EndTimeOfDay")!.GetTimeOfDay();
        var daysOfWeek = source.Value<JArray>("DaysOfWeek")!.Select(x => (DayOfWeek) Enum.Parse(typeof(DayOfWeek), x.Value<string>()!)).ToArray();
        var timeZone = TimeZoneUtil.FindTimeZoneById(source.Value<string>("TimeZone")!);

        return DailyTimeIntervalScheduleBuilder.Create()
            .WithRepeatCount(repeatCount)
            .WithInterval(repeatInterval, repeatIntervalUnit)
            .StartingDailyAt(startTimeOfDay)
            .EndingDailyAt(endTimeOfDay)
            .OnDaysOfTheWeek(daysOfWeek)
            .InTimeZone(timeZone);
    }

    protected override void SerializeFields(JsonWriter writer, IDailyTimeIntervalTrigger trigger)
    {
        writer.WritePropertyName("RepeatCount");
        writer.WriteValue(trigger.RepeatCount);

        writer.WritePropertyName("RepeatInterval");
        writer.WriteValue(trigger.RepeatInterval);

        writer.WritePropertyName("RepeatIntervalUnit");
        writer.WriteValue(trigger.RepeatIntervalUnit.ToString());

        writer.WriteTimeOfDay("StartTimeOfDay", trigger.StartTimeOfDay);
        writer.WriteTimeOfDay("EndTimeOfDay", trigger.EndTimeOfDay);
        writer.WriteArray("DaysOfWeek", trigger.DaysOfWeek, (w, v) => w.WriteValue(v.ToString()));

        writer.WritePropertyName("TimeZone");
        writer.WriteValue(trigger.TimeZone.Id);

        writer.WritePropertyName("TimesTriggered");
        writer.WriteValue(trigger.TimesTriggered);
    }

    protected override void DeserializeFields(IDailyTimeIntervalTrigger trigger, JObject source)
    {
        // This properties might not exist in the JSON if trigger was serialized with older version
        var timesTriggered = source.Value<int?>("TimesTriggered");
        trigger.TimesTriggered = timesTriggered ?? 0;
    }
}