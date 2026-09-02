using System.Text.Json;

using Quartz.Impl.Triggers;

namespace Quartz.Serialization.SystemTextJson.Triggers;

/// <summary>
/// How a <see cref="ICalendarIntervalTrigger" /> is written to and read from the store's JSON.
/// </summary>
/// <remarks>
/// Public and unsealed on purpose: a trigger deriving from the built-in implementation pairs with a
/// serializer deriving from this one, overriding <c>SerializeFields</c> and <c>DeserializeFields</c>
/// and calling base so the built-in fields keep their stored shape.
/// </remarks>
public class CalendarIntervalTriggerSerializer : TriggerSerializer<CalendarIntervalTriggerImpl>
{
    /// <inheritdoc />
    public override string TriggerTypeName => "CalendarIntervalTrigger";

    /// <inheritdoc />
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

    /// <inheritdoc />
    protected override void SerializeFields(Utf8JsonWriter writer, CalendarIntervalTriggerImpl trigger, JsonSerializerOptions options)
    {
        writer.WriteNumber(options.GetPropertyName("RepeatInterval"), trigger.RepeatInterval);
        writer.WriteEnum(options.GetPropertyName("RepeatIntervalUnit"), trigger.RepeatIntervalUnit);
        writer.WriteTimeZoneInfo(options.GetPropertyName("TimeZone"), trigger.TimeZone);
        writer.WriteBoolean(options.GetPropertyName("PreserveHourOfDayAcrossDaylightSavings"), trigger.PreserveHourOfDayAcrossDaylightSavings);
        writer.WriteBoolean(options.GetPropertyName("SkipDayIfHourDoesNotExist"), trigger.SkipDayIfHourDoesNotExist);
        writer.WriteNumber(options.GetPropertyName("TimesTriggered"), trigger.TimesTriggered);
    }

    /// <inheritdoc />
    protected override void DeserializeFields(CalendarIntervalTriggerImpl trigger, JsonElement jsonElement, JsonSerializerOptions options)
    {
        // This property might not exist in the JSON if trigger was serialized with older version
        var timesTriggered = jsonElement.GetPropertyOrNull(options.GetPropertyName("TimesTriggered"))?.GetInt32();
        trigger.TimesTriggered = timesTriggered ?? 0;
    }
}