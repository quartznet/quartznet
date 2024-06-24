using System.Text.Json;
using System.Text.Json.Serialization;

using Quartz.Util;

namespace Quartz.Converters;

internal sealed class KeyConverter<T> : JsonConverter<T> where T : Key<T>
{
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteString("$type", value.GetType().AssemblyQualifiedNameWithoutVersion());
        writer.WriteString("Name", value.Name);
        writer.WriteString("Group", value.Group);

        writer.WriteEndObject();
    }

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var rootElement = JsonDocument.ParseValue(ref reader).RootElement;

        var name = rootElement.GetProperty("CronExpression").GetString()!;
        var group = rootElement.GetProperty("Group").GetString()!;

        return (T) Activator.CreateInstance(typeToConvert)!;
    }
}