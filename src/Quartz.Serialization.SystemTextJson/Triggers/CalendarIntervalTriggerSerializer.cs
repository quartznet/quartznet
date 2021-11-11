using System.Text.Json;

using Quartz.Util;

namespace Quartz.Triggers;

internal class CalendarIntervalTriggerSerializer : TriggerSerializer<ICalendarIntervalTrigger>
{
    public const string TriggerTypeKey = "CalendarIntervalTrigger";

    public override string TriggerTypeForJson => TriggerTypeKey;

    public override IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement)
    {
        var repeatIntervalUnit = jsonElement.GetProperty("RepeatIntervalUnit").GetEnum<IntervalUnit>();
        var repeatInterval = jsonElement.GetProperty("RepeatInterval").GetInt32();
        var timeZone = jsonElement.GetProperty("TimeZone").GetTimeZone();
        var preserveHourOfDayAcrossDaylightSavings = jsonElement.GetProperty("PreserveHourOfDayAcrossDaylightSavings").GetBoolean();
        var skipDayIfHourDoesNotExist = jsonElement.GetProperty("SkipDayIfHourDoesNotExist").GetBoolean();

        return CalendarIntervalScheduleBuilder.Create()
            .WithInterval(repeatInterval, repeatIntervalUnit)
            .InTimeZone(timeZone)
            .PreserveHourOfDayAcrossDaylightSavings(preserveHourOfDayAcrossDaylightSavings)
            .SkipDayIfHourDoesNotExist(skipDayIfHourDoesNotExist);
    }

    protected override void SerializeFields(Utf8JsonWriter writer, ICalendarIntervalTrigger trigger)
    {
        writer.WriteNumber("RepeatInterval", trigger.RepeatInterval);
        writer.WriteEnum("RepeatIntervalUnit", trigger.RepeatIntervalUnit);
        writer.WriteTimeZoneInfo("TimeZone", trigger.TimeZone);
        writer.WriteBoolean("PreserveHourOfDayAcrossDaylightSavings", trigger.PreserveHourOfDayAcrossDaylightSavings);
        writer.WriteBoolean("SkipDayIfHourDoesNotExist", trigger.SkipDayIfHourDoesNotExist);
    }
}