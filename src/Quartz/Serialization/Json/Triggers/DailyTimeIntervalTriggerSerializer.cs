using System.Text.Json;

namespace Quartz.Serialization.Json.Triggers;

public class DailyTimeIntervalTriggerSerializer : TriggerSerializer<IDailyTimeIntervalTrigger>
{
    public override string TriggerTypeForJson => "DailyTimeIntervalTrigger";

    public override IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement, JsonSerializerOptions options)
    {
        var repeatCount = jsonElement.GetProperty(options.GetPropertyName("RepeatCount")).GetInt32();
        var repeatIntervalUnit = jsonElement.GetProperty(options.GetPropertyName("RepeatIntervalUnit")).GetEnum<IntervalUnit>();
        var repeatInterval = jsonElement.GetProperty(options.GetPropertyName("RepeatInterval")).GetInt32();
        var startTimeOfDay = jsonElement.GetProperty(options.GetPropertyName("StartTimeOfDay")).GetTimeOfDay(options);
        var endTimeOfDay = jsonElement.GetProperty(options.GetPropertyName("EndTimeOfDay")).GetTimeOfDay(options);
        var daysOfWeek = jsonElement.GetProperty(options.GetPropertyName("DaysOfWeek")).GetArray(x => x.GetEnum<DayOfWeek>());
        var timeZone = jsonElement.GetProperty(options.GetPropertyName("TimeZone")).GetTimeZone();

        return DailyTimeIntervalScheduleBuilder.Create()
            .WithRepeatCount(repeatCount)
            .WithInterval(repeatInterval, repeatIntervalUnit)
            .StartingDailyAt(startTimeOfDay)
            .EndingDailyAt(endTimeOfDay)
            .OnDaysOfTheWeek(daysOfWeek)
            .InTimeZone(timeZone);
    }

    protected override void SerializeFields(Utf8JsonWriter writer, IDailyTimeIntervalTrigger trigger, JsonSerializerOptions options)
    {
        writer.WriteNumber(options.GetPropertyName("RepeatCount"), trigger.RepeatCount);
        writer.WriteNumber(options.GetPropertyName("RepeatInterval"), trigger.RepeatInterval);
        writer.WriteEnum(options.GetPropertyName("RepeatIntervalUnit"), trigger.RepeatIntervalUnit);
        writer.WriteTimeOfDay(options.GetPropertyName("StartTimeOfDay"), trigger.StartTimeOfDay, options);
        writer.WriteTimeOfDay(options.GetPropertyName("EndTimeOfDay"), trigger.EndTimeOfDay, options);
        writer.WriteArray(options.GetPropertyName("DaysOfWeek"), trigger.DaysOfWeek, (w, v) => w.WriteEnumValue(v));
        writer.WriteTimeZoneInfo(options.GetPropertyName("TimeZone"), trigger.TimeZone);
        writer.WriteNumber(options.GetPropertyName("TimesTriggered"), trigger.TimesTriggered);
    }

    protected override void DeserializeFields(IDailyTimeIntervalTrigger trigger, JsonElement jsonElement, JsonSerializerOptions options)
    {
        // This property might not exist in the JSON if trigger was serialized with older version
        var timesTriggered = jsonElement.GetPropertyOrNull(options.GetPropertyName("TimesTriggered"))?.GetInt32();
        trigger.TimesTriggered = timesTriggered ?? 0;
    }
}