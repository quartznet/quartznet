using System.Text.Json;

using Quartz.Impl.Triggers;

namespace Quartz.Serialization.SystemTextJson.Triggers;

/// <summary>
/// How a <see cref="IDailyTimeIntervalTrigger" /> is written to and read from the store's JSON.
/// </summary>
/// <remarks>
/// Public and unsealed on purpose: a trigger deriving from the built-in implementation pairs with a
/// serializer deriving from this one, overriding <c>SerializeFields</c> and <c>DeserializeFields</c>
/// and calling base so the built-in fields keep their stored shape.
/// </remarks>
public class DailyTimeIntervalTriggerSerializer : TriggerSerializer<DailyTimeIntervalTriggerImpl>
{
    /// <inheritdoc />
    public override string TriggerTypeName => "DailyTimeIntervalTrigger";

    /// <inheritdoc />
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

    /// <inheritdoc />
    protected override void SerializeFields(Utf8JsonWriter writer, DailyTimeIntervalTriggerImpl trigger, JsonSerializerOptions options)
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

    /// <inheritdoc />
    protected override void DeserializeFields(DailyTimeIntervalTriggerImpl trigger, JsonElement jsonElement, JsonSerializerOptions options)
    {
        // This property might not exist in the JSON if trigger was serialized with older version
        var timesTriggered = jsonElement.GetPropertyOrNull(options.GetPropertyName("TimesTriggered"))?.GetInt32();
        trigger.TimesTriggered = timesTriggered ?? 0;
    }
}