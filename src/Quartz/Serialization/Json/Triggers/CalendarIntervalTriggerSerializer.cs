using System.Text.Json;

namespace Quartz.Serialization.Json.Triggers;

public class CalendarIntervalTriggerSerializer : TriggerSerializer<ICalendarIntervalTrigger>
{
    public override string TriggerTypeForJson => "CalendarIntervalTrigger";

    public override IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement, JsonSerializerOptions options)
    {
        var repeatIntervalUnit = jsonElement.GetProperty(options.GetPropertyName("RepeatIntervalUnit")).GetEnum<IntervalUnit>();
        var repeatInterval = jsonElement.GetProperty(options.GetPropertyName("RepeatInterval")).GetInt32();
        var timeZone = jsonElement.GetProperty(options.GetPropertyName("TimeZone")).GetTimeZone();
        var preserveHourOfDayAcrossDaylightSavings = jsonElement.GetProperty(options.GetPropertyName("PreserveHourOfDayAcrossDaylightSavings")).GetBoolean();
        var skipDayIfHourDoesNotExist = jsonElement.GetProperty(options.GetPropertyName("SkipDayIfHourDoesNotExist")).GetBoolean();

        return CalendarIntervalScheduleBuilder.Create()
            .WithInterval(repeatInterval, repeatIntervalUnit)
            .InTimeZone(timeZone)
            .PreserveHourOfDayAcrossDaylightSavings(preserveHourOfDayAcrossDaylightSavings)
            .SkipDayIfHourDoesNotExist(skipDayIfHourDoesNotExist);
    }

    protected override void SerializeFields(Utf8JsonWriter writer, ICalendarIntervalTrigger trigger, JsonSerializerOptions options)
    {
        writer.WriteNumber(options.GetPropertyName("RepeatInterval"), trigger.RepeatInterval);
        writer.WriteEnum(options.GetPropertyName("RepeatIntervalUnit"), trigger.RepeatIntervalUnit);
        writer.WriteTimeZoneInfo(options.GetPropertyName("TimeZone"), trigger.TimeZone);
        writer.WriteBoolean(options.GetPropertyName("PreserveHourOfDayAcrossDaylightSavings"), trigger.PreserveHourOfDayAcrossDaylightSavings);
        writer.WriteBoolean(options.GetPropertyName("SkipDayIfHourDoesNotExist"), trigger.SkipDayIfHourDoesNotExist);
        writer.WriteNumber(options.GetPropertyName("TimesTriggered"), trigger.TimesTriggered);
    }

    protected override void DeserializeFields(ICalendarIntervalTrigger trigger, JsonElement jsonElement, JsonSerializerOptions options)
    {
        // This property might not exist in the JSON if trigger was serialized with older version
        var timesTriggered = jsonElement.GetPropertyOrNull(options.GetPropertyName("TimesTriggered"))?.GetInt32();
        trigger.TimesTriggered = timesTriggered ?? 0;
    }
}