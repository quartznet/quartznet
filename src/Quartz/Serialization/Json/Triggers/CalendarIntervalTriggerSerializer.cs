using System.Text.Json;

using Quartz.Util;

namespace Quartz.Serialization.Json.Triggers;

internal sealed class CalendarIntervalTriggerSerializer : TriggerSerializer<ICalendarIntervalTrigger>
{
    public static CalendarIntervalTriggerSerializer Instance { get; } = new();

    private CalendarIntervalTriggerSerializer()
    {
    }

    public const string TriggerTypeKey = "CalendarIntervalTrigger";

    public override string TriggerTypeForJson => TriggerTypeKey;

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
    }
}