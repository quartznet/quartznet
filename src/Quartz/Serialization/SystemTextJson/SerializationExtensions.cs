using System.Globalization;
using System.Text.Json;

namespace Quartz.Serialization.SystemTextJson;

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

    /// <summary>
    /// Writes a time of day as the historical <c>{ Hour, Minute, Second }</c> object, which is the
    /// shape already sitting in every persisted daily time interval trigger.
    /// </summary>
    public static void WriteTimeOfDay(this Utf8JsonWriter writer, string propertyName, TimeOnly value, JsonSerializerOptions options)
    {
        writer.WriteStartObject(propertyName);

        writer.WriteNumber(options.GetPropertyName("Hour"), value.Hour);
        writer.WriteNumber(options.GetPropertyName("Minute"), value.Minute);
        writer.WriteNumber(options.GetPropertyName("Second"), value.Second);

        writer.WriteEndObject();
    }

    /// <summary>
    /// Reads the <c>{ Hour, Minute, Second }</c> object written by every version of Quartz.NET.
    /// </summary>
    public static TimeOnly GetTimeOfDay(this JsonElement jsonElement, JsonSerializerOptions options)
    {
        var hour = jsonElement.GetProperty(options.GetPropertyName("Hour")).GetInt32();
        var minute = jsonElement.GetProperty(options.GetPropertyName("Minute")).GetInt32();
        var second = jsonElement.GetProperty(options.GetPropertyName("Second")).GetInt32();

        return new TimeOnly(hour, minute, second);
    }

    public static void WriteDateOnlyArray(this Utf8JsonWriter writer, string propertyName, IEnumerable<DateOnly> values)
    {
        WriteArray(writer, propertyName, values, static (w, v) => w.WriteStringValue(v.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// Reads an array of dates, accepting both the date-only form written from 4.0 on and the
    /// full timestamps written by earlier versions.
    /// </summary>
    public static DateOnly[] GetDateOnlyArray(this JsonElement jsonElement)
    {
        return jsonElement.GetArray(static x => ParseDateOnly(x.GetString()!));
    }

    private static DateOnly ParseDateOnly(string value)
    {
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
        {
            return date;
        }

        // Pre-4.0 payloads carry a full timestamp; only its date part ever mattered.
        return DateOnly.FromDateTime(DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    /// <summary>
    /// Reads an array of days of the month, accepting both the day numbers written from 4.0 on and
    /// the flag-per-day boolean array, indexed by day minus one, written by earlier versions.
    /// </summary>
    public static int[] GetDayOfMonthArray(this JsonElement jsonElement)
    {
        List<int> days = [];
        int index = 0;
        foreach (JsonElement item in jsonElement.EnumerateArray())
        {
            switch (item.ValueKind)
            {
                case JsonValueKind.True:
                    days.Add(index + 1);
                    break;
                case JsonValueKind.False:
                    break;
                default:
                    days.Add(item.GetInt32());
                    break;
            }

            index++;
        }

        return days.ToArray();
    }

    /// <summary>
    /// Reads an array of week days, accepting both the day names written from 4.0 on and the
    /// flag-per-day boolean array written by earlier versions.
    /// </summary>
    public static DayOfWeek[] GetDayOfWeekArray(this JsonElement jsonElement)
    {
        List<DayOfWeek> days = [];
        int index = 0;
        foreach (JsonElement item in jsonElement.EnumerateArray())
        {
            switch (item.ValueKind)
            {
                case JsonValueKind.True:
                    days.Add((DayOfWeek) index);
                    break;
                case JsonValueKind.False:
                    break;
                case JsonValueKind.Number:
                    days.Add((DayOfWeek) item.GetInt32());
                    break;
                default:
                    days.Add(item.GetEnum<DayOfWeek>());
                    break;
            }

            index++;
        }

        return days.ToArray();
    }

    public static void WriteTimeOnly(this Utf8JsonWriter writer, string propertyName, TimeOnly value)
    {
        writer.WriteString(propertyName, value.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Reads a <see cref="Quartz.Impl.Calendar.DailyCalendar" />'s range, accepting both the
    /// <c>RangeStart</c>/<c>RangeEnd</c> pair written from 4.0 on and the
    /// <c>RangeStartingTime</c>/<c>RangeEndingTime</c> <c>HH:MM[:SS[:mmm]]</c> strings written by
    /// earlier versions.
    /// </summary>
    public static (TimeOnly Start, TimeOnly End) GetDailyCalendarRange(this JsonElement jsonElement, JsonSerializerOptions options)
    {
        JsonElement? start = jsonElement.GetPropertyOrNull(options.GetPropertyName("RangeStart"));
        JsonElement? end = jsonElement.GetPropertyOrNull(options.GetPropertyName("RangeEnd"));

        if (start is not null && end is not null)
        {
            return (TimeOnly.Parse(start.Value.GetString()!, CultureInfo.InvariantCulture),
                TimeOnly.Parse(end.Value.GetString()!, CultureInfo.InvariantCulture));
        }

        string legacyStart = jsonElement.GetProperty(options.GetPropertyName("RangeStartingTime")).GetString()!;
        string legacyEnd = jsonElement.GetProperty(options.GetPropertyName("RangeEndingTime")).GetString()!;
        return (ParseLegacyDailyCalendarTime(legacyStart), ParseLegacyDailyCalendarTime(legacyEnd));
    }

    /// <summary>
    /// Parses the <c>HH:MM[:SS[:mmm]]</c> form a <see cref="Quartz.Impl.Calendar.DailyCalendar" />
    /// used to be written with - note the colon before the milliseconds.
    /// </summary>
    internal static TimeOnly ParseLegacyDailyCalendarTime(string value)
    {
        string[] parts = value.Split(':');
        if (parts.Length < 2 || parts.Length > 4)
        {
            throw new JsonException($"Invalid time string '{value}'");
        }

        int hour = int.Parse(parts[0], CultureInfo.InvariantCulture);
        int minute = int.Parse(parts[1], CultureInfo.InvariantCulture);
        int second = parts.Length > 2 ? int.Parse(parts[2], CultureInfo.InvariantCulture) : 0;
        int millisecond = parts.Length > 3 ? int.Parse(parts[3], CultureInfo.InvariantCulture) : 0;

        return new TimeOnly(hour, minute, second, millisecond);
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

    public static void WriteJobDataMapValue(this Utf8JsonWriter writer, JobDataMap jobDataMap, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var pair in jobDataMap)
        {
            writer.WritePropertyName(pair.Key);
            JsonSerializer.Serialize(writer, pair.Value, options);
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