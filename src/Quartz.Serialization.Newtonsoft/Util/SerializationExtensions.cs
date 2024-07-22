using System.Collections;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Quartz.Util;

internal static class Utf8JsonWriterExtensions
{
    public static void WriteTimeOfDay(this JsonWriter writer, string propertyName, TimeOfDay value)
    {
        writer.WritePropertyName(propertyName);

        writer.WriteStartObject();

        writer.WritePropertyName("Hour");
        writer.WriteValue(value.Hour);

        writer.WritePropertyName("Minute");
        writer.WriteValue(value.Minute);

        writer.WritePropertyName("Second");
        writer.WriteValue(value.Second);

        writer.WriteEndObject();
    }

    public static TimeOfDay GetTimeOfDay(this JObject source)
    {
        var hour = source.Value<int>("Hour");
        var minute = source.Value<int>("Minute");
        var second = source.Value<int>("Second");

        return new TimeOfDay(hour, minute, second);
    }

    public static void WriteArray<T>(this JsonWriter writer, string propertyName, IEnumerable<T> values, Action<JsonWriter, T> valueWriter)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartArray();
        foreach (var value in values)
        {
            valueWriter(writer, value);
        }

        writer.WriteEndArray();
    }

    public static void WriteKey<T>(this JsonWriter writer, string propertyName, Key<T>? key)
    {
        writer.WritePropertyName(propertyName);

        if (key == null)
        {
            writer.WriteNull();
            return;
        }

        writer.WriteStartObject();

        writer.WritePropertyName("Name");
        writer.WriteValue(key.Name);

        writer.WritePropertyName("Group");
        writer.WriteValue(key.Group);

        writer.WriteEndObject();
    }

    public static TriggerKey GetTriggerKey(this JObject jsonElement, string propertyName)
    {
        var key = jsonElement.Value<JObject>(propertyName)!;
        var name = key.Value<string>("Name");
        var group = key.Value<string>("Group");

        return new TriggerKey(name!, group!);
    }

    public static JobKey? GetJobKey(this JObject jsonElement, string propertyName)
    {
        var key = jsonElement.Value<JObject?>(propertyName);

        if (key == null)
        {
            return null;
        }

        var name = key.Value<string>("Name");
        var group = key.Value<string>("Group");

        return new JobKey(name!, group!);
    }

    public static void WriteJobDataMapValue<T>(this JsonWriter writer, T jobDataMap) where T : IDictionary
    {
        writer.WriteStartObject();

        foreach (object? key in jobDataMap.Keys)
        {
            writer.WritePropertyName(key.ToString()!);
            var value = jobDataMap[key];
            writer.WriteValue(value);
        }

        writer.WriteEndObject();
    }

    public static JobDataMap? GetJobDataMap(this JObject? jsonElement)
    {
        if (jsonElement == null)
        {
            return null;
        }

        var properties = jsonElement.ToObject<IDictionary>()!;

        var result = new JobDataMap();
        foreach (string key in properties.Keys)
        {
            result.Add(key, properties[key]);
        }

        result.ClearDirtyFlag();
        return result;
    }
}