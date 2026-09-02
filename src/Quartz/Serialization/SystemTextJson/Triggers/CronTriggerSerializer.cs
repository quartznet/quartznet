using System.Text.Json;

namespace Quartz.Serialization.SystemTextJson.Triggers;

/// <summary>
/// How a <see cref="ICronTrigger" /> is written to and read from the store's JSON.
/// </summary>
/// <remarks>
/// Public and unsealed on purpose: a trigger deriving from the built-in implementation pairs with a
/// serializer deriving from this one, overriding <c>SerializeFields</c> and <c>DeserializeFields</c>
/// and calling base so the built-in fields keep their stored shape.
/// </remarks>
public class CronTriggerSerializer : TriggerSerializer<ICronTrigger>
{
    /// <inheritdoc />
    public override string TriggerTypeName => "CronTrigger";

    /// <inheritdoc />
    public override IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement, JsonSerializerOptions options)
    {
        var cronExpressionString = jsonElement.GetProperty(options.GetPropertyName("CronExpressionString")).GetString()!;
        var timeZone = jsonElement.GetProperty(options.GetPropertyName("TimeZone")).GetTimeZone();

        return CronScheduleBuilder.Create(cronExpressionString)
            .InTimeZone(timeZone);
    }

    /// <inheritdoc />
    protected override void SerializeFields(Utf8JsonWriter writer, ICronTrigger trigger, JsonSerializerOptions options)
    {
        writer.WriteString(options.GetPropertyName("CronExpressionString"), trigger.CronExpressionString);
        writer.WriteTimeZoneInfo(options.GetPropertyName("TimeZone"), trigger.TimeZone);
    }
}