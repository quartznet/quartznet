using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Impl.Triggers;

namespace Quartz.Serialization.Newtonsoft.Triggers;

/// <summary>
/// Stores an <see cref="ICalendarIntervalTrigger" /> as its interval, unit, time zone and the two
/// daylight-saving choices.
/// </summary>
/// <remarks>
/// <inheritdoc cref="SimpleTriggerSerializer" path="/remarks" />
/// </remarks>
public class CalendarIntervalTriggerSerializer : TriggerSerializer<CalendarIntervalTriggerImpl>
{
    /// <inheritdoc />
    public override string TriggerTypeName => "CalendarIntervalTrigger";

    /// <inheritdoc />
    public override IScheduleBuilder CreateScheduleBuilder(JObject source)
    {
        var repeatIntervalUnit = source["RepeatIntervalUnit"]!.ToObject<IntervalUnit>();
        var repeatInterval = source.Value<int>("RepeatInterval");
        var timeZone = TimeZones.FindById(source.Value<string>("TimeZone")!);
        var preserveHourOfDayAcrossDaylightSavings = source.Value<bool>("PreserveHourOfDayAcrossDaylightSavings");
        var skipDayIfHourDoesNotExist = source.Value<bool>("SkipDayIfHourDoesNotExist");

        return CalendarIntervalScheduleBuilder.Create()
            .WithInterval(repeatInterval, repeatIntervalUnit)
            .InTimeZone(timeZone)
            .PreserveHourOfDayAcrossDaylightSavings(preserveHourOfDayAcrossDaylightSavings)
            .SkipDayIfHourDoesNotExist(skipDayIfHourDoesNotExist);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    protected override void DeserializeFields(CalendarIntervalTriggerImpl trigger, JObject source)
    {
        // This properties might not exist in the JSON if trigger was serialized with older version
        var timesTriggered = source.Value<int?>("TimesTriggered");
        trigger.TimesTriggered = timesTriggered ?? 0;
    }
}