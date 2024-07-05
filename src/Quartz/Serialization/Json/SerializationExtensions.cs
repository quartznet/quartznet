using System.Collections;
using System.Globalization;
using System.Text.Json;

using Quartz.Util;

namespace Quartz.Serialization.Json;

internal static class Utf8JsonWriterExtensions
{
    public static void WriteString(this Utf8JsonWriter writer, string propertyName, DateTimeOffset? value)
    {
        if (value.HasValue)
        {
            writer.WriteString(propertyName, value.Value);
        }
        else
        {
            writer.WriteNull(propertyName);
        }
    }

    public static DateTimeOffset? GetDateTimeOffsetOrNull(this JsonElement jsonElement)
    {
        if (jsonElement.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return jsonElement.GetDateTimeOffset();
    }

    public static void WriteString(this Utf8JsonWriter writer, string propertyName, TimeSpan value)
    {
        writer.WriteString(propertyName, value.ToString("c"));
    }

    public static TimeSpan GetTimeSpan(this JsonElement jsonElement)
    {
        var value = jsonElement.GetString() ?? "";
        var result = TimeSpan.ParseExact(value, "c", CultureInfo.InvariantCulture);
        return result;
    }

    public static void WriteTimeZoneInfo(this Utf8JsonWriter writer, string propertyName, TimeZoneInfo value)
    {
        writer.WriteString(propertyName, value.Id);
    }

    public static TimeZoneInfo GetTimeZone(this JsonElement jsonElement)
    {
        var timeZoneId = jsonElement.GetString();
        return TimeZoneUtil.FindTimeZoneById(timeZoneId!);
    }

    public static void WriteEnum<T>(this Utf8JsonWriter writer, string propertyName, T value) where T : Enum
    {
        writer.WritePropertyName(propertyName);
        writer.WriteEnumValue(value);
    }

    public static void WriteEnumValue<T>(this Utf8JsonWriter writer, T value) where T : Enum
    {
        writer.WriteStringValue(value.ToString());
    }

    public static T GetEnum<T>(this JsonElement jsonElement) where T : Enum
    {
        var value = jsonElement.GetString() ?? "";
        var result = Enum.Parse(typeof(T), value, ignoreCase: true);
        return (T) result;
    }

    public static void WriteTimeOfDay(this Utf8JsonWriter writer, string propertyName, TimeOfDay value, JsonSerializerOptions options)
    {
        writer.WriteStartObject(propertyName);

        writer.WriteNumber(options.GetPropertyName("Hour"), value.Hour);
        writer.WriteNumber(options.GetPropertyName("Minute"), value.Minute);
        writer.WriteNumber(options.GetPropertyName("Second"), value.Second);

        writer.WriteEndObject();
    }

    public static TimeOfDay GetTimeOfDay(this JsonElement jsonElement, JsonSerializerOptions options)
    {
        var hour = jsonElement.GetProperty(options.GetPropertyName("Hour")).GetInt32();
        var minute = jsonElement.GetProperty(options.GetPropertyName("Minute")).GetInt32();
        var second = jsonElement.GetProperty(options.GetPropertyName("Second")).GetInt32();

        return new TimeOfDay(hour, minute, second);
    }

    public static void WriteDateTimeArray(this Utf8JsonWriter writer, string propertyName, IEnumerable<DateTime> values)
    {
        WriteArray(writer, propertyName, values, (w, v) => w.WriteStringValue(v));
    }

    public static DateTime[] GetDateTimeArray(this JsonElement jsonElement)
    {
        var result = jsonElement.GetArray(x => x.GetDateTime());
        return result;
    }

    public static void WriteBooleanArray(this Utf8JsonWriter writer, string propertyName, IEnumerable<bool> values)
    {
        WriteArray(writer, propertyName, values, (w, v) => w.WriteBooleanValue(v));
    }

    public static bool[] GetBooleanArray(this JsonElement jsonElement)
    {
        var result = jsonElement.GetArray(x => x.GetBoolean());
        return result;
    }

    public static void WriteArray<T>(this Utf8JsonWriter writer, string propertyName, IEnumerable<T> values, Action<Utf8JsonWriter, T> valueWriter)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartArray();
        foreach (var value in values)
        {
            valueWriter(writer, value);
        }

        writer.WriteEndArray();
    }

    public static T[] GetArray<T>(this JsonElement jsonElement, Func<JsonElement, T> valueGetter)
    {
        var result = jsonElement
            .EnumerateArray()
            .Select(valueGetter)
            .ToArray();

        return result;
    }

    public static void WriteKey<T>(this Utf8JsonWriter writer, string propertyName, Key<T>? key, JsonSerializerOptions options)
    {
        if (key is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteStartObject(propertyName);
        writer.WriteString(options.GetPropertyName("Name"), key.Name);
        writer.WriteString(options.GetPropertyName("Group"), key.Group);
        writer.WriteEndObject();
    }

    public static TriggerKey GetTriggerKey(this JsonElement jsonElement, JsonSerializerOptions options)
    {
        var name = jsonElement.GetProperty(options.GetPropertyName("Name")).GetString();
        var group = jsonElement.GetProperty(options.GetPropertyName("Group")).GetString();

        return new TriggerKey(name!, group!);
    }

    public static JobKey? GetJobKey(this JsonElement jsonElement, JsonSerializerOptions options)
    {
        if (jsonElement.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var name = jsonElement.GetProperty(options.GetPropertyName("Name")).GetString();
        var group = jsonElement.GetProperty(options.GetPropertyName("Group")).GetString();

        return new JobKey(name!, group!);
    }

    public static void WriteJobDataMapValue<T>(this Utf8JsonWriter writer, T jobDataMap, JsonSerializerOptions options) where T : IDictionary
    {
        writer.WriteStartObject();

        foreach (object? key in jobDataMap.Keys)
        {
            writer.WritePropertyName(key.ToString()!);
            JsonSerializer.Serialize(writer, jobDataMap[key], options);
        }

        writer.WriteEndObject();
    }

    public static JobDataMap GetJobDataMap(this JsonElement jsonElement, JsonSerializerOptions options)
    {
        var result = new JobDataMap();

        foreach (JsonProperty property in jsonElement.EnumerateObject())
        {
            object? value;
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.String:
                    value = property.Value.GetString();
                    break;
                case JsonValueKind.True:
                    value = true;
                    break;
                case JsonValueKind.False:
                    value = false;
                    break;
                case JsonValueKind.Null:
                    value = null;
                    break;
                case JsonValueKind.Number:
                    if (property.Value.TryGetInt32(out int intValue))
                    {
                        value = intValue;
                    }
                    else if (property.Value.TryGetInt64(out long longValue))
                    {
                        value = longValue;
                    }
                    else
                    {
                        value = property.Value.GetDouble();
                    }
                    break;
                case JsonValueKind.Object:
                    value = property.Value.Deserialize<Dictionary<string, string>>(options);
                    break;
                default:
                    throw new JsonException($"Unsupported value kind: {property.Value.ValueKind}");
            }

            result.Add(property.Name, value);
        }

        result.ClearDirtyFlag();
        return result;
    }

    internal static string GetPropertyName(this JsonSerializerOptions options, string propertyName)
    {
        return options.PropertyNamingPolicy?.ConvertName(propertyName) ?? propertyName;
    }

    public static JsonElement? GetPropertyOrNull(this JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var result) ? result : null;
    }
}