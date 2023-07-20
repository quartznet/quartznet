using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Util;

namespace Quartz.Converters;

internal sealed class CronExpressionConverter : JsonConverter
{
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        var cronExpression = (CronExpression) value!;
        writer.WriteStartObject();

        writer.WritePropertyName("$type");
        writer.WriteValue(value!.GetType().AssemblyQualifiedNameWithoutVersion());

        writer.WritePropertyName("CronExpression");
        writer.WriteValue(cronExpression.CronExpressionString);

        writer.WritePropertyName("TimeZoneId");
        writer.WriteValue(cronExpression.TimeZone?.Id);

        writer.WriteEndObject();
    }

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        JObject jObject = JObject.Load(reader);
        var cronExpressionString = jObject["CronExpression"]!.Value<string>()!;

        var cronExpression = new CronExpression(cronExpressionString);
        cronExpression.TimeZone = TimeZoneUtil.FindTimeZoneById(jObject["TimeZoneId"]!.Value<string>()!);
        return cronExpression;
    }

    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(CronExpression);
    }
}