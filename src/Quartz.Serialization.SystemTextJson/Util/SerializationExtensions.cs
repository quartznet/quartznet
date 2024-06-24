using System.Globalization;
using System.Text.Json;

namespace Quartz.Util;

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

    public static void WriteTimeOfDay(this Utf8JsonWriter writer, string propertyName, TimeOfDay value)
    {
        writer.WriteStartObject(propertyName);

        writer.WriteNumber("Hour", value.Hour);
        writer.WriteNumber("Minute", value.Minute);
        writer.WriteNumber("Second", value.Second);

        writer.WriteEndObject();
    }

    public static TimeOfDay GetTimeOfDay(this JsonElement jsonElement)
    {
        var hour = jsonElement.GetProperty("Hour").GetInt32();
        var minute = jsonElement.GetProperty("Minute").GetInt32();
        var second = jsonElement.GetProperty("Second").GetInt32();

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

    public static void WriteKey<T>(this Utf8JsonWriter writer, string propertyName, Key<T>? key)
    {
        if (key == null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteStartObject(propertyName);
        writer.WriteString("Name", key.Name);
        writer.WriteString("Group", key.Group);
        writer.WriteEndObject();
    }

    public static TriggerKey GetTriggerKey(this JsonElement jsonElement)
    {
        var name = jsonElement.GetProperty("Name").GetString();
        var group = jsonElement.GetProperty("Group").GetString();

        return new TriggerKey(name!, group!);
    }

    public static JobKey? GetJobKey(this JsonElement jsonElement)
    {
        if (jsonElement.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var name = jsonElement.GetProperty("Name").GetString();
        var group = jsonElement.GetProperty("Group").GetString();

        return new JobKey(name!, group!);
    }

    public static void WriteJobDataMap(this Utf8JsonWriter writer, string propertyName, JobDataMap jobDataMap)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteJobDataMapValue(jobDataMap);
    }

    public static void WriteJobDataMapValue(this Utf8JsonWriter writer, JobDataMap jobDataMap)
    {
        if (jobDataMap.Values.Any(static x => x is not string))
        {
            throw new NotSupportedException("Only string values are supported in JobDataMap");
        }

        writer.WriteStartObject();

        foreach (KeyValuePair<string, object?> keyValuePair in jobDataMap)
        {
            writer.WriteString(keyValuePair.Key, (string?) keyValuePair.Value);
        }

        writer.WriteEndObject();
    }

    public static JobDataMap GetJobDataMap(this JsonElement jsonElement)
    {
        var result = new JobDataMap();

        foreach (var property in jsonElement.EnumerateObject())
        {
            var value = property.Value.GetString();
            result.Add(property.Name, value!);
        }

        return result;
    }
}