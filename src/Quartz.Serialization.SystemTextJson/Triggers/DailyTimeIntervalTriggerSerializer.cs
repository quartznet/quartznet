using System.Text.Json;

using Quartz.Util;

namespace Quartz.Triggers;

internal class DailyTimeIntervalTriggerSerializer : TriggerSerializer<IDailyTimeIntervalTrigger>
{
    public static DailyTimeIntervalTriggerSerializer Instance { get; } = new();

    private DailyTimeIntervalTriggerSerializer()
    {
    }

    public const string TriggerTypeKey = "DailyTimeIntervalTrigger";

    public override string TriggerTypeForJson => TriggerTypeKey;

    public override IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement)
    {
        var repeatCount = jsonElement.GetProperty("RepeatCount").GetInt32();
        var repeatIntervalUnit = jsonElement.GetProperty("RepeatIntervalUnit").GetEnum<IntervalUnit>();
        var repeatInterval = jsonElement.GetProperty("RepeatInterval").GetInt32();
        var startTimeOfDay = jsonElement.GetProperty("StartTimeOfDay").GetTimeOfDay();
        var endTimeOfDay = jsonElement.GetProperty("EndTimeOfDay").GetTimeOfDay();
        var daysOfWeek = jsonElement.GetProperty("DaysOfWeek").GetArray(x => x.GetEnum<DayOfWeek>());
        var timeZone = jsonElement.GetProperty("TimeZone").GetTimeZone();

        return DailyTimeIntervalScheduleBuilder.Create()
            .WithRepeatCount(repeatCount)
            .WithInterval(repeatInterval, repeatIntervalUnit)
            .StartingDailyAt(startTimeOfDay)
            .EndingDailyAt(endTimeOfDay)
            .OnDaysOfTheWeek(daysOfWeek)
            .InTimeZone(timeZone);
    }

    protected override void SerializeFields(Utf8JsonWriter writer, IDailyTimeIntervalTrigger trigger)
    {
        writer.WriteNumber("RepeatCount", trigger.RepeatCount);
        writer.WriteNumber("RepeatInterval", trigger.RepeatInterval);
        writer.WriteEnum("RepeatIntervalUnit", trigger.RepeatIntervalUnit);
        writer.WriteTimeOfDay("StartTimeOfDay", trigger.StartTimeOfDay);
        writer.WriteTimeOfDay("EndTimeOfDay", trigger.EndTimeOfDay);
        writer.WriteArray("DaysOfWeek", trigger.DaysOfWeek, (w, v) => w.WriteEnumValue(v));
        writer.WriteTimeZoneInfo("TimeZone", trigger.TimeZone);
    }
}