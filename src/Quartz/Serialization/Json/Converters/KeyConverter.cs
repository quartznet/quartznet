using System.Text.Json;
using System.Text.Json.Serialization;

using Quartz.Util;

namespace Quartz.Serialization.Json.Converters;

internal sealed class JobKeyConverter : KeyConverter<JobKey>
{
    protected override JobKey Create(string name, string group)
    {
        return new JobKey(name, group);
    }
}

internal sealed class TriggerKeyConverter : KeyConverter<TriggerKey>
{
    protected override TriggerKey Create(string name, string group)
    {
        return new TriggerKey(name, group);
    }
}

internal abstract class KeyConverter<T> : JsonConverter<T> where T : Key<T>
{
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteString("$type", value.GetType().AssemblyQualifiedNameWithoutVersion());
        writer.WriteString(options.GetPropertyName("Name"), value.Name);
        writer.WriteString(options.GetPropertyName("Group"), value.Group);

        writer.WriteEndObject();
    }

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var rootElement = JsonDocument.ParseValue(ref reader).RootElement;

        var name = rootElement.GetProperty(options.GetPropertyName("Name")).GetString()!;
        var group = rootElement.GetProperty(options.GetPropertyName("Group")).GetString()!;

        return Create(name, group);
    }

    protected abstract T Create(string name, string group);
}