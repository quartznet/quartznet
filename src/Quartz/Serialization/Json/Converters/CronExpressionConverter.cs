using System.Text.Json;
using System.Text.Json.Serialization;

using Quartz.Util;

namespace Quartz.Serialization.Json.Converters;

internal sealed class CronExpressionConverter : JsonConverter<CronExpression>
{
    public override void Write(Utf8JsonWriter writer, CronExpression value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteString("$type", value.GetType().AssemblyQualifiedNameWithoutVersion());
        writer.WriteString(options.GetPropertyName("CronExpression"), value.CronExpressionString);
        writer.WriteString(options.GetPropertyName("TimeZoneId"), value.TimeZone?.Id);

        writer.WriteEndObject();
    }

    public override CronExpression Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var rootElement = JsonDocument.ParseValue(ref reader).RootElement;

        var cronExpressionString = rootElement.GetProperty(options.GetPropertyName("CronExpression")).GetString()!;

        var cronExpression = new CronExpression(cronExpressionString);
        string? timeZoneId = rootElement.GetProperty(options.GetPropertyName("TimeZoneId")).GetString();
        if (!string.IsNullOrEmpty(timeZoneId))
        {
            cronExpression.TimeZone = TimeZoneUtil.FindTimeZoneById(timeZoneId!);
        }
        return cronExpression;
    }
}