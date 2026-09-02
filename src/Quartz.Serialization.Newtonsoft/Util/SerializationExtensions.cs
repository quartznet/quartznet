using System.Globalization;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Quartz.Util;

internal static class Utf8JsonWriterExtensions
{
    /// <summary>
    /// Writes a time of day as the historical <c>{ Hour, Minute, Second }</c> object, which is the
    /// shape already sitting in every persisted daily time interval trigger.
    /// </summary>
    public static void WriteTimeOfDay(this JsonWriter writer, string propertyName, TimeOnly value)
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

    /// <summary>
    /// Reads the <c>{ Hour, Minute, Second }</c> object written by every version of Quartz.NET.
    /// </summary>
    public static TimeOnly GetTimeOfDay(this JObject source)
    {
        var hour = source.Value<int>("Hour");
        var minute = source.Value<int>("Minute");
        var second = source.Value<int>("Second");

        return new TimeOnly(hour, minute, second);
    }

    public static void WriteDateOnlyArray(this JsonWriter writer, string propertyName, IEnumerable<DateOnly> values)
    {
        WriteArray(writer, propertyName, values, static (w, v) => w.WriteValue(v.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// Reads an array of dates, accepting both the date-only form written from 4.0 on and the
    /// full timestamps written by earlier versions.
    /// </summary>
    public static DateOnly[] GetDateOnlyArray(this JToken token)
    {
        List<DateOnly> result = [];
        foreach (JToken item in token)
        {
            // Newtonsoft turns an ISO timestamp into a date token before we get here, and this
            // serializer's DateParseHandling makes that a DateTimeOffset.
            object? raw = (item as JValue)?.Value;
            result.Add(raw switch
            {
                DateOnly date => date,
                DateTime dateTime => DateOnly.FromDateTime(dateTime),
                DateTimeOffset dateTimeOffset => DateOnly.FromDateTime(dateTimeOffset.DateTime),
                _ => ParseDateOnly(Convert.ToString(raw, CultureInfo.InvariantCulture)!)
            });
        }

        return result.ToArray();
    }

    private static DateOnly ParseDateOnly(string value)
    {
        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
            ? date
            : DateOnly.FromDateTime(DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    /// <summary>
    /// Reads an array of days of the month, accepting both the day numbers written from 4.0 on and
    /// the flag-per-day boolean array, indexed by day minus one, written by earlier versions.
    /// </summary>
    public static int[] GetDayOfMonthArray(this JToken token)
    {
        List<int> days = [];
        int index = 0;
        foreach (JToken item in token)
        {
            if (item.Type == JTokenType.Boolean)
            {
                if (item.Value<bool>())
                {
                    days.Add(index + 1);
                }
            }
            else
            {
                days.Add(item.Value<int>());
            }

            index++;
        }

        return days.ToArray();
    }

    /// <summary>
    /// Reads an array of week days, accepting both the day names written from 4.0 on and the
    /// flag-per-day boolean array written by earlier versions.
    /// </summary>
    public static DayOfWeek[] GetDayOfWeekArray(this JToken token)
    {
        List<DayOfWeek> days = [];
        int index = 0;
        foreach (JToken item in token)
        {
            switch (item.Type)
            {
                case JTokenType.Boolean:
                    if (item.Value<bool>())
                    {
                        days.Add((DayOfWeek) index);
                    }
                    break;
                case JTokenType.Integer:
                    days.Add((DayOfWeek) item.Value<int>());
                    break;
                default:
                    days.Add(Enum.Parse<DayOfWeek>(item.Value<string>()!, ignoreCase: true));
                    break;
            }

            index++;
        }

        return days.ToArray();
    }

    public static void WriteTimeOnly(this JsonWriter writer, string propertyName, TimeOnly value)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteValue(value.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Reads a <see cref="Quartz.Impl.Calendar.DailyCalendar" />'s range, accepting both the
    /// <c>RangeStart</c>/<c>RangeEnd</c> pair written from 4.0 on and the
    /// <c>RangeStartingTime</c>/<c>RangeEndingTime</c> <c>HH:MM[:SS[:mmm]]</c> strings written by
    /// earlier versions.
    /// </summary>
    public static (TimeOnly Start, TimeOnly End) GetDailyCalendarRange(this JObject source)
    {
        var start = source["RangeStart"];
        var end = source["RangeEnd"];

        if (start is not null && end is not null)
        {
            return (TimeOnly.Parse(start.Value<string>()!, CultureInfo.InvariantCulture),
                TimeOnly.Parse(end.Value<string>()!, CultureInfo.InvariantCulture));
        }

        return (ParseLegacyDailyCalendarTime(source["RangeStartingTime"]!.Value<string>()!),
            ParseLegacyDailyCalendarTime(source["RangeEndingTime"]!.Value<string>()!));
    }

    /// <summary>
    /// Parses the <c>HH:MM[:SS[:mmm]]</c> form a <see cref="Quartz.Impl.Calendar.DailyCalendar" />
    /// used to be written with - note the colon before the milliseconds.
    /// </summary>
    private static TimeOnly ParseLegacyDailyCalendarTime(string value)
    {
        string[] parts = value.Split(':');
        if (parts.Length < 2 || parts.Length > 4)
        {
            // Quartz's exception, deliberately - this file is in Quartz.Util, so the unqualified name
            // would bind to Quartz's type regardless of the "using Newtonsoft.Json" above.
            throw new Quartz.JsonSerializationException($"Invalid time string '{value}'");
        }

        int hour = int.Parse(parts[0], CultureInfo.InvariantCulture);
        int minute = int.Parse(parts[1], CultureInfo.InvariantCulture);
        int second = parts.Length > 2 ? int.Parse(parts[2], CultureInfo.InvariantCulture) : 0;
        int millisecond = parts.Length > 3 ? int.Parse(parts[3], CultureInfo.InvariantCulture) : 0;

        return new TimeOnly(hour, minute, second, millisecond);
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
}